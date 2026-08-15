// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class InterpolationDiagnosticsTests
{
    private static List<Diagnostic> Analyze(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var diagnostics = new List<Diagnostic>();
        InterpolationDiagnostics.Validate(statements, diagnostics);
        return diagnostics;
    }

    [Fact]
    public void PlainStringBraceIdent_IsMaldaInterpWarning()
    {
        var diagnostics = Analyze("var n = 1;\nprint(\"n is {n}\");\n");
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-interp" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void InterpolatedString_NoWarning()
    {
        var diagnostics = Analyze("var n = 1;\nprint($\"n is {n}\");\n");
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-interp");
    }

    [Fact]
    public void PromptBodyTemplate_NoWarning()
    {
        var diagnostics = Analyze("""
            prompt greet(name) {
                user: "Hello {name}"
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-interp");
    }
}
