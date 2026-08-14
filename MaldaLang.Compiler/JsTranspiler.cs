// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using MaldaLang;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

public sealed class JsTranspileResult
{
    public string JavaScript { get; }
    public string? SourceMapJson { get; }

    public JsTranspileResult(string javaScript, string? sourceMapJson)
    {
        JavaScript = javaScript;
        SourceMapJson = sourceMapJson;
    }
}

public class JsTranspiler
{
    private readonly StringBuilder _output;
    private int _indentLevel;
    private int _matchTempCounter;
    private bool _isInActorHandler;
    private readonly List<SourceMappingEntry> _mappings;
    private readonly Stack<Statement?> _desugaredForLoopIncrements = new();
    private readonly HashSet<string> _asyncFunctions = new(StringComparer.Ordinal);
    private int _generatedLine;
    private int? _currentSourceLine;
    private int? _currentSourceColumn;

    public JsTranspiler()
    {
        _output = new StringBuilder();
        _indentLevel = 0;
        _matchTempCounter = 0;
        _isInActorHandler = false;
        _mappings = new List<SourceMappingEntry>();
        _generatedLine = 1;
        _currentSourceLine = null;
        _currentSourceColumn = null;
    }

    public string Transpile(List<Statement> statements, bool isLibrary = false, string? sourceFilePath = null)
    {
        return TranspileWithSourceMap(statements, isLibrary, sourceFilePath, sourceContent: null, generatedFileName: null).JavaScript;
    }

    public JsTranspileResult TranspileWithSourceMap(
        List<Statement> statements,
        bool isLibrary = false,
        string? sourceFilePath = null,
        string? sourceContent = null,
        string? generatedFileName = null)
    {
        _output.Clear();
        _indentLevel = 0;
        _matchTempCounter = 0;
        _isInActorHandler = false;
        _mappings.Clear();
        _desugaredForLoopIncrements.Clear();
        _asyncFunctions.Clear();
        _generatedLine = 1;
        _currentSourceLine = null;
        _currentSourceColumn = null;

        var functions = new List<FunctionDeclaration>();
        var typeDeclarations = new List<TypeDeclaration>();
        var actorDeclarations = new List<ActorDeclaration>();
        var classDeclarations = new List<ClassDeclaration>();
        var propertyDeclarations = new List<PropertyDeclaration>();
        var topLevelStatements = new List<Statement>();

        foreach (var statement in statements)
        {
            if (statement is FunctionDeclaration functionDeclaration)
            {
                functions.Add(functionDeclaration);
            }
            else if (statement is TypeDeclaration typeDeclaration)
            {
                typeDeclarations.Add(typeDeclaration);
            }
            else if (statement is ActorDeclaration actorDeclaration)
            {
                actorDeclarations.Add(actorDeclaration);
            }
            else if (statement is ClassDeclaration classDeclaration)
            {
                classDeclarations.Add(classDeclaration);
            }
            else if (statement is PropertyDeclaration propertyDeclaration)
            {
                propertyDeclarations.Add(propertyDeclaration);
            }
            else
            {
                topLevelStatements.Add(statement);
            }
        }

        EmitLine("const MaldaApp = (() => {");
        _indentLevel++;

        EmitLine("if (typeof globalThis.mlRuntime === \"undefined\") {");
        _indentLevel++;
        EmitLine("throw new Error(\"mlRuntime is not available. Include malda-js-runtime.js before running generated MALDA JavaScript.\");");
        _indentLevel--;
        EmitLine("}");
        EmitLine("const mlRuntime = globalThis.mlRuntime;");
        EmitLine(string.Empty);

        foreach (var typeDeclaration in typeDeclarations)
        {
            EmitVariantConstructors(typeDeclaration);
        }

        if (typeDeclarations.Count > 0)
        {
            EmitLine(string.Empty);
        }

        foreach (var function in functions)
        {
            WithSource(function, () =>
            {
                TranspileFunctionDeclaration(function);
                EmitLine(string.Empty);
            });
        }

        foreach (var actor in actorDeclarations)
        {
            WithSource(actor, () =>
            {
                TranspileActorDeclaration(actor);
                EmitLine(string.Empty);
            });
        }

        foreach (var classDeclaration in classDeclarations)
        {
            WithSource(classDeclaration, () =>
            {
                TranspileClassDeclaration(classDeclaration);
                EmitLine(string.Empty);
            });
        }

        foreach (var propertyDeclaration in propertyDeclarations)
        {
            WithSource(propertyDeclaration, () =>
            {
                TranspilePropertyDeclaration(propertyDeclaration);
                EmitLine(string.Empty);
            });
        }

        if (propertyDeclarations.Count > 0)
        {
            EmitLine("const __propertyRegistry = {");
            _indentLevel++;
            for (var i = 0; i < propertyDeclarations.Count; i++)
            {
                var property = propertyDeclarations[i];
                var parameterJson = JsonSerializer.Serialize(property.Parameters);
                EmitLine($"{EscapeIdentifier(property.Name)}: {{ name: {TranspileLiteral(property.Name)}, fn: {GetPropertyRunnerName(property.Name)}, parameters: {parameterJson} }}{(i < propertyDeclarations.Count - 1 ? "," : "")}");
            }
            _indentLevel--;
            EmitLine("};");
            EmitLine(string.Empty);
        }

        var mainNeedsAsync = topLevelStatements.Any(StatementRequiresAsync);
        EmitLine(mainNeedsAsync ? "async function main() {" : "function main() {");
        _indentLevel++;
        foreach (var statement in topLevelStatements)
        {
            TranspileStatement(statement);
        }
        _indentLevel--;
        EmitLine("}");
        EmitLine(string.Empty);
        var exportNames = new List<string> { "main" };
        if (functions.Any(f => f.Name == "renderRoot"))
        {
            exportNames.Add("renderRoot");
        }

        if (functions.Any(f => f.Name == "bootstrap"))
        {
            exportNames.Add("bootstrap");
        }

        EmitLine($"return {{ {string.Join(", ", exportNames)} }};");

        _indentLevel--;
        EmitLine("})();");
        EmitLine(string.Empty);
        EmitLine("if (typeof globalThis !== \"undefined\") {");
        _indentLevel++;
        EmitLine("globalThis.MaldaApp = MaldaApp;");
        _indentLevel--;
        EmitLine("}");
        EmitLine(string.Empty);
        EmitLine("if (typeof module !== \"undefined\" && module.exports) {");
        _indentLevel++;
        EmitLine("module.exports = MaldaApp;");
        _indentLevel--;
        EmitLine("}");
        EmitLine(string.Empty);
        EmitLine("async function __maldaRunMain() {");
        _indentLevel++;
        EmitLine("try {");
        _indentLevel++;
        EmitLine("await MaldaApp.main();");
        _indentLevel--;
        EmitLine("} finally {");
        _indentLevel++;
        EmitLine("if (mlRuntime.actors && typeof mlRuntime.actors.shutdownAsync === \"function\") {");
        _indentLevel++;
        EmitLine("await mlRuntime.actors.shutdownAsync();");
        _indentLevel--;
        EmitLine("}");
        _indentLevel--;
        EmitLine("}");
        _indentLevel--;
        EmitLine("}");
        EmitLine(string.Empty);
        EmitLine("if (typeof require !== \"undefined\" && require.main === module) {");
        _indentLevel++;
        EmitLine("__maldaRunMain().catch((error) => {");
        _indentLevel++;
        EmitLine("throw error;");
        _indentLevel--;
        EmitLine("});");
        _indentLevel--;
        EmitLine("}");

        var output = _output.ToString();
        var sourceMap = BuildSourceMapJson(sourceFilePath, sourceContent, generatedFileName);
        return new JsTranspileResult(output, sourceMap);
    }

