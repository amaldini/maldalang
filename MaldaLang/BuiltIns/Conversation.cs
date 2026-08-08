// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
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

public partial class ConversationInstance : ObjectInstance
{
    internal static DateTime? ThinkDeadlineUtc { get; set; }

    internal static int ResolveThinkTimeoutMs()
    {
        foreach (var name in new[] { "MALDA_AGENT_THINK_TIMEOUT_MS", "MALDA_RALPH_ITER_TIMEOUT_MS" })
        {
            var raw = System.Environment.GetEnvironmentVariable(name);
            if (int.TryParse(raw, out var ms) && ms > 0)
                return ms;
        }
        return 0;
    }

    internal static int ResolveMaxLlmRounds()
    {
        foreach (var name in new[] { "MALDA_AGENT_MAX_LLM_ROUNDS", "MALDA_RALPH_INTERVIEW_MAX_LLM_ROUNDS", "MALDA_RALPH_MAX_LLM_ROUNDS" })
        {
            var raw = System.Environment.GetEnvironmentVariable(name);
            if (int.TryParse(raw, out var rounds) && rounds > 0)
                return rounds;
        }
        return 0;
    }

    internal static void EnsureWithinThinkDeadline()
    {
        if (ThinkDeadlineUtc.HasValue && DateTime.UtcNow > ThinkDeadlineUtc.Value)
            throw new InvalidOperationException("Agent think() timed out");
    }

    private List<RuntimeValue> _messages = new();
    private LLMClientInstance? _client;
    private LlamaCppClientInstance? _llamaClient;
    private LLMClientBridge.LLMClientBridgeInstance? _bridgeClient;
    private string _systemPrompt;
    private Dictionary<string, ToolInstance> _tools = new();
    private IInputProvider? _inputProvider;
    private static Action<string, string, string, bool, string?>? _toolCallLogger;
    private static readonly object AgentProgressLock = new();
    private static Action<RuntimeValue>? _agentProgressHandler;
    /// <summary>Process-wide fallback (single-threaded hosts / tests).</summary>
    private static string? _agentProgressLiveChannel;
    /// <summary>Per-request/async flow channel so concurrent ASK sessions do not clash.</summary>
    private static readonly AsyncLocal<string?> AgentProgressLiveChannelLocal = new();
    private static bool _verboseLoggingResolved;
    private static bool _verboseLoggingEnabled;
    private static string? _verbosePhaseLabel;
    private static bool? _parallelToolCallsEnabled;

    [ThreadStatic]
    private static string? _readFileToolLineSummary;

    private static bool? _agentRichCliResolved;
    private static bool _agentRichCli;
    private static string? _statusBanner;

