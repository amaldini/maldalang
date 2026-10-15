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

        if (normalized != null)
            BeginThinkCancelScope(normalized);
    }

    /// <summary>
    /// Cancel in-flight <c>think()</c> for a live channel (another HTTP request can call this).
    /// </summary>
    public static void RequestThinkCancel(string channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? null : channel.Trim();
        if (normalized == null)
            return;

        var cts = ThinkCancelByChannel.GetOrAdd(normalized, static _ => new CancellationTokenSource());
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public static bool IsThinkCancelRequested()
    {
        var channel = GetAgentProgressLiveChannel();
        if (string.IsNullOrEmpty(channel))
            return false;
        return ThinkCancelByChannel.TryGetValue(channel, out var cts) && cts.IsCancellationRequested;
    }

    public static CancellationToken GetThinkCancellationToken()
    {
        var channel = GetAgentProgressLiveChannel();
        if (string.IsNullOrEmpty(channel))
            return CancellationToken.None;
        var cts = ThinkCancelByChannel.GetOrAdd(channel, static _ => new CancellationTokenSource());
        return cts.Token;
    }

    private static void BeginThinkCancelScope(string channel)
    {
        ThinkCancelByChannel.AddOrUpdate(
            channel,
            static _ => new CancellationTokenSource(),
            static (_, old) =>
            {
                try
                {
                    old.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                try
                {
                    old.Dispose();
                }
                catch
                {
                }

                return new CancellationTokenSource();
            });
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
}
