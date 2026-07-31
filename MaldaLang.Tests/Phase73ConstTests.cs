// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class Phase73ConstTests : TestBase
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
    public void Const_Declaration_AllowsRead()
    {
        var source = """
            const limit = 10;
            print(limit);
            """;
        Assert.Equal("10", RunProgram(source).Trim());
    }

    [Fact]
    public void Const_Reassignment_ThrowsAtRuntime()
    {
        var source = """
            const limit = 10;
            limit = 20;
            """;
        var ex = Assert.Throws<RuntimeException>(() => RunProgram(source));
        Assert.Contains("const", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StrictMode_ConstReassignment_IsStaticError()
    {
        var diagnostics = AnalyzeStrict("""
            const limit = 10;
            limit = 20;
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-const" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Const_ShadowedByVar_AllowsAssignment()
    {
        var source = """
            const limit = 1;
            {
                var limit = 2;
                limit = 3;
                print(limit);
            }
            """;
        Assert.Equal("3", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_ConstReassignment_MatchesInterpreter()
    {
        var source = """
            const limit = 10;
            var ok = true;
            try {
                limit = 20;
                ok = false;
            } catch (e) {
                ok = true;
            }
            print(ok);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void Parser_AcceptsConstWithTypeHint()
    {
        var lexer = new Lexer("const name: string = \"alice\";");
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var decl = Assert.IsType<MaldaLang.Parser.AST.Statements.VarDeclStatement>(statements[0]);
        Assert.True(decl.IsConst);
        Assert.Equal("string", decl.TypeHint);
    }
}
