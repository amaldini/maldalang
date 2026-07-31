// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang;

public class Token
{
    public TokenType Type { get; }
    public string Lexeme { get; }
    public object? Literal { get; }
    public int Line { get; }
    public int Column { get; }
    
    public Token(TokenType type, string lexeme, object? literal, int line, int column)
    {
        Type = type;
        Lexeme = lexeme;
        Literal = literal;
        Line = line;
        Column = column;
    }
    
    public override string ToString()
    {
        return $"{Type} {Lexeme} {Literal}";
    }
}
