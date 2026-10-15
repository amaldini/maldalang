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
    private Statement? Declaration()
    {
        try
        {
            RejectRemovedFunctionAlias();

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
                RejectRemovedFunctionAlias();
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
                    RejectRemovedFunctionAlias();
                    throw Error(token, "Decorators must be followed by 'function', 'property', or 'prompt' keyword");
                }
                // Parse function declaration with the decorators we already parsed
                return FunctionDeclarationWithDecorators(decorators, false);
            }
            
            if (Match(TokenType.Function))
                return FunctionDeclaration();
            if (MatchLexeme("suite"))
                return ParseSuiteDeclaration();
            if (MatchLexeme("context"))
                return ParseContextDeclaration();
            if (MatchLexeme("policy"))
                return ParsePolicyDeclaration();
            return Statement();
        }
        catch (ParseException ex)
        {
            _errors.Add(ex);
            Synchronize();
            return null;
        }
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

    /// <summary>
    /// Type hints are a single identifier (<c>int</c>, <c>vec3</c>). Shader parameters
    /// may prefix GLSL <c>out</c>: <c>tHit: out float</c>.
    /// </summary>
    private string ParseTypeHintName()
    {
        var first = Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme;
        if (string.Equals(first, "out", StringComparison.Ordinal) && Check(TokenType.Identifier))
            return "out " + Advance().Lexeme;
        return first;
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
                // Optional type hint: : Type  (shader params may use `out float`)
                if (Match(TokenType.Colon))
                    parameterTypeHints.Add(ParseTypeHintName());
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
        var body = ParseFunctionOrMethodBody("function");
        return new FunctionDeclaration(name, parameters, body, decorators, parameterDecorators, parameterTypeHints, returnType, isExported, nameToken.Line, nameToken.Column);
    }

    /// <summary>
    /// Shared body for top-level functions and methods: <c>{ … }</c> or a single expression + <c>;</c>
    /// (desugars to <c>return expr;</c>). Constructors stay block-only.
    /// </summary>
    private BlockStatement ParseFunctionOrMethodBody(string kind)
    {
        if (Match(TokenType.LeftBrace))
            return Block();

        var expr = Expression();
        var semi = Consume(TokenType.Semicolon, $"Expect ';' after single-expression {kind} body.");
        return new BlockStatement(new List<Statement> { new ReturnStatement(expr, semi.Line, semi.Column) });
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
            RejectRemovedFunctionAlias();
            Consume(TokenType.Function, "Expect 'function' method signature in api body.");
            var methodName = ConsumeIdentifierLike("Expect method name in api.");
            Consume(TokenType.LeftParen, "Expect '(' after api method name.");
            var paramNames = new List<string>();
            var paramTypes = new List<string?>();
            var paramRequired = new List<bool>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    paramNames.Add(ConsumeIdentifierOrKeyword("Expect parameter name in api method."));
                    if (Match(TokenType.Colon))
                    {
                        var payloadType = ParseSchemaType(
                            "Expect api parameter type after ':'.",
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
            Consume(TokenType.RightParen, "Expect ')' after api method parameters.");
            Consume(TokenType.Semicolon, "Expect ';' after api method signature (bodies are separate top-level functions).");
            methods.Add(new ApiMethodSignature(methodName, paramNames, paramTypes, paramRequired));
        }
        Consume(TokenType.RightBrace, "Expect '}' after api methods.");
        return new ApiDeclaration(apiName, methods, apiToken.Line, apiToken.Column);
    }

    /// <summary>
    /// Schema field / variant-constructor / api-method payload type: Identifier, optional <c>[]</c>, optional <c>?</c>.
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

    private SuiteDeclaration ParseSuiteDeclaration()
    {
        var start = Previous();
        var titleTok = Consume(TokenType.String, "Expect suite title string.");
        var title = titleTok.Literal as string ?? titleTok.Lexeme.Trim('"');
        Consume(TokenType.LeftBrace, "Expect '{' after suite title.");
        var cases = new List<EvalCaseDeclaration>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            var decorators = Check(TokenType.At) ? ParseDecorators() : new List<Decorator>();
            Consume(TokenType.Case, "Expect 'case' in suite.");
            var caseTitleTok = Consume(TokenType.String, "Expect case title string.");
            var caseTitle = caseTitleTok.Literal as string ?? caseTitleTok.Lexeme.Trim('"');
            Consume(TokenType.LeftBrace, "Expect '{' after case title.");
            var body = Block();
            cases.Add(new EvalCaseDeclaration(caseTitle, body, decorators, caseTitleTok.Line, caseTitleTok.Column));
        }

        Consume(TokenType.RightBrace, "Expect '}' after suite.");
        return new SuiteDeclaration(title, cases, start.Line, start.Column);
    }

    private ContextDeclaration ParseContextDeclaration()
    {
        var start = Previous();
        var name = ConsumeIdentifierLike("Expect context name.");
        Consume(TokenType.LeftBrace, "Expect '{' after context name.");
        int? budget = null;
        var pin = new List<string>();
        int? retain = null;
        var evict = "oldest";
        string? compact = null;
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            var field = ConsumeIdentifierLike("Expect context field.");
            Consume(TokenType.Colon, "Expect ':' after context field.");
            if (string.Equals(field, "budget", StringComparison.Ordinal))
            {
                var n = Consume(TokenType.Integer, "Expect budget token count.");
                budget = Convert.ToInt32(n.Literal);
                MatchLexeme("tokens");
            }
            else if (string.Equals(field, "pin", StringComparison.Ordinal))
            {
                pin.Add(ConsumeIdentifierLike("Expect pin name."));
                while (Match(TokenType.Comma))
                    pin.Add(ConsumeIdentifierLike("Expect pin name."));
            }
            else if (string.Equals(field, "retain", StringComparison.Ordinal))
            {
                MatchLexeme("last");
                var n = Consume(TokenType.Integer, "Expect retain count.");
                retain = Convert.ToInt32(n.Literal);
            }
            else if (string.Equals(field, "evict", StringComparison.Ordinal))
            {
                evict = ConsumeIdentifierLike("Expect evict strategy.");
            }
            else if (string.Equals(field, "compact", StringComparison.Ordinal))
            {
                Match(TokenType.Prompt);
                compact = ConsumeIdentifierLike("Expect compact prompt name.");
            }
            else
            {
                while (!Check(TokenType.Semicolon) && !Check(TokenType.RightBrace) && !IsAtEnd())
                    Advance();
            }

            Match(TokenType.Semicolon);
        }

        Consume(TokenType.RightBrace, "Expect '}' after context body.");
        return new ContextDeclaration(name, budget, pin, retain, evict, compact, start.Line, start.Column);
    }

    private PolicyDeclaration ParsePolicyDeclaration()
    {
        var start = Previous();
        Consume(TokenType.LeftBrace, "Expect '{' after policy.");
        var rules = new List<PolicyRule>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            var domain = ConsumeIdentifierLike("Expect policy domain.");
            Consume(TokenType.Colon, "Expect ':' after policy domain.");
            var action = ConsumeIdentifierLike("Expect policy action.");
            var args = new List<string>();
            MatchLexeme("under");
            while (!Check(TokenType.Semicolon) && !Check(TokenType.RightBrace) && !IsAtEnd())
            {
                if (Check(TokenType.String))
                    args.Add((Consume(TokenType.String, "Expect policy argument.").Literal as string) ?? "");
                else if (Check(TokenType.Identifier))
                    args.Add(Advance().Lexeme);
                else
                    Advance();
                Match(TokenType.Comma);
            }

            Match(TokenType.Semicolon);
            rules.Add(new PolicyRule(domain, action, args));
        }

        Consume(TokenType.RightBrace, "Expect '}' after policy.");
        return new PolicyDeclaration(rules, start.Line, start.Column);
    }
}
