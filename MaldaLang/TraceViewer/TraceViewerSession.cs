// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Session model for a loaded trace file, exposing events, replay context, and metadata.

namespace MaldaLang.TraceViewer;

using System.Collections.Generic;
using MaldaLang.Runtime.Tracing;

/// <summary>
/// Result of loading a trace file: events as view models, replay context, and metadata.
/// </summary>
public sealed class TraceViewerSession
{
    public IReadOnlyList<TraceEventViewModel> Events { get; }
    public ReplayContext Context { get; }
    public string? SessionId { get; }
    public IReadOnlyList<string> AgentNames { get; }
    public DateTime? StartUtc { get; }
    public DateTime? EndUtc { get; }

    public TraceViewerSession(
        IReadOnlyList<TraceEventViewModel> events,
        ReplayContext context,
        string? sessionId,
        IReadOnlyList<string> agentNames,
        DateTime? startUtc,
        DateTime? endUtc)
    {
        Events = events ?? throw new ArgumentNullException(nameof(events));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        SessionId = sessionId;
        AgentNames = agentNames ?? Array.Empty<string>();
        StartUtc = startUtc;
        EndUtc = endUtc;
    }
}
