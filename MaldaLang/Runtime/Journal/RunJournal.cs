// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Journal;

using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.LlmCassettes;

/// <summary>
/// Per-run event journal. Workflow SQLite remains the durability store; step events
/// are also mirrored here so <c>trace.journal()</c> sees the same substrate.
/// </summary>
public sealed class RunJournal
{
    private static readonly AsyncLocal<RunJournal?> CurrentLocal = new();
    private static RunJournal? _processFallback;
    private readonly List<JournalEvent> _events = new();
    private readonly Stack<string> _spans = new();
    private readonly object _sync = new();
    private RunUsage? _lastUsage;

    public static RunJournal Current
    {
        get
        {
            if (CurrentLocal.Value != null)
                return CurrentLocal.Value;
            return _processFallback ??= new RunJournal();
        }
        set => CurrentLocal.Value = value;
    }

    public static void ResetForTesting()
    {
        CurrentLocal.Value = null;
        _processFallback = new RunJournal();
        JournalExporter.ResetForTesting();
    }

    public IReadOnlyList<JournalEvent> Events
    {
        get
        {
            lock (_sync)
                return _events.ToList();
        }
    }

    public RunUsage? LastUsage
    {
        get
        {
            lock (_sync)
                return _lastUsage;
        }
    }

    public string? CurrentSpan
    {
        get
        {
            lock (_sync)
                return _spans.Count > 0 ? _spans.Peek() : null;
        }
    }

    public IDisposable PushSpan(string name)
    {
        lock (_sync)
            _spans.Push(name);
        return new SpanPop(this);
    }

    public void Append(JournalEvent evt)
    {
        evt.Span ??= CurrentSpan;
        lock (_sync)
        {
            _events.Add(evt);
            if (evt.Tokens != null || evt.Cost != null || evt.Ms != null)
                _lastUsage = evt.ToUsage();
        }

        JournalExporter.Emit(evt);
    }

    public RuntimeValue ToRuntimeArray()
    {
        var list = new List<RuntimeValue>();
        foreach (var evt in Events)
            list.Add(ToRuntimeValue(evt));
        return RuntimeValue.Array(list);
    }

    public static RuntimeValue ToRuntimeValue(JournalEvent evt)
    {
        var obj = new JsonObject();
        obj.Set("ts", RuntimeValue.String(evt.Ts.ToString("O")));
        obj.Set("span", RuntimeValue.String(evt.Span ?? ""));
        obj.Set("kind", RuntimeValue.String(evt.Kind.ToString().ToLowerInvariant()));
        obj.Set("name", RuntimeValue.String(evt.Name));
        if (evt.PromptHash != null)
            obj.Set("promptHash", RuntimeValue.String(evt.PromptHash));
        if (evt.Model != null)
            obj.Set("model", RuntimeValue.String(evt.Model));
        if (evt.Tokens != null)
        {
            var tokens = new JsonObject();
            tokens.Set("in", RuntimeValue.Integer(evt.Tokens.In));
            tokens.Set("out", RuntimeValue.Integer(evt.Tokens.Out));
            obj.Set("tokens", RuntimeValue.Object(tokens));
        }

        if (evt.Cost.HasValue)
            obj.Set("cost", RuntimeValue.Float(evt.Cost.Value));
        if (evt.Ms.HasValue)
            obj.Set("ms", RuntimeValue.Integer((int)Math.Min(evt.Ms.Value, int.MaxValue)));
        obj.Set("ok", RuntimeValue.Boolean(evt.Ok));
        if (evt.Repairs.HasValue)
            obj.Set("repairs", RuntimeValue.Integer(evt.Repairs.Value));
        if (evt.Error != null)
            obj.Set("error", RuntimeValue.String(evt.Error));
        return RuntimeValue.Object(obj);
    }

    private sealed class SpanPop : IDisposable
    {
        private readonly RunJournal _journal;
        private bool _disposed;

        public SpanPop(RunJournal journal) => _journal = journal;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            lock (_journal._sync)
            {
                if (_journal._spans.Count > 0)
                    _journal._spans.Pop();
            }
        }
    }
}

/// <summary>
/// <c>MALDA_TRACE=jsonl|otlp</c> + <c>MALDA_TRACE_FILE</c> export of journal events.
/// </summary>
public static class JournalExporter
{
    private static readonly object Sync = new();
    private static StreamWriter? _jsonl;
    private static string? _path;

    public static void ResetForTesting()
    {
        lock (Sync)
        {
            _jsonl?.Dispose();
            _jsonl = null;
            _path = null;
        }
    }

    public static void Emit(JournalEvent evt)
    {
        var mode = System.Environment.GetEnvironmentVariable("MALDA_TRACE");
        if (string.IsNullOrWhiteSpace(mode))
            return;

        if (string.Equals(mode, "jsonl", StringComparison.OrdinalIgnoreCase))
            WriteJsonl(evt);
        else if (string.Equals(mode, "otlp", StringComparison.OrdinalIgnoreCase))
            WriteOtlp(evt);
    }

    private static void WriteJsonl(JournalEvent evt)
    {
        var path = System.Environment.GetEnvironmentVariable("MALDA_TRACE_FILE");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), "malda-run-journal.jsonl");

        lock (Sync)
        {
            if (_jsonl == null || !string.Equals(_path, path, StringComparison.Ordinal))
            {
                _jsonl?.Dispose();
                _path = path;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                _jsonl = new StreamWriter(System.IO.File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };
            }

            var payload = PromptHasher.CanonicalJson(new
            {
                ts = evt.Ts,
                span = evt.Span,
                kind = evt.Kind.ToString().ToLowerInvariant(),
                name = evt.Name,
                promptHash = evt.PromptHash,
                model = evt.Model,
                tokens = evt.Tokens == null ? null : new { @in = evt.Tokens.In, @out = evt.Tokens.Out },
                cost = evt.Cost,
                ms = evt.Ms,
                ok = evt.Ok,
                repairs = evt.Repairs,
                error = evt.Error
            });
            _jsonl.WriteLine(payload);
        }
    }

    private static void WriteOtlp(JournalEvent evt)
    {
        // Minimal OTLP-shaped JSONL span (no collector dependency). Same file env.
        var path = System.Environment.GetEnvironmentVariable("MALDA_TRACE_FILE");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), "malda-run-journal.otlp.jsonl");

        lock (Sync)
        {
            if (_jsonl == null || !string.Equals(_path, path, StringComparison.Ordinal))
            {
                _jsonl?.Dispose();
                _path = path;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                _jsonl = new StreamWriter(System.IO.File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };
            }

            var payload = PromptHasher.CanonicalJson(new
            {
                name = evt.Name,
                kind = evt.Kind.ToString().ToLowerInvariant(),
                startTimeUnixNano = evt.Ts.ToUnixTimeMilliseconds() * 1_000_000L,
                endTimeUnixNano = (evt.Ts.ToUnixTimeMilliseconds() + (evt.Ms ?? 0)) * 1_000_000L,
                attributes = new Dictionary<string, object?>
                {
                    ["malda.span"] = evt.Span,
                    ["malda.promptHash"] = evt.PromptHash,
                    ["malda.model"] = evt.Model,
                    ["malda.ok"] = evt.Ok,
                    ["malda.cost"] = evt.Cost,
                    ["malda.repairs"] = evt.Repairs,
                    ["malda.error"] = evt.Error
                }
            });
            _jsonl.WriteLine(payload);
        }
    }
}
