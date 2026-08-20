// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

/// <summary>
/// Detects programs that must run in a browser host (<c>dom.*</c> / <c>game.*</c> /
/// <c>three.*</c> or <c>@client()</c> / <c>@javascript()</c>). The interpreter
/// cannot stop these; Desktop IDE F5 uses WebView2 instead.
/// </summary>
public static class JsBrowserApiDetector
{
    private static readonly HashSet<string> BrowserModules = new(StringComparer.Ordinal)
    {
        "dom",
        "game",
        "three"
    };

    private static readonly HashSet<string> ClientDecorators = new(StringComparer.Ordinal)
    {
        "client",
        "javascript"
    };

    public static bool UsesBrowserHost(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        List<Token> tokens;
        try
        {
            tokens = new Lexer(source).Tokenize();
        }
        catch
        {
            return false;
        }

        for (var i = 0; i < tokens.Count - 1; i++)
        {
            var current = tokens[i];
            var next = tokens[i + 1];
            if (current.Type == TokenType.Identifier &&
                next.Type == TokenType.Dot &&
                BrowserModules.Contains(current.Lexeme))
            {
                return true;
            }

            if (current.Type == TokenType.At &&
                next.Type == TokenType.Identifier &&
                ClientDecorators.Contains(next.Lexeme))
            {
                return true;
            }
        }

        return false;
    }
}
