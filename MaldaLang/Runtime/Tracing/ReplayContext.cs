// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Lightweight, read-only replay context over a sequence of TraceEvent instances.
// Used for offline inspection of traces (CLI / IDE), not for live execution.

namespace MaldaLang.Runtime.Tracing;

using System;
using System.Collections.Generic;
using System.Text.Json;

public sealed class ReplayContext
{
    private readonly IReadOnlyList<TraceEvent> _events;
    private readonly Dictionary<string, string> _files = new();
    private readonly Dictionary<string, (TraceEvent start, TraceEvent? end)> _toolCallsByCorrelationId = new();

    private int _position = -1;

    public ReplayContext(IEnumerable<TraceEvent> events)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));
        _events = new List<TraceEvent>(events);
    }

    /// <summary>
    /// Current zero-based position within the event sequence. -1 means "before first event".
    /// </summary>
    public int Position => _position;

    /// <summary>
    /// The event most recently applied by Step/StepTo, or null if none.
    /// </summary>
    public TraceEvent? CurrentEvent =>
        _position >= 0 && _position < _events.Count ? _events[_position] : null;

    /// <summary>
    /// Virtual file-system snapshot reconstructed from FileEdit events.
    /// Maps file path to last known contents.
    /// </summary>
    public IReadOnlyDictionary<string, string> Files => _files;

    public TraceEvent? LastLlmRequest { get; private set; }
    public TraceEvent? LastLlmResponse { get; private set; }
    public TraceEvent? LastToolCallStart { get; private set; }
    public TraceEvent? LastToolCallEnd { get; private set; }

    /// <summary>
    /// Resets the context to the beginning (no events applied, empty virtual state).
    /// </summary>
    public void Reset()
    {
        _position = -1;
        _files.Clear();
        _toolCallsByCorrelationId.Clear();
        LastLlmRequest = null;
        LastLlmResponse = null;
        LastToolCallStart = null;
        LastToolCallEnd = null;
    }

    /// <summary>
    /// Advances by one event and applies it to the virtual state.
    /// Returns false when there are no more events.
    /// </summary>
    public bool Step()
    {
        if (_position + 1 >= _events.Count)
            return false;

        _position++;
        ApplyEvent(_events[_position]);
        return true;
    }

    /// <summary>
    /// Fast-forwards to the given step index, applying all intermediate events.
    /// If the index is before the current position, the context is reset and replayed from the start.
    /// </summary>
    public void StepTo(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= _events.Count)
            throw new ArgumentOutOfRangeException(nameof(stepIndex));

        if (stepIndex < _position)
        {
            Reset();
        }

        while (_position < stepIndex)
        {
            Step();
        }
    }

    private void ApplyEvent(TraceEvent evt)
    {
        switch (evt.Type)
        {
            case TraceEventType.FileEdit:
                ApplyFileEdit(evt);
                break;

            case TraceEventType.LlmRequest:
                LastLlmRequest = evt;
                break;

            case TraceEventType.LlmResponse:
                LastLlmResponse = evt;
                break;

            case TraceEventType.ToolCallStart:
                ApplyToolCallStart(evt);
                break;

            case TraceEventType.ToolCallEnd:
                ApplyToolCallEnd(evt);
                break;

            default:
                // Other event types are currently not reflected in virtual state
                break;
        }
    }

    private void ApplyFileEdit(TraceEvent evt)
    {
        if (!TryGetPayload(evt, out var payload))
            return;

        if (!payload.TryGetProperty("path", out var pathProp) ||
            pathProp.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var path = pathProp.GetString() ?? string.Empty;

        if (!payload.TryGetProperty("afterContent", out var afterContentProp) ||
            (afterContentProp.ValueKind != JsonValueKind.String &&
             afterContentProp.ValueKind != JsonValueKind.Null))
        {
            return;
        }

        var afterContent = afterContentProp.ValueKind == JsonValueKind.Null
            ? null
            : afterContentProp.GetString();

        if (afterContent != null)
        {
            _files[path] = afterContent;
        }
    }

    private void ApplyToolCallStart(TraceEvent evt)
    {
        LastToolCallStart = evt;

        if (!TryGetPayload(evt, out var payload))
            return;

        if (!payload.TryGetProperty("correlationId", out var idProp) ||
            idProp.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var id = idProp.GetString();
        if (string.IsNullOrEmpty(id))
            return;

        _toolCallsByCorrelationId[id] = (evt, null);
    }

    private void ApplyToolCallEnd(TraceEvent evt)
    {
        LastToolCallEnd = evt;

        if (!TryGetPayload(evt, out var payload))
            return;

        if (!payload.TryGetProperty("correlationId", out var idProp) ||
            idProp.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var id = idProp.GetString();
        if (string.IsNullOrEmpty(id))
            return;

        if (_toolCallsByCorrelationId.TryGetValue(id, out var existing))
        {
            _toolCallsByCorrelationId[id] = (existing.start, evt);
        }
        else
        {
            _toolCallsByCorrelationId[id] = (evt, evt);
        }
    }

    private static bool TryGetPayload(TraceEvent evt, out JsonElement payload)
    {
        // When loaded from a trace file, TraceEvent.Payload is stored as a JSON string.
        if (evt.Payload is string json && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                payload = doc.RootElement.Clone();
                return true;
            }
            catch
            {
                payload = default;
                return false;
            }
        }

        // As a fallback (e.g. for in-process events), serialize the payload object.
        try
        {
            var serialized = JsonSerializer.Serialize(evt.Payload);
            using var doc = JsonDocument.Parse(serialized);
            payload = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            payload = default;
            return false;
        }
    }
}

