// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class FunctionAliasRemovalTests
{
    [Theory]
    [InlineData("fn")]
    [InlineData("def")]
    public void Alias_IsParseError(string alias)
    {
        var source = $"{alias} add(a, b) {{ return a + b; }}\n";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        parser.Parse();
        Assert.Contains(parser.Errors, e =>
            e.Message.Contains($"'{alias}' is not a function keyword", StringComparison.Ordinal));
    }

    [Fact]
    public void FunctionKeyword_StillParses()
    {
        var source = "function add(a, b) { return a + b; }\n";
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        Assert.Contains(statements, s => s is MaldaLang.Parser.AST.Declarations.FunctionDeclaration);
    }
}
