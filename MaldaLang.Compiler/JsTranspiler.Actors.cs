// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

public partial class JsTranspiler
{
    private string TranspileSpawnExpression(SpawnExpression spawn)
    {
        var args = string.Join(", ", spawn.Arguments.Select(TranspileExpression));
        var actorName = EscapeIdentifier(spawn.ActorName);
        if (_workerActorNames.Contains(spawn.ActorName))
        {
            return $"mlRuntime.actors.spawnWorker({WorkerScopeName}, {TranspileLiteral(spawn.ActorName)}, [{args}])";
        }

        if (args.Length == 0)
        {
            return $"mlRuntime.actors.spawn(new {actorName}())";
        }

        return $"mlRuntime.actors.spawn(new {actorName}({args}))";
    }

    private const string WorkerScopeName = "__maldaWorkerScope";

    /// <summary>
    /// A self-contained function a Web Worker runs to get the program's declarations: the runtime
    /// aliases, a copy of the emitted types, functions, actors and classes, the top-level consts
    /// they use, and the worker actor classes as its result. The runtime ships its source text
    /// (<c>Function.prototype.toString</c>) to the worker, so it must not reach anything outside
    /// itself but <c>globalThis</c>; top-level variables stay null there, which
    /// <c>WorkerActorRules</c> already rejects for code a worker runs.
    /// </summary>
    private void EmitWorkerScope(
        string typeDeclarations,
        string declarations,
        IReadOnlyList<string> moduleVarNames,
        IReadOnlyList<VarDeclStatement> constants,
        IReadOnlyList<ActorDeclaration> workers)
    {
        var isAsync = constants.Any(c => ExpressionProducesPromise(c.Initializer));
        EmitLine($"{(isAsync ? "async " : string.Empty)}function {WorkerScopeName}() {{");
        _indentLevel++;
        EmitLine("const mlRuntime = globalThis.mlRuntime;");
        EmitLine("const __maldaAdd = mlRuntime.add, __maldaFormat = mlRuntime.formatValue, __maldaFloat = mlRuntime.coerceToFloat;");
        EmitLine("const __maldaGetIndex = mlRuntime.getIndex, __maldaSetIndex = mlRuntime.setIndex, __maldaDivide = mlRuntime.divide, __maldaModulo = mlRuntime.modulo;");
        EmitLine("const __maldaBufGet = mlRuntime.bufGet, __maldaBufSet = mlRuntime.bufSet;");
        if (moduleVarNames.Count > 0)
            EmitLine($"let {string.Join(", ", moduleVarNames.Select(EscapeIdentifier))};");
        EmitIndentedCopy(typeDeclarations);
        EmitIndentedCopy(declarations);
        foreach (var constant in constants)
            EmitLine($"{EscapeIdentifier(constant.Name)} = {(isAsync ? TranspileExpressionAwaited(constant.Initializer) : TranspileExpression(constant.Initializer))};");
        EmitLine($"return {{ {string.Join(", ", workers.Select(w => $"{TranspileLiteral(w.Name)}: {EscapeIdentifier(w.Name)}"))} }};");
        _indentLevel--;
        EmitLine("}");
        EmitLine(string.Empty);
    }

    /// <summary>
    /// Appends already-emitted text one level deeper. Text with a template literal is copied as
    /// it is, since a literal spanning lines must not gain spaces.
    /// </summary>
    private void EmitIndentedCopy(string text)
    {
        if (text.Contains('`'))
        {
            _output.Append(text);
            _generatedLine += text.Count(c => c == '\n');
            return;
        }

        foreach (var line in text.Split('\n').SkipLast(1))
        {
            var content = line.TrimEnd('\r');
            _output.Append(content.Length == 0 ? string.Empty : "    ").AppendLine(content);
            _generatedLine++;
        }
    }

    private void TranspileActorDeclaration(ActorDeclaration actor)
    {
        EmitLine($"class {EscapeIdentifier(actor.Name)} {{");
        _indentLevel++;

        if (actor.Messages.Count > 0)
        {
            var summary = string.Join(", ", actor.Messages.Select(m => $"{m.Name}({string.Join(", ", m.ParameterNames)})"));
            EmitLine($"// Messages: {summary}");
        }

        var previousActor = _currentActor;
        _currentActor = actor;
        ComputeAsyncActorMethods(actor);
        try
        {
            foreach (var member in actor.Members)
            {
                TranspileActorMember(member, actor.Name);
            }
        }
        finally
        {
            _currentActor = previousActor;
        }

        _indentLevel--;
        EmitLine("}");
    }

