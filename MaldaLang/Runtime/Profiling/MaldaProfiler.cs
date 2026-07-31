// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Profiling;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public enum ProfilingFormat
{
    Text,
    Json,
    Both
}

public enum ProfileEventKind
{
    Statement,
    Function,
    BuiltIn
}

public sealed class ProfilingOptions
{
    public bool Enabled { get; init; }
    public string? OutputPath { get; init; }
    public ProfilingFormat Format { get; init; } = ProfilingFormat.Text;
    public bool WriteToConsole { get; init; } = true;
    public int MaxEntriesPerSection { get; init; } = 20;

    /// <summary>
    /// When greater than zero, writes the same profile output file(s) on this wall-clock interval (seconds)
    /// while the session is still running, so interrupted runs retain partial data. Final write on session complete still occurs.
    /// </summary>
    public double PeriodicSnapshotSeconds { get; init; }

    public static ProfilingOptions Disabled { get; } = new()
    {
        Enabled = false,
        WriteToConsole = false
    };

    public ProfilingOptions Clone()
    {
        return new ProfilingOptions
        {
            Enabled = Enabled,
            OutputPath = OutputPath,
            Format = Format,
            WriteToConsole = WriteToConsole,
            MaxEntriesPerSection = MaxEntriesPerSection,
            PeriodicSnapshotSeconds = PeriodicSnapshotSeconds
        };
    }
}

public sealed class ProfilingEntry
{
    public string Name { get; init; } = string.Empty;
    public string? File { get; init; }
    public int? Line { get; init; }
    public long Calls { get; init; }
    public double TotalMs { get; init; }
    public double SelfMs { get; init; }
    public double AvgMs { get; init; }
}

public sealed class ProfilingReport
{
    public string? SessionName { get; init; }
    public DateTime StartedUtc { get; init; }
    public DateTime FinishedUtc { get; init; }
    public double TotalMs { get; init; }
    /// <summary>True when this report was written mid-session (periodic snapshot); false for the final summary.</summary>
    public bool Partial { get; init; }
    public IReadOnlyList<ProfilingEntry> BuiltIns { get; init; } = Array.Empty<ProfilingEntry>();
    public IReadOnlyList<ProfilingEntry> Functions { get; init; } = Array.Empty<ProfilingEntry>();
    public IReadOnlyList<ProfilingEntry> Statements { get; init; } = Array.Empty<ProfilingEntry>();
}

public readonly struct ProfileToken
{
    internal ProfileToken(ProfileSession session, ProfileFrame frame, long startTimestamp)
    {
        Session = session;
        Frame = frame;
        StartTimestamp = startTimestamp;
    }

    internal ProfileSession? Session { get; }
    internal ProfileFrame? Frame { get; }
    internal long StartTimestamp { get; }
    public bool IsActive => Session != null && Frame != null;
}

internal sealed class ProfileSession
{
    private readonly ConcurrentDictionary<ProfileKey, MutableProfileStats> _stats = new();
    private readonly ConcurrentDictionary<ProfileFrame, byte> _activeFrames = new();
    private readonly object _periodicSnapshotGate = new();
    private readonly Timer? _periodicSnapshotTimer;

    public ProfileSession(ProfilingOptions options, string? sessionName)
    {
        Options = options;
        SessionName = sessionName;
        StartedUtc = DateTime.UtcNow;
        StartTimestamp = Stopwatch.GetTimestamp();
        if (options.PeriodicSnapshotSeconds > 0.0 && !string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var interval = TimeSpan.FromSeconds(options.PeriodicSnapshotSeconds);
            // First fire immediately so profile.json exists even if the process is killed before the first interval elapses.
            _periodicSnapshotTimer = new Timer(
                static state => ((ProfileSession)state!).WritePeriodicSnapshotFromTimer(),
                this,
                TimeSpan.Zero,
                interval);
        }
    }

    public ProfilingOptions Options { get; }
    public string? SessionName { get; }
    public DateTime StartedUtc { get; }
    public long StartTimestamp { get; }
    public DateTime FinishedUtc { get; private set; }
    public long EndTimestamp { get; private set; }

    public void Complete()
    {
        lock (_periodicSnapshotGate)
        {
            if (EndTimestamp != 0)
            {
                return;
            }

            FinishedUtc = DateTime.UtcNow;
            EndTimestamp = Stopwatch.GetTimestamp();
            _periodicSnapshotTimer?.Dispose();
        }
    }

