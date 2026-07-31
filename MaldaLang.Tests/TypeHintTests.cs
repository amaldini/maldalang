// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Tests;

public class TypeHintTests : TestBase
{
    [Fact]
    public void ParseVarDeclaration_WithTypeHint_StoresTypeHint()
    {
        var source = "var x: int = 1;";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();

        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        Assert.IsType<VarDeclStatement>(statements[0]);

        var varDecl = (VarDeclStatement)statements[0];
        Assert.Equal("x", varDecl.Name);
        Assert.Equal("int", varDecl.TypeHint);
    }

    [Fact]
    public void ParseVarDeclaration_WithoutTypeHint_TypeHintIsNull()
    {
        var source = "var y = 42;";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();

        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var varDecl = (VarDeclStatement)statements[0];
        Assert.Equal("y", varDecl.Name);
        Assert.Null(varDecl.TypeHint);
    }

    [Fact]
    public void ParseFunctionDeclaration_WithParameterAndReturnTypeHints_StoresTypeHints()
    {
        var source = "function add(x: int, y: int) -> int { return x + y; }";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();

        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        Assert.IsType<FunctionDeclaration>(statements[0]);

        var funcDecl = (FunctionDeclaration)statements[0];
        Assert.Equal("add", funcDecl.Name);
        Assert.Equal(2, funcDecl.Parameters.Count);
        Assert.Equal("x", funcDecl.Parameters[0]);
        Assert.Equal("y", funcDecl.Parameters[1]);
        Assert.NotNull(funcDecl.ParameterTypeHints);
        Assert.Equal(2, funcDecl.ParameterTypeHints!.Count);
        Assert.Equal("int", funcDecl.ParameterTypeHints[0]);
        Assert.Equal("int", funcDecl.ParameterTypeHints[1]);
        Assert.Equal("int", funcDecl.ReturnType);
    }

    [Fact]
    public void ParseFunctionDeclaration_WithMixedParameterTypeHints_StoresHintsCorrectly()
    {
        var source = "function greet(name: string, count) -> string { return name; }";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();

        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var funcDecl = (FunctionDeclaration)statements[0];
        Assert.Equal("greet", funcDecl.Name);
        Assert.Equal(2, funcDecl.Parameters.Count);
        Assert.NotNull(funcDecl.ParameterTypeHints);
        Assert.Equal(2, funcDecl.ParameterTypeHints!.Count);
        Assert.Equal("string", funcDecl.ParameterTypeHints[0]);
        Assert.Null(funcDecl.ParameterTypeHints[1]);
        Assert.Equal("string", funcDecl.ReturnType);
    }

    [Fact]
    public void ParseFunctionDeclaration_WithoutTypeHints_TypeHintsAreNull()
    {
        var source = "function f(a, b) { return a; }";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();

        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var funcDecl = (FunctionDeclaration)statements[0];
        Assert.Equal("f", funcDecl.Name);
        Assert.Equal(2, funcDecl.Parameters.Count);
        Assert.Null(funcDecl.ReturnType);
        // ParameterTypeHints may be empty list or null; either way no hints per param
        if (funcDecl.ParameterTypeHints != null)
        {
            Assert.Equal(2, funcDecl.ParameterTypeHints.Count);
            Assert.Null(funcDecl.ParameterTypeHints[0]);
            Assert.Null(funcDecl.ParameterTypeHints[1]);
        }
    }

    [Fact]
    public void ParseFunctionDeclaration_OneStatementBody_ParsesAsReturn()
    {
        var source = "function square(x) x*x;";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();

        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var funcDecl = (FunctionDeclaration)statements[0];
        Assert.Equal("square", funcDecl.Name);
        Assert.Single(funcDecl.Parameters);
        Assert.Equal("x", funcDecl.Parameters[0]);
        Assert.Single(funcDecl.Body.Statements);
        Assert.IsType<ReturnStatement>(funcDecl.Body.Statements[0]);
        var ret = (ReturnStatement)funcDecl.Body.Statements[0];
        Assert.NotNull(ret.Value);
    }

    [Fact]
    public void ParseClassField_WithTypeHint_StoresTypeHint()
    {
        var source = """
            class Box {
                var value: float = 1.5;
            }
            """;
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();

        Assert.Empty(parser.Errors);
        Assert.Single(statements);
        var classDecl = Assert.IsType<ClassDeclaration>(statements[0]);
        Assert.Single(classDecl.Members);
        var field = classDecl.Members[0];
        Assert.Equal(MemberType.Field, field.Type);
        Assert.Equal("value", field.Name);
        Assert.Equal("float", field.TypeHint);
    }
}
