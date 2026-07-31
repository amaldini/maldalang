// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Helper methods for the `malda trace` CLI subcommands. Kept separate from
// Program.Main to make them easily testable.

namespace MaldaLang;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MaldaLang.Runtime.Tracing;
using MaldaLang.TraceViewer;

public static class TraceCli
{
    /// <summary>
    /// Prints a high-level summary of a trace file: counts, per-type stats, and RunCommand stats.
    /// </summary>
    public static int Summary(string traceFile, TextWriter output, TextWriter error)
    {
        if (string.IsNullOrWhiteSpace(traceFile))
        {
            error.WriteLine("Error: Trace file path is required.");
            return 1;
        }

        if (!File.Exists(traceFile))
        {
            error.WriteLine($"Error: Trace file not found: {traceFile}");
            return 1;
        }

        var events = TraceLog.Load(traceFile).ToList();
        if (events.Count == 0)
        {
            output.WriteLine("No events found in trace.");
            return 0;
        }

        var total = events.Count;
        var byType = events
            .GroupBy(e => e.Type)
            .OrderBy(g => g.Key.ToString())
            .ToList();

        var uniqueAgents = events
            .Select(e => e.AgentName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal)
            .Count();

        // RunCommand stats
        var runCommandEvents = events.Where(e => e.Type == TraceEventType.RunCommand).ToList();
        var durations = new List<int>();
        var runSuccess = 0;
        var runFailure = 0;

        foreach (var evt in runCommandEvents)
        {
            if (!TryGetPayload(evt, out var payload))
                continue;

            int? durationMs = null;
            int? exitCode = null;

            if (payload.TryGetProperty("durationMs", out var durProp) &&
                durProp.ValueKind == System.Text.Json.JsonValueKind.Number &&
                durProp.TryGetInt32(out var d))
            {
                durationMs = d;
            }

            if (payload.TryGetProperty("exitCode", out var exitProp) &&
                exitProp.ValueKind == System.Text.Json.JsonValueKind.Number &&
                exitProp.TryGetInt32(out var ec))
            {
                exitCode = ec;
            }

            if (durationMs.HasValue)
            {
                durations.Add(durationMs.Value);
            }

            if (exitCode.HasValue)
            {
                if (exitCode.Value == 0)
                    runSuccess++;
                else
                    runFailure++;
            }
        }

        output.WriteLine($"Trace summary for: {traceFile}");
        output.WriteLine($"  Total events: {total}");
        output.WriteLine($"  Unique agents: {uniqueAgents}");
        output.WriteLine("  Events by type:");
        foreach (var g in byType)
        {
            output.WriteLine($"    {g.Key}: {g.Count()}");
        }

        if (runCommandEvents.Count > 0)
        {
            output.WriteLine("  RunCommand stats:");
            output.WriteLine($"    Events: {runCommandEvents.Count}");
            output.WriteLine($"    Success: {runSuccess}, Failure: {runFailure}");

            if (durations.Count > 0)
            {
                var min = durations.Min();
                var max = durations.Max();
                var avg = durations.Average();
                output.WriteLine($"    DurationMs: min={min}, max={max}, avg={avg.ToString("F2", CultureInfo.InvariantCulture)}");
            }
        }

        return 0;
    }

    /// <summary>
    /// Restores file state from a trace into the specified output directory using the final snapshot.
    /// This is a CLI-friendly wrapper around TraceReplayEngine.RestoreStateToStep.
    /// </summary>
    public static int Replay(string traceFile, string outputDirectory, TextWriter output, TextWriter error)
    {
        if (string.IsNullOrWhiteSpace(traceFile))
        {
            error.WriteLine("Error: Trace file path is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            error.WriteLine("Error: Output directory is required.");
            return 1;
        }

        if (!File.Exists(traceFile))
        {
            error.WriteLine($"Error: Trace file not found: {traceFile}");
            return 1;
        }

        try
        {
            var events = TraceLog.Load(traceFile).OrderBy(e => e.StepIndex).ToList();
            if (events.Count == 0)
            {
                output.WriteLine("No events found in trace. Nothing to restore.");
                return 0;
            }

            Directory.CreateDirectory(outputDirectory);

            var replay = new ReplayContext(events);
            var lastIndex = events[events.Count - 1].StepIndex;

            var restored = TraceReplayEngine.RestoreStateToStep(replay, lastIndex, outputDirectory);

            output.WriteLine($"Restored {restored.Count} file(s) to: {outputDirectory}");
            foreach (var path in restored)
            {
                output.WriteLine($"  {path}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            error.WriteLine($"Error: Failed to replay trace. {ex.Message}");
            return 1;
        }
    }

    public static int Show(
        string traceFile,
        int from,
        int to,
        TraceEventType? typeFilter,
        TextWriter output,
        TextWriter error)
    {
        if (string.IsNullOrWhiteSpace(traceFile))
        {
            error.WriteLine("Error: Trace file path is required.");
            return 1;
        }

        if (!File.Exists(traceFile))
        {
            error.WriteLine($"Error: Trace file not found: {traceFile}");
            return 1;
        }

        if (from < 0) from = 0;
        if (to < from) to = from + 50;

        var events = TraceLog.Load(traceFile).OrderBy(e => e.StepIndex).ToList();
        if (events.Count == 0)
        {
            output.WriteLine("No events found in trace.");
            return 0;
        }

        if (to >= events.Count)
            to = events.Count - 1;

        var replay = new ReplayContext(events);
        if (from > 0)
        {
            replay.StepTo(from - 1);
        }

        output.WriteLine($"Trace events {from}..{to} from: {traceFile}");

        for (var i = from; i <= to; i++)
        {
            replay.Step();
            var evt = replay.CurrentEvent!;

            if (typeFilter.HasValue && evt.Type != typeFilter.Value)
                continue;

            var summary = GetEventSummary(evt);
            output.WriteLine(
                $"{evt.StepIndex,4} | {evt.TimestampUtc:O} | {evt.Type,-14} | {evt.AgentName ?? "-",-12} | {summary}");
        }

        return 0;
    }

    /// <summary>Returns a short summary string for a trace event, for display in CLI or IDE.</summary>
    public static string GetEventSummary(TraceEvent evt) => TraceSummaryHelper.Summarize(evt);

    private static bool TryGetPayload(TraceEvent evt, out System.Text.Json.JsonElement payload)
    {
        // Payload is stored as JSON string when loaded via TraceLog.
        if (evt.Payload is string json && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                payload = doc.RootElement.Clone();
                return true;
            }
            catch
            {
                payload = default;
                return false;
            }
        }

        payload = default;
        return false;
    }
}

