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
            d.Message.Contains("does not match value", StringComparison.Ordinal));
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

    [Fact]
    public void GetDiagnostics_AssignmentIdentifierMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var source = """
            var n: int = 1;
            n = "a";
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("variable 'n'", StringComparison.Ordinal) &&
            d.Message.Contains("string", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_CallSiteIdentifierMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var source = """
            var s: string = "x";
            function f(x: int) { }
            f(s);
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("argument 1", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ReturnIdentifierMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var source = """
            function f(n: int) -> string {
                return n;
            }
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("return value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_AssignmentAndCallCompatible_NoWarning()
    {
        var service = new LanguageService();
        var source = """
            var n: int = 1;
            n = 2;
            function f(x: int) { }
            f(n);
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Analyze_StrictTypes_ElevatesAssignmentMismatchToError()
    {
        var source = """
            var n: int = 1;
            n = "a";
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
            d.Message.Contains("variable 'n'", StringComparison.Ordinal));
        Assert.True(StrictTypesAnalysis.HasErrors(diagnostics));
    }

    [Fact]
    public void GetDiagnostics_UninferableRhs_NoWarning()
    {
        var service = new LanguageService();
        var source = """
            var n: int = 1;
            var a = 1;
            n = unknownIdent;
            n = a + 1;
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassHint_NewExpressionMatch_NoWarning()
    {
        var service = new LanguageService();
        var source = """
            class Person {
                function Person() { }
            }
            var p: Person = new Person();
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("Unknown type hint", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassHint_LiteralMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var source = """
            class Person {
                function Person() { }
            }
            var p: Person = 1;
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("Person", StringComparison.Ordinal) &&
            d.Message.Contains("int", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassHint_IdentifierMatch_NoWarning()
    {
        var service = new LanguageService();
        var source = """
            class Person {
                function Person() { }
            }
            var q: Person = new Person();
            var p: Person = q;
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassHint_IdentifierMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var source = """
            class Person {
                function Person() { }
            }
            var q: string = "x";
            var p: Person = q;
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("variable 'p'", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_StrictTypes_ClassHintMismatch_IsError_AndNotUnknown()
    {
        var source = """
            class Person {
                function Person() { }
            }
            var p: Person = 1;
            """;
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, diagnostics);
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("Unknown type hint", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
        Assert.True(StrictTypesAnalysis.HasErrors(diagnostics));
    }
}
