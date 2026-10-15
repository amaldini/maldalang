// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Tests;

using System.Collections.Generic;
using System.Linq;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using Xunit;

/// <summary>
/// Anonymous <c>function (…) { … }</c> expressions, trailing commas in literals and argument
/// lists, and contextual keywords (<c>context</c>, <c>suite</c>, <c>policy</c>, <c>within</c>)
/// used as plain identifiers.
/// </summary>
[Collection("Sequential")]
public class ParserSyntaxGapTests : TestBase
{
    /// <summary>Shared with the interpret/C#/JS pair tests.</summary>
    public const string AnonymousFunctionSource =
        """
        var inc = function(x) { return x + 1; };
        io.print(inc(4));
        function ap(f, v) { return f(v); }
        io.print(ap(function (x) { return x * 2; }, 5));
        var base = 10;
        var addBase = function(a, b,) { return a + b + base; };
        io.print(addBase(1, 2,));
        var nums = [3, 1, 2,];
        io.print(nums.map(function(n) { return n * n; }));
        var cfg = { name: "m", size: 2, };
        io.print(cfg.name + string(cfg.size));
        var noArgs = function() { return "none"; };
        io.print(noArgs());
        var context = "a";
        context = context + "b";
        io.print(context);
        """;

    private static List<Statement> Parse(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var parser = new Parser(tokens);
        var statements = parser.Parse();
        Assert.True(parser.Errors.Count == 0,
            string.Join("\n", parser.Errors.Select(e => e.Message)));
        return statements;
    }

    private static Expression Initializer(Statement stmt)
    {
        var decl = Assert.IsType<VarDeclStatement>(stmt);
        Assert.NotNull(decl.Initializer);
        return decl.Initializer;
    }

    [Fact]
    public void AnonymousFunction_ProducesBlockLambda()
    {
        var stmts = Parse("var f = function(x, y) { return x + y; };");
        var lambda = Assert.IsType<LambdaExpression>(Initializer(stmts[0]));
        Assert.Equal(new[] { "x", "y" }, lambda.Parameters);
        Assert.Null(lambda.ExpressionBody);
        Assert.NotNull(lambda.BlockBody);
    }

    [Fact]
    public void AnonymousFunction_AsCallArgument_Parses()
    {
        var stmts = Parse("function ap(f, v) { return f(v); }\nvar r = ap(function(x) { return x * 2; }, 5);");
        Assert.IsType<FunctionDeclaration>(stmts[0]);
        var call = Assert.IsType<FunctionCallExpression>(Initializer(stmts[1]));
        Assert.IsType<LambdaExpression>(call.Arguments[0]);
    }

    [Fact]
    public void AnonymousFunction_TypeHintsAccepted()
    {
        var stmts = Parse("var f = function(x: int) { return x; };");
        Assert.Equal(new[] { "x" }, Assert.IsType<LambdaExpression>(Initializer(stmts[0])).Parameters);
    }

    [Fact]
    public void FunctionDeclaration_AtStatementStart_StillDeclaration()
    {
        var stmts = Parse("function add(a, b) { return a + b; }");
        Assert.Equal("add", Assert.IsType<FunctionDeclaration>(stmts[0]).Name);
    }

    [Fact]
    public void AnonymousFunction_Interprets()
    {
        var output = RunProgram(AnonymousFunctionSource);
        Assert.Equal("5\n10\n13\n[9, 1, 4]\nm2\nnone\nab", output.Replace("\r\n", "\n").Trim());
    }

    [Fact]
    public void TrailingCommas_InArrayObjectDictAndCall()
    {
        var stmts = Parse("""
            var a = [1, 2,];
            var o = { a: 1, b: 2, };
            var d = dict { "k": 1, };
            var n = max(1, 2,);
            """);
        Assert.Equal(2, Assert.IsType<ArrayLiteralExpression>(Initializer(stmts[0])).Elements.Count);
        Assert.Equal(2, Assert.IsType<ObjectLiteralExpression>(Initializer(stmts[1])).Properties.Count);
        Assert.Single(Assert.IsType<DictionaryLiteralExpression>(Initializer(stmts[2])).Entries);
        Assert.Equal(2, Assert.IsType<FunctionCallExpression>(Initializer(stmts[3])).Arguments.Count);
    }

    [Fact]
    public void TrailingComma_BeforeComment_InGraphLiteral()
    {
        var stmts = Parse("""
            var g = graph directed {
              nodes: ["X", "Y"],
              edges: [
                { from: "X", to: "Y", weight: 1 },
                // trailing comment
              ],
            };
            """);
        Assert.IsType<GraphLiteralExpression>(Initializer(stmts[0]));
    }

    [Fact]
    public void LoneComma_StillAnError()
    {
        var parser = new Parser(new Lexer("var a = [,];").Tokenize());
        parser.Parse();
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void ContextualKeywords_UsableAsIdentifiers()
    {
        var stmts = Parse("""
            var context = "x";
            context = context + "y";
            var suite = 1;
            suite = suite + 1;
            var policy = 2;
            policy = 3;
            var within = 4;
            within = 5;
            """);
        Assert.Equal(8, stmts.Count);
        Assert.IsType<AssignmentStatement>(stmts[1]);
    }

    [Fact]
    public void ContextualKeywords_DeclarationsStillParse()
    {
        var stmts = Parse("""
            context Chat { budget: 100 tokens; }
            policy { net: allow "example.com"; }
            suite "s" { case "c" { io.print(1); } }
            """);
        Assert.IsType<ContextDeclaration>(stmts[0]);
        Assert.IsType<PolicyDeclaration>(stmts[1]);
        Assert.IsType<SuiteDeclaration>(stmts[2]);
    }
}
