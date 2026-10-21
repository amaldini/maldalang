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

public partial class JsTranspiler
{
    private readonly StringBuilder _output;
    private int _indentLevel;
    private int _matchTempCounter;
    private bool _isInActorHandler;
    private readonly List<SourceMappingEntry> _mappings;
    private readonly Stack<Statement?> _desugaredForLoopIncrements = new();
    private readonly HashSet<string> _asyncFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _variantConstructorArities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionDeclaration> _shaderFunctions = new(StringComparer.Ordinal);
    private readonly Stack<HashSet<string>> _localScopes = new();
    private readonly HashSet<string> _moduleConstNames = new(StringComparer.Ordinal);
    private readonly Stack<HashSet<string>> _constScopeStack = new();
    private readonly Dictionary<string, ClassDeclaration> _classDeclarations = new(StringComparer.Ordinal);
    private string? _currentClassName;
    /// <summary>Actor being emitted: its fields and methods resolve through <c>this</c> like class members.</summary>
    private ActorDeclaration? _currentActor;
    /// <summary>Methods of <see cref="_currentActor"/> that must be emitted <c>async</c>.</summary>
    private readonly HashSet<string> _asyncActorMethods = new(StringComparer.Ordinal);
    private bool _currentMemberIsStatic;
    private bool _rewriteImplicitMembers;

    private static readonly HashSet<string> JsRuntimeModules = new(StringComparer.Ordinal)
    {
        "dom", "game", "three", "math", "Math", "str", "io", "nn",
        "result", "option", "grounded", "cap", "agents", "schema"
    };

    private static readonly HashSet<string> AsyncBuiltInNames = new(StringComparer.Ordinal)
    {
        "sleep", "runProperty", "receive",
        "httpGet", "httpPost", "httpPut", "httpDelete", "httpPatch"
    };
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

