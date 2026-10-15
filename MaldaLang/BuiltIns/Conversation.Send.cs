// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLang.Runtime.Tracing;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class ConversationInstance
{
    public RuntimeValue Send(RuntimeValue? responseFormat = null, LlmRequestOverrides? overrides = null)
    {
        using var activityTurn = ActivityTurnScope.Enter();
        EnsureWithinThinkDeadline();
        TrimContextIfOverBudget();
        if (_client == null && _llamaClient == null && _bridgeClient == null)
        {
            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String("Error: LLM client not initialized"));
            return RuntimeValue.Object(errorObj);
        }

        // Build tools array (optional per-request filter from PromptInstance.tools)
        var toolsArray = new List<RuntimeValue>();
        foreach (var tool in _tools.Values)
        {
            if (overrides?.ToolNames != null && overrides.ToolNames.Count > 0 && !overrides.ToolNames.Contains(tool.Name))
                continue;
            toolsArray.Add(tool.GetSchema());
        }

        var tools = toolsArray.Count > 0 ? RuntimeValue.Array(toolsArray) : null;
        RuntimeValue response;
        
        LogLlmRequest();
        
        // Record LLM request in trace (if enabled)
        try
        {
            var modelName =
                _client != null ? _client.Model :
                _llamaClient != null ? _llamaClient.ModelPath :
                null;
            
            // Create a lightweight summary of the current messages for tracing
            var messageSummaries = new List<object>();
            foreach (var msgVal in _messages)
            {
                if (msgVal.Type != ValueType.Object)
                    continue;
                
                var msgObj = msgVal.AsObject();
                var role = GetStringProperty(msgObj, "role") ?? "user";
                var msgContent = GetStringProperty(msgObj, "content");
                messageSummaries.Add(new
                {
                    role,
                    content = msgContent
                });
            }
            
            // Basic tool summaries (name + working directory)
            var toolSummaries = new List<object>();
            foreach (var tool in _tools.Values)
            {
                toolSummaries.Add(new
                {
                    name = tool.Name,
                    workingDirectory = tool.WorkingDirectory
                });
            }
            
            TraceManager.Record(
                TraceEventType.LlmRequest,
                new
                {
                    model = modelName,
                    systemPrompt = _systemPrompt,
                    messages = messageSummaries,
                    tools = toolSummaries
                },
                AgentName,
                SessionId);
        }
        catch
        {
            // Tracing must never interfere with normal execution
        }
        
        if (_bridgeClient != null)
        {
            response = _bridgeClient.Chat(RuntimeValue.Array(_messages), tools, responseFormat, overrides);
        }
        else if (_llamaClient != null)
        {
            response = _llamaClient.Chat(RuntimeValue.Array(_messages), tools, responseFormat, overrides);
        }
        else
        {
            response = ChatWithOptionalStreaming(() =>
                _client!.Chat(RuntimeValue.Array(_messages), tools, responseFormat, overrides));
        }

        MaldaLang.Runtime.Journal.RunJournal.Current.Append(new MaldaLang.Runtime.Journal.JournalEvent
        {
            Kind = MaldaLang.Runtime.Journal.JournalKind.Prompt,
            Name = "conversation.send",
            Model = _client?.Model ?? _llamaClient?.ModelPath,
            Ok = response.Type == ValueType.Object
        });
        
        // Ensure we always have an object to work with
        if (response.Type != ValueType.Object)
        {
            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String("Error: Unable to get response from LLM"));
            return RuntimeValue.Object(errorObj);
        }
        
        var responseObj = response.AsObject();
        if (responseObj is JsonObject jsonResponse)
        {
            LogLlmResponse(jsonResponse);
            AccumulateTurnUsage(jsonResponse);
            RecordBudgetUsageAfterLlmResponse(jsonResponse);
            
            // Record LLM response in trace (if enabled)
            try
            {
                var modelName =
                    _client != null ? _client.Model :
                    _llamaClient != null ? _llamaClient.ModelPath :
                    null;
                
                // Collect tool call summaries as we parse them (if any)
                var toolCallSummaries = new List<object>();
                
                var toolCallsForTrace = jsonResponse.Get("tool_calls", null);
                if (toolCallsForTrace != null && toolCallsForTrace.Type == ValueType.Array)
                {
                    foreach (var tc in toolCallsForTrace.AsArray())
                    {
                        if (tc.Type != ValueType.Object)
                            continue;
                        
                        var tcObj = tc.AsObject();
                        var tcId = GetStringProperty(tcObj, "id");
                        var funcVal = GetProperty(tcObj, "function");
                        string? toolNameForTrace = null;
                        string? argumentsJsonForTrace = null;
                        if (funcVal != null && funcVal.Type == ValueType.Object)
                        {
                            var funcObj = funcVal.AsObject();
                            toolNameForTrace = GetStringProperty(funcObj, "name");
                            argumentsJsonForTrace = GetStringProperty(funcObj, "arguments");
                        }
                        
                        toolCallSummaries.Add(new
                        {
                            id = tcId,
                            name = toolNameForTrace,
                            argumentsJson = argumentsJsonForTrace
                        });
                    }
                }
                
                var contentForTrace = GetStringProperty(jsonResponse, "content");
                
                TraceManager.Record(
                    TraceEventType.LlmResponse,
                    new
                    {
                        model = modelName,
                        content = contentForTrace,
                        toolCalls = toolCallSummaries.Count > 0 ? toolCallSummaries : null,
                        rawResponse = (object?)null,
                        error = (object?)null
                    },
                    AgentName,
                    SessionId);
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            // Check for tool_calls
            var toolCalls = jsonResponse.Get("tool_calls", null);
            if (toolCalls != null && toolCalls.Type == ValueType.Array)
            {
                // Filter tool calls to only include those with valid IDs
                var validToolCalls = new List<RuntimeValue>();
                var toolCallsToExecute = new List<RuntimeValue>();
                
                foreach (var tc in toolCalls.AsArray())
                {
                    if (tc.Type == ValueType.Object)
                    {
                        var tcObj = tc.AsObject();
                        var toolCallId = GetStringProperty(tcObj, "id");
                        
                        // Only include tool calls with valid IDs
                        if (!string.IsNullOrEmpty(toolCallId))
                        {
                            validToolCalls.Add(tc);
                            toolCallsToExecute.Add(tc);
                        }
                    }
                }
                
                // Only add assistant message if we have valid tool calls
                if (validToolCalls.Count > 0)
                {
                    _messages.Add(RuntimeValue.Object(
                        BuildAssistantToolCallHistoryMessage(jsonResponse, validToolCalls)));
                }

                var toolNames = ExtractToolCallNames(toolCalls);
                if (toolNames.Count > 0)
                {
                    EmitAgentProgress(
                        "tool_calls",
                        message: "Running tools: " + string.Join(", ", toolNames),
                        tools: toolNames);
                }
                
                // Execute tool calls (parallel batches for read-only tools when enabled)
                foreach (var outcome in ExecuteToolCalls(toolCallsToExecute))
                {
                    AppendToolResultMessage(outcome);
                    if (!string.IsNullOrEmpty(outcome.ToolName))
                    {
                        EmitAgentProgress(
                            "tool_done",
                            message: (outcome.Succeeded ? "Done: " : "Failed: ") + outcome.ToolName,
                            tool: outcome.ToolName,
                            ok: outcome.Succeeded);
                    }
                }
                
                EnsureVerboseLoggingSetup();
                if (_verboseLoggingEnabled)
                {
                    var plain = $"[llm] round {_llmRound}: tool results sent, continuing...";
                    WriteVerboseLine(
                        plain,
                        IsAgentRichCli() ? $"[dim][llm][/] round {_llmRound} · tool results sent, continuing..." : null);
                }

                var maxLlmRounds = ResolveMaxLlmRounds();
                if (maxLlmRounds > 0 && _llmRound >= maxLlmRounds)
                {
                    var stopContent =
                        $"Stopped after {maxLlmRounds} LLM rounds in this think() turn. " +
                        "Summarize what you accomplished and finish with a final text response (no more tool calls). " +
                        "If the PRD is complete, reply with exactly TASK_COMPLETE.";
                    if (_verboseLoggingEnabled)
                    {
                        WriteVerboseLine(
                            $"[llm] round {_llmRound}: max LLM rounds ({maxLlmRounds}) reached — stopping tool loop",
                            IsAgentRichCli()
                                ? $"[yellow][llm][/] round {_llmRound} · max LLM rounds ({maxLlmRounds}) reached"
                                : null);
                    }
                    EmitAgentProgress("done", message: $"Stopped after {maxLlmRounds} LLM rounds");
                    AddAssistantMessage(stopContent);
                    var stopResponse = new JsonObject();
                    stopResponse.Set("content", RuntimeValue.String(stopContent));
                    AttachAccumulatedUsage(RuntimeValue.Object(stopResponse));
                    return RuntimeValue.Object(stopResponse);
                }

                EmitAgentProgress("continue", message: "Continuing after tools…");
                
                // Recursively call send() again with tool results
                return Send();
            }
            else
            {
                // No tool calls, add assistant response.
                // Do not promote reasoning/CoT into content: thinking models keep the
                // chain-of-thought in "reasoning" and the user-facing answer in "content".
                var content = jsonResponse.Get("content", null);
                if (content != null && content.Type == ValueType.String &&
                    !string.IsNullOrWhiteSpace(content.AsString()))
                {
                    AddAssistantMessage(content.AsString());
                }
                EmitAgentProgress("done", message: "Answer ready");
            }
        }
        
        // #endregion
        AttachAccumulatedUsage(response);
        return response;
    }
    
    private void ResetTurnUsage()
    {
        _turnPromptTokens = 0;
        _turnCompletionTokens = 0;
        _turnTotalTokens = 0;
        _turnCost = 0;
        _turnHasUsage = false;
        _recordedTurnTokens = 0;
        _recordedTurnCost = 0;
    }

    private static void AddNormalizedUsage(JsonObject usageSource, ref int promptTokens, ref int completionTokens, ref int totalTokens, ref double cost, ref bool hasUsage)
    {
        var ptVal = usageSource.Get("promptTokens", null);
        if (ptVal != null && ptVal.Type == ValueType.Integer)
        {
            promptTokens += (int)ptVal.AsInteger();
            hasUsage = true;
        }

        var ctVal = usageSource.Get("completionTokens", null);
        if (ctVal != null && ctVal.Type == ValueType.Integer)
        {
            completionTokens += (int)ctVal.AsInteger();
            hasUsage = true;
        }

        var ttVal = usageSource.Get("totalTokens", null);
        if (ttVal != null && ttVal.Type == ValueType.Integer)
        {
            totalTokens += (int)ttVal.AsInteger();
            hasUsage = true;
        }

        var costVal = usageSource.Get("cost", null);
        if (costVal != null && (costVal.Type == ValueType.Float || costVal.Type == ValueType.Integer))
        {
            cost += costVal.Type == ValueType.Float ? costVal.AsFloat() : costVal.AsInteger();
            hasUsage = true;
        }
    }

    private void AccumulateTurnUsage(JsonObject jsonResponse)
    {
        var usage = jsonResponse.Get("usage", null);
        if (usage == null || usage.Type != ValueType.Object)
            return;

        if (usage.AsObject() is JsonObject usageObj)
            AddNormalizedUsage(usageObj, ref _turnPromptTokens, ref _turnCompletionTokens, ref _turnTotalTokens, ref _turnCost, ref _turnHasUsage);
    }

    private void AttachAccumulatedUsage(RuntimeValue response)
    {
        if (!_turnHasUsage || response.Type != ValueType.Object)
            return;

        if (response.AsObject() is not JsonObject jsonResponse)
            return;

        var usageObj = new JsonObject();
        usageObj.Set("promptTokens", RuntimeValue.Integer(_turnPromptTokens));
        usageObj.Set("completionTokens", RuntimeValue.Integer(_turnCompletionTokens));
        if (_turnTotalTokens > 0)
            usageObj.Set("totalTokens", RuntimeValue.Integer(_turnTotalTokens));
        else if (_turnPromptTokens > 0 || _turnCompletionTokens > 0)
            usageObj.Set("totalTokens", RuntimeValue.Integer(_turnPromptTokens + _turnCompletionTokens));
        if (_turnCost > 0)
            usageObj.Set("cost", RuntimeValue.Float(_turnCost));
        jsonResponse.Set("usage", RuntimeValue.Object(usageObj));
    }

    private void RecordBudgetUsageAfterLlmResponse(JsonObject jsonResponse)
    {
        if (!ResourceBoundsContext.HasActiveBound)
            return;

        if (_turnHasUsage)
        {
            var tokens = _turnTotalTokens > 0
                ? _turnTotalTokens
                : _turnPromptTokens + _turnCompletionTokens;
            var tokenDelta = tokens - _recordedTurnTokens;
            if (tokenDelta > 0)
                ResourceBoundsContext.RecordTokens(tokenDelta);
            _recordedTurnTokens = tokens;

            var costDelta = _turnCost - _recordedTurnCost;
            if (costDelta > 0)
                ResourceBoundsContext.RecordCost(costDelta);
            _recordedTurnCost = _turnCost;
            return;
        }

        // Best-effort: chars/4 of conversation payload plus this response, as a running delta.
        var estimated = EstimateContextTokens() + EstimateResponseTokens(jsonResponse);
        var estimateDelta = estimated - _recordedTurnTokens;
        if (estimateDelta > 0)
            ResourceBoundsContext.RecordTokens(estimateDelta);
        _recordedTurnTokens = estimated;
    }

    private int EstimateResponseTokens(JsonObject jsonResponse)
    {
        var chars = 0;
        var content = jsonResponse.Get("content", null);
        if (content != null && content.Type == ValueType.String)
            chars += content.AsString()?.Length ?? 0;

        var toolCalls = jsonResponse.Get("tool_calls", null);
        if (toolCalls != null && toolCalls.Type != ValueType.Null)
        {
            try
            {
                chars += SerializeRuntimeValueToJson(toolCalls).Length;
            }
            catch
            {
                chars += 256;
            }
        }

        return chars <= 0 ? 0 : (int)Math.Ceiling(chars / 4.0);
    }
}
