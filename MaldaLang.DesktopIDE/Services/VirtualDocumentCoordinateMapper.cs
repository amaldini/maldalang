// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Converts between AvalonEdit / debug 1-based lines and virtual-section 0-based ranges.
/// <c>VirtualStartLine</c> / <c>VirtualEndLine</c> are 0-based inclusive.
/// </summary>
public static class VirtualDocumentCoordinateMapper
{
    public static int ToPhysicalLine(int editorOneBasedLine, int virtualStartLineZeroBased)
    {
        return editorOneBasedLine + virtualStartLineZeroBased;
    }

    public static int ToEditorLine(int physicalOneBasedLine, int virtualStartLineZeroBased)
    {
        return physicalOneBasedLine - virtualStartLineZeroBased;
    }

    public static bool ContainsPhysicalLine(int physicalOneBasedLine, int virtualStartLineZeroBased, int virtualEndLineZeroBased)
    {
        var zeroBased = physicalOneBasedLine - 1;
        return zeroBased >= virtualStartLineZeroBased && zeroBased <= virtualEndLineZeroBased;
    }

    public static bool ContainsDiagnosticLine(int diagnosticZeroBasedLine, int virtualStartLineZeroBased, int virtualEndLineZeroBased)
    {
        return diagnosticZeroBasedLine >= virtualStartLineZeroBased &&
               diagnosticZeroBasedLine <= virtualEndLineZeroBased;
    }

    public static int ToSectionLocalDiagnosticLine(int diagnosticZeroBasedLine, int virtualStartLineZeroBased)
    {
        return diagnosticZeroBasedLine - virtualStartLineZeroBased;
    }
}