    private void CopyTranspileStateFrom(JsTranspiler other)
    {
        _variantConstructorArities.Clear();
        foreach (var kv in other._variantConstructorArities)
            _variantConstructorArities[kv.Key] = kv.Value;
        _asyncFunctions.Clear();
        foreach (var name in other._asyncFunctions)
            _asyncFunctions.Add(name);
        _shaderFunctions.Clear();
        foreach (var kv in other._shaderFunctions)
            _shaderFunctions[kv.Key] = kv.Value;
        CopyLocalScopesFrom(other);
        CopyConstScopesFrom(other);
        _classDeclarations.Clear();
        foreach (var kv in other._classDeclarations)
            _classDeclarations[kv.Key] = kv.Value;
        _currentClassName = other._currentClassName;
        _currentActor = other._currentActor;
        _asyncActorMethods.Clear();
        _asyncActorMethods.UnionWith(other._asyncActorMethods);
        _currentMemberIsStatic = other._currentMemberIsStatic;
        _rewriteImplicitMembers = other._rewriteImplicitMembers;
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
        _variantConstructorArities.Clear();
        _shaderFunctions.Clear();
        ResetLocalScopes();
        ResetConstScopes();
        _classDeclarations.Clear();
        _currentClassName = null;
        _currentActor = null;
        _currentMemberIsStatic = false;
        _rewriteImplicitMembers = false;
        _generatedLine = 1;
        _currentSourceLine = null;
        _currentSourceColumn = null;

        // User bindings shadow flat built-ins of the same name (same rule as the interpreter):
        // marks IdentifierExpression.ShadowsBuiltIn, read by the call emitters.
        BuiltInShadowAnalysis.Analyze(statements);

        var functions = new List<FunctionDeclaration>();
        var typeDeclarations = new List<TypeDeclaration>();
        var schemaDeclarations = new List<SchemaDeclaration>();
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
                foreach (var ctor in typeDeclaration.Constructors)
                    _variantConstructorArities[ctor.Name] = ctor.ParameterNames.Count;
            }
            else if (statement is SchemaDeclaration schemaDeclaration)
            {
                schemaDeclarations.Add(schemaDeclaration);
            }
            else if (statement is ActorDeclaration actorDeclaration)
            {
                actorDeclarations.Add(actorDeclaration);
            }
            else if (statement is ClassDeclaration classDeclaration)
            {
                classDeclarations.Add(classDeclaration);
                _classDeclarations[classDeclaration.Name] = classDeclaration;
            }
            else if (statement is PropertyDeclaration propertyDeclaration)
            {
                propertyDeclarations.Add(propertyDeclaration);
            }
            else if (statement is ImportStatement or UsingStatement)
            {
                // File imports are inlined before transpile; leftover import/using are no-ops.
            }
            else
            {
                topLevelStatements.Add(statement);
                if (statement is VarDeclStatement topLevelVar && topLevelVar.IsConst)
                    _moduleConstNames.Add(topLevelVar.Name);
            }
        }

        CollectShaderFunctions(statements);

        EmitLine("const MaldaApp = (() => {");
        _indentLevel++;

        EmitLine("if (typeof globalThis.mlRuntime === \"undefined\") {");
        _indentLevel++;
        EmitLine("throw new Error(\"mlRuntime is not available. Include malda-js-runtime.js before running generated MALDA JavaScript.\");");
        _indentLevel--;
        EmitLine("}");
        EmitLine("const mlRuntime = globalThis.mlRuntime;");
        // Hot helpers behind +, $"{…}", string(), a[i], / and %: a local binding keeps V8 from a
        // property lookup on the large runtime object at every call.
        EmitLine("const __maldaAdd = mlRuntime.add, __maldaFormat = mlRuntime.formatValue;");
        EmitLine("const __maldaGetIndex = mlRuntime.getIndex, __maldaSetIndex = mlRuntime.setIndex, __maldaDivide = mlRuntime.divide, __maldaModulo = mlRuntime.modulo;");
        EmitLine(string.Empty);

        foreach (var typeDeclaration in typeDeclarations)
        {
            EmitVariantConstructors(typeDeclaration);
        }

        if (typeDeclarations.Count > 0)
        {
            EmitLine(string.Empty);
        }

        foreach (var schemaDeclaration in schemaDeclarations)
        {
            EmitSchemaRegistration(schemaDeclaration);
        }

        if (schemaDeclarations.Count > 0)
        {
            EmitLine(string.Empty);
        }

        // A script-level var that a top-level function, class or actor uses must live beside
        // them, not as a local of main(), or the reference throws ReferenceError (the
        // interpreter and C# both share it). The others stay main() locals.
        var namesUsedByDeclarations = new HashSet<string>(
            functions.Where(f => !ShaderFunction.IsMarked(f)).Cast<Node>().Concat(classDeclarations).Concat(actorDeclarations).Concat(propertyDeclarations)
                .SelectMany(declaration => AstNodeWalker.Descendants(declaration))
                .OfType<IdentifierExpression>()
                .Select(identifier => identifier.Name),
            StringComparer.Ordinal);
        var moduleVarNames = topLevelStatements
            .OfType<VarDeclStatement>()
            .Select(v => v.Name)
            .Where(name => namesUsedByDeclarations.Contains(name) && name != "main" && name != "mlRuntime")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (moduleVarNames.Count > 0)
        {
            EmitLine($"let {string.Join(", ", moduleVarNames.Select(EscapeIdentifier))};");
            EmitLine(string.Empty);
        }

        foreach (var function in functions)
        {
            if (ShaderFunction.IsMarked(function))
                continue;

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
        InLocalScope(Array.Empty<string>(), () =>
        {
            var hoisted = new HashSet<string>(moduleVarNames, StringComparer.Ordinal);
            foreach (var statement in topLevelStatements)
            {
                if (statement is VarDeclStatement moduleVar && hoisted.Contains(moduleVar.Name))
                {
                    WithSource(moduleVar, () =>
                    {
                        EmitLineWithSource(moduleVar.Initializer, $"{EscapeIdentifier(moduleVar.Name)} = {TranspileExpressionAwaited(moduleVar.Initializer)};");
                        DeclareLocal(moduleVar.Name);
                        if (moduleVar.IsConst)
                            RegisterConstBinding(moduleVar.Name);
                    });
                    continue;
                }

                TranspileStatement(statement);
            }
        });
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
        if (ShaderFunction.IsMarked(declaration))
            return;

        var parameters = string.Join(", ", declaration.Parameters.Select(EscapeIdentifier));
        var withinMs = DeclarationBounds.TryGetWithinTimeoutMs(declaration);
        var needsAsync = StatementRequiresAsync(declaration.Body) || withinMs is > 0;
        if (needsAsync)
        {
            _asyncFunctions.Add(declaration.Name);
        }

        var asyncPrefix = needsAsync ? "async " : string.Empty;
        EmitLine($"{asyncPrefix}function {EscapeIdentifier(declaration.Name)}({parameters}) {{");
        _indentLevel++;
        InLocalScope(declaration.Parameters, () =>
        {
            if (withinMs is > 0)
            {
                var runner = needsAsync ? "async () =>" : "() =>";
                EmitLine($"return mlRuntime.within.run({withinMs.Value}, {TranspileLiteral(declaration.Name)}, {runner} {{");
                _indentLevel++;
                TranspileStatementsWithOptionalDeferFrame(declaration.Body.Statements);
                _indentLevel--;
                EmitLine("});");
            }
            else
            {
                TranspileStatementsWithOptionalDeferFrame(declaration.Body.Statements);
            }
        });
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
                    DeclareLocal(varDecl.Name);
                    if (varDecl.IsConst)
                        RegisterConstBinding(varDecl.Name);
                    break;
                case DestructuringVarDecl destVarDecl:
                    TranspileDestructuringVarDecl(destVarDecl);
                    break;
                case AssignmentStatement assignment:
                    EmitLineWithSource(assignment.Value, TranspileAssignment(assignment));
                    break;
                case DestructuringAssignment destAssign:
                    TranspileDestructuringAssignment(destAssign);
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
                    _desugaredForLoopIncrements.Push(whileStatement.DesugaredForIncrement);
                    EmitLineWithSource(whileStatement.Condition, $"while ({TranspileCondition(whileStatement.Condition)})");
                    TranspileEmbeddedStatement(whileStatement.Body);
                    _desugaredForLoopIncrements.Pop();
                    break;
                case ForStatement forStatement:
                    EmitLineWithSource(forStatement.Condition ?? (Node?)forStatement.Increment ?? forStatement.Initializer, $"for ({TranspileForInitializer(forStatement.Initializer)}; {TranspileForCondition(forStatement.Condition)}; {TranspileForIncrement(forStatement.Increment)})");
                    var forLocals = forStatement.Initializer is VarDeclStatement forVar
                        ? new[] { forVar.Name }
                        : Array.Empty<string>();
                    WithOwnContinue(() => InLocalScope(forLocals, () => TranspileEmbeddedStatement(forStatement.Body)));
                    break;
                case FunctionDeclaration declaration:
                    if (!ShaderFunction.IsMarked(declaration))
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
                {
                    // `for (const f of [f(1)])` reads the loop binding of `f`, still uninitialized,
                    // while evaluating the collection; MALDA reads the outer `f` there. Evaluate a
                    // collection that names the loop variable before the loop.
                    var collection = TranspileExpression(forInStatement.Collection);
                    var shadowsCollection = AstNodeWalker.Descendants(forInStatement.Collection)
                        .OfType<IdentifierExpression>()
                        .Any(identifier => identifier.Name == forInStatement.VariableName);
                    if (shadowsCollection)
                    {
                        var temp = $"__forInCollection_{++_matchTempCounter}";
                        EmitLine("{");
                        _indentLevel++;
                        EmitLineWithSource(forInStatement.Collection, $"const {temp} = {collection};");
                        collection = temp;
                    }
                    EmitLineWithSource(forInStatement.Collection, $"for (const {EscapeIdentifier(forInStatement.VariableName)} of {collection})");
                    WithOwnContinue(() => InLocalScope([forInStatement.VariableName], () => TranspileEmbeddedStatement(forInStatement.Body)));
                    if (shadowsCollection)
                    {
                        _indentLevel--;
                        EmitLine("}");
                    }
                    break;
                }
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
                case SchemaDeclaration:
                    // Schema declarations are registered at module scope.
                    break;
                case ImportStatement:
                case UsingStatement:
                    break;
                case PromptDeclaration:
                    throw new NotSupportedException(
                        "prompt declarations are host-only (interpreter and C# transpile). JavaScript does not support prompts.");
                case WorkflowDeclaration:
                    throw new NotSupportedException(
                        "workflow declarations are host-only (interpreter and C# transpile). JavaScript does not support workflows.");
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
                    InLocalScope([usingResource.VariableName], () =>
                    {
                        TranspileStatementsWithOptionalDeferFrame(usingResource.Body.Statements);
                    });
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

    private string TranspileExpression(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return TranspileLiteral(literal.Value);
            case IdentifierExpression identifier:
                return TranspileIdentifierReference(identifier.Name);
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
                    IsUnshadowedRuntimeModule(objectIdentifier.Name))
                {
                    return $"mlRuntime.{MapRuntimeModuleName(objectIdentifier.Name)}.{EscapeIdentifier(memberAccess.Member)}";
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
                return $"__maldaGetIndex({TranspileExpression(arrayAccess.Array)}, {TranspileExpression(arrayAccess.Index)})";
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
                return "mlRuntime.actors.receiveAsync()";
            case AwaitExpression awaitExpression:
                return $"(await {TranspileExpression(awaitExpression.Expression)})";
            case AsyncExpression asyncExpression:
                return $"(async () => ({TranspileExpression(asyncExpression.Expression)}))()";
            case ThisExpression:
                return "this";
            case SuperExpression:
                return "super";
            case InterpolatedStringExpression interpolated:
                return TranspileInterpolatedString(interpolated);
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
        // `var t = async f();` stores the task; awaiting it here would run f to completion
        // (and throw its error) before `await t` / all(t, ...) ever see it.
        if (expression is AsyncExpression)
            return transpiled;
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
        InLocalScope(propertyDeclaration.Parameters, () =>
        {
            TranspileStatementsWithOptionalDeferFrame(propertyDeclaration.Body.Statements);
        });
        _indentLevel--;
        EmitLine("}");
    }

    private void TranspileClassDeclaration(ClassDeclaration classDeclaration)
    {
        var extendsClause = string.IsNullOrEmpty(classDeclaration.Superclass)
            ? string.Empty
            : $" extends {EscapeIdentifier(classDeclaration.Superclass)}";
        EmitLine($"class {EscapeIdentifier(classDeclaration.Name)}{extendsClause} {{");
        _indentLevel++;
        var previousClassName = _currentClassName;
        _currentClassName = classDeclaration.Name;
        try
        {
            foreach (var member in classDeclaration.Members)
            {
                TranspileClassMember(member);
            }
        }
        finally
        {
            _currentClassName = previousClassName;
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
                var previousStatic = _currentMemberIsStatic;
                var previousRewrite = _rewriteImplicitMembers;
                _currentMemberIsStatic = member.IsStatic;
                _rewriteImplicitMembers = true;
                try
                {
                    InLocalScope(method.Parameters, () =>
                    {
                        TranspileStatementsWithOptionalDeferFrame(method.Body.Statements);
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
            case MemberType.Constructor:
            {
                if (member.Value is not FunctionDeclaration constructor)
                {
                    return;
                }

                var parameters = string.Join(", ", constructor.Parameters.Select(EscapeIdentifier));
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

    private void EmitSchemaRegistration(SchemaDeclaration schema)
    {
        var fields = schema.Fields.Select(field => new
        {
            name = field.Name,
            type = field.TypeName,
            required = field.Required
        });
        var json = JsonSerializer.Serialize(fields);
        EmitLine($"mlRuntime.schema.register({TranspileLiteral(schema.Name)}, {json});");
    }

    private void TranspileDestructuringVarDecl(DestructuringVarDecl stmt)
    {
        var temp = $"__destructureValue_{++_matchTempCounter}";
        EmitLineWithSource(stmt.Initializer, $"const {temp} = {TranspileExpressionAwaited(stmt.Initializer)};");
        TranspileDestructuringPattern(stmt.Pattern, temp, declare: true);
    }

    private void TranspileDestructuringAssignment(DestructuringAssignment stmt)
    {
        var temp = $"__destructureValue_{++_matchTempCounter}";
        EmitLineWithSource(stmt.Value, $"const {temp} = {TranspileExpressionAwaited(stmt.Value)};");
        TranspileDestructuringPattern(stmt.Pattern, temp, declare: false);
    }

    private void TranspileDestructuringPattern(DestructuringPattern pattern, string valueExpr, bool declare)
    {
        var prefix = declare ? "let " : string.Empty;
        if (pattern is ArrayDestructuringPattern arrayPattern)
        {
            var arrTemp = $"__destructureArr_{_matchTempCounter}";
            var required = arrayPattern.Elements.Count;
            var comparison = arrayPattern.Rest != null ? "<" : "!==";
            EmitLine($"const {arrTemp} = mlRuntime.getArray({valueExpr});");
            EmitLine($"if ({arrTemp}.length {comparison} {required}) {{ mlRuntime.throwMalda(\"Destructuring pattern did not match value.\"); }}");
            for (var i = 0; i < arrayPattern.Elements.Count; i++)
            {
                var element = arrayPattern.Elements[i];
                if (element is IdentifierPattern idPattern)
                {
                    EmitLine($"{prefix}{EscapeIdentifier(idPattern.Name)} = {arrTemp}[{i}];");
                    if (declare)
                    {
                        DeclareLocal(idPattern.Name);
                    }
                }
                else if (element is DestructuringPattern nestedPattern)
                {
                    var nestedTemp = $"__destructureNested_{++_matchTempCounter}";
                    EmitLine($"const {nestedTemp} = {arrTemp}[{i}];");
                    TranspileDestructuringPattern(nestedPattern, nestedTemp, declare);
                }
            }

            if (arrayPattern.Rest?.Name != null)
            {
                EmitLine($"{prefix}{EscapeIdentifier(arrayPattern.Rest.Name)} = {arrTemp}.slice({required});");
                if (declare)
                {
                    DeclareLocal(arrayPattern.Rest.Name);
                }
            }

            return;
        }

        if (pattern is ObjectDestructuringPattern objectPattern)
        {
            EmitLine($"if (!mlRuntime.isObject({valueExpr})) {{ mlRuntime.throwMalda(\"Destructuring pattern did not match value.\"); }}");
            foreach (var prop in objectPattern.Properties)
            {
                var keyLiteral = TranspileLiteral(prop.Key);
                EmitLine($"if (!mlRuntime.objectHasKey({valueExpr}, {keyLiteral})) {{ mlRuntime.throwMalda(\"Destructuring pattern did not match value.\"); }}");
                if (prop.Pattern is DestructuringPattern nestedPattern)
                {
                    var nestedTemp = $"__destructureNested_{++_matchTempCounter}";
                    EmitLine($"const {nestedTemp} = {valueExpr}[{keyLiteral}];");
                    TranspileDestructuringPattern(nestedPattern, nestedTemp, declare);
                }
                else
                {
                    var varName = prop.BindingName
                        ?? (prop.Pattern is IdentifierPattern identifier ? identifier.Name : prop.Key);
                    EmitLine($"{prefix}{EscapeIdentifier(varName)} = {valueExpr}[{keyLiteral}];");
                    if (declare)
                    {
                        DeclareLocal(varName);
                    }
                }
            }
        }
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
            DeclareLocal(catchVariable);

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
}
