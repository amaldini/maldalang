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

    [Fact]
    public void Const_NameAsString_UsesIdentifierLexeme()
    {
        var source = """
            const BUY;
            print(BUY);
            """;
        Assert.Equal("BUY", RunProgram(source).Trim());
    }

    [Fact]
    public void Const_NameAsStringList_DeclaresEachName()
    {
        var source = """
            const BUY, SELL;
            print(BUY + SELL);
            """;
        Assert.Equal("BUYSELL", RunProgram(source).Trim());
    }

    [Fact]
    public void Const_NameAsString_MatchComparesEquality()
    {
        var source = """
            const BUY;
            var result = match "BUY" {
                case BUY: "ok";
                case _: "no";
            };
            print(result);
            """;
        Assert.Equal("ok", RunProgram(source).Trim());
    }

    [Fact]
    public void Parser_NameAsStringConst_DesugarsToStringLiteral()
    {
        var lexer = new Lexer("const BUY: string;");
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var decl = Assert.IsType<MaldaLang.Parser.AST.Statements.VarDeclStatement>(Assert.Single(statements));
        Assert.True(decl.IsConst);
        Assert.Equal("BUY", decl.Name);
        Assert.Equal("string", decl.TypeHint);
        var literal = Assert.IsType<MaldaLang.Parser.AST.Expressions.LiteralExpression>(decl.Initializer);
        Assert.Equal("BUY", literal.Value);
    }

    [Fact]
    public void Parser_NameAsStringConstList_DesugarsToSeparateDecls()
    {
        var lexer = new Lexer("const BUY, SELL, EURUSD;");
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        Assert.Equal(3, statements.Count);
        Assert.Collection(statements,
            s => AssertNameAsStringConst(s, "BUY"),
            s => AssertNameAsStringConst(s, "SELL"),
            s => AssertNameAsStringConst(s, "EURUSD"));
    }

    [Fact]
    public void Parser_VarWithoutInitializer_StillRequiresAssign()
    {
        Assert.Contains("Expect '=' after variable name.", FirstParseError("var BUY;"));
    }

    [Fact]
    public void Parser_NameAsStringList_RejectsMixedInitializer()
    {
        var message = FirstParseError("const BUY, MAX = 3;");
        Assert.Contains("Name-as-string const lists cannot include initializers", message);
    }

    [Fact]
    public void Transpiled_NameAsStringConst_MatchesInterpreter()
    {
        var source = """
            const BUY, SELL;
            print(BUY);
            print(SELL);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
        Assert.Contains("BUY", interpreted);
        Assert.Contains("SELL", interpreted);
    }

    private static void AssertNameAsStringConst(MaldaLang.Parser.AST.Statements.Statement stmt, string name)
    {
        var decl = Assert.IsType<MaldaLang.Parser.AST.Statements.VarDeclStatement>(stmt);
        Assert.True(decl.IsConst);
        Assert.Equal(name, decl.Name);
        var literal = Assert.IsType<MaldaLang.Parser.AST.Expressions.LiteralExpression>(decl.Initializer);
        Assert.Equal(name, literal.Value);
    }

    private static string FirstParseError(string source)
    {
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        parser.Parse();
        Assert.NotEmpty(parser.Errors);
        return parser.Errors[0].Message;
    }
}
