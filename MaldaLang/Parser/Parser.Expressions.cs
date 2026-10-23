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
                var defaultBody = EmbeddedStatement();
                defaultCase = defaultBody;
                // Allow optional semicolon after body (e.g. default: {};)
                if (Check(TokenType.Semicolon)) Advance();
                break;
            }
            
            Consume(TokenType.Case, "Expect 'case' in match expression.");
            var pattern = ParsePattern();
            Expression? guard = null;
            if (Match(TokenType.If))
                guard = Expression();
            Consume(TokenType.Colon, "Expect ':' after pattern.");
            var body = EmbeddedStatement();
            // Allow optional semicolon after body (e.g. case X: {};)
            if (Check(TokenType.Semicolon)) Advance();
            cases.Add(new MatchCase(pattern, body, pattern.Line, pattern.Column, guard));
        }
        
        Consume(TokenType.RightBrace, "Expect '}' after match cases.");
        
        return new MatchExpression(value, cases, defaultCase, matchToken.Line, matchToken.Column);
    }
    
    private Statement ParseAssignment(Expression target, Expression value, Token equals)
    {
        var op = equals.Type;
        if (target is IdentifierExpression idExpr)
        {
            return new AssignmentStatement(new IdentifierExpression(idExpr.Name, idExpr.Line, idExpr.Column), value, op, equals.Line, equals.Column);
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

                var question = Advance();
                Match(TokenType.LeftBracket);
                var index = Expression();
                Consume(TokenType.RightBracket, "Expect ']' after index.");
                expr = new ArrayAccessExpression(expr, index, isNullConditional: true, question.Line, question.Column);
            }
            else if (Check(TokenType.QuestionMark) && Peek(1)?.Type == TokenType.Dot)
            {
                Advance();
                Match(TokenType.Dot);
                var name = ConsumeIdentifierOrKeyword("Expect property name after '?.'.");
                var nameToken = Previous();
                expr = new MemberAccessExpression(expr, name, isNullConditional: true, nameToken.Line, nameToken.Column);
            }
            else if (Match(TokenType.LeftBracket))
            {
                var bracket = Previous();
                var index = Expression();
                Consume(TokenType.RightBracket, "Expect ']' after index.");
                expr = new ArrayAccessExpression(expr, index, isNullConditional: false, bracket.Line, bracket.Column);
            }
            else if (Match(TokenType.Dot))
            {
                var name = ConsumeIdentifierOrKeyword("Expect property name after '.'.");
                // Position of the member name: diagnostics underline it (`io.ensureDir`) and
                // runtime errors on `o.missing.x` point at the property that failed.
                var nameToken = Previous();
                expr = new MemberAccessExpression(expr, name, isNullConditional: false, nameToken.Line, nameToken.Column);
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
                    if (parameters.Count > 0 && Check(TokenType.RightParen))
                        break; // trailing comma
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
    
    /// <summary>
    /// Comma-separated call arguments up to (not including) <c>)</c>; one trailing comma is allowed.
    /// </summary>
    private void ParseArgumentList(List<Expression> arguments)
    {
        if (Check(TokenType.RightParen))
            return;
        do
        {
            if (arguments.Count > 0 && Check(TokenType.RightParen))
                break; // trailing comma
            arguments.Add(Expression());
        } while (Match(TokenType.Comma));
    }

    /// <summary>
    /// <c>function (a, b) { … }</c> as an expression. Produces the same node as the
    /// block-bodied lambda <c>(a, b) =&gt; { … }</c>, so every backend handles it unchanged.
    /// Optional <c>: Type</c> hints on parameters are accepted and ignored.
    /// </summary>
    private Expression AnonymousFunctionExpression()
    {
        var token = Consume(TokenType.Function, "Expect 'function'.");
        Consume(TokenType.LeftParen, "Expect '(' after 'function'.");
        var parameters = new List<string>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                if (parameters.Count > 0 && Check(TokenType.RightParen))
                    break; // trailing comma
                parameters.Add(ConsumeIdentifierLike("Expect parameter name."));
                if (Match(TokenType.Colon))
                    ParseTypeHintName();
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        Consume(TokenType.LeftBrace, "Expect '{' before anonymous function body.");
        var body = Block();
        return new LambdaExpression(parameters, null, body, token.Line, token.Column);
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
            // `new THREE.Vector3(…)`: a constructor reached through a module or object
            // (JavaScript interop). ClassName keeps the dotted path.
            while (Match(TokenType.Dot))
                className += "." + ConsumeIdentifierLike("Expect a name after '.' in 'new'.");
            Consume(TokenType.LeftParen, "Expect '(' after class name.");
            var arguments = new List<Expression>();
            ParseArgumentList(arguments);
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
            ParseArgumentList(arguments);
            Consume(TokenType.RightParen, "Expect ')' after arguments.");
            return new SpawnExpression(actorName, arguments, token.Line, token.Column);
        }
        
        if (Check(TokenType.Function) && Peek(1)?.Type == TokenType.LeftParen)
            return AnonymousFunctionExpression();

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
            while (Match(TokenType.Comma) && !Check(TokenType.RightBracket))
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
                    if (entries.Count > 0 && Check(TokenType.RightBrace))
                        break; // trailing comma
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
                var sawProperty = false;
                do
                {
                    if (sawProperty && Check(TokenType.RightBrace))
                        break; // trailing comma
                    sawProperty = true;
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
                    if (properties.Count > 0 && Check(TokenType.RightBrace))
                        break; // trailing comma
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
                    var segmentLine = lexerSegment.SourceLine > 0 ? lexerSegment.SourceLine : token.Line;
                    var segmentColumn = lexerSegment.SourceLine > 0 ? lexerSegment.SourceColumn : token.Column;
                    var expressionLexer = new Lexer(expressionContent, _sourceFileName, segmentLine - 1);
                    var expressionTokens = RebaseTokens(expressionLexer.Tokenize(), segmentLine, segmentColumn);
                    
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
    
    /// <summary>
    /// Tokens lexed from an interpolation hole start at 1:1; move them to where the hole sits in
    /// the file so nodes inside <c>$"{…}"</c> report real positions.
    /// </summary>
    private static List<Token> RebaseTokens(List<Token> tokens, int line, int column) =>
        tokens.Select(t => new Token(
                t.Type,
                t.Lexeme,
                t.Literal,
                line + t.Line - 1,
                t.Line == 1 ? column + t.Column - 1 : t.Column))
            .ToList();

    private Expression FinishCall(Expression callee)
    {
        var arguments = new List<Expression>();
        ParseArgumentList(arguments);

        var paren = Consume(TokenType.RightParen, "Expect ')' after arguments.");
        return new FunctionCallExpression(callee, arguments, paren.Line, paren.Column);
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
