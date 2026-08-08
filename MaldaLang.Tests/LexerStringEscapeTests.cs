// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Linq;
using Xunit;

namespace MaldaLang.Tests;

public class LexerStringEscapeTests
{
    private static Token LexStringLiteral(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var token = tokens.FirstOrDefault(t => t.Type == TokenType.String);
        Assert.NotNull(token);
        return token!;
    }

    [Fact]
    public void PlainString_Decodes_Cr_Lf_Tab_And_Quotes()
    {
        var token = LexStringLiteral("""var s = "a\r\nb\tc\"d\\e";""");
        Assert.Equal("a\r\nb\tc\"d\\e", token.Literal as string);
    }

    [Fact]
    public void SingleQuotedString_Decodes_Cr()
    {
        var token = LexStringLiteral("""var s = 'line\rend';""");
        Assert.Equal("line\rend", token.Literal as string);
    }

    [Fact]
    public void InterpolatedString_Decodes_Cr()
    {
        var tokens = new Lexer("""var s = $"a\rb";""").Tokenize();
        var token = tokens.First(t => t.Type == TokenType.InterpolatedString);
        var segments = Assert.IsType<System.Collections.Generic.List<LexerInterpolatedStringSegment>>(token.Literal);
        Assert.Single(segments);
        Assert.False(segments[0].IsExpression);
        Assert.Equal("a\rb", segments[0].Content);
    }

    [Fact]
    public void UnknownEscape_IsLexerError_InPlainString()
    {
        var ex = Assert.Throws<Exception>(() => new Lexer("""var s = "\x";""").Tokenize());
        Assert.Contains("Unknown escape sequence", ex.Message, StringComparison.Ordinal);
        Assert.Contains("\\x", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownEscape_IsLexerError_InInterpolatedString()
    {
        var ex = Assert.Throws<Exception>(() => new Lexer("""var s = $"\x";""").Tokenize());
        Assert.Contains("Unknown escape sequence", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CrEscape_IsNotSilentlyTurnedIntoLetterR()
    {
        // Regression: before \r support, "\r" decoded as "r".
        var token = LexStringLiteral("""var s = "Brand\r";""");
        Assert.Equal("Brand\r", token.Literal as string);
        Assert.NotEqual("Brandr", token.Literal as string);
    }
}
