// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;

namespace MaldaLang.DesktopIDE.Services;

public sealed class EditorDiagnosticSpan
{
    public int Offset { get; init; }
    public int Length { get; init; }
    public DiagnosticSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public int Line { get; init; }
    public int Column { get; init; }
    public AutoFixInfo? AutoFix { get; init; }
}

public delegate bool TryGetEditorOffset(int zeroBasedLine, int zeroBasedColumn, out int offset);

/// <summary>
/// Maps <see cref="MaldaLang.IDE.LanguageService"/> diagnostics onto editor coordinates.
/// Offset lookup stays with the AvalonEdit host.
/// </summary>
public sealed class EditorDiagnosticsService
{
    public List<Diagnostic> FilterForVirtualSection(
        IReadOnlyList<Diagnostic> diagnostics,
        int virtualStartLineZeroBased,
        int virtualEndLineZeroBased)
    {
        return diagnostics
            .Where(diagnostic => VirtualDocumentCoordinateMapper.ContainsDiagnosticLine(
                diagnostic.Line,
                virtualStartLineZeroBased,
                virtualEndLineZeroBased))
            .Select(diagnostic => new Diagnostic
            {
                Message = diagnostic.Message,
                Severity = diagnostic.Severity,
                Line = VirtualDocumentCoordinateMapper.ToSectionLocalDiagnosticLine(
                    diagnostic.Line,
                    virtualStartLineZeroBased),
                Column = diagnostic.Column,
                Length = diagnostic.Length,
                AutoFix = diagnostic.AutoFix,
                Source = diagnostic.Source,
                LearningHint = diagnostic.LearningHint,
                SuggestedFix = diagnostic.SuggestedFix,
                RelatedExamplePath = diagnostic.RelatedExamplePath,
                RelatedExampleTitle = diagnostic.RelatedExampleTitle,
                RelatedDocumentationPath = diagnostic.RelatedDocumentationPath,
                RelatedDocumentationTitle = diagnostic.RelatedDocumentationTitle
            })
            .ToList();
    }

    public List<EditorDiagnosticSpan> ToSpans(
        IEnumerable<Diagnostic> diagnostics,
        TryGetEditorOffset tryGetOffset)
    {
        var spans = new List<EditorDiagnosticSpan>();
        foreach (var diagnostic in diagnostics)
        {
            var length = Math.Max(1, diagnostic.Length);
            if (!tryGetOffset(diagnostic.Line, diagnostic.Column, out var offset))
            {
                continue;
            }

            spans.Add(new EditorDiagnosticSpan
            {
                Offset = offset,
                Length = length,
                Severity = diagnostic.Severity,
                Message = diagnostic.Message,
                Line = diagnostic.Line,
                Column = diagnostic.Column,
                AutoFix = diagnostic.AutoFix
            });
        }

        return spans;
    }
}
