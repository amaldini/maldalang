// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Shared summarization logic for trace events. Used by TraceCli and IDE trace viewers.

namespace MaldaLang.TraceViewer;

using System;
using System.Text.Json;
using MaldaLang.Runtime.Tracing;

/// <summary>
/// Produces short summary strings for trace events, for display in CLI or IDE.
/// </summary>
public static class TraceSummaryHelper
{
    /// <summary>
    /// Returns a one-line summary for the given trace event.
    /// </summary>
    public static string Summarize(TraceEvent evt)
    {
        if (evt == null) return "";
        if (!TryGetPayload(evt, out var payload)) return "";

        try
        {
            switch (evt.Type)
            {
                case TraceEventType.LlmRequest:
                    {
                        var model = payload.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                        var messages = payload.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array ? msgs : default;
                        string? lastContent = null;
                        if (messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0)
                        {
                            var last = messages[messages.GetArrayLength() - 1];
                            if (last.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                                lastContent = c.GetString();
                        }
                        lastContent ??= "";
                        if (lastContent.Length > 60) lastContent = lastContent[..60] + "...";
                        return $"model={model ?? "-"}, msg=\"{lastContent}\"";
                    }
                case TraceEventType.LlmResponse:
                    {
                        var content = payload.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "";
                        if (content != null && content.Length > 80) content = content[..80] + "...";
                        return $"content=\"{content}\"";
                    }
                case TraceEventType.ToolCallStart:
                    {
                        var name = payload.TryGetProperty("toolName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                        var type = payload.TryGetProperty("toolType", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                        return $"start tool={name ?? "-"} type={type ?? "-"}";
                    }
                case TraceEventType.ToolCallEnd:
                    {
                        var name = payload.TryGetProperty("toolName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                        var success = payload.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                        return $"end tool={name ?? "-"} success={success}";
                    }
                case TraceEventType.FileEdit:
                    {
                        var path = payload.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                        var op = payload.TryGetProperty("operation", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
                        return $"file={path ?? "-"} op={op ?? "-"}";
                    }
                case TraceEventType.RunCommand:
                    {
                        var command = payload.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        int exitCode = 0;
                        if (payload.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number && ec.TryGetInt32(out var eci))
                            exitCode = eci;
                        int? duration = null;
                        if (payload.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var di))
                            duration = di;
                        return $"cmd={command ?? "-"} exit={exitCode} durMs={(duration?.ToString() ?? "-")}";
                    }
                case TraceEventType.RunMalda:
                    {
                        var src = payload.TryGetProperty("sourceOrFilePath", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                        var success = payload.TryGetProperty("success", out var suc) && suc.ValueKind == JsonValueKind.True;
                        return $"runMALDA={src ?? "-"} success={success}";
                    }
                case TraceEventType.CompileMalda:
                    {
                        var src = payload.TryGetProperty("sourcePath", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                        var success = payload.TryGetProperty("success", out var suc) && suc.ValueKind == JsonValueKind.True;
                        return $"compileMALDA={src ?? "-"} success={success}";
                    }
                default:
                    return "";
            }
        }
        catch
        {
            return "";
        }
    }

    private static bool TryGetPayload(TraceEvent evt, out JsonElement payload)
    {
        payload = default;
        if (evt.Payload is string json && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                payload = doc.RootElement.Clone();
                return true;
            }
            catch { }
        }
        return false;
    }
}
