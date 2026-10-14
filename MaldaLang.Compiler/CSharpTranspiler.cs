// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;

using MaldaLang.Compiler.OptionalPack;

namespace MaldaLang.Compiler;

public partial class CSharpTranspiler
{
    private enum TranspiledClrType
    {
        Object,
        Double,
        DoubleArray,
        /// <summary>Only from <see cref="NumericVariableInference"/>; MALDA int hints stay boxed.</summary>
        Int
    }

    private readonly StringBuilder _output;
    private readonly AstDeferScan _deferScan = new();
    private int _indentLevel;
    private readonly HashSet<string> _usedNamespaces;
    private readonly HashSet<string> _generatedAttributeClasses;
    private readonly HashSet<string> _functionNames;
    private readonly HashSet<string> _promptNames;
    private readonly HashSet<string> _variantConstructorNames;
    private readonly Dictionary<string, ClassDeclaration> _classDeclarations;
    private readonly List<TypeDeclaration> _typeDeclarations;
    private readonly List<ApiDeclaration> _apiDeclarations;
    private readonly List<ContextDeclaration> _contextDeclarations;
    private bool _isInWorkflowBody;
    private bool _isInActorHandler;
    /// <summary>Instance methods of the actor being emitted; bare calls to them are direct sync calls.</summary>
    private HashSet<string>? _currentActorMethodNames;
    private bool _transpileCallAsTask;
    private bool _canAwait;
    private bool _emitLineDirectives;
    private string? _sourceFilePath;
    private readonly ProfilingOptions? _profilingOptions;
    private int _profileTempCounter;
    private int _matchBindCounter;
    private int _destructureTempCounter;
    /// <summary>Innermost loop on top; null for a loop whose <c>continue</c> is plain C#.</summary>
    private readonly Stack<string?> _desugaredForContinueLabels = new();
    private readonly Stack<Dictionary<string, TranspiledClrType>> _typedScopeStack;
    private readonly Stack<HashSet<string>> _constScopeStack;
    private readonly HashSet<string> _moduleConstNames;
    private readonly Dictionary<string, TranspiledClrType> _functionReturnTypes;
    private readonly Dictionary<string, IReadOnlyList<TranspiledClrType>> _functionParameterTypes;
    /// <summary>
    /// Block-scoped <c>function</c> declarations, innermost frame first.
    /// Top-level functions stay in <see cref="_functionNames"/>.
    /// </summary>
    private readonly Stack<Dictionary<string, IReadOnlyList<TranspiledClrType>>> _localFunctionStack = new();
    private readonly Stack<TranspiledClrType> _currentFunctionReturnType;
    /// <summary>
    /// Name of the MALDA class whose member body is currently being transpiled, or null
    /// at top level. Used to emit the accessing-class argument for private-member checks.
    /// </summary>
    private string? _currentClassName;
    /// <summary>True while the member body being transpiled is a static member.</summary>
    private bool _currentMemberIsStatic;
    private readonly int _typedTranspileLevel;
    private string? _catchFilterRenameFrom;
    /// <summary>
    /// Top-level functions that also get a synchronous twin (<c>name__Sync</c>) used by direct
    /// calls. Null until the first pass computes the candidates; functions whose twin turned
    /// out to need an await are dropped and the program is transpiled again.
    /// </summary>
    private HashSet<string>? _syncTwinFunctions;
    private int _directMethodCallCounter;
    /// <summary>Untyped vars that are ints or floats on every path (see <see cref="NumericVariableInference"/>).</summary>
    private NumericVariableInference.Result _inferredNumerics = NumericVariableInference.Result.Empty();
    /// <summary>Top-level inferred vars, emitted as <c>int</c>/<c>double</c> fields.</summary>
    private Dictionary<string, TranspiledClrType> _inferredGlobalTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _syncTwinRejected = new(StringComparer.Ordinal);
    /// <summary>Candidate → top-level functions that call it directly (reverse call graph).</summary>
    private Dictionary<string, HashSet<string>> _syncTwinCallers = new(StringComparer.Ordinal);

    /// <summary>Parameter and return types of a <c>name__Typed</c> function variant.</summary>
    private sealed record TypedTwinSignature(TranspiledClrType[] Parameters, TranspiledClrType Return);

