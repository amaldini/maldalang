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
    public void GetDiagnostics_LiteralMismatch_EmitsError()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("var n: int = \"abc\";");
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_Lenient_LiteralMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics(
            "var n: int = \"abc\";",
            strictTypesOptions: StrictTypesOptions.Lenient);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_LiteralMatch_NoCompatibilityError()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("var n: int = 1;");
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GetDiagnostics_FloatHintAcceptsIntLiteral()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("var x: float = 1;");
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Analyze_FieldLiteralMismatch_Lenient_EmitsWarning()
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
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Lenient, diagnostics);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("label", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_FieldLiteralMismatch_Default_EmitsError()
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
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("label", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_CallSiteLiteralMismatch_EmitsError()
    {
        var service = new LanguageService();
        var source = """
            function f(x: int) { }
            f("a");
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("argument 1", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ReturnLiteralMismatch_EmitsError()
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
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("return value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_CallSiteLiteralMatch_NoError()
    {
        var service = new LanguageService();
        var source = """
            function f(x: int) { }
            f(1);
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GetDiagnostics_PrimaryConstructorArgMismatch_EmitsError()
    {
        var service = new LanguageService();
        var source = """
            class punto(x: int, y: int);
            var p = new punto(10, "Ciao");
            """;
        var diagnostics = service.GetDiagnostics(source);
        var mismatch = Assert.Single(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("argument 2 of 'new punto'", StringComparison.Ordinal) &&
            d.Message.Contains("int", StringComparison.Ordinal) &&
            d.Message.Contains("string", StringComparison.Ordinal));
        Assert.Equal(1, mismatch.Line);
    }

    [Fact]
    public void GetDiagnostics_PrimaryConstructorArgMatch_NoError()
    {
        var service = new LanguageService();
        var source = """
            class punto(x: int, y: int);
            var p = new punto(10, 20);
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassicConstructorArgMismatch_EmitsError()
    {
        var service = new LanguageService();
        var source = """
            class punto {
                var x;
                var y;
                function punto(x: int, y: int) {
                    this.x = x;
                    this.y = y;
                }
            }
            var p = new punto(10, "Ciao");
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("argument 2 of 'new punto'", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_Lenient_PrimaryConstructorArgMismatch_EmitsWarning()
    {
        var service = new LanguageService();
        var source = """
            class punto(x: int, y: int);
            var p = new punto(10, "Ciao");
            """;
        var diagnostics = service.GetDiagnostics(source, strictTypesOptions: StrictTypesOptions.Lenient);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("argument 2 of 'new punto'", StringComparison.Ordinal));
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
    public void GetDiagnostics_AssignmentIdentifierMismatch_EmitsError()
    {
        var service = new LanguageService();
        var source = """
            var n: int = 1;
            n = "a";
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("variable 'n'", StringComparison.Ordinal) &&
            d.Message.Contains("string", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_CallSiteIdentifierMismatch_EmitsError()
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
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("argument 1", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ReturnIdentifierMismatch_EmitsError()
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
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("return value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_AssignmentAndCallCompatible_NoError()
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
            d.Severity == DiagnosticSeverity.Error);
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
    public void GetDiagnostics_UninferableRhs_NoError()
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
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_InferredIntPlus_Compatible_NoError()
    {
        var service = new LanguageService();
        var source = """
            var n: int = 1;
            var a: int = 1;
            n = a + 1;
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_DivisionAssignedToInt_EmitsError()
    {
        var service = new LanguageService();
        var source = """
            var n: int = 1;
            var a: int = 2;
            n = a / 2;
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("float", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_MathFloorAssignedToString_EmitsError()
    {
        var service = new LanguageService();
        var source = """
            var s: string = math.floor(1.5);
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_StrTrimAssignedToString_NoError()
    {
        var service = new LanguageService();
        var source = """
            var s: string = str.trim(" x ");
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassHint_NewExpressionMatch_NoError()
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
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("Unknown type hint", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassHint_LiteralMismatch_EmitsError()
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
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("Person", StringComparison.Ordinal) &&
            d.Message.Contains("int", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassHint_IdentifierMatch_NoError()
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
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_ClassHint_IdentifierMismatch_EmitsError()
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
            d.Severity == DiagnosticSeverity.Error &&
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

    [Fact]
    public void GetDiagnostics_CallReturnMismatch_EmitsError()
    {
        var service = new LanguageService();
        var source = """
            function make() -> string { return "x"; }
            var n: int = make();
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("variable 'n'", StringComparison.Ordinal) &&
            d.Message.Contains("string", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_CallReturnMatch_NoError()
    {
        var service = new LanguageService();
        var source = """
            function make() -> int { return 1; }
            var n: int = make();
            function take(x: int) { }
            take(make());
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_StrictTypes_CallReturnMismatch_IsError()
    {
        var source = """
            function make() -> string { return "x"; }
            var n: int = make();
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
    public void GetDiagnostics_AwaitCallReturnMismatch_EmitsError()
    {
        var service = new LanguageService();
        var source = """
            function make() -> string { return "x"; }
            var n: int = await make();
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("variable 'n'", StringComparison.Ordinal));
    }
}
