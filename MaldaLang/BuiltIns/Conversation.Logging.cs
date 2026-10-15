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
    private static int GetStatusBannerInterval()
    {
        var env = GetAgentEnv("MALDA_AGENT_STATUS_EVERY", "MALDA_RALPH_STATUS_EVERY");
        if (int.TryParse(env?.Trim(), out var interval) && interval > 0)
            return interval;
        return 4;
    }

    private static void MaybeWriteStatusBanner(int llmRound)
    {
        if (string.IsNullOrEmpty(_statusBanner))
            return;

        var every = GetStatusBannerInterval();
        if (llmRound != 1 && llmRound % every != 0)
            return;

        var line = _statusBanner + " · llm round " + llmRound;
        if (IsAgentRichCli())
        {
            WriteVerboseLine(line, "[bold]" + EscapeMarkup(line) + "[/]");
            WriteVerboseLine("", "[dim]────────────────────────────────────────────────────────[/]");
        }
        else
        {
            WriteVerboseLine("--- " + line + " ---");
        }
    }

    private static string? TryExtractToolTarget(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var key in new[] { "filePath", "path", "file", "directory", "pattern", "query", "command" })
            {
                if (!root.TryGetProperty(key, out var el))
                    continue;
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return TruncateForLog(s, 80);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string SummarizeToolResult(string? toolName, string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return "";

        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return TruncateForLog(result, 120);

        try
        {
            using var doc = JsonDocument.Parse(result);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("success", out var successEl) && successEl.ValueKind == JsonValueKind.False)
                {
                    if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.String)
                        return TruncateForLog(errorEl.GetString() ?? "failed", 120);
                    if (root.TryGetProperty("applied", out var appliedEl) && appliedEl.ValueKind == JsonValueKind.Number)
                        return $"failed (applied {appliedEl.GetInt32()})";
                    return "failed";
                }
                if (root.TryGetProperty("success", out var successEl2) && root.TryGetProperty("applied", out var appliedEl2))
                {
                    if (appliedEl2.ValueKind == JsonValueKind.Number)
                        return successEl2.GetBoolean() ? $"applied {appliedEl2.GetInt32()}" : "failed";
                }
                if (root.TryGetProperty("success", out var successOnly))
                    return successOnly.GetBoolean() ? "ok" : "failed";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                var s = doc.RootElement.GetString() ?? "";
                if (!string.IsNullOrEmpty(toolName) && CompactVerboseToolNames.Contains(toolName))
                    return $"{s.Length} chars";
            }
        }
        catch
        {
        }

        if (!string.IsNullOrEmpty(toolName) && CompactVerboseToolNames.Contains(toolName))
            return $"{result.Length} chars";

        return TruncateForLog(result, 80);
    }

    internal static bool IsWriteToolName(string? toolName) =>
        !string.IsNullOrEmpty(toolName) && WriteToolNames.Contains(toolName);

    internal static bool IsToolResultFailure(RuntimeValue? toolResult, out string? failureSummary)
    {
        failureSummary = null;
        if (toolResult == null)
            return false;

        if (toolResult.Type == ValueType.String)
        {
            var text = toolResult.AsString();
            if (text.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                failureSummary = text;
                return true;
            }
            return false;
        }

        if (toolResult.Type == ValueType.Boolean && !toolResult.AsBoolean())
        {
            failureSummary = "Tool returned success=false";
            return true;
        }

        if (toolResult.Type != ValueType.Object || toolResult.AsObject() is not JsonObject obj)
            return false;

        try
        {
            var successVal = obj.Get("success", null);
            if (successVal == null || successVal.Type != ValueType.Boolean || successVal.AsBoolean())
                return false;

            var errorVal = obj.Get("error", null);
            if (errorVal != null && errorVal.Type == ValueType.String && !string.IsNullOrWhiteSpace(errorVal.AsString()))
            {
                failureSummary = errorVal.AsString();
                return true;
            }

            var appliedVal = obj.Get("applied", null);
            var totalVal = obj.Get("totalEdits", null);
            if (appliedVal != null && appliedVal.Type == ValueType.Integer &&
                totalVal != null && totalVal.Type == ValueType.Integer)
            {
                failureSummary = $"Tool reported success=false (applied {appliedVal.AsInteger()}/{totalVal.AsInteger()})";
            }
            else if (appliedVal != null && appliedVal.Type == ValueType.Integer)
            {
                failureSummary = $"Tool reported success=false (applied {appliedVal.AsInteger()})";
            }
            else
            {
                failureSummary = "Tool reported success=false";
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RecordFailedWriteTool(string? toolName, string? argumentsJson, string? failureSummary)
    {
        if (!IsWriteToolName(toolName))
            return;

        _turnFailedWriteTools.Add(new FailedWriteToolRecord
        {
            ToolName = toolName ?? "unknown",
            Target = TryExtractToolTarget(argumentsJson),
            Error = failureSummary ?? "Write tool failed"
        });
    }

    internal static string FormatReadFileLogSummary(string? argumentsJson, string? content)
    {
        var lineCount = CountContentLines(content);
        var rangeLabel = TryGetReadFileRequestedRangeLabel(argumentsJson);

        if (!string.IsNullOrEmpty(rangeLabel))
            return $"{rangeLabel} · {FormatLineCount(lineCount)}";

        return FormatLineCount(lineCount);
    }

    internal static int CountContentLines(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        return content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).Length;
    }

    internal static string? TryGetReadFileRequestedRangeLabel(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var hasStart = root.TryGetProperty("startLine", out var startEl) && startEl.ValueKind == JsonValueKind.Number;
            var hasEnd = root.TryGetProperty("endLine", out var endEl) && endEl.ValueKind == JsonValueKind.Number;

            if (!hasStart && !hasEnd)
                return "full file";

            if (hasStart && !hasEnd)
            {
                var start = startEl.GetInt32();
                if (start < 0)
                    return $"last {Math.Abs(start)} lines";
                return $"lines {start}-end";
            }

            if (!hasStart && hasEnd)
                return $"lines 1-{endEl.GetInt32()}";

            return $"lines {startEl.GetInt32()}-{endEl.GetInt32()}";
        }
        catch
        {
            return null;
        }
    }

    private static string FormatLineCount(int lineCount) =>
        lineCount == 1 ? "1 line" : $"{lineCount} lines";

    private static string? GetReadFileToolLineSummary(string? toolName, string? argumentsJson, RuntimeValue? toolResult)
    {
        if (!string.Equals(toolName, "read_file", StringComparison.OrdinalIgnoreCase))
            return null;

        if (toolResult == null || toolResult.Type != ValueType.String)
            return null;

        var content = toolResult.AsString();
        if (content == null || content.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return null;

        return FormatReadFileLogSummary(argumentsJson, content);
    }

    private static string EscapeMarkup(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.Replace("[", "[[", StringComparison.Ordinal)
            .Replace("]", "]]", StringComparison.Ordinal);
    }

    private static void WriteVerboseLinePlain(string message)
    {
        if (!string.IsNullOrEmpty(_verbosePhaseLabel))
            message = $"[{_verbosePhaseLabel}] {message}";
        Console.WriteLine(message);
        Console.Out.Flush();
    }

    private static void WriteVerboseLine(string message, string? markupLine = null)
    {
        ClearActivityStatus();
        if (IsAgentRichCli() && !string.IsNullOrEmpty(markupLine))
        {
            try
            {
                AnsiConsole.MarkupLine(markupLine);
                Console.Out.Flush();
                return;
            }
            catch
            {
            }
        }

        WriteVerboseLinePlain(message);
    }
    
    private static void WriteVerboseToolCall(string toolName, string args, string result, bool isError, string? fullArgs)
    {
        if (IsAgentRichCli() && !IsToolDetailFull())
        {
            var safeName = EscapeMarkup(toolName ?? "?");
            var target = TryExtractToolTarget(fullArgs ?? args);
            var targetMarkup = string.IsNullOrEmpty(target) ? "" : $" [dim]{EscapeMarkup(target)}[/]";
            var summary = SummarizeToolResult(toolName, result);

            if (isError)
            {
                var err = EscapeMarkup(TruncateForLog(result, 120));
                WriteVerboseLine(
                    $"[tool ERROR] {toolName}: {TruncateForLog(result, 120)}",
                    $"  [red]✗ {safeName}[/]{targetMarkup} [dim]{err}[/]");
                return;
            }

            if (!string.IsNullOrEmpty(toolName) && CompactVerboseToolNames.Contains(toolName))
            {
                var lineSummary = string.Equals(toolName, "read_file", StringComparison.OrdinalIgnoreCase)
                    ? _readFileToolLineSummary
                    : null;
                var linePlain = string.IsNullOrEmpty(lineSummary) ? "" : $" · {lineSummary}";
                var lineMarkup = string.IsNullOrEmpty(lineSummary) ? "" : $" [dim]· {EscapeMarkup(lineSummary)}[/]";
                WriteVerboseLine(
                    $"[tool] {toolName} {target ?? TruncateForLog(args, 60)}{linePlain}",
                    $"  [cyan]{safeName}[/]{targetMarkup}{lineMarkup}");
                return;
            }

            var summaryMarkup = string.IsNullOrEmpty(summary) ? "" : $" [dim]{EscapeMarkup(summary)}[/]";
            WriteVerboseLine(
                $"[tool] {toolName}{target ?? ""} {summary}",
                $"  [green]{safeName}[/]{targetMarkup}{summaryMarkup}");
            return;
        }

        var prefix = isError ? "[tool ERROR]" : "[tool]";
        WriteVerboseLine($"{prefix} {toolName}");
        if (!string.IsNullOrEmpty(args))
            WriteVerboseLine($"  args: {TruncateForLog(args, 300)}");
        if (string.Equals(toolName, "read_file", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(_readFileToolLineSummary))
        {
            WriteVerboseLine($"  lines: {_readFileToolLineSummary}");
        }
        WriteVerboseLine($"  result: {TruncateForLog(result, 400)}");
    }
    
    private void EmitAgentProgress(
        string phase,
        string? message = null,
        IReadOnlyList<string>? tools = null,
        string? tool = null,
        bool? ok = null)
    {
        Action<RuntimeValue>? handler;
        lock (AgentProgressLock)
        {
            handler = _agentProgressHandler;
        }
        var liveChannel = GetAgentProgressLiveChannel();
        if (handler == null && liveChannel == null)
            return;

        var payload = new JsonObject();
        payload.Set("phase", RuntimeValue.String(phase));
        payload.Set("round", RuntimeValue.Integer(_llmRound));
        if (!string.IsNullOrEmpty(AgentName))
            payload.Set("agent", RuntimeValue.String(AgentName));
        if (!string.IsNullOrEmpty(message))
            payload.Set("message", RuntimeValue.String(message));
        if (tools != null)
        {
            var toolValues = new List<RuntimeValue>(tools.Count);
            foreach (var name in tools)
                toolValues.Add(RuntimeValue.String(name));
            payload.Set("tools", RuntimeValue.Array(toolValues));
        }
        if (!string.IsNullOrEmpty(tool))
            payload.Set("tool", RuntimeValue.String(tool));
        if (ok.HasValue)
            payload.Set("ok", RuntimeValue.Boolean(ok.Value));
        DeliverAgentProgress(RuntimeValue.Object(payload));
    }

    private List<string> ExtractToolCallNames(RuntimeValue toolCalls)
    {
        var names = new List<string>();
        if (toolCalls.Type != ValueType.Array)
            return names;
        foreach (var tc in toolCalls.AsArray())
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
        return names;
    }

    private void LogLlmRequest()
    {
        // Always advance round for max-round enforcement and live progress,
        // even when verbose CLI logging is off.
        _llmRound++;
        EmitAgentProgress("round_start", message: "Calling LLM…");
        WriteActivityStatus(FormatThinkingActivity(_llmRound));

        EnsureVerboseLoggingSetup();
        if (!_verboseLoggingEnabled)
            return;

        MaybeWriteStatusBanner(_llmRound);
        var modelName =
            _client != null ? _client.Model :
            _llamaClient != null ? _llamaClient.ModelPath :
            _bridgeClient != null ? "bridge" :
            "unknown";
        var plain = $"[llm] round {_llmRound}: sending request ({_messages.Count} message(s), {_tools.Count} tool(s), model={modelName})";
        if (IsAgentRichCli())
        {
            WriteVerboseLine(
                plain,
                $"[dim][llm][/] round {_llmRound} · {EscapeMarkup(modelName)} · {_messages.Count} msg · {_tools.Count} tools");
        }
        else
        {
            WriteVerboseLine(plain);
        }
    }
    
    private void LogLlmResponse(JsonObject jsonResponse)
    {
        EnsureVerboseLoggingSetup();
        if (!_verboseLoggingEnabled)
            return;
        
        var toolCalls = jsonResponse.Get("tool_calls", null);
        var hasToolCalls = toolCalls != null && toolCalls.Type == ValueType.Array && toolCalls.AsArray().Count > 0;

        var thinkingText = ExtractThinkingFromResponse(jsonResponse, hasToolCalls);
        LogLlmThinking(thinkingText, hasToolCalls);

        if (hasToolCalls)
        {
            var names = new List<string>();
            foreach (var tc in toolCalls!.AsArray())
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
            var plain = $"[llm] round {_llmRound}: tool calls ({toolCalls.AsArray().Count}): {joined}";
            if (IsAgentRichCli())
            {
                WriteVerboseLine(
                    plain,
                    $"[cyan][llm][/] round {_llmRound} → {EscapeMarkup(joined)}");
            }
            else
            {
                WriteVerboseLine(plain);
            }
        }
        else if (GetLlmThinkingMode() == LlmThinkingMode.Off)
        {
            var content = GetStringProperty(jsonResponse, "content") ?? "";
            var plain = $"[llm] round {_llmRound}: final response ({content.Length} chars)";
            if (IsAgentRichCli())
            {
                WriteVerboseLine(
                    plain,
                    $"[dim][llm][/] round {_llmRound} · final response ({content.Length} chars)");
            }
            else if (IsLlmResponsePreviewFull())
            {
                plain = $"{plain}: {TruncateForLog(content, 240)}";
                WriteVerboseLine(plain);
            }
            else
            {
                WriteVerboseLine(plain);
            }
        }
        else
        {
            var content = GetStringProperty(jsonResponse, "content") ?? "";
            var plain = $"[llm] round {_llmRound}: final response ({content.Length} chars)";
            if (IsAgentRichCli())
            {
                WriteVerboseLine(
                    plain,
                    $"[dim][llm][/] round {_llmRound} · final response ({content.Length} chars)");
            }
            else
            {
                WriteVerboseLine(plain);
            }
        }
    }
}