    public void Record(ProfileEventKind kind, string name, string? file, int? line, long elapsedTicks, long selfTicks)
    {
        var key = new ProfileKey(kind, name, file, line);
        var stats = _stats.GetOrAdd(key, static _ => new MutableProfileStats());
        stats.Add(elapsedTicks, selfTicks);
    }

    public void RegisterActiveFrame(ProfileFrame frame)
    {
        _activeFrames.TryAdd(frame, 0);
    }

    public void UnregisterActiveFrame(ProfileFrame frame)
    {
        _activeFrames.TryRemove(frame, out _);
    }

    public ProfilingReport CreateReport()
    {
        if (EndTimestamp == 0)
        {
            Complete();
        }

        return BuildReport(EndTimestamp, FinishedUtc, partial: false);
    }

    /// <summary>Snapshot of current stats without ending the session (for periodic file writes).</summary>
    public ProfilingReport CreateSnapshotReport()
    {
        var nowUtc = DateTime.UtcNow;
        var nowTicks = Stopwatch.GetTimestamp();
        return BuildReport(nowTicks, nowUtc, partial: true);
    }

    private ProfilingReport BuildReport(long endTicks, DateTime finishedUtc, bool partial)
    {
        var allEntries = BuildEntries(endTicks, partial)
            .OrderByDescending(static entry => entry.TotalMs)
            .ThenBy(static entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        var limit = Options.MaxEntriesPerSection <= 0 ? int.MaxValue : Options.MaxEntriesPerSection;

        return new ProfilingReport
        {
            SessionName = SessionName,
            StartedUtc = StartedUtc,
            FinishedUtc = finishedUtc,
            TotalMs = ToMilliseconds(endTicks - StartTimestamp),
            Partial = partial,
            BuiltIns = allEntries.Where(static entry => entry.Kind == ProfileEventKind.BuiltIn).Take(limit).Select(static entry => entry.AsPublic()).ToArray(),
            Functions = allEntries.Where(static entry => entry.Kind == ProfileEventKind.Function).Take(limit).Select(static entry => entry.AsPublic()).ToArray(),
            Statements = allEntries.Where(static entry => entry.Kind == ProfileEventKind.Statement).Take(limit).Select(static entry => entry.AsPublic()).ToArray()
        };
    }

    private IEnumerable<ProfileEntryInternal> BuildEntries(long endTicks, bool includeActiveFrames)
    {
        var aggregates = new Dictionary<ProfileKey, MutableAggregateStats>();
        foreach (var kvp in _stats)
        {
            var snapshot = kvp.Value.Snapshot();
            GetOrCreateAggregate(aggregates, kvp.Key).Add(snapshot.Calls, snapshot.ElapsedTicks, snapshot.SelfTicks);
        }

        if (includeActiveFrames)
        {
            AddActiveFrameContributions(aggregates, endTicks);
        }

        return aggregates.Select(kvp => kvp.Value.ToEntry(kvp.Key));
    }

    private void AddActiveFrameContributions(Dictionary<ProfileKey, MutableAggregateStats> aggregates, long endTicks)
    {
        var activeFrames = _activeFrames.Keys.Where(static frame => !frame.IsCompleted).ToArray();
        if (activeFrames.Length == 0)
        {
            return;
        }

        var activeElapsedByFrame = new Dictionary<ProfileFrame, long>(activeFrames.Length);
        var activeChildTicksByParent = new Dictionary<ProfileFrame, long>();

        foreach (var frame in activeFrames)
        {
            var elapsedTicks = Math.Max(0, endTicks - frame.StartTimestamp);
            activeElapsedByFrame[frame] = elapsedTicks;

            if (frame.Parent != null && !frame.Parent.IsCompleted)
            {
                activeChildTicksByParent.TryGetValue(frame.Parent, out var currentChildTicks);
                activeChildTicksByParent[frame.Parent] = currentChildTicks + elapsedTicks;
            }
        }

        foreach (var frame in activeFrames)
        {
            var elapsedTicks = activeElapsedByFrame[frame];
            var completedChildTicks = Interlocked.Read(ref frame.ChildTicks);
            activeChildTicksByParent.TryGetValue(frame, out var activeChildTicks);
            var selfTicks = Math.Max(0, elapsedTicks - completedChildTicks - activeChildTicks);
            var key = new ProfileKey(frame.Kind, frame.Name, frame.File, frame.Line);
            GetOrCreateAggregate(aggregates, key).Add(1, elapsedTicks, selfTicks);
        }
    }

    private static MutableAggregateStats GetOrCreateAggregate(Dictionary<ProfileKey, MutableAggregateStats> aggregates, ProfileKey key)
    {
        if (!aggregates.TryGetValue(key, out var aggregate))
        {
            aggregate = new MutableAggregateStats();
            aggregates[key] = aggregate;
        }

        return aggregate;
    }

    private void WritePeriodicSnapshotFromTimer()
    {
        lock (_periodicSnapshotGate)
        {
            if (EndTimestamp != 0 || Options.PeriodicSnapshotSeconds <= 0.0 || string.IsNullOrWhiteSpace(Options.OutputPath))
            {
                return;
            }

            try
            {
                var report = CreateSnapshotReport();
                MaldaProfiler.WriteOutputs(Options, report, TextWriter.Null);
            }
            catch
            {
                // best-effort; profiling must not break the program
            }
        }
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private readonly record struct ProfileKey(ProfileEventKind Kind, string Name, string? File, int? Line);

    private sealed class MutableProfileStats
    {
        private long _calls;
        private long _elapsedTicks;
        private long _selfTicks;

        public void Add(long elapsedTicks, long selfTicks)
        {
            Interlocked.Increment(ref _calls);
            Interlocked.Add(ref _elapsedTicks, elapsedTicks);
            Interlocked.Add(ref _selfTicks, selfTicks);
        }

        public (long Calls, long ElapsedTicks, long SelfTicks) Snapshot()
        {
            return (
                Interlocked.Read(ref _calls),
                Interlocked.Read(ref _elapsedTicks),
                Interlocked.Read(ref _selfTicks));
        }

        public ProfileEntryInternal ToEntry(ProfileKey key)
        {
            var calls = Interlocked.Read(ref _calls);
            var elapsedTicks = Interlocked.Read(ref _elapsedTicks);
            var selfTicks = Interlocked.Read(ref _selfTicks);
            var totalMs = ToMilliseconds(elapsedTicks);
            return new ProfileEntryInternal(
                key.Kind,
                key.Name,
                key.File,
                key.Line,
                calls,
                totalMs,
                ToMilliseconds(selfTicks),
                calls == 0 ? 0 : totalMs / calls);
        }
    }

    private sealed class MutableAggregateStats
    {
        private long _calls;
        private long _elapsedTicks;
        private long _selfTicks;

        public void Add(long calls, long elapsedTicks, long selfTicks)
        {
            _calls += calls;
            _elapsedTicks += elapsedTicks;
            _selfTicks += selfTicks;
        }

        public ProfileEntryInternal ToEntry(ProfileKey key)
        {
            var totalMs = ToMilliseconds(_elapsedTicks);
            return new ProfileEntryInternal(
                key.Kind,
                key.Name,
                key.File,
                key.Line,
                _calls,
                totalMs,
                ToMilliseconds(_selfTicks),
                _calls == 0 ? 0 : totalMs / _calls);
        }
    }

    private readonly record struct ProfileEntryInternal(
        ProfileEventKind Kind,
        string Name,
        string? File,
        int? Line,
        long Calls,
        double TotalMs,
        double SelfMs,
        double AvgMs)
    {
        public ProfilingEntry AsPublic()
        {
            return new ProfilingEntry
            {
                Name = Name,
                File = File,
                Line = Line,
                Calls = Calls,
                TotalMs = TotalMs,
                SelfMs = SelfMs,
                AvgMs = AvgMs
            };
        }
    }
}

internal sealed class ProfileFrame
{
    private int _isCompleted;

    public ProfileFrame(ProfileEventKind kind, string name, string? file, int? line, ProfileFrame? parent, long startTimestamp)
    {
        Kind = kind;
        Name = name;
        File = file;
        Line = line;
        Parent = parent;
        StartTimestamp = startTimestamp;
    }

    public ProfileEventKind Kind { get; }
    public string Name { get; }
    public string? File { get; }
    public int? Line { get; }
    public ProfileFrame? Parent { get; }
    public long StartTimestamp { get; }
    public long ChildTicks;
    public bool IsCompleted => Volatile.Read(ref _isCompleted) != 0;

    public void MarkCompleted()
    {
        Interlocked.Exchange(ref _isCompleted, 1);
    }
}

public static class MaldaProfiler
{
    private static readonly AsyncLocal<ProfileSession?> _currentSession = new();
    private static readonly AsyncLocal<ProfileFrame?> _currentFrame = new();

    /// <summary>
    /// Process-wide strong reference to the active session. Used when:
    /// <list type="bullet">
    /// <item><description><see cref="AppDomain.ProcessExit"/> (AsyncLocal is unavailable).</description></item>
    /// <item><description>Thread-pool / HTTP handlers: execution context often does not carry the host <see cref="AsyncLocal{T}"/> from <c>async Task Main</c>, so <see cref="_currentSession"/> is null even though profiling is enabled.</description></item>
    /// </list>
    /// </summary>
    private static ProfileSession? _shutdownSession;

    static MaldaProfiler()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    /// <summary>Logical session for the current thread: AsyncLocal first, then the process-wide profiling session.</summary>
    private static ProfileSession? ResolveSessionForEnter()
    {
        var s = _currentSession.Value ?? _shutdownSession;
        return s is { Options.Enabled: true } ? s : null;
    }

    public static bool IsEnabled => ResolveSessionForEnter()?.Options.Enabled == true;

    public static void StartSession(ProfilingOptions? options, string? sessionName = null)
    {
        _currentFrame.Value = null;
        if (options == null || !options.Enabled)
        {
            _currentSession.Value = null;
            _shutdownSession = null;
            return;
        }

        var session = new ProfileSession(options.Clone(), sessionName);
        _currentSession.Value = session;
        _shutdownSession = session;
    }

    public static ProfilingReport? CompleteSession(TextWriter? writer = null)
    {
        // Prefer AsyncLocal, but async Main continuations may have lost it while _shutdownSession still holds the session.
        var session = _currentSession.Value ?? _shutdownSession;
        _currentSession.Value = null;
        _currentFrame.Value = null;
        if (session == null)
        {
            return null;
        }

        if (ReferenceEquals(_shutdownSession, session))
        {
            _shutdownSession = null;
        }

        try
        {
            session.Complete();
            var report = session.CreateReport();
            WriteOutputs(session.Options, report, writer ?? Console.Out);
            return report;
        }
        catch
        {
            return null;
        }
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        var session = _shutdownSession;
        if (session == null)
        {
            return;
        }

        try
        {
            session.Complete();
            var report = session.CreateReport();
            WriteOutputs(session.Options, report, TextWriter.Null);
        }
        catch
        {
        }
        finally
        {
            if (ReferenceEquals(_shutdownSession, session))
            {
                _shutdownSession = null;
            }
        }
    }

    public static ProfileToken EnterStatement(string? file, int line, string name)
    {
        if (line <= 0)
        {
            return default;
        }

        return Enter(ProfileEventKind.Statement, name, file, line);
    }

    public static ProfileToken EnterFunction(string name, string? file, int line)
    {
        return Enter(ProfileEventKind.Function, name, file, line);
    }

    public static ProfileToken EnterBuiltIn(string name)
    {
        return Enter(ProfileEventKind.BuiltIn, name, null, null);
    }

    public static void Exit(ProfileToken token)
    {
        if (!token.IsActive || token.Session == null || token.Frame == null)
        {
            return;
        }

        if (!CallerMayExitToken(token.Session))
        {
            return;
        }

        var elapsedTicks = Stopwatch.GetTimestamp() - token.StartTimestamp;
        var childTicks = Interlocked.Read(ref token.Frame.ChildTicks);
        var selfTicks = Math.Max(0, elapsedTicks - childTicks);

        token.Frame.MarkCompleted();
        token.Session.UnregisterActiveFrame(token.Frame);
        token.Session.Record(token.Frame.Kind, token.Frame.Name, token.Frame.File, token.Frame.Line, elapsedTicks, selfTicks);

        if (ReferenceEquals(_currentFrame.Value, token.Frame))
        {
            _currentFrame.Value = token.Frame.Parent;
        }

        if (token.Frame.Parent != null)
        {
            Interlocked.Add(ref token.Frame.Parent.ChildTicks, elapsedTicks);
        }
    }

    /// <summary>
    /// Exit is valid if this thread's AsyncLocal session matches the token, or AsyncLocal is empty but the token
    /// belongs to the process-wide profiling session (typical on thread-pool / HTTP worker threads).
    /// </summary>
    private static bool CallerMayExitToken(ProfileSession tokenSession)
    {
        var asyncSession = _currentSession.Value;
        if (ReferenceEquals(asyncSession, tokenSession))
        {
            return true;
        }

        return asyncSession == null && ReferenceEquals(_shutdownSession, tokenSession);
    }

    public static T ProfileBuiltIn<T>(string name, Func<T> callback)
    {
        var token = EnterBuiltIn(name);
        try
        {
            return callback();
        }
        finally
        {
            Exit(token);
        }
    }

    public static void ProfileBuiltIn(string name, Action callback)
    {
        var token = EnterBuiltIn(name);
        try
        {
            callback();
        }
        finally
        {
            Exit(token);
        }
    }

    public static async Task<T> ProfileBuiltInAsync<T>(string name, Func<Task<T>> callback)
    {
        var token = EnterBuiltIn(name);
        try
        {
            return await callback();
        }
        finally
        {
            Exit(token);
        }
    }

    public static async Task ProfileBuiltInAsync(string name, Func<Task> callback)
    {
        var token = EnterBuiltIn(name);
        try
        {
            await callback();
        }
        finally
        {
            Exit(token);
        }
    }

    private static ProfileToken Enter(ProfileEventKind kind, string name, string? file, int? line)
    {
        var session = ResolveSessionForEnter();
        if (session == null)
        {
            return default;
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        var frame = new ProfileFrame(kind, name, file, line, _currentFrame.Value, startTimestamp);
        session.RegisterActiveFrame(frame);
        _currentFrame.Value = frame;
        return new ProfileToken(session, frame, startTimestamp);
    }

    internal static void WriteOutputs(ProfilingOptions options, ProfilingReport report, TextWriter consoleWriter)
    {
        // Periodic snapshots: only write files, avoid flooding the console.
        if (options.WriteToConsole && !report.Partial && (options.Format == ProfilingFormat.Text || options.Format == ProfilingFormat.Both))
        {
            consoleWriter.WriteLine(FormatText(report));
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return;
        }

        var targets = ResolveOutputTargets(options.OutputPath!, options.Format);
        if (targets.TextPath != null)
        {
            EnsureParentDirectory(targets.TextPath);
            File.WriteAllText(targets.TextPath, FormatText(report));
        }

        if (targets.JsonPath != null)
        {
            EnsureParentDirectory(targets.JsonPath);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(targets.JsonPath, json);
        }
    }

    private static (string? TextPath, string? JsonPath) ResolveOutputTargets(string outputPath, ProfilingFormat format)
    {
        if (format == ProfilingFormat.Text)
        {
            return (outputPath, null);
        }

        if (format == ProfilingFormat.Json)
        {
            return (null, outputPath);
        }

        var basePath = outputPath;
        var extension = Path.GetExtension(outputPath);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            basePath = outputPath[..^extension.Length];
        }

        return ($"{basePath}.txt", $"{basePath}.json");
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public static string FormatText(ProfilingReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MALDA profile summary");
        if (report.Partial)
        {
            sb.AppendLine("(partial snapshot — session still running)");
        }
        if (!string.IsNullOrWhiteSpace(report.SessionName))
        {
            sb.Append("Session: ");
            sb.AppendLine(report.SessionName);
        }
        sb.Append("Total runtime: ");
        sb.Append(report.TotalMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        sb.AppendLine(" ms");

        AppendSection(sb, "Top built-ins", report.BuiltIns);
        AppendSection(sb, "Top functions", report.Functions);
        AppendSection(sb, "Top statements", report.Statements);

        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<ProfilingEntry> entries)
    {
        sb.AppendLine();
        sb.AppendLine(title);
        if (entries.Count == 0)
        {
            sb.AppendLine("  (none)");
            return;
        }

        foreach (var entry in entries)
        {
            sb.Append("  ");
            sb.Append(entry.Name);
            if (!string.IsNullOrWhiteSpace(entry.File))
            {
                sb.Append(" @ ");
                sb.Append(entry.File);
                if (entry.Line.HasValue)
                {
                    sb.Append(':');
                    sb.Append(entry.Line.Value);
                }
            }
            else if (entry.Line.HasValue)
            {
                sb.Append(" @ line ");
                sb.Append(entry.Line.Value);
            }

            sb.Append(" | calls=");
            sb.Append(entry.Calls);
            sb.Append(" totalMs=");
            sb.Append(entry.TotalMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(" selfMs=");
            sb.Append(entry.SelfMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(" avgMs=");
            sb.Append(entry.AvgMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine();
        }
    }
}
