// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Lightweight view model for trace events, for binding in IDE trace viewers.

namespace MaldaLang.TraceViewer;

using MaldaLang.Runtime.Tracing;

/// <summary>
/// View model for a single trace event in the trace viewer UI.
/// </summary>
public sealed class TraceEventViewModel
{
    public int StepIndex { get; }
    public DateTime TimestampUtc { get; }
    public string Type { get; }
    public string? AgentName { get; }
    public string Summary { get; }
    public TraceEvent RawEvent { get; }

    public TraceEventViewModel(
        int stepIndex,
        DateTime timestampUtc,
        string type,
        string? agentName,
        string summary,
        TraceEvent rawEvent)
    {
        StepIndex = stepIndex;
        TimestampUtc = timestampUtc;
        Type = type ?? "";
        AgentName = agentName;
        Summary = summary ?? "";
        RawEvent = rawEvent ?? throw new ArgumentNullException(nameof(rawEvent));
    }
}
