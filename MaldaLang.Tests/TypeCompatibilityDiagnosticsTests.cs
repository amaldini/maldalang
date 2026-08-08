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

    [Fact]
    public void GetDiagnostics_CallSiteLiteralMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var source = """
            function f(x: int) { }
            f("a");
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("argument 1", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ReturnLiteralMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var source = """
            function f() -> int {
                return "a";
            }
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("return value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_CallSiteLiteralMatch_NoWarning()
    {
        var service = new LanguageService();
        var source = """
            function f(x: int) { }
            f(1);
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Analyze_StrictTypes_ElevatesLiteralMismatchToError()
    {
        var source = """
            function f(x: int) { }
            f("a");
            """;
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, diagnostics);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("argument 1", StringComparison.Ordinal));
        Assert.True(StrictTypesAnalysis.HasErrors(diagnostics));
    }
}