    /// <summary>
    /// A method is async when its body awaits, directly or through another async helper of
    /// the same actor (fixed point). Sync helpers stay plain methods so they can be called
    /// from the constructor and return values rather than promises.
    /// </summary>
    private void ComputeAsyncActorMethods(ActorDeclaration actor)
    {
        _asyncActorMethods.Clear();
        var methods = actor.Members
            .Where(m => m.Type == MemberType.Method && m.Value is FunctionDeclaration)
            .ToList();
        bool changed;
        do
        {
            changed = false;
            foreach (var method in methods)
            {
                if (_asyncActorMethods.Contains(method.Name))
                    continue;
                var body = ((FunctionDeclaration)method.Value!).Body;
                if (InLocalScope(((FunctionDeclaration)method.Value!).Parameters, () => StatementRequiresAsync(body)))
                {
                    _asyncActorMethods.Add(method.Name);
                    changed = true;
                }
            }
        } while (changed);
    }

    private void TranspileActorMember(ClassMember member, string actorName)
    {
        switch (member.Type)
        {
            case MemberType.Field:
            {
                var staticPrefix = member.IsStatic ? "static " : string.Empty;
                var initializer = member.Value is Expression expression
                    ? TranspileExpression(expression)
                    : "null";
                EmitLineWithSource(member.Value as Node, $"{staticPrefix}{EscapeIdentifier(member.Name)} = {initializer};");
                break;
            }
            case MemberType.Method:
            {
                if (member.Value is not FunctionDeclaration method)
                {
                    return;
                }

                var staticPrefix = member.IsStatic ? "static " : string.Empty;
                var parameters = string.Join(", ", method.Parameters.Select(EscapeIdentifier));
                var asyncPrefix = _asyncActorMethods.Contains(member.Name) ? "async " : string.Empty;
                EmitLine($"{staticPrefix}{asyncPrefix}{EscapeIdentifier(member.Name)}({parameters}) {{");
                _indentLevel++;
                var previousInActorHandler = _isInActorHandler;
                var previousStatic = _currentMemberIsStatic;
                var previousRewrite = _rewriteImplicitMembers;
                _isInActorHandler = true;
                _currentMemberIsStatic = member.IsStatic;
                _rewriteImplicitMembers = true;
                try
                {
                    InLocalScope(method.Parameters, () =>
                    {
                        foreach (var statement in method.Body.Statements)
                        {
                            TranspileStatement(statement);
                        }
                    });
                }
                finally
                {
                    _isInActorHandler = previousInActorHandler;
                    _currentMemberIsStatic = previousStatic;
                    _rewriteImplicitMembers = previousRewrite;
                }
                _indentLevel--;
                EmitLine("}");
                break;
            }
            case MemberType.Constructor:
            {
                if (member.Value is not FunctionDeclaration constructor)
                {
                    return;
                }

                var parameters = string.Join(", ", constructor.Parameters.Select(EscapeIdentifier));
                // JavaScript constructors cannot be async: fail at transpile time rather than
                // emit an `await` the browser rejects when loading the script.
                if (InLocalScope(constructor.Parameters, () => StatementRequiresAsync(constructor.Body)))
                {
                    throw new NotSupportedException(
                        $"Actor '{actorName}' constructor awaits (sleep, an async helper, ...). JavaScript constructors cannot be async; move that work into a message handler.");
                }
                EmitLine($"constructor({parameters}) {{");
                _indentLevel++;
                var previousStatic = _currentMemberIsStatic;
                var previousRewrite = _rewriteImplicitMembers;
                _currentMemberIsStatic = false;
                _rewriteImplicitMembers = true;
                try
                {
                    InLocalScope(constructor.Parameters, () =>
                    {
                        foreach (var statement in constructor.Body.Statements)
                        {
                            TranspileStatement(statement);
                        }
                    });
                }
                finally
                {
                    _currentMemberIsStatic = previousStatic;
                    _rewriteImplicitMembers = previousRewrite;
                }
                _indentLevel--;
                EmitLine("}");
                break;
            }
        }
    }

