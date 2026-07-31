// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Shared payload rendering for trace viewer UIs. Prettifies JSON for display.

namespace MaldaLang.TraceViewer;

using System;
using System.Text.Json;

/// <summary>
/// Renders trace event payload JSON for display in IDE trace viewers.
/// </summary>
public static class TracePayloadHelper
{
    /// <summary>
    /// Returns a prettified JSON string for display. Handles malformed JSON by
    /// returning the raw string or a short error message.
    /// </summary>
    public static string Prettify(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"[Invalid JSON: {ex.Message}]\n\n{json}";
        }
    }
}