    /// <summary>
    /// Sync-twin functions that also get <c>name__Typed</c>, a copy with int/double parameters
    /// and/or return (see <see cref="ComputeTypedTwins"/>). A direct call uses it only when
    /// every argument is already of the parameter's C# type.
    /// </summary>
    private Dictionary<string, TypedTwinSignature> _typedTwins = new(StringComparer.Ordinal);
    private readonly HashSet<string> _typedTwinRejected = new(StringComparer.Ordinal);
    private bool _typedTwinRejectedThisPass;
    /// <summary>
    /// Inferred int/double locals per function, method and constructor body (see
    /// <see cref="NumericVariableInference.InferLocals"/>): for the untyped versions, and for the
    /// <c>__Typed</c> variant where typed parameters can make more locals numeric.
    /// </summary>
    private Dictionary<FunctionDeclaration, NumericVariableInference.LocalResult> _untypedLocals = new(ReferenceEqualityComparer.Instance);
    private Dictionary<FunctionDeclaration, NumericVariableInference.LocalResult> _typedLocals = new(ReferenceEqualityComparer.Instance);
    /// <summary>The locals of the body being emitted.</summary>
    private NumericVariableInference.LocalResult _activeLocals = NumericVariableInference.LocalResult.Empty();
    /// <summary>Member call names with built-in handling ahead of user methods.</summary>
    private static readonly HashSet<string> BuiltInMemberMethodNames = new(StringComparer.Ordinal)
    {
        "append", "pop", "shift", "concat", "except", "popOrNull", "shiftOrNull", "get", "at",
        "map", "filter", "reduce", "forEach", "find", "findIndex", "some", "every",
        "sort", "reverse", "slice", "indexOf", "includes", "join", "sum", "average", "min", "max",
        "start", "stop", "getRoutes", "clearCache", "generate"
    };
    /// <summary>Classes whose name only ever means the class, so <c>Class.m(...)</c> may call <c>m__Typed</c>.</summary>
    private HashSet<string> _typedStaticReceivers = new(StringComparer.Ordinal);
    /// <summary>Typed parameters of the <c>__Typed</c> body being emitted (for coercion checks).</summary>
    private readonly HashSet<string> _currentTypedIntParams = new(StringComparer.Ordinal);
    private readonly HashSet<string> _currentTypedDoubleParams = new(StringComparer.Ordinal);
    private string? _catchFilterRenameTo;

    public CSharpTranspiler(ProfilingOptions? profilingOptions = null, int typedTranspileLevel = 1)
    {
        _output = new StringBuilder();
        _indentLevel = 0;
        _usedNamespaces = new HashSet<string>();
        _generatedAttributeClasses = new HashSet<string>();
        _functionNames = new HashSet<string>();
        _promptNames = new HashSet<string>();
        _variantConstructorNames = new HashSet<string>();
        _classDeclarations = new Dictionary<string, ClassDeclaration>(StringComparer.Ordinal);
        _typeDeclarations = new List<TypeDeclaration>();
        _apiDeclarations = new List<ApiDeclaration>();
        _contextDeclarations = new List<ContextDeclaration>();
        _isInWorkflowBody = false;
        _profilingOptions = profilingOptions?.Clone();
        _typedScopeStack = new Stack<Dictionary<string, TranspiledClrType>>();
        _constScopeStack = new Stack<HashSet<string>>();
        _moduleConstNames = new HashSet<string>(StringComparer.Ordinal);
        _functionReturnTypes = new Dictionary<string, TranspiledClrType>(StringComparer.Ordinal);
        _functionParameterTypes = new Dictionary<string, IReadOnlyList<TranspiledClrType>>(StringComparer.Ordinal);
        _currentFunctionReturnType = new Stack<TranspiledClrType>();
        _typedTranspileLevel = Math.Clamp(typedTranspileLevel, 0, 2);
    }

    public string Transpile(List<Statement> statements)
    {
        return Transpile(statements, isLibrary: false, sourceFilePath: null);
    }

    public string Transpile(List<Statement> statements, bool isLibrary)
    {
        return Transpile(statements, isLibrary, sourceFilePath: null);
    }

    public string Transpile(List<Statement> statements, bool isLibrary, string? sourceFilePath)
    {
        _syncTwinFunctions = null;
        _typedTwinRejected.Clear();
        // A twin is rejected for something in its own body or for calling a rejected function.
        // Rejections are pushed to every caller at once, so a second pass normally settles it;
        // the cap is a guard, and a final pass without twins is always valid.
        for (var pass = 0; pass < 4; pass++)
        {
            _syncTwinRejected.Clear();
            _typedTwinRejectedThisPass = false;
            var result = TranspileOnce(statements, isLibrary, sourceFilePath);
            if (_syncTwinRejected.Count == 0 && !_typedTwinRejectedThisPass)
            {
                return result;
            }

            RejectSyncTwinsWithCallers(_syncTwinRejected);
        }

        _syncTwinFunctions = new HashSet<string>(StringComparer.Ordinal);
        _syncTwinRejected.Clear();
        return TranspileOnce(statements, isLibrary, sourceFilePath);
    }

