// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class EditorQuickFixServiceTests
{
    private readonly EditorQuickFixService _quickFixes = new();
    private readonly EditorDiagnosticsService _diagnostics = new();

    [Fact]
    public void GetFixesAtCaret_PrefersSpanContainingCaret()
    {
        var spans = new[]
        {
            CreateSpan(offset: 0, length: 5, line: 0, description: "other line", column: 0),
            CreateSpan(offset: 10, length: 4, line: 1, description: "insert ';'", column: 10)
        };

        var fixes = _quickFixes.GetFixesAtCaret(spans, caretOffset: 12, caretLineZeroBased: 1);

        var fix = Assert.Single(fixes);
        Assert.Equal("insert ';'", fix.AutoFix?.Description);
    }

    [Fact]
    public void GetFixesAtCaret_IncludesCaretSittingAtSpanEnd()
    {
        var spans = new[]
        {
            CreateSpan(offset: 4, length: 3, line: 0, description: "insert ')'", column: 4)
        };

        var fixes = _quickFixes.GetFixesAtCaret(spans, caretOffset: 7, caretLineZeroBased: 0);

        Assert.Equal("insert ')'", Assert.Single(fixes).AutoFix?.Description);
    }

    [Fact]
    public void GetFixesAtCaret_FallsBackToSameLineWhenCaretMissesSpan()
    {
        var spans = new[]
        {
            CreateSpan(offset: 0, length: 4, line: 0, description: "line 0", column: 0),
            CreateSpan(offset: 20, length: 1, line: 1, description: "line 1 missing brace", column: 8)
        };

        var fixes = _quickFixes.GetFixesAtCaret(spans, caretOffset: 30, caretLineZeroBased: 1);

        var fix = Assert.Single(fixes);
        Assert.Equal("line 1 missing brace", fix.AutoFix?.Description);
    }

    [Fact]
    public void GetFixesAtCaret_EmptyWhenNoFixOnLine()
    {
        var spans = new[]
        {
            CreateSpan(offset: 0, length: 4, line: 0, description: "line 0", column: 0)
        };

        Assert.Empty(_quickFixes.GetFixesAtCaret(spans, caretOffset: 20, caretLineZeroBased: 2));
    }

    [Fact]
    public void GetFixesAtCaret_DeduplicatesIdenticalAutofixes()
    {
        var spans = new[]
        {
            CreateSpan(offset: 4, length: 2, line: 0, description: "insert ';'", column: 6),
            CreateSpan(offset: 4, length: 2, line: 0, description: "insert ';'", column: 6)
        };

        var fixes = _quickFixes.GetFixesAtCaret(spans, caretOffset: 5, caretLineZeroBased: 0);

        Assert.Single(fixes);
    }

    [Fact]
    public void LineHasFix_IsTrueOnlyForLinesWithAutofix()
    {
        var spans = new[]
        {
            CreateSpan(offset: 0, length: 1, line: 2, description: "insert '}'", column: 0),
            new EditorDiagnosticSpan
            {
                Offset = 10,
                Length = 3,
                Line = 3,
                Column = 0,
                Message = "no fix",
                Severity = DiagnosticSeverity.Error
            }
        };

        Assert.True(_quickFixes.LineHasFix(spans, 2));
        Assert.False(_quickFixes.LineHasFix(spans, 3));
        Assert.False(_quickFixes.LineHasFix(spans, 0));
    }

    [Fact]
    public void ToBatchEdits_IncludesNonSimpleCharacterFixes()
    {
        var diagnostics = new[]
        {
            new Diagnostic
            {
                Line = 0,
                Column = 5,
                AutoFix = new AutoFixInfo
                {
                    Description = "insert ';'",
                    Line = 0,
                    Column = 12,
                    TextToInsert = ";",
                    LengthToReplace = 0,
                    IsSimpleCharacterFix = true
                }
            },
            new Diagnostic
            {
                Line = 1,
                Column = 0,
                AutoFix = new AutoFixInfo
                {
                    Description = "insert missing block",
                    Line = 1,
                    Column = 0,
                    TextToInsert = " {}",
                    LengthToReplace = 0,
                    IsSimpleCharacterFix = false
                }
            }
        };

        var edits = _quickFixes.ToBatchEdits(diagnostics);

        Assert.Equal(2, edits.Count);
        Assert.Equal(";", edits[0].NewText);
        Assert.Equal(" {}", edits[1].NewText);
        Assert.Equal(12, edits[0].Span.Column);
    }

    [Fact]
    public void FilterForVirtualSection_RemapsAutoFixLine()
    {
        var diagnostics = new List<Diagnostic>
        {
            new()
            {
                Line = 12,
                Column = 4,
                Length = 1,
                Message = "Expect ';' after expression.",
                AutoFix = new AutoFixInfo
                {
                    Description = "Insert missing ';'",
                    Line = 12,
                    Column = 8,
                    TextToInsert = ";",
                    LengthToReplace = 0,
                    IsSimpleCharacterFix = true
                }
            }
        };

        var filtered = _diagnostics.FilterForVirtualSection(diagnostics, 10, 20);
        var local = Assert.Single(filtered);
        Assert.Equal(2, local.Line);
        Assert.Equal(2, local.AutoFix?.Line);
        Assert.Equal(8, local.AutoFix?.Column);
        Assert.Equal(";", local.AutoFix?.TextToInsert);
    }

    [Fact]
    public void GetFixesAtCaret_UsesParserAutofixFromLanguageService()
    {
        var language = new LanguageService();
        const string source = "print(\"hi\"\n";
        var diagnostics = language.GetDiagnostics(source);
        var withFix = diagnostics.Where(diagnostic => diagnostic.AutoFix != null).ToList();
        Assert.NotEmpty(withFix);

        var spans = _diagnostics.ToSpans(withFix, (int line, int column, out int offset) =>
            TryGetOffset(source, line, column, out offset));
        var span = Assert.Single(spans);
        var caret = span.Offset + Math.Min(1, span.Length);

        var fixes = _quickFixes.GetFixesAtCaret(spans, caret, span.Line);

        Assert.NotNull(Assert.Single(fixes).AutoFix);
    }

    [Fact]
    public void LineHasFix_MissingSemicolon_IsOnStatementLineNotFollowingComment()
    {
        const string source = "print(\"hi\")\n// comment\nvar x = 1;\n";
        var language = new LanguageService();
        var diagnostics = language.GetDiagnostics(source);
        var withFix = diagnostics.Where(diagnostic => diagnostic.AutoFix != null).ToList();
        var spans = _diagnostics.ToSpans(withFix, (int line, int column, out int offset) =>
            TryGetOffset(source, line, column, out offset));

        Assert.True(_quickFixes.LineHasFix(spans, 0));
        Assert.False(_quickFixes.LineHasFix(spans, 1));
        Assert.False(_quickFixes.LineHasFix(spans, 2));

        var fix = Assert.Single(spans).AutoFix;
        Assert.Equal(";", fix?.TextToInsert);
        Assert.Equal(0, fix?.Line);
        Assert.Equal(11, fix?.Column);
    }

    private static EditorDiagnosticSpan CreateSpan(
        int offset,
        int length,
        int line,
        string description,
        int column)
    {
        return new EditorDiagnosticSpan
        {
            Offset = offset,
            Length = length,
            Line = line,
            Column = column,
            Message = description,
            Severity = DiagnosticSeverity.Error,
            AutoFix = new AutoFixInfo
            {
                Description = description,
                Line = line,
                Column = column,
                TextToInsert = ";",
                LengthToReplace = 0,
                IsSimpleCharacterFix = true
            }
        };
    }

    private static bool TryGetOffset(string source, int zeroBasedLine, int zeroBasedColumn, out int offset)
    {
        offset = 0;
        var lines = source.Split('\n');
        if (zeroBasedLine < 0 || zeroBasedLine >= lines.Length)
        {
            return false;
        }

        for (var i = 0; i < zeroBasedLine; i++)
        {
            offset += lines[i].Length + 1;
        }

        offset += Math.Min(Math.Max(0, zeroBasedColumn), lines[zeroBasedLine].Length);
        return true;
    }
}
