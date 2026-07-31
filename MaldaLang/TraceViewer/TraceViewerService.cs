// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Shared trace inspection service for IDE trace viewers (Desktop WPF and Web Blazor).
// Uses TraceLog and ReplayContext to load trace files and produce view models for UI binding.

namespace MaldaLang.TraceViewer;

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang;
using MaldaLang.Runtime.Tracing;

/// <summary>
/// Loads trace files and builds view-model sessions for the trace viewer UI.
/// </summary>
public static class TraceViewerService
{
    /// <summary>
    /// Loads a trace file and returns a session with events, replay context, and metadata.
    /// </summary>
    /// <param name="filePath">Path to a .malda-trace.jsonl file.</param>
    /// <returns>A trace viewer session with events, context, session id, agent names, and time span.</returns>
    public static TraceViewerSession LoadTrace(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Trace file path cannot be null or empty.", nameof(filePath));

        var rawEvents = TraceLog.Load(filePath).OrderBy(e => e.StepIndex).ToList();
        return BuildSession(rawEvents);
    }

    /// <summary>
    /// Loads trace from JSONL content (e.g. from Web IDE file picker) and returns a session.
    /// </summary>
    public static TraceViewerSession LoadTraceFromContent(string jsonlContent)
    {
        if (jsonlContent == null)
            throw new ArgumentNullException(nameof(jsonlContent));

        var rawEvents = TraceLog.LoadFromContent(jsonlContent).OrderBy(e => e.StepIndex).ToList();
        return BuildSession(rawEvents);
    }

    private static TraceViewerSession BuildSession(List<TraceEvent> rawEvents)
    {
        var context = new ReplayContext(rawEvents);

        var viewModels = new List<TraceEventViewModel>(rawEvents.Count);
        var agentNameSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var evt in rawEvents)
        {
            if (!string.IsNullOrEmpty(evt.AgentName))
                agentNameSet.Add(evt.AgentName);

            var summary = TraceSummaryHelper.Summarize(evt);
            viewModels.Add(new TraceEventViewModel(
                evt.StepIndex,
                evt.TimestampUtc,
                evt.Type.ToString(),
                evt.AgentName,
                summary,
                evt));
        }

        string? sessionId = rawEvents.Count > 0 ? rawEvents[0].SessionId : null;
        var agentNames = agentNameSet.OrderBy(n => n, StringComparer.Ordinal).ToList();
        DateTime? startUtc = rawEvents.Count > 0 ? rawEvents[0].TimestampUtc : null;
        DateTime? endUtc = rawEvents.Count > 0 ? rawEvents[rawEvents.Count - 1].TimestampUtc : null;

        return new TraceViewerSession(
            viewModels,
            context,
            sessionId,
            agentNames,
            startUtc,
            endUtc);
    }
}