    private void TranspileFunctionDeclaration(FunctionDeclaration declaration)
    {
        var parameters = string.Join(", ", declaration.Parameters.Select(EscapeIdentifier));
        var needsAsync = StatementRequiresAsync(declaration.Body);
        if (needsAsync)
        {
            _asyncFunctions.Add(declaration.Name);
        }

        var asyncPrefix = needsAsync ? "async " : string.Empty;
        EmitLine($"{asyncPrefix}function {EscapeIdentifier(declaration.Name)}({parameters}) {{");
        _indentLevel++;
        TranspileStatementsWithOptionalDeferFrame(declaration.Body.Statements);
        _indentLevel--;
        EmitLine("}");
    }

    private static string GetPropertyRunnerName(string propertyName) => $"__property_{propertyName}";

    private void TranspileStatement(Statement statement)
    {
        WithSource(statement, () =>
        {
            switch (statement)
            {
                case BlockStatement block:
                    EmitLine("{");
                    _indentLevel++;
                    TranspileStatementsWithOptionalDeferFrame(block.Statements);
                    _indentLevel--;
                    EmitLine("}");
                    break;
                case VarDeclStatement varDecl:
                    EmitLineWithSource(varDecl.Initializer, $"let {EscapeIdentifier(varDecl.Name)} = {TranspileExpressionAwaited(varDecl.Initializer)};");
                    break;
                case AssignmentStatement assignment:
                    EmitLineWithSource(assignment.Value, $"{TranspileAssignmentTarget(assignment.Target)} {MapAssignmentOperator(assignment.Operator)} {TranspileExpression(assignment.Value)};");
                    break;
                case IfStatement ifStatement:
                    EmitLineWithSource(ifStatement.Condition, $"if ({TranspileCondition(ifStatement.Condition)})");
                    TranspileEmbeddedStatement(ifStatement.ThenBranch);
                    if (ifStatement.ElseBranch != null)
                    {
                        EmitLine("else");
                        TranspileEmbeddedStatement(ifStatement.ElseBranch);
                    }
                    break;
                case WhileStatement whileStatement:
                    _desugaredForLoopIncrements.Push(GetDesugaredForIncrement(whileStatement.Body));
                    EmitLineWithSource(whileStatement.Condition, $"while ({TranspileCondition(whileStatement.Condition)})");
                    TranspileEmbeddedStatement(whileStatement.Body);
                    _desugaredForLoopIncrements.Pop();
                    break;
                case ForStatement forStatement:
                    EmitLineWithSource(forStatement.Condition ?? (Node?)forStatement.Increment ?? forStatement.Initializer, $"for ({TranspileForInitializer(forStatement.Initializer)}; {TranspileForCondition(forStatement.Condition)}; {TranspileForIncrement(forStatement.Increment)})");
                    TranspileEmbeddedStatement(forStatement.Body);
                    break;
                case FunctionDeclaration declaration:
                    TranspileFunctionDeclaration(declaration);
                    break;
                case ReturnStatement returnStatement:
                    if (returnStatement.Value == null)
                    {
                        EmitLine("return;");
                    }
                    else
                    {
                        EmitLineWithSource(returnStatement.Value, $"return {TranspileExpressionAwaited(returnStatement.Value)};");
                    }
                    break;
                case ExpressionStatement expressionStatement:
                    EmitLineWithSource(expressionStatement.Expression, $"{TranspileExpressionAwaited(expressionStatement.Expression)};");
                    break;
                case PrintStatement printStatement:
                    EmitLineWithSource(printStatement.Expression, $"mlRuntime.builtins.println({TranspileExpression(printStatement.Expression)});");
                    break;
                case BreakStatement:
                    EmitLine("break;");
                    break;
                case ContinueStatement:
                    if (_desugaredForLoopIncrements.TryPeek(out var desugaredIncrement) && desugaredIncrement != null)
                    {
                        TranspileStatement(desugaredIncrement);
                    }
                    EmitLine("continue;");
                    break;
                case ForInStatement forInStatement:
                    EmitLineWithSource(forInStatement.Collection, $"for (const {EscapeIdentifier(forInStatement.VariableName)} of {TranspileExpression(forInStatement.Collection)})");
                    TranspileEmbeddedStatement(forInStatement.Body);
                    break;
                case TryStatement tryStatement:
                    TranspileTry(tryStatement);
                    break;
                case ThrowStatement throwStatement:
                    EmitLineWithSource(throwStatement.Exception, $"mlRuntime.throwMalda({TranspileExpression(throwStatement.Exception)});");
                    break;
                case SendStatement sendStatement:
                    TranspileSendStatement(sendStatement);
                    break;
                case TypeDeclaration:
                    // Type declarations are emitted as constructor helpers at module scope.
                    break;
                case ApiDeclaration:
                    throw new NotSupportedException(
                        "Closed api / program(Api) / runProgram is host-only (interpreter and C# transpile). JavaScript does not support api declarations.");
                case ActorDeclaration actorDeclaration:
                    // Top-level actor declarations are emitted before main.
                    // Nested actor declarations (inside blocks/functions) must be emitted inline.
                    TranspileActorDeclaration(actorDeclaration);
                    break;
                case ClassDeclaration classDeclaration:
                    TranspileClassDeclaration(classDeclaration);
                    break;
                case DeferStatement deferStatement:
                    EmitLine("mlRuntime.registerDefer(async () => {");
                    _indentLevel++;
                    foreach (var nestedStatement in deferStatement.Body.Statements)
                    {
                        TranspileStatement(nestedStatement);
                    }
                    _indentLevel--;
                    EmitLine("});");
                    break;
                case UsingResourceStatement usingResource:
                    EmitLineWithSource(usingResource.Initializer, $"const {EscapeIdentifier(usingResource.VariableName)} = {TranspileExpressionAwaited(usingResource.Initializer)};");
                    EmitLine("{");
                    _indentLevel++;
                    TranspileStatementsWithOptionalDeferFrame(usingResource.Body.Statements);
                    _indentLevel--;
                    EmitLine("}");
                    EmitLine($"await mlRuntime.disposeResource({EscapeIdentifier(usingResource.VariableName)});");
                    break;
                default:
                    throw new NotSupportedException($"JavaScript transpilation for statement '{statement.GetType().Name}' is not supported yet.");
            }
        });
    }

    private void TranspileEmbeddedStatement(Statement statement)
    {
        if (statement is BlockStatement)
        {
            TranspileStatement(statement);
            return;
        }

        EmitLine("{");
        _indentLevel++;
        TranspileStatement(statement);
        _indentLevel--;
        EmitLine("}");
    }

    private static Statement? GetDesugaredForIncrement(Statement body)
    {
        if (body is BlockStatement block && block.Statements.Count == 2)
        {
            return block.Statements[1];
        }

        return null;
    }

