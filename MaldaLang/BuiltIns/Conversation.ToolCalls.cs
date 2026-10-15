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
    /// <summary>
    /// Best-effort classification of tool type based on its name.
    /// Used only for tracing metadata; has no impact on behavior.
    /// </summary>
    private string? ResolveToolName(string? toolName)
    {
        if (toolName == null)
            return null;
        if (_tools.ContainsKey(toolName))
            return toolName;
        if (ToolNameAliases.TryGetValue(toolName, out var alias) && _tools.ContainsKey(alias))
            return alias;
        return toolName;
    }

    private string BuildToolNotFoundMessage(string? toolName)
    {
        var name = toolName ?? "unknown";
        var available = string.Join(", ", _tools.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        if (ShellWrapperToolNames.Contains(name))
        {
            return $"Error: '{name}' is not a tool. Use run_command with command + args (e.g. dotnet, npm). Available tools: {available}";
        }
        if (name.Equals("findstr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("rg", StringComparison.OrdinalIgnoreCase))
        {
            return $"Error: Use the grep tool instead of '{name}'. Available tools: {available}";
        }
        return $"Error: Tool '{name}' not found. Available tools: {available}";
    }

    private sealed class ToolCallOutcome
    {
        public required string ToolCallId { get; init; }
        public RuntimeValue? ToolResult { get; init; }
        public string? ToolName { get; init; }
        public string? CorrelationId { get; init; }
        public bool ToolCallEndRecorded { get; init; }
        public bool ExecutedCoreTool { get; init; }
        public bool Succeeded { get; init; } = true;
    }

    private sealed class FailedWriteToolRecord
    {
        public required string ToolName { get; init; }
        public string? Target { get; init; }
        public required string Error { get; init; }
    }

    /// <summary>
    /// Whether read-only tool calls in the same LLM round may run in parallel.
    /// Enabled by default; set MALDA_PARALLEL_TOOL_CALLS=false to disable.
    /// </summary>
    internal static bool IsParallelToolCallsEnabled()
    {
        if (_parallelToolCallsEnabled.HasValue)
            return _parallelToolCallsEnabled.Value;

        var env = System.Environment.GetEnvironmentVariable("MALDA_PARALLEL_TOOL_CALLS");
        if (string.IsNullOrWhiteSpace(env))
        {
            _parallelToolCallsEnabled = true;
            return true;
        }

        var lower = env.Trim().ToLowerInvariant();
        _parallelToolCallsEnabled = lower is not ("0" or "false" or "no" or "off");
        return _parallelToolCallsEnabled.Value;
    }

    internal static bool IsParallelSafeBuiltInTool(ToolInstance tool, string resolvedName)
    {
        if (tool is AgentToolInstance or MCPToolInstance)
            return false;
        if (tool.GetTranspiledMethod() != null || tool.GetFunctionHandler() != null)
            return false;
        return ParallelSafeToolNames.Contains(resolvedName);
    }

    private bool CanParallelizeToolCall(RuntimeValue tc)
    {
        if (!IsParallelToolCallsEnabled() || tc.Type != ValueType.Object)
            return false;

        var tcObj = tc.AsObject();
        var func = GetProperty(tcObj, "function");
        if (func == null || func.Type != ValueType.Object)
            return false;

        var toolName = GetStringProperty(func.AsObject(), "name");
        var resolvedToolName = ResolveToolName(toolName);
        if (resolvedToolName == null || !_tools.TryGetValue(resolvedToolName, out var tool))
            return false;

        return IsParallelSafeBuiltInTool(tool, resolvedToolName);
    }

    private List<ToolCallOutcome> ExecuteToolCalls(List<RuntimeValue> toolCallsToExecute)
    {
        var outcomes = new List<ToolCallOutcome>(toolCallsToExecute.Count);
        var i = 0;

        while (i < toolCallsToExecute.Count)
        {
            var tc = toolCallsToExecute[i];
            if (tc.Type != ValueType.Object)
            {
                i++;
                continue;
            }

            if (!CanParallelizeToolCall(tc))
            {
                outcomes.Add(ExecuteSingleToolCall(tc));
                i++;
                continue;
            }

            var batchStart = i;
            while (i < toolCallsToExecute.Count && CanParallelizeToolCall(toolCallsToExecute[i]))
                i++;

            var batch = toolCallsToExecute.GetRange(batchStart, i - batchStart);
            if (batch.Count == 1)
            {
                outcomes.Add(ExecuteSingleToolCall(batch[0]));
                continue;
            }

            LogParallelToolBatch(batch);
            WriteParallelActivityStart(batch);
            var tasks = batch.Select(item => Task.Run(() => ExecuteSingleToolCall(item, reportActivity: false))).ToArray();
            try
            {
                Task.WaitAll(tasks);
            }
            catch (AggregateException agg)
            {
                foreach (var inner in agg.Flatten().InnerExceptions)
                {
                    if (inner is InputRequiredException)
                    {
                        ClearActivityStatus();
                        throw inner;
                    }
                    if (inner is RuntimeException)
                        throw inner;
                }
                throw;
            }

            var batchOutcomes = new List<ToolCallOutcome>(tasks.Length);
            foreach (var task in tasks)
                batchOutcomes.Add(task.Result);
            outcomes.AddRange(batchOutcomes);
            WriteParallelActivityDone(batch, batchOutcomes);
        }

        return outcomes;
    }

    private void WriteParallelActivityStart(List<RuntimeValue> batch)
    {
        var parts = new List<string>();
        foreach (var tc in batch)
        {
            if (!TryReadToolCall(tc, out var name, out var args))
                continue;
            parts.Add(FormatToolActivity(name, TryExtractToolTarget(args)));
        }

        if (parts.Count == 0)
            return;
        WriteActivityStatus(FormatParallelActivity(parts));
    }

    private void WriteParallelActivityDone(List<RuntimeValue> batch, List<ToolCallOutcome> outcomes)
    {
        var parts = new List<string>();
        var anyFailed = false;
        for (var i = 0; i < outcomes.Count; i++)
        {
            var outcome = outcomes[i];
            string? args = null;
            if (i < batch.Count)
                TryReadToolCall(batch[i], out _, out args);
            if (!outcome.Succeeded)
                anyFailed = true;
            var suffix = outcome.Succeeded
                ? (GetReadFileToolLineSummary(outcome.ToolName, args, outcome.ToolResult) ?? "ok")
                : "failed";
            parts.Add(FormatToolActivity(outcome.ToolName, TryExtractToolTarget(args), suffix));
        }

        if (parts.Count == 0)
            return;
        var line = FormatParallelActivity(parts);
        if (anyFailed)
            CommitActivityStatus(line);
        else
            WriteActivityStatus(line);
    }

    private bool TryReadToolCall(RuntimeValue tc, out string? toolName, out string? argumentsJson)
    {
        toolName = null;
        argumentsJson = null;
        if (tc.Type != ValueType.Object)
            return false;
        var func = GetProperty(tc.AsObject(), "function");
        if (func == null || func.Type != ValueType.Object)
            return false;
        var funcObj = func.AsObject();
        toolName = GetStringProperty(funcObj, "name");
        argumentsJson = GetStringProperty(funcObj, "arguments");
        return true;
    }

    private void LogParallelToolBatch(List<RuntimeValue> batch)
    {
        EnsureVerboseLoggingSetup();
        if (!_verboseLoggingEnabled)
            return;

        var names = new List<string>();
        foreach (var tc in batch)
        {
            if (tc.Type != ValueType.Object)
                continue;
            var func = GetProperty(tc.AsObject(), "function");
            if (func != null && func.Type == ValueType.Object)
            {
                var toolName = GetStringProperty(func.AsObject(), "name");
                if (!string.IsNullOrEmpty(toolName))
                    names.Add(toolName);
            }
        }

        var joined = names.Count > 0 ? string.Join(", ", names) : "unknown";
        var parallelPlain = $"[llm] round {_llmRound}: executing {batch.Count} read-only tool call(s) in parallel: {joined}";
        WriteVerboseLine(
            parallelPlain,
            IsAgentRichCli()
                ? $"[dim][llm][/] round {_llmRound} · parallel ({batch.Count}): {EscapeMarkup(joined)}"
                : null);
    }

    private ToolCallOutcome ExecuteSingleToolCall(RuntimeValue tc, bool reportActivity = true)
    {
        ResourceBoundsContext.RecordToolInvocation();
        var tcObj = tc.AsObject();
        var toolCallId = GetStringProperty(tcObj, "id") ?? "";
        var func = GetProperty(tcObj, "function");

        string? toolName = null;
        string? correlationId = null;
        var toolCallEndRecorded = false;
        var executedCoreTool = false;
        var toolCallLogged = false;
        string? fullArguments = null;
        string? argsDisplay = null;
        RuntimeValue? toolResult = null;

        if (func != null && func.Type == ValueType.Object)
        {
            var funcObj = func.AsObject();
            toolName = GetStringProperty(funcObj, "name");
            var argumentsJson = GetStringProperty(funcObj, "arguments");
            var resolvedToolName = ResolveToolName(toolName);

            if (resolvedToolName != null && _tools.ContainsKey(resolvedToolName))
            {
                var tool = _tools[resolvedToolName];

                fullArguments = argumentsJson ?? "{}";
                argsDisplay = fullArguments;
                if (argsDisplay.Length > 200)
                    argsDisplay = argsDisplay.Substring(0, 200) + "...";

                correlationId = Guid.NewGuid().ToString("N");

                try
                {
                    TraceManager.Record(
                        TraceEventType.ToolCallStart,
                        new
                        {
                            toolName = toolName,
                            toolType = InferToolType(toolName),
                            argumentsJson = fullArguments,
                            workingDirectory = tool.WorkingDirectory,
                            correlationId
                        },
                        AgentName,
                        SessionId);
                }
                catch
                {
                    // Tracing must never interfere with normal execution
                }

                try
                {
                    RuntimeValue? argsValue = null;
                    if (argumentsJson != null)
                    {
                        JsonException? lastException = null;
                        try
                        {
                            var options = new JsonDocumentOptions
                            {
                                AllowTrailingCommas = true,
                                CommentHandling = JsonCommentHandling.Skip
                            };
                            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson, options);
                            argsValue = JsonToRuntimeValue(doc.RootElement);
                        }
                        catch (JsonException jsonEx)
                        {
                            lastException = jsonEx;
                            try
                            {
                                var fixedJson = TryFixJsonEscapeSequences(argumentsJson);
                                var options = new JsonDocumentOptions
                                {
                                    AllowTrailingCommas = true,
                                    CommentHandling = JsonCommentHandling.Skip
                                };
                                using var doc = System.Text.Json.JsonDocument.Parse(fixedJson, options);
                                argsValue = JsonToRuntimeValue(doc.RootElement);
                                lastException = null;
                            }
                            catch (JsonException)
                            {
                            }
                        }

                        if (argsValue == null && lastException != null)
                        {
                            var jsonEx = lastException;
                            var isTruncationError = ToolArgumentsJsonHelper.IsLikelyTruncated(jsonEx, argumentsJson, toolName);

                            if (isTruncationError)
                            {
                                if (toolName == "write_file")
                                {
                                    var jsonErrorMsg = $"Error parsing tool arguments JSON: {jsonEx.Message} (JSON length: {argumentsJson.Length} characters). The JSON appears to be truncated. Cannot write/update file with incomplete content to prevent data corruption.";
                                    _toolCallLogger?.Invoke(toolName, argsDisplay, $"Error: {jsonErrorMsg}", true, fullArguments);
                                    toolResult = RuntimeValue.String(ToolArgumentsJsonHelper.WriteFileTruncationToolResult(argumentsJson.Length));
                                    toolCallLogged = true;
                                    RecordFailedWriteTool(toolName, fullArguments, toolResult.AsString());
                                }
                                else if (toolName == "replace_in_file")
                                {
                                    _toolCallLogger?.Invoke(toolName, argsDisplay, $"Error: The text to replace was not found in the file (JSON was truncated during parsing).", true, fullArguments);
                                    toolResult = RuntimeValue.String($"Error: The text to replace was not found in the file. Please verify that the text exists in the file or check for encoding/whitespace differences.");
                                    toolCallLogged = true;
                                    RecordFailedWriteTool(toolName, fullArguments, toolResult.AsString());
                                }
                                else
                                {
                                    var extractedArgs = TryExtractFromTruncatedJson(argumentsJson);
                                    if (extractedArgs != null)
                                        argsValue = extractedArgs;
                                    else
                                    {
                                        var jsonErrorMsg = $"Error parsing tool arguments JSON: {jsonEx.Message} (JSON length: {argumentsJson.Length} characters). The JSON appears to be truncated.";
                                        _toolCallLogger?.Invoke(toolName, argsDisplay, $"Error: {jsonErrorMsg}", true, fullArguments);
                                        toolResult = RuntimeValue.String($"Error: Invalid JSON in tool arguments. {jsonEx.Message}. The JSON appears to be truncated and could not be recovered.");
                                        toolCallLogged = true;
                                        RecordFailedWriteTool(toolName, fullArguments, toolResult.AsString());
                                    }
                                }
                            }
                            else if (toolName == "write_file" && ToolArgumentsJsonHelper.LooksLikeWriteFilePayload(argumentsJson))
                            {
                                var jsonErrorMsg = $"Error parsing tool arguments JSON: {jsonEx.Message} (JSON length: {argumentsJson.Length} characters).";
                                _toolCallLogger?.Invoke(toolName, argsDisplay, $"Error: {jsonErrorMsg}", true, fullArguments);
                                toolResult = RuntimeValue.String(ToolArgumentsJsonHelper.WriteFileTruncationToolResult(argumentsJson.Length));
                                toolCallLogged = true;
                                RecordFailedWriteTool(toolName, fullArguments, toolResult.AsString());
                            }
                            else
                            {
                                var jsonErrorMsg = $"Error parsing tool arguments JSON: {jsonEx.Message}";
                                if (argumentsJson.Length > 20000)
                                    jsonErrorMsg += $" (JSON length: {argumentsJson.Length} characters)";
                                if (jsonEx.Message.Contains("escape") || jsonEx.Message.Contains("Invalid character"))
                                    jsonErrorMsg += " Attempted to fix escape sequences but parsing still failed.";
                                _toolCallLogger?.Invoke(toolName, argsDisplay, $"Error: {jsonErrorMsg}", true, fullArguments);
                                toolResult = RuntimeValue.String($"Error: Invalid JSON in tool arguments. {jsonEx.Message}. This may occur if the content contains unescaped quotes, malformed escape sequences, or is truncated.");
                                toolCallLogged = true;
                                RecordFailedWriteTool(toolName, fullArguments, toolResult.AsString());
                            }
                        }
                    }
                    else
                    {
                        argsValue = RuntimeValue.Null();
                    }

                    if (toolResult == null)
                    {
                        if (reportActivity)
                            WriteActivityStatus(FormatToolActivity(toolName, TryExtractToolTarget(fullArguments)));
                        toolResult = ExecuteToolOperation(tool, argsValue ?? RuntimeValue.Null());
                        executedCoreTool = true;

                        var resultJsonFull = SerializeRuntimeValueToJson(toolResult ?? RuntimeValue.Null());
                        var resultDisplay = TruncateForLog(resultJsonFull, 500);
                        var traceResult = TruncateForTrace(resultJsonFull);
                        var toolFailed = IsToolResultFailure(toolResult, out var failureSummary);
                        if (toolFailed)
                            RecordFailedWriteTool(toolName, fullArguments, failureSummary);
                        _readFileToolLineSummary = GetReadFileToolLineSummary(toolName, fullArguments, toolResult);
                        try
                        {
                            _toolCallLogger?.Invoke(toolName, argsDisplay, resultDisplay, toolFailed, fullArguments);
                            toolCallLogged = true;
                        }
                        finally
                        {
                            _readFileToolLineSummary = null;
                        }

                        try
                        {
                            TraceManager.Record(
                                TraceEventType.ToolCallEnd,
                                new
                                {
                                    toolName = toolName,
                                    toolType = InferToolType(toolName),
                                    correlationId,
                                    durationMs = (int?)null,
                                    resultJson = traceResult,
                                    success = !toolFailed,
                                    error = toolFailed ? failureSummary : (object?)null
                                },
                                AgentName,
                                SessionId);
                            toolCallEndRecorded = true;
                        }
                        catch
                        {
                        }

                        if (!string.IsNullOrEmpty(AgentName))
                        {
                            try
                            {
                                AgentDashboardService.Instance.ReportToolCall(AgentName, toolName, !toolFailed, toolFailed ? failureSummary : null);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch (InputRequiredException)
                {
                    ClearActivityStatus();
                    throw;
                }
                catch (Exception ex)
                {
                    var errorMsg = ex.Message;
                    var logError = TruncateForLog(errorMsg, 500);
                    var traceError = TruncateForTrace(errorMsg);
                    _toolCallLogger?.Invoke(toolName, argsDisplay, $"Error: {logError}", true, fullArguments);
                    toolResult = RuntimeValue.String($"Error executing tool: {ex.Message}");
                    toolCallLogged = true;
                    RecordFailedWriteTool(toolName, fullArguments, toolResult.AsString());

                    try
                    {
                        TraceManager.Record(
                            TraceEventType.ToolCallEnd,
                            new
                            {
                                toolName = toolName,
                                toolType = InferToolType(toolName),
                                correlationId,
                                durationMs = (int?)null,
                                resultJson = (string?)null,
                                success = false,
                                error = traceError
                            },
                            AgentName,
                            SessionId);
                        toolCallEndRecorded = true;
                    }
                    catch
                    {
                    }

                    if (!string.IsNullOrEmpty(AgentName))
                    {
                        try
                        {
                            AgentDashboardService.Instance.ReportToolCall(AgentName, toolName, false, errorMsg);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            else
            {
                toolResult = RuntimeValue.String(BuildToolNotFoundMessage(toolName));

                if (!string.IsNullOrEmpty(AgentName))
                {
                    try
                    {
                        AgentDashboardService.Instance.ReportToolCall(AgentName, toolName ?? "unknown", false, "Tool not found");
                    }
                    catch
                    {
                    }
                }
            }
        }
        else
        {
            toolResult = RuntimeValue.String("Error: Invalid tool call format");

            if (!string.IsNullOrEmpty(AgentName))
            {
                try
                {
                    AgentDashboardService.Instance.ReportToolCall(AgentName, "unknown", false, "Invalid tool call format");
                }
                catch
                {
                }
            }
        }

        var succeeded = !IsToolResultFailure(toolResult, out _);
        if (reportActivity)
            FinishToolActivity(toolName, fullArguments, toolResult, succeeded);
        if (!toolCallLogged && toolResult != null && argsDisplay != null)
        {
            var lateResultDisplay = SerializeRuntimeValueToJson(toolResult);
            if (lateResultDisplay.Length > 500)
                lateResultDisplay = lateResultDisplay.Substring(0, 500) + "...";
            _toolCallLogger?.Invoke(toolName ?? "unknown", argsDisplay, lateResultDisplay, !succeeded, fullArguments);
        }

        return new ToolCallOutcome
        {
            ToolCallId = toolCallId,
            ToolResult = toolResult,
            ToolName = toolName,
            CorrelationId = correlationId,
            ToolCallEndRecorded = toolCallEndRecorded,
            ExecutedCoreTool = executedCoreTool,
            Succeeded = succeeded
        };
    }

    private void AppendToolResultMessage(ToolCallOutcome outcome)
    {
        var toolMsg = new JsonObject();
        toolMsg.Set("role", RuntimeValue.String("tool"));
        toolMsg.Set("tool_call_id", RuntimeValue.String(outcome.ToolCallId));

        string toolResultJson;
        if (outcome.ToolResult == null)
            toolResultJson = "null";
        else
            toolResultJson = SerializeRuntimeValueToJson(outcome.ToolResult);

        toolMsg.Set("content", RuntimeValue.String(toolResultJson));
        _messages.Add(RuntimeValue.Object(toolMsg));

        if (!outcome.ToolCallEndRecorded)
        {
            try
            {
                TraceManager.Record(
                    TraceEventType.ToolCallEnd,
                    new
                    {
                        toolName = outcome.ToolName ?? "unknown",
                        toolType = InferToolType(outcome.ToolName ?? "unknown"),
                        correlationId = outcome.CorrelationId,
                        durationMs = (int?)null,
                        resultJson = TruncateForTrace(toolResultJson),
                        success = outcome.Succeeded,
                        error = outcome.Succeeded ? null : (object?)"Tool did not execute successfully"
                    },
                    AgentName,
                    SessionId);
            }
            catch
            {
            }
        }
    }

    private static string InferToolType(string toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return "other";

        if (toolName.StartsWith("git", StringComparison.OrdinalIgnoreCase))
            return "git";

        if (toolName.Equals("run_command", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("runCommand", StringComparison.OrdinalIgnoreCase))
            return "runCommand";

        if (toolName.IndexOf("maldatool", StringComparison.OrdinalIgnoreCase) >= 0 ||
            toolName.IndexOf("run_malda", StringComparison.OrdinalIgnoreCase) >= 0 ||
            toolName.IndexOf("compile_malda", StringComparison.OrdinalIgnoreCase) >= 0)
            return "malda";

        if (toolName.Contains("file", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("directory", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("grep", StringComparison.OrdinalIgnoreCase))
            return "file";

        if (toolName.Contains("mcp", StringComparison.OrdinalIgnoreCase))
            return "mcp";

        if (toolName.Equals("web_search", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("web_fetch", StringComparison.OrdinalIgnoreCase))
            return "web";

        return "other";
    }
}
