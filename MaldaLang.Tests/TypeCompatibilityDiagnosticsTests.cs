// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class TypeCompatibilityDiagnosticsTests
{
    [Fact]
    public void GetDiagnostics_LiteralMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("var n: int = \"abc\";");
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("does not match literal", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_LiteralMatch_NoCompatibilityWarning()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("var n: int = 1;");
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void GetDiagnostics_FloatHintAcceptsIntLiteral()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("var x: float = 1;");
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Analyze_FieldLiteralMismatch_EmitsWarning()
    {
        var source = """
            class Box {
                var label: string = 42;
            }
            """;
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("label", StringComparison.Ordinal));
    }
}
