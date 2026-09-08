// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.LlmCassettes;

using MaldaLang.Interpreter;
using MaldaLang.Runtime.Journal;

/// <summary>
/// Shared record/replay gate for every LLM <c>Chat</c> path.
/// </summary>
public static class CassetteTransport
{
    public const string RecordEnv = "MALDA_RECORD";
    public const string ReplayEnv = "MALDA_REPLAY";
    public const string StrictEnv = "MALDA_REPLAY_STRICT";

    private static readonly object Sync = new();
    private static CassetteStore? _recordStore;
    private static CassetteStore? _replayStore;
    private static string? _recordPath;
    private static string? _replayPath;

    /// <summary>Optional override for tests; when set, env vars are ignored.</summary>
    public static string? RecordPathOverride { get; set; }
    public static string? ReplayPathOverride { get; set; }
    public static bool? StrictOverride { get; set; }

    public static bool IsRecording => !string.IsNullOrWhiteSpace(ResolveRecordPath());
    public static bool IsReplaying => !string.IsNullOrWhiteSpace(ResolveReplayPath());
    public static bool IsStrict
    {
        get
        {
            if (StrictOverride.HasValue)
                return StrictOverride.Value;
            var raw = System.Environment.GetEnvironmentVariable(StrictEnv);
            return string.Equals(raw, "1", StringComparison.Ordinal)
                || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void ResetForTesting()
    {
        lock (Sync)
        {
            _recordStore = null;
            _replayStore = null;
            _recordPath = null;
            _replayPath = null;
            RecordPathOverride = null;
            ReplayPathOverride = null;
            StrictOverride = null;
        }
    }

    public static RuntimeValue Execute(CassetteRequest request, Func<RuntimeValue> liveCall)
    {
        Interpreter.CurrentCancelToken.ThrowIfCancellationRequested();
        var key = CassetteKey.Compute(request);

        if (IsReplaying)
        {
            var replay = GetReplayStore();
            if (replay != null && replay.TryGet(key, out var recorded))
                return recorded;

            RunJournal.Current.Append(new JournalEvent
            {
                Kind = JournalKind.Prompt,
                Name = "cassette_miss",
                PromptHash = request.PromptHash,
                Model = request.Model,
                Ok = false,
                Error = key
            });

            if (IsStrict)
                throw new RuntimeException($"Cassette miss (MALDA_REPLAY_STRICT): {key}");

            Console.Error.WriteLine($"malda: cassette miss, falling through to live client ({key[..Math.Min(12, key.Length)]}…)");
        }

        var result = liveCall();

        if (IsRecording)
        {
            var record = GetRecordStore();
            record?.Append(key, request, result);
        }

        return result;
    }

    public static CassetteRequest RequestFromRuntime(
        RuntimeValue messages,
        RuntimeValue? tools,
        RuntimeValue? responseFormat,
        string? model,
        string mode = "A",
        string? promptHash = null)
    {
        return new CassetteRequest(
            Messages: CassetteJson.ToElement(messages),
            Tools: tools == null || tools.Value == null ? null : CassetteJson.ToElement(tools),
            ResponseFormat: responseFormat == null || responseFormat.Value == null
                ? null
                : CassetteJson.ToElement(responseFormat),
            Model: model,
            Mode: mode,
            PromptHash: promptHash,
            SchemaHash: responseFormat == null || responseFormat.Value == null
                ? null
                : PromptHasher.Hash(CassetteJson.ToJsonString(responseFormat)));
    }

    private static string? ResolveRecordPath() =>
        RecordPathOverride ?? System.Environment.GetEnvironmentVariable(RecordEnv);

    private static string? ResolveReplayPath() =>
        ReplayPathOverride ?? System.Environment.GetEnvironmentVariable(ReplayEnv);

    private static CassetteStore? GetRecordStore()
    {
        var path = ResolveRecordPath();
        if (string.IsNullOrWhiteSpace(path))
            return null;
        lock (Sync)
        {
            if (_recordStore == null || !string.Equals(_recordPath, path, StringComparison.Ordinal))
            {
                _recordPath = path;
                _recordStore = new CassetteStore(path);
            }

            return _recordStore;
        }
    }

    private static CassetteStore? GetReplayStore()
    {
        var path = ResolveReplayPath();
        if (string.IsNullOrWhiteSpace(path))
            return null;
        lock (Sync)
        {
            if (_replayStore == null || !string.Equals(_replayPath, path, StringComparison.Ordinal))
            {
                _replayPath = path;
                _replayStore = new CassetteStore(path);
            }

            return _replayStore;
        }
    }
}
