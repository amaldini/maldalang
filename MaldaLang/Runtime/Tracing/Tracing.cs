// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Core tracing primitives for MALDA agent sessions.
// This layer is intentionally minimal and dependency-free so it can be
// used from interpreter, built-ins, IDE host, and future replay tooling.

namespace MaldaLang.Runtime.Tracing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

/// <summary>
/// High-level classification for trace events emitted by the MALDA runtime.
/// </summary>
public enum TraceEventType
{
    LlmRequest,
    LlmResponse,
    ToolCallStart,
    ToolCallEnd,
    FileEdit,
    RunCommand,
    RunMalda,
    CompileMalda,
    AgentMessage,
    Error
}

/// <summary>
/// A single trace event within an agent session.
/// Serialized as one JSON object per line in a .malda-trace.jsonl file.
/// </summary>
public sealed class TraceEvent
{
    public string SessionId { get; }
    public int StepIndex { get; }
    public DateTime TimestampUtc { get; }
    public TraceEventType Type { get; }
    public string? AgentName { get; }
    public string? ConversationId { get; }
    public object Payload { get; }

    public TraceEvent(
        string sessionId,
        int stepIndex,
        DateTime timestampUtc,
        TraceEventType type,
        object payload,
        string? agentName = null,
        string? conversationId = null)
    {
        SessionId = sessionId;
        StepIndex = stepIndex;
        TimestampUtc = timestampUtc;
        Type = type;
        Payload = payload;
        AgentName = agentName;
        ConversationId = conversationId;
    }
}

/// <summary>
/// Per-logical-run agent session context. Backed by AsyncLocal so it can flow
/// through asynchronous code without needing to be threaded manually.
/// </summary>
public sealed class AgentSessionContext
{
    private int _nextStepIndex;

    public string SessionId { get; }
    public string? Name { get; }
    public IReadOnlyDictionary<string, string>? Tags { get; }

    internal AgentSessionContext(string sessionId, string? name, Dictionary<string, string>? tags)
    {
        SessionId = sessionId;
        Name = name;
        Tags = tags;
        _nextStepIndex = 0;
    }

    internal int GetNextStepIndex()
    {
        return Interlocked.Increment(ref _nextStepIndex) - 1;
    }
}

/// <summary>
/// Manages the ambient AgentSessionContext for the current async-flow.
/// </summary>
public static class AgentSession
{
    private static readonly AsyncLocal<AgentSessionContext?> _current = new();

    public static AgentSessionContext? Current => _current.Value;

    public static AgentSessionContext Start(string? name = null, Dictionary<string, string>? tags = null, string? sessionId = null)
    {
        // Allow caller to supply a session id (for tests) or generate a new one.
        var id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId!;
        var ctx = new AgentSessionContext(id, name, tags);
        _current.Value = ctx;
        return ctx;
    }

    public static void SetCurrent(AgentSessionContext? context)
    {
        _current.Value = context;
    }

    public static void Stop()
    {
        _current.Value = null;
    }
}

/// <summary>
/// Abstraction for writing trace events. Implementations may write to disk,
/// memory, network, etc. When tracing is disabled, a no-op writer is used.
/// </summary>
public interface ITraceWriter
{
    bool IsEnabled { get; }

    void Write(TraceEvent evt);
}

/// <summary>
/// No-op writer used when tracing is disabled.
/// </summary>
internal sealed class NoOpTraceWriter : ITraceWriter
{
    public static readonly NoOpTraceWriter Instance = new();

    public bool IsEnabled => false;

    private NoOpTraceWriter()
    {
    }

    public void Write(TraceEvent evt)
    {
        // Intentionally no-op.
    }
}

/// <summary>
/// Simple file-based writer that appends one JSON object per line to a trace file.
/// </summary>
public sealed class FileTraceWriter : ITraceWriter, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _serializerOptions;

    public string FilePath { get; }

    public bool IsEnabled => true;

    public FileTraceWriter(string baseDirectory, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Base directory cannot be null or empty.", nameof(baseDirectory));

        Directory.CreateDirectory(baseDirectory);

        FilePath = Path.Combine(baseDirectory, $"{sessionId}.malda-trace.jsonl");

        // Use append mode so multiple processes/steps can contribute to the same trace.
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };

        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }

    public void Write(TraceEvent evt)
    {
        if (evt == null)
            return;

        // Shape the JSON according to the documented schema.
        var jsonObject = new
        {
            sessionId = evt.SessionId,
            stepIndex = evt.StepIndex,
            timestampUtc = evt.TimestampUtc.ToString("O"),
            type = evt.Type.ToString(),
            agentName = evt.AgentName,
            conversationId = evt.ConversationId,
            payload = evt.Payload
        };

        var json = JsonSerializer.Serialize(jsonObject, _serializerOptions);
        _writer.WriteLine(json);
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}

/// <summary>
/// Central access point for the current trace writer and helper to record events.
/// </summary>
public static class TraceManager
{
    private static ITraceWriter _current = NoOpTraceWriter.Instance;

    /// <summary>
    /// Gets the current trace writer. When tracing is disabled this is a no-op writer.
    /// </summary>
    public static ITraceWriter Current => _current;

    /// <summary>
    /// Enables tracing with the specified writer.
    /// </summary>
    public static void EnableTracing(ITraceWriter writer)
    {
        _current = writer ?? NoOpTraceWriter.Instance;
    }

    /// <summary>
    /// Disables tracing, causing all subsequent calls to record events to be ignored.
    /// </summary>
    public static void DisableTracing()
    {
        // Dispose the current writer if it holds unmanaged resources (e.g., file handles)
        // so that trace files can be accessed immediately after disabling tracing.
        if (_current is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Swallow any disposal errors; tracing teardown should never throw.
            }
        }

        _current = NoOpTraceWriter.Instance;
    }

    /// <summary>
    /// Records a trace event for the current AgentSession, if any and if tracing is enabled.
    /// Convenience helper so callers don't have to construct TraceEvent manually.
    /// </summary>
    public static void Record(
        TraceEventType type,
        object payload,
        string? agentName = null,
        string? conversationId = null)
    {
        var writer = _current;
        if (!writer.IsEnabled)
            return;

        var ctx = AgentSession.Current;
        if (ctx == null)
            return;

        var stepIndex = ctx.GetNextStepIndex();
        var evt = new TraceEvent(
            ctx.SessionId,
            stepIndex,
            DateTime.UtcNow,
            type,
            payload,
            agentName,
            conversationId);

        writer.Write(evt);
    }
}

/// <summary>
/// Global configuration for tracing behavior. This provides simple knobs
/// for hosts and IDEs without requiring changes in core runtime logic.
/// </summary>
public static class TracingConfig
{
    /// <summary>
    /// When true, hosts may choose to enable tracing automatically for
    /// new agents/sessions. The core runtime does not enforce this flag.
    /// </summary>
    public static bool EnabledByDefault { get; set; } = false;

    /// <summary>
    /// Default base directory for trace files when none is provided
    /// explicitly. Defaults to a "traces" subdirectory of the current
    /// working directory.
    /// </summary>
    public static string BaseDirectory { get; set; } =
        Path.Combine(Environment.CurrentDirectory, "traces");

    /// <summary>
    /// When true, tracing infrastructure should avoid recording obvious
    /// secrets (such as raw environment variable values) when emitting
    /// payloads. v1 implementation is conservative and focuses on not
    /// adding new secret surfaces rather than deep inspection.
    /// </summary>
    public static bool RedactSecrets { get; set; } = true;
}