    private string TranspileOnce(List<Statement> statements, bool isLibrary, string? sourceFilePath)
    {
        _sourceFilePath = sourceFilePath;
        _output.Clear();
        _indentLevel = 0;
        _usedNamespaces.Clear();
        _generatedAttributeClasses.Clear();
        _functionNames.Clear();
        _promptNames.Clear();
        _variantConstructorNames.Clear();
        _classDeclarations.Clear();
        _typeDeclarations.Clear();
        _apiDeclarations.Clear();
        _contextDeclarations.Clear();
        _canAwait = false;
        _isInWorkflowBody = false;
        _emitLineDirectives = true;
        _profileTempCounter = 0;
        _typedScopeStack.Clear();
        _constScopeStack.Clear();
        _moduleConstNames.Clear();
        _functionReturnTypes.Clear();
        _functionParameterTypes.Clear();
        _localFunctionStack.Clear();
        _currentFunctionReturnType.Clear();
        _currentClassName = null;
        _currentMemberIsStatic = false;

        statements = ModuleSymbolResolver.ExpandFileImportsForTranspile(statements, _sourceFilePath);

        // Settled together with the typed function variants in ComputeTypedTwins.
        _inferredNumerics = NumericVariableInference.Result.Empty();
        _inferredGlobalTypes = new Dictionary<string, TranspiledClrType>(StringComparer.Ordinal);

        // Generate using statements
        _output.AppendLine("using System;");
        _output.AppendLine("using System.Collections.Generic;");
        _output.AppendLine("using System.Linq;");
        _output.AppendLine("using System.Text.Json;");
        _output.AppendLine("using System.Threading.Tasks;");
        _output.AppendLine("using System.Reflection;");
        _output.AppendLine("using System.Threading;");
        _output.AppendLine("using System.Runtime.InteropServices;");
        _output.AppendLine("using System.IO.Ports;");
        _output.AppendLine("using MaldaLang.BuiltIns;");
        _output.AppendLine("using MaldaLang.Interpreter;");
        _output.AppendLine("using MaldaLang.Runtime.Profiling;");
        _output.AppendLine("using MaldaLang.Runtime.Workflows;");
        _output.AppendLine("using MaldaLang.Runtime.Actors;");
        _output.AppendLine("using Spectre.Console;");
        _output.AppendLine();
        _output.AppendLine("namespace GeneratedCode;");
        _output.AppendLine();

        // Generate attribute classes for decorators
        GenerateAttributeClasses(statements);

        _output.AppendLine("public class Program");
        _output.AppendLine("{");
        _indentLevel++;

        // Generate Windows API declarations for ANSI support (if needed)
        GenerateWindowsApiDeclarations();

        // Generate runtime helpers
        GenerateRuntimeHelpers();

        // Transpile classes, actors, functions, and prompts (need to collect them first)
        var classes = new List<ClassDeclaration>();
        var schemas = new List<SchemaDeclaration>();
        var actors = new List<ActorDeclaration>();
        var functions = new List<FunctionDeclaration>();
        var prompts = new List<PromptDeclaration>();
        var properties = new List<PropertyDeclaration>();
        var workflows = new List<WorkflowDeclaration>();
        var topLevelStatements = new List<Statement>();
        var topLevelVariables = new List<VarDeclStatement>(); // For library mode

        foreach (var statement in statements)
        {
            if (statement is ClassDeclaration classDecl)
                classes.Add(classDecl);
            else if (statement is SchemaDeclaration schemaDecl)
                schemas.Add(schemaDecl);
            else if (statement is ActorDeclaration actorDecl)
                actors.Add(actorDecl);
            else if (statement is FunctionDeclaration funcDecl)
                functions.Add(funcDecl);
            else if (statement is PromptDeclaration promptDecl)
                prompts.Add(promptDecl);
            else if (statement is PropertyDeclaration propertyDecl)
                properties.Add(propertyDecl);
            else if (statement is WorkflowDeclaration workflowDecl)
                workflows.Add(workflowDecl);
            else if (statement is TypeDeclaration typeDecl)
            {
                _typeDeclarations.Add(typeDecl);
                foreach (var ctor in typeDecl.Constructors)
                    _variantConstructorNames.Add(ctor.Name);
            }
            else if (statement is ApiDeclaration apiDecl)
            {
                _apiDeclarations.Add(apiDecl);
            }
            else if (statement is ContextDeclaration contextDecl)
                _contextDeclarations.Add(contextDecl);
            else if (statement is SuiteDeclaration or PolicyDeclaration)
            {
                // Eval/policy declarations are host metadata, not executable statements.
            }
            else if (statement is VarDeclStatement varDecl)
            {
                // Track top-level variables for field generation in executable mode.
                topLevelVariables.Add(varDecl);
                if (varDecl.IsConst)
                    _moduleConstNames.Add(varDecl.Name);
                if (!isLibrary)
                    topLevelStatements.Add(statement);
            }
            else
                topLevelStatements.Add(statement);
        }

        // Collect function names before transpiling classes/functions so
        // method bodies can resolve direct calls to known functions.
        foreach (var funcDecl in functions)
        {
            _functionNames.Add(funcDecl.Name);
        }

        foreach (var workflowDecl in workflows)
        {
            _functionNames.Add(GetWorkflowRunnerMethodName(workflowDecl.Name));
        }

        foreach (var classDecl in classes)
        {
            _classDeclarations[classDecl.Name] = classDecl;
            foreach (var member in classDecl.Members)
            {
                if (member.Type == MemberType.Method && member.Value is FunctionDeclaration methodFunc)
                {
                    _functionNames.Add(methodFunc.Name);
                }
            }
        }

        // Decide function twins before any body is emitted: class methods, actors and prompts call
        // top-level functions too, and must see the same set as the functions themselves.
        _syncTwinFunctions ??= ComputeSyncTwinCandidates(functions, classes);
        _typedTwins = ComputeTypedTwins(functions, statements);

        // Library mode exposes top-level state through the generated API, which expects objects.
        if (!isLibrary)
        {
            foreach (var topLevel in topLevelVariables)
            {
                var inferred = ResolveVarDeclType(topLevel);
                if (string.IsNullOrWhiteSpace(topLevel.TypeHint) && inferred != TranspiledClrType.Object)
                    _inferredGlobalTypes[topLevel.Name] = inferred;
            }
            EmitTopLevelFieldDeclarations(topLevelVariables);
        }

        // Generate classes
        foreach (var classDecl in classes)
        {
            EmitLineDirective(classDecl.Line, classDecl.SourceFile ?? _sourceFilePath);
            TranspileClass(classDecl);
            _output.AppendLine();
        }
        
        // Generate prompts
        foreach (var promptDecl in prompts)
        {
            _promptNames.Add(promptDecl.Name);
            EmitLineDirective(promptDecl.Line, promptDecl.SourceFile ?? _sourceFilePath);
            TranspilePrompt(promptDecl, classes, schemas);
            _output.AppendLine();
        }
        
        // Generate actors
        foreach (var actorDecl in actors)
        {
            EmitLineDirective(actorDecl.Line, actorDecl.SourceFile ?? _sourceFilePath);
            TranspileActor(actorDecl);
            _output.AppendLine();
        }

        // Collect function names first
        foreach (var funcDecl in functions)
        {
            if (ShaderFunction.IsMarked(funcDecl))
                continue;

            _functionNames.Add(funcDecl.Name);
            _functionReturnTypes[funcDecl.Name] = ResolveTranspiledTypeHint(funcDecl.ReturnType);
            var parameterTypes = new List<TranspiledClrType>(funcDecl.Parameters.Count);
            for (int i = 0; i < funcDecl.Parameters.Count; i++)
            {
                var paramType = (funcDecl.ParameterTypeHints != null && i < funcDecl.ParameterTypeHints.Count)
                    ? ResolveTranspiledTypeHint(funcDecl.ParameterTypeHints[i])
                    : TranspiledClrType.Object;
                parameterTypes.Add(paramType);
            }
            _functionParameterTypes[funcDecl.Name] = parameterTypes;
        }
        
        // Also collect class method names
        foreach (var classDecl in classes)
        {
            foreach (var member in classDecl.Members)
            {
                if (member.Type == MemberType.Method && member.Value is FunctionDeclaration methodFunc)
                {
                    _functionNames.Add(methodFunc.Name);
                }
            }
        }

        // Generate functions
        foreach (var funcDecl in functions)
        {
            if (ShaderFunction.IsMarked(funcDecl))
                continue;

            EmitLineDirective(funcDecl.Line, funcDecl.SourceFile ?? _sourceFilePath);
            TranspileFunction(funcDecl);
            _output.AppendLine();
        }

        // Generate workflow runners for durable workflow built-ins.
        foreach (var workflowDecl in workflows)
        {
            EmitLineDirective(workflowDecl.Line, workflowDecl.SourceFile ?? _sourceFilePath);
            TranspileWorkflow(workflowDecl);
            _output.AppendLine();
        }

        if (workflows.Count > 0)
        {
            GenerateWorkflowRegistration(workflows);
            _output.AppendLine();
        }

        if (properties.Count > 0)
        {
            GenerateTranspiledPropertyRegistry(properties);
            _output.AppendLine();
            foreach (var propertyDecl in properties)
            {
                EmitLineDirective(propertyDecl.Line, propertyDecl.SourceFile ?? _sourceFilePath);
                TranspileProperty(propertyDecl);
                _output.AppendLine();
            }
        }

        // Generate variant constructors (from type declarations)
        var emittedCtorKeys = new HashSet<(string Name, int Arity)>();
        foreach (var typeDecl in _typeDeclarations)
        {
            foreach (var ctor in typeDecl.Constructors)
            {
                var key = (ctor.Name, ctor.ParameterNames.Count);
                if (!emittedCtorKeys.Add(key))
                    continue;
                EmitVariantConstructorMethod(ctor);
            }
        }

        foreach (var typeDecl in _typeDeclarations)
            EmitSumTypeNamespaceField(typeDecl);

        // Generate decorator registration method if needed
        if (HasDecorators(statements))
        {
            GenerateDecoratorRegistration(statements);
            _output.AppendLine();
        }

        if (isLibrary)
        {
            // Generate top-level variables as static fields in library mode
            foreach (var varDecl in topLevelVariables)
            {
                WriteIndent();
                _output.Append("public static object ");
                _output.Append(EscapeIdentifier(varDecl.Name));
                _output.Append(" = ");
                TranspileExpression(varDecl.Initializer);
                _output.Append(";");
                AppendComment(nameof(Transpile) + " (top-level variable)");
                _output.AppendLine();
            }
            
            if (topLevelVariables.Count > 0)
            {
                _output.AppendLine();
            }

            // Generate Initialize method for library mode
            WriteIndent();
            _output.Append("public static async Task Initialize()");
            AppendComment(nameof(Transpile) + " (Initialize)");
            _output.AppendLine();
            WriteIndent();
            _output.Append("{");
            AppendComment(nameof(Transpile) + " (Initialize open)");
            _output.AppendLine();
            _indentLevel++;

            GenerateSchemaRegistration(schemas);
            GenerateSumTypeRegistration(schemas);
            GenerateApiRegistration();
            if (HasDecorators(statements))
            {
                WriteIndent();
                _output.AppendLine("RegisterDecoratedFunctions();");
            }
            if (workflows.Count > 0)
            {
                WriteIndent();
                _output.AppendLine("RegisterTranspiledWorkflows();");
            }

            // Transpile top-level statements (assignments, function calls, etc.)
            var previousCanAwaitInInitialize = _canAwait;
            _canAwait = true;
            PushLocalFunctionScope();
            foreach (var statement in topLevelStatements)
            {
                TranspileStatement(statement);
            }
            PopLocalFunctionScope();
            _canAwait = previousCanAwaitInInitialize;

            _indentLevel--;
            WriteIndent();
            _output.Append("}");
            AppendComment(nameof(Transpile) + " (Initialize close)");
            _output.AppendLine();
        }
        else
        {
            // Generate Main method for executable mode
            WriteIndent();
            _output.Append("public static async Task Main(string[] args)");
            AppendComment(nameof(Transpile) + " (Main)");
            _output.AppendLine();
            WriteIndent();
            _output.Append("{");
            AppendComment(nameof(Transpile) + " (Main open)");
            _output.AppendLine();
            _indentLevel++;

            // Number parsing/formatting must not follow the OS locale: on it-IT, float("0.5")
            // would read 5 and 1.5 would print as "1,5". Match the interpreter (invariant).
            WriteIndent();
            _output.AppendLine("System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;");
            WriteIndent();
            _output.AppendLine("System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;");
            WriteIndent();
            _output.AppendLine("System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;");
            WriteIndent();
            _output.AppendLine("System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;");

            // Enable ANSI escape codes in Windows console for Spectre.Console support
            WriteIndent();
            _output.AppendLine("// Enable ANSI escape codes in Windows console");
            WriteIndent();
            _output.AppendLine("if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("try");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("var stdoutHandle = GetStdHandle(-11); // STD_OUTPUT_HANDLE");
            WriteIndent();
            _output.AppendLine("if (GetConsoleMode(stdoutHandle, out uint mode))");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("mode |= 0x0004; // ENABLE_VIRTUAL_TERMINAL_PROCESSING");
            WriteIndent();
            _output.AppendLine("SetConsoleMode(stdoutHandle, mode);");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            WriteIndent();
            _output.AppendLine("catch");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("// Ignore errors - ANSI might not be supported");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            _output.AppendLine();

            WriteIndent();
            _output.AppendLine("MaldaLang.Runtime.TranspiledBuiltinRuntime.Initialize();");
            _output.AppendLine();

            // Wrap user code in try/catch so runtime errors (e.g. Integer overflow) are reported to stderr and exit with 1
            WriteIndent();
            _output.AppendLine("try");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;

            GenerateSchemaRegistration(schemas);
            GenerateSumTypeRegistration(schemas);
            GenerateApiRegistration();
            if (HasDecorators(statements))
            {
                WriteIndent();
                _output.AppendLine("RegisterDecoratedFunctions();");
            }
            if (workflows.Count > 0)
            {
                WriteIndent();
                _output.AppendLine("RegisterTranspiledWorkflows();");
            }

            EmitProfilingSessionStart();

            // Transpile top-level statements
            var previousCanAwaitInMain = _canAwait;
            _canAwait = true;
            PushConstScope();
            PushLocalFunctionScope();
            foreach (var statement in topLevelStatements)
            {
                if (statement is VarDeclStatement topLevelVarDecl)
                {
                    TranspileTopLevelVarInitialization(topLevelVarDecl);
                }
                else
                {
                    TranspileStatement(statement);
                }
            }
            PopLocalFunctionScope();
            PopConstScope();
            _canAwait = previousCanAwaitInMain;

            // Ensure actors are cleanly shut down before exit
            WriteIndent();
            _output.AppendLine("await ActorsRuntime.ShutdownAsync();");

            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            WriteIndent();
            _output.AppendLine("catch (Exception __ex)");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("Console.Error.WriteLine(__ex.ToString());");
            WriteIndent();
            _output.AppendLine("System.Environment.Exit(1);");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            WriteIndent();
            _output.AppendLine("finally");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            EmitProfilingSessionComplete();
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");

            _indentLevel--;
            WriteIndent();
            _output.Append("}");
            AppendComment(nameof(Transpile) + " (Main close)");
            _output.AppendLine();
        }

        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(Transpile) + " (Program class close)");
        _output.AppendLine();

        if (isLibrary)
        {
            // Generate MaldaLangApi class for library mode
            GenerateMaldaLangApi(statements, functions, classes, actors, properties);
        }

        return _output.ToString();
    }

