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

    /// <summary>
    /// 1-based location immediately after this token's lexeme.
    /// </summary>
    public (int Line, int Column) GetEndLocation()
    {
        var line = Line;
        var column = Column;
        var lexeme = Lexeme ?? string.Empty;
        if (lexeme.IndexOf('\n') < 0)
        {
            return (line, column + lexeme.Length);
        }

        foreach (var ch in lexeme)
        {
            if (ch == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }
    
    public override string ToString()
    {
        return $"{Type} {Lexeme} {Literal}";
    }
}