    private void TranspileSendStatement(SendStatement send)
    {
        if (send.HandlerName == "stop" && send.Arguments.Count == 0 && send.Callback == null && send.TimeoutMilliseconds == null)
        {
            EmitLine($"mlRuntime.actors.callActorOrVoidStop({TranspileExpression(send.Target)});");
            return;
        }

        EmitLine("{");
        _indentLevel++;
        EmitLine($"const __target = {TranspileExpression(send.Target)};");

        if (send.Callback != null)
        {
            EmitLine("const __self = mlRuntime.actors.getSelf();");
            var callback = send.Callback;
            EmitLine($"const __callback = async ({EscapeIdentifier(callback.ParameterName)}Arg) => {{");
            _indentLevel++;
            EmitLine($"let {EscapeIdentifier(callback.ParameterName)} = {EscapeIdentifier(callback.ParameterName)}Arg;");
            InLocalScope([callback.ParameterName], () =>
            {
                foreach (var statement in callback.Body.Statements)
                {
                    TranspileStatement(statement);
                }
            });
            _indentLevel--;
            EmitLine("};");

            if (send.TimeoutErrorHandler != null)
            {
                var errorHandler = send.TimeoutErrorHandler;
                EmitLine($"const __timeoutErrorHandler = async ({EscapeIdentifier(errorHandler.ParameterName)}Arg) => {{");
                _indentLevel++;
                EmitLine($"let {EscapeIdentifier(errorHandler.ParameterName)} = {EscapeIdentifier(errorHandler.ParameterName)}Arg;");
                InLocalScope([errorHandler.ParameterName], () =>
                {
                    foreach (var statement in errorHandler.Body.Statements)
                    {
                        TranspileStatement(statement);
                    }
                });
                _indentLevel--;
                EmitLine("};");
            }
            else
            {
                EmitLine("const __timeoutErrorHandler = null;");
            }

            if (send.TimeoutMilliseconds != null)
            {
                EmitLine($"const __timeoutMs = mlRuntime.coerceToInt({TranspileExpression(send.TimeoutMilliseconds)});");
            }
            else
            {
                EmitLine("const __timeoutMs = null;");
            }

            var handlerName = send.HandlerName == null ? "null" : $"\"{EscapeString(send.HandlerName)}\"";
            var args = string.Join(", ", send.Arguments.Select(TranspileExpression));
            if (args.Length == 0)
            {
                EmitLine($"mlRuntime.actors.sendWithCallback(__self, __target, {handlerName}, __callback, __timeoutMs, __timeoutErrorHandler);");
            }
            else
            {
                EmitLine($"mlRuntime.actors.sendWithCallback(__self, __target, {handlerName}, __callback, __timeoutMs, __timeoutErrorHandler, {args});");
            }
        }
        else
        {
            var handlerName = send.HandlerName == null ? "null" : $"\"{EscapeString(send.HandlerName)}\"";
            var args = string.Join(", ", send.Arguments.Select(TranspileExpression));
            if (args.Length == 0)
            {
                EmitLine($"mlRuntime.actors.send(__target, {handlerName});");
            }
            else
            {
                EmitLine($"mlRuntime.actors.send(__target, {handlerName}, {args});");
            }
        }

        _indentLevel--;
        EmitLine("}");
    }

    private bool StatementRequiresAsync(Statement statement)
    {
        return statement switch
        {
            BlockStatement block => block.Statements.Any(StatementRequiresAsync),
            IfStatement ifStatement =>
                ExpressionRequiresAsync(ifStatement.Condition) ||
                StatementRequiresAsync(ifStatement.ThenBranch) ||
                (ifStatement.ElseBranch != null && StatementRequiresAsync(ifStatement.ElseBranch)),
            WhileStatement whileStatement =>
                ExpressionRequiresAsync(whileStatement.Condition) ||
                StatementRequiresAsync(whileStatement.Body),
            ForStatement forStatement =>
                (forStatement.Initializer != null && StatementRequiresAsync(forStatement.Initializer)) ||
                (forStatement.Condition != null && ExpressionRequiresAsync(forStatement.Condition)) ||
                (forStatement.Increment != null && ExpressionRequiresAsync(forStatement.Increment)) ||
                StatementRequiresAsync(forStatement.Body),
            FunctionDeclaration functionDeclaration => StatementRequiresAsync(functionDeclaration.Body),
            ReturnStatement returnStatement => returnStatement.Value != null && ExpressionProducesPromise(returnStatement.Value),
            ExpressionStatement expressionStatement => ExpressionProducesPromise(expressionStatement.Expression),
            PrintStatement printStatement => ExpressionRequiresAsync(printStatement.Expression),
            VarDeclStatement varDecl => ExpressionProducesPromise(varDecl.Initializer),
            AssignmentStatement assignment =>
                ExpressionRequiresAsync(assignment.Target) ||
                ExpressionRequiresAsync(assignment.Value),
            SendStatement sendStatement =>
                ExpressionRequiresAsync(sendStatement.Target) ||
                sendStatement.Arguments.Any(ExpressionRequiresAsync) ||
                (sendStatement.TimeoutMilliseconds != null && ExpressionRequiresAsync(sendStatement.TimeoutMilliseconds)) ||
                (sendStatement.Callback != null && StatementRequiresAsync(sendStatement.Callback.Body)) ||
                (sendStatement.TimeoutErrorHandler != null && StatementRequiresAsync(sendStatement.TimeoutErrorHandler.Body)),
            ForInStatement forIn =>
                ExpressionRequiresAsync(forIn.Collection) ||
                StatementRequiresAsync(forIn.Body),
            TryStatement tryStatement =>
                tryStatement.TryBlock.Statements.Any(StatementRequiresAsync) ||
                tryStatement.CatchClauses.Any(c => StatementRequiresAsync(c.Body)) ||
                (tryStatement.FinallyBlock?.Statements.Any(StatementRequiresAsync) ?? false),
            ThrowStatement throwStatement => ExpressionRequiresAsync(throwStatement.Exception),
            ActorDeclaration actor => actor.Members.Any(ActorMemberRequiresAsync),
            DeferStatement => true,
            UsingResourceStatement => true,
            DestructuringVarDecl destVar => ExpressionProducesPromise(destVar.Initializer),
            DestructuringAssignment destAssign => ExpressionProducesPromise(destAssign.Value),
            ClassDeclaration classDecl => classDecl.Members.Any(ClassMemberRequiresAsync),
            _ => false
        };
    }

