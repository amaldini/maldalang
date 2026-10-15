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
    private Pattern ParsePattern()
    {
        var token = Peek();
        
        // Check for array pattern: [pattern, pattern, ...rest]
        if (Match(TokenType.LeftBracket))
        {
            return ParseArrayPattern(token);
        }
        
        // Check for object pattern: { prop: pattern, prop }
        if (Match(TokenType.LeftBrace))
        {
            return ParseObjectPattern(token);
        }
        
        // Check for wildcard: _
        if (Match(TokenType.Underscore))
        {
            return new WildcardPattern(token.Line, token.Column);
        }
        
        // Check for literal patterns
        if (Match(TokenType.Integer, TokenType.Float, TokenType.String, TokenType.Boolean, TokenType.Null, TokenType.True, TokenType.False))
        {
            var prev = Previous();
            return new LiteralPattern(prev.Literal, prev.Line, prev.Column);
        }
        
        // Check for variant pattern: Tag(payloadPattern, ...) e.g. Ok(v) or Err(msg)
        if (Check(TokenType.Identifier) && Peek(1)?.Type == TokenType.LeftParen)
        {
            Advance();
            var tagToken = Previous();
            Consume(TokenType.LeftParen, "Expect '(' after variant tag in pattern.");
            var payloadPatterns = new List<Pattern>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    payloadPatterns.Add(ParsePattern());
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expect ')' after variant payload patterns.");
            return new VariantPattern(tagToken.Lexeme, payloadPatterns, tagToken.Line, tagToken.Column);
        }
        
        // Otherwise, it's an identifier pattern (binds the value to a variable)
        if (Match(TokenType.Identifier))
        {
            var prev = Previous();
            return new IdentifierPattern(prev.Lexeme, prev.Line, prev.Column);
        }
        
        throw Error(token, "Expected pattern.");
    }
    
    private ArrayPattern ParseArrayPattern(Token startToken)
    {
        var elements = new List<Pattern>();
        RestPattern? rest = null;
        
        if (!Check(TokenType.RightBracket))
        {
            do
            {
                // Check for rest pattern: ...rest or ...
                if (Check(TokenType.Dot) && Peek(1)?.Type == TokenType.Dot && Peek(2)?.Type == TokenType.Dot)
                {
                    Advance(); // consume first dot
                    Advance(); // consume second dot
                    Advance(); // consume third dot
                    
                    string? restName = null;
                    if (Match(TokenType.Identifier))
                    {
                        restName = Previous().Lexeme;
                    }
                    rest = new RestPattern(restName, startToken.Line, startToken.Column);
                    break; // Rest pattern must be last
                }
                
                elements.Add(ParsePattern());
            } while (Match(TokenType.Comma));
        }
        
        Consume(TokenType.RightBracket, "Expect ']' after array pattern.");
        return new ArrayPattern(elements, rest, startToken.Line, startToken.Column);
    }
    
    private ObjectPattern ParseObjectPattern(Token startToken)
    {
        var properties = new List<ObjectPatternProperty>();
        
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                // Property key (identifier, keyword, or string)
                string key;
                if (Check(TokenType.Identifier) || IsKeyword(Peek().Type))
                {
                    key = ConsumeIdentifierOrKeyword("Expected property name in object pattern.");
                }
                else if (Match(TokenType.String))
                {
                    key = (string)(Previous().Literal ?? "");
                }
                else
                {
                    throw Error(Peek(), "Expected property name in object pattern.");
                }
                
                // Check for : pattern or shorthand (just the key)
                if (Match(TokenType.Colon))
                {
                    // { key: pattern } or { key: identifier }
                    var pattern = ParsePattern();
                    properties.Add(new ObjectPatternProperty(key, pattern));
                }
                else
                {
                    // Shorthand: { key } means { key: key }
                    properties.Add(new ObjectPatternProperty(key, null, key));
                }
            } while (Match(TokenType.Comma));
        }
        
        Consume(TokenType.RightBrace, "Expect '}' after object pattern.");
        return new ObjectPattern(properties, startToken.Line, startToken.Column);
    }
    
    private DestructuringPattern ParseDestructuringPattern()
    {
        var token = Peek();
        
        if (Match(TokenType.LeftBracket))
        {
            // Array destructuring: [pattern, pattern, ...rest]
            var elements = new List<Pattern>();
            RestPattern? rest = null;
            
            if (!Check(TokenType.RightBracket))
            {
                do
                {
                    // Check for rest pattern: ...rest or ...
                    if (Check(TokenType.Dot) && Peek(1)?.Type == TokenType.Dot && Peek(2)?.Type == TokenType.Dot)
                    {
                        Advance(); // consume first dot
                        Advance(); // consume second dot
                        Advance(); // consume third dot
                        
                        string? restName = null;
                        if (Match(TokenType.Identifier))
                        {
                            restName = Previous().Lexeme;
                        }
                        rest = new RestPattern(restName, token.Line, token.Column);
                        break;
                    }
                    
                    elements.Add(ParsePattern());
                } while (Match(TokenType.Comma));
            }
            
            Consume(TokenType.RightBracket, "Expect ']' after array destructuring pattern.");
            return new ArrayDestructuringPattern(elements, rest, token.Line, token.Column);
        }
        
        if (Match(TokenType.LeftBrace))
        {
            // Object destructuring: { prop: pattern, prop }
            var properties = new List<ObjectPatternProperty>();
            
            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    string key;
                    if (Check(TokenType.Identifier) || IsKeyword(Peek().Type))
                    {
                        key = ConsumeIdentifierOrKeyword("Expected property name in object destructuring pattern.");
                    }
                    else if (Match(TokenType.String))
                    {
                        key = (string)(Previous().Literal ?? "");
                    }
                    else
                    {
                        throw Error(Peek(), "Expected property name in object destructuring pattern.");
                    }
                    
                    if (Match(TokenType.Colon))
                    {
                        var pattern = ParsePattern();
                        properties.Add(new ObjectPatternProperty(key, pattern));
                    }
                    else
                    {
                        // Shorthand: { key } means { key: key }
                        properties.Add(new ObjectPatternProperty(key, null, key));
                    }
                } while (Match(TokenType.Comma));
            }
            
            Consume(TokenType.RightBrace, "Expect '}' after object destructuring pattern.");
            return new ObjectDestructuringPattern(properties, token.Line, token.Column);
        }
        
        throw Error(token, "Expected destructuring pattern ([...] or {...}).");
    }
}
