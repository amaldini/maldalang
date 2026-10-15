// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.PackageManager;
using System.Threading;

namespace MaldaLang.IDE.Services;

public partial class LanguageService
{
    public AutoFixInfo? GetAutoFix(string source, Diagnostic diagnostic, MaldaLang.Parser.ParseException? parseException = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Only autofix parser errors with missing characters
        if (diagnostic.Source != "parser")
        {
            return null;
        }
        
        // First, try to use ExpectedType from ParseException if available
        if (parseException != null && parseException.ExpectedType.HasValue)
        {
            var expectedType = parseException.ExpectedType.Value;
            var expectedTypeStr = expectedType.ToString();
            var charToken = TokenTypeToCharacter(expectedTypeStr);
            
            if (charToken != null && IsSimpleCharacterToken(charToken))
            {
                var insertLine = diagnostic.Line;
                var insertColumn = diagnostic.Column + diagnostic.Length;
                var lengthToReplace = 0;
                
                // If we're replacing (ActualType is different), replace at current position
                if (parseException.ActualType.HasValue && parseException.ActualType.Value != expectedType)
                {
                    insertColumn = diagnostic.Column;
                    lengthToReplace = diagnostic.Length;
                }

                if (charToken == ";" &&
                    TryGetInsertPositionAfterPrecedingToken(source, diagnostic.Line, diagnostic.Column, out var semiLine, out var semiColumn))
                {
                    insertLine = semiLine;
                    insertColumn = semiColumn;
                    lengthToReplace = 0;
                }
                
                return new AutoFixInfo
                {
                    Description = $"Insert missing '{charToken}'",
                    Line = insertLine,
                    Column = insertColumn,
                    TextToInsert = charToken,
                    LengthToReplace = lengthToReplace,
                    IsSimpleCharacterFix = true
                };
            }
        }
        
        var message = diagnostic.Message;
        
        // Check for "Expect 'X' after/before ..." pattern (e.g., "Expect '}' after object properties.")
        if (message.Contains("Expect '"))
        {
            var expectIndex = message.IndexOf("Expect '");
            if (expectIndex >= 0)
            {
                var startQuote = expectIndex + 8; // Length of "Expect '"
                var endQuote = message.IndexOf("'", startQuote);
                
                if (endQuote > startQuote)
                {
                    var expectedToken = message.Substring(startQuote, endQuote - startQuote);
                    var charToken = TokenTypeToCharacter(expectedToken);
                    
                    // Check if it's a simple character (brace, bracket, parenthesis, semicolon)
                    if (charToken != null && IsSimpleCharacterToken(charToken))
                    {
                        // For "Expect 'X' after/before ..." patterns, the parser column points to where
                        // it encountered the error. For "after" messages, the parser has already advanced
                        // past where we should insert, so we need to insert at the previous position.
                        // For "before" messages, we insert at the current position.
                        var insertLine = diagnostic.Line;
                        var insertColumn = diagnostic.Column;

                        if (charToken == ";" &&
                            TryGetInsertPositionAfterPrecedingToken(source, diagnostic.Line, diagnostic.Column, out var semiLine, out var semiColumn))
                        {
                            // Missing ';' is inserted at the end of the preceding statement, even
                            // when the parser reported the next token after comments or blank lines.
                            insertLine = semiLine;
                            insertColumn = semiColumn;
                        }
                        else if (message.Contains(" after ") || message.Contains("after "))
                        {
                            // Parser has advanced past where we should insert, so go back one position
                            insertColumn = Math.Max(0, diagnostic.Column - 1);
                        }
                        
                        return new AutoFixInfo
                        {
                            Description = $"Insert missing '{charToken}'",
                            Line = insertLine,
                            Column = insertColumn,
                            TextToInsert = charToken,
                            LengthToReplace = 0,
                            IsSimpleCharacterFix = true
                        };
                    }
                }
            }
        }
        
        // Check for "Missing 'X'" or "Expected 'X'" patterns
        if (message.StartsWith("Missing '") || message.StartsWith("Expected '"))
        {
            // Extract the expected token from message
            var startQuote = message.IndexOf("'") + 1;
            var endQuote = message.IndexOf("'", startQuote);
            
            if (startQuote > 0 && endQuote > startQuote)
            {
                var expectedToken = message.Substring(startQuote, endQuote - startQuote);
                var charToken = TokenTypeToCharacter(expectedToken);
                
                // Check if it's a simple character (brace, bracket, parenthesis, semicolon)
                if (charToken != null && IsSimpleCharacterToken(charToken))
                {
                    return new AutoFixInfo
                    {
                        Description = $"Insert missing '{charToken}'",
                        Line = diagnostic.Line,
                        Column = diagnostic.Column + diagnostic.Length,
                        TextToInsert = charToken,
                        LengthToReplace = 0,
                        IsSimpleCharacterFix = true
                    };
                }
            }
        }
        
        // Check for "Expected 'X' but found 'Y'" pattern
        if (message.Contains("Expected '") && message.Contains(" but found '"))
        {
            var expectedStart = message.IndexOf("Expected '") + 10;
            var expectedEnd = message.IndexOf("'", expectedStart);
            
            if (expectedStart > 0 && expectedEnd > expectedStart)
            {
                var expectedToken = message.Substring(expectedStart, expectedEnd - expectedStart);
                var charToken = TokenTypeToCharacter(expectedToken);
                
                // Only autofix if expected token is simple character
                if (charToken != null && IsSimpleCharacterToken(charToken))
                {
                    return new AutoFixInfo
                    {
                        Description = $"Insert '{charToken}' instead of unexpected token",
                        Line = diagnostic.Line,
                        Column = diagnostic.Column,
                        TextToInsert = charToken,
                        LengthToReplace = diagnostic.Length,
                        IsSimpleCharacterFix = true
                    };
                }
            }
        }
        
        // Try to detect missing closing braces by analyzing the code structure
        var missingBraceFix = DetectMissingClosingBrace(source, diagnostic);
        if (missingBraceFix != null)
        {
            return missingBraceFix;
        }
        
        return null;
    }
    
