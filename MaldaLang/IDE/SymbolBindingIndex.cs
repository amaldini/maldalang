// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.IDE.Services;

/// <summary>
/// Maps identifier occurrences to the declaration they bind to so rename /
/// references stay inside one lexical scope instead of rewriting every
/// same-name token in the file.
/// </summary>
internal sealed class SymbolBindingIndex
{
    private readonly List<BoundOccurrence> _occurrences = new();
    private readonly List<Token> _tokens;
    private readonly Scope _root = new(parent: null);
    private int _nextBindingId;

    private SymbolBindingIndex(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public static SymbolBindingIndex Build(List<Statement> statements, List<Token> tokens)
    {
        var index = new SymbolBindingIndex(tokens);
        index.HoistTopLevel(statements);
        foreach (var statement in statements)
        {
            index.VisitStatement(statement, index._root);
        }

        return index;
    }

    public IReadOnlyList<BoundOccurrence> FindRelated(int line, int column)
    {
        var origin = FindOccurrenceAt(line, column);
        if (origin == null)
        {
            return Array.Empty<BoundOccurrence>();
        }

        return _occurrences
            .Where(occurrence => occurrence.BindingId == origin.BindingId)
            .OrderBy(occurrence => occurrence.Span.Line)
            .ThenBy(occurrence => occurrence.Span.Column)
            .ToList();
    }

    public BoundOccurrence? FindOccurrenceAt(int line, int column)
    {
        return _occurrences.FirstOrDefault(occurrence =>
            occurrence.Span.Line == line &&
            occurrence.Span.Column <= column &&
            occurrence.Span.Column + occurrence.Span.Length >= column);
    }

    public bool IsWorkspaceVisible(int line, int column)
    {
        return FindOccurrenceAt(line, column)?.Kind == BindingKind.Global;
    }

    public IReadOnlyList<BoundOccurrence> FindWorkspaceVisible(string name)
    {
        return _occurrences
            .Where(occurrence => occurrence.Kind == BindingKind.Global && occurrence.Name == name)
            .ToList();
    }

    internal static IEnumerable<Token> EnumerateIdentifierTokens(IEnumerable<Token> tokens)
    {
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.Identifier)
            {
                yield return token;
            }
            else if (token.Type == TokenType.InterpolatedString)
            {
                foreach (var nested in EnumerateInterpolatedStringIdentifiers(token))
                {
                    yield return nested;
                }
            }
        }
    }

    internal static IEnumerable<Token> EnumerateInterpolatedStringIdentifiers(Token interpolated)
    {
        if (interpolated.Literal is not List<LexerInterpolatedStringSegment> segments)
        {
            yield break;
        }

        foreach (var segment in segments)
        {
            if (!segment.IsExpression || string.IsNullOrWhiteSpace(segment.Content))
            {
                continue;
            }

            List<Token> nestedTokens;
            try
            {
                nestedTokens = new Lexer(segment.Content).Tokenize();
            }
            catch
            {
                continue;
            }

            foreach (var ident in EnumerateIdentifierTokens(nestedTokens))
            {
                var line = segment.SourceLine + ident.Line - 1;
                var column = ident.Line == 1
                    ? segment.SourceColumn + ident.Column - 1
                    : ident.Column;
                yield return new Token(TokenType.Identifier, ident.Lexeme, ident.Literal, line, column);
            }
        }
    }

    private void HoistTopLevel(List<Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclaration functionDeclaration:
                    Declare(_root, functionDeclaration.Name, BindingKind.Global);
                    break;
                case ClassDeclaration classDeclaration:
                    Declare(_root, classDeclaration.Name, BindingKind.Global);
                    break;
                case ActorDeclaration actorDeclaration:
                    Declare(_root, actorDeclaration.Name, BindingKind.Global);
                    break;
                case PromptDeclaration promptDeclaration:
                    Declare(_root, promptDeclaration.Name, BindingKind.Global);
                    break;
                case WorkflowDeclaration workflowDeclaration:
                    Declare(_root, workflowDeclaration.Name, BindingKind.Global);
                    break;
                case SchemaDeclaration schemaDeclaration:
                    Declare(_root, schemaDeclaration.Name, BindingKind.Global);
                    break;
                case TypeDeclaration typeDeclaration:
                    Declare(_root, typeDeclaration.TypeName, BindingKind.Global);
                    break;
                case PropertyDeclaration propertyDeclaration:
                    Declare(_root, propertyDeclaration.Name, BindingKind.Global);
                    break;
            }
        }
    }

    private void VisitStatement(Statement? statement, Scope scope)
    {
        if (statement == null)
        {
            return;
        }

        switch (statement)
        {
            case FunctionDeclaration functionDeclaration:
                VisitFunction(functionDeclaration, scope, isMethod: false);
                break;
            case ClassDeclaration classDeclaration:
                VisitClassLike(classDeclaration.Name, classDeclaration.Line, classDeclaration.Column, classDeclaration.Members, classDeclaration.Superclass, scope);
                break;
            case ActorDeclaration actorDeclaration:
                VisitClassLike(actorDeclaration.Name, actorDeclaration.Line, actorDeclaration.Column, actorDeclaration.Members, superclass: null, scope);
                break;
            case PromptDeclaration promptDeclaration:
                VisitPrompt(promptDeclaration, scope);
                break;
            case WorkflowDeclaration workflowDeclaration:
                VisitWorkflow(workflowDeclaration, scope);
                break;
            case SchemaDeclaration schemaDeclaration:
                RecordDeclaration(schemaDeclaration.Name, schemaDeclaration.Line, schemaDeclaration.Column, Declare(scope, schemaDeclaration.Name, BindingKind.Global));
                break;
            case TypeDeclaration typeDeclaration:
                RecordDeclaration(typeDeclaration.TypeName, typeDeclaration.Line, typeDeclaration.Column, Declare(scope, typeDeclaration.TypeName, BindingKind.Global));
                break;
            case PropertyDeclaration propertyDeclaration:
                VisitFunctionLike(
                    propertyDeclaration.Name,
                    propertyDeclaration.Parameters,
                    propertyDeclaration.Body,
                    propertyDeclaration.Decorators,
                    propertyDeclaration.Line,
                    propertyDeclaration.Column,
                    scope,
                    isMethod: false);
                break;
            case VarDeclStatement variableDeclaration:
                VisitExpression(variableDeclaration.Initializer, scope);
                RecordTypeName(variableDeclaration.TypeHint, variableDeclaration.Line, variableDeclaration.Column, scope);
                RecordDeclaration(
                    variableDeclaration.Name,
                    variableDeclaration.Line,
                    variableDeclaration.Column,
                    Declare(scope, variableDeclaration.Name, ScopeKind(scope)));
                break;
            case DestructuringVarDecl destructuring:
                VisitExpression(destructuring.Initializer, scope);
                RecordTypeName(destructuring.TypeHint, destructuring.Line, destructuring.Column, scope);
                DeclarePattern(destructuring.Pattern, scope);
                break;
            case BlockStatement block:
                var blockScope = new Scope(scope);
                foreach (var inner in block.Statements)
                {
                    VisitStatement(inner, blockScope);
                }
                break;
            case IfStatement ifStatement:
                VisitExpression(ifStatement.Condition, scope);
                VisitStatement(ifStatement.ThenBranch, scope);
                VisitStatement(ifStatement.ElseBranch, scope);
                break;
            case WhileStatement whileStatement:
                VisitExpression(whileStatement.Condition, scope);
                VisitStatement(whileStatement.Body, scope);
                break;
            case ForStatement forStatement:
                var forScope = new Scope(scope);
                VisitStatement(forStatement.Initializer, forScope);
                VisitExpression(forStatement.Condition, forScope);
                VisitExpression(forStatement.Increment, forScope);
                VisitStatement(forStatement.Body, forScope);
                break;
            case ForInStatement forIn:
                VisitExpression(forIn.Collection, scope);
                var forInScope = new Scope(scope);
                RecordDeclaration(forIn.VariableName, forIn.Line, forIn.Column, Declare(forInScope, forIn.VariableName, BindingKind.Local));
                VisitStatement(forIn.Body, forInScope);
                break;
            case TryStatement tryStatement:
                VisitStatement(tryStatement.TryBlock, scope);
                foreach (var catchClause in tryStatement.CatchClauses)
                {
                    var catchScope = new Scope(scope);
                    if (!string.IsNullOrEmpty(catchClause.ExceptionVariable))
                    {
                        RecordDeclaration(
                            catchClause.ExceptionVariable,
                            catchClause.Line,
                            catchClause.Column,
                            Declare(catchScope, catchClause.ExceptionVariable, BindingKind.Local));
                    }

                    VisitExpression(catchClause.Filter, catchScope);
                    VisitStatement(catchClause.Body, catchScope);
                }

                VisitStatement(tryStatement.FinallyBlock, scope);
                break;
            case UsingResourceStatement usingResource:
                VisitExpression(usingResource.Initializer, scope);
                var usingScope = new Scope(scope);
                RecordDeclaration(
                    usingResource.VariableName,
                    usingResource.Line,
                    usingResource.Column,
                    Declare(usingScope, usingResource.VariableName, BindingKind.Local));
                VisitStatement(usingResource.Body, usingScope);
                break;
            case AssignmentStatement assignment:
                VisitExpression(assignment.Target, scope);
                VisitExpression(assignment.Value, scope);
                break;
            case DestructuringAssignment destructuringAssignment:
                VisitExpression(destructuringAssignment.Value, scope);
                RecordPatternUses(destructuringAssignment.Pattern, scope);
                break;
            case ExpressionStatement expressionStatement:
                VisitExpression(expressionStatement.Expression, scope);
                break;
            case ReturnStatement returnStatement:
                VisitExpression(returnStatement.Value, scope);
                break;
            case PrintStatement printStatement:
                VisitExpression(printStatement.Expression, scope);
                break;
            case ThrowStatement throwStatement:
                VisitExpression(throwStatement.Exception, scope);
                break;
            case DeferStatement deferStatement:
                VisitStatement(deferStatement.Body, scope);
                break;
            case WithinStatement withinStatement:
                VisitStatement(withinStatement.Body, scope);
                break;
            case PromptBodyStatement promptBody:
                VisitExpression(promptBody.Expression, scope);
                break;
            case WorkflowStepStatement step:
                RecordDeclaration(step.StepId, step.Line, step.Column, Declare(scope, step.StepId, BindingKind.Local));
                VisitExpression(step.CallExpression, scope);
                break;
            case WorkflowApprovalStatement approval:
                RecordDeclaration(approval.ApprovalId, approval.Line, approval.Column, Declare(scope, approval.ApprovalId, BindingKind.Local));
                VisitExpression(approval.ApprovalNameExpr, scope);
                VisitExpression(approval.PayloadExpr, scope);
                VisitExpression(approval.OnReject, scope);
                break;
            case WorkflowAwaitSignalStatement wait:
                RecordDeclaration(wait.SignalId, wait.Line, wait.Column, Declare(scope, wait.SignalId, BindingKind.Local));
                VisitExpression(wait.SignalNameExpr, scope);
                VisitExpression(wait.PayloadExpr, scope);
                break;
            case SendStatement send:
                VisitExpression(send.Target, scope);
                foreach (var argument in send.Arguments)
                {
                    VisitExpression(argument, scope);
                }

                VisitExpression(send.TimeoutMilliseconds, scope);
                VisitCallback(send.Callback, scope);
                VisitCallback(send.TimeoutErrorHandler, scope);
                break;
            case EvalCaseDeclaration evalCase:
                VisitDecorators(evalCase.Decorators, scope);
                VisitStatement(evalCase.Body, scope);
                break;
            case SuiteDeclaration suite:
                foreach (var evalCase in suite.Cases)
                {
                    VisitStatement(evalCase, scope);
                }
                break;
        }
    }

    private void VisitFunction(FunctionDeclaration function, Scope scope, bool isMethod)
    {
        VisitFunctionLike(
            function.Name,
            function.Parameters,
            function.Body,
            function.Decorators,
            function.Line,
            function.Column,
            scope,
            isMethod,
            function.ParameterTypeHints,
            function.ReturnType);
    }

    private void VisitFunctionLike(
        string name,
        List<string> parameters,
        BlockStatement body,
        List<Decorator> decorators,
        int line,
        int column,
        Scope scope,
        bool isMethod,
        List<string?>? parameterTypeHints = null,
        string? returnType = null)
    {
        var kind = isMethod ? BindingKind.Member : ScopeKind(scope);
        var binding = Declare(scope, name, kind);
        RecordDeclaration(name, line, column, binding);
        VisitDecorators(decorators, scope);

        var functionScope = new Scope(scope);
        RecordParameters(name, parameters, line, column, functionScope);
        RecordTypeNames(parameterTypeHints, line, column, functionScope);
        RecordTypeName(returnType, line, column, functionScope);
        VisitStatement(body, functionScope);
    }

    private void VisitClassLike(
        string name,
        int line,
        int column,
        List<ClassMember> members,
        string? superclass,
        Scope scope)
    {
        RecordDeclaration(name, line, column, Declare(scope, name, BindingKind.Global));
        if (!string.IsNullOrEmpty(superclass))
        {
            RecordTypeName(superclass, line, column, scope);
        }

        var classScope = new Scope(scope);
        foreach (var member in members)
        {
            if (member.Type == MemberType.Field)
            {
                RecordDeclaration(member.Name, line, column, Declare(classScope, member.Name, BindingKind.Member));
                RecordTypeName(member.TypeHint, line, column, classScope);
                if (member.Value is Expression fieldInitializer)
                {
                    VisitExpression(fieldInitializer, classScope);
                }
            }
            else if (member.Value is FunctionDeclaration method)
            {
                Declare(classScope, method.Name, BindingKind.Member);
            }
        }

        foreach (var member in members)
        {
            if (member.Value is FunctionDeclaration method)
            {
                VisitFunction(method, classScope, isMethod: true);
            }
        }
    }

    private void VisitPrompt(PromptDeclaration prompt, Scope scope)
    {
        RecordDeclaration(prompt.Name, prompt.Line, prompt.Column, Declare(scope, prompt.Name, BindingKind.Global));
        VisitDecorators(prompt.Decorators, scope);

        var promptScope = new Scope(scope);
        RecordParameters(prompt.Name, prompt.Parameters, prompt.Line, prompt.Column, promptScope);
        RecordTypeName(prompt.ReturnType, prompt.Line, prompt.Column, promptScope);
        if (prompt.BodyType == PromptBodyType.ObjectLiteral)
        {
            VisitExpression(prompt.ObjectBody, promptScope);
        }
        else if (prompt.StatementBody != null)
        {
            foreach (var inner in prompt.StatementBody)
            {
                VisitStatement(inner, promptScope);
            }
        }
    }

    private void VisitWorkflow(WorkflowDeclaration workflow, Scope scope)
    {
        RecordDeclaration(workflow.Name, workflow.Line, workflow.Column, Declare(scope, workflow.Name, BindingKind.Global));
        var workflowScope = new Scope(scope);
        RecordParameters(workflow.Name, workflow.Parameters, workflow.Line, workflow.Column, workflowScope);
        foreach (var inner in workflow.Body.Statements)
        {
            VisitStatement(inner, workflowScope);
        }
    }

    private void VisitCallback(CallbackDefinition? callback, Scope scope)
    {
        if (callback == null)
        {
            return;
        }

        var callbackScope = new Scope(scope);
        if (!string.IsNullOrEmpty(callback.ParameterName))
        {
            RecordDeclaration(
                callback.ParameterName,
                callback.Body.Line,
                callback.Body.Column,
                Declare(callbackScope, callback.ParameterName, BindingKind.Local));
        }

        VisitStatement(callback.Body, callbackScope);
    }

    private void VisitDecorators(List<Decorator>? decorators, Scope scope)
    {
        if (decorators == null)
        {
            return;
        }

        foreach (var decorator in decorators)
        {
            foreach (var argument in decorator.Arguments)
            {
                VisitExpression(argument, scope);
            }
        }
    }

    private void VisitExpression(Expression? expression, Scope scope)
    {
        if (expression == null)
        {
            return;
        }

        switch (expression)
        {
            case IdentifierExpression identifier:
                RecordUse(identifier.Name, identifier.Line, identifier.Column, scope);
                break;
            case FunctionCallExpression call:
                VisitExpression(call.Callee, scope);
                foreach (var argument in call.Arguments)
                {
                    VisitExpression(argument, scope);
                }
                break;
            case MemberAccessExpression member:
                VisitExpression(member.Object, scope);
                if (member.Object is ThisExpression or SelfExpression)
                {
                    RecordMemberUse(member.Member, member.Line, member.Column, scope);
                }
                break;
            case BinaryExpression binary:
                VisitExpression(binary.Left, scope);
                VisitExpression(binary.Right, scope);
                break;
            case UnaryExpression unary:
                VisitExpression(unary.Right, scope);
                break;
            case PostfixExpression postfix:
                VisitExpression(postfix.Left, scope);
                break;
            case TernaryExpression ternary:
                VisitExpression(ternary.Condition, scope);
                VisitExpression(ternary.ThenBranch, scope);
                VisitExpression(ternary.ElseBranch, scope);
                break;
            case AwaitExpression awaitExpression:
                VisitExpression(awaitExpression.Expression, scope);
                break;
            case AsyncExpression asyncExpression:
                VisitExpression(asyncExpression.Expression, scope);
                break;
            case PipeExpression pipe:
                VisitExpression(pipe.Left, scope);
                VisitExpression(pipe.Right, scope);
                break;
            case ArrayAccessExpression arrayAccess:
                VisitExpression(arrayAccess.Array, scope);
                VisitExpression(arrayAccess.Index, scope);
                break;
            case ArrayLiteralExpression arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                {
                    VisitExpression(element, scope);
                }
                break;
            case DictionaryLiteralExpression dictionary:
                foreach (var (key, value) in dictionary.Entries)
                {
                    VisitExpression(key, scope);
                    VisitExpression(value, scope);
                }
                break;
            case ObjectLiteralExpression objectLiteral:
                foreach (var (key, value) in objectLiteral.Properties)
                {
                    VisitExpression(key, scope);
                    VisitExpression(value, scope);
                }
                break;
            case InterpolatedStringExpression interpolated:
                RecordInterpolatedIdentifiers(interpolated, scope);
                break;
            case LambdaExpression lambda:
                var lambdaScope = new Scope(scope);
                RecordParameters(nameAnchor: null, lambda.Parameters, lambda.Line, lambda.Column, lambdaScope);
                VisitExpression(lambda.ExpressionBody, lambdaScope);
                VisitStatement(lambda.BlockBody, lambdaScope);
                break;
            case ListComprehensionExpression listComp:
                VisitExpression(listComp.Iterable, scope);
                var listScope = new Scope(scope);
                RecordDeclaration(listComp.Variable, listComp.Line, listComp.Column, Declare(listScope, listComp.Variable, BindingKind.Local));
                VisitExpression(listComp.Element, listScope);
                VisitExpression(listComp.Filter, listScope);
                break;
            case DictComprehensionExpression dictComp:
                VisitExpression(dictComp.Iterable, scope);
                var dictScope = new Scope(scope);
                RecordDeclaration(dictComp.Variable, dictComp.Line, dictComp.Column, Declare(dictScope, dictComp.Variable, BindingKind.Local));
                VisitExpression(dictComp.Key, dictScope);
                VisitExpression(dictComp.Value, dictScope);
                VisitExpression(dictComp.Filter, dictScope);
                break;
            case MatchExpression match:
                VisitExpression(match.Value, scope);
                foreach (var arm in match.Cases)
                {
                    var caseScope = new Scope(scope);
                    DeclarePattern(arm.Pattern, caseScope);
                    VisitExpression(arm.Guard, caseScope);
                    VisitStatement(arm.Body, caseScope);
                }

                VisitStatement(match.DefaultCase, scope);
                break;
            case NewExpression newExpression:
                RecordTypeName(newExpression.ClassName, newExpression.Line, newExpression.Column, scope);
                foreach (var argument in newExpression.Arguments)
                {
                    VisitExpression(argument, scope);
                }
                break;
            case SpawnExpression spawn:
                RecordTypeName(spawn.ActorName, spawn.Line, spawn.Column, scope);
                foreach (var argument in spawn.Arguments)
                {
                    VisitExpression(argument, scope);
                }
                break;
            case GraphLiteralExpression graph:
                VisitExpression(graph.NodesExpression, scope);
                VisitExpression(graph.EdgesExpression, scope);
                break;
            case NamedArgumentExpression named:
                VisitExpression(named.Value, scope);
                break;
        }
    }

    private void DeclarePattern(Pattern? pattern, Scope scope)
    {
        switch (pattern)
        {
            case IdentifierPattern identifier:
                RecordDeclaration(identifier.Name, identifier.Line, identifier.Column, Declare(scope, identifier.Name, BindingKind.Local));
                break;
            case ArrayPattern array:
                foreach (var element in array.Elements)
                {
                    DeclarePattern(element, scope);
                }

                if (!string.IsNullOrEmpty(array.Rest?.Name))
                {
                    RecordDeclaration(array.Rest!.Name!, array.Rest.Line, array.Rest.Column, Declare(scope, array.Rest.Name!, BindingKind.Local));
                }
                break;
            case ArrayDestructuringPattern arrayDestructuring:
                foreach (var element in arrayDestructuring.Elements)
                {
                    DeclarePattern(element, scope);
                }

                if (!string.IsNullOrEmpty(arrayDestructuring.Rest?.Name))
                {
                    RecordDeclaration(
                        arrayDestructuring.Rest!.Name!,
                        arrayDestructuring.Rest.Line,
                        arrayDestructuring.Rest.Column,
                        Declare(scope, arrayDestructuring.Rest.Name!, BindingKind.Local));
                }
                break;
            case ObjectPattern objectPattern:
                DeclareObjectPattern(objectPattern.Properties, scope, objectPattern.Line, objectPattern.Column);
                break;
            case ObjectDestructuringPattern objectDestructuring:
                DeclareObjectPattern(objectDestructuring.Properties, scope, objectDestructuring.Line, objectDestructuring.Column);
                break;
            case VariantPattern variant:
                foreach (var payload in variant.PayloadPatterns)
                {
                    DeclarePattern(payload, scope);
                }
                break;
            case RestPattern rest when !string.IsNullOrEmpty(rest.Name):
                RecordDeclaration(rest.Name!, rest.Line, rest.Column, Declare(scope, rest.Name!, BindingKind.Local));
                break;
        }
    }

    private void DeclareObjectPattern(List<ObjectPatternProperty> properties, Scope scope, int line, int column)
    {
        foreach (var property in properties)
        {
            if (property.Pattern != null)
            {
                DeclarePattern(property.Pattern, scope);
                continue;
            }

            var bindingName = property.BindingName ?? property.Key;
            if (!string.IsNullOrEmpty(bindingName))
            {
                RecordDeclaration(bindingName, line, column, Declare(scope, bindingName, BindingKind.Local));
            }
        }
    }

    private void RecordParameters(string? nameAnchor, List<string> parameters, int line, int column, Scope scope)
    {
        var startLine = line;
        var startColumn = column;
        if (!string.IsNullOrEmpty(nameAnchor))
        {
            startColumn = column + nameAnchor.Length;
        }

        foreach (var parameter in parameters)
        {
            var token = FindNameToken(parameter, startLine, startColumn);
            var binding = Declare(scope, parameter, BindingKind.Local);
            if (token != null)
            {
                RecordToken(token, binding, isDeclaration: true);
                startLine = token.Line;
                startColumn = token.Column + token.Lexeme.Length;
            }
            else
            {
                RecordDeclaration(parameter, line, column, binding);
            }
        }
    }

    private void RecordInterpolatedIdentifiers(InterpolatedStringExpression interpolated, Scope scope)
    {
        foreach (var token in _tokens)
        {
            if (token.Type != TokenType.InterpolatedString || token.Line != interpolated.Line)
            {
                continue;
            }

            foreach (var ident in EnumerateInterpolatedStringIdentifiers(token))
            {
                RecordUse(ident.Lexeme, ident.Line, ident.Column, scope);
            }

            return;
        }

        foreach (var segment in interpolated.Segments)
        {
            if (segment.IsExpression)
            {
                VisitExpression(segment.Expression, scope);
            }
        }
    }

    private void RecordTypeNames(List<string?>? typeNames, int line, int column, Scope scope)
    {
        if (typeNames == null)
        {
            return;
        }

        foreach (var typeName in typeNames)
        {
            RecordTypeName(typeName, line, column, scope);
        }
    }

    private void RecordTypeName(string? typeName, int line, int column, Scope scope)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return;
        }

        var name = typeName.Trim();
        if (name.EndsWith("[]", StringComparison.Ordinal))
        {
            name = name[..^2];
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var token = FindNameToken(name, line, column);
        if (token != null)
        {
            RecordUse(token.Lexeme, token.Line, token.Column, _root);
            return;
        }

        RecordUse(name, line, column, _root);
    }

    private void RecordPatternUses(Pattern? pattern, Scope scope)
    {
        switch (pattern)
        {
            case IdentifierPattern identifier:
                RecordUse(identifier.Name, identifier.Line, identifier.Column, scope);
                break;
            case ArrayPattern array:
                foreach (var element in array.Elements)
                {
                    RecordPatternUses(element, scope);
                }

                if (!string.IsNullOrEmpty(array.Rest?.Name))
                {
                    RecordUse(array.Rest!.Name!, array.Rest.Line, array.Rest.Column, scope);
                }
                break;
            case ArrayDestructuringPattern arrayDestructuring:
                foreach (var element in arrayDestructuring.Elements)
                {
                    RecordPatternUses(element, scope);
                }

                if (!string.IsNullOrEmpty(arrayDestructuring.Rest?.Name))
                {
                    RecordUse(arrayDestructuring.Rest!.Name!, arrayDestructuring.Rest.Line, arrayDestructuring.Rest.Column, scope);
                }
                break;
            case ObjectPattern objectPattern:
                foreach (var property in objectPattern.Properties)
                {
                    if (property.Pattern != null)
                    {
                        RecordPatternUses(property.Pattern, scope);
                    }
                    else if (!string.IsNullOrEmpty(property.BindingName ?? property.Key))
                    {
                        RecordUse(property.BindingName ?? property.Key, objectPattern.Line, objectPattern.Column, scope);
                    }
                }
                break;
            case ObjectDestructuringPattern objectDestructuring:
                foreach (var property in objectDestructuring.Properties)
                {
                    if (property.Pattern != null)
                    {
                        RecordPatternUses(property.Pattern, scope);
                    }
                    else if (!string.IsNullOrEmpty(property.BindingName ?? property.Key))
                    {
                        RecordUse(property.BindingName ?? property.Key, objectDestructuring.Line, objectDestructuring.Column, scope);
                    }
                }
                break;
            case VariantPattern variant:
                foreach (var payload in variant.PayloadPatterns)
                {
                    RecordPatternUses(payload, scope);
                }
                break;
            case RestPattern rest when !string.IsNullOrEmpty(rest.Name):
                RecordUse(rest.Name!, rest.Line, rest.Column, scope);
                break;
        }
    }

    private void RecordMemberUse(string name, int line, int column, Scope scope)
    {
        var binding = FindMemberBinding(scope, name);
        if (binding == null)
        {
            return;
        }

        var token = FindNameToken(name, line, column);
        if (token != null)
        {
            RecordToken(token, binding, isDeclaration: false);
        }
    }

    private void RecordDeclaration(string name, int line, int column, Binding binding)
    {
        var token = FindNameToken(name, line, column);
        if (token != null)
        {
            RecordToken(token, binding, isDeclaration: true);
            return;
        }

        AddOccurrence(name, ToSpan(line, column, name.Length), binding, isDeclaration: true);
    }

    private void RecordUse(string name, int line, int column, Scope scope)
    {
        AddOccurrence(name, ToSpan(line, column, name.Length), Resolve(scope, name), isDeclaration: false);
    }

    private void RecordToken(Token token, Binding binding, bool isDeclaration)
    {
        AddOccurrence(token.Lexeme, ToSpan(token.Line, token.Column, token.Lexeme.Length), binding, isDeclaration);
    }

    private void AddOccurrence(string name, TextSpanInfo span, Binding binding, bool isDeclaration)
    {
        if (span.Line < 0 || span.Column < 0 || span.Length <= 0)
        {
            return;
        }

        if (_occurrences.Any(occurrence =>
                occurrence.BindingId == binding.Id &&
                occurrence.Span.Line == span.Line &&
                occurrence.Span.Column == span.Column &&
                occurrence.Span.Length == span.Length))
        {
            return;
        }

        _occurrences.Add(new BoundOccurrence
        {
            Name = name,
            Span = span,
            BindingId = binding.Id,
            Kind = binding.Kind,
            IsDeclaration = isDeclaration
        });
    }

    private Binding Declare(Scope scope, string name, BindingKind kind)
    {
        if (scope.Bindings.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var binding = new Binding
        {
            Id = ++_nextBindingId,
            Name = name,
            Kind = kind
        };
        scope.Bindings[name] = binding;
        return binding;
    }

    private Binding Resolve(Scope scope, string name)
    {
        return scope.Lookup(name) ?? Declare(_root, name, BindingKind.Global);
    }

    private static Binding? FindMemberBinding(Scope scope, string name)
    {
        for (var current = scope; current != null; current = current.Parent)
        {
            if (current.Bindings.TryGetValue(name, out var binding) && binding.Kind == BindingKind.Member)
            {
                return binding;
            }
        }

        return null;
    }

    private BindingKind ScopeKind(Scope scope)
    {
        return scope == _root ? BindingKind.Global : BindingKind.Local;
    }

    private Token? FindNameToken(string name, int line, int column)
    {
        Token? nearby = null;
        foreach (var token in EnumerateIdentifierTokens(_tokens))
        {
            if (token.Lexeme != name)
            {
                continue;
            }

            if (token.Line < line || (token.Line == line && token.Column < column))
            {
                continue;
            }

            if (token.Line == line)
            {
                return token;
            }

            if (token.Line <= line + 3)
            {
                nearby ??= token;
            }
        }

        return nearby;
    }

    private static TextSpanInfo ToSpan(int line1, int column1, int length)
    {
        return new TextSpanInfo
        {
            Line = Math.Max(0, line1 - 1),
            Column = Math.Max(0, column1 - 1),
            Length = Math.Max(0, length)
        };
    }

    internal enum BindingKind
    {
        Local,
        Member,
        Global
    }

    internal sealed class BoundOccurrence
    {
        public string Name { get; init; } = string.Empty;
        public TextSpanInfo Span { get; init; } = new();
        public int BindingId { get; init; }
        public BindingKind Kind { get; init; }
        public bool IsDeclaration { get; init; }
    }

    private sealed class Binding
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public BindingKind Kind { get; init; }
    }

    private sealed class Scope
    {
        public Scope? Parent { get; }
        public Dictionary<string, Binding> Bindings { get; } = new(StringComparer.Ordinal);

        public Scope(Scope? parent)
        {
            Parent = parent;
        }

        public Binding? Lookup(string name)
        {
            if (Bindings.TryGetValue(name, out var binding))
            {
                return binding;
            }

            return Parent?.Lookup(name);
        }
    }
}
