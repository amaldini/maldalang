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
    private const int TraceToolResultMaxChars = 65536;

    private static string TruncateForLog(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength) + "...";
    }

    /// <summary>
    /// Keeps tool results in the session trace. Caps only past 64KB and marks that cap
    /// so a later replay can tell a stored result from a clipped one.
    /// </summary>
    private static string TruncateForTrace(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        if (text.Length <= TraceToolResultMaxChars)
            return text;
        return text.Substring(0, TraceToolResultMaxChars) + $"... [truncated, {text.Length} characters]";
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

    /// <summary>
    /// Interactive one-line activity. Off when verbose logging is on, stdout is
    /// redirected, or <c>MALDA_AGENT_STATUS</c> is off/0/false/no.
    /// </summary>
    internal static bool IsAgentActivityStatusEnabled(bool verboseEnabled, bool outputRedirected, string? statusEnv)
    {
        if (verboseEnabled || outputRedirected)
            return false;
        if (string.IsNullOrWhiteSpace(statusEnv))
            return true;
        var lower = statusEnv.Trim().ToLowerInvariant();
        return lower is not ("0" or "false" or "no" or "off");
    }

    private static bool IsAgentActivityStatusEnabled()
    {
        EnsureVerboseLoggingSetup();
        return IsAgentActivityStatusEnabled(
            _verboseLoggingEnabled,
            System.Console.IsOutputRedirected,
            GetAgentEnv("MALDA_AGENT_STATUS", "MALDA_RALPH_STATUS"));
    }

    internal static string FormatThinkingActivity(int round) =>
        "thinking\u2026 round " + round;

    internal static string FormatToolActivity(string? toolName, string? target, string? resultSuffix = null)
    {
        var name = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName.Trim();
        var text = string.IsNullOrWhiteSpace(target)
            ? name
            : name + " " + CollapseActivityText(target);
        if (!string.IsNullOrWhiteSpace(resultSuffix))
            text += " \u00b7 " + CollapseActivityText(resultSuffix);
        return text;
    }

    internal static string FormatParallelActivity(IReadOnlyList<string> parts) =>
        string.Join(", ", parts);

    internal readonly struct ActivityRewrite
    {
        public ActivityRewrite(string text, int visibleLength)
        {
            Text = text;
            VisibleLength = visibleLength;
        }

        public string Text { get; }
        public int VisibleLength { get; }
    }

    /// <summary>
    /// Carriage-return rewrite. <paramref name="consoleWidth"/> of 0 or less falls back to 80.
    /// The visible length is the dirty span the next rewrite must cover.
    /// </summary>
    internal static ActivityRewrite BuildActivityRewrite(string? text, int previousWidth, int consoleWidth)
    {
        var width = consoleWidth > 0 ? consoleWidth : 80;
        var max = Math.Max(1, width - 1);
        var oneLine = CollapseActivityText(text ?? "");
        string clipped;
        if (oneLine.Length <= max)
            clipped = oneLine;
        else if (max <= 3)
            clipped = oneLine.Substring(0, max);
        else
            clipped = oneLine.Substring(0, max - 3) + "...";

        var pad = Math.Max(0, previousWidth - clipped.Length);
        var covered = Math.Max(previousWidth, clipped.Length);
        return new ActivityRewrite("\r" + clipped + new string(' ', pad), covered);
    }

    internal static string BuildActivityErase(int previousWidth)
    {
        if (previousWidth <= 0)
            return "";
        return "\r" + new string(' ', previousWidth) + "\r";
    }

    private static string CollapseActivityText(string text)
    {
        var oneLine = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (oneLine.Contains("  ", StringComparison.Ordinal))
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        return oneLine;
    }

    private static int ResolveActivityConsoleWidth()
    {
        try
        {
            var width = System.Console.WindowWidth;
            if (width > 0)
                return width;
        }
        catch
        {
            // WindowWidth throws when stdout is not a console.
        }

        return 80;
    }

    private static void WriteActivityStatus(string text)
    {
        if (!IsAgentActivityStatusEnabled())
            return;

        lock (ActivityStatusLock)
        {
            var rewrite = BuildActivityRewrite(text, _activityLineWidth, ResolveActivityConsoleWidth());
            try
            {
                System.Console.Write(rewrite.Text);
                System.Console.Out.Flush();
                _activityLineWidth = rewrite.VisibleLength;
            }
            catch
            {
                // Status display must never break the agent loop.
            }
        }
    }

    private static void CommitActivityStatus(string text)
    {
        if (!IsAgentActivityStatusEnabled())
            return;

        lock (ActivityStatusLock)
        {
            var rewrite = BuildActivityRewrite(text, _activityLineWidth, ResolveActivityConsoleWidth());
            try
            {
                System.Console.Write(rewrite.Text);
                System.Console.WriteLine();
                System.Console.Out.Flush();
            }
            catch
            {
                // Status display must never break the agent loop.
            }

            _activityLineWidth = 0;
        }
    }

    internal static void ClearActivityStatus()
    {
        lock (ActivityStatusLock)
        {
            if (_activityLineWidth <= 0)
                return;

            try
            {
                System.Console.Write(BuildActivityErase(_activityLineWidth));
                System.Console.Out.Flush();
            }
            catch
            {
                // Status display must never break the agent loop.
            }

            _activityLineWidth = 0;
        }
    }

    private static void FinishToolActivity(string? toolName, string? argumentsJson, RuntimeValue? toolResult, bool succeeded)
    {
        var suffix = succeeded
            ? (GetReadFileToolLineSummary(toolName, argumentsJson, toolResult) ?? "ok")
            : "failed";
        var line = FormatToolActivity(toolName, TryExtractToolTarget(argumentsJson), suffix);
        if (succeeded)
            WriteActivityStatus(line);
        else
            CommitActivityStatus(line);
    }

    /// <summary>
    /// Clears the activity line when the outermost <c>Send</c> returns, including
    /// the recursive tool-round calls inside one turn.
    /// </summary>
    private readonly struct ActivityTurnScope : IDisposable
    {
        private readonly int _depth;

        private ActivityTurnScope(int depth) => _depth = depth;

        public static ActivityTurnScope Enter()
        {
            var depth = ActivitySendDepth.Value;
            ActivitySendDepth.Value = depth + 1;
            return new ActivityTurnScope(depth);
        }

        public void Dispose()
        {
            ActivitySendDepth.Value = _depth;
            if (_depth == 0)
                ClearActivityStatus();
        }
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
}
