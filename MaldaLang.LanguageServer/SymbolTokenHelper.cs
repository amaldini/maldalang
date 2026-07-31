// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using System.Linq;
using MaldaLang;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal static class SymbolTokenHelper
{
    public static Token? FindTokenAtPosition(List<Token> tokens, int line, int column)
    {
        return tokens.FirstOrDefault(t => t.Line == line && t.Column <= column &&
            t.Column + t.Lexeme.Length >= column);
    }

    public static Token? FindIdentifierTokenAtPosition(List<Token> tokens, Position position)
    {
        var line1 = position.Line + 1;
        var col1 = position.Character + 1;
        var token = FindTokenAtPosition(tokens, line1, col1);
        return token != null && token.Type == TokenType.Identifier ? token : null;
    }

    public static bool IsValidIdentifier(string name)
    {
        if (name.Length == 0) return false;
        var c = name[0];
        if (c != '_' && !char.IsLetter(c)) return false;
        for (var i = 1; i < name.Length; i++)
        {
            c = name[i];
            if (c != '_' && !char.IsLetterOrDigit(c)) return false;
        }
        return true;
    }
}
