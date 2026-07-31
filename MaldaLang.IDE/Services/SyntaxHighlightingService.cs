// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE.Models;

namespace MaldaLang.IDE.Services;

public class SyntaxHighlightingService
{
    public List<TokenInfo> GetTokens(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        
        return tokens.Select(t => new TokenInfo
        {
            Type = MapTokenType(t.Type),
            StartIndex = GetTokenStartIndex(source, t),
            Length = t.Lexeme.Length,
            Line = t.Line - 1, // Monaco uses 0-based lines
            Column = t.Column - 1 // Monaco uses 0-based columns
        }).ToList();
    }
    
    private string MapTokenType(TokenType type)
    {
        return type switch
        {
            // Keywords
            TokenType.If or TokenType.Else or TokenType.While or TokenType.For or TokenType.Foreach or
            TokenType.Function or TokenType.Return or TokenType.Var or TokenType.Print or
            TokenType.Input or TokenType.And or TokenType.Or or TokenType.Not or
            TokenType.Break or TokenType.Continue or TokenType.Class or TokenType.New or
            TokenType.This or TokenType.Super or TokenType.Extends or TokenType.Public or
            TokenType.Private or TokenType.Static or TokenType.Null => "keyword",
            
            // Literals
            TokenType.Integer or TokenType.Float => "number",
            TokenType.String => "string",
            TokenType.Boolean or TokenType.True or TokenType.False => "keyword",
            
            // Operators
            TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Divide or
            TokenType.Modulo or TokenType.Equal or TokenType.NotEqual or TokenType.LessThan or
            TokenType.GreaterThan or TokenType.LessThanOrEqual or TokenType.GreaterThanOrEqual or
            TokenType.Assign or TokenType.Arrow => "operator",
            
            // Identifiers
            TokenType.Identifier => "identifier",
            
            // Delimiters
            TokenType.LeftParen or TokenType.RightParen or TokenType.LeftBrace or
            TokenType.RightBrace or TokenType.LeftBracket or TokenType.RightBracket or
            TokenType.Comma or TokenType.Dot or TokenType.Semicolon => "delimiter",
            
            _ => "text"
        };
    }
    
    private int GetTokenStartIndex(string source, Token token)
    {
        // Calculate absolute position from line and column
        int line = 1;
        int column = 1;
        int index = 0;
        
        while (line < token.Line && index < source.Length)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
            index++;
        }
        
        while (column < token.Column && index < source.Length)
        {
            index++;
            column++;
        }
        
        return index;
    }
}

public class TokenInfo
{
    public string Type { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int Length { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}