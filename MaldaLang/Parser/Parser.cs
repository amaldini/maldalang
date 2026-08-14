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

public class Parser
{
    private readonly List<Token> _tokens;
    private int _current = 0;
    private readonly List<ParseException> _errors = new();
    private readonly List<Token> _syntheticTokens = new();
    private readonly string? _sourceFileName;
    private readonly HashSet<string> _includeResolutionStack;
    private int _blockDepth = 0;
    private bool _inWorkflowBlock;
    private HashSet<string>? _workflowStepIds;
    
    public Parser(List<Token> tokens, string? sourceFileName = null, HashSet<string>? includeResolutionStack = null)
    {
        _tokens = tokens;
        _sourceFileName = sourceFileName;
        _includeResolutionStack = includeResolutionStack ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
    
    public List<ParseException> Errors => _errors;
    
    public List<Statement> Parse()
    {
        var statements = new List<Statement>();
        while (!IsAtEnd())
        {
            var stmt = Declaration();
            if (stmt == null)
                continue;

            ApplySourceFileRecursive(stmt);

            if (stmt is IncludeStatement includeStmt)
            {
                try
                {
                    statements.AddRange(ParseIncludedFile(includeStmt));
                }
                catch (ParseException ex)
                {
                    _errors.Add(ex);
                }
                continue;
            }

            statements.Add(stmt);
        }
        return statements;
    }

    private void ApplySourceFileRecursive(Node node)
    {
        ApplySourceFileRecursive(node, new HashSet<Node>());
    }

    private void ApplySourceFileRecursive(Node? node, HashSet<Node> visited)
    {
        if (node == null || !visited.Add(node))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(node.SourceFile))
        {
            node.SourceFile = _sourceFileName;
        }

        var properties = node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch
            {
                continue;
            }

            switch (value)
            {
                case Node child:
                    ApplySourceFileRecursive(child, visited);
                    break;
                case IEnumerable sequence when value is not string:
                    foreach (var item in sequence)
                    {
                        if (item is Node childNode)
                        {
                            ApplySourceFileRecursive(childNode, visited);
                        }
                    }
                    break;
            }
        }
    }
    
    private Statement? Declaration()
    {
        try
        {
            if (Match(TokenType.Include))
                return IncludeStatement();

            if (Match(TokenType.Import))
                return ImportStatement();

            if (Match(TokenType.Using))
            {
                // Disambiguate: `using alias = package;` vs `using name = expr { … }`
                if (Check(TokenType.Identifier) && Peek(1)?.Type == TokenType.Assign)
                {
                    if (LooksLikePackageUsingAlias())
                        return UsingStatement();
                    return UsingResourceStatement();
                }

                return UsingStatement();
            }

            var isExported = Match(TokenType.Export);
            if (isExported)
            {
                if (Match(TokenType.Class))
                    return ClassDeclaration(isExported);
                if (Match(TokenType.Function))
                    return FunctionDeclaration(isExported);
                if (Match(TokenType.Var, TokenType.Const))
                    return VarDeclaration(isExported, Previous().Type == TokenType.Const);
                if (Match(TokenType.Type))
                    return TypeDeclaration(isExported);
                if (Match(TokenType.Schema))
                    return SchemaDeclaration(isExported);
                var exportToken = Previous();
                throw Error(exportToken, "'export' must be followed by 'function', 'var', 'const', 'class', 'type', or 'schema'.");
            }
            
            if (Match(TokenType.Workflow))
                return WorkflowDeclaration();
            
            if (Match(TokenType.Actor))
                return ActorDeclaration();
            
            if (Match(TokenType.Class))
                return ClassDeclaration();
            
            if (Match(TokenType.Prompt))
                return PromptDeclaration([]);

            if (Match(TokenType.Property))
                return PropertyDeclaration();
            
            if (Match(TokenType.Type))
                return TypeDeclaration();

            if (Match(TokenType.Schema))
                return SchemaDeclaration();

            if (Match(TokenType.Api))
                return ApiDeclaration();

            if (Match(TokenType.Component))
                return ComponentDeclaration();
            
            // Check for decorators before function keyword
            // If we see @, parse decorators and then expect function keyword
            if (Check(TokenType.At))
            {
                // Parse decorators first
                var decorators = ParseDecorators();
                // Decorators currently support function and property declarations.
                if (Match(TokenType.Prompt))
                    return PromptDeclaration(decorators);
                if (!Match(TokenType.Function))
                {
                    if (Match(TokenType.Property))
                        return PropertyDeclarationWithDecorators(decorators);

                    var token = Current() ?? new Token(TokenType.EOF, "", null, 0, 0);
                    throw Error(token, "Decorators must be followed by 'function', 'fn', 'def', 'property', or 'prompt' keyword");
                }
                // Parse function declaration with the decorators we already parsed
                return FunctionDeclarationWithDecorators(decorators, false);
            }
            
            if (Match(TokenType.Function))
                return FunctionDeclaration();
            return Statement();
        }
        catch (ParseException ex)
        {
            _errors.Add(ex);
            Synchronize();
            return null;
        }
    }
    
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
        else
        {
            Consume(TokenType.LeftBrace, primaryParams != null
                ? "Expect '{' or ';' after primary constructor."
                : "Expect '{' after class name.");
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

        return new ClassDeclaration(name, superclass, members, isExported, nameToken.Line, nameToken.Column);
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
                var paramDecorators = ParseDecorators();
                parameters.Add(ConsumeIdentifierOrInput("Expect parameter name."));
                parameterDecorators.AddRange(paramDecorators);
                if (Match(TokenType.Colon))
                    parameterTypeHints.Add(Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme);
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
                var paramDecorators = ParseDecorators();
                parameters.Add(ConsumeIdentifierLike("Expect parameter name."));
                parameterDecorators.AddRange(paramDecorators);
                if (Match(TokenType.Colon))
                    parameterTypeHints.Add(Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme);
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
        Consume(TokenType.LeftBrace, "Expect '{' before method body.");
        var body = Block();
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
    
    private List<Decorator> ParseDecorators()
    {
        var decorators = new List<Decorator>();
        while (Match(TokenType.At))
        {
            var name = ConsumeIdentifierLike("Expect decorator name after @");
            Consume(TokenType.LeftParen, "Expect '(' after decorator name");
            var args = new List<Expression>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    args.Add(ParseDecoratorArgument());
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expect ')' after decorator arguments");
            decorators.Add(new Decorator(name, args, Current()?.Line ?? 0, Current()?.Column ?? 0));
        }
        return decorators;
    }

    /// <summary>
    /// Decorator arguments may be positional expressions or named <c>key: value</c> pairs
    /// (<c>@budget(tokens: 4000, tools: 8)</c>). Named form is decorator-only so it does
    /// not change general call-site <c>ArgList</c> parsing.
    /// </summary>
    private Expression ParseDecoratorArgument()
    {
        if (IsIdentifierLikeExpressionToken(Peek()?.Type) && Peek(1)?.Type == TokenType.Colon)
        {
            var nameToken = ConsumeIdentifierTokenLike("Expect named decorator argument.");
            Consume(TokenType.Colon, "Expect ':' after named decorator argument.");
            var value = Expression();
            return new NamedArgumentExpression(nameToken.Lexeme, value, nameToken.Line, nameToken.Column);
        }

        return Expression();
    }
    
    private Statement FunctionDeclaration(bool isExported = false)
    {
        // This is called when there's no decorator before function keyword
        var decorators = new List<Decorator>();
        return FunctionDeclarationWithDecorators(decorators, isExported);
    }

    // Syntax sugar:
    // component Name(params) { ... }
    // Desugars to:
    // @COMPONENT()
    // function Name(params) { ... }
    private Statement ComponentDeclaration()
    {
        var decorators = new List<Decorator>
        {
            new Decorator("COMPONENT", new List<Expression>(), Current()?.Line ?? 0, Current()?.Column ?? 0)
        };
        return FunctionDeclarationWithDecorators(decorators, false);
    }
    
    private Statement FunctionDeclarationWithDecorators(List<Decorator> decorators, bool isExported = false)
    {
        var nameToken = ConsumeIdentifierTokenLike("Expect function name.");
        var name = nameToken.Lexeme;
        Consume(TokenType.LeftParen, "Expect '(' after function name.");
        var parameters = new List<string>();
        var parameterDecorators = new List<Decorator>();
        var parameterTypeHints = new List<string?>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                // Parse optional parameter decorators (e.g., @PathParam("id"))
                var paramDecorators = ParseDecorators();
                var paramName = ConsumeIdentifierLike("Expect parameter name.");
                parameters.Add(paramName);
                // Store decorators for this parameter (only the first one if multiple, or empty list)
                parameterDecorators.AddRange(paramDecorators);
                // Optional type hint: : Type
                if (Match(TokenType.Colon))
                    parameterTypeHints.Add(Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme);
                else
                    parameterTypeHints.Add(null);
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        // Optional return type: -> Type
        string? returnType = null;
        if (Match(TokenType.Arrow))
            returnType = Consume(TokenType.Identifier, "Expect return type name after '->'.").Lexeme;
        else if (Check(TokenType.Minus) && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.GreaterThan)
        {
            Advance();
            Advance();
            returnType = Consume(TokenType.Identifier, "Expect return type name after '->'.").Lexeme;
        }
        BlockStatement body;
        if (Match(TokenType.LeftBrace))
        {
            body = Block();
        }
        else
        {
            // One-statement function: function square(x) x*x;
            var expr = Expression();
            var semi = Consume(TokenType.Semicolon, "Expect ';' after single-expression function body.");
            body = new BlockStatement(new List<Statement> { new ReturnStatement(expr, semi.Line, semi.Column) });
        }
        return new FunctionDeclaration(name, parameters, body, decorators, parameterDecorators, parameterTypeHints, returnType, isExported, nameToken.Line, nameToken.Column);
    }

    private Statement WorkflowDeclaration()
    {
        var nameToken = ConsumeIdentifierTokenLike("Expect workflow name.");
        var name = nameToken.Lexeme;
        Consume(TokenType.LeftParen, "Expect '(' after workflow name.");
        var parameters = new List<string>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                parameters.Add(ConsumeIdentifierOrInput("Expect parameter name."));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        Consume(TokenType.LeftBrace, "Expect '{' before workflow body.");
        var body = WorkflowBlock();
        Consume(TokenType.RightBrace, "Expect '}' after workflow body.");
        return new WorkflowDeclaration(name, parameters, body, nameToken.Line, nameToken.Column);
    }

    private BlockStatement WorkflowBlock()
    {
        var statements = new List<Statement>();
        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _workflowStepIds = stepIds;
        _inWorkflowBlock = true;
        _blockDepth++;
        try
        {
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                var stmt = WorkflowStatement(stepIds);
                if (stmt != null)
                    statements.Add(stmt);
            }
            return new BlockStatement(statements);
        }
        finally
        {
            _blockDepth--;
            _inWorkflowBlock = false;
            _workflowStepIds = null;
        }
    }

    private Statement? WorkflowStatement(HashSet<string> stepIds)
    {
        try
        {
            if (Match(TokenType.Step))
                return StepStatement(stepIds);
            if (Match(TokenType.Approval))
                return ApprovalStatement();
            if (Match(TokenType.Wait))
                return WaitSignalStatement();
            return Statement();
        }
        catch (ParseException ex)
        {
            _errors.Add(ex);
            Synchronize();
            return null;
        }
    }

    private Statement StepStatement(HashSet<string> stepIds)
    {
        var token = Previous();
        var stepId = ConsumeIdentifierLike("Expect step identifier.");
        Consume(TokenType.Assign, "Expect '=' after step identifier.");
        var callExpr = Call();
        var options = ParseStepOptions();
        Consume(TokenType.Semicolon, "Expect ';' after step statement.");

        if (stepIds.Contains(stepId))
            _errors.Add(Error(Current() ?? new Token(TokenType.Identifier, stepId, null, token.Line, token.Column), $"Duplicate step identifier '{stepId}' in same workflow scope.", "WF1003"));
        else
            stepIds.Add(stepId);

        if (options != null)
            ValidateStepOptions(options, token);

        return new WorkflowStepStatement(stepId, callExpr, options, token.Line, token.Column);
    }

    private WorkflowStepOptions? ParseStepOptions()
    {
        int? retryCount = null;
        string? backoff = null;
        int? delayMs = null;
        int? maxDelayMs = null;
        int? timeoutMs = null;
        Expression? compensate = null;

        while (true)
        {
            if (Match(TokenType.Retry))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'retry'.");
                retryCount = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.Backoff))
            {
                var s = Consume(TokenType.String, "Expect string after 'backoff' (e.g. \"fixed\", \"linear\", \"exponential\").");
                backoff = s.Literal as string ?? s.Lexeme.Trim('"');
            }
            else if (Match(TokenType.Delay))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'delay'.");
                delayMs = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.MaxDelay))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'maxDelay'.");
                maxDelayMs = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.Timeout))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'timeout'.");
                timeoutMs = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.Compensate))
            {
                compensate = Call();
            }
            else
                break;
        }

        if (retryCount == null && backoff == null && delayMs == null && maxDelayMs == null && timeoutMs == null && compensate == null)
            return null;

        return new WorkflowStepOptions(retryCount, backoff, delayMs, maxDelayMs, timeoutMs, compensate);
    }

    private void ValidateStepOptions(WorkflowStepOptions options, Token stepToken)
    {
        if (options.Backoff != null && options.RetryCount == null)
            _errors.Add(Error(stepToken, "'backoff' requires 'retry' to be specified.", "WF1004"));
        if (options.DelayMs != null && options.RetryCount == null)
            _errors.Add(Error(stepToken, "'delay' requires 'retry' to be specified.", "WF1004"));
        if (options.MaxDelayMs != null && options.RetryCount == null)
            _errors.Add(Error(stepToken, "'maxDelay' requires 'retry' to be specified.", "WF1004"));
        if (options.Backoff != null && options.Backoff != "fixed" && options.Backoff != "linear" && options.Backoff != "exponential")
            _errors.Add(Error(stepToken, $"Invalid backoff '{options.Backoff}'. Use \"fixed\", \"linear\", or \"exponential\".", "WF1004"));
        if (options.RetryCount.HasValue && options.RetryCount.Value < 0)
            _errors.Add(Error(stepToken, "Retry count must be >= 0.", "WF1004"));
    }

    private Statement ApprovalStatement()
    {
        var token = Previous();
        var approvalId = ConsumeIdentifierLike("Expect approval identifier.");
        Consume(TokenType.Assign, "Expect '=' after approval identifier.");
        Consume(TokenType.Approval, "Expect 'approval' after '='.");
        Consume(TokenType.LeftParen, "Expect '(' after 'approval'.");
        var nameExpr = Expression();
        Expression payloadExpr;
        if (Match(TokenType.Comma))
            payloadExpr = Expression();
        else
            payloadExpr = new LiteralExpression(null, nameExpr.Line, nameExpr.Column);
        Consume(TokenType.RightParen, "Expect ')' after approval arguments.");

        int? timeoutMs = null;
        Expression? onReject = null;
        while (true)
        {
            if (Match(TokenType.Timeout))
            {
                var n = Consume(TokenType.Integer, "Expect integer after 'timeout'.");
                timeoutMs = (int)(n.Literal ?? 0);
            }
            else if (Match(TokenType.OnReject))
            {
                onReject = Call();
            }
            else
                break;
        }
        Consume(TokenType.Semicolon, "Expect ';' after approval statement.");

        return new WorkflowApprovalStatement(approvalId, nameExpr, payloadExpr, timeoutMs, onReject, token.Line, token.Column);
    }

    private Statement WaitSignalStatement()
    {
        var token = Previous();
        var signalId = ConsumeIdentifierLike("Expect signal identifier.");
        Consume(TokenType.Assign, "Expect '=' after signal identifier.");
        if (!Check(TokenType.Identifier) || Peek().Lexeme != "awaitSignal")
            throw Error(Peek(), "Expect 'awaitSignal' after '=' in wait statement.");
        Advance(); // consume awaitSignal
        Consume(TokenType.LeftParen, "Expect '(' after 'awaitSignal'.");
        var nameExpr = Expression();
        Expression payloadExpr;
        if (Match(TokenType.Comma))
            payloadExpr = Expression();
        else
            payloadExpr = new LiteralExpression(null, nameExpr.Line, nameExpr.Column);
        Consume(TokenType.RightParen, "Expect ')' after awaitSignal arguments.");

        int? timeoutMs = null;
        while (Match(TokenType.Timeout))
        {
            var n = Consume(TokenType.Integer, "Expect integer after 'timeout'.");
            timeoutMs = (int)(n.Literal ?? 0);
        }
        Consume(TokenType.Semicolon, "Expect ';' after wait statement.");

        return new WorkflowAwaitSignalStatement(signalId, nameExpr, payloadExpr, timeoutMs, token.Line, token.Column);
    }

    private Statement PromptDeclaration(List<Decorator> decorators)
    {
        var nameToken = ConsumeIdentifierTokenLike("Expect prompt name.");
        var name = nameToken.Lexeme;
        Consume(TokenType.LeftParen, "Expect '(' after prompt name.");
        var parameters = new List<string>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                var paramName = ConsumeIdentifierLike("Expect parameter name.");
                parameters.Add(paramName);
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        
        // Parse optional return type: -> ReturnType, => ReturnType, or -> program(ApiName)
        string? returnType = null;
        if (Match(TokenType.Arrow))
        {
            returnType = ParsePromptReturnTypeName();
        }
        else if (Check(TokenType.Minus) && _current + 1 < _tokens.Count && _tokens[_current + 1].Type == TokenType.GreaterThan)
        {
            Advance(); // consume Minus
            Advance(); // consume GreaterThan
            returnType = ParsePromptReturnTypeName();
        }
        
        // Parse body - detect if statement-based or object literal
        var bodyToken = Consume(TokenType.LeftBrace, "Expect '{' before prompt body.");
        
        // Check if this is statement-based syntax (system, user, model, etc.)
        bool isStatementBased = false;
        if (Check(TokenType.Identifier))
        {
            var keyword = Peek().Lexeme;
            if (PromptBodyFields.IsName(keyword))
            {
                // Check if next token after identifier is NOT colon (statement-based)
                if (_current + 1 < _tokens.Count && _tokens[_current + 1].Type != TokenType.Colon)
                {
                    isStatementBased = true;
                }
            }
        }
        
        if (isStatementBased)
        {
            // Parse statement-based body
            var statements = new List<Statement>();
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                // Parse prompt body statements: system "...", user text, etc.
                if (!Match(TokenType.Identifier))
                {
                    throw Error(Peek(), $"Expect prompt body keyword ({PromptBodyFields.DisplayList}).");
                }
                
                var keyword = Previous().Lexeme;
                if (!PromptBodyFields.IsName(keyword))
                {
                    throw Error(Previous(), $"Unexpected keyword '{keyword}' in prompt body. Expected: {PromptBodyFields.DisplayList}.");
                }
                
                // Parse expression after keyword
                var expr = Expression();
                Consume(TokenType.Semicolon, "Expect ';' after prompt body statement.");
                
                // Create a prompt body statement for this field
                statements.Add(new PromptBodyStatement(keyword, expr, Previous().Line, Previous().Column));
            }
            Consume(TokenType.RightBrace, "Expect '}' after prompt body.");
            
            return new PromptDeclaration(name, parameters, statements, returnType, decorators, nameToken.Line, nameToken.Column);
        }
        else
        {
            // Parse object literal body (backward compatible)
            var properties = new List<(Expression Key, Expression Value)>();
            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    // Parse key (must be a string literal, identifier, or keyword)
                    Expression key;
                    if (Match(TokenType.String))
                    {
                        key = new LiteralExpression(Previous().Literal, Previous().Line, Previous().Column);
                    }
                    else if (Check(TokenType.Identifier) || IsKeyword(Peek().Type))
                    {
                        var keyName = ConsumeIdentifierOrKeyword("Expect string or identifier as object key.");
                        var keyToken = Previous();
                        key = new LiteralExpression(keyName, keyToken.Line, keyToken.Column);
                    }
                    else
                    {
                        throw Error(Peek(), "Expect string or identifier as object key.");
                    }
                    
                    Consume(TokenType.Colon, "Expect ':' after object key.");
                    var value = Expression();
                    properties.Add((key, value));
                    Match(TokenType.Semicolon); // optional; object-literal prompts also accept key: value;
                    // Continue on comma, or when next token is a key (allow missing comma between properties)
                } while (Match(TokenType.Comma) || (!Check(TokenType.RightBrace) && (Check(TokenType.Identifier) || Check(TokenType.String))));
            }
            Consume(TokenType.RightBrace, "Expect '}' after prompt body.");
            var body = new ObjectLiteralExpression(properties, bodyToken.Line, bodyToken.Column);
            
            return new PromptDeclaration(name, parameters, body, returnType, decorators, nameToken.Line, nameToken.Column);
        }
    }

    private Statement PropertyDeclaration()
    {
        return PropertyDeclarationWithDecorators(new List<Decorator>());
    }

    private Statement PropertyDeclarationWithDecorators(List<Decorator> decorators)
    {
        var token = Previous();
        var name = ConsumeIdentifierLike("Expect property name.");
        var parameters = new List<string>();

        if (Match(TokenType.LeftParen))
        {
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    parameters.Add(ConsumeIdentifierLike("Expect parameter name."));
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expect ')' after property parameters.");
        }

        Consume(TokenType.LeftBrace, "Expect '{' before property body.");
        var body = Block();
        return new PropertyDeclaration(name, parameters, body, decorators, token.Line, token.Column);
    }
    
    private Statement SchemaDeclaration(bool isExported = false)
    {
        var schemaToken = Previous();
        var schemaName = ConsumeIdentifierLike("Expect schema name after 'schema'.");
        Consume(TokenType.LeftBrace, "Expect '{' after schema name.");
        var fields = new List<SchemaField>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            var fieldName = ConsumeIdentifierLike("Expect field name in schema.");
            Consume(TokenType.Colon, "Expect ':' after schema field name.");
            var typeName = ParseSchemaType("Expect field type in schema.", out var required);
            Consume(TokenType.Semicolon, "Expect ';' after schema field.");
            fields.Add(new SchemaField(fieldName, typeName, required));
        }
        Consume(TokenType.RightBrace, "Expect '}' after schema fields.");
        return new SchemaDeclaration(schemaName, fields, isExported, schemaToken.Line, schemaToken.Column);
    }

    private Statement ApiDeclaration()
    {
        var apiToken = Previous();
        var apiName = ConsumeIdentifierLike("Expect api name after 'api'.");
        Consume(TokenType.LeftBrace, "Expect '{' after api name.");
        var methods = new List<ApiMethodSignature>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            Consume(TokenType.Function, "Expect 'function' method signature in api body.");
            var methodName = ConsumeIdentifierLike("Expect method name in api.");
            Consume(TokenType.LeftParen, "Expect '(' after api method name.");
            var paramNames = new List<string>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    paramNames.Add(ConsumeIdentifierOrKeyword("Expect parameter name in api method."));
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expect ')' after api method parameters.");
            Consume(TokenType.Semicolon, "Expect ';' after api method signature (bodies are separate top-level functions).");
            methods.Add(new ApiMethodSignature(methodName, paramNames));
        }
        Consume(TokenType.RightBrace, "Expect '}' after api methods.");
        return new ApiDeclaration(apiName, methods, apiToken.Line, apiToken.Column);
    }

    /// <summary>
    /// Schema field / variant-constructor payload type: Identifier, optional <c>[]</c>, optional <c>?</c>.
    /// Prompt parameters stay name-only and must not call this.
    /// </summary>
    private string ParseSchemaType(string expectTypeMessage, out bool required)
    {
        var typeName = ConsumeIdentifierLike(expectTypeMessage);
        if (Match(TokenType.LeftBracket))
        {
            Consume(TokenType.RightBracket, "Expect ']' after '[' in array type.");
            typeName += "[]";
        }

        required = true;
        if (Match(TokenType.QuestionMark))
            required = false;
        return typeName;
    }

    private string ParsePromptReturnTypeName()
    {
        var nameToken = Consume(TokenType.Identifier, "Expect return type name after '->'.");
        var name = nameToken.Lexeme;
        if (string.Equals(name, "program", StringComparison.Ordinal) && Match(TokenType.LeftParen))
        {
            var apiName = ConsumeIdentifierLike("Expect api name in program(ApiName).");
            Consume(TokenType.RightParen, "Expect ')' after program(ApiName).");
            return "program(" + apiName + ")";
        }

        return name;
    }

    private Statement TypeDeclaration(bool isExported = false)
    {
        var typeToken = Previous();
        var typeName = ConsumeIdentifierLike("Expect type name after 'type'.");
        Consume(TokenType.Assign, "Expect '=' after type name.");
        var constructors = new List<VariantConstructor>();
        do
        {
            var ctorName = ConsumeIdentifierLike("Expect constructor name.");
            var paramNames = new List<string>();
            var paramTypes = new List<string?>();
            var paramRequired = new List<bool>();
            if (Match(TokenType.LeftParen))
            {
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        paramNames.Add(ConsumeIdentifierOrKeyword("Expect parameter name in constructor."));
                        if (Match(TokenType.Colon))
                        {
                            var payloadType = ParseSchemaType(
                                "Expect constructor payload type after ':'.",
                                out var required);
                            paramTypes.Add(payloadType);
                            paramRequired.Add(required);
                        }
                        else
                        {
                            paramTypes.Add(null);
                            paramRequired.Add(true);
                        }
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expect ')' after constructor parameters.");
            }
            constructors.Add(new VariantConstructor(ctorName, paramNames, paramTypes, paramRequired));
        } while (Match(TokenType.Pipe));
        Consume(TokenType.Semicolon, "Expect ';' after type declaration.");
        return new TypeDeclaration(typeName, constructors, isExported, typeToken.Line, typeToken.Column);
    }
    
    private Statement Statement()
    {
        if (Match(TokenType.If)) return IfStatement();
        if (Match(TokenType.While)) return WhileStatement();
        if (Match(TokenType.Foreach)) return ForeachStatement();
        if (Match(TokenType.For)) return ForStatement();
        if (Match(TokenType.Return)) return ReturnStatement();
        if (Match(TokenType.Print)) return PrintStatement();
        if (Match(TokenType.Break)) return BreakStatement();
        if (Match(TokenType.Continue)) return ContinueStatement();
        if (Match(TokenType.Try)) return TryStatement();
        if (Match(TokenType.Throw)) return ThrowStatement();
        if (Match(TokenType.Defer)) return DeferStatement();
        if (Match(TokenType.Using))
        {
            if (Check(TokenType.Identifier) && Peek(1)?.Type == TokenType.Assign)
                return UsingResourceStatement();
            throw Error(Previous(), "Package 'using' is only allowed at top-level. Use 'using name = expr { ... }' for resources.");
        }
        if (Match(TokenType.Send)) return SendStatement();
        // Allow bare `match` expressions as statements without requiring a trailing semicolon:
        // 
        //     match msg {
        //         case Inc(n): value = value + n;
        //         default: {};
        //     }
        //
        // These are parsed as expressions elsewhere (e.g., in variable initializers), but here we
        // want them to behave like statement blocks. We parse them via MatchExpression and wrap
        // them in an ExpressionStatement, without consuming a ';' after the closing '}'.
        if (Check(TokenType.Match))
        {
            var matchExpr = MatchExpression();
            return new ExpressionStatement(matchExpr, matchExpr.Line, matchExpr.Column);
        }
        // Check for destructuring assignment before block: [x, y] = ... or { name, age } = ...
        // We use lookahead so plain expression statements like [1,2,3].forEach(...) are not
        // misparsed as destructuring assignments.
        if (IsArrayDestructuringAssignmentStart() || IsObjectDestructuringAssignmentStart())
        {
            var pattern = ParseDestructuringPattern();
            if (!Check(TokenType.Assign))
            {
                var currentToken = Peek();
                throw Error(currentToken, $"Expect '=' after destructuring pattern (pattern started at line {pattern.Line}, column {pattern.Column}).");
            }
            Advance();
            var value = Expression();
            Consume(TokenType.Semicolon, "Expect ';' after destructuring assignment.");
            return new DestructuringAssignment(pattern, value, pattern.Line, pattern.Column);
        }
        if (Match(TokenType.LeftBrace)) return Block();
        if (Match(TokenType.Var, TokenType.Const))
            return VarDeclaration(isConst: Previous().Type == TokenType.Const);
        
        // Try to parse as assignment first
        var expr = LogicalOr();
        if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign, TokenType.MultiplyAssign, TokenType.DivideAssign))
        {
            var equals = Previous();
            var value = Expression();
            var assignment = ParseAssignment(expr, value, equals);
            Consume(TokenType.Semicolon, "Expect ';' after assignment.");
            return assignment;
        }
        
        // Otherwise it's an expression statement
        Consume(TokenType.Semicolon, "Expect ';' after expression.");
        return new ExpressionStatement(expr, expr.Line, expr.Column);
    }
    
    private Statement SendStatement()
    {
        var token = Previous();
        
        // Parse the initial expression after 'send'
        var expr = Expression();
        
        // Two syntaxes supported:
        // 1. Call-style with handler: send target.handlerName(args...) [then (result) { ... }];
        // 2. Direct call without handler: send target(args...) [then (result) { ... }];
        if (expr is FunctionCallExpression callExpr)
        {
            Expression target;
            string? handlerName;
            
            if (callExpr.Callee is MemberAccessExpression memberAccess)
            {
                // Syntax 1: send target.handlerName(args...)
                target = memberAccess.Object;
                handlerName = memberAccess.Member;
            }
            else
            {
                // Syntax 2: send target(args...) - no handler name specified
                target = callExpr.Callee;
                handlerName = null;
            }
            
            var arguments = callExpr.Arguments;
            CallbackDefinition? callback = null;
            
            // Optional callback: then (result) { ... }
            if (Match(TokenType.Then))
            {
                Consume(TokenType.LeftParen, "Expect '(' after 'then' in send callback.");
                var paramToken = ConsumeIdentifierTokenLike("Expect callback parameter name after '('.");
                Consume(TokenType.RightParen, "Expect ')' after callback parameter name.");

                // Parse callback body: require '{' then use Block() like other statement bodies
                Consume(TokenType.LeftBrace, "Expect '{' before send callback body.");
                var callbackBody = Block();
                callback = new CallbackDefinition(paramToken.Lexeme, callbackBody);
            }
            
            // Optional timeout: timeout <expression>
            Expression? timeoutMs = null;
            CallbackDefinition? timeoutErrorHandler = null;
            if (Match(TokenType.Timeout))
            {
                // Parse timeout value expression
                timeoutMs = Expression();
                
                // Optional timeout error handler: catch (error) { ... }
                if (Match(TokenType.Catch))
                {
                    Consume(TokenType.LeftParen, "Expect '(' after 'catch' in timeout error handler.");
                    var errorParamToken = ConsumeIdentifierTokenLike("Expect error parameter name after '('.");
                    Consume(TokenType.RightParen, "Expect ')' after error parameter name.");
                    
                    // Parse error handler body
                    Consume(TokenType.LeftBrace, "Expect '{' before timeout error handler body.");
                    var errorHandlerBody = Block();
                    timeoutErrorHandler = new CallbackDefinition(errorParamToken.Lexeme, errorHandlerBody);
                }
            }
            
            Consume(TokenType.Semicolon, "Expect ';' after send statement.");
            return new SendStatement(target, handlerName, arguments, callback, timeoutMs, timeoutErrorHandler, token.Line, token.Column);
        }
        
        throw Error(Previous(), "Invalid send statement. Use 'send target.handler(args...) [then (result) { ... }];' or 'send target(args...) [then (result) { ... }];'.");
    }
    
    private Statement IfStatement()
    {
        var token = Previous();
        Consume(TokenType.LeftParen, "Expect '(' after 'if'.");
        var condition = Expression();
        Consume(TokenType.RightParen, "Expect ')' after condition.");
        var thenBranch = Statement();
        Statement? elseBranch = null;
        if (Match(TokenType.Else))
        {
            elseBranch = Statement();
        }
        return new IfStatement(condition, thenBranch, elseBranch, token.Line, token.Column);
    }
    
    private Statement WhileStatement()
    {
        var token = Previous();
        Consume(TokenType.LeftParen, "Expect '(' after 'while'.");
        var condition = Expression();
        Consume(TokenType.RightParen, "Expect ')' after condition.");
        var body = Statement();
        return new WhileStatement(condition, body, token.Line, token.Column);
    }
    
    private Statement ForeachStatement()
    {
        var token = Previous();
        Consume(TokenType.LeftParen, "Expect '(' after 'foreach'.");
        Consume(TokenType.Var, "Expect 'var' after '(' in foreach.");
        var name = ConsumeIdentifierLike("Expect variable name.");
        Consume(TokenType.In, "Expect 'in' after variable name in foreach.");
        var collection = Expression();
        Consume(TokenType.RightParen, "Expect ')' after collection expression.");
        var body = Statement();
        return new ForInStatement(name, collection, body, token.Line, token.Column);
    }
    
    private Statement ForStatement()
    {
        var token = Previous();
        Consume(TokenType.LeftParen, "Expect '(' after 'for'.");
        
        // Check for for-in syntax: for (var x in collection)
        if (Match(TokenType.Var))
        {
            var varToken = Previous();
            var name = ConsumeIdentifierLike("Expect variable name.");
            
            // Check if this is a for-in loop
            if (Match(TokenType.In))
            {
                var collection = Expression();
                Consume(TokenType.RightParen, "Expect ')' after collection expression.");
                var forInBody = Statement();
                return new ForInStatement(name, collection, forInBody, token.Line, token.Column);
            }
            
            // Otherwise, it's a regular for loop with var declaration
            Consume(TokenType.Assign, "Expect '=' after variable name.");
            var initializerExpr = Expression();
            var varInitializer = new VarDeclStatement(name, initializerExpr, null, false, false, varToken.Line, varToken.Column);
            
            // Continue with regular for loop parsing
            Consume(TokenType.Semicolon, "Expect ';' after loop initializer.");
            
            Expression? loopCondition = null;
            if (!Check(TokenType.Semicolon))
            {
                loopCondition = Expression();
            }
            Consume(TokenType.Semicolon, "Expect ';' after loop condition.");
            
            Statement? loopIncrement = null;
            if (!Check(TokenType.RightParen))
            {
                // Try to parse as assignment first
                var expr = LogicalOr();
                if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign, TokenType.MultiplyAssign, TokenType.DivideAssign))
                {
                    var equals = Previous();
                    var value = Expression();
                    loopIncrement = ParseAssignment(expr, value, equals);
                }
                else
                {
                    loopIncrement = new ExpressionStatement(expr, expr.Line, expr.Column);
                }
            }
            Consume(TokenType.RightParen, "Expect ')' after for clauses.");
            
            var loopBody = Statement();
            
            // Desugar for loop into while loop
            if (loopIncrement != null)
            {
                var statements = new List<Statement> { loopBody, loopIncrement };
                loopBody = new BlockStatement(statements);
            }
            
            if (loopCondition == null)
            {
                loopCondition = new LiteralExpression(true);
            }
            loopBody = new WhileStatement(loopCondition, loopBody);
            
            var statements2 = new List<Statement> { varInitializer, loopBody };
            loopBody = new BlockStatement(statements2);
            
            return loopBody;
        }
        
        Statement? initializer = null;
        if (!Check(TokenType.Semicolon))
        {
            var expr = LogicalOr();
            if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign, TokenType.MultiplyAssign, TokenType.DivideAssign))
            {
                var equals = Previous();
                var value = Expression();
                initializer = ParseAssignment(expr, value, equals);
            }
            else
            {
                if (Match(TokenType.Semicolon)) { }
                initializer = new ExpressionStatement(expr, expr.Line, expr.Column);
            }
        }
        Consume(TokenType.Semicolon, "Expect ';' after loop initializer.");
        
        Expression? condition = null;
        if (!Check(TokenType.Semicolon))
        {
            condition = Expression();
        }
        Consume(TokenType.Semicolon, "Expect ';' after loop condition.");
        
        Statement? incrementStmt = null;
        if (!Check(TokenType.RightParen))
        {
            // Try to parse as assignment first
            var expr = LogicalOr();
            if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign, TokenType.MultiplyAssign, TokenType.DivideAssign))
            {
                var equals = Previous();
                var value = Expression();
                incrementStmt = ParseAssignment(expr, value, equals);
            }
            else
            {
                incrementStmt = new ExpressionStatement(expr, expr.Line, expr.Column);
            }
        }
        Consume(TokenType.RightParen, "Expect ')' after for clauses.");
        
        var body = Statement();
        
        // Desugar for loop into while loop
        if (incrementStmt != null)
        {
            var statements = new List<Statement> { body, incrementStmt };
            body = new BlockStatement(statements);
        }
        
        if (condition == null)
        {
            condition = new LiteralExpression(true);
        }
        body = new WhileStatement(condition, body);
        
        if (initializer != null)
        {
            var statements = new List<Statement> { initializer, body };
            body = new BlockStatement(statements);
        }
        
        return body;
    }
    
    private Statement ReturnStatement()
    {
        var token = Previous();
        Expression? value = null;
        if (!Check(TokenType.Semicolon))
        {
            value = Expression();
        }
        Consume(TokenType.Semicolon, "Expect ';' after return value.");
        return new ReturnStatement(value, token.Line, token.Column);
    }
    
    private Statement PrintStatement()
    {
        var token = Previous();
        Consume(TokenType.LeftParen, "Expect '(' after 'print'.");
        var value = Expression();
        Consume(TokenType.RightParen, "Expect ')' after value.");
        Consume(TokenType.Semicolon, "Expect ';' after value.");
        return new PrintStatement(value, token.Line, token.Column);
    }
    
    private Statement BreakStatement()
    {
        var token = Previous();
        Consume(TokenType.Semicolon, "Expect ';' after 'break'.");
        return new BreakStatement(token.Line, token.Column);
    }
    
    private Statement ContinueStatement()
    {
        var token = Previous();
        Consume(TokenType.Semicolon, "Expect ';' after 'continue'.");
        return new ContinueStatement(token.Line, token.Column);
    }
    
    private Statement ThrowStatement()
    {
        var token = Previous();
        var exception = Expression();
        Consume(TokenType.Semicolon, "Expect ';' after throw expression.");
        return new ThrowStatement(exception, token.Line, token.Column);
    }
    
    private Statement TryStatement()
    {
        var token = Previous();
        Consume(TokenType.LeftBrace, "Expect '{' after 'try'.");
        var tryBlock = Block();
        
        var catchClauses = new List<CatchClause>();
        while (Match(TokenType.Catch))
        {
            catchClauses.Add(CatchClause());
        }
        
        BlockStatement? finallyBlock = null;
        if (Match(TokenType.Finally))
        {
            Consume(TokenType.LeftBrace, "Expect '{' after 'finally'.");
            finallyBlock = Block();
        }
        
        if (catchClauses.Count == 0 && finallyBlock == null)
        {
            throw Error(Previous(), "Try statement must have at least one catch clause or a finally block.");
        }
        
        return new TryStatement(tryBlock, catchClauses, finallyBlock, token.Line, token.Column);
    }
    
    private CatchClause CatchClause()
    {
        var token = Previous();
        string? exceptionVariable = null;
        
        Expression? filter = null;
        if (Match(TokenType.LeftParen))
        {
            exceptionVariable = ConsumeIdentifierLike("Expect exception variable name.");
            if (Match(TokenType.If))
            {
                if (exceptionVariable == null)
                    throw Error(Previous(), "Catch filter requires an exception variable: catch (e if condition).");
                filter = Expression();
            }

            Consume(TokenType.RightParen, "Expect ')' after catch parameter.");
        }
        
        Consume(TokenType.LeftBrace, "Expect '{' after 'catch'.");
        var body = Block();
        
        return new CatchClause(exceptionVariable, body, filter, token.Line, token.Column);
    }
    
    private BlockStatement Block()
    {
        var statements = new List<Statement>();
        _blockDepth++;
        try
        {
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                var stmt = _inWorkflowBlock && _workflowStepIds != null
                    ? WorkflowStatement(_workflowStepIds)
                    : Declaration();
                if (stmt != null)
                    statements.Add(stmt);
            }
            Consume(TokenType.RightBrace, "Expect '}' after block.");
            return new BlockStatement(statements);
        }
        finally
        {
            _blockDepth--;
        }
    }
    
    private Statement VarDeclaration(bool isExported = false, bool isConst = false)
    {
        var token = Previous();
        string? typeHint = null;
        Expression initializer;
        
        // Check if this is a destructuring pattern: [x, y] or {x, y}
        if (Check(TokenType.LeftBracket) || Check(TokenType.LeftBrace))
        {
            var pattern = ParseDestructuringPattern();
            if (Match(TokenType.Colon))
                typeHint = Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme;
            if (!Check(TokenType.Assign))
            {
                var currentToken = Peek();
                throw Error(currentToken, $"Expect '=' after destructuring pattern (pattern started at line {pattern.Line}, column {pattern.Column}).");
            }
            Advance();
            initializer = Expression();
            Consume(TokenType.Semicolon, "Expect ';' after destructuring declaration.");
            
            return new DestructuringVarDecl(pattern, initializer, typeHint, token?.Line ?? 0, token?.Column ?? 0);
        }
        
        // Regular variable declaration
        var name = ConsumeIdentifierLike("Expect variable name.");
        typeHint = null;
        if (Match(TokenType.Colon))
            typeHint = Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme;
        Consume(TokenType.Assign, "Expect '=' after variable name.");
        initializer = Expression();
        Consume(TokenType.Semicolon, "Expect ';' after variable declaration.");
        
        return new VarDeclStatement(name, initializer, typeHint, isExported, isConst, token?.Line ?? 0, token?.Column ?? 0);
    }

    private Statement ImportStatement()
    {
        var token = Previous();
        if (_blockDepth > 0)
            throw Error(token, "'import' is only allowed at top-level scope.");

        // Selective: import { a, b } from "…" | package
        if (Check(TokenType.LeftBrace))
            return ParseSelectiveImport(token);

        string? alias = null;
        if (Check(TokenType.Identifier) && Peek(1)?.Type == TokenType.Assign)
        {
            alias = ConsumeIdentifierLike("Expect alias name.");
            Consume(TokenType.Assign, "Expect '=' after alias name.");
        }

        if (Check(TokenType.String))
        {
            var pathToken = Consume(TokenType.String, "Expect string literal path after 'import'.");
            Consume(TokenType.Semicolon, "Expect ';' after import statement.");

            var filePath = pathToken.Literal as string;
            if (string.IsNullOrWhiteSpace(filePath))
                filePath = pathToken.Lexeme.Trim('"');

            return new ImportStatement(filePath, null, null, alias, token.Line, token.Column);
        }

        var packageName = ConsumePackageName("Expect package name.");
        string? subModule = null;
        while (Match(TokenType.Dot))
        {
            if (subModule == null)
                subModule = ConsumeIdentifierLike("Expect sub-module name.");
            else
                subModule += "." + ConsumeIdentifierLike("Expect sub-module name.");
        }

        Consume(TokenType.Semicolon, "Expect ';' after import statement.");

        return new ImportStatement(null, packageName, subModule, alias, token.Line, token.Column);
    }

    private Statement ParseSelectiveImport(Token importToken)
    {
        Consume(TokenType.LeftBrace, "Expect '{' after 'import' for selective import.");
        var selected = new List<string>();
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                selected.Add(ConsumeIdentifierLike("Expect imported binding name."));
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBrace, "Expect '}' after selective import list.");
        if (selected.Count == 0)
            throw Error(Previous(), "Selective import list must contain at least one name.");

        // Contextual 'from' — not a reserved keyword.
        if (!Check(TokenType.Identifier) ||
            !string.Equals(Peek().Lexeme, "from", StringComparison.Ordinal))
        {
            throw Error(Peek(), "Expect 'from' after selective import list.");
        }

        Advance();

        if (Check(TokenType.String))
        {
            var pathToken = Consume(TokenType.String, "Expect string literal path after 'from'.");
            Consume(TokenType.Semicolon, "Expect ';' after import statement.");

            var filePath = pathToken.Literal as string;
            if (string.IsNullOrWhiteSpace(filePath))
                filePath = pathToken.Lexeme.Trim('"');

            return new ImportStatement(
                filePath, null, null, null,
                importToken.Line, importToken.Column,
                selected);
        }

        var packageName = ConsumePackageName("Expect package name after 'from'.");
        string? subModule = null;
        while (Match(TokenType.Dot))
        {
            if (subModule == null)
                subModule = ConsumeIdentifierLike("Expect sub-module name.");
            else
                subModule += "." + ConsumeIdentifierLike("Expect sub-module name.");
        }

        Consume(TokenType.Semicolon, "Expect ';' after import statement.");

        return new ImportStatement(
            null, packageName, subModule, null,
            importToken.Line, importToken.Column,
            selected);
    }

    /// <summary>
    /// After <c>using</c>, with look-ahead at <c>Identifier Assign …</c>: true when the
    /// RHS is a package name ending in <c>;</c> (not a resource <c>using</c> initializer).
    /// </summary>
    private bool LooksLikePackageUsingAlias()
    {
        // Peek(0)=alias, Peek(1)='=', Peek(2)=package start
        var index = 2;
        if (!IsPackageNameStart(Peek(index)))
            return false;
        index++;

        while (true)
        {
            var token = Peek(index);
            if (token?.Type == TokenType.Minus)
            {
                index++;
                if (!IsPackageNameStart(Peek(index)))
                    return false;
                index++;
                continue;
            }

            if (token?.Type == TokenType.Dot)
            {
                index++;
                if (!IsPackageNameStart(Peek(index)))
                    return false;
                index++;
                continue;
            }

            break;
        }

        return Peek(index)?.Type == TokenType.Semicolon;
    }

    private bool IsPackageNameStart(Token? token)
    {
        if (token == null)
            return false;
        return token.Type == TokenType.Identifier ||
               token.Type == TokenType.Input ||
               IsKeyword(token.Type);
    }

    private Statement UsingResourceStatement()
    {
        var token = Previous();
        var variableName = ConsumeIdentifierLike("Expect variable name after 'using'.");
        Consume(TokenType.Assign, "Expect '=' after using variable name.");
        var initializer = Expression();
        Consume(TokenType.LeftBrace, "Expect '{' after using initializer.");
        var body = Block();
        return new UsingResourceStatement(variableName, initializer, body, token.Line, token.Column);
    }

    private Statement DeferStatement()
    {
        var token = Previous();
        Consume(TokenType.LeftBrace, "Expect '{' after 'defer'.");
        var body = Block();
        return new DeferStatement(body, token.Line, token.Column);
    }

    private Statement UsingStatement()
    {
        var token = Previous();
        string? alias = null;
        string packageName;
        string? subModule = null;
        
        // Check for alias syntax: using Alias = PackageName;
        if (Check(TokenType.Identifier) && Peek(1)?.Type == TokenType.Assign)
        {
            alias = ConsumeIdentifierLike("Expect alias name.");
            Consume(TokenType.Assign, "Expect '=' after alias name.");
        }
        
        packageName = ConsumePackageName("Expect package name.");
        
        while (Match(TokenType.Dot))
        {
            if (subModule == null)
                subModule = ConsumeIdentifierLike("Expect sub-module name.");
            else
                subModule += "." + ConsumeIdentifierLike("Expect sub-module name.");
        }
        
        Consume(TokenType.Semicolon, "Expect ';' after using statement.");
        
        return new UsingStatement(packageName, subModule, alias, token.Line, token.Column);
    }

    private Statement IncludeStatement()
    {
        var token = Previous();
        if (_blockDepth > 0)
            throw Error(token, "'include' is only allowed at top-level scope.");

        var includePathToken = Consume(TokenType.String, "Expect string literal path after 'include'.");
        Consume(TokenType.Semicolon, "Expect ';' after include statement.");

        var includePath = includePathToken.Literal as string;
        if (string.IsNullOrWhiteSpace(includePath))
            includePath = includePathToken.Lexeme.Trim('"');

        return new IncludeStatement(includePath ?? string.Empty, token.Line, token.Column);
    }

    private List<Statement> ParseIncludedFile(IncludeStatement includeStmt)
    {
        if (string.IsNullOrWhiteSpace(includeStmt.IncludePath))
            throw new ParseException(includeStmt.Line, includeStmt.Column, "Include path cannot be empty.", _sourceFileName);

        var baseDirectory = !string.IsNullOrWhiteSpace(_sourceFileName)
            ? Path.GetDirectoryName(Path.GetFullPath(_sourceFileName!))
            : System.Environment.CurrentDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = System.Environment.CurrentDirectory;

        var resolvedPath = Path.IsPathRooted(includeStmt.IncludePath)
            ? Path.GetFullPath(includeStmt.IncludePath)
            : Path.GetFullPath(Path.Combine(baseDirectory, includeStmt.IncludePath));

        if (_includeResolutionStack.Contains(resolvedPath))
            throw new ParseException(includeStmt.Line, includeStmt.Column, $"Circular include detected: {resolvedPath}", _sourceFileName);

        if (!File.Exists(resolvedPath))
            throw new ParseException(includeStmt.Line, includeStmt.Column, $"Included file not found: {resolvedPath}", _sourceFileName);

        var includeSource = File.ReadAllText(resolvedPath);
        var includeLexer = new Lexer(includeSource, resolvedPath);
        var includeTokens = includeLexer.Tokenize();

        var nestedStack = new HashSet<string>(_includeResolutionStack, StringComparer.OrdinalIgnoreCase)
        {
            resolvedPath
        };

        var includeParser = new Parser(includeTokens, resolvedPath, nestedStack);
        var includeStatements = includeParser.Parse();
        if (includeParser.Errors.Count > 0)
        {
            // Preserve the original included-file location metadata.
            throw includeParser.Errors[0];
        }

        return includeStatements;
    }
    
    
    private Expression Expression()
    {
        return Assignment();
    }
    
    // Public method to parse a single expression from tokens
    public static Expression ParseExpression(List<Token> tokens)
    {
        var parser = new Parser(tokens);
        var expr = parser.Expression();
        
        // Verify we consumed all tokens except EOF
        if (!parser.IsAtEnd())
        {
            var remainingToken = parser.Peek();
            throw new ParseException(remainingToken.Line, remainingToken.Column, 
                $"Unexpected token after expression: {remainingToken.Type}");
        }
        
        return expr;
    }
    
    private Expression Assignment()
    {
        var expr = Pipe();
        
        if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign, TokenType.MultiplyAssign, TokenType.DivideAssign))
        {
            var equals = Previous();
            var value = Assignment();
            
            // Check if left side is a destructuring pattern
            if (expr is MatchExpression matchExpr && matchExpr.Value is LiteralExpression)
            {
                // This shouldn't happen - destructuring should be handled in Statement()
            }
            
            // Return the value for chained assignments, but this will be converted to statement in Statement()
            return value;
        }
        
        return expr;
    }

    private Expression Pipe()
    {
        var expr = Ternary();

        while (Match(TokenType.PipeForward))
        {
            var op = Previous();
            var right = Ternary();
            expr = new PipeExpression(expr, right, op.Line, op.Column);
        }

        return expr;
    }
    
    private Expression Ternary()
    {
        var expr = NullCoalesce();
        
        if (Match(TokenType.QuestionMark))
        {
            var questionToken = Previous();
            var thenBranch = Expression();
            Consume(TokenType.Colon, "Expect ':' after ternary then branch.");
            var elseBranch = Expression();
            return new TernaryExpression(expr, thenBranch, elseBranch, questionToken.Line, questionToken.Column);
        }
        
        return expr;
    }

    /// <summary>
    /// Null-coalescing <c>??</c>: between logical OR and the ternary. Right-associative;
    /// only <c>null</c> triggers the right side (unlike <c>or</c>, which uses truthiness).
    /// </summary>
    private Expression NullCoalesce()
    {
        var expr = MatchExpression();

        if (Match(TokenType.NullCoalesce))
        {
            var op = Previous();
            var right = NullCoalesce();
            return new BinaryExpression(expr, TokenType.NullCoalesce, right, op.Line, op.Column);
        }

        return expr;
    }
    
    private Expression MatchExpression()
    {
        if (!Match(TokenType.Match))
        {
            return LogicalOr();
        }
        
        var matchToken = Previous();
        var value = Expression();
        Consume(TokenType.LeftBrace, "Expect '{' after match expression.");
        
        var cases = new List<MatchCase>();
        Statement? defaultCase = null;
        
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            if (Match(TokenType.Default))
            {
                Consume(TokenType.Colon, "Expect ':' after 'default'.");
                var defaultBody = Statement();
                defaultCase = defaultBody;
                // Allow optional semicolon after body (e.g. default: {};)
                if (Check(TokenType.Semicolon)) Advance();
                break;
            }
            
            Consume(TokenType.Case, "Expect 'case' in match expression.");
            var pattern = ParsePattern();
            Consume(TokenType.Colon, "Expect ':' after pattern.");
            var body = Statement();
            // Allow optional semicolon after body (e.g. case X: {};)
            if (Check(TokenType.Semicolon)) Advance();
            cases.Add(new MatchCase(pattern, body, pattern.Line, pattern.Column));
        }
        
        Consume(TokenType.RightBrace, "Expect '}' after match cases.");
        
        return new MatchExpression(value, cases, defaultCase, matchToken.Line, matchToken.Column);
    }
    
    private Statement ParseAssignment(Expression target, Expression value, Token equals)
    {
        var op = equals.Type;
        if (target is IdentifierExpression idExpr)
        {
            return new AssignmentStatement(new IdentifierExpression(idExpr.Name), value, op, equals.Line, equals.Column);
        }
        else if (target is MemberAccessExpression memberExpr)
        {
            return new AssignmentStatement(memberExpr, value, op, equals.Line, equals.Column);
        }
        else if (target is ArrayAccessExpression arrayExpr)
        {
            return new AssignmentStatement(arrayExpr, value, op, equals.Line, equals.Column);
        }
        
        throw Error(equals, "Invalid assignment target.");
    }
    
    private Expression LogicalOr()
    {
        var expr = LogicalAnd();
        
        while (Match(TokenType.Or))
        {
            var op = Previous();
            var right = LogicalAnd();
            expr = new BinaryExpression(expr, op.Type, right, op.Line, op.Column);
        }
        
        return expr;
    }
    
    private Expression LogicalAnd()
    {
        var expr = Equality();
        
        while (Match(TokenType.And))
        {
            var op = Previous();
            var right = Equality();
            expr = new BinaryExpression(expr, op.Type, right, op.Line, op.Column);
        }
        
        return expr;
    }
    
    private Expression Equality()
    {
        var expr = Comparison();
        
        while (Match(TokenType.Equal, TokenType.NotEqual))
        {
            var op = Previous();
            var right = Comparison();
            expr = new BinaryExpression(expr, op.Type, right, op.Line, op.Column);
        }
        
        return expr;
    }
    
    private Expression Comparison()
    {
        var expr = Additive();
        
        while (Match(TokenType.GreaterThan, TokenType.GreaterThanOrEqual, TokenType.LessThan, TokenType.LessThanOrEqual))
        {
            var op = Previous();
            var right = Additive();
            expr = new BinaryExpression(expr, op.Type, right, op.Line, op.Column);
        }
        
        return expr;
    }
    
    private Expression Additive()
    {
        var expr = Multiplicative();
        
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            var op = Previous();
            var right = Multiplicative();
            expr = new BinaryExpression(expr, op.Type, right, op.Line, op.Column);
        }
        
        return expr;
    }
    
    private Expression Multiplicative()
    {
        var expr = Unary();
        
        while (Match(TokenType.Multiply, TokenType.Divide, TokenType.Modulo))
        {
            var op = Previous();
            var right = Unary();
            expr = new BinaryExpression(expr, op.Type, right, op.Line, op.Column);
        }
        
        return expr;
    }
    
    private Expression Unary()
    {
        if (Match(TokenType.Await))
        {
            var op = Previous();
            var right = Unary();
            return new AwaitExpression(right, op.Line, op.Column);
        }
        if (Match(TokenType.Async))
        {
            var op = Previous();
            var right = Unary();
            return new AsyncExpression(right, op.Line, op.Column);
        }
        if (Match(TokenType.Not, TokenType.Minus, TokenType.Increment, TokenType.Decrement))
        {
            var op = Previous();
            var right = Unary();
            return new UnaryExpression(op.Type, right, op.Line, op.Column);
        }
        
        return Call();
    }
    
    private Expression Call()
    {
        var expr = Primary();
        
        while (true)
        {
            if (Match(TokenType.LeftParen))
            {
                expr = FinishCall(expr);
            }
            else if (Check(TokenType.QuestionMark) && Peek(1)?.Type == TokenType.LeftBracket)
            {
                // ? [ can be null-conditional index (expr?[index]) or ternary (expr ? [items] : alt).
                // Defer to Ternary when the bracketed form is followed by ':'.
                if (IsQuestionMarkArrayTernaryStart())
                    break;

                Advance();
                Match(TokenType.LeftBracket);
                var index = Expression();
                Consume(TokenType.RightBracket, "Expect ']' after index.");
                expr = new ArrayAccessExpression(expr, index, isNullConditional: true);
            }
            else if (Check(TokenType.QuestionMark) && Peek(1)?.Type == TokenType.Dot)
            {
                Advance();
                Match(TokenType.Dot);
                var name = ConsumeIdentifierOrKeyword("Expect property name after '?.'.");
                expr = new MemberAccessExpression(expr, name, isNullConditional: true);
            }
            else if (Match(TokenType.LeftBracket))
            {
                var index = Expression();
                Consume(TokenType.RightBracket, "Expect ']' after index.");
                expr = new ArrayAccessExpression(expr, index);
            }
            else if (Match(TokenType.Dot))
            {
                var name = ConsumeIdentifierOrKeyword("Expect property name after '.'.");
                expr = new MemberAccessExpression(expr, name);
            }
            else if (Match(TokenType.Increment, TokenType.Decrement))
            {
                var op = Previous();
                expr = new PostfixExpression(expr, op.Type, op.Line, op.Column);
            }
            else
            {
                break;
            }
        }
        
        return expr;
    }
    
    private bool IsLambdaParameterList()
    {
        // Check if we're looking at a lambda parameter list: (param, param) or () followed by =>
        // We haven't consumed LeftParen yet, so check what's after it
        var savedCurrent = _current;
        try
        {
            if (!Check(TokenType.LeftParen))
            {
                return false;
            }
            Advance(); // consume LeftParen
            
            if (Check(TokenType.RightParen))
            {
                // Empty parameter list: ()
                Advance(); // consume )
                if (Check(TokenType.Arrow))
                {
                    _current = savedCurrent;
                    return true;
                }
                _current = savedCurrent;
                return false;
            }
            
            // Check if it's a parameter list (identifiers separated by commas)
            while (!Check(TokenType.RightParen) && !IsAtEnd())
            {
                if (Check(TokenType.Identifier))
                {
                    Advance(); // consume identifier
                    if (Check(TokenType.Comma))
                    {
                        Advance(); // consume comma
                        continue;
                    }
                    else if (Check(TokenType.RightParen))
                    {
                        break;
                    }
                    else
                    {
                        // Not a parameter list
                        _current = savedCurrent;
                        return false;
                    }
                }
                else
                {
                    // Not a parameter list
                    _current = savedCurrent;
                    return false;
                }
            }
            
            if (Check(TokenType.RightParen))
            {
                Advance(); // consume )
                if (Check(TokenType.Arrow))
                {
                    _current = savedCurrent;
                    return true;
                }
            }
            
            _current = savedCurrent;
            return false;
        }
        catch
        {
            _current = savedCurrent;
            return false;
        }
    }
    
    private Expression LambdaExpression()
    {
        var token = Current();
        List<string> parameters = new();
        
        // Parse parameters
        if (Check(TokenType.LeftParen))
        {
            Match(TokenType.LeftParen); // consume (
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    parameters.Add(ConsumeIdentifierLike("Expect parameter name."));
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expect ')' after parameters.");
        }
        else if (Check(TokenType.Identifier))
        {
            // Single parameter without parentheses
            parameters.Add(ConsumeIdentifierLike("Expect parameter name."));
        }
        else
        {
            throw Error(Peek(), "Expect parameter list or single parameter.");
        }
        
        Consume(TokenType.Arrow, "Expect '=>' after lambda parameters.");
        
        // Parse body - expression or block
        if (Match(TokenType.LeftBrace))
        {
            var body = Block();
            return new LambdaExpression(parameters, null, body, token?.Line ?? 0, token?.Column ?? 0);
        }
        else
        {
            // Parse the expression body - this can be any expression including function calls
            var expr = Expression();
            return new LambdaExpression(parameters, expr, null, token?.Line ?? 0, token?.Column ?? 0);
        }
    }
    
    private Expression Primary()
    {
        if (Match(TokenType.False)) return new LiteralExpression(false, Previous().Line, Previous().Column);
        if (Match(TokenType.True)) return new LiteralExpression(true, Previous().Line, Previous().Column);
        if (Match(TokenType.Null)) return new LiteralExpression(null, Previous().Line, Previous().Column);
        
        if (Match(TokenType.InterpolatedString))
        {
            return ParseInterpolatedString();
        }
        
        if (Match(TokenType.Integer, TokenType.Float, TokenType.String, TokenType.Boolean))
        {
            return new LiteralExpression(Previous().Literal, Previous().Line, Previous().Column);
        }
        
        if (Match(TokenType.This)) return new ThisExpression(Previous().Line, Previous().Column);
        if (Match(TokenType.Super)) return new SuperExpression(Previous().Line, Previous().Column);
        if (Match(TokenType.Self)) return new SelfExpression(Previous().Line, Previous().Column);
        
        // Handle receive() - it's a special expression that can be called with ()
        if (Check(TokenType.Receive) && Peek(1)?.Type == TokenType.LeftParen)
        {
            var token = Peek();
            Advance(); // consume 'receive'
            Match(TokenType.LeftParen); // consume '('
            Consume(TokenType.RightParen, "Expect ')' after 'receive'.");
            return new ReceiveExpression(token.Line, token.Column);
        }
        
        // Check for lambda: single parameter without parentheses (identifier => ...)
        if (Check(TokenType.Identifier))
        {
            // Peek ahead to see if it's followed by =>
            var savedCurrent = _current;
            var identifier = Peek();
            Advance(); // consume identifier
            if (Check(TokenType.Arrow))
            {
                // It's a lambda with single parameter
                _current = savedCurrent; // reset
                return LambdaExpression();
            }
            _current = savedCurrent; // reset
        }
        
        // Allow 'print' to be used as a function call expression (e.g., in lambda: x => print(x))
        // Check if 'print' is followed by '(' - if so, treat it as an identifier for function call
        if (Check(TokenType.Print) && Peek(1)?.Type == TokenType.LeftParen)
        {
            var token = Peek();
            Advance(); // consume 'print'
            return new IdentifierExpression("print", token.Line, token.Column);
        }
        
        if (Check(TokenType.New) && IsIdentifierLikeExpressionToken(Peek(1)?.Type))
        {
            Advance();
            var token = Previous();
            var className = ConsumeIdentifierLike("Expect class name.");
            Consume(TokenType.LeftParen, "Expect '(' after class name.");
            var arguments = new List<Expression>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    arguments.Add(Expression());
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expect ')' after arguments.");
            return new NewExpression(className, arguments, token.Line, token.Column);
        }
        
        if (Check(TokenType.Spawn) && IsIdentifierLikeExpressionToken(Peek(1)?.Type))
        {
            Advance();
            var token = Previous();
            var actorName = ConsumeIdentifierLike("Expect actor name.");
            Consume(TokenType.LeftParen, "Expect '(' after actor name.");
            var arguments = new List<Expression>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    arguments.Add(Expression());
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expect ')' after arguments.");
            return new SpawnExpression(actorName, arguments, token.Line, token.Column);
        }
        
        if (Match(TokenType.LeftBracket))
        {
            var token = Previous();
            if (Check(TokenType.RightBracket))
            {
                Consume(TokenType.RightBracket, "Expect ']' after array elements.");
                return new ArrayLiteralExpression([], token.Line, token.Column);
            }

            var first = Expression();
            if (Match(TokenType.For))
            {
                var variable = ConsumeIdentifierLike("Expect variable name in list comprehension.");
                Consume(TokenType.In, "Expect 'in' after comprehension variable.");
                var iterable = Expression();
                Expression? filter = null;
                if (Match(TokenType.If))
                    filter = Expression();
                Consume(TokenType.RightBracket, "Expect ']' after list comprehension.");
                return new ListComprehensionExpression(first, variable, iterable, filter, token.Line, token.Column);
            }

            var elements = new List<Expression> { first };
            while (Match(TokenType.Comma))
                elements.Add(Expression());
            Consume(TokenType.RightBracket, "Expect ']' after array elements.");
            return new ArrayLiteralExpression(elements, token.Line, token.Column);
        }
        
        if (Check(TokenType.Dict) && Peek(1)?.Type == TokenType.LeftBrace)
        {
            Advance();
            var token = Previous();
            Consume(TokenType.LeftBrace, "Expect '{' after 'dict'.");
            var entries = new List<(Expression Key, Expression Value)>();
            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    var key = Expression();
                    Consume(TokenType.Colon, "Expect ':' after dictionary key.");
                    var value = Expression();
                    if (Match(TokenType.For))
                        return ParseDictComprehensionTail(key, value, token);
                    entries.Add((key, value));
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightBrace, "Expect '}' after dictionary entries.");
            return new DictionaryLiteralExpression(entries, token.Line, token.Column);
        }
        
        if (Check(TokenType.Graph) && (Peek(1)?.Type == TokenType.Directed || Peek(1)?.Type == TokenType.Undirected))
        {
            Advance();
            var token = Previous();
            bool isDirected = true;
            
            if (Match(TokenType.Directed))
            {
                isDirected = true;
            }
            else if (Match(TokenType.Undirected))
            {
                isDirected = false;
            }
            else
            {
                throw Error(Peek(), "Expect 'directed' or 'undirected' after 'graph'.");
            }
            
            Consume(TokenType.LeftBrace, "Expect '{' after graph type.");
            
            Expression? nodesExpression = null;
            Expression? edgesExpression = null;
            
            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    // Parse property name (must be "nodes" or "edges")
                    if (Match(TokenType.Identifier))
                    {
                        var propName = Previous().Lexeme;
                        Consume(TokenType.Colon, "Expect ':' after property name.");
                        var value = Expression();
                        
                        if (propName == "nodes")
                        {
                            nodesExpression = value;
                        }
                        else if (propName == "edges")
                        {
                            edgesExpression = value;
                        }
                        else
                        {
                            throw Error(Previous(), $"Unknown graph property '{propName}'. Expected 'nodes' or 'edges'.");
                        }
                    }
                    else
                    {
                        throw Error(Peek(), "Expect identifier as graph property name.");
                    }
                } while (Match(TokenType.Comma));
            }
            
            Consume(TokenType.RightBrace, "Expect '}' after graph properties.");
            return new GraphLiteralExpression(isDirected, nodesExpression, edgesExpression, token.Line, token.Column);
        }
        
        if (Match(TokenType.LeftBrace))
        {
            var token = Previous();
            var properties = new List<(Expression Key, Expression Value)>();
            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    var key = Expression();
                    Consume(TokenType.Colon, "Expect ':' after object key.");
                    var value = Expression();
                    if (Match(TokenType.For))
                    {
                        if (properties.Count > 0)
                            throw Error(Peek(), "Dict comprehension cannot follow other object entries.");
                        return ParseDictComprehensionTail(key, value, token);
                    }
                    properties.Add((NormalizeObjectLiteralKey(key), value));
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightBrace, "Expect '}' after object properties.");
            return new ObjectLiteralExpression(properties, token.Line, token.Column);
        }
        
        if (Check(TokenType.LeftParen))
        {
            // Check if this is a lambda: (params) => ...
            if (IsLambdaParameterList())
            {
                // Don't consume LeftParen here - LambdaExpression() will handle it
                return LambdaExpression();
            }
            // Otherwise it's a parenthesized expression
            Match(TokenType.LeftParen); // consume it
            var expr = Expression();
            Consume(TokenType.RightParen, "Expect ')' after expression.");
            return expr;
        }

        if (IsIdentifierLikeExpressionToken(Peek()?.Type))
        {
            var identifierLike = Advance();
            return new IdentifierExpression(identifierLike.Lexeme, identifierLike.Line, identifierLike.Column);
        }
        
        throw Error(Peek(), "Expect expression.");
    }
    
    private Expression ParseInterpolatedString()
    {
        var token = Previous();
        var lexerSegments = token.Literal as List<LexerInterpolatedStringSegment>;
        
        if (lexerSegments == null)
            throw Error(token, "Invalid interpolated string token.");
        
        var segments = new List<InterpolatedStringSegment>();
        
        foreach (var lexerSegment in lexerSegments)
        {
            if (lexerSegment.IsExpression)
            {
                try
                {
                    // Trim the expression content to handle any whitespace issues
                    var expressionContent = lexerSegment.Content.Trim();
                    if (string.IsNullOrEmpty(expressionContent))
                    {
                        throw Error(token, "Empty expression in interpolated string");
                    }
                    
                    // Parse the expression string
                    // Use the original string's line as a line offset so nested lexer
                    // reports the correct source line number in errors.
                    var expressionLexer = new Lexer(expressionContent, _sourceFileName, token.Line - 1);
                    var expressionTokens = expressionLexer.Tokenize();
                    
                    // Parse a single expression using the static helper
                    var expression = ParseExpression(expressionTokens);
                    segments.Add(new InterpolatedStringSegment(expression));
                }
                catch (ParseException ex)
                {
                    // Re-throw with the original token's line/column for better error reporting
                    var message = ex.Message;
                    var colonIndex = message.LastIndexOf(": ");
                    if (colonIndex >= 0 && colonIndex < message.Length - 2)
                    {
                        message = message.Substring(colonIndex + 2);
                    }
                    throw Error(token, $"Invalid expression in interpolated string: {message}");
                }
            }
            else
            {
                // Text segment
                segments.Add(new InterpolatedStringSegment(lexerSegment.Content));
            }
        }
        
        return new InterpolatedStringExpression(segments, token.Line, token.Column);
    }
    
    private Expression FinishCall(Expression callee)
    {
        var arguments = new List<Expression>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                arguments.Add(Expression());
            } while (Match(TokenType.Comma));
        }
        
        var paren = Consume(TokenType.RightParen, "Expect ')' after arguments.");
        return new FunctionCallExpression(callee, arguments, paren.Line, paren.Column);
    }
    
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
        var error = Error(Peek(), message);
        throw error;
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
    
    private ParseException Error(Token token, string message, string? diagnosticCode = null)
    {
        return new ParseException(token.Line, token.Column, message, _sourceFileName, diagnosticCode);
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

    private DictComprehensionExpression ParseDictComprehensionTail(Expression key, Expression value, Token startToken)
    {
        var variable = ConsumeIdentifierLike("Expect variable name in dict comprehension.");
        Consume(TokenType.In, "Expect 'in' after comprehension variable.");
        var iterable = Expression();
        Expression? filter = null;
        if (Match(TokenType.If))
            filter = Expression();
        Consume(TokenType.RightBrace, "Expect '}' after dict comprehension.");
        return new DictComprehensionExpression(key, value, variable, iterable, filter, startToken.Line, startToken.Column);
    }

    private static Expression NormalizeObjectLiteralKey(Expression key)
    {
        if (key is IdentifierExpression id)
            return new LiteralExpression(id.Name, id.Line, id.Column);
        if (key is LiteralExpression)
            return key;
        throw new ParseException(key.Line, key.Column,
            "Object literal keys must be string literals or identifiers.");
    }
}