    private static readonly HashSet<string> CompactVerboseToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file",
        "grep",
        "glob",
        "list_directory",
        "get_symbols",
        "get_parse_errors",
        "web_search",
        "git_status",
        "git_log",
        "git_diff",
        "git_branch",
        "recall_progress",
    };

    private static readonly HashSet<string> ParallelSafeToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file",
        "grep",
        "glob",
        "list_directory",
        "get_symbols",
        "get_parse_errors",
        "web_search",
        "git_status",
        "git_log",
        "git_diff",
        "git_branch",
        "recall_progress",
    };

    private static readonly HashSet<string> WriteToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "write_file",
        "replace_in_file",
        "edit_file",
        "insertAtLine",
        "insert_at_line",
    };

    private static readonly Dictionary<string, string> ToolNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["findstr"] = "grep",
        ["rg"] = "grep",
        ["run_terminal_cmd"] = "run_command",
        ["runCommand"] = "run_command",
        ["execute_command"] = "run_command",
    };

    private static readonly HashSet<string> ShellWrapperToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "pwsh", "cmd", "bash", "sh", "zsh", "fish", "csh", "tcsh", "ksh"
    };
    private int _llmRound;
    private bool _llmStreamHeaderPrinted;
    private bool _llmStreamThinkingPrinted;
    private int _turnPromptTokens;
    private int _turnCompletionTokens;
    private int _turnTotalTokens;
    private double _turnCost;
    private bool _turnHasUsage;
    private readonly List<FailedWriteToolRecord> _turnFailedWriteTools = new();
    
    /// <summary>
    /// Optional trace session identifier associated with this conversation.
    /// When set and tracing is enabled, LLM and tool calls will be recorded
    /// under this session using <see cref="TraceManager"/>.
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// The name of the agent that owns this conversation, used for dashboard reporting.
    /// Set automatically by AgentInstance when the conversation is initialized.
    /// </summary>
    public string? AgentName { get; set; }
    
    public static void SetToolCallLogger(Action<string, string, string, bool, string?>? logger)
    {
        _toolCallLogger = logger;
    }

    /// <summary>
    /// Optional sink for live agent progress (LLM rounds / tool calls).
    /// Used by ASK UI and other hosts; must never throw into think().
    /// </summary>
    public static void SetAgentProgressHandler(Action<RuntimeValue>? handler)
    {
        lock (AgentProgressLock)
        {
            _agentProgressHandler = handler;
        }
    }

    /// <summary>
    /// When set, progress events are also pushed via componentLiveEmit(channel, …).
    /// Works in transpiled apps without an interpreter callback.
    /// Uses <see cref="AsyncLocal{T}"/> so concurrent HTTP asks keep distinct channels.
    /// </summary>
    public static void SetAgentProgressLiveChannel(string? channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? null : channel.Trim();
        AgentProgressLiveChannelLocal.Value = normalized;
        lock (AgentProgressLock)
        {
            // Keep static fallback for hosts that are not concurrent / unit tests.
            _agentProgressLiveChannel = normalized;
        }
    }

    public static void ClearAgentProgressHandler()
    {
        AgentProgressLiveChannelLocal.Value = null;
        lock (AgentProgressLock)
        {
            _agentProgressHandler = null;
            _agentProgressLiveChannel = null;
        }
    }

    /// <summary>
    /// Active live channel for this async flow, else process-wide fallback.
    /// </summary>
    public static string? GetAgentProgressLiveChannel()
    {
        var local = AgentProgressLiveChannelLocal.Value;
        if (!string.IsNullOrWhiteSpace(local))
            return local;
        lock (AgentProgressLock)
        {
            return _agentProgressLiveChannel;
        }
    }

    /// <summary>
    /// Delivers a progress event to the registered handler and/or live channel.
    /// Used by the agent loop; also available for focused unit tests.
    /// </summary>
    public static void DeliverAgentProgress(RuntimeValue evt)
    {
        Action<RuntimeValue>? handler;
        lock (AgentProgressLock)
        {
            handler = _agentProgressHandler;
        }
        var liveChannel = GetAgentProgressLiveChannel();

        try
        {
            handler?.Invoke(evt);
            if (liveChannel != null)
            {
                BuiltInFunctions.CallBuiltIn(
                    "componentLiveEmit",
                    new List<RuntimeValue>
                    {
                        RuntimeValue.String(liveChannel),
                        evt,
                        RuntimeValue.String("ask-progress")
                    },
                    null);
            }
        }
        catch
        {
            // Progress callbacks must never break the agent loop.
        }
    }
    
    public static void EnableVerboseLogging(bool enabled)
    {
        _verboseLoggingEnabled = enabled;
        _verboseLoggingResolved = true;
        if (enabled && _toolCallLogger == null)
        {
            SetToolCallLogger(WriteVerboseToolCall);
        }
    }

    /// <summary>
    /// Optional phase label prefixed to verbose log lines (e.g. current PRD feature).
    /// </summary>
    public static void SetVerbosePhase(string? phase)
    {
        _verbosePhaseLabel = string.IsNullOrWhiteSpace(phase) ? null : phase.Trim();
    }

    /// <summary>
    /// Single-line status banner repeated during verbose LLM rounds (e.g. Ralph project / phase / progress).
    /// </summary>
    public static void SetStatusBanner(string? banner)
    {
        _statusBanner = string.IsNullOrWhiteSpace(banner) ? null : banner.Trim();
    }
    
    private static void EnsureVerboseLoggingSetup()
    {
        if (_verboseLoggingResolved)
            return;
        
        _verboseLoggingResolved = true;
        var env = GetAgentEnv("MALDA_AGENT_VERBOSE", "MALDA_RALPH_VERBOSE");
        _verboseLoggingEnabled = IsTruthyEnv(env);
        if (_verboseLoggingEnabled && _toolCallLogger == null)
        {
            SetToolCallLogger(WriteVerboseToolCall);
        }
    }
    
    private static bool IsTruthyEnv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var lower = value.Trim().ToLowerInvariant();
        return lower is "1" or "true" or "yes" or "on";
    }
    
    private static string TruncateForLog(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength) + "...";
    }

    private static string? GetAgentEnv(string primary, string legacyAlias)
    {
        var value = System.Environment.GetEnvironmentVariable(primary);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        return System.Environment.GetEnvironmentVariable(legacyAlias);
    }

    private static bool IsAgentRichCli()
    {
        if (_agentRichCliResolved.HasValue)
            return _agentRichCli;

        var env = GetAgentEnv("MALDA_AGENT_RICH", "MALDA_RALPH_RICH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var lower = env.Trim().ToLowerInvariant();
            _agentRichCli = lower is not ("0" or "false" or "no" or "off");
        }
        else
        {
            _agentRichCli = true;
        }

        if (System.Console.IsOutputRedirected)
            _agentRichCli = false;

        _agentRichCliResolved = true;
        return _agentRichCli;
    }

    private static bool IsToolDetailFull()
    {
        var env = GetAgentEnv("MALDA_AGENT_TOOL_DETAIL", "MALDA_RALPH_TOOL_DETAIL");
        return string.Equals(env?.Trim(), "full", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLlmResponsePreviewFull()
    {
        var env = GetAgentEnv("MALDA_AGENT_LLM_PREVIEW", "MALDA_RALPH_LLM_PREVIEW");
        return string.Equals(env?.Trim(), "full", StringComparison.OrdinalIgnoreCase);
    }

    internal enum LlmThinkingMode
    {
        Off,
        Compact,
        Full
    }

    private static bool? _llmThinkingModeResolved;
    private static LlmThinkingMode _llmThinkingMode = LlmThinkingMode.Compact;

    internal static LlmThinkingMode GetLlmThinkingMode()
    {
        if (_llmThinkingModeResolved.HasValue)
            return _llmThinkingMode;

        var env = GetAgentEnv("MALDA_AGENT_LLM_THINKING", "MALDA_RALPH_LLM_THINKING");
        if (string.IsNullOrWhiteSpace(env))
        {
            _llmThinkingMode = LlmThinkingMode.Compact;
        }
        else
        {
            var lower = env.Trim().ToLowerInvariant();
            _llmThinkingMode = lower switch
            {
                "0" or "false" or "no" or "off" => LlmThinkingMode.Off,
                "full" => LlmThinkingMode.Full,
                _ => LlmThinkingMode.Compact
            };
        }

        _llmThinkingModeResolved = true;
        return _llmThinkingMode;
    }

    /// <summary>
    /// Picks text to show as LLM thinking: native reasoning field, or assistant content on tool-call rounds.
    /// </summary>
    internal static string? ExtractThinkingFromResponse(JsonObject jsonResponse, bool hasToolCalls)
    {
        var reasoningVal = jsonResponse.Get("reasoning", null);
        if (reasoningVal != null && reasoningVal.Type == ValueType.String)
        {
            var reasoning = reasoningVal.AsString().Trim();
            if (!string.IsNullOrEmpty(reasoning))
                return reasoning;
        }

        var contentVal = jsonResponse.Get("content", null);
        if (contentVal == null || contentVal.Type != ValueType.String)
            return null;

        var content = contentVal.AsString().Trim();
        if (string.IsNullOrEmpty(content))
            return null;

        if (hasToolCalls)
            return content;

        if (GetLlmThinkingMode() != LlmThinkingMode.Off)
            return content;

        return IsLlmResponsePreviewFull() ? content : null;
    }

    internal static string FormatThinkingPreview(string thinkingText, LlmThinkingMode mode)
    {
        thinkingText = thinkingText.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (mode == LlmThinkingMode.Full)
            return thinkingText;

        return TruncateForLog(CollapseThinkingToOneLine(thinkingText), 280);
    }

    private static string CollapseThinkingToOneLine(string thinkingText)
    {
        var oneLine = thinkingText.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (oneLine.Contains("  ", StringComparison.Ordinal))
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        return oneLine;
    }

    private void LogLlmThinking(string? thinkingText, bool hasToolCalls)
    {
        EnsureVerboseLoggingSetup();
        if (!_verboseLoggingEnabled)
            return;

        if (_llmStreamThinkingPrinted)
            return;

        var mode = GetLlmThinkingMode();
        if (mode == LlmThinkingMode.Off || string.IsNullOrWhiteSpace(thinkingText))
            return;

        thinkingText = thinkingText.Trim();
        var kind = hasToolCalls ? "planning" : "response";

        if (mode == LlmThinkingMode.Full)
        {
            var plainHeader = $"[think] round {_llmRound} ({kind}, {thinkingText.Length} chars):";
            if (IsAgentRichCli())
            {
                WriteVerboseLine(
                    plainHeader,
                    $"[italic dim][think][/] round {_llmRound} · {EscapeMarkup(kind)} ({thinkingText.Length} chars)");
            }
            else
            {
                WriteVerboseLine(plainHeader);
            }

            foreach (var line in thinkingText.Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = line.TrimEnd();
                if (trimmed.Length == 0)
                    continue;
                WriteVerboseLine(
                    "  " + trimmed,
                    IsAgentRichCli() ? $"  [dim]{EscapeMarkup(trimmed)}[/]" : null);
            }

            return;
        }

        var preview = FormatThinkingPreview(thinkingText, LlmThinkingMode.Compact);
        var plain = $"[think] round {_llmRound} ({kind}): {preview}";
        if (IsAgentRichCli())
        {
            WriteVerboseLine(
                plain,
                $"[italic dim][think][/] round {_llmRound} · {EscapeMarkup(kind)} · {EscapeMarkup(preview)}");
        }
        else
        {
            WriteVerboseLine(plain);
        }
    }

    private bool ShouldStreamThinkingToConsole()
    {
        EnsureVerboseLoggingSetup();
        return _verboseLoggingEnabled &&
               GetLlmThinkingMode() != LlmThinkingMode.Off &&
               LLMClientInstance.IsLlmStreamingEnabled();
    }

    private void WriteLlmStreamDelta(LlmStreamDelta delta)
    {
        if (delta.Kind != "content" && delta.Kind != "reasoning")
            return;

        if (string.IsNullOrEmpty(delta.Text))
            return;

        if (!_llmStreamHeaderPrinted)
        {
            var phasePrefix = !string.IsNullOrEmpty(_verbosePhaseLabel)
                ? $"[{_verbosePhaseLabel}] "
                : "";
            Console.Write(phasePrefix + $"[think] round {_llmRound}: ");
            _llmStreamHeaderPrinted = true;
            _llmStreamThinkingPrinted = true;
        }

        Console.Write(delta.Text);
        Console.Out.Flush();
    }

    private void FinishLlmStreamLine()
    {
        if (!_llmStreamHeaderPrinted)
            return;

        Console.WriteLine();
        Console.Out.Flush();
        _llmStreamHeaderPrinted = false;
    }

    private RuntimeValue ChatWithOptionalStreaming(Func<RuntimeValue> chatCall)
    {
        var useStreamDisplay = ShouldStreamThinkingToConsole();
        var promptHandler = AiPipelineHelpers.PromptRunStreamHandler;
        _llmStreamHeaderPrinted = false;
        _llmStreamThinkingPrinted = false;

        if (useStreamDisplay || promptHandler != null)
        {
            LLMClientInstance.StreamDeltaHandler = delta =>
            {
                promptHandler?.Invoke(delta);
                if (useStreamDisplay)
                    WriteLlmStreamDelta(delta);
            };
        }

        try
        {
            return chatCall();
        }
        finally
        {
            LLMClientInstance.StreamDeltaHandler = null;
            if (useStreamDisplay)
                FinishLlmStreamLine();
        }
    }

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
    
    public ConversationInstance() : base(null)
    {
        _systemPrompt = "";
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle method access - create a FunctionValue wrapper
        if (name == "addUserMessage" || name == "addAssistantMessage" || name == "addTool" || 
            name == "send" || name == "getMessages" || name == "clear" || name == "getHistory" ||
            name == "getFailedWriteTools" || name == "getEstimatedContextTokens" ||
            name == "setContextTrimHandoff")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on Conversation.");
    }
    
    public void Initialize(LLMClientInstance? client, LlamaCppClientInstance? llamaClient, LLMClientBridge.LLMClientBridgeInstance? bridgeClient, string systemPrompt, IInputProvider? inputProvider = null)
    {
        _client = client;
        _llamaClient = llamaClient;
        _bridgeClient = bridgeClient;
        _systemPrompt = systemPrompt;
        _inputProvider = inputProvider;
        _messages.Clear();
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            var systemMsg = new JsonObject();
            systemMsg.Set("role", RuntimeValue.String("system"));
            systemMsg.Set("content", RuntimeValue.String(systemPrompt));
            _messages.Add(RuntimeValue.Object(systemMsg));
        }
    }
    
    // Overload for backward compatibility
    public void Initialize(LLMClientInstance? client, string systemPrompt, IInputProvider? inputProvider = null)
    {
        Initialize(client, null, null, systemPrompt, inputProvider);
    }
    
    // Overload for backward compatibility with llamaClient
    public void Initialize(LLMClientInstance? client, LlamaCppClientInstance? llamaClient, string systemPrompt, IInputProvider? inputProvider = null)
    {
        Initialize(client, llamaClient, null, systemPrompt, inputProvider);
    }
    
    public void SetInputProvider(IInputProvider? inputProvider)
    {
        _inputProvider = inputProvider;
    }

    /// <summary>
    /// Sets the system prompt without clearing messages. Used when restoring from a trace replay.
    /// </summary>
    public void SetSystemPrompt(string systemPrompt)
    {
        _systemPrompt = systemPrompt ?? "";
    }

    /// <summary>
    /// Appends text to the system prompt without clearing messages.
    /// </summary>
    public void AppendToSystemPrompt(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        _systemPrompt = string.IsNullOrEmpty(_systemPrompt) ? text : _systemPrompt + text;
    }

    public RuntimeValue AddUserMessage(string content)
    {
        _llmRound = 0;
        ResetTurnUsage();
        _turnFailedWriteTools.Clear();
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String("user"));
        msg.Set("content", RuntimeValue.String(content));
        _messages.Add(RuntimeValue.Object(msg));
        
        // Record agent message in trace (if enabled)
        TraceManager.Record(
            TraceEventType.AgentMessage,
            new
            {
                role = "user",
                content,
                toolName = (string?)null,
                toolCallId = (string?)null
            },
            AgentName,
            SessionId);
        
        return RuntimeValue.Null();
    }
    
    public RuntimeValue AddAssistantMessage(string content)
    {
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String("assistant"));
        msg.Set("content", RuntimeValue.String(content));
        _messages.Add(RuntimeValue.Object(msg));
        
        // Record agent message in trace (if enabled)
        TraceManager.Record(
            TraceEventType.AgentMessage,
            new
            {
                role = "assistant",
                content,
                toolName = (string?)null,
                toolCallId = (string?)null
            },
            AgentName,
            SessionId);
        
        return RuntimeValue.Null();
    }
    
    public RuntimeValue AddTool(ToolInstance tool)
    {
        _tools[tool.Name] = tool;
        return RuntimeValue.Null();
    }
    
    public RuntimeValue Send(RuntimeValue? responseFormat = null, LlmRequestOverrides? overrides = null)
    {
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
    
    public RuntimeValue GetMessages()
    {
        return RuntimeValue.Array(new List<RuntimeValue>(_messages));
    }

    public RuntimeValue GetFailedWriteTools()
    {
        var items = new List<RuntimeValue>(_turnFailedWriteTools.Count);
        foreach (var failure in _turnFailedWriteTools)
        {
            var obj = new JsonObject();
            obj.Set("toolName", RuntimeValue.String(failure.ToolName));
            if (!string.IsNullOrEmpty(failure.Target))
                obj.Set("target", RuntimeValue.String(failure.Target));
            obj.Set("error", RuntimeValue.String(failure.Error));
            items.Add(RuntimeValue.Object(obj));
        }
        return RuntimeValue.Array(items);
    }
    
    public RuntimeValue Clear()
    {
        _messages.Clear();
        _turnFailedWriteTools.Clear();
        if (!string.IsNullOrEmpty(_systemPrompt))
        {
            var systemMsg = new JsonObject();
            systemMsg.Set("role", RuntimeValue.String("system"));
            systemMsg.Set("content", RuntimeValue.String(_systemPrompt));
            _messages.Add(RuntimeValue.Object(systemMsg));
        }
        return RuntimeValue.Null();
    }
    
    public RuntimeValue GetHistory()
    {
        var history = new StringBuilder();
        foreach (var msg in _messages)
        {
            if (msg.Type == ValueType.Object)
            {
                var msgObj = msg.AsObject();
                var role = GetStringProperty(msgObj, "role") ?? "unknown";
                var content = GetStringProperty(msgObj, "content") ?? "";
                history.AppendLine($"{role}: {content}");
            }
        }
        return RuntimeValue.String(history.ToString());
    }
    
    private RuntimeValue ExecuteSPLToolFunction(FunctionValue function, Interpreter interpreter, RuntimeValue arguments)
    {
        return ToolInstance.InvokeMaldaToolFunction(function, interpreter, arguments);
    }
    
    private RuntimeValue ExecuteTranspiledToolMethod(System.Reflection.MethodInfo method, RuntimeValue arguments)
    {
        try
        {
            // Extract arguments from the JSON object
            if (arguments.Type != ValueType.Object)
            {
                return RuntimeValue.String("Error: Tool arguments must be an object");
            }
            
            var argsObj = arguments.AsObject();
            var methodParams = method.GetParameters();
            var methodArgs = new List<object?>();
            
            // Map JSON object properties to method parameters
            foreach (var param in methodParams)
            {
                try
                {
                    var paramValue = argsObj.Get(param.Name ?? "", null);
                    if (paramValue == null || paramValue.Type == ValueType.Null)
                    {
                        // Parameter not provided - use default value or null
                        methodArgs.Add(param.HasDefaultValue ? param.DefaultValue : null);
                    }
                    else
                    {
                        // Convert RuntimeValue to appropriate type
                        var convertedValue = ConvertRuntimeValueToObject(paramValue, param.ParameterType);
                        methodArgs.Add(convertedValue);
                    }
                }
                catch
                {
                    // Parameter not found - use default value or null
                    methodArgs.Add(null);
                }
            }
            
            // Call the transpiled method (it returns Task<object>)
            var task = (Task<object>)method.Invoke(null, methodArgs.ToArray())!;
            var result = task.GetAwaiter().GetResult();
            
            // Convert result back to RuntimeValue
            return ConvertObjectToRuntimeValue(result);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing transpiled tool method: {ex.Message}");
        }
    }
    
    private object? ConvertRuntimeValueToObject(RuntimeValue value, Type targetType)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        
        return value.Type switch
        {
            ValueType.Integer => Convert.ChangeType(value.AsInteger(), underlyingType),
            ValueType.Float => Convert.ChangeType(value.AsFloat(), underlyingType),
            ValueType.String => value.AsString(),
            ValueType.Boolean => value.AsBoolean(),
            ValueType.Array => value.AsArray(),
            ValueType.Object => value.AsObject(),
            ValueType.Null => null,
            _ => null
        };
    }
    
    private RuntimeValue ConvertObjectToRuntimeValue(object? result)
    {
        return result switch
        {
            int i => RuntimeValue.Integer(i),
            long l => RuntimeValue.Integer((int)l),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            string s => RuntimeValue.String(s),
            bool b => RuntimeValue.Boolean(b),
            MaldaLang.Interpreter.ObjectInstance oi => RuntimeValue.Object(oi),
            List<object> list => RuntimeValue.Array(list.Select(ConvertObjectToRuntimeValue).ToList()),
            _ => RuntimeValue.Null()
        };
    }
    
    private string? GetStringProperty(ObjectInstance obj, string name)
    {
        try
        {
            var prop = obj.Get(name, null);
            return prop?.AsString();
        }
        catch
        {
            return null;
        }
    }
    
    private RuntimeValue? GetProperty(ObjectInstance obj, string name)
    {
        try
        {
            return obj.Get(name, null);
        }
        catch
        {
            return null;
        }
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "addUserMessage":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("addUserMessage() expects 1 string argument");
                return AddUserMessage(args[0].AsString());
            
            case "addAssistantMessage":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("addAssistantMessage() expects 1 string argument");
                return AddAssistantMessage(args[0].AsString());
            
            case "addTool":
                if (args.Count != 1 || args[0].Type != ValueType.Object)
                    throw new Exception("addTool() expects 1 Tool argument");
                var toolObj = args[0].AsObject();
                if (toolObj is not ToolInstance tool)
                    throw new Exception("addTool() expects a Tool instance");
                return AddTool(tool);
            
            case "send":
                return Send();
            
            case "getMessages":
                return GetMessages();

            case "getFailedWriteTools":
                return GetFailedWriteTools();
            
            case "clear":
                return Clear();
            
            case "getHistory":
                return GetHistory();

            case "getEstimatedContextTokens":
                return RuntimeValue.Integer(EstimateContextTokens());

            case "setContextTrimHandoff":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setContextTrimHandoff() expects 1 string argument");
                SetContextTrimHandoffNote(args[0].AsString());
                return RuntimeValue.Null();
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    private string SerializeRuntimeValueToJson(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.String:
                return JsonSerializer.Serialize(value.AsString());
            
            case ValueType.Integer:
                return value.AsInteger().ToString();
            
            case ValueType.Float:
                return value.AsFloat().ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            
            case ValueType.Boolean:
                return value.AsBoolean().ToString().ToLower();
            
            case ValueType.Null:
                return "null";
            
            case ValueType.Array:
                var arr = value.AsArray();
                var items = arr.Select(SerializeRuntimeValueToJson);
                return "[" + string.Join(",", items) + "]";
            
            case ValueType.Object:
                var obj = value.AsObject();
                if (obj is JsonObject jsonObj)
                {
                    var props = new List<string>();
                    foreach (var kvp in jsonObj.GetProperties())
                    {
                        var key = JsonSerializer.Serialize(kvp.Key);
                        var val = SerializeRuntimeValueToJson(kvp.Value);
                        props.Add($"{key}:{val}");
                    }
                    return "{" + string.Join(",", props) + "}";
                }
                // For regular ObjectInstance, return empty object for now
                return "{}";
            
            default:
                return JsonSerializer.Serialize($"<{value.Type}>");
        }
    }
    
    private RuntimeValue? TryExtractFromTruncatedJson(string json)
    {
        // Try to extract filePath and content from truncated JSON
        // This is a fallback for when JSON is truncated but we can still extract the values we need
        try
        {
            var result = new JsonObject();
            bool foundAny = false;
            
            // Try to extract filePath - it should be complete even if content is truncated
            var filePathMatch = System.Text.RegularExpressions.Regex.Match(
                json, 
                @"""filePath""\s*:\s*""((?:[^""\\]|\\.)*)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            if (filePathMatch.Success)
            {
                var filePath = UnescapeJsonString(filePathMatch.Groups[1].Value);
                result.Set("filePath", RuntimeValue.String(filePath));
                foundAny = true;
            }
            
            // Try to extract content - this might be truncated
            // Look for "content":" and extract everything after it
            var contentStartMatch = System.Text.RegularExpressions.Regex.Match(
                json,
                @"""content""\s*:\s*""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            if (contentStartMatch.Success)
            {
                // Extract everything after "content":"
                var contentStart = contentStartMatch.Index + contentStartMatch.Length;
                var contentRaw = json.Substring(contentStart);
                
                // Try to unescape the content, handling truncation
                var unescapedContent = UnescapeJsonStringTruncated(contentRaw);
                result.Set("content", RuntimeValue.String(unescapedContent));
                foundAny = true;
            }
            
            return foundAny ? RuntimeValue.Object(result) : null;
        }
        catch
        {
            return null;
        }
    }
    
    private string TryFixJsonEscapeSequences(string json)
    {
        // This method attempts to fix common JSON escape sequence issues
        // It's conservative - only fixes clearly malformed sequences, not truncated ones
        var result = new StringBuilder();
        bool inString = false;
        bool isEscaped = false;
        
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            
            if (!inString)
            {
                result.Append(c);
                if (c == '"')
                {
                    inString = true;
                }
                continue;
            }
            
            // We're inside a string
            if (isEscaped)
            {
                if (c == 'u')
                {
                    // Unicode escape sequence: \uXXXX
                    if (i + 4 < json.Length)
                    {
                        // Complete unicode escape - check if valid
                        var hex = json.Substring(i + 1, 4);
                        if (System.Text.RegularExpressions.Regex.IsMatch(hex, @"^[0-9A-Fa-f]{4}$"))
                        {
                            // Valid unicode escape - keep as is
                            result.Append('\\').Append('u').Append(hex);
                            i += 4;
                        }
                        else
                        {
                            // Invalid hex digits - replace with null character escape
                            result.Append('\\').Append('u').Append("0000");
                            i += 4;
                        }
                    }
                    else
                    {
                        // Truncated unicode escape - replace with a safe character (space)
                        // This prevents JSON parse errors while indicating data loss
                        result.Append(' ');
                        var remaining = json.Length - i - 1;
                        if (remaining > 0)
                        {
                            i += remaining;
                        }
                    }
                    isEscaped = false;
                }
                else if (c == '\\' || c == '"' || c == '/' || c == 'b' || c == 'f' || c == 'n' || c == 'r' || c == 't')
                {
                    // Valid escape sequences
                    result.Append('\\').Append(c);
                    isEscaped = false;
                }
                else
                {
                    // Unknown escape sequence - keep as is (might be valid in some contexts)
                    result.Append('\\').Append(c);
                    isEscaped = false;
                }
            }
            else if (c == '\\')
            {
                isEscaped = true;
            }
            else if (c == '"')
            {
                result.Append(c);
                inString = false;
            }
            else
            {
                result.Append(c);
            }
        }
        
        // If we ended in the middle of an escape sequence, complete it safely
        if (isEscaped)
        {
            result.Append('\\');
        }
        
        return result.ToString();
    }
    
    private string UnescapeJsonString(string escaped)
    {
        var result = new StringBuilder();
        bool isEscaped = false;
        for (int i = 0; i < escaped.Length; i++)
        {
            char c = escaped[i];
            if (isEscaped)
            {
                switch (c)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        // Unicode escape: \uXXXX
                        if (i + 4 < escaped.Length)
                        {
                            var hex = escaped.Substring(i + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                result.Append((char)code);
                                i += 4;
                            }
                            else
                            {
                                result.Append('\\').Append('u').Append(hex);
                                i += 4;
                            }
                        }
                        else
                        {
                            result.Append('\\').Append(c);
                        }
                        break;
                    default: result.Append('\\').Append(c); break;
                }
                isEscaped = false;
            }
            else if (c == '\\')
            {
                isEscaped = true;
            }
            else if (c == '"')
            {
                // End of string
                break;
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
    
    private string UnescapeJsonStringTruncated(string escaped)
    {
        // Same as UnescapeJsonString but doesn't stop at closing quote (for truncated strings)
        var result = new StringBuilder();
        bool isEscaped = false;
        for (int i = 0; i < escaped.Length; i++)
        {
            char c = escaped[i];
            if (isEscaped)
            {
                switch (c)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        // Unicode escape: \uXXXX
                        if (i + 4 < escaped.Length)
                        {
                            var hex = escaped.Substring(i + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                result.Append((char)code);
                                i += 4;
                            }
                            else
                            {
                                result.Append('\\').Append('u').Append(hex);
                                i += 4;
                            }
                        }
                        else
                        {
                            // Truncated unicode escape - just append what we have
                            result.Append('\\').Append(c);
                        }
                        break;
                    default: result.Append('\\').Append(c); break;
                }
                isEscaped = false;
            }
            else if (c == '\\')
            {
                isEscaped = true;
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
    
    private RuntimeValue JsonToRuntimeValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var jsonObj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    jsonObj.Set(prop.Name, JsonToRuntimeValue(prop.Value));
                }
                return RuntimeValue.Object(jsonObj);
            
            case JsonValueKind.Array:
                var arr = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(JsonToRuntimeValue(item));
                }
                return RuntimeValue.Array(arr);
            
            case JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return RuntimeValue.Integer(intVal);
                return RuntimeValue.Float(element.GetDouble());
            
            case JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            
            case JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            
            case JsonValueKind.Null:
                return RuntimeValue.Null();
            
            default:
                return RuntimeValue.Null();
        }
    }
    
    private RuntimeValue ExecuteToolOperation(ToolInstance tool, RuntimeValue arguments)
    {
        try
        {
            // Check if tool is an AgentToolInstance (agent wrapped as tool)
            if (tool is AgentToolInstance agentTool)
            {
                // Execute agent tool directly
                return agentTool.Execute(arguments, null);
            }
            
            if (tool is MemoryProgressToolInstance memoryProgressTool)
            {
                return memoryProgressTool.ExecuteMemoryTool(arguments);
            }

            if (tool is DevAgentCodeMemoryToolInstance codeMemoryTool)
            {
                return codeMemoryTool.ExecuteCodeMemoryTool(arguments);
            }
            
            // Check if tool has a transpiled method handler
            var transpiledMethod = tool.GetTranspiledMethod();
            if (transpiledMethod != null)
            {
                return ExecuteTranspiledToolMethod(transpiledMethod, arguments);
            }
            
            // Check if tool has a MALDA function handler
            var functionHandler = tool.GetFunctionHandler();
            var interpreter = tool.GetInterpreter();
            
            if (functionHandler != null && interpreter != null)
            {
                return ExecuteSPLToolFunction(functionHandler, interpreter, arguments);
            }
            
            // Extract arguments
            if (arguments.Type != ValueType.Object)
                return RuntimeValue.String("Error: Tool arguments must be an object");
            
            var argsObj = arguments.AsObject();
            
            // Validate path if tool has a working directory
            string? filePath = null;
            string? dirPath = null;
            
            try
            {
                var filePathVal = argsObj.Get("filePath", null);
                if (filePathVal != null && filePathVal.Type == ValueType.String)
                    filePath = filePathVal.AsString();
            }
            catch { }
            
            try
            {
                var dirPathVal = argsObj.Get("dirPath", null);
                if (dirPathVal != null && dirPathVal.Type == ValueType.String)
                    dirPath = dirPathVal.AsString();
            }
            catch { }
            
            var pathToCheck = filePath ?? dirPath;
            if (pathToCheck != null && !string.IsNullOrEmpty(tool.WorkingDirectory))
            {
                var normalizedPath = tool.NormalizePathForWorkingDirectory(pathToCheck);
                if (normalizedPath == null)
                {
                    return RuntimeValue.String($"Error: Path '{pathToCheck}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \"PRD.md\", \"snake.html\").");
                }
                if (filePath != null)
                    filePath = normalizedPath;
                else
                    dirPath = normalizedPath;
                pathToCheck = normalizedPath;
            }
            else if (pathToCheck != null && !tool.IsPathAllowed(pathToCheck))
            {
                return RuntimeValue.String($"Error: Path '{pathToCheck}' is outside the allowed working directory '{tool.WorkingDirectory}'");
            }
            
            // Resolve relative paths relative to working directory (disk or embed:)
            string? resolvedFilePath = null;
            string? resolvedDirPath = null;
            
            if (filePath != null && !string.IsNullOrEmpty(tool.WorkingDirectory))
            {
                resolvedFilePath = tool.ResolvePathAgainstWorkingDirectory(filePath) ?? filePath;
            }
            else if (filePath != null)
            {
                resolvedFilePath = filePath;
            }
            
            if (dirPath != null && !string.IsNullOrEmpty(tool.WorkingDirectory))
            {
                resolvedDirPath = tool.ResolvePathAgainstWorkingDirectory(dirPath) ?? dirPath;
            }
            else if (dirPath != null)
            {
                resolvedDirPath = dirPath;
            }
            
            // Execute the appropriate built-in function based on tool name
            switch (tool.Name)
            {
                case "read_file":
                    if (resolvedFilePath == null)
                        return RuntimeValue.String("Error: filePath parameter required");
                    
                    var readArgs = new List<RuntimeValue> { RuntimeValue.String(resolvedFilePath) };
                    
                    // Check for optional line range parameters
                    try
                    {
                        var startLineVal = argsObj.Get("startLine", null);
                        if (startLineVal != null && startLineVal.Type == ValueType.Integer)
                        {
                            readArgs.Add(startLineVal);
                            
                            var endLineVal = argsObj.Get("endLine", null);
                            if (endLineVal != null && endLineVal.Type == ValueType.Integer)
                            {
                                readArgs.Add(endLineVal);
                            }
                        }
                    }
                    catch { }
                    
                    return BuiltInFunctions.CallBuiltIn("readFile", readArgs, null);
                
                case "write_file":
                    if (resolvedFilePath == null)
                        return RuntimeValue.String("Error: filePath parameter required");
                    try
                    {
                        var contentVal = argsObj.Get("content", null);
                        if (contentVal == null || contentVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: content parameter required");
                        
                        var content = contentVal.AsString();
                        const int maxContentLength = 50000;
                        if (content.Length > maxContentLength)
                        {
                            return RuntimeValue.String($"Error: Content length ({content.Length} characters) exceeds maximum allowed length ({maxContentLength} characters). Please split the content into smaller chunks or use replace_in_file for partial updates.");
                        }
                        
                        return BuiltInFunctions.CallBuiltIn("writeFile", new List<RuntimeValue> 
                        { 
                            RuntimeValue.String(resolvedFilePath), 
                            contentVal 
                        }, null);
                    }
                    catch
                    {
                        return RuntimeValue.String("Error: content parameter required");
                    }
                
                case "replace_in_file":
                    if (resolvedFilePath == null)
                        return RuntimeValue.String("Error: filePath parameter required");
                    try
                    {
                        var oldTextVal = argsObj.Get("oldText", null);
                        var newTextVal = argsObj.Get("newText", null);
                        var contextLinesVal = argsObj.Get("contextLines", null);
                        
                        if (oldTextVal == null || oldTextVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: oldText parameter required");
                        if (newTextVal == null || newTextVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: newText parameter required");
                        
                        var oldText = oldTextVal.AsString();
                        var newText = newTextVal.AsString();
                        
                        // Validate against LLM hallucinations: detect excessive consecutive newlines
                        // This catches cases where LLM generates excessive \u000A sequences that get converted to \n
                        const int maxConsecutiveNewlines = 10;
                        var consecutiveNewlines = 0;
                        var maxConsecutiveFound = 0;
                        for (int i = 0; i < oldText.Length; i++)
                        {
                            // This will catch both \n (LF), \r (CR), and \r\n (CRLF)
                            // All Unicode escape sequences like \u000A have already been converted to \n by JSON parser
                            if (oldText[i] == '\n' || oldText[i] == '\r')
                            {
                                consecutiveNewlines++;
                                maxConsecutiveFound = Math.Max(maxConsecutiveFound, consecutiveNewlines);
                            }
                            else if (oldText[i] != ' ' && oldText[i] != '\t')
                            {
                                // Reset counter on non-whitespace character
                                consecutiveNewlines = 0;
                            }
                        }
                        
                        if (maxConsecutiveFound > maxConsecutiveNewlines)
                        {
                            return RuntimeValue.String($"Error: oldText contains {maxConsecutiveFound} consecutive newlines, which exceeds the maximum allowed ({maxConsecutiveNewlines}). " +
                                $"This appears to be an error. oldText should contain actual code/text to replace, not just whitespace. " +
                                $"Please provide the actual text content to replace, not just empty lines.");
                        }
                        
                        // Check if oldText is mostly whitespace (another sign of hallucination)
                        var nonWhitespaceChars = 0;
                        foreach (var c in oldText)
                        {
                            if (!char.IsWhiteSpace(c))
                                nonWhitespaceChars++;
                        }
                        if (oldText.Length > 100 && nonWhitespaceChars < oldText.Length * 0.1)
                        {
                            return RuntimeValue.String($"Error: oldText is mostly whitespace ({nonWhitespaceChars} non-whitespace characters out of {oldText.Length} total). " +
                                $"oldText should contain actual code/text content to replace. Please provide the actual text, not just whitespace or newlines.");
                        }
                        
                        // Validate content lengths
                        const int maxContentLength = 50000;
                        if (oldText.Length > maxContentLength)
                        {
                            return RuntimeValue.String($"Error: oldText length ({oldText.Length} characters) exceeds maximum allowed length ({maxContentLength} characters).");
                        }
                        if (newText.Length > maxContentLength)
                        {
                            return RuntimeValue.String($"Error: newText length ({newText.Length} characters) exceeds maximum allowed length ({maxContentLength} characters). Please split the replacement into smaller chunks.");
                        }
                        
                        var args = new List<RuntimeValue>
                        {
                            RuntimeValue.String(resolvedFilePath),
                            oldTextVal,
                            newTextVal
                        };
                        
                        if (contextLinesVal != null && contextLinesVal.Type == ValueType.Integer)
                            args.Add(contextLinesVal);
                        else
                            args.Add(RuntimeValue.Integer(3));
                        
                        var result = BuiltInFunctions.CallBuiltIn("replaceInFile", args, null);
                        
                        // If the result is false, provide a descriptive error message for the LLM
                        if (result.Type == ValueType.Boolean && !result.AsBoolean())
                        {
                            // Check if file exists to provide more specific error
                            if (!System.IO.File.Exists(resolvedFilePath))
                            {
                                return RuntimeValue.String($"Error: File not found: '{resolvedFilePath}'. Cannot perform replacement.");
                            }
                            
                            // Provide helpful error message explaining why replacement failed
                            var oldTextPreview = oldText.Length > 50 ? oldText.Substring(0, 50) + "..." : oldText;
                            // Escape special characters for display
                            var escapedOldText = oldTextPreview.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
                            
                            return RuntimeValue.String($"Error: The text to replace was not found in the file '{resolvedFilePath}'. " +
                                $"The oldText '{escapedOldText}' does not exist in the file (after whitespace normalization). " +
                                $"Please verify that the text exists in the file or check for encoding/whitespace differences.");
                        }
                        
                        return result;
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error: {ex.Message}");
                    }
                
                case "edit_file":
                    if (resolvedFilePath == null)
                        return RuntimeValue.String("Error: filePath parameter required");
                    try
                    {
                        var editsVal = argsObj.Get("edits", null);
                        if (editsVal == null || editsVal.Type != ValueType.Array)
                            return RuntimeValue.String("Error: edits parameter required and must be an array");
                        
                        var editsArray = editsVal.AsArray();
                        if (editsArray.Count == 0)
                        {
                            // Empty edits array - return success with applied=0
                            var resultObj = new JsonObject();
                            resultObj.Set("success", RuntimeValue.Boolean(true));
                            resultObj.Set("applied", RuntimeValue.Integer(0));
                            return RuntimeValue.Object(resultObj);
                        }
                        
                        // Convert edits array to the format expected by editFile()
                        var editList = new List<RuntimeValue>();
                        foreach (var editValue in editsArray)
                        {
                            if (editValue.Type != ValueType.Object)
                                continue;
                            
                            var editObj = editValue.AsObject();
                            RuntimeValue? oldTextVal = null;
                            RuntimeValue? newTextVal = null;
                            RuntimeValue? contextLinesVal = null;
                            
                            try { oldTextVal = editObj.Get("oldText", null); } catch { }
                            try { newTextVal = editObj.Get("newText", null); } catch { }
                            try { contextLinesVal = editObj.Get("contextLines", null); } catch { }
                            
                            if (oldTextVal == null || oldTextVal.Type != ValueType.String)
                                return RuntimeValue.String("Error: Each edit must have 'oldText' (string) property");
                            if (newTextVal == null || newTextVal.Type != ValueType.String)
                                return RuntimeValue.String("Error: Each edit must have 'newText' (string) property");
                            
                            var oldText = oldTextVal.AsString();
                            var newText = newTextVal.AsString();
                            
                            // Apply same validation as replace_in_file
                            const int maxConsecutiveNewlines = 10;
                            var consecutiveNewlines = 0;
                            var maxConsecutiveFound = 0;
                            for (int i = 0; i < oldText.Length; i++)
                            {
                                if (oldText[i] == '\n' || oldText[i] == '\r')
                                {
                                    consecutiveNewlines++;
                                    maxConsecutiveFound = Math.Max(maxConsecutiveFound, consecutiveNewlines);
                                }
                                else if (oldText[i] != ' ' && oldText[i] != '\t')
                                {
                                    consecutiveNewlines = 0;
                                }
                            }
                            
                            if (maxConsecutiveFound > maxConsecutiveNewlines)
                            {
                                return RuntimeValue.String($"Error: oldText in one of the edits contains {maxConsecutiveFound} consecutive newlines, which exceeds the maximum allowed ({maxConsecutiveNewlines}). oldText should contain actual code/text to replace, not just whitespace.");
                            }
                            
                            // Check if oldText is mostly whitespace
                            var nonWhitespaceChars = 0;
                            foreach (var c in oldText)
                            {
                                if (!char.IsWhiteSpace(c))
                                    nonWhitespaceChars++;
                            }
                            if (oldText.Length > 100 && nonWhitespaceChars < oldText.Length * 0.1)
                            {
                                return RuntimeValue.String($"Error: oldText in one of the edits is mostly whitespace ({nonWhitespaceChars} non-whitespace characters out of {oldText.Length} total). oldText should contain actual code/text content to replace.");
                            }
                            
                            // Validate content lengths
                            const int maxContentLength = 50000;
                            if (oldText.Length > maxContentLength)
                            {
                                return RuntimeValue.String($"Error: oldText length ({oldText.Length} characters) in one of the edits exceeds maximum allowed length ({maxContentLength} characters).");
                            }
                            if (newText.Length > maxContentLength)
                            {
                                return RuntimeValue.String($"Error: newText length ({newText.Length} characters) in one of the edits exceeds maximum allowed length ({maxContentLength} characters). Please split the edits into smaller chunks.");
                            }
                            
                            // Create edit object
                            var editObjResult = new JsonObject();
                            editObjResult.Set("oldText", oldTextVal);
                            editObjResult.Set("newText", newTextVal);
                            if (contextLinesVal != null && contextLinesVal.Type == ValueType.Integer)
                                editObjResult.Set("contextLines", contextLinesVal);
                            else
                                editObjResult.Set("contextLines", RuntimeValue.Integer(3));
                            
                            editList.Add(RuntimeValue.Object(editObjResult));
                        }
                        
                        // Call editFile built-in function
                        var editFileArgs = new List<RuntimeValue>
                        {
                            RuntimeValue.String(resolvedFilePath),
                            RuntimeValue.Array(editList)
                        };
                        
                        var editFileResult = BuiltInFunctions.CallBuiltIn("editFile", editFileArgs, null);
                        
                        // editFile returns {success: boolean, applied: integer}
                        return editFileResult;
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing edit_file tool: {ex.Message}");
                    }
                
                case "list_directory":
                    if (resolvedDirPath == null)
                        return RuntimeValue.String("Error: dirPath parameter required");
                    return BuiltInFunctions.CallBuiltIn("listDirectory", new List<RuntimeValue> { RuntimeValue.String(resolvedDirPath) }, null);
                
                case "insertAtLine":
                    try
                    {
                        var filePathVal = argsObj.Get("filePath", null);
                        var lineNumberVal = argsObj.Get("lineNumber", null);
                        var contentVal = argsObj.Get("content", null);
                        
                        if (filePathVal == null || filePathVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: filePath parameter required");
                        if (lineNumberVal == null || lineNumberVal.Type != ValueType.Integer)
                            return RuntimeValue.String("Error: lineNumber parameter required");
                        if (contentVal == null || contentVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: content parameter required");
                        
                        var localFilePath = filePathVal.AsString();
                        var lineNumber = lineNumberVal.AsInteger();
                        var content = contentVal.AsString();
                        
                        // Validate content length
                        const int maxContentLength = 50000;
                        if (content.Length > maxContentLength)
                        {
                            return RuntimeValue.String($"Error: Content length ({content.Length} characters) exceeds maximum allowed length ({maxContentLength} characters). Please split the content into smaller chunks.");
                        }
                        
                        if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                        {
                            var normalizedLocalPath = tool.NormalizePathForWorkingDirectory(localFilePath);
                            if (normalizedLocalPath == null)
                            {
                                return RuntimeValue.String($"Error: Path '{localFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \"PRD.md\", \"snake.html\").");
                            }
                            localFilePath = normalizedLocalPath;
                        }
                        else if (!tool.IsPathAllowed(localFilePath))
                        {
                            return RuntimeValue.String($"Error: Path '{localFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                        }
                        
                        // Resolve path relative to working directory
                        string localResolvedFilePath;
                        if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                        {
                            localResolvedFilePath = Path.Combine(tool.WorkingDirectory, localFilePath);
                        }
                        else
                        {
                            localResolvedFilePath = localFilePath;
                        }
                        
                        // Extract optional insertAfter parameter
                        var insertAfter = false;
                        try
                        {
                            var insertAfterVal = argsObj.Get("insertAfter", null);
                            if (insertAfterVal != null && insertAfterVal.Type == ValueType.Boolean)
                                insertAfter = insertAfterVal.AsBoolean();
                        }
                        catch { }
                        
                        // Build arguments list for BuiltInInsertAtLine
                        var insertArgs = new List<RuntimeValue>
                        {
                            RuntimeValue.String(localResolvedFilePath),
                            RuntimeValue.Integer(lineNumber),
                            RuntimeValue.String(content),
                            RuntimeValue.Boolean(insertAfter)
                        };
                        
                        var result = BuiltInFunctions.CallBuiltIn("insertAtLine", insertArgs, null);
                        
                        // Return success message or error
                        if (result.Type == ValueType.Boolean && result.AsBoolean())
                        {
                            return RuntimeValue.String("Success: Content inserted at line " + lineNumber);
                        }
                        else
                        {
                            return RuntimeValue.String("Error: Failed to insert content. File may not exist or there was an error writing to the file.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing insertAtLine tool: {ex.Message}");
                    }
                
                case "ask_user":
                    try
                    {
                        var questionVal = argsObj.Get("question", null);
                        if (questionVal == null || questionVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: question parameter required");
                        
                        var question = questionVal.AsString();
                        
                        // Try to get input from input provider if available
                        if (_inputProvider != null)
                        {
                            // Check for queued input first
                            if (_inputProvider.HasQueuedInput())
                            {
                                var queuedInput = _inputProvider.GetQueuedInput();
                                return RuntimeValue.String(queuedInput);
                            }
                            
                            // No queued input - use GetInputAsync to request input from UI
                            // Since ExecuteToolOperation is synchronous, we need to block on the async call
                            // This is safe because execution happens in a background thread (Task.Run)
                            try
                            {
                                var inputTask = _inputProvider.GetInputAsync(question);
                                var input = inputTask.GetAwaiter().GetResult();
                                return RuntimeValue.String(input ?? "");
                            }
                            catch (Exception asyncEx)
                            {
                                // If async input fails, fall back to exception-based approach for compatibility
                                Console.WriteLine(question);
                                Console.WriteLine("(Waiting for input...)");
                                throw new InputRequiredException(question);
                            }
                        }
                        
                        // Fallback to console input
                        Console.Write(question);
                        var consoleInput = Console.ReadLine() ?? "";
                        return RuntimeValue.String(consoleInput);
                    }
                    catch (InputRequiredException)
                    {
                        // Re-throw to let execution service handle it
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error asking user: {ex.Message}");
                    }
                
                case "glob":
                    try
                    {
                        var globPatternVal = argsObj.Get("pattern", null);
                        if (globPatternVal == null || globPatternVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: pattern parameter required");

                        var globPattern = globPatternVal.AsString();
                        var globDirPath = ".";

                        var globDirPathVal = argsObj.Get("dirPath", null);
                        if (globDirPathVal != null && globDirPathVal.Type == ValueType.String)
                            globDirPath = globDirPathVal.AsString();

                        if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                        {
                            var normalizedGlobDir = tool.NormalizePathForWorkingDirectory(globDirPath);
                            if (normalizedGlobDir == null)
                            {
                                return RuntimeValue.String($"Error: Path '{globDirPath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \".\", \"src\").");
                            }
                            globDirPath = normalizedGlobDir;
                        }
                        else if (!tool.IsPathAllowed(globDirPath))
                        {
                            return RuntimeValue.String($"Error: Path '{globDirPath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                        }

                        var globMaxResults = GlobHelper.DefaultMaxResults;
                        try
                        {
                            var maxResultsVal = argsObj.Get("maxResults", null);
                            if (maxResultsVal != null && maxResultsVal.Type == ValueType.Integer)
                                globMaxResults = maxResultsVal.AsInteger();
                        }
                        catch { }

                        var globIncludeDirectories = false;
                        try
                        {
                            var includeDirsVal = argsObj.Get("includeDirectories", null);
                            if (includeDirsVal != null && includeDirsVal.Type == ValueType.Boolean)
                                globIncludeDirectories = includeDirsVal.AsBoolean();
                        }
                        catch { }

                        var globExcludeDirs = "";
                        try
                        {
                            var excludeDirsVal = argsObj.Get("excludeDirs", null);
                            if (excludeDirsVal != null && excludeDirsVal.Type == ValueType.String)
                                globExcludeDirs = excludeDirsVal.AsString();
                        }
                        catch { }

                        var globArgs = new List<RuntimeValue>
                        {
                            RuntimeValue.String(globPattern),
                            RuntimeValue.String(globDirPath),
                            RuntimeValue.Integer(globMaxResults),
                            RuntimeValue.Boolean(globIncludeDirectories),
                            RuntimeValue.String(globExcludeDirs),
                            RuntimeValue.String(tool.WorkingDirectory ?? "")
                        };

                        return BuiltInFunctions.CallBuiltIn("glob", globArgs, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing glob tool: {ex.Message}");
                    }

                case "grep":
                    try
                    {
                        var patternVal = argsObj.Get("pattern", null);
                        var filePathVal = argsObj.Get("filePath", null) ?? argsObj.Get("file_path", null);
                        
                        if (patternVal == null || patternVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: pattern parameter required");
                        if (filePathVal == null || filePathVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: filePath parameter required. Example: use filePath: \"MALDA_SPEC.md\" to search the language spec.");
                        
                        var pattern = patternVal.AsString();
                        var localFilePath = filePathVal.AsString();
                        
                        if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                        {
                            var normalizedGrepPath = tool.NormalizePathForWorkingDirectory(localFilePath);
                            if (normalizedGrepPath == null)
                            {
                                return RuntimeValue.String($"Error: Path '{localFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \"PRD.md\", \"snake.html\").");
                            }
                            localFilePath = normalizedGrepPath;
                        }
                        else if (!tool.IsPathAllowed(localFilePath))
                        {
                            return RuntimeValue.String($"Error: Path '{localFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                        }
                        
                        // If pattern contains "|" (alternation) it is intended as regex; default useRegex to true so it matches
                        var useRegexFromArgs = argsObj.Get("useRegex", null);
                        var useRegexExplicit = useRegexFromArgs != null && useRegexFromArgs.Type == ValueType.Boolean;
                        
                        // Resolve path relative to working directory (disk or embed:)
                        string localResolvedFilePath;
                        if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                        {
                            localResolvedFilePath = tool.ResolvePathAgainstWorkingDirectory(localFilePath) ?? localFilePath;
                        }
                        else
                        {
                            localResolvedFilePath = localFilePath;
                        }
                        
                        // Extract optional parameters
                        var useRegex = false;
                        var caseInsensitive = false;
                        var includeLineNumbers = true;
                        var contextLines = 3;
                        var countOnly = false;
                        var recursive = true;
                        
                        try
                        {
                            var useRegexVal = argsObj.Get("useRegex", null);
                            if (useRegexVal != null && useRegexVal.Type == ValueType.Boolean)
                                useRegex = useRegexVal.AsBoolean();
                        }
                        catch { }
                        
                        // Pattern with "|" is alternation (e.g. "FUNCTIONS|function"); treat as regex so it matches
                        if (!useRegexExplicit && pattern.Contains("|"))
                            useRegex = true;
                        
                        try
                        {
                            var caseInsensitiveVal = argsObj.Get("caseInsensitive", null);
                            if (caseInsensitiveVal != null && caseInsensitiveVal.Type == ValueType.Boolean)
                                caseInsensitive = caseInsensitiveVal.AsBoolean();
                        }
                        catch { }
                        
                        try
                        {
                            var includeLineNumbersVal = argsObj.Get("includeLineNumbers", null);
                            if (includeLineNumbersVal != null && includeLineNumbersVal.Type == ValueType.Boolean)
                                includeLineNumbers = includeLineNumbersVal.AsBoolean();
                        }
                        catch { }
                        
                        try
                        {
                            var contextLinesVal = argsObj.Get("contextLines", null);
                            if (contextLinesVal != null && contextLinesVal.Type == ValueType.Integer)
                                contextLines = contextLinesVal.AsInteger();
                        }
                        catch { }
                        
                        try
                        {
                            var countOnlyVal = argsObj.Get("countOnly", null);
                            if (countOnlyVal != null && countOnlyVal.Type == ValueType.Boolean)
                                countOnly = countOnlyVal.AsBoolean();
                        }
                        catch { }
                        
                        try
                        {
                            var recursiveVal = argsObj.Get("recursive", null);
                            if (recursiveVal != null && recursiveVal.Type == ValueType.Boolean)
                                recursive = recursiveVal.AsBoolean();
                        }
                        catch { }
                        
                        // Build arguments list for BuiltInGrep (9th arg = workingDirectory so grep returns relative paths in results)
                        var grepArgs = new List<RuntimeValue>
                        {
                            RuntimeValue.String(pattern),
                            RuntimeValue.String(localResolvedFilePath),
                            RuntimeValue.Boolean(useRegex),
                            RuntimeValue.Boolean(caseInsensitive),
                            RuntimeValue.Boolean(includeLineNumbers),
                            RuntimeValue.Integer(contextLines),
                            RuntimeValue.Boolean(countOnly),
                            RuntimeValue.Boolean(recursive),
                            RuntimeValue.String(tool.WorkingDirectory ?? "")
                        };
                        
                        return BuiltInFunctions.CallBuiltIn("grep", grepArgs, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing grep tool: {ex.Message}");
                    }
                
                case "git_status":
                    try
                    {
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        return BuiltInFunctions.CallBuiltIn("gitStatus", new List<RuntimeValue> { RuntimeValue.String(repoPath) }, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_status tool: {ex.Message}");
                    }
                
                case "git_add":
                    try
                    {
                        var filesVal = argsObj.Get("files", null);
                        if (filesVal == null || filesVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: files parameter required");
                        
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        return BuiltInFunctions.CallBuiltIn("gitAdd", new List<RuntimeValue> 
                        { 
                            RuntimeValue.String(repoPath),
                            filesVal
                        }, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_add tool: {ex.Message}");
                    }
                
                case "git_commit":
                    try
                    {
                        var messageVal = argsObj.Get("message", null);
                        if (messageVal == null || messageVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: message parameter required");
                        
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        return BuiltInFunctions.CallBuiltIn("gitCommit", new List<RuntimeValue> 
                        { 
                            RuntimeValue.String(repoPath),
                            messageVal
                        }, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_commit tool: {ex.Message}");
                    }
                
                case "git_log":
                    try
                    {
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        var countVal = argsObj.Get("count", null);
                        var count = countVal != null && countVal.Type == ValueType.Integer 
                            ? countVal.AsInteger() 
                            : 10;
                        
                        return BuiltInFunctions.CallBuiltIn("gitLog", new List<RuntimeValue> 
                        { 
                            RuntimeValue.String(repoPath),
                            RuntimeValue.Integer(count)
                        }, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_log tool: {ex.Message}");
                    }
                
                case "git_diff":
                    try
                    {
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        var filePathVal = argsObj.Get("filePath", null);
                        var stagedVal = argsObj.Get("staged", null);
                        var staged = stagedVal != null && stagedVal.Type == ValueType.Boolean 
                            ? stagedVal.AsBoolean() 
                            : false;
                        
                        var args = new List<RuntimeValue> { RuntimeValue.String(repoPath) };
                        if (filePathVal != null && filePathVal.Type == ValueType.String)
                            args.Add(filePathVal);
                        else
                            args.Add(RuntimeValue.Null());
                        args.Add(RuntimeValue.Boolean(staged));
                        
                        return BuiltInFunctions.CallBuiltIn("gitDiff", args, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_diff tool: {ex.Message}");
                    }
                
                case "git_branch":
                    try
                    {
                        var actionVal = argsObj.Get("action", null);
                        if (actionVal == null || actionVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: action parameter required");
                        
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        var args = new List<RuntimeValue> { RuntimeValue.String(repoPath), actionVal };
                        
                        var branchNameVal = argsObj.Get("branchName", null);
                        if (branchNameVal != null && branchNameVal.Type == ValueType.String)
                            args.Add(branchNameVal);
                        
                        return BuiltInFunctions.CallBuiltIn("gitBranch", args, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_branch tool: {ex.Message}");
                    }
                
                case "git_checkout":
                    try
                    {
                        var branchNameVal = argsObj.Get("branchName", null);
                        if (branchNameVal == null || branchNameVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: branchName parameter required");
                        
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        var createVal = argsObj.Get("create", null);
                        var create = createVal != null && createVal.Type == ValueType.Boolean 
                            ? createVal.AsBoolean() 
                            : false;
                        
                        return BuiltInFunctions.CallBuiltIn("gitCheckout", new List<RuntimeValue> 
                        { 
                            RuntimeValue.String(repoPath),
                            branchNameVal,
                            RuntimeValue.Boolean(create)
                        }, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_checkout tool: {ex.Message}");
                    }
                
                case "git_push":
                    try
                    {
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        var remoteVal = argsObj.Get("remote", null);
                        var remote = remoteVal != null && remoteVal.Type == ValueType.String 
                            ? remoteVal.AsString() 
                            : "origin";
                        
                        var branchVal = argsObj.Get("branch", null);
                        
                        var args = new List<RuntimeValue> { RuntimeValue.String(repoPath), RuntimeValue.String(remote) };
                        if (branchVal != null && branchVal.Type == ValueType.String)
                            args.Add(branchVal);
                        
                        return BuiltInFunctions.CallBuiltIn("gitPush", args, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_push tool: {ex.Message}");
                    }
                
                case "git_pull":
                    try
                    {
                        var repoPathVal = argsObj.Get("repoPath", null);
                        var repoPath = repoPathVal != null && repoPathVal.Type == ValueType.String 
                            ? repoPathVal.AsString() 
                            : (!string.IsNullOrEmpty(tool.WorkingDirectory) ? tool.WorkingDirectory : Directory.GetCurrentDirectory());
                        
                        var remoteVal = argsObj.Get("remote", null);
                        var remote = remoteVal != null && remoteVal.Type == ValueType.String 
                            ? remoteVal.AsString() 
                            : "origin";
                        
                        var branchVal = argsObj.Get("branch", null);
                        
                        var args = new List<RuntimeValue> { RuntimeValue.String(repoPath), RuntimeValue.String(remote) };
                        if (branchVal != null && branchVal.Type == ValueType.String)
                            args.Add(branchVal);
                        
                        return BuiltInFunctions.CallBuiltIn("gitPull", args, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing git_pull tool: {ex.Message}");
                    }
                
                case "web_search":
                    try
                    {
                        var queryVal = argsObj.Get("query", null);
                        if (queryVal == null || queryVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: query parameter required");
                        var query = queryVal.AsString();
                        if (string.IsNullOrWhiteSpace(query))
                            return RuntimeValue.String("Error: query cannot be empty");
                        var webSearchArgs = new List<RuntimeValue> { RuntimeValue.String(query) };
                        try
                        {
                            var apiKeyVal = argsObj.Get("apiKey", null);
                            if (apiKeyVal != null && apiKeyVal.Type == ValueType.String && !string.IsNullOrWhiteSpace(apiKeyVal.AsString()))
                                webSearchArgs.Add(apiKeyVal);
                        }
                        catch { }
                        return BuiltInFunctions.CallBuiltIn("webSearch", webSearchArgs, null);
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing web_search tool: {ex.Message}");
                    }
                
                case "run_command":
                    try
                    {
                        var commandVal = argsObj.Get("command", null);
                        if (commandVal == null || commandVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: command parameter required");
                        
                        var command = commandVal.AsString();
                        if (string.IsNullOrWhiteSpace(command))
                            return RuntimeValue.String("Error: command cannot be empty");
                        
                        // Extract optional args array
                        RuntimeValue? argsArray = null;
                        try
                        {
                            var argsVal = argsObj.Get("args", null);
                            if (argsVal != null && argsVal.Type == ValueType.Array)
                                argsArray = argsVal;
                        }
                        catch { }
                        
                        // Extract optional working directory
                        string? cmdWorkingDirectory = null;
                        try
                        {
                            var workingDirVal = argsObj.Get("workingDirectory", null);
                            if (workingDirVal != null && workingDirVal.Type == ValueType.String)
                            {
                                cmdWorkingDirectory = workingDirVal.AsString();
                                
                                // Validate working directory if tool has restrictions
                                if (!string.IsNullOrEmpty(tool.WorkingDirectory) && !tool.IsPathAllowed(cmdWorkingDirectory))
                                {
                                    return RuntimeValue.String($"Error: Working directory '{cmdWorkingDirectory}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                                }
                            }
                        }
                        catch { }
                        
                        // Use tool's working directory as default if not provided
                        if (string.IsNullOrEmpty(cmdWorkingDirectory) && !string.IsNullOrEmpty(tool.WorkingDirectory))
                        {
                            cmdWorkingDirectory = tool.WorkingDirectory;
                        }
                        
                        // Extract optional timeout
                        RuntimeValue? timeoutVal = null;
                        try
                        {
                            var timeoutMsVal = argsObj.Get("timeoutMs", null);
                            if (timeoutMsVal != null && timeoutMsVal.Type == ValueType.Integer)
                                timeoutVal = timeoutMsVal;
                        }
                        catch { }
                        
                        // Build arguments list for BuiltInRunCommand
                        var runCommandArgs = new List<RuntimeValue> { RuntimeValue.String(command) };
                        
                        if (argsArray != null)
                            runCommandArgs.Add(argsArray);
                        else
                            runCommandArgs.Add(RuntimeValue.Null());
                        
                        if (!string.IsNullOrEmpty(cmdWorkingDirectory))
                            runCommandArgs.Add(RuntimeValue.String(cmdWorkingDirectory));
                        else
                            runCommandArgs.Add(RuntimeValue.Null());
                        
                        if (timeoutVal != null)
                            runCommandArgs.Add(timeoutVal);
                        else
                            runCommandArgs.Add(RuntimeValue.Null());

                        // Parse args for approval display
                        List<string>? argStrings = null;
                        if (argsArray != null)
                        {
                            argStrings = new List<string>();
                            foreach (var arg in argsArray.AsArray())
                            {
                                if (arg.Type == ValueType.String)
                                    argStrings.Add(arg.AsString());
                            }
                        }

                        var approval = CommandApprovalService.EnsureApprovedAsync(
                            _inputProvider, command, argStrings, cmdWorkingDirectory
                        ).GetAwaiter().GetResult();
                        if (!approval.Approved)
                            return RuntimeValue.String(approval.ErrorMessage ?? "Error: Command not approved");

                        RuntimeValue result;
                        using (CommandExecutionContext.EnterUserApprovedScope())
                        {
                            result = BuiltInFunctions.CallBuiltIn("runCommand", runCommandArgs, null);
                        }
                        return result;
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing run_command tool: {ex.Message}");
                    }
                
                case "run_malda":
                    try
                    {
                        var sourceOrFilePathVal = argsObj.Get("sourceOrFilePath", null);
                        if (sourceOrFilePathVal == null || sourceOrFilePathVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: sourceOrFilePath parameter required");
                        
                        var sourceOrFilePath = sourceOrFilePathVal.AsString();
                        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
                            return RuntimeValue.String("Error: sourceOrFilePath cannot be empty");
                        
                        // Extract optional input parameter
                        RuntimeValue? inputVal = null;
                        try
                        {
                            var inputParam = argsObj.Get("input", null);
                            if (inputParam != null && inputParam.Type == ValueType.String)
                                inputVal = inputParam;
                        }
                        catch { }
                        
                        // Build arguments list for BuiltInRunMALDA
                        var runMaldaArgs = new List<RuntimeValue> { RuntimeValue.String(sourceOrFilePath) };
                        
                        if (inputVal != null)
                            runMaldaArgs.Add(inputVal);
                        
                        var result = BuiltInFunctions.CallBuiltIn("runMALDA", runMaldaArgs, null);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing run_malda tool: {ex.Message}");
                    }
                
                case "compile_malda":
                    try
                    {
                        var sourcePathVal = argsObj.Get("sourcePath", null);
                        if (sourcePathVal == null || sourcePathVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: sourcePath parameter required");
                        
                        var sourcePath = sourcePathVal.AsString();
                        if (string.IsNullOrWhiteSpace(sourcePath))
                            return RuntimeValue.String("Error: sourcePath cannot be empty");
                        
                        // Build arguments list for BuiltInCompileMALDA
                        var compileSplArgs = new List<RuntimeValue> { RuntimeValue.String(sourcePath) };
                        
                        // Extract optional outputPath parameter
                        try
                        {
                            var outputPathParam = argsObj.Get("outputPath", null);
                            if (outputPathParam != null && outputPathParam.Type == ValueType.String)
                                compileSplArgs.Add(outputPathParam);
                        }
                        catch { }
                        
                        // Extract optional mode parameter
                        try
                        {
                            var modeParam = argsObj.Get("mode", null);
                            if (modeParam != null && modeParam.Type == ValueType.String)
                                compileSplArgs.Add(modeParam);
                        }
                        catch { }
                        
                        var result = BuiltInFunctions.CallBuiltIn("compileMALDA", compileSplArgs, null);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing compile_malda tool: {ex.Message}");
                    }
                
                case "get_symbols":
                    try
                    {
                        var filePathOrSourceVal = argsObj.Get("filePathOrSource", null);
                        if (filePathOrSourceVal == null || filePathOrSourceVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: filePathOrSource parameter required");
                        
                        var filePathOrSource = filePathOrSourceVal.AsString();
                        if (string.IsNullOrWhiteSpace(filePathOrSource))
                            return RuntimeValue.String("Error: filePathOrSource cannot be empty");
                        
                        string sourceCode = filePathOrSource;
                        
                        // If it looks like a file path, read the file first
                        if ((filePathOrSource.Contains(Path.DirectorySeparatorChar) || 
                             filePathOrSource.Contains(Path.AltDirectorySeparatorChar) ||
                             filePathOrSource.EndsWith(".malda", StringComparison.OrdinalIgnoreCase)))
                        {
                            string getSymbolslFilePath;
                            if (Path.IsPathRooted(filePathOrSource))
                            {
                                getSymbolslFilePath = Path.GetFullPath(filePathOrSource);
                            }
                            else if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                            {
                                getSymbolslFilePath = Path.Combine(tool.WorkingDirectory, filePathOrSource);
                            }
                            else
                            {
                                getSymbolslFilePath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, filePathOrSource));
                            }
                            
                            // Validate path if it's a file path
                            if (!string.IsNullOrEmpty(tool.WorkingDirectory) && !tool.IsPathAllowed(getSymbolslFilePath))
                            {
                                return RuntimeValue.String($"Error: Path '{getSymbolslFilePath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                            }
                            
                            // Check for path traversal attempts
                            if (filePathOrSource.Contains("..") || filePathOrSource.Contains("~"))
                            {
                                return RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)");
                            }
                            
                            // Read the file content
                            if (File.Exists(getSymbolslFilePath))
                            {
                                sourceCode = File.ReadAllText(getSymbolslFilePath);
                            }
                            else
                            {
                                return RuntimeValue.String($"Error: File not found: {getSymbolslFilePath}");
                            }
                        }
                        
                        // Build arguments list for BuiltInGetSymbols (pass source code, not file path)
                        var getSymbolsArgs = new List<RuntimeValue> { RuntimeValue.String(sourceCode) };
                        
                        var result = BuiltInFunctions.CallBuiltIn("getSymbols", getSymbolsArgs, null);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing get_symbols tool: {ex.Message}");
                    }
                
                case "get_parse_errors":
                    try
                    {
                        var sourceOrFilePathVal = argsObj.Get("sourceOrFilePath", null);
                        if (sourceOrFilePathVal == null || sourceOrFilePathVal.Type != ValueType.String)
                            return RuntimeValue.String("Error: sourceOrFilePath parameter required");
                        
                        var sourceOrFilePath = sourceOrFilePathVal.AsString();
                        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
                            return RuntimeValue.String("Error: sourceOrFilePath cannot be empty");
                        
                        string sourceCode = sourceOrFilePath;
                        
                        if (sourceOrFilePath.Contains(Path.DirectorySeparatorChar) ||
                             sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
                             sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
                        {
                            string resolvedPath;
                            if (Path.IsPathRooted(sourceOrFilePath))
                            {
                                resolvedPath = Path.GetFullPath(sourceOrFilePath);
                            }
                            else if (!string.IsNullOrEmpty(tool.WorkingDirectory))
                            {
                                resolvedPath = Path.Combine(tool.WorkingDirectory, sourceOrFilePath);
                            }
                            else
                            {
                                resolvedPath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                            }
                            
                            if (!string.IsNullOrEmpty(tool.WorkingDirectory) && !tool.IsPathAllowed(resolvedPath))
                            {
                                return RuntimeValue.String($"Error: Path '{resolvedPath}' is outside the allowed working directory '{tool.WorkingDirectory}'");
                            }
                            
                            if (sourceOrFilePath.Contains("..") || sourceOrFilePath.Contains("~"))
                            {
                                return RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)");
                            }
                            
                            if (File.Exists(resolvedPath))
                            {
                                sourceCode = File.ReadAllText(resolvedPath);
                            }
                            else
                            {
                                return RuntimeValue.String($"Error: File not found: {resolvedPath}");
                            }
                        }
                        
                        var getParseErrorsArgs = new List<RuntimeValue> { RuntimeValue.String(sourceCode) };
                        var result = BuiltInFunctions.CallBuiltIn("getParseErrors", getParseErrorsArgs, null);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error executing get_parse_errors tool: {ex.Message}");
                    }
                
                case "submit_plan":
                    try
                    {
                        var planVal = argsObj.Get("plan", null);
                        var stepsVal = argsObj.Get("steps", null);
                        RuntimeValue planOrSteps;
                        if (planVal != null && (planVal.Type == ValueType.Object || planVal.Type == ValueType.Array))
                            planOrSteps = planVal;
                        else if (stepsVal != null && stepsVal.Type == ValueType.Array)
                            planOrSteps = stepsVal;
                        else
                        {
                            var outErr = new JsonObject();
                            outErr.Set("accepted", RuntimeValue.Boolean(false));
                            outErr.Set("error", RuntimeValue.String("submit_plan requires 'plan' (object with steps) or 'steps' (array)"));
                            return RuntimeValue.Object(outErr);
                        }
                        var validation = BuiltInFunctions.ValidateAndNormalizePlan(planOrSteps);
                        if (validation.Type != ValueType.Object)
                        {
                            var outErr = new JsonObject();
                            outErr.Set("accepted", RuntimeValue.Boolean(false));
                            outErr.Set("error", RuntimeValue.String("Invalid plan"));
                            return RuntimeValue.Object(outErr);
                        }
                        var vObj = validation.AsObject();
                        var errVal = vObj.Get("error", null);
                        if (errVal != null && errVal.Type == ValueType.String)
                        {
                            var outErr = new JsonObject();
                            outErr.Set("accepted", RuntimeValue.Boolean(false));
                            outErr.Set("error", errVal);
                            return RuntimeValue.Object(outErr);
                        }
                        var planIdVal = vObj.Get("planId", null);
                        var stepsArr = vObj.Get("steps", null);
                        int stepCount = stepsArr != null && stepsArr.Type == ValueType.Array ? stepsArr.AsArray().Count : 0;
                        var outOk = new JsonObject();
                        outOk.Set("accepted", RuntimeValue.Boolean(true));
                        outOk.Set("planId", planIdVal ?? RuntimeValue.String(""));
                        outOk.Set("stepCount", RuntimeValue.Integer(stepCount));
                        return RuntimeValue.Object(outOk);
                    }
                    catch (Exception ex)
                    {
                        var outErr = new JsonObject();
                        outErr.Set("accepted", RuntimeValue.Boolean(false));
                        outErr.Set("error", RuntimeValue.String(ex.Message));
                        return RuntimeValue.Object(outErr);
                    }
                
                default:
                    return RuntimeValue.String($"Error: Unknown tool '{tool.Name}'");
            }
        }
        catch (InputRequiredException)
        {
            // Re-throw InputRequiredException to let execution service handle it
            throw;
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing tool: {ex.Message}");
        }
    }

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
            var tasks = batch.Select(item => Task.Run(() => ExecuteSingleToolCall(item))).ToArray();
            try
            {
                Task.WaitAll(tasks);
            }
            catch (AggregateException agg)
            {
                foreach (var inner in agg.Flatten().InnerExceptions)
                {
                    if (inner is InputRequiredException)
                        throw inner;
                }
                throw;
            }

            foreach (var task in tasks)
                outcomes.Add(task.Result);
        }

        return outcomes;
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

    private ToolCallOutcome ExecuteSingleToolCall(RuntimeValue tc)
    {
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
                        toolResult = ExecuteToolOperation(tool, argsValue ?? RuntimeValue.Null());
                        executedCoreTool = true;

                        var resultDisplay = SerializeRuntimeValueToJson(toolResult ?? RuntimeValue.Null());
                        if (resultDisplay.Length > 500)
                            resultDisplay = resultDisplay.Substring(0, 500) + "...";
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
                                    resultJson = resultDisplay,
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
                    throw;
                }
                catch (Exception ex)
                {
                    var errorMsg = ex.Message;
                    if (errorMsg.Length > 500)
                        errorMsg = errorMsg.Substring(0, 500) + "...";
                    _toolCallLogger?.Invoke(toolName, argsDisplay, $"Error: {errorMsg}", true, fullArguments);
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
                                error = errorMsg
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
                        resultJson = toolResultJson,
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

        if (toolName.Equals("web_search", StringComparison.OrdinalIgnoreCase))
            return "web";

        return "other";
    }
}