    private string? TokenTypeToCharacter(string tokenType)
    {
        // Map TokenType enum names to their character representations
        return tokenType switch
        {
            "RightBrace" => "}",
            "LeftBrace" => "{",
            "RightParen" => ")",
            "LeftParen" => "(",
            "RightBracket" => "]",
            "LeftBracket" => "[",
            "Semicolon" => ";",
            "Comma" => ",",
            "Dot" => ".",
            "Colon" => ":",
            // Also handle if it's already a character
            "}" => "}",
            "{" => "{",
            ")" => ")",
            "(" => "(",
            "]" => "]",
            "[" => "[",
            ";" => ";",
            "," => ",",
            "." => ".",
            ":" => ":",
            _ => null
        };
    }
    
    private bool IsSimpleCharacterToken(string token)
    {
        // Return true for tokens that are single characters or simple multi-character
        // tokens that we can safely auto-insert
        return token == ";" || token == "{" || token == "}" || 
               token == "(" || token == ")" || 
               token == "[" || token == "]" ||
               token == "," || token == "." || token == ":";
    }

    private static void SnapDiagnosticToSemicolonAutofix(Diagnostic diagnostic)
    {
        var fix = diagnostic.AutoFix;
        if (fix == null || fix.TextToInsert != ";")
        {
            return;
        }

        // Lightbulb / squiggle follow diagnostic.Line; keep them on the statement
        // that is missing ';' instead of on the next token after comments.
        diagnostic.Line = fix.Line;
        diagnostic.Column = fix.Column;
    }

    /// <summary>
    /// Insert missing ';' immediately after the last token that starts before
    /// <paramref name="diagnosticLine"/>/<paramref name="diagnosticColumn"/>.
    /// Comments are not tokens, so this skips blank lines and comments.
    /// </summary>
    private static bool TryGetInsertPositionAfterPrecedingToken(
        string source,
        int diagnosticLine,
        int diagnosticColumn,
        out int insertLine,
        out int insertColumn)
    {
        insertLine = diagnosticLine;
        insertColumn = diagnosticColumn;
        if (string.IsNullOrEmpty(source))
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

        Token? preceding = null;
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.EOF)
            {
                break;
            }

            var tokenLine = token.Line - 1;
            var tokenColumn = Math.Max(0, token.Column - 1);
            if (tokenLine > diagnosticLine ||
                (tokenLine == diagnosticLine && tokenColumn >= diagnosticColumn))
            {
                break;
            }

            preceding = token;
        }

        if (preceding == null)
        {
            return false;
        }

        var (endLine, endColumn) = preceding.GetEndLocation();
        insertLine = Math.Max(0, endLine - 1);
        insertColumn = Math.Max(0, endColumn - 1);
        return true;
    }
    
    private AutoFixInfo? DetectMissingClosingBrace(string source, Diagnostic diagnostic)
    {
        // Only check for missing closing braces if the error is near the end of the file
        // or if the message suggests a brace-related issue
        var message = diagnostic.Message.ToLower();
        if (!message.Contains("brace") && !message.Contains("}") && !message.Contains("block"))
        {
            return null;
        }
        
        var lines = source.Split('\n');
        if (diagnostic.Line >= lines.Length) return null;
        
        // Count opening and closing braces from the error line to the end
        int openBraces = 0;
        int closeBraces = 0;
        
        for (int i = diagnostic.Line; i < lines.Length; i++)
        {
            var line = lines[i];
            for (int j = (i == diagnostic.Line ? diagnostic.Column : 0); j < line.Length; j++)
            {
                if (line[j] == '{') openBraces++;
                if (line[j] == '}') closeBraces++;
            }
        }
        
        // If we have unmatched opening braces, suggest adding a closing brace at the end
        if (openBraces > closeBraces)
        {
            var lastLine = lines.Length - 1;
            var lastLineText = lines[lastLine];
            var insertColumn = lastLineText.Length;
            
            // Skip trailing whitespace
            while (insertColumn > 0 && char.IsWhiteSpace(lastLineText[insertColumn - 1]))
            {
                insertColumn--;
            }
            
            return new AutoFixInfo
            {
                Description = "Insert missing closing brace '}'",
                Line = lastLine,
                Column = insertColumn,
                TextToInsert = "}",
                LengthToReplace = 0,
                IsSimpleCharacterFix = true
            };
        }
        
        return null;
    }
}