public class ParseException : Exception
{
    public int Line { get; }
    public int Column { get; }
    public string? SourceFileName { get; }
    public string Details { get; }
    public TokenType? ExpectedType { get; }
    public TokenType? ActualType { get; }
    public bool IsSyntheticInsertion { get; }
    /// <summary>Optional diagnostic code (e.g., WF1003, WF1004 for workflow diagnostics).</summary>
    public string? DiagnosticCode { get; }
    
    public ParseException(string message, string? diagnosticCode = null) : base(message) 
    {
        Line = 0;
        Column = 0;
        SourceFileName = null;
        Details = message;
        ExpectedType = null;
        ActualType = null;
        IsSyntheticInsertion = false;
        DiagnosticCode = diagnosticCode;
    }
    
    public ParseException(int line, int column, string message, string? sourceFileName = null, string? diagnosticCode = null) : base(BuildMessage(line, column, message, sourceFileName))
    {
        Line = line;
        Column = column;
        SourceFileName = sourceFileName;
        Details = message;
        ExpectedType = null;
        ActualType = null;
        IsSyntheticInsertion = false;
        DiagnosticCode = diagnosticCode;
    }
    
    public ParseException(int line, int column, TokenType expectedType, TokenType actualType, bool isSyntheticInsertion = false, string? sourceFileName = null, string? diagnosticCode = null) 
        : base(BuildMessage(line, column,
            isSyntheticInsertion
                ? $"Missing '{expectedType}' - inserted automatically"
                : $"Expected '{expectedType}' but found '{actualType}'",
            sourceFileName))
    {
        Line = line;
        Column = column;
        SourceFileName = sourceFileName;
        Details = isSyntheticInsertion
            ? $"Missing '{expectedType}' - inserted automatically"
            : $"Expected '{expectedType}' but found '{actualType}'";
        ExpectedType = expectedType;
        ActualType = actualType;
        IsSyntheticInsertion = isSyntheticInsertion;
        DiagnosticCode = diagnosticCode;
    }

    private static string BuildMessage(int line, int column, string details, string? sourceFileName)
    {
        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            return $"Parse error in {sourceFileName} at line {line}, column {column}: {details}";
        }

        return $"Parse error at line {line}, column {column}: {details}";
    }
}
