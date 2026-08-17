// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class EditorDiagnosticsServiceTests
{
    [Fact]
    public void FilterForVirtualSection_RemapsLines()
    {
        var service = new EditorDiagnosticsService();
        var diagnostics = new List<Diagnostic>
        {
            new() { Line = 2, Column = 0, Length = 4, Message = "in section", Severity = DiagnosticSeverity.Error },
            new() { Line = 20, Column = 0, Length = 4, Message = "outside", Severity = DiagnosticSeverity.Error }
        };

        var filtered = service.FilterForVirtualSection(diagnostics, 2, 10);

        var local = Assert.Single(filtered);
        Assert.Equal(0, local.Line);
        Assert.Equal("in section", local.Message);
    }

    [Fact]
    public void ToSpans_UsesOffsetLookup()
    {
        var service = new EditorDiagnosticsService();
        var diagnostics = new List<Diagnostic>
        {
            new() { Line = 0, Column = 4, Length = 3, Message = "oops", Severity = DiagnosticSeverity.Error }
        };

        var spans = service.ToSpans(diagnostics, (int line, int column, out int offset) =>
        {
            offset = 10 + column;
            return line == 0;
        });

        var span = Assert.Single(spans);
        Assert.Equal(14, span.Offset);
        Assert.Equal(3, span.Length);
        Assert.Equal(DiagnosticSeverity.Error, span.Severity);
    }

    [Fact]
    public void GetDiagnostics_UnknownIdentifier_ProducesErrorSpanInputs()
    {
        var language = new LanguageService();
        var diagnostics = language.GetDiagnostics("print(missingName);\n");
        Assert.NotEmpty(diagnostics);
        Assert.True(diagnostics[0].Length > 0 || diagnostics[0].Message.Length > 0);
    }
}
