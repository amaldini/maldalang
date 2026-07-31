// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using Xunit;

namespace MaldaLang.Tests;

public class Phase6EffectsTests : TestBase
{
    private static List<Diagnostic> AnalyzeStrict(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, diagnostics);
        return diagnostics;
    }

    [Fact]
    public void StrictMode_PureFunctionWithPrint_IsError()
    {
        var diagnostics = AnalyzeStrict("""
            @pure()
            function bad() {
                print("nope");
            }
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-pure" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StrictMode_PureFunctionWithoutIo_Passes()
    {
        var diagnostics = AnalyzeStrict("""
            @pure()
            function normalizeName(name) {
                return upper(trim(name));
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-pure");
    }

    [Fact]
    public void Validate_RegisteredSchema_AcceptsMatchingObject()
    {
        SchemaRegistry.ClearForTesting();
        var source = """
            schema ToolInput {
                name: string;
            }
            var raw = dict { "name": "alice" };
            var check = validate("ToolInput", raw);
            print(check.ok);
            var validated = check.data;
            print(validated.name);
            """;
        var output = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", output[0]);
        Assert.Equal("alice", output[1]);
    }

    [Fact]
    public void StrictMode_EffectsAllowList_PermitsDeclaredIo()
    {
        var diagnostics = AnalyzeStrict("""
            @effects("print")
            function logOnly(msg) {
                print(msg);
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-effects");
    }

    [Fact]
    public void StrictMode_EffectsAllowList_RejectsUndeclaredIo()
    {
        var diagnostics = AnalyzeStrict("""
            @effects("print")
            function bad() {
                readFile("secret.txt");
            }
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-effects" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StrictMode_WithinDecorator_InvalidArg_IsError()
    {
        var diagnostics = AnalyzeStrict("""
            @within(0)
            function bad() {
                return 1;
            }
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-bounds" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void WithinBound_ExceedsLimit_Throws()
    {
        var source = """
            @within(20)
            function slow() {
                sleep(100);
            }
            slow();
            """;
        var ex = Assert.Throws<RuntimeException>(() => RunProgram(source));
        Assert.Contains("exceeded @within", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StrictMode_PromptWithinDecorator_InvalidArg_IsError()
    {
        var diagnostics = AnalyzeStrict("""
            @within(-1)
            prompt bad() {
                user "slow";
            }
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-bounds" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StrictMode_PromptWithinDecorator_Valid_Passes()
    {
        var diagnostics = AnalyzeStrict("""
            @within(2000)
            prompt bounded() {
                user "ok";
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-bounds");
    }

    [Fact]
    public void PromptWithin_BuildsPromptInstanceWithTimeout()
    {
        var lexer = new Lexer("""
            @within(900)
            prompt bounded() {
                user "hello";
            }
            """);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var prompt = Assert.IsType<MaldaLang.Parser.AST.Declarations.PromptDeclaration>(statements[0]);
        var timeout = DeclarationBounds.TryGetWithinTimeoutMs(prompt);
        Assert.Equal(900, timeout);
    }

    [Fact]
    public void Phase6Example_PureHelperAndValidate()
    {
        SchemaRegistry.ClearForTesting();
        var source = """
            schema ToolInput {
                name: string;
            }

            @pure()
            function normalizeName(name) {
                return upper(trim(name));
            }

            function handleToolInput(raw) {
                var check = validate("ToolInput", raw);
                if (!check.ok) {
                    print("invalid");
                    return;
                }
                var validated = check.data;
                print(normalizeName(validated.name));
            }

            handleToolInput(dict { "name": "  alice  " });
            """;
        Assert.Equal("ALICE", RunProgram(source));
    }
}
