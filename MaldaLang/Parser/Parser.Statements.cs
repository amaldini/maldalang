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
    private Statement Statement()
    {
        if (Match(TokenType.If)) return IfStatement();
        if (Match(TokenType.While)) return WhileStatement();
        if (Match(TokenType.Foreach)) return ForeachStatement();
        if (Match(TokenType.For))
        {
            if (Match(TokenType.Await))
                return ForAwaitStatement();
            return ForStatement();
        }
        if (CheckLexeme("within") && Peek(1)?.Type == TokenType.LeftParen)
        {
            Advance();
            return ParseWithinStatement();
        }
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
        var thenBranch = EmbeddedStatement();
        Statement? elseBranch = null;
        if (Match(TokenType.Else))
        {
            elseBranch = EmbeddedStatement();
        }
        return new IfStatement(condition, thenBranch, elseBranch, token.Line, token.Column);
    }
    
    private Statement WhileStatement()
    {
        var token = Previous();
        Consume(TokenType.LeftParen, "Expect '(' after 'while'.");
        var condition = Expression();
        Consume(TokenType.RightParen, "Expect ')' after condition.");
        var body = EmbeddedStatement();
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
        var body = EmbeddedStatement();
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
                var forInBody = EmbeddedStatement();
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
            
            var loopBody = EmbeddedStatement();
            
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
            loopBody = new WhileStatement(loopCondition, loopBody) { IsDesugaredFor = loopIncrement != null };
            
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
        
        var body = EmbeddedStatement();
        
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
        body = new WhileStatement(condition, body) { IsDesugaredFor = incrementStmt != null };
        
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
                AppendFlattened(statements, stmt, applySourceFile: false);
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
        var nameToken = ConsumeIdentifierTokenLike("Expect variable name.");
        var name = nameToken.Lexeme;
        typeHint = null;
        if (Match(TokenType.Colon))
            typeHint = Consume(TokenType.Identifier, "Expect type name after ':'.").Lexeme;

        if (Match(TokenType.Assign))
        {
            initializer = Expression();
            Consume(TokenType.Semicolon, "Expect ';' after variable declaration.");
            return new VarDeclStatement(name, initializer, typeHint, isExported, isConst, token?.Line ?? 0, token?.Column ?? 0);
        }

        if (!isConst)
            throw Error(Peek(), "Expect '=' after variable name.");

        if (Match(TokenType.Comma))
        {
            if (typeHint != null)
                throw Error(Previous(), "Type hints are only allowed on a single name-as-string const (const NAME: string;).");

            var decls = new List<Statement>
            {
                NameAsStringConst(name, nameToken, typeHint, isExported, token)
            };

            do
            {
                if (Check(TokenType.Assign))
                    throw NameAsStringListInitializerError();

                var nextNameToken = ConsumeIdentifierTokenLike("Expect constant name after ','.");
                if (Match(TokenType.Colon))
                    throw Error(Previous(), "Type hints are only allowed on a single name-as-string const (const NAME: string;).");
                if (Check(TokenType.Assign))
                    throw NameAsStringListInitializerError();

                decls.Add(NameAsStringConst(nextNameToken.Lexeme, nextNameToken, null, isExported, token));
            } while (Match(TokenType.Comma));

            if (Check(TokenType.Assign))
                throw NameAsStringListInitializerError();

            Consume(TokenType.Semicolon, "Expect ';' after constant declaration.");
            return decls.Count == 1
                ? decls[0]
                : new StatementSequence(decls, token?.Line ?? 0, token?.Column ?? 0);
        }

        Consume(TokenType.Semicolon, "Expect '=' after variable name.");
        return NameAsStringConst(name, nameToken, typeHint, isExported, token);
    }

    private ParseException NameAsStringListInitializerError()
    {
        return Error(Peek(),
            "Name-as-string const lists cannot include initializers. Write 'const BUY;' and 'const MAX = 3;' separately, or 'const BUY, MAX;'.");
    }

    private static VarDeclStatement NameAsStringConst(
        string name,
        Token nameToken,
        string? typeHint,
        bool isExported,
        Token? keyword)
    {
        var initializer = new LiteralExpression(name, nameToken.Line, nameToken.Column);
        return new VarDeclStatement(
            name,
            initializer,
            typeHint,
            isExported,
            isConst: true,
            keyword?.Line ?? nameToken.Line,
            keyword?.Column ?? nameToken.Column);
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

    private WithinStatement ParseWithinStatement()
    {
        var start = Previous();
        Consume(TokenType.LeftParen, "Expect '(' after within.");
        var amountTok = Consume(TokenType.Integer, "Expect duration amount.");
        var amount = Convert.ToInt32(amountTok.Literal);
        var unit = "ms";
        if (Check(TokenType.Identifier))
            unit = Advance().Lexeme;
        Consume(TokenType.RightParen, "Expect ')' after duration.");
        var ms = unit switch
        {
            "s" => amount * 1000,
            "m" => amount * 60_000,
            _ => amount
        };

        int? tokens = null, tools = null;
        double? cost = null;
        if (MatchLexeme("budget"))
        {
            Consume(TokenType.LeftParen, "Expect '(' after budget.");
            while (!Check(TokenType.RightParen) && !IsAtEnd())
            {
                var key = ConsumeIdentifierLike("Expect budget key.");
                Consume(TokenType.Colon, "Expect ':' after budget key.");
                var num = Current();
                Advance();
                if (string.Equals(key, "tokens", StringComparison.Ordinal))
                    tokens = Convert.ToInt32(num?.Literal ?? 0);
                else if (string.Equals(key, "tools", StringComparison.Ordinal))
                    tools = Convert.ToInt32(num?.Literal ?? 0);
                else if (string.Equals(key, "cost", StringComparison.Ordinal))
                    cost = Convert.ToDouble(num?.Literal ?? 0);
                Match(TokenType.Comma);
            }

            Consume(TokenType.RightParen, "Expect ')' after budget args.");
        }

        Consume(TokenType.LeftBrace, "Expect '{' after within.");
        var body = Block();
        return new WithinStatement(ms, tokens, tools, cost, body, start.Line, start.Column);
    }

    private ForInStatement ForAwaitStatement()
    {
        var start = Previous();
        Consume(TokenType.LeftParen, "Expect '(' after for await.");
        Consume(TokenType.Var, "Expect 'var' in for await.");
        var name = ConsumeIdentifierLike("Expect variable name.");
        Consume(TokenType.In, "Expect 'in' after for-await variable.");
        var collection = Expression();
        Consume(TokenType.RightParen, "Expect ')' after collection.");
        var body = EmbeddedStatement();
        return new ForInStatement(name, collection, body, start.Line, start.Column, isAwait: true);
    }
}
