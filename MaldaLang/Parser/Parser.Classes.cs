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
    private Statement ClassDeclaration(bool isExported = false)
    {
        var nameToken = ConsumeIdentifierTokenLike("Expect class name.");
        var name = nameToken.Lexeme;

        List<string>? primaryParams = null;
        List<string?>? primaryHints = null;
        if (Match(TokenType.LeftParen))
        {
            (primaryParams, primaryHints) = ParsePrimaryConstructorParams();
            if (Check(TokenType.Extends))
            {
                throw Error(Peek(),
                    "A primary constructor cannot be combined with 'extends'. Write a classic class body and call super(...) explicitly.");
            }
        }

        string? superclass = null;
        if (Match(TokenType.Extends))
        {
            superclass = ConsumeIdentifierLike("Expect superclass name.");
        }

        var members = new List<ClassMember>();
        if (primaryParams != null && Match(TokenType.Semicolon))
        {
            // Data-only form: class Point(x, y);
        }
        else if (superclass == null && IsSingleMethodClassStart())
        {
            // Brace-less single-method class, optional primary constructor:
            // class Point function total() this.x + this.y;
            // class Point(x, y) function total() this.x + this.y;
            members.Add(ClassMember(name));
        }
        else
        {
            Consume(TokenType.LeftBrace, primaryParams != null
                ? "Expect '{', ';', or 'function' after primary constructor."
                : "Expect '{' after class name or 'function' for a single-method class.");
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                members.Add(ClassMember(name));
            }
            Consume(TokenType.RightBrace, "Expect '}' after class body.");
        }

        if (primaryParams != null)
        {
            ValidatePrimaryConstructor(name, nameToken, primaryParams, members);
            members = DesugarPrimaryConstructor(name, nameToken, primaryParams, primaryHints!, members);
        }

        return new ClassDeclaration(name, superclass, members, isExported, nameToken.Line, nameToken.Column, primaryParams != null);
    }

    /// <summary>
    /// <c>function</c>, optional <c>public</c>/<c>private</c> and <c>static</c>, starting a brace-less
    /// single-method class: <c>class Point function total() this.x + this.y;</c> or
    /// <c>class Point(x, y) function total() this.x + this.y;</c>.
    /// </summary>
    private bool IsSingleMethodClassStart()
    {
        var offset = 0;
        var first = Peek(offset);
        if (first == null)
            return false;
        if (first.Type == TokenType.Public || first.Type == TokenType.Private)
            offset++;
        var next = Peek(offset);
        if (next != null && next.Type == TokenType.Static)
            offset++;
        var fn = Peek(offset);
        return fn != null && fn.Type == TokenType.Function;
    }

    private (List<string> Names, List<string?> Hints) ParsePrimaryConstructorParams()
    {
        var names = new List<string>();
        var hints = new List<string?>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!Check(TokenType.RightParen))
        {
            do
            {
                if (names.Count > 0 && Check(TokenType.RightParen))
                    break; // trailing comma
                var paramToken = ConsumeIdentifierTokenLike("Expect parameter name in primary constructor.");
                var paramName = paramToken.Lexeme;
                if (!seen.Add(paramName))
                    throw Error(paramToken, $"Duplicate primary constructor parameter '{paramName}'.");
                names.Add(paramName);
                if (Match(TokenType.Colon))
                    hints.Add(Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme);
                else
                    hints.Add(null);
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after primary constructor parameters.");
        return (names, hints);
    }

    private void ValidatePrimaryConstructor(string className, Token nameToken, List<string> primaryParams, List<ClassMember> members)
    {
        var primaryNames = new HashSet<string>(primaryParams, StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (member.Type == MemberType.Constructor)
            {
                throw Error(nameToken,
                    $"Class '{className}' already has a primary constructor; do not declare function {className}(...).");
            }

            if (member.Type == MemberType.Field && primaryNames.Contains(member.Name))
            {
                throw Error(nameToken,
                    $"Field '{member.Name}' duplicates a primary constructor parameter.");
            }
        }
    }

    private static List<ClassMember> DesugarPrimaryConstructor(
        string className,
        Token nameToken,
        List<string> primaryParams,
        List<string?> primaryHints,
        List<ClassMember> bodyMembers)
    {
        var line = nameToken.Line;
        var column = nameToken.Column;
        var synthesized = new List<ClassMember>(primaryParams.Count + 1 + bodyMembers.Count);

        for (var i = 0; i < primaryParams.Count; i++)
        {
            synthesized.Add(new ClassMember(
                AccessModifier.Public,
                isStatic: false,
                MemberType.Field,
                primaryParams[i],
                value: null,
                primaryHints[i]));
        }

        var assignments = new List<Statement>(primaryParams.Count);
        foreach (var paramName in primaryParams)
        {
            var thisExpr = new ThisExpression(line, column);
            var target = new MemberAccessExpression(thisExpr, paramName, isNullConditional: false, line, column);
            var value = new IdentifierExpression(paramName, line, column);
            assignments.Add(new AssignmentStatement(target, value, TokenType.Assign, line, column));
        }

        var ctorBody = new BlockStatement(assignments, line, column);
        var ctorDecl = new FunctionDeclaration(
            className,
            new List<string>(primaryParams),
            ctorBody,
            decorators: null,
            parameterDecorators: null,
            parameterTypeHints: new List<string?>(primaryHints),
            returnType: null,
            isExported: false,
            line,
            column);
        synthesized.Add(new ClassMember(AccessModifier.Default, isStatic: false, MemberType.Constructor, className, ctorDecl));
        synthesized.AddRange(bodyMembers);
        return synthesized;
    }

    private MessageDeclaration ParseMessageDeclaration()
    {
        // We have already consumed the 'message' keyword.
        var nameToken = ConsumeIdentifierTokenLike("Expect message name after 'message'.");
        var name = nameToken.Lexeme;

        Consume(TokenType.LeftParen, "Expect '(' after message name.");
        var parameters = new List<string>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                if (parameters.Count > 0 && Check(TokenType.RightParen))
                    break; // trailing comma
                parameters.Add(ConsumeIdentifierLike("Expect parameter name in message declaration."));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after message parameters.");

        string? returnType = null;
        if (Match(TokenType.Arrow))
        {
            returnType = Consume(TokenType.Identifier, "Expect return type name after '->' in message declaration.").Lexeme;
        }
        else if (Check(TokenType.Minus) && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.GreaterThan)
        {
            // Support '-' '>' as an alternative way of writing '->', consistent with function parsing.
            Advance();
            Advance();
            returnType = Consume(TokenType.Identifier, "Expect return type name after '->' in message declaration.").Lexeme;
        }

        Consume(TokenType.Semicolon, "Expect ';' after message declaration.");

        return new MessageDeclaration(name, parameters, returnType);
    }
    
    private Statement ActorDeclaration()
    {
        var nameToken = ConsumeIdentifierTokenLike("Expect actor name.");
        var name = nameToken.Lexeme;
        
        Consume(TokenType.LeftBrace, "Expect '{' after actor name.");
        
        var members = new List<ClassMember>();
        var messages = new List<MessageDeclaration>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            if (Match(TokenType.Message))
            {
                messages.Add(ParseMessageDeclaration());
            }
            else
            {
                members.Add(ActorMember(name));
            }
        }
        
        Consume(TokenType.RightBrace, "Expect '}' after actor body.");
        return new ActorDeclaration(name, members, messages, nameToken.Line, nameToken.Column);
    }
    
    private ClassMember ActorMember(string actorName)
    {
        AccessModifier access = AccessModifier.Default;
        bool isStatic = false;
        
        if (Match(TokenType.Public))
            access = AccessModifier.Public;
        else if (Match(TokenType.Private))
            access = AccessModifier.Private;
        
        if (Match(TokenType.Static))
            isStatic = true;
        
        if (Check(TokenType.Message))
        {
            // Actor messages are parsed at the ActorDeclaration level, not here
            throw Error(Current() ?? new Token(TokenType.EOF, "", null, 0, 0), "Unexpected 'message' keyword in actor member. Message declarations must appear directly in the actor body without access modifiers.");
        }
        
        // Check for message handler (on handlerName(...) { ... })
        RejectRemovedFunctionAlias();

        if (Match(TokenType.On))
        {
            var handlerNameToken = ConsumeIdentifierTokenLike("Expect message handler name.");
            return MessageHandlerDeclaration(access, isStatic, handlerNameToken);
        }
        else if (Match(TokenType.Function))
        {
            var nameToken = ConsumeIdentifierTokenLike("Expect method name.");
            var name = nameToken.Lexeme;
            
            // Check if it's a constructor (same name as actor)
            if (name == actorName)
            {
                return ConstructorDeclaration(access, isStatic, nameToken);
            }
            else
            {
                return MethodDeclaration(access, isStatic, nameToken);
            }
        }
        else
        {
            // Field declaration
            Consume(TokenType.Var, "Expect 'var' for field declaration.");
            var name = ConsumeIdentifierLike("Expect field name.");
            string? typeHint = null;
            if (Match(TokenType.Colon))
                typeHint = Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme;
            Expression? initializer = null;
            if (Match(TokenType.Assign))
            {
                initializer = Expression();
            }
            Consume(TokenType.Semicolon, "Expect ';' after field declaration.");
            return new ClassMember(access, isStatic, MemberType.Field, name, initializer, typeHint);
        }
    }
    
    private ClassMember MessageHandlerDeclaration(AccessModifier access, bool isStatic, Token handlerNameToken)
    {
        var handlerName = handlerNameToken.Lexeme;
        // Parse function parameters and body (same as method)
        Consume(TokenType.LeftParen, "Expect '(' after message handler name.");
        var parameters = new List<string>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                if (parameters.Count > 0 && Check(TokenType.RightParen))
                    break; // trailing comma
                parameters.Add(ConsumeIdentifierOrInput("Expect parameter name."));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        
        Consume(TokenType.LeftBrace, "Expect '{' before message handler body.");
        var body = Block();
        
        var funcDecl = new FunctionDeclaration(handlerName, parameters, body, new List<Decorator>(), new List<Decorator>(), null, null, false, handlerNameToken.Line, handlerNameToken.Column);
        return new ClassMember(access, isStatic, MemberType.Method, handlerName, funcDecl);
    }
    
    private ClassMember ClassMember(string className)
    {
        AccessModifier access = AccessModifier.Default;
        bool isStatic = false;
        
        if (Match(TokenType.Public))
            access = AccessModifier.Public;
        else if (Match(TokenType.Private))
            access = AccessModifier.Private;
        
        if (Match(TokenType.Static))
            isStatic = true;

        RejectRemovedFunctionAlias();
        
        if (Match(TokenType.Function))
        {
            var nameToken = ConsumeIdentifierTokenLike("Expect method name.");
            var name = nameToken.Lexeme;
            
            // Check if it's a constructor (same name as class)
            if (name == className)
            {
                return ConstructorDeclaration(access, isStatic, nameToken);
            }
            else
            {
                return MethodDeclaration(access, isStatic, nameToken);
            }
        }
        else
        {
            // Field declaration
            Consume(TokenType.Var, "Expect 'var' for field declaration.");
            var name = ConsumeIdentifierLike("Expect field name.");
            string? typeHint = null;
            if (Match(TokenType.Colon))
                typeHint = Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme;
            Expression? initializer = null;
            if (Match(TokenType.Assign))
            {
                initializer = Expression();
            }
            Consume(TokenType.Semicolon, "Expect ';' after field declaration.");
            return new ClassMember(access, isStatic, MemberType.Field, name, initializer, typeHint);
        }
    }
    
    private ClassMember ConstructorDeclaration(AccessModifier access, bool isStatic, Token nameToken)
    {
        var name = nameToken.Lexeme;
        var decorators = ParseDecorators();
        Consume(TokenType.LeftParen, "Expect '(' after constructor name.");
        var parameters = new List<string>();
        var parameterDecorators = new List<Decorator>();
        var parameterTypeHints = new List<string?>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                if (parameters.Count > 0 && Check(TokenType.RightParen))
                    break; // trailing comma
                var paramDecorators = ParseDecorators();
                parameters.Add(ConsumeIdentifierOrInput("Expect parameter name."));
                parameterDecorators.AddRange(paramDecorators);
                if (Match(TokenType.Colon))
                    parameterTypeHints.Add(ParseTypeHintName());
                else
                    parameterTypeHints.Add(null);
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        string? returnType = null;
        if (Match(TokenType.Arrow))
            returnType = Consume(TokenType.Identifier, "Expect return type name after '->'.").Lexeme;
        else if (Check(TokenType.Minus) && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.GreaterThan)
        {
            Advance();
            Advance();
            returnType = Consume(TokenType.Identifier, "Expect return type name after '->'.").Lexeme;
        }
        Consume(TokenType.LeftBrace, "Expect '{' before constructor body.");
        var body = Block();
        var funcDecl = new FunctionDeclaration(name, parameters, body, decorators, parameterDecorators, parameterTypeHints, returnType, false, nameToken.Line, nameToken.Column);
        return new ClassMember(access, isStatic, MemberType.Constructor, name, funcDecl);
    }
    
    private ClassMember MethodDeclaration(AccessModifier access, bool isStatic, Token nameToken)
    {
        var name = nameToken.Lexeme;
        var decorators = ParseDecorators();
        Consume(TokenType.LeftParen, "Expect '(' after method name.");
        var parameters = new List<string>();
        var parameterDecorators = new List<Decorator>();
        var parameterTypeHints = new List<string?>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                if (parameters.Count > 0 && Check(TokenType.RightParen))
                    break; // trailing comma
                var paramDecorators = ParseDecorators();
                parameters.Add(ConsumeIdentifierLike("Expect parameter name."));
                parameterDecorators.AddRange(paramDecorators);
                if (Match(TokenType.Colon))
                    parameterTypeHints.Add(ParseTypeHintName());
                else
                    parameterTypeHints.Add(null);
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        string? returnType = null;
        if (Match(TokenType.Arrow))
            returnType = Consume(TokenType.Identifier, "Expect return type name after '->'.").Lexeme;
        else if (Check(TokenType.Minus) && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.GreaterThan)
        {
            Advance();
            Advance();
            returnType = Consume(TokenType.Identifier, "Expect return type name after '->'.").Lexeme;
        }
        var body = ParseFunctionOrMethodBody("method");
        var funcDecl = new FunctionDeclaration(name, parameters, body, decorators, parameterDecorators, parameterTypeHints, returnType, false, nameToken.Line, nameToken.Column);
        return new ClassMember(access, isStatic, MemberType.Method, name, funcDecl);
    }
    
    private string? GetCurrentClass()
    {
        // Simple heuristic - look back for class name
        for (int i = _current - 1; i >= 0 && i >= _current - 10; i--)
        {
            if (_tokens[i].Type == TokenType.Class && i + 1 < _tokens.Count)
                return _tokens[i + 1].Lexeme;
        }
        return null;
    }
}