    private void WriteIndent()
    {
        for (int i = 0; i < _indentLevel; i++)
        {
            _output.Append("    ");
        }
    }

    /// <summary>Emits each line at the current indent (lines may carry their own extra indent).</summary>
    private void EmitLines(params string[] lines)
    {
        foreach (var line in lines)
        {
            WriteIndent();
            _output.AppendLine(line);
        }
    }

    private void WriteNeuralLayerDispatch(string typeName, string localName)
    {
        WriteIndent();
        _output.Append("else if (instance is ");
        _output.Append(typeName);
        _output.Append(' ');
        _output.Append(localName);
        _output.AppendLine(")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.Append("result = ");
        _output.Append(localName);
        _output.AppendLine(".CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void AppendComment(string functionName)
    {
        _output.Append($" // Generated by: {functionName}");
    }

    private bool TypedTranspileEnabled => _typedTranspileLevel >= 1;
    private bool AggressiveTypedTranspileEnabled => _typedTranspileLevel >= 2;

    private void EmitLineDirective(int line, string? file)
    {
        // Unmapped / synthetic AST nodes (Line == 0) must not claim .malda:1 — that makes
        // every downstream Roslyn error look like it came from the first source line.
        if (line <= 0)
        {
            _output.AppendLine("#line default");
            return;
        }

        var path = file ?? _sourceFilePath ?? "program.malda";
        _output.AppendLine($"#line {line} \"{path}\"");
    }
}