    private bool ClassMemberRequiresAsync(ClassMember member)
    {
        if (member.Value is FunctionDeclaration function)
        {
            return StatementRequiresAsync(function.Body);
        }

        return member.Value is Expression expression && ExpressionProducesPromise(expression);
    }

    private bool ActorMemberRequiresAsync(ClassMember member)
    {
        if (member.Value is not FunctionDeclaration function)
        {
            return member.Value is Expression expression && ExpressionRequiresAsync(expression);
        }

        return StatementRequiresAsync(function.Body) || member.Type == MemberType.Method;
    }

    private bool ExpressionRequiresAsync(Expression expression)
    {
        return expression switch
        {
            AwaitExpression => true,
            ReceiveExpression => true,
            AsyncExpression => true,
            BinaryExpression binary => ExpressionRequiresAsync(binary.Left) || ExpressionRequiresAsync(binary.Right),
            UnaryExpression unary => ExpressionRequiresAsync(unary.Right),
            PostfixExpression postfix => ExpressionRequiresAsync(postfix.Left),
            TernaryExpression ternary =>
                ExpressionRequiresAsync(ternary.Condition) ||
                ExpressionRequiresAsync(ternary.ThenBranch) ||
                ExpressionRequiresAsync(ternary.ElseBranch),
            MatchExpression match =>
                ExpressionRequiresAsync(match.Value) ||
                match.Cases.Any(c =>
                    (c.Guard != null && ExpressionRequiresAsync(c.Guard)) ||
                    StatementRequiresAsync(c.Body)) ||
                (match.DefaultCase != null && StatementRequiresAsync(match.DefaultCase)),
            FunctionCallExpression functionCall => FunctionCallProducesPromise(functionCall),
            MemberAccessExpression member => ExpressionRequiresAsync(member.Object),
            ArrayAccessExpression arrayAccess =>
                ExpressionRequiresAsync(arrayAccess.Array) ||
                ExpressionRequiresAsync(arrayAccess.Index),
            ArrayLiteralExpression arrayLiteral => arrayLiteral.Elements.Any(ExpressionRequiresAsync),
            ObjectLiteralExpression objectLiteral => objectLiteral.Properties.Any(p => ExpressionRequiresAsync(p.Key) || ExpressionRequiresAsync(p.Value)),
            DictionaryLiteralExpression dictionaryLiteral => dictionaryLiteral.Entries.Any(p => ExpressionRequiresAsync(p.Key) || ExpressionRequiresAsync(p.Value)),
            SpawnExpression spawn => spawn.Arguments.Any(ExpressionRequiresAsync),
            NewExpression newExpression => newExpression.Arguments.Any(ExpressionRequiresAsync),
            LambdaExpression lambda =>
                lambda.ExpressionBody != null
                    ? ExpressionRequiresAsync(lambda.ExpressionBody)
                    : lambda.BlockBody?.Statements.Any(StatementRequiresAsync) ?? false,
            PipeExpression pipe => ExpressionRequiresAsync(pipe.Left) || ExpressionRequiresAsync(pipe.Right),
            InterpolatedStringExpression interpolated => interpolated.Segments.Any(
                segment => segment.IsExpression && segment.Expression != null && ExpressionRequiresAsync(segment.Expression)),
            ListComprehensionExpression list =>
                ExpressionRequiresAsync(list.Element) ||
                ExpressionRequiresAsync(list.Iterable) ||
                (list.Filter != null && ExpressionRequiresAsync(list.Filter)),
            DictComprehensionExpression dict =>
                ExpressionRequiresAsync(dict.Key) ||
                ExpressionRequiresAsync(dict.Value) ||
                ExpressionRequiresAsync(dict.Iterable) ||
                (dict.Filter != null && ExpressionRequiresAsync(dict.Filter)),
            _ => false
        };
    }
}
