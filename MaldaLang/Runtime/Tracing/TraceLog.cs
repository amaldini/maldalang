// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Helpers for loading and querying trace files written by FileTraceWriter.

namespace MaldaLang.Runtime.Tracing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public static class TraceLog
{
    /// <summary>
    /// Loads all trace events from a single .malda-trace.jsonl file.
    /// Each line is expected to match the JSON schema written by FileTraceWriter.
    /// </summary>
    /// <param name="filePath">Path to the trace file.</param>
    /// <returns>An enumerable sequence of <see cref="TraceEvent"/> instances.</returns>
    public static IEnumerable<TraceEvent> Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Trace file path cannot be null or empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Trace file not found.", filePath);

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (TryParseLine(line, out var traceEvent))
                yield return traceEvent;
        }
    }

    /// <summary>
    /// Loads trace events from JSONL content (e.g. from an in-memory string or uploaded file).
    /// </summary>
    public static IEnumerable<TraceEvent> LoadFromContent(string jsonlContent)
    {
        if (string.IsNullOrEmpty(jsonlContent))
            yield break;

        foreach (var line in jsonlContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (TryParseLine(line, out var traceEvent))
                yield return traceEvent;
        }
    }

    private static bool TryParseLine(string line, out TraceEvent traceEvent)
    {
        traceEvent = default!;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var sessionId = root.GetProperty("sessionId").GetString() ?? string.Empty;
            var stepIndex = root.GetProperty("stepIndex").GetInt32();
            var timestampString = root.GetProperty("timestampUtc").GetString() ?? string.Empty;
            var typeString = root.GetProperty("type").GetString() ?? string.Empty;
            var agentName = root.TryGetProperty("agentName", out var an) ? an.GetString() : null;
            var conversationId = root.TryGetProperty("conversationId", out var cn) ? cn.GetString() : null;

            if (!Enum.TryParse<TraceEventType>(typeString, ignoreCase: true, out var eventType))
            {
                // Unknown event type - skip rather than failing the whole load.
                return false;
            }

            DateTime timestampUtc;
            if (!DateTime.TryParse(timestampString, null, System.Globalization.DateTimeStyles.RoundtripKind, out timestampUtc))
            {
                timestampUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
            }

            string payloadJson = "{}";
            if (root.TryGetProperty("payload", out var payloadElement))
            {
                payloadJson = payloadElement.GetRawText();
            }

            traceEvent = new TraceEvent(
                sessionId,
                stepIndex,
                timestampUtc,
                eventType,
                payloadJson,
                agentName,
                conversationId);

            return true;
        }
        catch
        {
            // Skip malformed lines but continue reading the rest of the file.
            return false;
        }
    }
}

