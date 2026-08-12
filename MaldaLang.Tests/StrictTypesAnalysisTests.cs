// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

public class StrictTypesAnalysisTests
{
    private static List<Diagnostic> Analyze(string source, bool strict)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(
            statements,
            strict ? StrictTypesOptions.Enabled : StrictTypesOptions.Default,
            diagnostics);
        return diagnostics;
    }

    [Fact]
    public void StrictMode_UnknownTypeHint_IsError()
    {
        var diagnostics = Analyze("function f(x: NotARealType) -> int { return x; }", strict: true);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DefaultMode_UnknownTypeHint_IsInformation()
    {
        var diagnostics = Analyze("function f(x: NotARealType) -> int { return x; }", strict: false);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" && d.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void StrictMode_NonExhaustiveSumMatch_IsError()
    {
        var source = """
            type Result = Ok(value) | Err(message);
            var r = Ok(1);
            var out = match r {
                case Ok(v): v;
            };
            """;
        var diagnostics = Analyze(source, strict: true);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-match" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("Err", StringComparison.Ordinal));
    }

    [Fact]
    public void StrictMode_ExhaustiveSumMatch_NoMatchDiagnostic()
    {
        var source = """
            type Result = Ok(value) | Err(message);
            var r = Ok(1);
            var out = match r {
                case Ok(v): v;
                case Err(m): -1;
            };
            """;
        var diagnostics = Analyze(source, strict: true);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-match");
    }

    [Fact]
    public void Tier0ConformanceSnippets_PassStrictAnalysis()
    {
        var snippets = new[]
        {
            """
            var x = 42;
            var result = match x {
                case 42: "ok";
                default: "no";
            };
            print(result);
            """,
            """
            type Result = Ok(value) | Err(message);
            var r = Ok(7);
            var out = match r {
                case Ok(v): v;
                case Err(m): -1;
            };
            print(out);
            """,
            "print(typeOf(42));",
            """
            function compute() { return 99; }
            var t = async compute();
            var v = await t;
            print(v);
            """
        };

        foreach (var source in snippets)
        {
            var diagnostics = Analyze(source, strict: true);
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }
    }

    [Fact]
    public void StrictMode_CallReturnMismatch_IsError()
    {
        var source = """
            function make() -> string { return "x"; }
            var n: int = make();
            """;
        var diagnostics = Analyze(source, strict: true);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("variable 'n'", StringComparison.Ordinal));
    }

    [Fact]
    public void StrictMode_TypedCallArgFromCalleeReturn_Match_NoError()
    {
        var source = """
            function make() -> int { return 1; }
            function take(x: int) { return x; }
            var n: int = take(make());
            """;
        var diagnostics = Analyze(source, strict: true);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }

    [Fact]
    public void LanguageService_DefaultMode_StillInformationalForUnknownHint()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("function f(x: NotARealType) -> int { return x; }");
        Assert.Contains(diagnostics, d => d.Source == "malda-types" && d.Severity == DiagnosticSeverity.Info);
    }
}
