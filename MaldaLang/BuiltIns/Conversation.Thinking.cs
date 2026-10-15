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
            ClearActivityStatus();
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
        var liveDraft = !string.IsNullOrWhiteSpace(GetAgentProgressLiveChannel());
        _llmStreamHeaderPrinted = false;
        _llmStreamThinkingPrinted = false;

        if (liveDraft)
            ResetLiveDraft();

        if (useStreamDisplay || promptHandler != null || liveDraft)
        {
            LLMClientInstance.StreamDeltaHandler = delta =>
            {
                promptHandler?.Invoke(delta);
                if (useStreamDisplay)
                    WriteLlmStreamDelta(delta);
                if (liveDraft)
                    HandleLiveDraftDelta(delta);
            };
        }

        try
        {
            return chatCall();
        }
        finally
        {
            if (liveDraft)
                FlushLiveDraft();
            LLMClientInstance.StreamDeltaHandler = null;
            if (useStreamDisplay)
                FinishLlmStreamLine();
        }
    }

    private sealed class LiveDraftState
    {
        public readonly StringBuilder Text = new();
        public long LastEmitTimestamp;
        public bool Dirty;
    }

    internal static void ResetLiveDraft()
    {
        LiveDraftLocal.Value = new LiveDraftState();
    }

    internal void HandleLiveDraftDelta(LlmStreamDelta delta)
    {
        if (!string.Equals(delta.Kind, "content", StringComparison.OrdinalIgnoreCase))
            return;
        if (string.IsNullOrEmpty(delta.Text))
            return;
        if (string.IsNullOrWhiteSpace(GetAgentProgressLiveChannel()))
            return;

        var state = LiveDraftLocal.Value;
        if (state == null)
        {
            state = new LiveDraftState();
            LiveDraftLocal.Value = state;
        }

        if (state.Text.Length < LiveDraftMaxChars)
        {
            var remaining = LiveDraftMaxChars - state.Text.Length;
            if (remaining > 0)
            {
                var piece = delta.Text.Length <= remaining
                    ? delta.Text
                    : delta.Text.Substring(0, remaining);
                if (piece.Length > 0)
                    state.Text.Append(piece);
            }
        }

        state.Dirty = true;
        var interval = LiveDraftMinIntervalMs;
        var now = System.Environment.TickCount64;
        if (interval > 0 && state.LastEmitTimestamp != 0 && (now - state.LastEmitTimestamp) < interval)
            return;

        EmitLiveDraft(state, now);
    }

    internal void FlushLiveDraft()
    {
        var state = LiveDraftLocal.Value;
        if (state == null || !state.Dirty)
            return;
        EmitLiveDraft(state, System.Environment.TickCount64);
    }

    private void EmitLiveDraft(LiveDraftState state, long now)
    {
        if (state.Text.Length == 0)
            return;

        var payload = new JsonObject();
        payload.Set("phase", RuntimeValue.String("draft"));
        payload.Set("text", RuntimeValue.String(state.Text.ToString()));
        payload.Set("round", RuntimeValue.Integer(_llmRound));
        if (!string.IsNullOrEmpty(AgentName))
            payload.Set("agent", RuntimeValue.String(AgentName));
        DeliverAgentProgress(RuntimeValue.Object(payload));
        state.LastEmitTimestamp = now;
        state.Dirty = false;
    }
}
