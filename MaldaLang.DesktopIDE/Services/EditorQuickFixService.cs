// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Picks language-service autofixes for the caret or a line, without WPF types.
/// </summary>
public sealed class EditorQuickFixService
{
    public IReadOnlyList<EditorDiagnosticSpan> GetFixesAtCaret(
        IReadOnlyList<EditorDiagnosticSpan> spans,
        int caretOffset,
        int caretLineZeroBased)
    {
        var withFixes = GetSpansWithFixes(spans);
        var containing = withFixes.Where(span => ContainsCaret(span, caretOffset)).ToList();
        if (containing.Count > 0)
        {
            return Deduplicate(containing);
        }

        return GetFixesOnLine(withFixes, caretLineZeroBased);
    }

    public IReadOnlyList<EditorDiagnosticSpan> GetFixesOnLine(
        IReadOnlyList<EditorDiagnosticSpan> spans,
        int lineZeroBased)
    {
        return Deduplicate(GetSpansWithFixes(spans).Where(span => span.Line == lineZeroBased));
    }

    public bool LineHasFix(IReadOnlyList<EditorDiagnosticSpan> spans, int lineZeroBased)
    {
        return GetSpansWithFixes(spans).Any(span => span.Line == lineZeroBased);
    }

    public IReadOnlyList<TextEditInfo> ToBatchEdits(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics
            .Where(diagnostic => diagnostic.AutoFix != null)
            .Select(diagnostic => ToEdit(diagnostic.AutoFix!))
            .ToList();
    }

    public static TextEditInfo ToEdit(AutoFixInfo autofix)
    {
        ArgumentNullException.ThrowIfNull(autofix);
        return new TextEditInfo
        {
            Span = new TextSpanInfo
            {
                Line = autofix.Line,
                Column = autofix.Column,
                Length = Math.Max(0, autofix.LengthToReplace)
            },
            NewText = autofix.TextToInsert ?? string.Empty
        };
    }

    private static IReadOnlyList<EditorDiagnosticSpan> GetSpansWithFixes(
        IReadOnlyList<EditorDiagnosticSpan> spans)
    {
        if (spans == null || spans.Count == 0)
        {
            return Array.Empty<EditorDiagnosticSpan>();
        }

        return spans.Where(span => span.AutoFix != null).ToList();
    }

    private static bool ContainsCaret(EditorDiagnosticSpan span, int caretOffset)
    {
        var length = Math.Max(1, span.Length);
        var end = span.Offset + length;
        return caretOffset >= span.Offset && caretOffset <= end;
    }

    private static IReadOnlyList<EditorDiagnosticSpan> Deduplicate(
        IEnumerable<EditorDiagnosticSpan> spans)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<EditorDiagnosticSpan>();
        foreach (var span in spans.OrderBy(item => item.Offset))
        {
            var fix = span.AutoFix;
            if (fix == null)
            {
                continue;
            }

            var key = $"{fix.Line}|{fix.Column}|{fix.LengthToReplace}|{fix.TextToInsert}|{fix.Description}";
            if (seen.Add(key))
            {
                unique.Add(span);
            }
        }

        return unique;
    }
}
