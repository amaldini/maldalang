// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang;
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public partial class Parser
{
    private bool Match(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }
        return false;
    }
    
    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Peek().Type == type;
    }

    /// <summary>
    /// True when '? [' begins a ternary consequent (e.g. x ? [] : y), not null-conditional indexing.
    /// Scans to the matching ']' and checks for a trailing ':'.
    /// </summary>
    private bool IsQuestionMarkArrayTernaryStart()
    {
        if (!Check(TokenType.QuestionMark) || Peek(1)?.Type != TokenType.LeftBracket)
            return false;

        int depth = 1;
        int offset = 2;
        while (offset < 200)
        {
            var token = Peek(offset);
            if (token == null || token.Type == TokenType.EOF)
                return false;

            if (token.Type == TokenType.LeftBracket)
                depth++;
            else if (token.Type == TokenType.RightBracket)
            {
                depth--;
                if (depth == 0)
                {
                    var after = Peek(offset + 1);
                    return after != null && after.Type == TokenType.Colon;
                }
            }

            offset++;
        }

        return false;
    }

    /// <summary>
    /// True when current token is LeftBracket and what follows looks like an array destructuring pattern
    /// (e.g. [a, b] = x or [] = x). This method checks that the pattern is followed by '='
    /// to distinguish from regular array expressions.
    /// </summary>
    private bool IsArrayDestructuringAssignmentStart()
    {
        if (!Check(TokenType.LeftBracket)) return false;
        
        // Look ahead to find the closing bracket and check if '=' follows
        int depth = 1;
        int offset = 1;
        while (offset < 20) // Limit lookahead to avoid infinite loops
        {
            var token = Peek(offset);
            if (token == null || token.Type == TokenType.EOF) return false;
            
            if (token.Type == TokenType.LeftBracket) depth++;
            else if (token.Type == TokenType.RightBracket)
            {
                depth--;
                if (depth == 0)
                {
                    // Found closing bracket, check if next token is '='
                    var afterBracket = Peek(offset + 1);
                    return afterBracket != null && afterBracket.Type == TokenType.Assign;
                }
            }
            offset++;
        }
        
        return false; // Didn't find matching bracket within lookahead limit
    }
    
    /// <summary>
    /// True when current token is LeftBrace and what follows looks like an object destructuring pattern
    /// (e.g. { name, age } = x or { } = x), so we parse destructuring assignment instead of a block.
    /// This method checks that the pattern is followed by '=' to distinguish from blocks.
    /// </summary>
    private bool IsObjectDestructuringAssignmentStart()
    {
        if (!Check(TokenType.LeftBrace)) return false;
        
        // Look ahead to find the closing brace and check if '=' follows
        int depth = 1;
        int offset = 1;
        while (offset < 20) // Limit lookahead to avoid infinite loops
        {
            var token = Peek(offset);
            if (token == null || token.Type == TokenType.EOF) return false;
            
            if (token.Type == TokenType.LeftBrace) depth++;
            else if (token.Type == TokenType.RightBrace)
            {
                depth--;
                if (depth == 0)
                {
                    // Found closing brace, check if next token is '='
                    var afterBrace = Peek(offset + 1);
                    return afterBrace != null && afterBrace.Type == TokenType.Assign;
                }
            }
            offset++;
        }
        
        return false; // Didn't find matching brace within lookahead limit
    }
    
    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }
    
    private bool IsAtEnd()
    {
        return Peek().Type == TokenType.EOF;
    }
    
    private Token Peek()
    {
        return _tokens[_current];
    }
    
    private Token? Peek(int offset)
    {
        int index = _current + offset;
        if (index < 0 || index >= _tokens.Count) return null;
        return _tokens[index];
    }
    
    private Token Previous()
    {
        return _tokens[_current - 1];
    }
    
    private Token? Current()
    {
        if (_current < _tokens.Count)
            return _tokens[_current];
        return null;
    }
    
    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw ErrorAtExpectedToken(type, message);
    }

    /// <summary>
    /// Identifier policy:
    /// - Hard syntax keywords remain strict in statement/declaration heads.
    /// - In name-binding/property contexts, we accept identifier-like tokens
    ///   (identifiers, legacy input token, and contextual keywords) for compatibility.
    /// </summary>
    private string ConsumeIdentifierLike(string message)
    {
        var token = ConsumeIdentifierTokenLike(message);
        return token.Lexeme;
    }

    /// <summary>Package names may contain hyphens (e.g. malda-timeseries).</summary>
    private string ConsumePackageName(string message)
    {
        var name = ConsumeIdentifierLike(message);
        while (Match(TokenType.Minus))
        {
            name += "-";
            name += ConsumeIdentifierLike("Expect package name segment after '-'.");
        }

        return name;
    }

    private Token ConsumeIdentifierTokenLike(string message)
    {
        var token = Peek();
        if (token == null)
        {
            var error = Error(new Token(TokenType.EOF, "", null, 0, 0), message);
            throw error;
        }

        if (token.Type == TokenType.Identifier || token.Type == TokenType.Input || IsKeyword(token.Type))
        {
            Advance();
            return token;
        }

        var throwError = Error(token, message);
        throw throwError;
    }

    private string ConsumeIdentifierOrInput(string message)
    {
        return ConsumeIdentifierLike(message);
    }
    
    private string ConsumeIdentifierOrKeyword(string message)
    {
        return ConsumeIdentifierLike(message);
    }

    private bool IsIdentifierLikeExpressionToken(TokenType? type)
    {
        if (!type.HasValue)
            return false;

        if (type.Value == TokenType.Identifier || type.Value == TokenType.Input)
            return true;

        // Soft keywords in expression positions become identifiers unless they are
        // explicitly handled by dedicated parsing branches.
        return IsKeyword(type.Value);
    }
    
    private Token ConsumeOrInsert(TokenType type, string message, bool insertMissing = true)
    {
        if (Check(type)) return Advance();
        
        if (insertMissing && ShouldInsertMissingToken(type))
        {
            // Insert a synthetic token
            var syntheticToken = InsertSyntheticToken(type);
            var error = new ParseException(syntheticToken.Line, syntheticToken.Column, type, Peek().Type, isSyntheticInsertion: true, sourceFileName: _sourceFileName);
            _errors.Add(error);
            return syntheticToken;
        }
        
        var throwError = Error(Peek(), message);
        throw throwError;
    }
    
    private bool ShouldInsertMissingToken(TokenType expectedType)
    {
        // Don't insert if we're at EOF
        if (IsAtEnd()) return false;
        
        var nextType = Peek().Type;
        
        // Automatically insert closing braces, parentheses, brackets, and semicolons
        // when the next token suggests we've moved past the expected point
        switch (expectedType)
        {
            case TokenType.RightBrace:
                // Insert missing } if we see:
                // - Another statement keyword (if, while, for, var, etc.)
                // - Another opening brace (start of new block)
                // - EOF
                return nextType == TokenType.EOF ||
                       nextType == TokenType.If ||
                       nextType == TokenType.While ||
                       nextType == TokenType.For ||
                       nextType == TokenType.Var ||
                       nextType == TokenType.Function ||
                       nextType == TokenType.Class ||
                       nextType == TokenType.Actor ||
                       nextType == TokenType.Property ||
                       nextType == TokenType.Using ||
                       nextType == TokenType.Import ||
                       nextType == TokenType.Export ||
                       nextType == TokenType.Include ||
                       nextType == TokenType.Return ||
                       nextType == TokenType.Break ||
                       nextType == TokenType.Continue ||
                       nextType == TokenType.LeftBrace;
                       
            case TokenType.RightParen:
                // Insert missing ) if we see:
                // - Opening brace (start of block)
                // - Semicolon
                // - Binary operators
                // - EOF
                return nextType == TokenType.LeftBrace ||
                       nextType == TokenType.Semicolon ||
                       nextType == TokenType.Plus ||
                       nextType == TokenType.Minus ||
                       nextType == TokenType.Multiply ||
                       nextType == TokenType.Divide ||
                       nextType == TokenType.Modulo ||
                       nextType == TokenType.Equal ||
                       nextType == TokenType.NotEqual ||
                       nextType == TokenType.GreaterThan ||
                       nextType == TokenType.GreaterThanOrEqual ||
                       nextType == TokenType.LessThan ||
                       nextType == TokenType.LessThanOrEqual ||
                       nextType == TokenType.EOF;
                       
            case TokenType.RightBracket:
                // Insert missing ] if we see:
                // - Dot operator
                // - Opening parenthesis (function call)
                // - Assignment
                // - Semicolon
                // - Binary operators
                return nextType == TokenType.Dot ||
                       nextType == TokenType.LeftParen ||
                       nextType == TokenType.Assign ||
                       nextType == TokenType.PlusAssign ||
                       nextType == TokenType.MinusAssign ||
                       nextType == TokenType.MultiplyAssign ||
                       nextType == TokenType.DivideAssign ||
                       nextType == TokenType.Semicolon ||
                       nextType == TokenType.Comma ||
                       nextType == TokenType.Plus ||
                       nextType == TokenType.Minus ||
                       nextType == TokenType.Multiply ||
                       nextType == TokenType.Divide ||
                       nextType == TokenType.EOF;
                       
            case TokenType.Semicolon:
                // Insert missing ; if we see:
                // - New statement keywords
                // - Closing brace
                // - EOF
                return nextType == TokenType.If ||
                       nextType == TokenType.While ||
                       nextType == TokenType.For ||
                       nextType == TokenType.Foreach ||
                       nextType == TokenType.Var ||
                       nextType == TokenType.Const ||
                       nextType == TokenType.Function ||
                       nextType == TokenType.Class ||
                       nextType == TokenType.Actor ||
                       nextType == TokenType.Property ||
                       nextType == TokenType.RightBrace ||
                       nextType == TokenType.EOF;
                       
            default:
                return false;
        }
    }
    
    private Token InsertSyntheticToken(TokenType type)
    {
        // Create a synthetic token at the current position
        var currentToken = Peek();
        var syntheticToken = new Token(type, $"<synthetic {type}>", null, currentToken.Line, currentToken.Column);
        _syntheticTokens.Add(syntheticToken);
        
        // Insert the synthetic token into the token stream
        _tokens.Insert(_current, syntheticToken);
        
        return syntheticToken;
    }

    private ParseException Error(Token token, string message, string? diagnosticCode = null)
    {
        return new ParseException(token.Line, token.Column, message, _sourceFileName, diagnosticCode);
    }

    /// <summary>
    /// Missing statement terminators should be reported at the end of the
    /// preceding token (after comments/newlines have already been skipped),
    /// not on the next statement.
    /// </summary>
    private ParseException ErrorAtExpectedToken(TokenType expectedType, string message)
    {
        if (expectedType == TokenType.Semicolon && _current > 0)
        {
            var (line, column) = Previous().GetEndLocation();
            return new ParseException(line, column, message, _sourceFileName);
        }

        return Error(Peek(), message);
    }
    
    private bool IsKeyword(TokenType type)
    {
        // Check if the token type is a keyword (not Identifier, not a literal, not an operator, not a delimiter)
        return type != TokenType.Identifier &&
               type != TokenType.Integer &&
               type != TokenType.Float &&
               type != TokenType.String &&
               type != TokenType.InterpolatedString &&
               type != TokenType.Boolean &&
               type != TokenType.EOF &&
               type != TokenType.Plus &&
               type != TokenType.Minus &&
               type != TokenType.Multiply &&
               type != TokenType.Divide &&
               type != TokenType.Modulo &&
               type != TokenType.Equal &&
               type != TokenType.NotEqual &&
               type != TokenType.LessThan &&
               type != TokenType.GreaterThan &&
               type != TokenType.LessThanOrEqual &&
               type != TokenType.GreaterThanOrEqual &&
               type != TokenType.Assign &&
               type != TokenType.QuestionMark &&
               type != TokenType.Increment &&
               type != TokenType.Decrement &&
               type != TokenType.PlusAssign &&
               type != TokenType.MinusAssign &&
               type != TokenType.MultiplyAssign &&
               type != TokenType.DivideAssign &&
               type != TokenType.Arrow &&
               type != TokenType.Colon &&
               type != TokenType.Semicolon &&
               type != TokenType.LeftParen &&
               type != TokenType.RightParen &&
               type != TokenType.LeftBrace &&
               type != TokenType.RightBrace &&
               type != TokenType.LeftBracket &&
               type != TokenType.RightBracket &&
               type != TokenType.Comma &&
               type != TokenType.Dot &&
               type != TokenType.At &&
               type != TokenType.Underscore &&
               type != TokenType.Pipe &&
               type != TokenType.PipeForward;
    }
    
    private void Synchronize()
    {
        Advance();
        
        while (!IsAtEnd())
        {
            if (Previous().Type == TokenType.Semicolon) return;
            
            switch (Peek().Type)
            {
                case TokenType.Class:
                case TokenType.Function:
                case TokenType.Property:
                case TokenType.Var:
                case TokenType.Const:
                case TokenType.For:
                case TokenType.Foreach:
                case TokenType.If:
                case TokenType.While:
                case TokenType.Return:
                case TokenType.Try:
                case TokenType.Throw:
                case TokenType.Defer:
                    return;
            }
            
            Advance();
        }
    }

    private bool CheckLexeme(string name) =>
        Check(TokenType.Identifier) && string.Equals(Peek()?.Lexeme, name, StringComparison.Ordinal);

    private bool MatchLexeme(string name)
    {
        if (!CheckLexeme(name))
            return false;
        Advance();
        return true;
    }
}