    private string TranspileExpression(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return TranspileLiteral(literal.Value);
            case IdentifierExpression identifier:
                return EscapeIdentifier(identifier.Name);
            case NamedArgumentExpression named:
                return TranspileExpression(named.Value);
            case BinaryExpression binary:
                return TranspileBinaryExpression(binary);
            case UnaryExpression unary:
                return TranspileUnaryExpression(unary);
            case PostfixExpression postfix:
                return $"({TranspileExpression(postfix.Left)}{MapPostfixOperator(postfix.Operator)})";
            case TernaryExpression ternary:
                return TranspileTernaryExpression(ternary);
            case MatchExpression match:
                return TranspileMatchExpression(match);
            case FunctionCallExpression functionCall:
                return TranspileFunctionCall(functionCall);
            case MemberAccessExpression memberAccess:
                if (memberAccess.Object is IdentifierExpression objectIdentifier &&
                    (objectIdentifier.Name == "dom" || objectIdentifier.Name == "game" || objectIdentifier.Name == "three"))
                {
                    return $"mlRuntime.{objectIdentifier.Name}.{EscapeIdentifier(memberAccess.Member)}";
                }
                if (memberAccess.IsNullConditional)
                {
                    return $"mlRuntime.getMemberNullSafe({TranspileExpression(memberAccess.Object)}, {TranspileLiteral(memberAccess.Member)})";
                }
                return $"{TranspileExpression(memberAccess.Object)}.{EscapeIdentifier(memberAccess.Member)}";
            case ArrayAccessExpression arrayAccess:
                if (arrayAccess.IsNullConditional)
                {
                    return $"mlRuntime.getIndexNullSafe({TranspileExpression(arrayAccess.Array)}, {TranspileExpression(arrayAccess.Index)})";
                }
                return $"{TranspileExpression(arrayAccess.Array)}[{TranspileExpression(arrayAccess.Index)}]";
            case ArrayLiteralExpression arrayLiteral:
                return $"[{string.Join(", ", arrayLiteral.Elements.Select(TranspileExpression))}]";
            case ObjectLiteralExpression objectLiteral:
                return TranspileObjectLikeLiteral(objectLiteral.Properties);
            case DictionaryLiteralExpression dictionaryLiteral:
                return $"mlRuntime.markDict({TranspileObjectLikeLiteral(dictionaryLiteral.Entries)})";
            case SpawnExpression spawn:
                return TranspileSpawnExpression(spawn);
            case SelfExpression:
                return "mlRuntime.actors.getSelf()";
            case ReceiveExpression:
                return "await mlRuntime.actors.receiveAsync()";
            case AwaitExpression awaitExpression:
                return $"(await {TranspileExpression(awaitExpression.Expression)})";
            case AsyncExpression asyncExpression:
                return $"(async () => ({TranspileExpression(asyncExpression.Expression)}))()";
            case ThisExpression:
                return "this";
            case NewExpression newExpression:
                return $"new {EscapeIdentifier(newExpression.ClassName)}({JoinArguments(newExpression.Arguments)})";
            case LambdaExpression lambda:
                return TranspileLambdaExpression(lambda);
            case PipeExpression pipe:
                return TranspilePipeExpression(pipe);
            case ListComprehensionExpression listComprehension:
                return TranspileListComprehensionExpression(listComprehension);
            case DictComprehensionExpression dictComprehension:
                return TranspileDictComprehensionExpression(dictComprehension);
            default:
                throw new NotSupportedException($"JavaScript transpilation for expression '{expression.GetType().Name}' is not supported yet.");
        }
    }

    private string TranspileExpressionAwaited(Expression expression)
    {
        var transpiled = TranspileExpression(expression);
        return ExpressionProducesPromise(expression) ? $"await {transpiled}" : transpiled;
    }

    private void TranspileStatementsWithOptionalDeferFrame(List<Statement> statements)
    {
        if (!StatementsContainDefer(statements))
        {
            foreach (var statement in statements)
            {
                TranspileStatement(statement);
            }
            return;
        }

        EmitLine("mlRuntime.pushDeferFrame();");
        EmitLine("try {");
        _indentLevel++;
        foreach (var statement in statements)
        {
            TranspileStatement(statement);
        }
        _indentLevel--;
        EmitLine("} finally {");
        _indentLevel++;
        EmitLine("await mlRuntime.runAndPopDeferFrame();");
        _indentLevel--;
        EmitLine("}");
    }

    private static bool StatementsContainDefer(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            if (StatementContainsDefer(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementContainsDefer(Statement statement)
    {
        return statement switch
        {
            DeferStatement => true,
            BlockStatement block => StatementsContainDefer(block.Statements),
            IfStatement ifStatement =>
                StatementContainsDefer(ifStatement.ThenBranch) ||
                (ifStatement.ElseBranch != null && StatementContainsDefer(ifStatement.ElseBranch)),
            WhileStatement whileStatement => StatementContainsDefer(whileStatement.Body),
            ForStatement forStatement => StatementContainsDefer(forStatement.Body),
            ForInStatement forInStatement => StatementContainsDefer(forInStatement.Body),
            TryStatement tryStatement =>
                StatementsContainDefer(tryStatement.TryBlock.Statements) ||
                tryStatement.CatchClauses.Any(c => StatementContainsDefer(c.Body)) ||
                (tryStatement.FinallyBlock != null && StatementsContainDefer(tryStatement.FinallyBlock.Statements)),
            UsingResourceStatement usingResource => StatementsContainDefer(usingResource.Body.Statements),
            FunctionDeclaration functionDeclaration => StatementsContainDefer(functionDeclaration.Body.Statements),
            _ => false
        };
    }

    private void TranspilePropertyDeclaration(PropertyDeclaration propertyDeclaration)
    {
        var parameters = string.Join(", ", propertyDeclaration.Parameters.Select(EscapeIdentifier));
        var runnerName = GetPropertyRunnerName(propertyDeclaration.Name);
        var needsAsync = StatementRequiresAsync(propertyDeclaration.Body);
        if (needsAsync)
        {
            _asyncFunctions.Add(runnerName);
        }

        var asyncPrefix = needsAsync ? "async " : string.Empty;
        EmitLine($"{asyncPrefix}function {runnerName}({parameters}) {{");
        _indentLevel++;
        TranspileStatementsWithOptionalDeferFrame(propertyDeclaration.Body.Statements);
        _indentLevel--;
        EmitLine("}");
    }

    private void TranspileClassDeclaration(ClassDeclaration classDeclaration)
    {
        EmitLine($"class {EscapeIdentifier(classDeclaration.Name)} {{");
        _indentLevel++;
        foreach (var member in classDeclaration.Members)
        {
            TranspileClassMember(member);
        }
        _indentLevel--;
        EmitLine("}");
    }

    private void TranspileClassMember(ClassMember member)
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
                var asyncPrefix = StatementRequiresAsync(method.Body) ? "async " : string.Empty;
                EmitLine($"{staticPrefix}{asyncPrefix}{EscapeIdentifier(member.Name)}({parameters}) {{");
                _indentLevel++;
                TranspileStatementsWithOptionalDeferFrame(method.Body.Statements);
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
                EmitLine($"constructor({parameters}) {{");
                _indentLevel++;
                foreach (var statement in constructor.Body.Statements)
                {
                    TranspileStatement(statement);
                }
                _indentLevel--;
                EmitLine("}");
                break;
            }
        }
    }

    private string TranspileLambdaExpression(LambdaExpression lambda)
    {
        var parameters = string.Join(", ", lambda.Parameters.Select(EscapeIdentifier));
        if (lambda.ExpressionBody != null)
        {
            return $"(({parameters}) => ({TranspileExpression(lambda.ExpressionBody)}))";
        }

        if (lambda.BlockBody == null)
        {
            throw new NotSupportedException("Lambda expression requires a body.");
        }

        var builder = new StringBuilder();
        builder.Append("((");
        builder.Append(parameters);
        builder.Append(") => {");
        var inlineTranspiler = new JsTranspiler();
        foreach (var statement in lambda.BlockBody.Statements)
        {
            builder.Append(inlineTranspiler.TranspileStatementInline(statement));
        }
        builder.Append(" return null; })");
        return builder.ToString();
    }

    private static readonly HashSet<string> ArrayPipelineMethods = new(StringComparer.Ordinal)
    {
        "append", "pop", "shift", "concat", "popOrNull", "shiftOrNull", "get", "at",
        "map", "filter", "reduce", "forEach", "find", "findIndex", "some", "every",
        "sort", "reverse", "slice", "indexOf", "includes", "join", "sum", "average", "min", "max"
    };

    private string TranspilePipeExpression(PipeExpression pipe)
    {
        var left = pipe.Left;
        var right = pipe.Right;

        switch (right)
        {
            case FunctionCallExpression call when call.Callee is IdentifierExpression identifier:
                if (ArrayPipelineMethods.Contains(identifier.Name))
                {
                    var args = string.Join(", ", call.Arguments.Select(TranspileExpression));
                    if (args.Length == 0)
                    {
                        return $"mlRuntime.callArrayMethod({TranspileExpression(left)}, {TranspileLiteral(identifier.Name)})";
                    }

                    return $"mlRuntime.callArrayMethod({TranspileExpression(left)}, {TranspileLiteral(identifier.Name)}, [{args}])";
                }

                return TranspilePipedIdentifierCall(identifier.Name, left, call.Arguments);
            case FunctionCallExpression call:
            {
                var args = new List<Expression> { left };
                args.AddRange(call.Arguments);
                return TranspileFunctionCall(new FunctionCallExpression(call.Callee, args, pipe.Line, pipe.Column));
            }
            case IdentifierExpression identifier:
                return TranspilePipedIdentifierCall(identifier.Name, left, []);
            case LambdaExpression lambda:
                return $"({TranspileLambdaExpression(lambda)})({TranspileExpression(left)})";
            default:
                throw new NotSupportedException(
                    $"Right side of |> must be a function call, identifier, or lambda (got {right.GetType().Name}).");
        }
    }

    private string TranspilePipedIdentifierCall(string name, Expression left, List<Expression> tailArgs)
    {
        if (TryTranspileBuiltInCall(name, [left, ..tailArgs], out var builtInCall))
        {
            return builtInCall;
        }

        var args = string.Join(", ", new[] { TranspileExpression(left) }.Concat(tailArgs.Select(TranspileExpression)));
        return $"{EscapeIdentifier(name)}({args})";
    }

    private string TranspileListComprehensionExpression(ListComprehensionExpression comprehension)
    {
        var builder = new StringBuilder();
        builder.Append("(() => { const __list = []; for (const ");
        builder.Append(EscapeIdentifier(comprehension.Variable));
        builder.Append(" of mlRuntime.getArray(");
        builder.Append(TranspileExpression(comprehension.Iterable));
        builder.Append(")) {");
        if (comprehension.Filter != null)
        {
            builder.Append(" if (!mlRuntime.isTruthy(");
            builder.Append(TranspileExpression(comprehension.Filter));
            builder.Append(")) continue;");
        }

        builder.Append(" __list.push(");
        builder.Append(TranspileExpression(comprehension.Element));
        builder.Append("); } return __list; })()");
        return builder.ToString();
    }

    private string TranspileDictComprehensionExpression(DictComprehensionExpression comprehension)
    {
        var builder = new StringBuilder();
        builder.Append("(() => { const __dict = mlRuntime.markDict({}); for (const ");
        builder.Append(EscapeIdentifier(comprehension.Variable));
        builder.Append(" of mlRuntime.getArray(");
        builder.Append(TranspileExpression(comprehension.Iterable));
        builder.Append(")) {");
        if (comprehension.Filter != null)
        {
            builder.Append(" if (!mlRuntime.isTruthy(");
            builder.Append(TranspileExpression(comprehension.Filter));
            builder.Append(")) continue;");
        }

        builder.Append(" __dict[mlRuntime.coerceToString(");
        builder.Append(TranspileExpression(comprehension.Key));
        builder.Append(")] = ");
        builder.Append(TranspileExpression(comprehension.Value));
        builder.Append("; } return __dict; })()");
        return builder.ToString();
    }

    private bool ExpressionProducesPromise(Expression expression)
    {
        return expression switch
        {
            AwaitExpression => true,
            ReceiveExpression => true,
            AsyncExpression => true,
            FunctionCallExpression functionCall => FunctionCallProducesPromise(functionCall),
            BinaryExpression binary => ExpressionProducesPromise(binary.Left) || ExpressionProducesPromise(binary.Right),
            UnaryExpression unary => ExpressionProducesPromise(unary.Right),
            PostfixExpression postfix => ExpressionProducesPromise(postfix.Left),
            TernaryExpression ternary =>
                ExpressionProducesPromise(ternary.Condition) ||
                ExpressionProducesPromise(ternary.ThenBranch) ||
                ExpressionProducesPromise(ternary.ElseBranch),
            MatchExpression match =>
                ExpressionProducesPromise(match.Value) ||
                match.Cases.Any(c => StatementRequiresAsync(c.Body)) ||
                (match.DefaultCase != null && StatementRequiresAsync(match.DefaultCase)),
            MemberAccessExpression member => ExpressionProducesPromise(member.Object),
            ArrayAccessExpression arrayAccess =>
                ExpressionProducesPromise(arrayAccess.Array) ||
                ExpressionProducesPromise(arrayAccess.Index),
            ArrayLiteralExpression arrayLiteral => arrayLiteral.Elements.Any(ExpressionProducesPromise),
            ObjectLiteralExpression objectLiteral =>
                objectLiteral.Properties.Any(p => ExpressionProducesPromise(p.Key) || ExpressionProducesPromise(p.Value)),
            DictionaryLiteralExpression dictionaryLiteral =>
                dictionaryLiteral.Entries.Any(p => ExpressionProducesPromise(p.Key) || ExpressionProducesPromise(p.Value)),
            SpawnExpression spawn => spawn.Arguments.Any(ExpressionProducesPromise),
            NewExpression newExpression => newExpression.Arguments.Any(ExpressionProducesPromise),
            PipeExpression pipe => ExpressionProducesPromise(pipe.Left) || ExpressionProducesPromise(pipe.Right),
            ListComprehensionExpression list =>
                ExpressionProducesPromise(list.Element) ||
                ExpressionProducesPromise(list.Iterable) ||
                (list.Filter != null && ExpressionProducesPromise(list.Filter)),
            DictComprehensionExpression dict =>
                ExpressionProducesPromise(dict.Key) ||
                ExpressionProducesPromise(dict.Value) ||
                ExpressionProducesPromise(dict.Iterable) ||
                (dict.Filter != null && ExpressionProducesPromise(dict.Filter)),
            LambdaExpression lambda =>
                lambda.ExpressionBody != null
                    ? ExpressionProducesPromise(lambda.ExpressionBody)
                    : lambda.BlockBody?.Statements.Any(StatementRequiresAsync) ?? false,
            _ => false
        };
    }

    private bool FunctionCallProducesPromise(FunctionCallExpression functionCall)
    {
        if (functionCall.Callee is IdentifierExpression identifier)
        {
            if (identifier.Name is "sleep" or "runProperty")
            {
                return true;
            }

            if (_asyncFunctions.Contains(identifier.Name))
            {
                return true;
            }
        }

        return ExpressionProducesPromise(functionCall.Callee) ||
               functionCall.Arguments.Any(ExpressionProducesPromise);
    }

    private void EmitVariantConstructors(TypeDeclaration declaration)
    {
        foreach (var ctor in declaration.Constructors)
        {
            var functionName = EscapeIdentifier(ctor.Name);
            var parameterList = string.Join(", ", ctor.ParameterNames.Select(EscapeIdentifier));
            var escapedTag = EscapeString(ctor.Name);

            EmitLine($"var {functionName} = function({parameterList}) {{");
            _indentLevel++;
            EmitLine($"if (arguments.length !== {ctor.ParameterNames.Count}) {{");
            _indentLevel++;
            EmitLine($"throw new Error(\"Variant constructor {escapedTag} expects {ctor.ParameterNames.Count} argument(s) but got \" + arguments.length + \".\");");
            _indentLevel--;
            EmitLine("}");
            EmitLine("return mlRuntime.variant(\"" + escapedTag + "\", Array.from(arguments));");
            _indentLevel--;
            EmitLine("};");
        }
    }

    private string TranspileObjectLikeLiteral(List<(Expression Key, Expression Value)> entries)
    {
        var properties = entries.Select(entry =>
            $"[{TranspileExpression(entry.Key)}]: {TranspileExpression(entry.Value)}");
        return "{ " + string.Join(", ", properties) + " }";
    }

    private string TranspileMatchExpression(MatchExpression match)
    {
        var valueVar = $"__matchValue{_matchTempCounter}";
        _matchTempCounter++;

        var builder = new StringBuilder();
        builder.Append("(() => { ");
        builder.Append("const ");
        builder.Append(valueVar);
        builder.Append(" = ");
        builder.Append(TranspileExpression(match.Value));
        builder.Append("; ");

        for (int i = 0; i < match.Cases.Count; i++)
        {
            var matchCase = match.Cases[i];
            var resultVar = $"__matchResult{_matchTempCounter}_{i}";
            builder.Append("{ const ");
            builder.Append(resultVar);
            builder.Append(" = mlRuntime.matchPattern(");
            builder.Append(TranspilePatternDescriptor(matchCase.Pattern));
            builder.Append(", ");
            builder.Append(valueVar);
            builder.Append("); if (");
            builder.Append(resultVar);
            builder.Append(".matched) { ");

            foreach (var binding in CollectPatternBindings(matchCase.Pattern))
            {
                builder.Append("const ");
                builder.Append(EscapeIdentifier(binding));
                builder.Append(" = ");
                builder.Append(resultVar);
                builder.Append(".bindings[\"");
                builder.Append(EscapeString(binding));
                builder.Append("\"]; ");
            }

            builder.Append(TranspileMatchBody(matchCase.Body));
            builder.Append(" } } ");
        }

        if (match.DefaultCase != null)
        {
            builder.Append(TranspileMatchBody(match.DefaultCase));
        }
        else
        {
            builder.Append("throw new Error(\"Match expression had no matching case and no default case.\");");
        }

        builder.Append(" })()");
        return builder.ToString();
    }

    private string TranspileMatchBody(Statement body)
    {
        if (body is ExpressionStatement expressionStatement)
        {
            return "return " + TranspileExpression(expressionStatement.Expression) + ";";
        }

        if (body is BlockStatement blockStatement)
        {
            if (blockStatement.Statements.Count == 0)
            {
                return "return null;";
            }

            var builder = new StringBuilder();
            for (int i = 0; i < blockStatement.Statements.Count - 1; i++)
            {
                builder.Append(TranspileStatementInline(blockStatement.Statements[i]));
                builder.Append(' ');
            }

            var lastStatement = blockStatement.Statements[^1];
            if (lastStatement is ExpressionStatement lastExpressionStatement)
            {
                builder.Append("return ");
                builder.Append(TranspileExpression(lastExpressionStatement.Expression));
                builder.Append(';');
            }
            else
            {
                builder.Append(TranspileStatementInline(lastStatement));
                builder.Append(" return null;");
            }

            return builder.ToString();
        }

        return TranspileStatementInline(body) + " return null;";
    }

    private string TranspileStatementInline(Statement statement)
    {
        var inlineTranspiler = new JsTranspiler();
        inlineTranspiler._indentLevel = 0;
        inlineTranspiler._matchTempCounter = _matchTempCounter;
        inlineTranspiler.TranspileStatement(statement);
        _matchTempCounter = inlineTranspiler._matchTempCounter;
        return NormalizeInlineCode(inlineTranspiler._output.ToString());
    }

    private static string NormalizeInlineCode(string code)
    {
        return string.Join(" ", code
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()));
    }

    private string TranspilePatternDescriptor(Pattern pattern)
    {
        switch (pattern)
        {
            case LiteralPattern literalPattern:
                return "{ type: \"Literal\", value: " + TranspileLiteral(literalPattern.Value) + " }";
            case IdentifierPattern identifierPattern:
                return "{ type: \"Identifier\", name: \"" + EscapeString(identifierPattern.Name) + "\" }";
            case WildcardPattern:
                return "{ type: \"Wildcard\" }";
            case VariantPattern variantPattern:
                return "{ type: \"Variant\", tag: \"" + EscapeString(variantPattern.Tag) + "\", payloadPatterns: [" +
                       string.Join(", ", variantPattern.PayloadPatterns.Select(TranspilePatternDescriptor)) + "] }";
            case ArrayPattern arrayPattern:
                var rest = arrayPattern.Rest == null
                    ? "null"
                    : "{ type: \"Rest\", name: " + (arrayPattern.Rest.Name == null ? "null" : "\"" + EscapeString(arrayPattern.Rest.Name) + "\"") + " }";
                return "{ type: \"Array\", elements: [" +
                       string.Join(", ", arrayPattern.Elements.Select(TranspilePatternDescriptor)) +
                       "], rest: " + rest + " }";
            case ObjectPattern objectPattern:
                return "{ type: \"Object\", properties: [" + string.Join(", ", objectPattern.Properties.Select(prop =>
                    "{ key: \"" + EscapeString(prop.Key) + "\", pattern: " +
                    (prop.Pattern == null ? "null" : TranspilePatternDescriptor(prop.Pattern)) +
                    ", bindingName: " + (prop.BindingName == null ? "null" : "\"" + EscapeString(prop.BindingName) + "\"") + " }")) +
                    "] }";
            case RestPattern restPattern:
                return "{ type: \"Rest\", name: " + (restPattern.Name == null ? "null" : "\"" + EscapeString(restPattern.Name) + "\"") + " }";
            default:
                throw new NotSupportedException($"JavaScript transpilation does not support pattern '{pattern.GetType().Name}'.");
        }
    }

    private static List<string> CollectPatternBindings(Pattern pattern)
    {
        var bindings = new List<string>();
        CollectPatternBindings(pattern, bindings);
        return bindings.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void CollectPatternBindings(Pattern pattern, List<string> bindings)
    {
        switch (pattern)
        {
            case IdentifierPattern identifierPattern:
                bindings.Add(identifierPattern.Name);
                break;
            case VariantPattern variantPattern:
                foreach (var payloadPattern in variantPattern.PayloadPatterns)
                {
                    CollectPatternBindings(payloadPattern, bindings);
                }
                break;
            case ArrayPattern arrayPattern:
                foreach (var element in arrayPattern.Elements)
                {
                    CollectPatternBindings(element, bindings);
                }
                if (arrayPattern.Rest?.Name != null)
                {
                    bindings.Add(arrayPattern.Rest.Name);
                }
                break;
            case ObjectPattern objectPattern:
                foreach (var property in objectPattern.Properties)
                {
                    if (property.Pattern != null)
                    {
                        CollectPatternBindings(property.Pattern, bindings);
                    }
                    else if (property.BindingName != null)
                    {
                        bindings.Add(property.BindingName);
                    }
                }
                break;
            case RestPattern restPattern:
                if (restPattern.Name != null)
                {
                    bindings.Add(restPattern.Name);
                }
                break;
        }
    }

    private string TranspileFunctionCall(FunctionCallExpression functionCall)
    {
        if (functionCall.Callee is MemberAccessExpression stopCall &&
            stopCall.Member == "stop" &&
            functionCall.Arguments.Count == 0)
        {
            return $"mlRuntime.actors.callActorOrVoidStop({TranspileExpression(stopCall.Object)})";
        }

        if (functionCall.Callee is IdentifierExpression identifier &&
            TryTranspileBuiltInCall(identifier.Name, functionCall.Arguments, out var builtInCall))
        {
            return builtInCall;
        }

        if (functionCall.Callee is MemberAccessExpression memberCall)
        {
            if (memberCall.Member == "append" && functionCall.Arguments.Count == 1)
            {
                return $"mlRuntime.arrayAppend({TranspileExpression(memberCall.Object)}, {TranspileExpression(functionCall.Arguments[0])})";
            }

            if (memberCall.Object is IdentifierExpression memberObjectIdentifier)
            {
                if (memberObjectIdentifier.Name == "dom" || memberObjectIdentifier.Name == "game" || memberObjectIdentifier.Name == "three")
                {
                    return $"mlRuntime.{memberObjectIdentifier.Name}.{EscapeIdentifier(memberCall.Member)}({JoinArguments(functionCall.Arguments)})";
                }

                if (TryTranspileVariantStdLibCall(memberObjectIdentifier.Name, memberCall.Member, functionCall.Arguments, out var variantCall))
                {
                    return variantCall;
                }

                if (memberObjectIdentifier.Name == "grounded" && memberCall.Member == "wrap")
                {
                    return $"mlRuntime.grounded.wrap({JoinArguments(functionCall.Arguments)})";
                }

                if (memberObjectIdentifier.Name == "cap")
                {
                    return $"mlRuntime.cap.{EscapeIdentifier(memberCall.Member)}({JoinArguments(functionCall.Arguments)})";
                }
            }

            if (ArrayPipelineMethods.Contains(memberCall.Member))
            {
                var args = string.Join(", ", functionCall.Arguments.Select(TranspileExpression));
                if (args.Length == 0)
                {
                    return $"mlRuntime.callArrayMethod({TranspileExpression(memberCall.Object)}, {TranspileLiteral(memberCall.Member)})";
                }

                return $"mlRuntime.callArrayMethod({TranspileExpression(memberCall.Object)}, {TranspileLiteral(memberCall.Member)}, [{args}])";
            }
        }

        return $"{TranspileExpression(functionCall.Callee)}({JoinArguments(functionCall.Arguments)})";
    }

    private bool TryTranspileBuiltInCall(string name, List<Expression> arguments, out string transpiled)
    {
        transpiled = string.Empty;

        switch (name)
        {
            case "reply":
                transpiled = arguments.Count == 0
                    ? "mlRuntime.actors.reply(null)"
                    : $"mlRuntime.actors.reply({TranspileExpression(arguments[0])})";
                return true;
            case "print":
                transpiled = $"mlRuntime.builtins.print({JoinArguments(arguments)})";
                return true;
            case "println":
                transpiled = $"mlRuntime.builtins.println({JoinArguments(arguments)})";
                return true;
            case "sleep":
                transpiled = $"mlRuntime.builtins.sleep({JoinArguments(arguments)})";
                return true;
            case "string":
                transpiled = arguments.Count == 0
                    ? "mlRuntime.coerceToString(\"\")"
                    : $"mlRuntime.coerceToString({TranspileExpression(arguments[0])})";
                return true;
            case "typeOf":
                transpiled = $"mlRuntime.typeOf({JoinArguments(arguments)})";
                return true;
            case "isTag":
                transpiled = $"mlRuntime.isTag({JoinArguments(arguments)})";
                return true;
            case "isNumber":
                transpiled = $"mlRuntime.isNumber({JoinArguments(arguments)})";
                return true;
            case "all":
                transpiled = $"mlRuntime.all({JoinArguments(arguments)})";
                return true;
            case "range":
                transpiled = $"mlRuntime.rangeBuiltin({JoinArguments(arguments)})";
                return true;
            case "join":
                transpiled = $"mlRuntime.joinBuiltin({JoinArguments(arguments)})";
                return true;
            case "sort":
                transpiled = $"mlRuntime.sortBuiltin({JoinArguments(arguments)})";
                return true;
            case "runProperty":
                transpiled = $"mlRuntime.runProperty(__propertyRegistry, {JoinArguments(arguments)})";
                return true;
            default:
                return false;
        }
    }

    private bool TryTranspileVariantStdLibCall(string moduleName, string memberName, List<Expression> arguments, out string transpiled)
    {
        transpiled = string.Empty;
        if (moduleName is not ("result" or "option"))
        {
            return false;
        }

        transpiled = $"mlRuntime.{moduleName}.{EscapeIdentifier(memberName)}({JoinArguments(arguments)})";
        return true;
    }

    private void TranspileTry(TryStatement tryStatement)
    {
        EmitLine("try {");
        _indentLevel++;
        foreach (var statement in tryStatement.TryBlock.Statements)
        {
            TranspileStatement(statement);
        }
        _indentLevel--;
        EmitLine("}");

        if (tryStatement.CatchClauses.Count == 0 && tryStatement.FinallyBlock == null)
        {
            return;
        }

        EmitLine("catch (__maldaException) {");
        _indentLevel++;

        if (tryStatement.CatchClauses.Count > 0)
        {
            var catchVariable = tryStatement.CatchClauses
                .Select(c => c.ExceptionVariable)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "__maldaCatchValue";
            var catchVariableEscaped = EscapeIdentifier(catchVariable);
            EmitLine($"const {catchVariableEscaped} = mlRuntime.unwrapMaldaException(__maldaException);");

            for (var i = 0; i < tryStatement.CatchClauses.Count; i++)
            {
                var clause = tryStatement.CatchClauses[i];
                var branchKeyword = i == 0 ? "if" : "else if";

                if (clause.Filter != null)
                {
                    EmitLineWithSource(clause.Filter, $"{branchKeyword} (mlRuntime.isTruthy({TranspileExpression(clause.Filter)})) {{");
                }
                else if (i == 0)
                {
                    EmitLine("{");
                }
                else
                {
                    EmitLine("else {");
                }

                _indentLevel++;
                TranspileCatchBody(clause.Body);
                _indentLevel--;
                EmitLine("}");
            }

            if (tryStatement.CatchClauses.All(c => c.Filter != null))
            {
                EmitLine("else { throw __maldaException; }");
            }
        }
        else
        {
            EmitLine("throw __maldaException;");
        }

        _indentLevel--;
        EmitLine("}");

        if (tryStatement.FinallyBlock != null)
        {
            EmitLine("finally {");
            _indentLevel++;
            foreach (var statement in tryStatement.FinallyBlock.Statements)
            {
                TranspileStatement(statement);
            }
            _indentLevel--;
            EmitLine("}");
        }
    }

    private void TranspileCatchBody(BlockStatement body)
    {
        foreach (var statement in body.Statements)
        {
            TranspileStatement(statement);
        }
    }

    private string TranspileTernaryExpression(TernaryExpression ternary)
    {
        var condition = TranspileExpression(ternary.Condition);
        var thenBranch = TranspileExpression(ternary.ThenBranch);
        var elseBranch = TranspileExpression(ternary.ElseBranch);
        return $"(mlRuntime.isTruthy({condition}) ? {thenBranch} : {elseBranch})";
    }

    private string TranspileCondition(Expression expression)
    {
        return $"mlRuntime.isTruthy({TranspileExpression(expression)})";
    }

    private string TranspileUnaryExpression(UnaryExpression unary)
    {
        var right = TranspileExpression(unary.Right);
        return unary.Operator switch
        {
            TokenType.Not => $"(!mlRuntime.isTruthy({right}))",
            TokenType.Minus => $"(-mlRuntime.coerceToFloat({right}))",
            TokenType.Plus => $"(mlRuntime.coerceToFloat({right}))",
            TokenType.Increment => $"(++{right})",
            TokenType.Decrement => $"(--{right})",
            _ => throw new NotSupportedException($"JavaScript transpilation does not support unary operator '{unary.Operator}'.")
        };
    }

    private string TranspileBinaryExpression(BinaryExpression binary)
    {
        var left = TranspileExpression(binary.Left);
        var right = TranspileExpression(binary.Right);

        return binary.Operator switch
        {
            TokenType.Equal => $"mlRuntime.equals({left}, {right})",
            TokenType.NotEqual => $"(!mlRuntime.equals({left}, {right}))",
            TokenType.And => $"(mlRuntime.isTruthy({left}) && mlRuntime.isTruthy({right}))",
            TokenType.Or => $"(mlRuntime.isTruthy({left}) || mlRuntime.isTruthy({right}))",
            TokenType.NullCoalesce => $"mlRuntime.nullCoalesce({left}, () => {right})",
            TokenType.Minus => $"(mlRuntime.coerceToFloat({left}) - mlRuntime.coerceToFloat({right}))",
            TokenType.Multiply => $"(mlRuntime.coerceToFloat({left}) * mlRuntime.coerceToFloat({right}))",
            TokenType.Divide => $"(mlRuntime.coerceToFloat({left}) / mlRuntime.coerceToFloat({right}))",
            TokenType.Modulo => $"(mlRuntime.coerceToFloat({left}) % mlRuntime.coerceToFloat({right}))",
            TokenType.LessThan => $"(mlRuntime.coerceToFloat({left}) < mlRuntime.coerceToFloat({right}))",
            TokenType.GreaterThan => $"(mlRuntime.coerceToFloat({left}) > mlRuntime.coerceToFloat({right}))",
            TokenType.LessThanOrEqual => $"(mlRuntime.coerceToFloat({left}) <= mlRuntime.coerceToFloat({right}))",
            TokenType.GreaterThanOrEqual => $"(mlRuntime.coerceToFloat({left}) >= mlRuntime.coerceToFloat({right}))",
            TokenType.Plus => $"({left} + {right})",
            _ => throw new NotSupportedException($"JavaScript transpilation does not support binary operator '{binary.Operator}'.")
        };
    }

    private string JoinArguments(List<Expression> arguments)
    {
        return string.Join(", ", arguments.Select(TranspileExpression));
    }

    private static string TranspileLiteral(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        return value switch
        {
            bool booleanValue => booleanValue ? "true" : "false",
            string stringValue => $"\"{EscapeString(stringValue)}\"",
            int intValue => intValue.ToString(CultureInfo.InvariantCulture),
            long longValue => longValue.ToString(CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString(CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"JavaScript transpilation for literal '{value.GetType().Name}' is not supported yet.")
        };
    }

    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static string EscapeIdentifier(string value)
    {
        return value;
    }

    private static string TranspileAssignmentTarget(Expression expression)
    {
        return expression switch
        {
            IdentifierExpression identifier => EscapeIdentifier(identifier.Name),
            ThisExpression => "this",
            MemberAccessExpression memberAccess => $"{TranspileAssignmentTarget(memberAccess.Object)}.{EscapeIdentifier(memberAccess.Member)}",
            ArrayAccessExpression arrayAccess => $"{TranspileExpressionStatic(arrayAccess.Array)}[{TranspileExpressionStatic(arrayAccess.Index)}]",
            _ => throw new NotSupportedException($"JavaScript transpilation for assignment target '{expression.GetType().Name}' is not supported yet.")
        };
    }

    private static string TranspileExpressionStatic(Expression expression)
    {
        return expression switch
        {
            IdentifierExpression identifier => EscapeIdentifier(identifier.Name),
            ThisExpression => "this",
            LiteralExpression literal => TranspileLiteral(literal.Value),
            MemberAccessExpression memberAccess => $"{TranspileExpressionStatic(memberAccess.Object)}.{EscapeIdentifier(memberAccess.Member)}",
            ArrayAccessExpression arrayAccess => $"{TranspileExpressionStatic(arrayAccess.Array)}[{TranspileExpressionStatic(arrayAccess.Index)}]",
            _ => throw new NotSupportedException($"JavaScript transpilation for expression '{expression.GetType().Name}' in assignment target is not supported yet.")
        };
    }

    private static string MapAssignmentOperator(TokenType tokenType)
    {
        return tokenType switch
        {
            TokenType.Assign => "=",
            TokenType.PlusAssign => "+=",
            TokenType.MinusAssign => "-=",
            TokenType.MultiplyAssign => "*=",
            TokenType.DivideAssign => "/=",
            _ => throw new NotSupportedException($"JavaScript transpilation does not support assignment operator '{tokenType}'.")
        };
    }

    private static string MapPostfixOperator(TokenType tokenType)
    {
        return tokenType switch
        {
            TokenType.Increment => "++",
            TokenType.Decrement => "--",
            _ => throw new NotSupportedException($"JavaScript transpilation does not support postfix operator '{tokenType}'.")
        };
    }

    private string TranspileForInitializer(Statement? initializer)
    {
        if (initializer == null)
        {
            return string.Empty;
        }

        return initializer switch
        {
            VarDeclStatement varDecl => $"let {EscapeIdentifier(varDecl.Name)} = {TranspileExpression(varDecl.Initializer)}",
            ExpressionStatement expressionStatement => TranspileExpression(expressionStatement.Expression),
            _ => throw new NotSupportedException($"JavaScript transpilation does not support for-loop initializer '{initializer.GetType().Name}'.")
        };
    }

    private string TranspileForCondition(Expression? condition)
    {
        return condition == null ? string.Empty : TranspileExpression(condition);
    }

    private string TranspileForIncrement(Expression? increment)
    {
        return increment == null ? string.Empty : TranspileExpression(increment);
    }

    private string TranspileSpawnExpression(SpawnExpression spawn)
    {
        var args = string.Join(", ", spawn.Arguments.Select(TranspileExpression));
        var actorName = EscapeIdentifier(spawn.ActorName);
        if (args.Length == 0)
        {
            return $"mlRuntime.actors.spawn(new {actorName}())";
        }

        return $"mlRuntime.actors.spawn(new {actorName}({args}))";
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

        foreach (var member in actor.Members)
        {
            TranspileActorMember(member, actor.Name);
        }

        _indentLevel--;
        EmitLine("}");
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
                EmitLine($"{staticPrefix}async {EscapeIdentifier(member.Name)}({parameters}) {{");
                _indentLevel++;
                var previousInActorHandler = _isInActorHandler;
                _isInActorHandler = true;
                foreach (var statement in method.Body.Statements)
                {
                    TranspileStatement(statement);
                }
                _isInActorHandler = previousInActorHandler;
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
                // JavaScript constructors cannot be async.
                EmitLine($"constructor({parameters}) {{");
                _indentLevel++;
                foreach (var statement in constructor.Body.Statements)
                {
                    TranspileStatement(statement);
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
            foreach (var statement in callback.Body.Statements)
            {
                TranspileStatement(statement);
            }
            _indentLevel--;
            EmitLine("};");

            if (send.TimeoutErrorHandler != null)
            {
                var errorHandler = send.TimeoutErrorHandler;
                EmitLine($"const __timeoutErrorHandler = async ({EscapeIdentifier(errorHandler.ParameterName)}Arg) => {{");
                _indentLevel++;
                EmitLine($"let {EscapeIdentifier(errorHandler.ParameterName)} = {EscapeIdentifier(errorHandler.ParameterName)}Arg;");
                foreach (var statement in errorHandler.Body.Statements)
                {
                    TranspileStatement(statement);
                }
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
                match.Cases.Any(c => StatementRequiresAsync(c.Body)) ||
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

    private void EmitLine(string text)
    {
        if (text.Length == 0)
        {
            _output.AppendLine();
            _generatedLine++;
            return;
        }

        if (_currentSourceLine.HasValue && _currentSourceLine.Value > 0)
        {
            _mappings.Add(new SourceMappingEntry(
                _generatedLine,
                GeneratedColumn: 0,
                SourceLine: _currentSourceLine.Value,
                SourceColumn: Math.Max(0, (_currentSourceColumn ?? 1) - 1)));
        }

        _output.Append(new string(' ', _indentLevel * 4));
        _output.AppendLine(text);
        _generatedLine++;
    }

    private void EmitLineWithSource(Node? node, string text)
    {
        WithSource(node, () => EmitLine(text));
    }

    private void WithSource(Node? node, Action action)
    {
        var previousLine = _currentSourceLine;
        var previousColumn = _currentSourceColumn;
        if (node != null && node.Line > 0)
        {
            _currentSourceLine = node.Line;
            _currentSourceColumn = node.Column <= 0 ? 1 : node.Column;
        }

        action();
        _currentSourceLine = previousLine;
        _currentSourceColumn = previousColumn;
    }

    private string? BuildSourceMapJson(string? sourceFilePath, string? sourceContent, string? generatedFileName)
    {
        var sourceName = string.IsNullOrWhiteSpace(sourceFilePath)
            ? "source.malda"
            : sourceFilePath.Replace('\\', '/');
        var fileName = string.IsNullOrWhiteSpace(generatedFileName) ? "program.js" : generatedFileName;

        var mapObject = new
        {
            version = 3,
            file = fileName,
            sources = new[] { sourceName },
            sourcesContent = sourceContent != null ? new[] { sourceContent } : Array.Empty<string>(),
            names = Array.Empty<string>(),
            mappings = BuildMappingsField()
        };

        return JsonSerializer.Serialize(mapObject);
    }

    private string BuildMappingsField()
    {
        var mappingsByLine = _mappings
            .GroupBy(m => m.GeneratedLine)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.GeneratedColumn).ToList());

        var lineCount = _generatedLine;
        var builder = new StringBuilder();
        var previousSourceIndex = 0;
        var previousSourceLine = 0;
        var previousSourceColumn = 0;

        for (int line = 1; line <= lineCount; line++)
        {
            if (line > 1)
            {
                builder.Append(';');
            }

            if (!mappingsByLine.TryGetValue(line, out var lineMappings))
            {
                continue;
            }

            var previousGeneratedColumn = 0;
            for (int i = 0; i < lineMappings.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                var mapping = lineMappings[i];
                builder.Append(EncodeVlq(mapping.GeneratedColumn - previousGeneratedColumn));
                previousGeneratedColumn = mapping.GeneratedColumn;

                // single source file only
                builder.Append(EncodeVlq(0 - previousSourceIndex));
                previousSourceIndex = 0;

                var zeroBasedSourceLine = mapping.SourceLine - 1;
                builder.Append(EncodeVlq(zeroBasedSourceLine - previousSourceLine));
                previousSourceLine = zeroBasedSourceLine;

                builder.Append(EncodeVlq(mapping.SourceColumn - previousSourceColumn));
                previousSourceColumn = mapping.SourceColumn;
            }
        }

        return builder.ToString();
    }

    private static string EncodeVlq(int value)
    {
        var vlq = ToVlqSigned(value);
        var output = new StringBuilder();

        do
        {
            var digit = vlq & 31;
            vlq >>= 5;
            if (vlq > 0)
            {
                digit |= 32;
            }

            output.Append(Base64Digit(digit));
        } while (vlq > 0);

        return output.ToString();
    }

    private static int ToVlqSigned(int value)
    {
        return value < 0 ? ((-value) << 1) + 1 : (value << 1);
    }

    private static char Base64Digit(int value)
    {
        const string digits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        return digits[value];
    }

    private readonly record struct SourceMappingEntry(
        int GeneratedLine,
        int GeneratedColumn,
        int SourceLine,
        int SourceColumn);
}
