// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

/// <summary>
/// DAP-shaped inspect row for the Desktop debug tree (scope or variable).
/// </summary>
public sealed class DebugInspectNode
{
    public string Display { get; init; } = "";
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Type { get; init; } = "";
    public int VariablesReference { get; init; }
    public bool IsScope { get; init; }
    public int FrameId { get; init; }
    /// <summary>Stable tree path used to restore expansion across pauses.</summary>
    public string Path { get; set; } = "";

    public bool CanExpand => VariablesReference > 0;
}
