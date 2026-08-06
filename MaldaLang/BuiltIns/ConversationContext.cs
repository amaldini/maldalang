// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Text;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class ConversationInstance
{
    private string? _contextTrimHandoffNote;

    /// <summary>
    /// Optional note injected when context is trimmed (e.g. Ralph session summary).
    /// Consumed on the next trim.
    /// </summary>
    public void SetContextTrimHandoffNote(string? note)
    {
        _contextTrimHandoffNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    /// <summary>
    /// Rough token estimate for the current conversation payload (chars / 4).
    /// </summary>
    public int EstimateContextTokens()
    {
        return (int)Math.Ceiling(EstimateContextChars() / 4.0);
    }

    internal void TrimContextIfOverBudget()
    {
        if (!IsContextAutoTrimEnabled())
            return;

        var budget = ResolveContextBudgetTokens();
        if (budget <= 0)
            return;

        var estimated = EstimateContextTokens();
        if (estimated <= budget)
            return;

        var lastUser = FindLastUserMessageContent();
        if (string.IsNullOrEmpty(lastUser))
            return;

        var handoff = _contextTrimHandoffNote;
        _contextTrimHandoffNote = null;

        var preserved = BuildPreservedUserMessage(lastUser, handoff);
        var previousCount = _messages.Count;
        var previousEstimate = estimated;

        ReplaceConversationWithTrimmedTask(preserved);

        EnsureVerboseLoggingSetup();
        if (_verboseLoggingEnabled)
        {
            var plain =
                $"[llm] context trimmed (~{previousEstimate} est. tokens > {budget} budget; " +
                $"removed {previousCount - _messages.Count} message(s), kept system prompt + current task)";
            WriteVerboseLine(
                plain,
                IsAgentRichCli()
                    ? $"[yellow][llm][/] context trimmed · ~{previousEstimate} tok > {budget} budget"
                    : null);
        }
    }

    private void ReplaceConversationWithTrimmedTask(string preservedUser)
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

        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String("user"));
        msg.Set("content", RuntimeValue.String(preservedUser));
        _messages.Add(RuntimeValue.Object(msg));
    }

    private static string BuildPreservedUserMessage(string lastUser, string? handoff)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "[Context trimmed — earlier messages and tool results were removed to stay within context limits. Re-read files as needed.]");
        if (!string.IsNullOrEmpty(handoff))
        {
            sb.AppendLine();
            sb.AppendLine(handoff);
        }

        sb.AppendLine();
        sb.AppendLine(lastUser);
        return sb.ToString().TrimEnd();
    }

    private int EstimateContextChars()
    {
        var total = _systemPrompt?.Length ?? 0;
        foreach (var msg in _messages)
            total += EstimateMessageChars(msg);

        total += EstimateToolSchemaChars();
        return total;
    }

    private int EstimateToolSchemaChars()
    {
        if (_tools.Count == 0)
            return 0;

        var total = 0;
        foreach (var tool in _tools.Values)
        {
            try
            {
                total += SerializeRuntimeValueToJson(tool.GetSchema()).Length;
            }
            catch
            {
                total += 512;
            }
        }

        return total;
    }

    private int EstimateMessageChars(RuntimeValue msg)
    {
        if (msg.Type != ValueType.Object)
            return msg.ToString()?.Length ?? 0;

        var msgObj = msg.AsObject();
        var total = 0;

        var content = GetStringProperty(msgObj, "content");
        if (!string.IsNullOrEmpty(content))
            total += content.Length;

        var toolCalls = GetProperty(msgObj, "tool_calls");
        if (toolCalls != null)
        {
            try
            {
                total += SerializeRuntimeValueToJson(toolCalls).Length;
            }
            catch
            {
                total += 256;
            }
        }

        var role = GetStringProperty(msgObj, "role");
        if (!string.IsNullOrEmpty(role))
            total += role.Length;

        return total;
    }

    private string? FindLastUserMessageContent()
    {
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].Type != ValueType.Object)
                continue;

            var msgObj = _messages[i].AsObject();
            if (!string.Equals(GetStringProperty(msgObj, "role"), "user", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = GetStringProperty(msgObj, "content");
            if (!string.IsNullOrEmpty(content))
                return content;
        }

        return null;
    }

    private int ResolveContextBudgetTokens()
    {
        var explicitBudget = ResolveEnvInt("MALDA_AGENT_CONTEXT_BUDGET_TOKENS", "MALDA_RALPH_CONTEXT_BUDGET_TOKENS");
        if (explicitBudget > 0)
            return explicitBudget;

        var limit = ResolveEnvInt("MALDA_AGENT_CONTEXT_LIMIT_TOKENS", "MALDA_RALPH_CONTEXT_LIMIT_TOKENS");
        if (limit <= 0)
            limit = 1_048_576;

        var ratio = ResolveEnvDouble("MALDA_AGENT_CONTEXT_BUDGET_RATIO", "MALDA_RALPH_CONTEXT_BUDGET_RATIO");
        if (ratio <= 0 || ratio > 1)
            ratio = 0.75;

        var maxOutput = ResolveMaxOutputTokens();
        var toolReserve = ResolveEnvInt("MALDA_AGENT_CONTEXT_TOOL_RESERVE_TOKENS", "MALDA_RALPH_CONTEXT_TOOL_RESERVE_TOKENS");
        if (toolReserve <= 0)
            toolReserve = 8192;

        var budget = (int)Math.Floor(limit * ratio) - maxOutput - toolReserve;
        return Math.Max(budget, 0);
    }

    private int ResolveMaxOutputTokens()
    {
        if (_client != null && _client.MaxTokens > 0)
            return _client.MaxTokens;
        if (_bridgeClient != null)
        {
            try
            {
                var maxTokensVal = _bridgeClient.Get("maxTokens", null);
                if (maxTokensVal.Type == ValueType.Integer && maxTokensVal.AsInteger() > 0)
                    return maxTokensVal.AsInteger();
            }
            catch
            {
                // ignore
            }
        }

        return 16384;
    }

    private static bool IsContextAutoTrimEnabled()
    {
        var raw = GetAgentEnv("MALDA_AGENT_CONTEXT_AUTO_TRIM", "MALDA_RALPH_CONTEXT_AUTO_TRIM");
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var lower = raw.Trim().ToLowerInvariant();
        return lower is not ("0" or "false" or "no" or "off");
    }

    private static int ResolveEnvInt(string primary, string legacyAlias)
    {
        var raw = GetAgentEnv(primary, legacyAlias);
        if (string.IsNullOrWhiteSpace(raw))
            return 0;
        return int.TryParse(raw.Trim(), out var value) ? value : 0;
    }

    private static double ResolveEnvDouble(string primary, string legacyAlias)
    {
        var raw = GetAgentEnv(primary, legacyAlias);
        if (string.IsNullOrWhiteSpace(raw))
            return 0;
        return double.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    /// <summary>
    /// When true (default), omit assistant content on tool-call rounds from conversation history.
    /// Tool calls and results are kept; planning narration is not replayed on later LLM rounds.
    /// Set MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT=true to preserve legacy behavior.
    /// </summary>
    internal static bool ShouldStripToolRoundContent()
    {
        var raw = GetAgentEnv("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT", "MALDA_RALPH_KEEP_TOOL_ROUND_CONTENT");
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        var lower = raw.Trim().ToLowerInvariant();
        return lower is not ("1" or "true" or "yes" or "on");
    }

    internal JsonObject BuildAssistantToolCallHistoryMessage(JsonObject jsonResponse, List<RuntimeValue> validToolCalls)
    {
        var assistantMsg = new JsonObject();
        assistantMsg.Set("role", RuntimeValue.String("assistant"));

        if (!ShouldStripToolRoundContent())
        {
            var content = jsonResponse.Get("content", null);
            if (content != null)
                assistantMsg.Set("content", content);
        }

        // DeepSeek V4 / thinking models require reasoning_content to be replayed on
        // assistant messages that carried tool_calls; drop it and the next round 400s.
        var reasoning = jsonResponse.Get("reasoning", null);
        if (reasoning != null && reasoning.Type == ValueType.String &&
            !string.IsNullOrWhiteSpace(reasoning.AsString()))
        {
            assistantMsg.Set("reasoning", reasoning);
        }

        assistantMsg.Set("tool_calls", RuntimeValue.Array(validToolCalls));
        return assistantMsg;
    }
}
