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
using MaldaLang.Runtime.Profiling;

using MaldaLang.Compiler.OptionalPack;

namespace MaldaLang.Compiler;

public class CSharpTranspiler
{
    private enum TranspiledClrType
    {
        Object,
        Double,
        DoubleArray
    }

    private readonly StringBuilder _output;
    private int _indentLevel;
    private readonly HashSet<string> _usedNamespaces;
    private readonly HashSet<string> _generatedAttributeClasses;
    private readonly HashSet<string> _functionNames;
    private readonly HashSet<string> _promptNames;
    private readonly HashSet<string> _variantConstructorNames;
    private readonly List<TypeDeclaration> _typeDeclarations;
    private bool _isInWorkflowBody;
    private bool _isInActorHandler;
    private bool _transpileCallAsTask;
    private bool _canAwait;
    private bool _emitLineDirectives;
    private string? _sourceFilePath;
    private readonly ProfilingOptions? _profilingOptions;
    private int _profileTempCounter;
    private int _matchBindCounter;
    private readonly Stack<string> _desugaredForContinueLabels = new();
    private readonly Stack<Dictionary<string, TranspiledClrType>> _typedScopeStack;
    private readonly Stack<HashSet<string>> _constScopeStack;
    private readonly Dictionary<string, TranspiledClrType> _functionReturnTypes;
    private readonly Dictionary<string, IReadOnlyList<TranspiledClrType>> _functionParameterTypes;
    private readonly Stack<TranspiledClrType> _currentFunctionReturnType;
    private readonly int _typedTranspileLevel;
    private string? _catchFilterRenameFrom;
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
        _typeDeclarations = new List<TypeDeclaration>();
        _isInWorkflowBody = false;
        _profilingOptions = profilingOptions?.Clone();
        _typedScopeStack = new Stack<Dictionary<string, TranspiledClrType>>();
        _constScopeStack = new Stack<HashSet<string>>();
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
        _sourceFilePath = sourceFilePath;
        _output.Clear();
        _indentLevel = 0;
        _usedNamespaces.Clear();
        _generatedAttributeClasses.Clear();
        _functionNames.Clear();
        _promptNames.Clear();
        _variantConstructorNames.Clear();
        _typeDeclarations.Clear();
        _canAwait = false;
        _isInWorkflowBody = false;
        _emitLineDirectives = true;
        _profileTempCounter = 0;
        _typedScopeStack.Clear();
        _constScopeStack.Clear();
        _functionReturnTypes.Clear();
        _functionParameterTypes.Clear();
        _currentFunctionReturnType.Clear();

        statements = ModuleSymbolResolver.ExpandFileImportsForTranspile(statements, _sourceFilePath);

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
            else if (statement is ChainDeclaration chainDecl)
                functions.Add(chainDecl.ToFunctionDeclaration());
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
            else if (statement is VarDeclStatement varDecl)
            {
                // Track top-level variables for field generation in executable mode.
                topLevelVariables.Add(varDecl);
                if (!isLibrary)
                    topLevelStatements.Add(statement);
            }
            else
                topLevelStatements.Add(statement);
        }

        if (!isLibrary)
        {
            EmitTopLevelFieldDeclarations(topLevelVariables);
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
            foreach (var member in classDecl.Members)
            {
                if (member.Type == MemberType.Method && member.Value is FunctionDeclaration methodFunc)
                {
                    _functionNames.Add(methodFunc.Name);
                }
            }
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
        foreach (var typeDecl in _typeDeclarations)
        {
            foreach (var ctor in typeDecl.Constructors)
            {
                EmitVariantConstructorMethod(ctor);
            }
        }

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
            GenerateSchemaRegistration(schemas);

            // Transpile top-level statements (assignments, function calls, etc.)
            var previousCanAwaitInInitialize = _canAwait;
            _canAwait = true;
            foreach (var statement in topLevelStatements)
            {
                TranspileStatement(statement);
            }
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
            GenerateSchemaRegistration(schemas);

            EmitProfilingSessionStart();

            // Transpile top-level statements
            var previousCanAwaitInMain = _canAwait;
            _canAwait = true;
            PushConstScope();
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

    private void AppendComment(string functionName)
    {
        _output.Append($" // Generated by: {functionName}");
    }

    private bool TypedTranspileEnabled => _typedTranspileLevel >= 1;
    private bool AggressiveTypedTranspileEnabled => _typedTranspileLevel >= 2;

    private TranspiledClrType ResolveTranspiledTypeHint(string? typeHint)
    {
        if (!TypedTranspileEnabled)
            return TranspiledClrType.Object;

        if (string.Equals(typeHint, "float", StringComparison.OrdinalIgnoreCase))
            return TranspiledClrType.Double;
        if (AggressiveTypedTranspileEnabled &&
            (string.Equals(typeHint, "floatArray", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(typeHint, "doubleArray", StringComparison.OrdinalIgnoreCase)))
            return TranspiledClrType.DoubleArray;
        if (string.Equals(typeHint, "int", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "integer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "bool", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "boolean", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "string", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "any", StringComparison.OrdinalIgnoreCase))
            return TranspiledClrType.Object;
        if (string.IsNullOrWhiteSpace(typeHint))
            return TranspiledClrType.Object;
        throw new InvalidOperationException($"Unsupported transpiled type hint '{typeHint}'.");
    }

    private static string GetClrTypeName(TranspiledClrType type)
    {
        return type switch
        {
            TranspiledClrType.Double => "double",
            TranspiledClrType.DoubleArray => "System.Collections.Generic.List<double>",
            _ => "object"
        };
    }

    private static string GetCoercionExpressionPrefix(TranspiledClrType type)
    {
        return type switch
        {
            TranspiledClrType.Double => "(double)RuntimeHelpers.CoerceToFloat(",
            TranspiledClrType.DoubleArray => "RuntimeHelpers.CoerceToDoubleList(",
            _ => string.Empty
        };
    }

    private static string GetCoercionExpressionSuffix(TranspiledClrType type)
    {
        return type switch
        {
            TranspiledClrType.Double => ")",
            TranspiledClrType.DoubleArray => ")",
            _ => string.Empty
        };
    }

    private void PushTypedScope()
    {
        _typedScopeStack.Push(new Dictionary<string, TranspiledClrType>(StringComparer.Ordinal));
    }

    private void PopTypedScope()
    {
        if (_typedScopeStack.Count > 0)
            _typedScopeStack.Pop();
    }

    private void PushConstScope()
    {
        _constScopeStack.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private void PopConstScope()
    {
        if (_constScopeStack.Count > 0)
            _constScopeStack.Pop();
    }

    private void RegisterConstBinding(string name)
    {
        if (_constScopeStack.Count > 0)
            _constScopeStack.Peek().Add(name);
    }

    private bool IsConstBinding(string name)
    {
        foreach (var scope in _constScopeStack)
        {
            if (scope.Contains(name))
                return true;
        }

        return false;
    }

    private void EmitConstAssignGuard(string name)
    {
        if (!IsConstBinding(name))
            return;

        WriteIndent();
        _output.Append("throw new System.Exception(\"Cannot assign to const '");
        _output.Append(name);
        _output.AppendLine("'.\");");
    }

    private void RegisterTypedVariable(string name, TranspiledClrType type)
    {
        if (_typedScopeStack.Count == 0)
            return;
        _typedScopeStack.Peek()[name] = type;
    }

    private TranspiledClrType ResolveVariableTypeOrDefault(string name)
    {
        foreach (var scope in _typedScopeStack)
        {
            if (scope.TryGetValue(name, out var type))
                return type;
        }
        return TranspiledClrType.Object;
    }

    private TranspiledClrType ResolveExpressionType(Expression expression)
    {
        if (!TypedTranspileEnabled)
            return TranspiledClrType.Object;

        return expression switch
        {
            LiteralExpression literal when literal.Value is double => TranspiledClrType.Double,
            LiteralExpression literal when literal.Value is int => TranspiledClrType.Double,
            IdentifierExpression identifier => ResolveVariableTypeOrDefault(identifier.Name),
            ArrayAccessExpression arrayAccess when ResolveExpressionType(arrayAccess.Array) == TranspiledClrType.DoubleArray => TranspiledClrType.Double,
            PostfixExpression postfix when (postfix.Operator == TokenType.Increment || postfix.Operator == TokenType.Decrement) &&
                                             ResolveExpressionType(postfix.Left) == TranspiledClrType.Double => TranspiledClrType.Double,
            FunctionCallExpression call when call.Callee is IdentifierExpression id &&
                                            _functionReturnTypes.TryGetValue(id.Name, out var returnType) => returnType,
            FunctionCallExpression call when call.Callee is IdentifierExpression builtInId &&
                                            ResolveTypedBuiltInReturnType(builtInId.Name, call.Arguments) is var builtInReturnType &&
                                            builtInReturnType != TranspiledClrType.Object => builtInReturnType,
            UnaryExpression unary when unary.Operator == TokenType.Minus &&
                                       ResolveExpressionType(unary.Right) == TranspiledClrType.Double => TranspiledClrType.Double,
            BinaryExpression binary when IsNumericBinaryOperator(binary.Operator) &&
                                         ResolveExpressionType(binary.Left) == TranspiledClrType.Double &&
                                         ResolveExpressionType(binary.Right) == TranspiledClrType.Double => TranspiledClrType.Double,
            TernaryExpression ternary when ResolveExpressionType(ternary.ThenBranch) == TranspiledClrType.Double &&
                                          ResolveExpressionType(ternary.ElseBranch) == TranspiledClrType.Double => TranspiledClrType.Double,
            AwaitExpression awaitExpr => ResolveExpressionType(awaitExpr.Expression),
            _ => TranspiledClrType.Object
        };
    }

    private TranspiledClrType ResolveTypedBuiltInReturnType(string name, List<Expression> arguments)
    {
        if (!TypedTranspileEnabled)
            return TranspiledClrType.Object;

        bool FirstArgIsDouble() => arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double;
        bool FirstTwoArgsAreDouble() => arguments.Count > 1 &&
                                        ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
                                        ResolveExpressionType(arguments[1]) == TranspiledClrType.Double;

        return name switch
        {
            // Numeric built-ins that preserve/return double when inputs are proven doubles.
            "float" when FirstArgIsDouble() => TranspiledClrType.Double,
            "abs" when FirstArgIsDouble() => TranspiledClrType.Double,
            "sqrt" when FirstArgIsDouble() => TranspiledClrType.Double,
            "floor" when FirstArgIsDouble() => TranspiledClrType.Double,
            "ceil" when FirstArgIsDouble() => TranspiledClrType.Double,
            "round" when FirstArgIsDouble() => TranspiledClrType.Double,
            "trunc" when FirstArgIsDouble() => TranspiledClrType.Double,
            "sign" when FirstArgIsDouble() => TranspiledClrType.Double,
            "exp" when FirstArgIsDouble() => TranspiledClrType.Double,
            "log" when FirstArgIsDouble() => TranspiledClrType.Double,
            "log10" when FirstArgIsDouble() => TranspiledClrType.Double,
            "log2" when FirstArgIsDouble() => TranspiledClrType.Double,
            "sin" when FirstArgIsDouble() => TranspiledClrType.Double,
            "cos" when FirstArgIsDouble() => TranspiledClrType.Double,
            "tan" when FirstArgIsDouble() => TranspiledClrType.Double,
            "asin" when FirstArgIsDouble() => TranspiledClrType.Double,
            "acos" when FirstArgIsDouble() => TranspiledClrType.Double,
            "atan" when FirstArgIsDouble() => TranspiledClrType.Double,
            "pow" when FirstTwoArgsAreDouble() => TranspiledClrType.Double,
            "min" when FirstTwoArgsAreDouble() => TranspiledClrType.Double,
            "max" when FirstTwoArgsAreDouble() => TranspiledClrType.Double,
            "atan2" when FirstTwoArgsAreDouble() => TranspiledClrType.Double,
            _ => TranspiledClrType.Object
        };
    }

    private static bool IsNumericBinaryOperator(TokenType op)
    {
        return op is TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Divide or TokenType.Modulo;
    }

    private void EmitTopLevelFieldDeclarations(List<VarDeclStatement> topLevelVariables)
    {
        var emittedNames = new HashSet<string>();
        foreach (var varDecl in topLevelVariables)
        {
            if (!emittedNames.Add(varDecl.Name))
                continue;

            WriteIndent();
            var declaredType = ResolveTranspiledTypeHint(varDecl.TypeHint);
            _output.Append("public static ");
            _output.Append(GetClrTypeName(declaredType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier(varDecl.Name));
            _output.Append(" = ");
            _output.Append(declaredType == TranspiledClrType.Double ? "0d;" : "null!;");
            AppendComment(nameof(Transpile) + " (top-level field)");
            _output.AppendLine();
        }

        if (emittedNames.Count > 0)
        {
            _output.AppendLine();
        }
    }

    private void TranspileTopLevelVarInitialization(VarDeclStatement varDecl)
    {
        if (varDecl.IsConst)
            RegisterConstBinding(varDecl.Name);
        WriteIndent();
        string? profileVariable = null;
        if (ProfilingEnabled)
        {
            profileVariable = EmitStatementProfileStart(varDecl);
            WriteIndent();
        }
        var declaredType = ResolveTranspiledTypeHint(varDecl.TypeHint);
        _output.Append(EscapeIdentifier(varDecl.Name));
        _output.Append(" = ");
        _output.Append(GetCoercionExpressionPrefix(declaredType));
        TranspileExpression(varDecl.Initializer);
        _output.Append(GetCoercionExpressionSuffix(declaredType));
        _output.Append(";");
        AppendComment(nameof(Transpile) + " (top-level var init)");
        _output.AppendLine();
        if (profileVariable != null)
        {
            EmitStatementProfileExit(profileVariable);
        }
    }

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

    private bool ProfilingEnabled => _profilingOptions?.Enabled == true;

    private string GetGeneratedProfileSessionName()
    {
        return _sourceFilePath ?? "program.malda";
    }

    private string GetStatementProfileName(Statement statement)
    {
        var typeName = statement.GetType().Name;
        return typeName.EndsWith("Statement", StringComparison.Ordinal)
            ? typeName[..^"Statement".Length]
            : typeName;
    }

    private void AppendProfilingOptionsLiteral()
    {
        if (!ProfilingEnabled || _profilingOptions == null)
        {
            _output.Append("ProfilingOptions.Disabled");
            return;
        }

        _output.Append("new ProfilingOptions { Enabled = true, OutputPath = ");
        _output.Append(_profilingOptions.OutputPath == null ? "null" : ToQuotedString(_profilingOptions.OutputPath));
        _output.Append(", Format = ProfilingFormat.");
        _output.Append(_profilingOptions.Format.ToString());
        _output.Append(", WriteToConsole = ");
        _output.Append(_profilingOptions.WriteToConsole ? "true" : "false");
        _output.Append(", MaxEntriesPerSection = ");
        _output.Append(_profilingOptions.MaxEntriesPerSection.ToString());
        _output.Append(", PeriodicSnapshotSeconds = ");
        _output.Append(_profilingOptions.PeriodicSnapshotSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _output.Append(" }");
    }

    private void EmitProfilingSessionStart()
    {
        WriteIndent();
        _output.Append("MaldaProfiler.StartSession(");
        AppendProfilingOptionsLiteral();
        _output.Append(", ");
        _output.Append(ToQuotedString(GetGeneratedProfileSessionName()));
        _output.AppendLine(");");
    }

    private void EmitProfilingSessionComplete()
    {
        WriteIndent();
        _output.AppendLine("MaldaProfiler.CompleteSession();");
    }

    private string NextProfileVariableName(string prefix)
    {
        return $"__malda{prefix}{++_profileTempCounter}";
    }

    private string EmitStatementProfileStart(Statement statement)
    {
        var variableName = NextProfileVariableName("StmtProfile");
        _output.Append("var ");
        _output.Append(variableName);
        _output.Append(" = MaldaProfiler.EnterStatement(");
        _output.Append(ToQuotedString(statement.SourceFile ?? _sourceFilePath ?? "program.malda"));
        _output.Append(", ");
        _output.Append(statement.Line.ToString());
        _output.Append(", ");
        _output.Append(ToQuotedString(GetStatementProfileName(statement)));
        _output.AppendLine(");");
        return variableName;
    }

    private void EmitStatementProfileExit(string variableName)
    {
        WriteIndent();
        _output.Append("MaldaProfiler.Exit(");
        _output.Append(variableName);
        _output.AppendLine(");");
    }

    private void TranspileProfiledStructuredStatement(Statement statement, Action bodyEmitter)
    {
        if (!ProfilingEnabled)
        {
            bodyEmitter();
            return;
        }

        // Wrap profile token + try/finally in a block so parents like `while (c)` / `if (c)` without
        // braces bind one compound statement (otherwise only `var token = EnterStatement(...)` is the
        // loop/if body and the following try/finally breaks C# structure).
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        var profileVariable = EmitStatementProfileStart(statement);
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        bodyEmitter();
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        EmitStatementProfileExit(profileVariable);
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private string EmitFunctionProfileStart(string functionName, int line, string? file)
    {
        var variableName = NextProfileVariableName("FunctionProfile");
        WriteIndent();
        _output.Append("var ");
        _output.Append(variableName);
        _output.Append(" = MaldaProfiler.EnterFunction(");
        _output.Append(ToQuotedString(functionName));
        _output.Append(", ");
        _output.Append(ToQuotedString(file ?? _sourceFilePath ?? "program.malda"));
        _output.Append(", ");
        _output.Append(line.ToString());
        _output.AppendLine(");");
        return variableName;
    }

    private void EmitFunctionProfileExit(string variableName)
    {
        WriteIndent();
        _output.Append("MaldaProfiler.Exit(");
        _output.Append(variableName);
        _output.AppendLine(");");
    }

    private void GenerateMaldaLangApi(List<Statement> statements, List<FunctionDeclaration> functions, List<ClassDeclaration> classes, List<ActorDeclaration> actors, List<PropertyDeclaration> properties)
    {
        _output.AppendLine();
        _output.AppendLine("/// <summary>");
        _output.AppendLine("/// Public API class for accessing transpiled MALDA code from other .NET programs.");
        _output.AppendLine("/// </summary>");
        _output.AppendLine("public static class MaldaLangApi");
        _output.AppendLine("{");
        _indentLevel++;
        
        // Initialize method
        WriteIndent();
        _output.AppendLine("/// <summary>");
        WriteIndent();
        _output.AppendLine("/// Initializes the library. Call this before using any functions or classes.");
        WriteIndent();
        _output.AppendLine("/// </summary>");
        WriteIndent();
        _output.AppendLine("public static async Task Initialize()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("await Program.Initialize();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        // ShutdownAsync method
        WriteIndent();
        _output.AppendLine("/// <summary>");
        WriteIndent();
        _output.AppendLine("/// Shuts down the library and cleans up resources (e.g., actors).");
        WriteIndent();
        _output.AppendLine("/// </summary>");
        WriteIndent();
        _output.AppendLine("public static async Task ShutdownAsync()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("await ActorsRuntime.ShutdownAsync();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        // Access to Program class for functions
        WriteIndent();
        _output.AppendLine("/// <summary>");
        WriteIndent();
        _output.AppendLine("/// Provides access to transpiled functions and classes.");
        WriteIndent();
        _output.AppendLine("/// </summary>");
        WriteIndent();
        _output.AppendLine("public static class ProgramAccess");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("/// <summary>");
        WriteIndent();
        _output.AppendLine("/// Gets the Program class containing all transpiled functions.");
        WriteIndent();
        _output.AppendLine("/// </summary>");
        WriteIndent();
        _output.AppendLine("public static Type ProgramType => typeof(Program);");
        if (properties.Count > 0)
        {
            _output.AppendLine();
            WriteIndent();
            _output.AppendLine("/// <summary>");
            WriteIndent();
            _output.AppendLine("/// Gets metadata for transpiled properties.");
            WriteIndent();
            _output.AppendLine("/// </summary>");
            WriteIndent();
            _output.AppendLine("public static System.Collections.Generic.IReadOnlyList<Program.TranspiledPropertyMetadata> GetProperties() => Program.GetTranspiledProperties();");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void GenerateAttributeClasses(List<Statement> statements)
    {
        var decoratorNames = new HashSet<string>();
        CollectDecoratorNames(statements, decoratorNames);

        if (decoratorNames.Count == 0)
            return;

        foreach (var decoratorName in decoratorNames)
        {
            if (_generatedAttributeClasses.Contains(decoratorName))
                continue;

            _output.AppendLine($"public class {decoratorName}Attribute : System.Attribute");
            _output.AppendLine("{");
            _output.AppendLine("    public object[] Arguments { get; set; }");
            _output.AppendLine();
            _output.AppendLine($"    public {decoratorName}Attribute(params object[] args)");
            _output.AppendLine("    {");
            _output.AppendLine("        Arguments = args;");
            _output.AppendLine("    }");
            _output.AppendLine("}");
            _output.AppendLine();

            _generatedAttributeClasses.Add(decoratorName);
        }
    }

    private void CollectDecoratorNames(List<Statement> statements, HashSet<string> decoratorNames)
    {
        foreach (var statement in statements)
        {
            if (statement is FunctionDeclaration funcDecl)
            {
                if (funcDecl.Decorators != null)
                {
                    foreach (var decorator in funcDecl.Decorators)
                    {
                        if (!TargetPartitioner.IsCompileTimeTargetDecorator(decorator.Name))
                        {
                            decoratorNames.Add(decorator.Name);
                        }
                    }
                }
            }
            else if (statement is ClassDeclaration classDecl)
            {
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration memberFunc)
                    {
                        if (memberFunc.Decorators != null)
                        {
                            foreach (var decorator in memberFunc.Decorators)
                            {
                                if (!TargetPartitioner.IsCompileTimeTargetDecorator(decorator.Name))
                                {
                                    decoratorNames.Add(decorator.Name);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private bool HasDecorators(List<Statement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is FunctionDeclaration funcDecl && funcDecl.Decorators != null && funcDecl.Decorators.Any(d => !TargetPartitioner.IsCompileTimeTargetDecorator(d.Name)))
                return true;
            if (statement is ClassDeclaration classDecl)
            {
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration memberFunc &&
                        memberFunc.Decorators != null &&
                        memberFunc.Decorators.Any(d => !TargetPartitioner.IsCompileTimeTargetDecorator(d.Name)))
                        return true;
                }
            }
        }
        return false;
    }

    private void GenerateWindowsApiDeclarations()
    {
        // Generate Windows API declarations for enabling ANSI escape codes
        WriteIndent();
        _output.AppendLine("[System.Runtime.InteropServices.DllImport(\"kernel32.dll\", SetLastError = true)]");
        WriteIndent();
        _output.AppendLine("private static extern System.IntPtr GetStdHandle(int nStdHandle);");
        WriteIndent();
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("[System.Runtime.InteropServices.DllImport(\"kernel32.dll\", SetLastError = true)]");
        WriteIndent();
        _output.AppendLine("[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]");
        WriteIndent();
        _output.AppendLine("private static extern bool GetConsoleMode(System.IntPtr hConsoleHandle, out uint lpMode);");
        WriteIndent();
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("[System.Runtime.InteropServices.DllImport(\"kernel32.dll\", SetLastError = true)]");
        WriteIndent();
        _output.AppendLine("[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]");
        WriteIndent();
        _output.AppendLine("private static extern bool SetConsoleMode(System.IntPtr hConsoleHandle, uint dwMode);");
        WriteIndent();
        _output.AppendLine();
    }

    private void GenerateRuntimeHelpers()
    {
        // Generate RuntimeHelpers class inline
        WriteIndent();
        _output.Append("public static class RuntimeHelpers");
        AppendComment(nameof(GenerateRuntimeHelpers) + " (class)");
        _output.AppendLine();
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(GenerateRuntimeHelpers) + " (class open)");
        _output.AppendLine();
        _indentLevel++;
        
        WriteIndent();
        _output.AppendLine("private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue>, System.Collections.Generic.List<object>> __rvListToObjectListCache = new System.Runtime.CompilerServices.ConditionalWeakTable<System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue>, System.Collections.Generic.List<object>>();");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("// The interpreter writes input()'s prompt before reading; compiled programs must match.");
        WriteIndent();
        _output.AppendLine("public static string? ReadLineWithPrompt(object? prompt)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (prompt != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var text = CoerceToString(prompt);");
        WriteIndent();
        _output.AppendLine("if (text.Length > 0) Console.Write(text);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return Console.ReadLine();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("public static object CoerceToInt(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value == null) return 0;");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return rv.Type switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Integer => rv.AsInteger(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Float => (int)rv.AsFloat(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.String => int.TryParse(rv.AsString(), out var result) ? result : 0,");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Boolean => rv.AsBoolean() ? 1 : 0,");
        WriteIndent();
        _output.AppendLine("_ => 0");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return value switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("int i => i,");
        WriteIndent();
        _output.AppendLine("long l => (int)l,");
        WriteIndent();
        _output.AppendLine("double d => (int)d,");
        WriteIndent();
        _output.AppendLine("float f => (int)f,");
        WriteIndent();
        _output.AppendLine("string s => int.TryParse(s, out var result) ? result : 0,");
        WriteIndent();
        _output.AppendLine("bool b => b ? 1 : 0,");
        WriteIndent();
        _output.AppendLine("_ => throw new InvalidOperationException($\"Cannot coerce {value.GetType()} to int\")");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static object CoerceToFloat(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value == null) return 0.0;");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return rv.Type switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Integer => (double)rv.AsInteger(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Float => rv.AsFloat(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.String => double.TryParse(rv.AsString(), out var result) ? result : 0.0,");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Boolean => rv.AsBoolean() ? 1.0 : 0.0,");
        WriteIndent();
        _output.AppendLine("_ => 0.0");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return value switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("int i => (double)i,");
        WriteIndent();
        _output.AppendLine("long l => (double)l,");
        WriteIndent();
        _output.AppendLine("double d => d,");
        WriteIndent();
        _output.AppendLine("float f => (double)f,");
        WriteIndent();
        _output.AppendLine("string s => double.TryParse(s, out var result) ? result : 0.0,");
        WriteIndent();
        _output.AppendLine("bool b => b ? 1.0 : 0.0,");
        WriteIndent();
        _output.AppendLine("_ => throw new InvalidOperationException($\"Cannot coerce {value.GetType()} to float\")");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static string CoerceToString(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value == null) return \"null\";");
        WriteIndent();
        _output.AppendLine("if (value is string s) return s;");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return rv.Type switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.String => rv.AsString(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Integer => rv.AsInteger().ToString(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Float => rv.AsFloat().ToString(System.Globalization.CultureInfo.InvariantCulture),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Boolean => rv.AsBoolean().ToString().ToLower(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Null => \"null\",");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Array => FormatArrayFromRuntimeValue(rv.AsArray()),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Object => rv.AsObject().ToString(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Function => \"<function>\",");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Class => \"<class>\",");
        WriteIndent();
        _output.AppendLine("_ => rv.ToString() ?? \"null\"");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (value is List<object> list)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return FormatArray(list);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (value is List<MaldaLang.Interpreter.RuntimeValue> runtimeValueList)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return FormatArrayFromRuntimeValue(runtimeValueList);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle raw C# bool values (from CallObjectMethod, etc.)");
        WriteIndent();
        _output.AppendLine("if (value is bool boolValue)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return boolValue.ToString().ToLower();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return value.ToString() ?? \"null\";");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("private static string FormatArray(List<object> array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var elements = new List<string>();");
        WriteIndent();
        _output.AppendLine("foreach (var item in array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("elements.Add(CoerceToString(item));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return \"[\" + string.Join(\", \", elements) + \"]\";");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("private static string FormatArrayFromRuntimeValue(List<MaldaLang.Interpreter.RuntimeValue> array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var elements = new List<string>();");
        WriteIndent();
        _output.AppendLine("foreach (var item in array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("elements.Add(CoerceToString(item));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return \"[\" + string.Join(\", \", elements) + \"]\";");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static bool CoerceToBool(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value == null) return false;");
        WriteIndent();
        _output.AppendLine("return value switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("bool b => b,");
        WriteIndent();
        _output.AppendLine("int i => i != 0,");
        WriteIndent();
        _output.AppendLine("long l => l != 0,");
        WriteIndent();
        _output.AppendLine("double d => d != 0.0,");
        WriteIndent();
        _output.AppendLine("float f => f != 0.0f,");
        WriteIndent();
        _output.AppendLine("string s => !string.IsNullOrEmpty(s),");
        WriteIndent();
        _output.AppendLine("List<object> list => list.Count > 0,");
        WriteIndent();
        _output.AppendLine("_ => true");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static MaldaLang.Interpreter.RuntimeValue UnwrapMaldaExceptionValue(Exception ex)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (ex is MaldaLang.Interpreter.MALDAException malda)");
        WriteIndent();
        _output.AppendLine("    return malda.Value;");
        WriteIndent();
        _output.AppendLine("return MaldaLang.Interpreter.RuntimeValue.String(ex.Message);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool MaldaCatchWhen(Exception ex, Func<object, bool> predicate)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var bound = UnwrapRuntimeValue(UnwrapMaldaExceptionValue(ex));");
        WriteIndent();
        _output.AppendLine("return predicate(bound);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static bool IsString(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("    return rv.Type == MaldaLang.Interpreter.ValueType.String;");
        WriteIndent();
        _output.AppendLine("return value is string;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static bool IsInt(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("    return rv.Type == MaldaLang.Interpreter.ValueType.Integer;");
        WriteIndent();
        _output.AppendLine("return value is int or long;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static bool IsFloat(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("    return rv.Type == MaldaLang.Interpreter.ValueType.Float;");
        WriteIndent();
        _output.AppendLine("return value is double or float;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static bool IsNumber(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("    return rv.Type == MaldaLang.Interpreter.ValueType.Integer || rv.Type == MaldaLang.Interpreter.ValueType.Float;");
        WriteIndent();
        _output.AppendLine("return value is int or long or double or float;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static string TypeOfValue(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var unwrapped = UnwrapRuntimeValue(value);");
        WriteIndent();
        _output.AppendLine("if (unwrapped is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("    return MaldaLang.Interpreter.Tier0TypeTags.GetTag(rv);");
        WriteIndent();
        _output.AppendLine("if (unwrapped == null) return \"null\";");
        WriteIndent();
        _output.AppendLine("if (unwrapped is string) return \"string\";");
        WriteIndent();
        _output.AppendLine("if (unwrapped is bool) return \"bool\";");
        WriteIndent();
        _output.AppendLine("if (unwrapped is int or long) return \"int\";");
        WriteIndent();
        _output.AppendLine("if (unwrapped is double or float) return \"float\";");
        WriteIndent();
        _output.AppendLine("if (unwrapped is List<object> or List<MaldaLang.Interpreter.RuntimeValue>) return \"array\";");
        WriteIndent();
        _output.AppendLine("if (unwrapped is MaldaLang.Interpreter.DictionaryInstance or System.Collections.Generic.Dictionary<string, object?>) return \"dict\";");
        WriteIndent();
        _output.AppendLine("if (unwrapped is MaldaLang.Interpreter.ObjectInstance) return \"object\";");
        WriteIndent();
        _output.AppendLine("if (unwrapped is Delegate) return \"function\";");
        WriteIndent();
        _output.AppendLine("return \"unknown\";");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool IsTag(object? value, object? expectedTag)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var tag = CoerceToString(expectedTag);");
        WriteIndent();
        _output.AppendLine("return MaldaLang.Interpreter.Tier0TypeTags.MatchesTag(TypeOfValue(value), tag);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static void CheckNumberOperands(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (!IsNumber(left) || !IsNumber(right))");
        WriteIndent();
        _output.AppendLine("    throw new System.InvalidOperationException(\"Operands must be numbers.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        // Checked integer arithmetic (throws on overflow)
        WriteIndent();
        _output.AppendLine("public static int CheckedIntAdd(int a, int b) { try { return checked(a + b); } catch (System.OverflowException) { throw new MaldaLang.Interpreter.RuntimeException(\"Integer overflow.\"); } }");
        WriteIndent();
        _output.AppendLine("public static int CheckedIntSubtract(int a, int b) { try { return checked(a - b); } catch (System.OverflowException) { throw new MaldaLang.Interpreter.RuntimeException(\"Integer overflow.\"); } }");
        WriteIndent();
        _output.AppendLine("public static int CheckedIntMultiply(int a, int b) { try { return checked(a * b); } catch (System.OverflowException) { throw new MaldaLang.Interpreter.RuntimeException(\"Integer overflow.\"); } }");
        WriteIndent();
        _output.AppendLine("public static int CheckedIntMod(int a, int b) { try { return checked(a % b); } catch (System.OverflowException) { throw new MaldaLang.Interpreter.RuntimeException(\"Integer overflow.\"); } }");
        WriteIndent();
        _output.AppendLine("public static int CheckedIntNegate(int a) { try { return checked(-a); } catch (System.OverflowException) { throw new MaldaLang.Interpreter.RuntimeException(\"Integer overflow.\"); } }");
        WriteIndent();
        _output.AppendLine("public static int CheckedIntIncrement(int a) { try { return checked(a + 1); } catch (System.OverflowException) { throw new MaldaLang.Interpreter.RuntimeException(\"Integer overflow.\"); } }");
        WriteIndent();
        _output.AppendLine("public static int CheckedIntDecrement(int a) { try { return checked(a - 1); } catch (System.OverflowException) { throw new MaldaLang.Interpreter.RuntimeException(\"Integer overflow.\"); } }");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static bool IsPrimitiveForOperatorOverload(object? value) => value is null or int or long or double or float or string or bool;");
        WriteIndent();
        _output.AppendLine("private static object? ConvertOperatorArgument(object? value, System.Type parameterType)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (parameterType == typeof(object)) return value;");
        WriteIndent();
        _output.AppendLine("if (parameterType == typeof(MaldaLang.Interpreter.RuntimeValue)) return ToRuntimeValue(value);");
        WriteIndent();
        _output.AppendLine("if (value == null) return null;");
        WriteIndent();
        _output.AppendLine("if (parameterType.IsInstanceOfType(value)) return value;");
        WriteIndent();
        _output.AppendLine("return value;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static bool TryInvokeOperatorBinary(object? left, object? right, string methodName, out object? result)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var receiver = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var argument = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("if (receiver == null || IsPrimitiveForOperatorOverload(receiver))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = null;");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var receiverType = receiver.GetType();");
        WriteIndent();
        _output.AppendLine("var method = receiverType.GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(object) }, null);");
        WriteIndent();
        _output.AppendLine("if (method == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("method = receiverType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).FirstOrDefault(m =>");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("m.Name == methodName &&");
        WriteIndent();
        _output.AppendLine("m.GetParameters().Length == 1 &&");
        WriteIndent();
        _output.AppendLine("(m.GetParameters()[0].ParameterType == typeof(object) || m.GetParameters()[0].ParameterType == typeof(MaldaLang.Interpreter.RuntimeValue)));");
        _indentLevel--;
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (method == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = null;");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var parameterType = method.GetParameters()[0].ParameterType;");
        WriteIndent();
        _output.AppendLine("var convertedArgument = ConvertOperatorArgument(argument, parameterType);");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = method.Invoke(receiver, new[] { convertedArgument });");
        WriteIndent();
        _output.AppendLine("return true;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException($\"Operator overload '{methodName}' failed on {receiverType.FullName} with argument type {argument?.GetType().FullName}\", ex.InnerException);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static bool TryInvokeOperatorBinaryReversed(object? left, object? right, string methodName, out object? result)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var receiver = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("var argument = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("if (receiver == null || IsPrimitiveForOperatorOverload(receiver))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = null;");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var receiverType = receiver.GetType();");
        WriteIndent();
        _output.AppendLine("var method = receiverType.GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(object) }, null);");
        WriteIndent();
        _output.AppendLine("if (method == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("method = receiverType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).FirstOrDefault(m =>");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("m.Name == methodName &&");
        WriteIndent();
        _output.AppendLine("m.GetParameters().Length == 1 &&");
        WriteIndent();
        _output.AppendLine("(m.GetParameters()[0].ParameterType == typeof(object) || m.GetParameters()[0].ParameterType == typeof(MaldaLang.Interpreter.RuntimeValue)));");
        _indentLevel--;
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (method == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = null;");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var parameterType = method.GetParameters()[0].ParameterType;");
        WriteIndent();
        _output.AppendLine("var convertedArgument = ConvertOperatorArgument(argument, parameterType);");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = method.Invoke(receiver, new[] { convertedArgument });");
        WriteIndent();
        _output.AppendLine("return true;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException($\"Operator overload '{methodName}' failed on {receiverType.FullName} with argument type {argument?.GetType().FullName}\", ex.InnerException);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static bool TryInvokeOperatorUnary(object? operand, string methodName, out object? result)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var receiver = UnwrapRuntimeValue(operand);");
        WriteIndent();
        _output.AppendLine("if (receiver == null || IsPrimitiveForOperatorOverload(receiver))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = null;");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var method = receiver.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);");
        WriteIndent();
        _output.AppendLine("if (method == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = null;");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = method.Invoke(receiver, System.Array.Empty<object>());");
        WriteIndent();
        _output.AppendLine("return true;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw ex.InnerException;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object OperatorAdd(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__add__\", out var overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__radd__\", out overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("if (IsString(l) || IsString(r)) return CoerceToString(l) + CoerceToString(r);");
        WriteIndent();
        _output.AppendLine("if (IsInt(l) && IsInt(r)) return CheckedIntAdd((int)CoerceToInt(l), (int)CoerceToInt(r));");
        WriteIndent();
        _output.AppendLine("if (IsNumber(l) && IsNumber(r)) return (double)CoerceToFloat(l) + (double)CoerceToFloat(r);");
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException(\"Operands must be numbers or strings.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object OperatorSubtract(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__sub__\", out var overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rsub__\", out overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("CheckNumberOperands(l, r);");
        WriteIndent();
        _output.AppendLine("if (IsInt(l) && IsInt(r)) return CheckedIntSubtract((int)CoerceToInt(l), (int)CoerceToInt(r));");
        WriteIndent();
        _output.AppendLine("return (double)CoerceToFloat(l) - (double)CoerceToFloat(r);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object OperatorMultiply(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__mul__\", out var overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rmul__\", out overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("if (IsString(l) && (IsInt(r) || IsFloat(r)))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var count = (int)CoerceToInt(r);");
        WriteIndent();
        _output.AppendLine("return count <= 0 ? \"\" : RepeatString(CoerceToString(l), count);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if ((IsInt(l) || IsFloat(l)) && IsString(r))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var count = (int)CoerceToInt(l);");
        WriteIndent();
        _output.AppendLine("return count <= 0 ? \"\" : RepeatString(CoerceToString(r), count);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("CheckNumberOperands(l, r);");
        WriteIndent();
        _output.AppendLine("if (IsInt(l) && IsInt(r)) return CheckedIntMultiply((int)CoerceToInt(l), (int)CoerceToInt(r));");
        WriteIndent();
        _output.AppendLine("return (double)CoerceToFloat(l) * (double)CoerceToFloat(r);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object OperatorDivide(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__div__\", out var overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rdiv__\", out overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("CheckNumberOperands(l, r);");
        WriteIndent();
        _output.AppendLine("var divisor = (double)CoerceToFloat(r);");
        WriteIndent();
        _output.AppendLine("if (divisor == 0) throw new MaldaLang.Interpreter.RuntimeException(\"Division by zero.\");");
        WriteIndent();
        _output.AppendLine("return (double)CoerceToFloat(l) / divisor;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object OperatorModulo(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__mod__\", out var overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rmod__\", out overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("CheckNumberOperands(l, r);");
        WriteIndent();
        _output.AppendLine("if (IsInt(l) && IsInt(r))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var divisor = (int)CoerceToInt(r);");
        WriteIndent();
        _output.AppendLine("if (divisor == 0) throw new MaldaLang.Interpreter.RuntimeException(\"Division by zero.\");");
        WriteIndent();
        _output.AppendLine("return CheckedIntMod((int)CoerceToInt(l), divisor);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var divisorFloat = (double)CoerceToFloat(r);");
        WriteIndent();
        _output.AppendLine("if (divisorFloat == 0) throw new MaldaLang.Interpreter.RuntimeException(\"Division by zero.\");");
        WriteIndent();
        _output.AppendLine("return (double)CoerceToFloat(l) % divisorFloat;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object OperatorNegate(object? operand)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorUnary(operand, \"__neg__\", out var overloaded)) return overloaded!;");
        WriteIndent();
        _output.AppendLine("var value = UnwrapRuntimeValue(operand);");
        WriteIndent();
        _output.AppendLine("if (IsInt(value)) return CheckedIntNegate((int)CoerceToInt(value));");
        WriteIndent();
        _output.AppendLine("if (IsFloat(value)) return -(double)CoerceToFloat(value);");
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException(\"Operand must be a number.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool OperatorEqual(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__eq__\", out var overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__req__\", out overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("if (IsString(l) && IsString(r)) return CoerceToString(l) == CoerceToString(r);");
        WriteIndent();
        _output.AppendLine("return object.Equals(l, r);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool OperatorNotEqual(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__neq__\", out var overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rneq__\", out overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("return !OperatorEqual(left, right);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool OperatorLessThan(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__lt__\", out var overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rlt__\", out overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("if (IsString(l) && IsString(r)) return string.Compare(CoerceToString(l), CoerceToString(r), System.StringComparison.Ordinal) < 0;");
        WriteIndent();
        _output.AppendLine("if (IsNumber(l) && IsNumber(r)) return (double)CoerceToFloat(l) < (double)CoerceToFloat(r);");
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException(\"Operands must be both strings or both numbers.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool OperatorLessThanOrEqual(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__le__\", out var overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rle__\", out overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("if (IsString(l) && IsString(r)) return string.Compare(CoerceToString(l), CoerceToString(r), System.StringComparison.Ordinal) <= 0;");
        WriteIndent();
        _output.AppendLine("if (IsNumber(l) && IsNumber(r)) return (double)CoerceToFloat(l) <= (double)CoerceToFloat(r);");
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException(\"Operands must be both strings or both numbers.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool OperatorGreaterThan(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__gt__\", out var overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rgt__\", out overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("if (IsString(l) && IsString(r)) return string.Compare(CoerceToString(l), CoerceToString(r), System.StringComparison.Ordinal) > 0;");
        WriteIndent();
        _output.AppendLine("if (IsNumber(l) && IsNumber(r)) return (double)CoerceToFloat(l) > (double)CoerceToFloat(r);");
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException(\"Operands must be both strings or both numbers.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool OperatorGreaterThanOrEqual(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinary(left, right, \"__ge__\", out var overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("if (TryInvokeOperatorBinaryReversed(left, right, \"__rge__\", out overloaded)) return CoerceToBool(UnwrapRuntimeValue(overloaded));");
        WriteIndent();
        _output.AppendLine("var l = UnwrapRuntimeValue(left);");
        WriteIndent();
        _output.AppendLine("var r = UnwrapRuntimeValue(right);");
        WriteIndent();
        _output.AppendLine("if (IsString(l) && IsString(r)) return string.Compare(CoerceToString(l), CoerceToString(r), System.StringComparison.Ordinal) >= 0;");
        WriteIndent();
        _output.AppendLine("if (IsNumber(l) && IsNumber(r)) return (double)CoerceToFloat(l) >= (double)CoerceToFloat(r);");
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException(\"Operands must be both strings or both numbers.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.Append("public static object UnwrapRuntimeValue(object? value)");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue");
        _output.AppendLine();
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (open)");
        _output.AppendLine();
        _indentLevel++;
        WriteIndent();
        _output.Append("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (if)");
        _output.AppendLine();
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (if open)");
        _output.AppendLine();
        _indentLevel++;
        WriteIndent();
        _output.Append("return rv.Type switch");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (switch)");
        _output.AppendLine();
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (switch open)");
        _output.AppendLine();
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Integer => rv.AsInteger(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Float => rv.AsFloat(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.String => rv.AsString(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Boolean => rv.AsBoolean(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Array => rv.AsArray(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Object => rv.AsObject(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Variant => rv,");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Task => rv,");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Function => rv,");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Null => null!,");
        WriteIndent();
        _output.AppendLine("_ => rv");
        _indentLevel--;
        WriteIndent();
        _output.Append("};");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (switch close)");
        _output.AppendLine();
        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (if close)");
        _output.AppendLine();
        WriteIndent();
        _output.Append("return value;");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (return)");
        _output.AppendLine();
        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(GenerateRuntimeHelpers) + "." + "UnwrapRuntimeValue" + " (close)");
        _output.AppendLine();
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static async Task<object> UnwrapRuntimeValueAsync(Task<MaldaLang.Interpreter.RuntimeValue> task)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var rv = await task;");
        WriteIndent();
        _output.AppendLine("return UnwrapRuntimeValue(rv);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static async Task<object?> UnwrapTaskAsync(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Task)");
        WriteIndent();
        _output.AppendLine("    return UnwrapRuntimeValue(await rv.AsTask());");
        WriteIndent();
        _output.AppendLine("throw new System.InvalidOperationException(\"await requires a task value.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static async System.Threading.Tasks.Task<MaldaLang.Interpreter.RuntimeValue> WrapObjectTaskAsRuntimeValueTask<T>(System.Threading.Tasks.Task<T> task)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var result = await task;");
        WriteIndent();
        _output.AppendLine("return ToRuntimeValue(result);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static string RepeatString(string str, int count)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (count <= 0) return \"\";");
        WriteIndent();
        _output.AppendLine("return string.Concat(System.Linq.Enumerable.Repeat(str, count));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("private static List<object> MaterializeRuntimeValueList(System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> runtimeValueList)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var result = new List<object>(runtimeValueList.Count);");
        WriteIndent();
        _output.AppendLine("foreach (var rv in runtimeValueList)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result.Add(rv.Type switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Integer => rv.AsInteger(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Float => rv.AsFloat(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.String => rv.AsString(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Boolean => rv.AsBoolean(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Array => GetArray(rv.AsArray()),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Object => rv.AsObject(),");
        WriteIndent();
        _output.AppendLine("_ => null");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("});");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return result;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("public static List<object> GetArray(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// First unwrap RuntimeValue if needed");
        WriteIndent();
        _output.AppendLine("var unwrapped = UnwrapRuntimeValue(value);");
        WriteIndent();
        _output.AppendLine("if (unwrapped is List<object> list)");
        WriteIndent();
        _output.AppendLine("    return list;");
        WriteIndent();
        _output.AppendLine("// One stable List<object> per List<RuntimeValue> instance (identity). Avoids fresh copies on every read so append/length stay consistent.");
        WriteIndent();
        _output.AppendLine("if (unwrapped is System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> runtimeValueList)");
        WriteIndent();
        _output.AppendLine("    return __rvListToObjectListCache.GetValue(runtimeValueList, MaterializeRuntimeValueList);");
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException($\"Value is not an array: {value?.GetType()}\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("public static List<double> CoerceToDoubleList(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value is List<double> typed) return typed;");
        WriteIndent();
        _output.AppendLine("var arr = GetArray(value);");
        WriteIndent();
        _output.AppendLine("var result = new List<double>(arr.Count);");
        WriteIndent();
        _output.AppendLine("foreach (var item in arr) result.Add((double)CoerceToFloat(item));");
        WriteIndent();
        _output.AppendLine("return result;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("public static List<double> ArrayAppendDouble(List<double> arr, object? item)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("arr.Add((double)CoerceToFloat(item));");
        WriteIndent();
        _output.AppendLine("return arr;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("public static double GetIndexedDouble(List<double> arr, object? index)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var idx = (int)CoerceToInt(index);");
        WriteIndent();
        _output.AppendLine("if (idx < 0 || idx >= arr.Count) throw new IndexOutOfRangeException($\"Index {idx} out of range for array with length {arr.Count}\");");
        WriteIndent();
        _output.AppendLine("return arr[idx];");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static object ArrayAppend(List<object> arr, object? item)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("arr.Add(item ?? new object());");
        WriteIndent();
        _output.AppendLine("return arr;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static async System.Threading.Tasks.Task<object> ArrayAppendAsync(List<object> arr, System.Threading.Tasks.Task<object> itemTask)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var item = await itemTask;");
        WriteIndent();
        _output.AppendLine("arr.Add(item ?? new object());");
        WriteIndent();
        _output.AppendLine("return arr;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static object ArrayPop(List<object> arr)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (arr.Count == 0)");
        WriteIndent();
        _output.AppendLine("    throw new InvalidOperationException(\"Cannot pop from empty array\");");
        WriteIndent();
        _output.AppendLine("var lastIndex = arr.Count - 1;");
        WriteIndent();
        _output.AppendLine("var last = arr[lastIndex];");
        WriteIndent();
        _output.AppendLine("arr.RemoveAt(lastIndex);");
        WriteIndent();
        _output.AppendLine("return last;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static object ArrayShift(List<object> arr)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (arr.Count == 0)");
        WriteIndent();
        _output.AppendLine("    throw new InvalidOperationException(\"Cannot shift from empty array\");");
        WriteIndent();
        _output.AppendLine("var first = arr[0];");
        WriteIndent();
        _output.AppendLine("arr.RemoveAt(0);");
        WriteIndent();
        _output.AppendLine("return first;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static List<object> ArrayConcat(List<object> arr1, List<object> arr2)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var combined = new List<object>(arr1.Count + arr2.Count);");
        WriteIndent();
        _output.AppendLine("combined.AddRange(arr1);");
        WriteIndent();
        _output.AppendLine("combined.AddRange(arr2);");
        WriteIndent();
        _output.AppendLine("return combined;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static List<object> ArraySortWithCompare(List<object> list, System.Func<object, object, System.Threading.Tasks.Task<object>> compare)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var copy = new List<object>(list);");
        WriteIndent();
        _output.AppendLine("copy.Sort((a, b) => (int)CoerceToInt(compare(a, b).GetAwaiter().GetResult()));");
        WriteIndent();
        _output.AppendLine("return copy;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static int NormalizeArrayIndex(List<object> list, int index)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return index < 0 ? list.Count + index : index;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object? ArrayGet(List<object> list, int index, object? fallback = null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var normalized = NormalizeArrayIndex(list, index);");
        WriteIndent();
        _output.AppendLine("if (normalized < 0 || normalized >= list.Count) return fallback;");
        WriteIndent();
        _output.AppendLine("return list[normalized];");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool AreObjectsEqual(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (left == null && right == null) return true;");
        WriteIndent();
        _output.AppendLine("if (left == null || right == null) return false;");
        WriteIndent();
        _output.AppendLine("if (left is int li && right is double rd) return li == rd;");
        WriteIndent();
        _output.AppendLine("if (left is double ld && right is int ri) return ld == ri;");
        WriteIndent();
        _output.AppendLine("if (left is int li2 && right is long rl) return li2 == rl;");
        WriteIndent();
        _output.AppendLine("if (left is long ll && right is int ri2) return ll == ri2;");
        WriteIndent();
        _output.AppendLine("if (left is float lf && right is int ri3) return lf == ri3;");
        WriteIndent();
        _output.AppendLine("if (left is int li3 && right is float rf) return li3 == rf;");
        WriteIndent();
        _output.AppendLine("if (left is float lf2 && right is double rd2) return lf2 == rd2;");
        WriteIndent();
        _output.AppendLine("if (left is double ld2 && right is float rf2) return ld2 == rf2;");
        WriteIndent();
        _output.AppendLine("return object.Equals(left, right);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static int CompareObjects(object? left, object? right)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (left == null && right == null) return 0;");
        WriteIndent();
        _output.AppendLine("if (left == null) return -1;");
        WriteIndent();
        _output.AppendLine("if (right == null) return 1;");
        WriteIndent();
        _output.AppendLine("if ((left is int || left is long || left is float || left is double) && (right is int || right is long || right is float || right is double))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return ((double)CoerceToFloat(left)).CompareTo((double)CoerceToFloat(right));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return string.Compare(CoerceToString(left), CoerceToString(right), StringComparison.Ordinal);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static async System.Threading.Tasks.Task<object> CallObjectMethod(object? obj, string methodName, List<object> args)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (obj == null) throw new InvalidOperationException(\"Cannot access methods of null.\");");
        WriteIndent();
        _output.AppendLine("if (obj is System.Collections.Generic.Dictionary<string, object?> nativeDict)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("case \"containsKey\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return false;");
        WriteIndent();
        _output.AppendLine("return nativeDict.ContainsKey(CoerceToString(args[0]));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"keys\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return nativeDict.Keys.Select(k => (object)k).ToList();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"values\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return nativeDict.Values.Select(v => v ?? new object()).ToList();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> runtimeValueList)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("obj = runtimeValueList.Select(v => UnwrapRuntimeValue(v) ?? new object()).ToList();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is List<object> list)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("case \"includes\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return false;");
        WriteIndent();
        _output.AppendLine("return list.Any(item => AreObjectsEqual(item, args[0]));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"indexOf\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return -1;");
        WriteIndent();
        _output.AppendLine("for (var i = 0; i < list.Count; i++)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (AreObjectsEqual(list[i], args[0])) return i;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return -1;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"join\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var separator = args.Count > 0 ? CoerceToString(args[0]) : \",\";");
        WriteIndent();
        _output.AppendLine("return string.Join(separator, list.Select(CoerceToString));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"reverse\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("list.Reverse();");
        WriteIndent();
        _output.AppendLine("return list;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"popOrNull\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (list.Count == 0) return null;");
        WriteIndent();
        _output.AppendLine("return ArrayPop(list);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"shiftOrNull\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (list.Count == 0) return null;");
        WriteIndent();
        _output.AppendLine("return ArrayShift(list);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"get\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return null;");
        WriteIndent();
        _output.AppendLine("return ArrayGet(list, (int)CoerceToInt(args[0]), args.Count > 1 ? args[1] : null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"at\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return null;");
        WriteIndent();
        _output.AppendLine("return ArrayGet(list, (int)CoerceToInt(args[0]));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"slice\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return new List<object>();");
        WriteIndent();
        _output.AppendLine("var start = (int)CoerceToInt(args[0]);");
        WriteIndent();
        _output.AppendLine("var end = args.Count > 1 ? (int)CoerceToInt(args[1]) : list.Count;");
        WriteIndent();
        _output.AppendLine("start = NormalizeArrayIndex(list, start);");
        WriteIndent();
        _output.AppendLine("end = NormalizeArrayIndex(list, end);");
        WriteIndent();
        _output.AppendLine("start = System.Math.Max(0, System.Math.Min(start, list.Count));");
        WriteIndent();
        _output.AppendLine("end = System.Math.Max(0, System.Math.Min(end, list.Count));");
        WriteIndent();
        _output.AppendLine("end = System.Math.Max(start, end);");
        WriteIndent();
        _output.AppendLine("return list.GetRange(start, end - start);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"sort\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count > 0 && args[0] is System.Func<object, object, System.Threading.Tasks.Task<object>> compare)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("list.Sort((a, b) => (int)CoerceToInt(compare(a, b).GetAwaiter().GetResult()));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("list.Sort(CompareObjects);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return list;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.Interpreter.ObjectInstance instance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Convert arguments to RuntimeValue");
        WriteIndent();
        _output.AppendLine("var runtimeArgs = args.Select(a => a switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("null => MaldaLang.Interpreter.RuntimeValue.Null(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue runtimeValue => runtimeValue,");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.FunctionValue functionValue => MaldaLang.Interpreter.RuntimeValue.Function(functionValue),");
        WriteIndent();
        _output.AppendLine("int i => MaldaLang.Interpreter.RuntimeValue.Integer(i),");
        WriteIndent();
        _output.AppendLine("long l => MaldaLang.Interpreter.RuntimeValue.Integer((int)l),");
        WriteIndent();
        _output.AppendLine("double d => MaldaLang.Interpreter.RuntimeValue.Float(d),");
        WriteIndent();
        _output.AppendLine("float f => MaldaLang.Interpreter.RuntimeValue.Float(f),");
        WriteIndent();
        _output.AppendLine("string s => MaldaLang.Interpreter.RuntimeValue.String(s),");
        WriteIndent();
        _output.AppendLine("bool b => MaldaLang.Interpreter.RuntimeValue.Boolean(b),");
        WriteIndent();
        _output.AppendLine("System.Collections.Generic.Dictionary<string, object?> dictObj => MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.Interpreter.DictionaryInstance(dictObj.ToDictionary(e => e.Key, e => RuntimeHelpers.ToRuntimeValue(e.Value)))),");
        WriteIndent();
        _output.AppendLine("System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object?>> kvpsObj => MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.Interpreter.DictionaryInstance(kvpsObj.ToDictionary(e => e.Key, e => RuntimeHelpers.ToRuntimeValue(e.Value)))),");
        WriteIndent();
        _output.AppendLine("System.Collections.IEnumerable seq => MaldaLang.Interpreter.RuntimeValue.Array(seq.Cast<object?>().Select(v => RuntimeHelpers.ToRuntimeValue(v)).ToList()),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ObjectInstance oi => MaldaLang.Interpreter.RuntimeValue.Object(oi),");
        WriteIndent();
        _output.AppendLine("object other => MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.BuiltIns.DotNetObjectInstance(other))");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}).ToList();");
        WriteIndent();
        _output.AppendLine("// Call the method");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? result = null;");
        WriteIndent();
        _output.AppendLine("if (instance is MaldaLang.BuiltIns.RestServerInstance restServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = restServer.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.HttpServerInstance httpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = httpServer.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.RestClientInstance restClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = restClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.SerialConnectionInstance serialConnection)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = serialConnection.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ArduinoConnectionInstance arduinoConnection)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = arduinoConnection.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.GraphMemoryInstance graphMemory)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var transpiledInterpreter = MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter();");
        WriteIndent();
        _output.AppendLine("graphMemory.SetInterpreter(transpiledInterpreter);");
        WriteIndent();
        _output.AppendLine("result = graphMemory.CallMethod(methodName, runtimeArgs, transpiledInterpreter);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.AgentInstance agent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = agent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.CodingAgentInstance codingAgent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = codingAgent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.GitAgentInstance gitAgent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = gitAgent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.DevAgentInstance devAgent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = devAgent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.MALDACodingAgentInstance splCodingAgent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = splCodingAgent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ConversationInstance conv)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = conv.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.LLMClientInstance llmClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = llmClient.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.LlamaCppClientInstance llamaCppClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = llamaCppClient.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance llmBridge)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = llmBridge.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ToolInstance tool)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = tool.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.DotNetObjectInstance dotNetObj)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = dotNetObj.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.MCPClientInstance mcpClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = mcpClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.MCPServerInstance mcpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = mcpServer.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.SqlServerClientInstance sqlServerClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = sqlServerClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.PostgresClientInstance postgresClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = postgresClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.SqliteClientInstance sqliteClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = sqliteClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.AnsiConsoleInstance ansiConsole)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Check if it's an async method");
        WriteIndent();
        _output.AppendLine("if (methodName == \"status\" || methodName == \"prompt\" || methodName == \"progress\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = await ansiConsole.CallMethodAsync(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = ansiConsole.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.UiFrameworkInstance ui)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = ui.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.Interpreter.GraphInstance graph)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = graph.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.Interpreter.ArrayInstance array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = array.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.JsonObject jsonObject)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = jsonObject.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.Interpreter.DictionaryInstance dict)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = dict.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ProgressContextWrapper progressCtx)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = progressCtx.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ResponseContextInstance responseContext)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = responseContext.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.MiddlewareNextCallbackInstance nextCallback)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = nextCallback.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ComposedPipeInstance composedPipe)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = composedPipe.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// For other ObjectInstance types, return null");
        WriteIndent();
        _output.AppendLine("if (result == null) return null;");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue resultValue = result!;");
        WriteIndent();
        _output.AppendLine("return resultValue.Type switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Integer => resultValue.AsInteger(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Float => resultValue.AsFloat(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.String => resultValue.AsString(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Boolean => resultValue.AsBoolean(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Array => resultValue.AsArray(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Object => resultValue.AsObject(),");
        WriteIndent();
        _output.AppendLine("_ => null");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Reflection fallback for transpiled class instances");
        WriteIndent();
        _output.AppendLine("var targetType = obj.GetType();");
        WriteIndent();
        _output.AppendLine("var methodCandidate = targetType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine(".FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Count);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("if (methodCandidate != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var parameters = methodCandidate.GetParameters();");
        WriteIndent();
        _output.AppendLine("var invokeArgs = new object?[args.Count];");
        WriteIndent();
        _output.AppendLine("for (int i = 0; i < args.Count; i++)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var parameterType = parameters[i].ParameterType;");
        WriteIndent();
        _output.AppendLine("var argValue = args[i];");
        WriteIndent();
        _output.AppendLine("if (parameterType == typeof(object) || argValue == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = argValue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType == typeof(int))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = (int)CoerceToInt(argValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType == typeof(double))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = (double)CoerceToFloat(argValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType == typeof(float))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = (float)(double)CoerceToFloat(argValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType == typeof(string))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = CoerceToString(argValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType.IsInstanceOfType(argValue))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = argValue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = argValue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var invokeResult = methodCandidate.Invoke(obj, invokeArgs);");
        WriteIndent();
        _output.AppendLine("if (invokeResult is System.Threading.Tasks.Task<object> objectTask)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return await objectTask;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (invokeResult is System.Threading.Tasks.Task anyTask)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("await anyTask;");
        WriteIndent();
        _output.AppendLine("var resultProperty = anyTask.GetType().GetProperty(\"Result\");");
        WriteIndent();
        _output.AppendLine("return resultProperty != null ? resultProperty.GetValue(anyTask) : null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return invokeResult;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static object CallVoidMethod(object? obj, string methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.BuiltIns.HttpServerInstance httpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("case \"start\": httpServer.Start(); break;");
        WriteIndent();
        _output.AppendLine("case \"stop\": httpServer.Stop(); break;");
        WriteIndent();
        _output.AppendLine("case \"clearCache\": httpServer.ClearCache(); break;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (obj is MaldaLang.BuiltIns.MCPServerInstance mcpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("case \"start\": mcpServer.CallMethod(\"start\", new List<MaldaLang.Interpreter.RuntimeValue>()); break;");
        WriteIndent();
        _output.AppendLine("case \"stop\": mcpServer.CallMethod(\"stop\", new List<MaldaLang.Interpreter.RuntimeValue>()); break;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        // Helper to route .stop() calls either to ActorsRuntime.Stop for actors
        // or to regular object methods (e.g., HttpServerInstance, MCPServerInstance).
        WriteIndent();
        _output.AppendLine("public static void CallActorOrVoidStop(object? target)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (target == null) return;");
        WriteIndent();
        _output.AppendLine("// Check if target is an ActorRef (struct, may be boxed)");
        WriteIndent();
        _output.AppendLine("if (target is ActorRef actorRef)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("ActorsRuntime.Stop(actorRef);");
        WriteIndent();
        _output.AppendLine("return;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// For non-actor objects, try calling their stop method");
        WriteIndent();
        _output.AppendLine("CallVoidMethod(target, \"stop\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static bool TryConvertDictionaryLikeToRuntimeValue(object value, out MaldaLang.Interpreter.RuntimeValue runtimeValue)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value is System.Collections.IDictionary dictionary)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var entries = new System.Collections.Generic.Dictionary<string, MaldaLang.Interpreter.RuntimeValue>(System.StringComparer.Ordinal);");
        WriteIndent();
        _output.AppendLine("foreach (System.Collections.DictionaryEntry entry in dictionary)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var key = entry.Key == null ? string.Empty : CoerceToString(entry.Key);");
        WriteIndent();
        _output.AppendLine("entries[key] = ToRuntimeValue(entry.Value);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("runtimeValue = MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.Interpreter.DictionaryInstance(entries));");
        WriteIndent();
        _output.AppendLine("return true;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("var enumerableInterface = value.GetType().GetInterfaces()");
        WriteIndent();
        _output.AppendLine("    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IEnumerable<>) && i.GetGenericArguments()[0].IsGenericType && i.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(System.Collections.Generic.KeyValuePair<,>) && i.GetGenericArguments()[0].GetGenericArguments()[0] == typeof(string));");
        WriteIndent();
        _output.AppendLine("if (enumerableInterface != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var pairType = enumerableInterface.GetGenericArguments()[0];");
        WriteIndent();
        _output.AppendLine("var keyProperty = pairType.GetProperty(\"Key\");");
        WriteIndent();
        _output.AppendLine("var valueProperty = pairType.GetProperty(\"Value\");");
        WriteIndent();
        _output.AppendLine("if (keyProperty != null && valueProperty != null && value is System.Collections.IEnumerable kvpEnumerable)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var entries = new System.Collections.Generic.Dictionary<string, MaldaLang.Interpreter.RuntimeValue>(System.StringComparer.Ordinal);");
        WriteIndent();
        _output.AppendLine("foreach (var entry in kvpEnumerable)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (entry == null) continue;");
        WriteIndent();
        _output.AppendLine("var keyObj = keyProperty.GetValue(entry);");
        WriteIndent();
        _output.AppendLine("if (keyObj is not string key) continue;");
        WriteIndent();
        _output.AppendLine("entries[key] = ToRuntimeValue(valueProperty.GetValue(entry));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("runtimeValue = MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.Interpreter.DictionaryInstance(entries));");
        WriteIndent();
        _output.AppendLine("return true;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("runtimeValue = MaldaLang.Interpreter.RuntimeValue.Null();");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static bool TryConvertNativeObjectToRuntimeValue(object value, out MaldaLang.Interpreter.RuntimeValue runtimeValue)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var type = value.GetType();");
        WriteIndent();
        _output.AppendLine("if (typeof(System.Delegate).IsAssignableFrom(type) || value is System.Type or System.Reflection.MemberInfo or System.Exception or System.IO.Stream)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("runtimeValue = MaldaLang.Interpreter.RuntimeValue.Null();");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("var properties = type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)");
        WriteIndent();
        _output.AppendLine("    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)");
        WriteIndent();
        _output.AppendLine("    .ToList();");
        WriteIndent();
        _output.AppendLine("var fields = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).ToList();");
        WriteIndent();
        _output.AppendLine("if (properties.Count == 0 && fields.Count == 0)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("runtimeValue = MaldaLang.Interpreter.RuntimeValue.Null();");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("var entries = new System.Collections.Generic.Dictionary<string, MaldaLang.Interpreter.RuntimeValue>(System.StringComparer.Ordinal);");
        WriteIndent();
        _output.AppendLine("foreach (var property in properties)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("entries[property.Name] = ToRuntimeValue(property.GetValue(value));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("foreach (var field in fields)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (!entries.ContainsKey(field.Name)) entries[field.Name] = ToRuntimeValue(field.GetValue(value));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("runtimeValue = MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.Interpreter.DictionaryInstance(entries));");
        WriteIndent();
        _output.AppendLine("return true;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static MaldaLang.Interpreter.RuntimeValue WrapTranspiledDelegate(System.Delegate del)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (del is System.Func<object, System.Threading.Tasks.Task<object>> typed)");
        WriteIndent();
        _output.AppendLine("    return MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue { TranspiledDelegate = typed });");
        WriteIndent();
        _output.AppendLine("return MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("TranspiledDelegate = async arg =>");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var result = del.DynamicInvoke(arg);");
        WriteIndent();
        _output.AppendLine("if (result is System.Threading.Tasks.Task<object> taskObj)");
        WriteIndent();
        _output.AppendLine("    return await taskObj;");
        WriteIndent();
        _output.AppendLine("if (result is System.Threading.Tasks.Task task)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("await task;");
        WriteIndent();
        _output.AppendLine("var taskType = task.GetType();");
        WriteIndent();
        _output.AppendLine("if (taskType.IsGenericType)");
        WriteIndent();
        _output.AppendLine("    return taskType.GetProperty(\"Result\")?.GetValue(task);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return result;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("});");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static MaldaLang.Interpreter.RuntimeValue ToRuntimeValue(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value == null) return MaldaLang.Interpreter.RuntimeValue.Null();");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue runtimeValue) return runtimeValue;");
        WriteIndent();
        _output.AppendLine("if (value is int i) return MaldaLang.Interpreter.RuntimeValue.Integer(i);");
        WriteIndent();
        _output.AppendLine("if (value is long l) return MaldaLang.Interpreter.RuntimeValue.Integer((int)l);");
        WriteIndent();
        _output.AppendLine("if (value is double d) return MaldaLang.Interpreter.RuntimeValue.Float(d);");
        WriteIndent();
        _output.AppendLine("if (value is float f) return MaldaLang.Interpreter.RuntimeValue.Float(f);");
        WriteIndent();
        _output.AppendLine("if (value is string s) return MaldaLang.Interpreter.RuntimeValue.String(s);");
        WriteIndent();
        _output.AppendLine("if (value is bool b) return MaldaLang.Interpreter.RuntimeValue.Boolean(b);");
        WriteIndent();
        _output.AppendLine("if (value is short sh) return MaldaLang.Interpreter.RuntimeValue.Integer(sh);");
        WriteIndent();
        _output.AppendLine("if (value is byte bt) return MaldaLang.Interpreter.RuntimeValue.Integer(bt);");
        WriteIndent();
        _output.AppendLine("if (value is decimal dm) return MaldaLang.Interpreter.RuntimeValue.Float((double)dm);");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.GraphInstance gi) return MaldaLang.Interpreter.RuntimeValue.Object(gi);");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.ObjectInstance oi) return MaldaLang.Interpreter.RuntimeValue.Object(oi);");
        WriteIndent();
        _output.AppendLine("if (TryConvertDictionaryLikeToRuntimeValue(value, out var dictionaryValue)) return dictionaryValue;");
        WriteIndent();
        _output.AppendLine("if (value is System.Collections.IEnumerable seq && value is not string) return MaldaLang.Interpreter.RuntimeValue.Array(seq.Cast<object?>().Select(ToRuntimeValue).ToList());");
        WriteIndent();
        _output.AppendLine("if (TryConvertNativeObjectToRuntimeValue(value, out var objectValue)) return objectValue;");
        WriteIndent();
        _output.AppendLine("if (value is System.Func<object, System.Threading.Tasks.Task<object>> funcDelegate)");
        WriteIndent();
        _output.AppendLine("    return MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue { TranspiledDelegate = funcDelegate });");
        WriteIndent();
        _output.AppendLine("if (value is System.Delegate del) return WrapTranspiledDelegate(del);");
        WriteIndent();
        _output.AppendLine("return MaldaLang.Interpreter.RuntimeValue.Null();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        WriteIndent();
        _output.AppendLine("public static object? GetObjectMemberNullSafe(object? obj, string memberName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (obj == null) return null;");
        WriteIndent();
        _output.AppendLine("return GetObjectMember(obj, memberName);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object? GetIndexedNullSafe(object? target, object? index)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (target == null) return null;");
        WriteIndent();
        _output.AppendLine("return GetIndexed(target, index);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static object GetObjectMember(object? obj, string memberName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (obj == null) throw new InvalidOperationException(\"Cannot access members of null.\");");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.Interpreter.RuntimeValue __rvMember)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __unwrappedMember = UnwrapRuntimeValue(__rvMember);");
        WriteIndent();
        _output.AppendLine("if (__unwrappedMember is MaldaLang.Interpreter.ObjectInstance)");
        WriteIndent();
        _output.AppendLine("obj = __unwrappedMember;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle HttpServerInstance public properties");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.BuiltIns.HttpServerInstance httpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (memberName == \"webDirectory\") return httpServer.WebDirectory;");
        WriteIndent();
        _output.AppendLine("if (memberName == \"isRunning\") return httpServer.IsRunning;");
        WriteIndent();
        _output.AppendLine("if (memberName == \"port\") return httpServer.Port;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle RestClientInstance public properties");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.BuiltIns.RestClientInstance restClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// RestClientInstance properties are accessed via Get() method");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is System.Collections.Generic.Dictionary<string, object?> dict && dict.TryGetValue(memberName, out var dictValue))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return dictValue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.Interpreter.ObjectInstance instance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue value = instance.Get(memberName, null);");
        WriteIndent();
        _output.AppendLine("// Convert RuntimeValue to object");
        WriteIndent();
        _output.AppendLine("return value.Type switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Integer => value.AsInteger(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Float => value.AsFloat(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.String => value.AsString(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Boolean => value.AsBoolean(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Array => value.AsArray(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Object => value.AsObject(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Function => value.AsFunction(),");
        WriteIndent();
        _output.AppendLine("_ => null");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Fallback for transpiled class instances via reflection");
        WriteIndent();
        _output.AppendLine("var type = obj.GetType();");
        WriteIndent();
        _output.AppendLine("var field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);");
        WriteIndent();
        _output.AppendLine("if (field != null) return field.GetValue(obj);");
        WriteIndent();
        _output.AppendLine("var property = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);");
        WriteIndent();
        _output.AppendLine("if (property != null && property.CanRead) return property.GetValue(obj);");
        WriteIndent();
        _output.AppendLine("return null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static MaldaLang.Interpreter.RuntimeValue GetPromptObjectField(object? bodyValue, string fieldName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (bodyValue is System.Collections.Generic.Dictionary<string, object?> dict && dict.TryGetValue(fieldName, out var raw) && raw != null)");
        WriteIndent();
        _output.AppendLine("return ToRuntimeValue(raw);");
        WriteIndent();
        _output.AppendLine("var unwrapped = UnwrapRuntimeValue(bodyValue);");
        WriteIndent();
        _output.AppendLine("if (unwrapped is MaldaLang.BuiltIns.JsonObject jsonObj)");
        WriteIndent();
        _output.AppendLine("return jsonObj.Get(fieldName);");
        WriteIndent();
        _output.AppendLine("if (unwrapped is MaldaLang.Interpreter.DictionaryInstance dictInst && dictInst.TryGetEntry(fieldName, out var entry))");
        WriteIndent();
        _output.AppendLine("return entry;");
        WriteIndent();
        _output.AppendLine("return MaldaLang.Interpreter.RuntimeValue.Null();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static void SetObjectMember(object? obj, string memberName, object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (obj == null) return;");
        WriteIndent();
        _output.AppendLine("if (obj is System.Collections.Generic.Dictionary<string, object?> dict)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("dict[memberName] = value;");
        WriteIndent();
        _output.AppendLine("return;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.Interpreter.ObjectInstance instance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var runtimeValue = value is MaldaLang.Interpreter.RuntimeValue rv ? rv : RuntimeHelpers.ToRuntimeValue(value);");
        WriteIndent();
        _output.AppendLine("instance.Set(memberName, runtimeValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var type = obj.GetType();");
        WriteIndent();
        _output.AppendLine("var field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);");
        WriteIndent();
        _output.AppendLine("if (field != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("field.SetValue(obj, value);");
        WriteIndent();
        _output.AppendLine("return;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var property = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);");
        WriteIndent();
        _output.AppendLine("if (property != null && property.CanWrite)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("property.SetValue(obj, value);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("public static async System.Threading.Tasks.Task<object> CallFunction(object? func, object? arg)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (func == null) throw new InvalidOperationException(\"Cannot call null function\");");
        WriteIndent();
        _output.AppendLine("if (func is MaldaLang.Interpreter.RuntimeValue runtimeValue && runtimeValue.Type == MaldaLang.Interpreter.ValueType.Function)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("func = runtimeValue.AsFunction();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (func is MaldaLang.Interpreter.FunctionValue functionValue)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (functionValue.BuiltInInstance != null && !string.IsNullOrEmpty(functionValue.BuiltInMethod))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var callbackArgs = new List<object>();");
        WriteIndent();
        _output.AppendLine("if (arg != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("callbackArgs.Add(arg);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return await CallObjectMethod(functionValue.BuiltInInstance, functionValue.BuiltInMethod!, callbackArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException(\"Only built-in function callbacks can be invoked from transpiled CallFunction.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (func is System.Func<object, System.Threading.Tasks.Task<object>> funcDelegate)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var task = funcDelegate(arg);");
        WriteIndent();
        _output.AppendLine("return await task;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException($\"Cannot call function: {func.GetType()}\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static T BlockOn<T>(System.Threading.Tasks.Task<T> task)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return task.GetAwaiter().GetResult();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        // Dictionary helper methods
        WriteIndent();
        _output.AppendLine("public static System.Collections.Generic.Dictionary<string, object?> GetDictionary(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// First unwrap RuntimeValue if needed");
        WriteIndent();
        _output.AppendLine("var unwrapped = UnwrapRuntimeValue(value);");
        WriteIndent();
        _output.AppendLine("if (unwrapped is System.Collections.Generic.Dictionary<string, object?> dict)");
        WriteIndent();
        _output.AppendLine("    return dict;");
        WriteIndent();
        _output.AppendLine("if (unwrapped is MaldaLang.Interpreter.DictionaryInstance dictInstance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Convert to Dictionary<string, object?> using RuntimeValue conversion");
        WriteIndent();
        _output.AppendLine("var result = new System.Collections.Generic.Dictionary<string, object?>();");
        WriteIndent();
        _output.AppendLine("foreach (var kvp in dictInstance.GetEntries())");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result[kvp.Key] = UnwrapRuntimeValue(kvp.Value);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return result;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException($\"Value is not a dictionary: {value?.GetType()}\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static object? DictionaryGet(object? dictValue, object? keyValue)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var dict = GetDictionary(dictValue);");
        WriteIndent();
        _output.AppendLine("var key = CoerceToString(keyValue);");
        WriteIndent();
        _output.AppendLine("return dict.TryGetValue(key, out var result) ? result : null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static void DictionarySet(object? dictValue, object? keyValue, object? newValue)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var dict = GetDictionary(dictValue);");
        WriteIndent();
        _output.AppendLine("var key = CoerceToString(keyValue);");
        WriteIndent();
        _output.AppendLine("dict[key] = newValue!;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static bool IsArray(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return value is List<object> || value is System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> || value is MaldaLang.Interpreter.RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Array;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("/// <summary>length() for transpiled code: array element count or string character count.</summary>");
        WriteIndent();
        _output.AppendLine("public static int BuiltInLength(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var unwrapped = UnwrapRuntimeValue(value);");
        WriteIndent();
        _output.AppendLine("if (IsArray(unwrapped))");
        WriteIndent();
        _output.AppendLine("    return GetArray(unwrapped).Count;");
        WriteIndent();
        _output.AppendLine("return CoerceToString(unwrapped).Length;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static bool IsObject(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv)");
        WriteIndent();
        _output.AppendLine("    return rv.Type == MaldaLang.Interpreter.ValueType.Object;");
        WriteIndent();
        _output.AppendLine("return value is MaldaLang.Interpreter.ObjectInstance or System.Collections.Generic.Dictionary<string, object?>;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static bool ObjectHasKey(object? obj, string key)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.Interpreter.RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Object)");
        WriteIndent();
        _output.AppendLine("    return rv.AsObject().GetAllKeys().Contains(key);");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.Interpreter.ObjectInstance oi)");
        WriteIndent();
        _output.AppendLine("    return oi.GetAllKeys().Contains(key);");
        WriteIndent();
        _output.AppendLine("if (obj is System.Collections.Generic.Dictionary<string, object?> dict)");
        WriteIndent();
        _output.AppendLine("    return dict.ContainsKey(key);");
        WriteIndent();
        _output.AppendLine("return false;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static object? GetIndexed(object? target, object? index)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var value = UnwrapRuntimeValue(target);");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("// Arrays: integer index");
        WriteIndent();
        _output.AppendLine("if (IsArray(value))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var arr = GetArray(value);");
        WriteIndent();
        _output.AppendLine("int i = (int)CoerceToInt(index);");
        WriteIndent();
        _output.AppendLine("if (i < 0 || i >= arr.Count)");
        WriteIndent();
        _output.AppendLine("    throw new InvalidOperationException(\"Array index out of bounds.\");");
        WriteIndent();
        _output.AppendLine("return arr[i];");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("// Dictionaries: string key");
        WriteIndent();
        _output.AppendLine("if (value is System.Collections.Generic.Dictionary<string, object?> or MaldaLang.Interpreter.DictionaryInstance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return DictionaryGet(value, index);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("// ObjectInstance (e.g. JsonObject returned by getSymbols, getParseErrors)");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.ObjectInstance objInstance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var key = CoerceToString(index);");
        WriteIndent();
        _output.AppendLine("var rv = objInstance.Get(key, null);");
        WriteIndent();
        _output.AppendLine("return UnwrapRuntimeValue(rv);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException(\"Only arrays and dictionaries can be indexed in compiled code.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static void SetIndexed(object? target, object? index, object? newValue)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var value = UnwrapRuntimeValue(target);");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("// Arrays: integer index");
        WriteIndent();
        _output.AppendLine("if (IsArray(value))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var arr = GetArray(value);");
        WriteIndent();
        _output.AppendLine("int i = (int)CoerceToInt(index);");
        WriteIndent();
        _output.AppendLine("if (i < 0 || i >= arr.Count)");
        WriteIndent();
        _output.AppendLine("    throw new InvalidOperationException(\"Array index out of bounds.\");");
        WriteIndent();
        _output.AppendLine("arr[i] = newValue!;");
        WriteIndent();
        _output.AppendLine("return;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("// Dictionaries: string key");
        WriteIndent();
        _output.AppendLine("if (value is System.Collections.Generic.Dictionary<string, object?> or MaldaLang.Interpreter.DictionaryInstance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("DictionarySet(value, index, newValue);");
        WriteIndent();
        _output.AppendLine("return;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("// ObjectInstance (e.g. JsonObject)");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.ObjectInstance objInstance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var key = CoerceToString(index);");
        WriteIndent();
        _output.AppendLine("objInstance.Set(key, ToRuntimeValue(newValue));");
        WriteIndent();
        _output.AppendLine("return;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException(\"Only arrays and dictionaries can be indexed in compiled code.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static bool IsVariant(MaldaLang.Interpreter.RuntimeValue value) => value.Type == MaldaLang.Interpreter.ValueType.Variant;");
        WriteIndent();
        _output.AppendLine("public static string GetVariantTag(MaldaLang.Interpreter.RuntimeValue value) => value.AsVariant().Tag;");
        WriteIndent();
        _output.AppendLine("public static System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> GetVariantPayload(MaldaLang.Interpreter.RuntimeValue value) => value.AsVariant().Payload;");
        WriteIndent();
        _output.AppendLine("public static MaldaLang.Interpreter.RuntimeValue MapVariantWithDelegate(MaldaLang.Interpreter.RuntimeValue input, System.Func<object, System.Threading.Tasks.Task<object>> mapper, string successTag, string failureTag)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (input.Type != MaldaLang.Interpreter.ValueType.Variant)");
        WriteIndent();
        _output.AppendLine("    throw new System.Exception(\"Expected a variant value (Ok/Err/Some/None)\");");
        WriteIndent();
        _output.AppendLine("var variant = input.AsVariant();");
        WriteIndent();
        _output.AppendLine("if (variant.Tag == failureTag) return input;");
        WriteIndent();
        _output.AppendLine("if (variant.Tag != successTag)");
        WriteIndent();
        _output.AppendLine("    throw new System.Exception($\"map() expected variant tag '{successTag}' or '{failureTag}', got '{variant.Tag}'\");");
        WriteIndent();
        _output.AppendLine("var payload = variant.Payload.Count > 0 ? UnwrapRuntimeValue(variant.Payload[0]) : null;");
        WriteIndent();
        _output.AppendLine("var mapped = mapper(payload).GetAwaiter().GetResult();");
        WriteIndent();
        _output.AppendLine("return MaldaLang.Interpreter.RuntimeValue.Variant(successTag, new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { ToRuntimeValue(mapped) });");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("private static readonly System.Threading.AsyncLocal<System.Collections.Generic.Stack<System.Collections.Generic.List<System.Func<System.Threading.Tasks.Task>>>> __deferStacks = new System.Threading.AsyncLocal<System.Collections.Generic.Stack<System.Collections.Generic.List<System.Func<System.Threading.Tasks.Task>>>>();");
        WriteIndent();
        _output.AppendLine("public static void PushDeferFrame()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (__deferStacks.Value == null)");
        WriteIndent();
        _output.AppendLine("    __deferStacks.Value = new System.Collections.Generic.Stack<System.Collections.Generic.List<System.Func<System.Threading.Tasks.Task>>>();");
        WriteIndent();
        _output.AppendLine("__deferStacks.Value.Push(new System.Collections.Generic.List<System.Func<System.Threading.Tasks.Task>>());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("public static void RegisterDefer(System.Func<System.Threading.Tasks.Task> action)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (__deferStacks.Value == null || __deferStacks.Value.Count == 0)");
        WriteIndent();
        _output.AppendLine("    throw new System.Exception(\"'defer' is only valid inside a block, function, or 'using' body.\");");
        WriteIndent();
        _output.AppendLine("__deferStacks.Value.Peek().Add(action);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("public static async System.Threading.Tasks.Task RunAndPopDeferFrameAsync()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (__deferStacks.Value == null || __deferStacks.Value.Count == 0) return;");
        WriteIndent();
        _output.AppendLine("var actions = __deferStacks.Value.Pop();");
        WriteIndent();
        _output.AppendLine("for (int i = actions.Count - 1; i >= 0; i--)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("try { await actions[i](); } catch { }");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("public static async System.Threading.Tasks.Task DisposeResourceAsync(object? value)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (value == null) return;");
        WriteIndent();
        _output.AppendLine("object? target = value;");
        WriteIndent();
        _output.AppendLine("if (value is MaldaLang.Interpreter.RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Object)");
        WriteIndent();
        _output.AppendLine("    target = rv.AsObject();");
        WriteIndent();
        _output.AppendLine("foreach (var methodName in new[] { \"dispose\", \"close\", \"disconnect\" })");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (target is MaldaLang.Interpreter.ObjectInstance obj)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (!obj.TryGet(methodName, out var member) || member == null || member.Type != MaldaLang.Interpreter.ValueType.Function)");
        WriteIndent();
        _output.AppendLine("    continue;");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("await CallObjectMethod(obj, methodName, new System.Collections.Generic.List<object>());");
        WriteIndent();
        _output.AppendLine("return;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch { }");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var method = target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);");
        WriteIndent();
        _output.AppendLine("if (method == null) continue;");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var invokeArgs = method.GetParameters().Length == 0 ? null : System.Array.Empty<object>();");
        WriteIndent();
        _output.AppendLine("var result = method.Invoke(target, invokeArgs);");
        WriteIndent();
        _output.AppendLine("if (result is System.Threading.Tasks.Task task) await task;");
        WriteIndent();
        _output.AppendLine("return;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch { }");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        // Close RuntimeHelpers class
        // After closing SetObjectMember method, _indentLevel should be 2 (inside RuntimeHelpers class)
        // We need to close the RuntimeHelpers class, so decrement to 1 (back inside Program class)
        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(GenerateRuntimeHelpers) + " (class close)");
        _output.AppendLine();
        _output.AppendLine();
    }

    private void GenerateDecoratorRegistration(List<Statement> statements)
    {
        WriteIndent();
        _output.AppendLine("private static void RegisterDecoratedFunctions()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;

        // Use reflection to find methods with Tool or MCPTool attributes and register them
        WriteIndent();
        _output.AppendLine("var assembly = typeof(Program).Assembly;");
        WriteIndent();
        _output.AppendLine("var methods = typeof(Program).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);");
        WriteIndent();
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("foreach (var method in methods)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Check for custom attributes (decorators)");
        WriteIndent();
        _output.AppendLine("var attributes = method.GetCustomAttributes(false);");
        WriteIndent();
        _output.AppendLine("string? routeGroup = null;");
        WriteIndent();
        _output.AppendLine("string? routeVersion = null;");
        WriteIndent();
        _output.AppendLine("string? routeValidationSchema = null;");
        WriteIndent();
        _output.AppendLine("var routeMiddleware = new List<string>();");
        WriteIndent();
        _output.AppendLine("// Pre-scan metadata decorators so registration is order-independent.");
        WriteIndent();
        _output.AppendLine("foreach (var routeAttr in attributes)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var routeAttrTypeName = routeAttr.GetType().Name;");
        WriteIndent();
        _output.AppendLine("if (!routeAttrTypeName.EndsWith(\"Attribute\")) continue;");
        WriteIndent();
        _output.AppendLine("var routeDecoratorName = routeAttrTypeName.Substring(0, routeAttrTypeName.Length - 9);");
        WriteIndent();
        _output.AppendLine("var routeArgsProp = routeAttr.GetType().GetProperty(\"Arguments\");");
        WriteIndent();
        _output.AppendLine("if (routeArgsProp == null) continue;");
        WriteIndent();
        _output.AppendLine("var routeArgs = routeArgsProp.GetValue(routeAttr) as object[];");
        WriteIndent();
        _output.AppendLine("if (routeDecoratorName == \"RouteGroup\" || routeDecoratorName == \"Group\" || routeDecoratorName == \"Prefix\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs != null && routeArgs.Length > 0) routeGroup = routeArgs[0]?.ToString();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (routeDecoratorName == \"Version\" || routeDecoratorName == \"ApiVersion\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs != null && routeArgs.Length > 0) routeVersion = routeArgs[0]?.ToString();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (routeDecoratorName == \"Use\" || routeDecoratorName == \"Middleware\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("foreach (var routeMiddlewareName in routeArgs)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var routeMiddlewareText = routeMiddlewareName?.ToString();");
        WriteIndent();
        _output.AppendLine("if (!string.IsNullOrWhiteSpace(routeMiddlewareText))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("routeMiddleware.Add(routeMiddlewareText);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (routeDecoratorName == \"Validate\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs != null && routeArgs.Length > 0)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs[0] is string routeValidationText)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("routeValidationSchema = routeValidationText;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (routeArgs[0] != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("routeValidationSchema = System.Text.Json.JsonSerializer.Serialize(routeArgs[0]);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("foreach (var attr in attributes)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Get attribute type name (e.g., \"ToolAttribute\" -> \"Tool\")");
        WriteIndent();
        _output.AppendLine("var attrTypeName = attr.GetType().Name;");
        WriteIndent();
        _output.AppendLine("if (attrTypeName.EndsWith(\"Attribute\"))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var decoratorName = attrTypeName.Substring(0, attrTypeName.Length - 9);");
        WriteIndent();
        _output.AppendLine("// Get Arguments property via reflection");
        WriteIndent();
        _output.AppendLine("var argsProp = attr.GetType().GetProperty(\"Arguments\");");
        WriteIndent();
        _output.AppendLine("if (argsProp != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var args = argsProp.GetValue(attr) as object[];");
        WriteIndent();
        _output.AppendLine("if (args != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Handle PAGE/COMPONENT/LIVE decorators for HttpServer routes");
        WriteIndent();
        _output.AppendLine("if (decoratorName == \"PAGE\" || decoratorName == \"COMPONENT\" || decoratorName == \"LIVE\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Extract path from first argument");
        WriteIndent();
        _output.AppendLine("var defaultPath = decoratorName == \"COMPONENT\" ? \"/components/\" + method.Name : (decoratorName == \"LIVE\" ? \"/components/\" + method.Name + \"/live\" : \"/\");");
        WriteIndent();
        _output.AppendLine("var path = args.Length > 0 ? (args[0]?.ToString() ?? defaultPath) : defaultPath;");
        WriteIndent();
        _output.AppendLine("// Get parameter names from method");
        WriteIndent();
        _output.AppendLine("var paramNames = method.GetParameters().Select(p => p.Name ?? \"\").ToList();");
        WriteIndent();
        _output.AppendLine("// Register route with all HttpServer instances");
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.HttpServerInstance.RegisterTranspiledRoute(");
        WriteIndent();
        _output.AppendLine("    \"GET\", path, method.Name, paramNames, null, routeGroup, routeVersion, routeMiddleware, routeValidationSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle AIPAGE decorator for HttpServer routes");
        WriteIndent();
        _output.AppendLine("else if (decoratorName == \"AIPAGE\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Extract path from first argument and description from second");
        WriteIndent();
        _output.AppendLine("var path = args.Length > 0 ? (args[0]?.ToString() ?? \"/\") : \"/\";");
        WriteIndent();
        _output.AppendLine("var description = args.Length > 1 ? args[1]?.ToString() ?? \"\" : \"\";");
        WriteIndent();
        _output.AppendLine("// Get parameter names from method");
        WriteIndent();
        _output.AppendLine("var paramNames = method.GetParameters().Select(p => p.Name ?? \"\").ToList();");
        WriteIndent();
        _output.AppendLine("// Register AIPAGE route with description");
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.HttpServerInstance.RegisterTranspiledAIPage(");
        WriteIndent();
        _output.AppendLine("    path, method.Name, paramNames, description, routeGroup, routeVersion, routeMiddleware, routeValidationSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle REST API decorators (GET, POST, PUT, DELETE, PATCH, OPTIONS) and ACTION decorator");
        WriteIndent();
        _output.AppendLine("// Note: GET/POST and ACTION are also registered with HttpServer");
        WriteIndent();
        _output.AppendLine("else if (decoratorName == \"GET\" || decoratorName == \"POST\" || decoratorName == \"PUT\" || ");
        WriteIndent();
        _output.AppendLine("         decoratorName == \"DELETE\" || decoratorName == \"PATCH\" || decoratorName == \"OPTIONS\" || decoratorName == \"ACTION\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Extract path from first argument");
        WriteIndent();
        _output.AppendLine("var defaultPath = decoratorName == \"ACTION\" ? \"/components/\" + method.Name + \"/action\" : \"/\";");
        WriteIndent();
        _output.AppendLine("var path = args.Length > 0 ? (args[0]?.ToString() ?? defaultPath) : defaultPath;");
        WriteIndent();
        _output.AppendLine("// Get parameter names from method");
        WriteIndent();
        _output.AppendLine("var paramNames = method.GetParameters().Select(p => p.Name ?? \"\").ToList();");
        WriteIndent();
        _output.AppendLine("// Register route with all RestServer instances");
        WriteIndent();
        _output.AppendLine("if (decoratorName != \"ACTION\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.RestServerInstance.RegisterTranspiledRoute(");
        WriteIndent();
        _output.AppendLine("    decoratorName, path, method.Name, paramNames, null, routeGroup, routeVersion, routeMiddleware, routeValidationSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Also register with HttpServer");
        WriteIndent();
        _output.AppendLine("if (decoratorName == \"GET\" || decoratorName == \"POST\" || decoratorName == \"ACTION\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var httpMethod = decoratorName == \"ACTION\" ? \"POST\" : decoratorName;");
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.HttpServerInstance.RegisterTranspiledRoute(");
        WriteIndent();
        _output.AppendLine("    httpMethod, path, method.Name, paramNames, null, routeGroup, routeVersion, routeMiddleware, routeValidationSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle Tool decorator for tool registration");
        WriteIndent();
        _output.AppendLine("else if (decoratorName == \"Tool\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// @Tool decorator requires at least 2 arguments: name and description");
        WriteIndent();
        _output.AppendLine("if (args.Length >= 2)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var toolName = args[0]?.ToString() ?? method.Name;");
        WriteIndent();
        _output.AppendLine("var toolDescription = args[1]?.ToString() ?? \"\";");
        WriteIndent();
        _output.AppendLine("// Optional third argument: schema (ignored for transpiled tools, auto-generated)");
        WriteIndent();
        _output.AppendLine("// Register tool");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ToolSchemaGenerator.RegisterTranspiledTool(");
        WriteIndent();
        _output.AppendLine("    toolName, toolDescription, method, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle MCPTool decorator (same as Tool for now)");
        WriteIndent();
        _output.AppendLine("else if (decoratorName == \"MCPTool\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// @MCPTool decorator requires at least 2 arguments: name and description");
        WriteIndent();
        _output.AppendLine("if (args.Length >= 2)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var toolName = args[0]?.ToString() ?? method.Name;");
        WriteIndent();
        _output.AppendLine("var toolDescription = args[1]?.ToString() ?? \"\";");
        WriteIndent();
        _output.AppendLine("// Optional third argument: schema (ignored for transpiled tools, auto-generated)");
        WriteIndent();
        _output.AppendLine("// Register tool (MCPTool is registered the same way as Tool)");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ToolSchemaGenerator.RegisterTranspiledTool(");
        WriteIndent();
        _output.AppendLine("    toolName, toolDescription, method, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        // Close foreach (var method in methods) loop
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        // Close RegisterDecoratedFunctions method
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private static string GetWorkflowRunnerMethodName(string workflowName) => "__workflow_" + workflowName;
    private static string GetPropertyRunnerMethodName(string propertyName) => "__property_" + propertyName;

    private void GenerateWorkflowRegistration(List<WorkflowDeclaration> workflows)
    {
        WriteIndent();
        _output.AppendLine("private static void RegisterTranspiledWorkflows()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.ClearTranspiledWorkflowRunners();");
        foreach (var workflow in workflows)
        {
            WriteIndent();
            _output.Append("MaldaLang.BuiltIns.BuiltInFunctions.RegisterTranspiledWorkflowRunner(\"");
            _output.Append(workflow.Name);
            _output.Append("\", async (__input, __instanceId) => RuntimeHelpers.ToRuntimeValue(await ");
            _output.Append(EscapeIdentifier(GetWorkflowRunnerMethodName(workflow.Name)));
            _output.AppendLine("(RuntimeHelpers.UnwrapRuntimeValue(__input), __instanceId)));");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileWorkflow(WorkflowDeclaration workflowDecl)
    {
        WriteIndent();
        _output.Append("private static async Task<object> ");
        _output.Append(EscapeIdentifier(GetWorkflowRunnerMethodName(workflowDecl.Name)));
        _output.AppendLine("(object __workflowInput, string __workflowInstanceId)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __workflowEngine = WorkflowEngine.Instance;");
        WriteIndent();
        _output.AppendLine("var __workflowCompensations = new List<(string StepId, Func<Task<object>> Run)>();");
        if (workflowDecl.Parameters.Count > 0)
        {
            WriteIndent();
            _output.Append("object ");
            _output.Append(EscapeIdentifier(workflowDecl.Parameters[0]));
            _output.AppendLine(" = __workflowInput;");
        }

        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.EnterTranspiledWorkflowContext();");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;

        var previousCanAwait = _canAwait;
        var previousWorkflow = _isInWorkflowBody;
        _canAwait = true;
        _isInWorkflowBody = true;
        foreach (var statement in workflowDecl.Body.Statements)
        {
            TranspileStatement(statement);
        }
        _canAwait = previousCanAwait;
        _isInWorkflowBody = previousWorkflow;

        WriteIndent();
        _output.AppendLine("return null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch (MaldaLang.BuiltIns.BuiltInFunctions.TranspiledWorkflowPauseException)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch (Exception __workflowEx)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (__workflowCompensations.Count > 0)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __rootErrorJson = JsonSerializer.Serialize(new { message = __workflowEx.Message, type = __workflowEx.GetType().Name });");
        WriteIndent();
        _output.AppendLine("__workflowEngine.BeginCompensation(__workflowInstanceId, __rootErrorJson);");
        WriteIndent();
        _output.AppendLine("var __allCompensated = true;");
        WriteIndent();
        _output.AppendLine("var __compDiagnostics = new List<Dictionary<string, object?>>();");
        WriteIndent();
        _output.AppendLine("for (var __idx = __workflowCompensations.Count - 1; __idx >= 0; __idx--)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __comp = __workflowCompensations[__idx];");
        WriteIndent();
        _output.AppendLine("var __compStepName = __comp.StepId + \"__compensate\";");
        WriteIndent();
        _output.AppendLine("var __replayComp = __workflowEngine.GetLatestStepAttempt(__workflowInstanceId, __compStepName);");
        WriteIndent();
        _output.AppendLine("if (__replayComp != null && __replayComp.State == StepState.Compensated) continue;");
        WriteIndent();
        _output.AppendLine("var __latestComp = __workflowEngine.GetLatestStepAttempt(__workflowInstanceId, __compStepName);");
        WriteIndent();
        _output.AppendLine("var __compAttempt = __latestComp != null ? __latestComp.Attempt + 1 : 1;");
        WriteIndent();
        _output.AppendLine("var __compStepId = Guid.NewGuid().ToString(\"N\");");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalCompensationStart(__compStepId, __workflowInstanceId, __compStepName, __compAttempt, \"{}\");");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.EnterTranspiledWorkflowStep();");
        WriteIndent();
        _output.AppendLine("object __compResult;");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__compResult = await __comp.Run();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.ExitTranspiledWorkflowStep();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var __compOutputJson = MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"toJSON\", new List<MaldaLang.Interpreter.RuntimeValue> { RuntimeHelpers.ToRuntimeValue(__compResult) }, null).AsString();");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalCompensationSuccess(__compStepId, __workflowInstanceId, __compStepName, __compAttempt, __compOutputJson);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch (Exception __compEx)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__allCompensated = false;");
        WriteIndent();
        _output.AppendLine("var __compErrorJson = JsonSerializer.Serialize(new { type = \"CompensationError\", step = __comp.StepId, compensationStep = __compStepName, attempt = __compAttempt, message = __compEx.Message });");
        WriteIndent();
        _output.AppendLine("__compDiagnostics.Add(new Dictionary<string, object?> { [\"step\"] = __comp.StepId, [\"compensationStep\"] = __compStepName, [\"attempt\"] = __compAttempt, [\"message\"] = __compEx.Message });");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalCompensationFailure(__compStepId, __workflowInstanceId, __compStepName, __compAttempt, __compErrorJson);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var __diagJson = JsonSerializer.Serialize(new { root = __rootErrorJson, compensationDiagnostics = __compDiagnostics });");
        WriteIndent();
        _output.AppendLine("__workflowEngine.FinishCompensation(__workflowInstanceId, __allCompensated, __diagJson);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __errJson = JsonSerializer.Serialize(new { message = __workflowEx.Message, type = __workflowEx.GetType().Name });");
        WriteIndent();
        _output.AppendLine("__workflowEngine.FailInstance(__workflowInstanceId, __errJson);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("throw;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.ExitTranspiledWorkflowContext();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileStatement(Statement statement)
    {
        if (_emitLineDirectives)
        {
            EmitLineDirective(statement.Line, statement.SourceFile ?? _sourceFilePath);
        }
        WriteIndent();
        
        switch (statement)
        {
            case VarDeclStatement varDecl:
                string? varDeclProfile = null;
                if (ProfilingEnabled)
                {
                    varDeclProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileVarDecl(varDecl);
                if (varDeclProfile != null)
                {
                    EmitStatementProfileExit(varDeclProfile);
                }
                break;
            case SendStatement sendStmt:
                string? sendProfile = null;
                if (ProfilingEnabled)
                {
                    sendProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileSend(sendStmt);
                if (sendProfile != null)
                {
                    EmitStatementProfileExit(sendProfile);
                }
                break;
            case AssignmentStatement assignment:
                string? assignmentProfile = null;
                if (ProfilingEnabled)
                {
                    assignmentProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileAssignment(assignment);
                if (assignmentProfile != null)
                {
                    EmitStatementProfileExit(assignmentProfile);
                }
                break;
            case IfStatement ifStmt:
                TranspileProfiledStructuredStatement(statement, () => TranspileIf(ifStmt));
                break;
            case WhileStatement whileStmt:
                TranspileProfiledStructuredStatement(statement, () => TranspileWhile(whileStmt));
                break;
            case ForStatement forStmt:
                TranspileProfiledStructuredStatement(statement, () => TranspileFor(forStmt));
                break;
            case ForInStatement forInStmt:
                TranspileProfiledStructuredStatement(statement, () => TranspileForIn(forInStmt));
                break;
            case FunctionDeclaration funcDecl:
                // Functions are handled separately
                break;
            case ChainDeclaration:
                // Chains are transpiled as functions
                break;
            case ReturnStatement returnStmt:
                if (!ProfilingEnabled)
                {
                    TranspileReturn(returnStmt);
                    break;
                }

                _output.AppendLine("{");
                _indentLevel++;
                WriteIndent();
                var returnProfile = EmitStatementProfileStart(statement);
                WriteIndent();
                // object? so `= null` is valid (var would be CS0815)
                _output.Append("object? __maldaReturnValue = ");
                if (returnStmt.Value != null)
                {
                    TranspileExpression(returnStmt.Value);
                }
                else
                {
                    _output.Append("null");
                }
                _output.AppendLine(";");
                EmitStatementProfileExit(returnProfile);
                WriteIndent();
                _output.AppendLine("return __maldaReturnValue;");
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
                break;
            case PrintStatement printStmt:
                string? printProfile = null;
                if (ProfilingEnabled)
                {
                    printProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspilePrint(printStmt);
                if (printProfile != null)
                {
                    EmitStatementProfileExit(printProfile);
                }
                break;
            case ExpressionStatement exprStmt:
                string? expressionProfile = null;
                if (ProfilingEnabled)
                {
                    expressionProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileExpression(exprStmt.Expression);
                _output.AppendLine(";");
                if (expressionProfile != null)
                {
                    EmitStatementProfileExit(expressionProfile);
                }
                break;
            case BlockStatement block:
                TranspileProfiledStructuredStatement(statement, () => TranspileBlock(block));
                break;
            case BreakStatement:
                if (!ProfilingEnabled)
                {
                    _output.AppendLine("break;");
                    break;
                }

                _output.AppendLine("{");
                _indentLevel++;
                WriteIndent();
                var breakProfile = EmitStatementProfileStart(statement);
                EmitStatementProfileExit(breakProfile);
                WriteIndent();
                _output.AppendLine("break;");
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
                break;
            case ContinueStatement:
                if (_desugaredForContinueLabels.Count > 0)
                {
                    WriteIndent();
                    _output.Append("goto ");
                    _output.AppendLine($"{_desugaredForContinueLabels.Peek()};");
                    break;
                }

                if (!ProfilingEnabled)
                {
                    _output.AppendLine("continue;");
                    break;
                }

                _output.AppendLine("{");
                _indentLevel++;
                WriteIndent();
                var continueProfile = EmitStatementProfileStart(statement);
                EmitStatementProfileExit(continueProfile);
                WriteIndent();
                _output.AppendLine("continue;");
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
                break;
            case TryStatement tryStmt:
                TranspileProfiledStructuredStatement(statement, () => TranspileTry(tryStmt));
                break;
            case ThrowStatement throwStmt:
                if (!ProfilingEnabled)
                {
                    TranspileThrow(throwStmt);
                    break;
                }

                _output.AppendLine("{");
                _indentLevel++;
                WriteIndent();
                var throwProfile = EmitStatementProfileStart(statement);
                WriteIndent();
                _output.Append("var __maldaThrownValue = ");
                TranspileExpression(throwStmt.Exception);
                _output.AppendLine(";");
                EmitStatementProfileExit(throwProfile);
                WriteIndent();
                _output.AppendLine("throw new Exception(RuntimeHelpers.CoerceToString(__maldaThrownValue));");
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
                break;
            case DestructuringVarDecl destVarDecl:
                string? destructuringVarProfile = null;
                if (ProfilingEnabled)
                {
                    destructuringVarProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileDestructuringVarDecl(destVarDecl);
                if (destructuringVarProfile != null)
                {
                    EmitStatementProfileExit(destructuringVarProfile);
                }
                break;
            case DestructuringAssignment destAssign:
                string? destructuringAssignProfile = null;
                if (ProfilingEnabled)
                {
                    destructuringAssignProfile = EmitStatementProfileStart(statement);
                    WriteIndent();
                }
                TranspileDestructuringAssignment(destAssign);
                if (destructuringAssignProfile != null)
                {
                    EmitStatementProfileExit(destructuringAssignProfile);
                }
                break;
            case ClassDeclaration classDecl:
                // Classes are handled separately
                break;
            case PropertyDeclaration:
                // Properties are transpiled into dedicated property methods.
                break;
            case WorkflowDeclaration:
                // Workflow transpilation deferred to Sprint 5
                break;
            case ImportStatement:
            case UsingStatement:
                // Resolved at runtime or inlined via ExpandFileImportsForTranspile
                break;
            case UsingResourceStatement usingResource:
                TranspileProfiledStructuredStatement(statement, () => TranspileUsingResource(usingResource));
                break;
            case DeferStatement deferStmt:
                TranspileDefer(deferStmt);
                break;
            case WorkflowStepStatement workflowStep when _isInWorkflowBody:
                TranspileProfiledStructuredStatement(statement, () => TranspileWorkflowStep(workflowStep));
                break;
            case WorkflowApprovalStatement workflowApproval when _isInWorkflowBody:
                TranspileProfiledStructuredStatement(statement, () => TranspileWorkflowApproval(workflowApproval));
                break;
            case WorkflowAwaitSignalStatement workflowSignal when _isInWorkflowBody:
                TranspileProfiledStructuredStatement(statement, () => TranspileWorkflowAwaitSignal(workflowSignal));
                break;
        }
    }

    private void TranspileWorkflowStep(WorkflowStepStatement stmt)
    {
        var stepVar = EscapeIdentifier(stmt.StepId);
        WriteIndent();
        _output.Append("object ");
        _output.Append(stepVar);
        _output.AppendLine(" = null!;");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.Append("var __wfMaxAttempts = ");
        _output.Append(((stmt.Options?.RetryCount ?? 0) + 1).ToString());
        _output.AppendLine(";");
        WriteIndent();
        _output.Append("int? __wfTimeoutMs = ");
        _output.Append(stmt.Options?.TimeoutMs?.ToString() ?? "null");
        _output.AppendLine(";");
        WriteIndent();
        _output.AppendLine("var __wfReplay = __workflowEngine.GetReplayResult(__workflowInstanceId, \"" + stmt.StepId + "\");");
        WriteIndent();
        _output.AppendLine("if (__wfReplay != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __wfReplayJson = __wfReplay.OutputJson ?? \"null\";");
        WriteIndent();
        _output.Append(stepVar);
        _output.AppendLine(" = RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"parseJSON\", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(__wfReplayJson) }, null));");
        if (stmt.Options?.Compensate != null)
        {
            WriteIndent();
            _output.AppendLine("if (__wfReplay.State == StepState.Succeeded)");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.Append("__workflowCompensations.Add((\"");
            _output.Append(stmt.StepId);
            _output.AppendLine("\", async () => {");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.EnterTranspiledWorkflowStep();");
            WriteIndent();
            _output.AppendLine("try { return ");
            TranspileExpression(stmt.Options.Compensate);
            _output.AppendLine("; }");
            WriteIndent();
            _output.AppendLine("finally { MaldaLang.BuiltIns.BuiltInFunctions.ExitTranspiledWorkflowStep(); }");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}));");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __wfLatestAttempt = __workflowEngine.GetLatestStepAttempt(__workflowInstanceId, \"" + stmt.StepId + "\");");
        WriteIndent();
        _output.AppendLine("var __wfAttempt = __wfLatestAttempt != null ? __wfLatestAttempt.Attempt + 1 : 1;");
        WriteIndent();
        _output.AppendLine("while (__wfAttempt <= __wfMaxAttempts)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __wfStepId = Guid.NewGuid().ToString(\"N\");");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalStepStart(__wfStepId, __workflowInstanceId, \"" + stmt.StepId + "\", __wfAttempt, __wfMaxAttempts, __wfTimeoutMs, \"{}\", null);");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.EnterTranspiledWorkflowStep();");
        WriteIndent();
        _output.AppendLine("async Task<object> __wfEvalStep() { return ");
        TranspileExpression(stmt.CallExpression);
        _output.AppendLine("; }");
        WriteIndent();
        _output.AppendLine("var __wfTimedOut = false;");
        WriteIndent();
        _output.AppendLine("object __wfStepResult;");
        WriteIndent();
        _output.AppendLine("if (__wfTimeoutMs.HasValue && __wfTimeoutMs.Value > 0)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __wfStepTask = __wfEvalStep();");
        WriteIndent();
        _output.AppendLine("var __wfCompleted = await Task.WhenAny(__wfStepTask, Task.Delay(__wfTimeoutMs.Value));");
        WriteIndent();
        _output.AppendLine("if (__wfCompleted != __wfStepTask) { __wfTimedOut = true; __wfStepResult = null!; }");
        WriteIndent();
        _output.AppendLine("else { __wfStepResult = await __wfStepTask; }");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__wfStepResult = await __wfEvalStep();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (__wfTimedOut)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __wfTimeoutError = JsonSerializer.Serialize(new { type = \"StepTimeoutError\", step = \"" + stmt.StepId + "\", attempt = __wfAttempt, timeoutMs = __wfTimeoutMs, isRetryable = __wfAttempt < __wfMaxAttempts, message = \"Step '" + stmt.StepId + "' timed out\" });");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalStepTimeout(__wfStepId, __workflowInstanceId, \"" + stmt.StepId + "\", __wfAttempt, __wfTimeoutError);");
        WriteIndent();
        _output.AppendLine("if (__wfAttempt >= __wfMaxAttempts) throw new Exception(\"Step '" + stmt.StepId + "' timed out after max attempts\");");
        WriteIndent();
        _output.Append("var __wfRetryDelay = __workflowEngine.ComputeRetryDelayMs(__workflowInstanceId, \"");
        _output.Append(stmt.StepId);
        _output.Append("\", __wfAttempt, ");
        _output.Append(string.IsNullOrWhiteSpace(stmt.Options?.Backoff) ? "null" : ("\"" + stmt.Options!.Backoff + "\""));
        _output.Append(", ");
        _output.Append(stmt.Options?.DelayMs?.ToString() ?? "null");
        _output.Append(", ");
        _output.Append(stmt.Options?.MaxDelayMs?.ToString() ?? "null");
        _output.AppendLine(");");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalStepRetryScheduled(__workflowInstanceId, \"" + stmt.StepId + "\", __wfAttempt, __wfAttempt + 1, __wfRetryDelay, \"timeout\");");
        WriteIndent();
        _output.AppendLine("if (__wfRetryDelay > 0) await Task.Delay(__wfRetryDelay);");
        WriteIndent();
        _output.AppendLine("__wfAttempt++;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var __wfOutputJson = MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"toJSON\", new List<MaldaLang.Interpreter.RuntimeValue> { RuntimeHelpers.ToRuntimeValue(__wfStepResult) }, null).AsString();");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalStepSuccess(__wfStepId, __workflowInstanceId, \"" + stmt.StepId + "\", __wfAttempt, __wfOutputJson);");
        WriteIndent();
        _output.Append(stepVar);
        _output.AppendLine(" = __wfStepResult;");
        if (stmt.Options?.Compensate != null)
        {
            WriteIndent();
            _output.Append("__workflowCompensations.Add((\"");
            _output.Append(stmt.StepId);
            _output.AppendLine("\", async () => {");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.EnterTranspiledWorkflowStep();");
            WriteIndent();
            _output.AppendLine("try { return ");
            TranspileExpression(stmt.Options.Compensate);
            _output.AppendLine("; }");
            WriteIndent();
            _output.AppendLine("finally { MaldaLang.BuiltIns.BuiltInFunctions.ExitTranspiledWorkflowStep(); }");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}));");
        }
        WriteIndent();
        _output.AppendLine("break;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch (Exception __wfStepEx)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __wfErrJson = JsonSerializer.Serialize(new { message = __wfStepEx.Message, type = __wfStepEx.GetType().Name, attempt = __wfAttempt, isRetryable = __wfAttempt < __wfMaxAttempts });");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalStepFailure(__wfStepId, __workflowInstanceId, \"" + stmt.StepId + "\", __wfAttempt, __wfErrJson);");
        WriteIndent();
        _output.AppendLine("if (__wfAttempt >= __wfMaxAttempts) throw;");
        WriteIndent();
        _output.Append("var __wfRetryDelay = __workflowEngine.ComputeRetryDelayMs(__workflowInstanceId, \"");
        _output.Append(stmt.StepId);
        _output.Append("\", __wfAttempt, ");
        _output.Append(string.IsNullOrWhiteSpace(stmt.Options?.Backoff) ? "null" : ("\"" + stmt.Options!.Backoff + "\""));
        _output.Append(", ");
        _output.Append(stmt.Options?.DelayMs?.ToString() ?? "null");
        _output.Append(", ");
        _output.Append(stmt.Options?.MaxDelayMs?.ToString() ?? "null");
        _output.AppendLine(");");
        WriteIndent();
        _output.AppendLine("__workflowEngine.JournalStepRetryScheduled(__workflowInstanceId, \"" + stmt.StepId + "\", __wfAttempt, __wfAttempt + 1, __wfRetryDelay, \"failure\");");
        WriteIndent();
        _output.AppendLine("if (__wfRetryDelay > 0) await Task.Delay(__wfRetryDelay);");
        WriteIndent();
        _output.AppendLine("__wfAttempt++;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.ExitTranspiledWorkflowStep();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileWorkflowApproval(WorkflowApprovalStatement stmt)
    {
        var approvalVar = EscapeIdentifier(stmt.ApprovalId);
        WriteIndent();
        _output.Append("object ");
        _output.Append(approvalVar);
        _output.AppendLine(" = null!;");
        WriteIndent();
        _output.AppendLine("var __wfApprovalLatest = __workflowEngine.GetLatestStepAttempt(__workflowInstanceId, \"" + stmt.ApprovalId + "\");");
        WriteIndent();
        _output.AppendLine("if (__wfApprovalLatest != null && __wfApprovalLatest.State == StepState.Succeeded && !string.IsNullOrWhiteSpace(__wfApprovalLatest.OutputJson))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine(approvalVar + " = RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"parseJSON\", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(__wfApprovalLatest.OutputJson!) }, null));");
        WriteIndent();
        _output.AppendLine("string? __wfDecision = null;");
        WriteIndent();
        _output.AppendLine("using (var __wfDoc = JsonDocument.Parse(__wfApprovalLatest.OutputJson!)) { if (__wfDoc.RootElement.TryGetProperty(\"decision\", out var __wfDecisionProp) && __wfDecisionProp.ValueKind == JsonValueKind.String) __wfDecision = __wfDecisionProp.GetString(); }");
        WriteIndent();
        _output.AppendLine("if (__wfDecision == \"reject\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        if (stmt.OnReject != null)
        {
            WriteIndent();
            _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.EnterTranspiledWorkflowStep();");
            WriteIndent();
            _output.AppendLine("try {");
            _indentLevel++;
            WriteIndent();
            TranspileExpression(stmt.OnReject);
            _output.AppendLine(";");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("} finally { MaldaLang.BuiltIns.BuiltInFunctions.ExitTranspiledWorkflowStep(); }");
        }
        else
        {
            WriteIndent();
            _output.AppendLine("throw new Exception(\"Approval '" + stmt.ApprovalId + "' was rejected.\");");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (__wfDecision == \"timeout\") throw new Exception(\"Approval '" + stmt.ApprovalId + "' timed out.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __wfApprovalNameValue = ");
        TranspileExpression(stmt.ApprovalNameExpr);
        _output.AppendLine(";");
        WriteIndent();
        _output.AppendLine("var __wfApprovalName = RuntimeHelpers.CoerceToString(__wfApprovalNameValue);");
        WriteIndent();
        _output.Append("var __wfApprovalPayloadValue = ");
        TranspileExpression(stmt.PayloadExpr);
        _output.AppendLine(";");
        WriteIndent();
        _output.AppendLine("var __wfApprovalPayloadJson = MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"toJSON\", new List<MaldaLang.Interpreter.RuntimeValue> { RuntimeHelpers.ToRuntimeValue(__wfApprovalPayloadValue) }, null).AsString();");
        WriteIndent();
        _output.AppendLine("if (!__workflowEngine.EnterApprovalWait(__workflowInstanceId, \"" + stmt.ApprovalId + "\", __wfApprovalName, " + (stmt.TimeoutMs?.ToString() ?? "null") + ", __wfApprovalPayloadJson, out var __wfApprovalEnterError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(__wfApprovalEnterError ?? \"Failed to enter approval wait.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("throw new MaldaLang.BuiltIns.BuiltInFunctions.TranspiledWorkflowPauseException(\"Workflow paused waiting for approval '" + stmt.ApprovalId + "'.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileWorkflowAwaitSignal(WorkflowAwaitSignalStatement stmt)
    {
        var signalVar = EscapeIdentifier(stmt.SignalId);
        WriteIndent();
        _output.Append("object ");
        _output.Append(signalVar);
        _output.AppendLine(" = null!;");
        WriteIndent();
        _output.AppendLine("var __wfSignalLatest = __workflowEngine.GetLatestStepAttempt(__workflowInstanceId, \"" + stmt.SignalId + "\");");
        WriteIndent();
        _output.AppendLine("if (__wfSignalLatest != null && __wfSignalLatest.State == StepState.Succeeded && !string.IsNullOrWhiteSpace(__wfSignalLatest.OutputJson))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("string __wfPayloadJson = \"null\";");
        WriteIndent();
        _output.AppendLine("using (var __wfDoc = JsonDocument.Parse(__wfSignalLatest.OutputJson!)) { if (__wfDoc.RootElement.TryGetProperty(\"payload\", out var __wfPayloadProp)) __wfPayloadJson = __wfPayloadProp.GetRawText(); }");
        WriteIndent();
        _output.AppendLine(signalVar + " = RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"parseJSON\", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(__wfPayloadJson) }, null));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.Append("var __wfSignalNameValue = ");
        TranspileExpression(stmt.SignalNameExpr);
        _output.AppendLine(";");
        WriteIndent();
        _output.AppendLine("var __wfSignalName = RuntimeHelpers.CoerceToString(__wfSignalNameValue);");
        WriteIndent();
        _output.Append("var __wfSignalCorrelation = ");
        TranspileExpression(stmt.PayloadExpr);
        _output.AppendLine(";");
        WriteIndent();
        _output.AppendLine("var __wfSignalCorrelationJson = MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"toJSON\", new List<MaldaLang.Interpreter.RuntimeValue> { RuntimeHelpers.ToRuntimeValue(__wfSignalCorrelation) }, null).AsString();");
        WriteIndent();
        _output.AppendLine("if (!__workflowEngine.EnterSignalWait(__workflowInstanceId, \"" + stmt.SignalId + "\", __wfSignalName, " + (stmt.TimeoutMs?.ToString() ?? "null") + ", __wfSignalCorrelationJson, out var __wfSignalEnterError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(__wfSignalEnterError ?? \"Failed to enter signal wait.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("throw new MaldaLang.BuiltIns.BuiltInFunctions.TranspiledWorkflowPauseException(\"Workflow paused waiting for signal '" + stmt.SignalId + "'.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileVarDecl(VarDeclStatement varDecl)
    {
        var declaredType = ResolveTranspiledTypeHint(varDecl.TypeHint);
        RegisterTypedVariable(varDecl.Name, declaredType);
        if (varDecl.IsConst)
            RegisterConstBinding(varDecl.Name);
        _output.Append(GetClrTypeName(declaredType));
        _output.Append(" ");
        _output.Append(EscapeIdentifier(varDecl.Name));
        _output.Append(" = ");
        _output.Append(GetCoercionExpressionPrefix(declaredType));
        TranspileExpression(varDecl.Initializer);
        _output.Append(GetCoercionExpressionSuffix(declaredType));
        _output.Append(";");
        AppendComment(nameof(TranspileVarDecl));
        _output.AppendLine();
    }

    private void TranspileAssignment(AssignmentStatement assignment)
    {
        // Handle array assignment specially
        if (assignment.Target is ArrayAccessExpression arrayAccess)
        {
            if (ResolveExpressionType(arrayAccess.Array) == TranspiledClrType.DoubleArray)
            {
                if (assignment.Operator == TokenType.Assign)
                {
                    _output.Append("{ var __typedArr = RuntimeHelpers.CoerceToDoubleList(");
                    TranspileExpression(arrayAccess.Array);
                    _output.Append("); var __typedIdx = (int)RuntimeHelpers.CoerceToInt(");
                    TranspileExpression(arrayAccess.Index);
                    _output.Append("); if (__typedIdx < 0 || __typedIdx >= __typedArr.Count) throw new InvalidOperationException(\"Array index out of bounds.\"); __typedArr[__typedIdx] = (double)RuntimeHelpers.CoerceToFloat(");
                    TranspileExpression(assignment.Value);
                    _output.Append("); }");
                }
                else
                {
                    var opString = GetOperatorString(assignment.Operator);
                    _output.Append("{ var __typedArr = RuntimeHelpers.CoerceToDoubleList(");
                    TranspileExpression(arrayAccess.Array);
                    _output.Append("); var __typedIdx = (int)RuntimeHelpers.CoerceToInt(");
                    TranspileExpression(arrayAccess.Index);
                    _output.Append("); if (__typedIdx < 0 || __typedIdx >= __typedArr.Count) throw new InvalidOperationException(\"Array index out of bounds.\"); __typedArr[__typedIdx] = __typedArr[__typedIdx] ");
                    _output.Append(opString);
                    _output.Append(" (double)RuntimeHelpers.CoerceToFloat(");
                    TranspileExpression(assignment.Value);
                    _output.Append("); }");
                }
                AppendComment(nameof(TranspileAssignment) + " (typed indexed)");
                _output.AppendLine();
                return;
            }
            if (assignment.Operator == TokenType.Assign)
            {
                _output.Append("RuntimeHelpers.SetIndexed(");
                TranspileExpression(arrayAccess.Array);
                _output.Append(", ");
                TranspileExpression(arrayAccess.Index);
                _output.Append(", ");
                TranspileExpression(assignment.Value);
                _output.Append(");");
            }
            else
            {
                // Compound assignment for indexed targets: expand to get, operate, set
                var opString = GetOperatorString(assignment.Operator);
                _output.Append("{ var __indexedTarget = ");
                TranspileExpression(arrayAccess.Array);
                _output.Append("; var __indexedKey = ");
                TranspileExpression(arrayAccess.Index);
                _output.Append("; var __current = RuntimeHelpers.GetIndexed(__indexedTarget, __indexedKey); __current = (");
                _output.Append("__current ");
                _output.Append(opString);
                _output.Append(" ");
                TranspileExpression(assignment.Value);
                _output.Append("); RuntimeHelpers.SetIndexed(__indexedTarget, __indexedKey, __current); }");
            }
            AppendComment(nameof(TranspileAssignment) + " (indexed)");
            _output.AppendLine();
        }
        else if (assignment.Target is MemberAccessExpression memberAccess)
        {
            if (assignment.Operator == TokenType.Assign)
            {
                // Handle ObjectInstance property assignment (e.g., JsonObject properties)
                _output.Append("RuntimeHelpers.SetObjectMember(");
                TranspileExpression(memberAccess.Object);
                _output.Append(", \"");
                _output.Append(memberAccess.Member);
                _output.Append("\", ");
                TranspileExpression(assignment.Value);
                _output.Append(");");
            }
            else
            {
                // Compound assignment for members: expand to get, operate, set
                var opString = GetOperatorString(assignment.Operator);
                _output.Append("RuntimeHelpers.SetObjectMember(");
                TranspileExpression(memberAccess.Object);
                _output.Append(", \"");
                _output.Append(memberAccess.Member);
                _output.Append("\", RuntimeHelpers.GetObjectMember(");
                TranspileExpression(memberAccess.Object);
                _output.Append(", \"");
                _output.Append(memberAccess.Member);
                _output.Append("\") ");
                _output.Append(opString);
                _output.Append(" ");
                TranspileExpression(assignment.Value);
                _output.Append(");");
            }
            AppendComment(nameof(TranspileAssignment) + " (member)");
            _output.AppendLine();
        }
        else
        {
            // Simple variable assignment
            if (assignment.Target is IdentifierExpression identifierTarget)
            {
                EmitConstAssignGuard(identifierTarget.Name);
                var targetType = ResolveVariableTypeOrDefault(identifierTarget.Name);
                _output.Append(EscapeIdentifier(identifierTarget.Name));
                _output.Append(" ");
                if (assignment.Operator == TokenType.Assign)
                {
                    _output.Append("= ");
                    _output.Append(GetCoercionExpressionPrefix(targetType));
                    TranspileExpression(assignment.Value);
                    _output.Append(GetCoercionExpressionSuffix(targetType));
                }
                else
                {
                    var opString = GetOperatorString(assignment.Operator);
                    _output.Append(opString);
                    _output.Append(" ");
                    if (targetType == TranspiledClrType.Double)
                    {
                        _output.Append("(double)RuntimeHelpers.CoerceToFloat(");
                        TranspileExpression(assignment.Value);
                        _output.Append(")");
                    }
                    else
                    {
                        TranspileExpression(assignment.Value);
                    }
                }
            }
            else
            {
                TranspileExpression(assignment.Target);
                _output.Append(" ");
                if (assignment.Operator == TokenType.Assign)
                {
                    _output.Append("=");
                }
                else
                {
                    var opString = GetOperatorString(assignment.Operator);
                    _output.Append(opString);
                }
                _output.Append(" ");
                TranspileExpression(assignment.Value);
            }
            _output.Append(";");
            AppendComment(nameof(TranspileAssignment));
            _output.AppendLine();
        }
    }

    private void TranspileIf(IfStatement ifStmt)
    {
        _output.Append("if (RuntimeHelpers.CoerceToBool(");
        TranspileExpression(ifStmt.Condition);
        _output.Append("))");
        AppendComment(nameof(TranspileIf));
        _output.AppendLine();
        TranspileStatement(ifStmt.ThenBranch);
        
        if (ifStmt.ElseBranch != null)
        {
            WriteIndent();
            _output.Append("else");
            AppendComment(nameof(TranspileIf) + " (else)");
            _output.AppendLine();
            TranspileStatement(ifStmt.ElseBranch);
        }
    }

    private void TranspileWhile(WhileStatement whileStmt)
    {
        _output.Append("while (RuntimeHelpers.CoerceToBool(");
        TranspileExpression(whileStmt.Condition);
        _output.Append("))");
        AppendComment(nameof(TranspileWhile));
        _output.AppendLine();

        // Parser desugars `for` into `while` with body Block { userBody, increment }.
        // `continue` in userBody must still run increment (see Interpreter.ExecuteWhileAsync).
        if (whileStmt.Body is BlockStatement desugaredForBody &&
            desugaredForBody.Statements.Count == 2)
        {
            var incrementLabel = $"__forIncrement{_desugaredForContinueLabels.Count}";
            _desugaredForContinueLabels.Push(incrementLabel);
            try
            {
                WriteIndent();
                _output.AppendLine("{");
                _indentLevel++;
                TranspileStatement(desugaredForBody.Statements[0]);
                WriteIndent();
                _output.AppendLine($"{incrementLabel}: ;");
                TranspileStatement(desugaredForBody.Statements[1]);
                _indentLevel--;
                WriteIndent();
                _output.AppendLine("}");
            }
            finally
            {
                _desugaredForContinueLabels.Pop();
            }

            return;
        }

        TranspileStatement(whileStmt.Body);
    }

    private void TranspileFor(ForStatement forStmt)
    {
        _output.Append("for (");
        
        if (forStmt.Initializer != null)
        {
            if (forStmt.Initializer is VarDeclStatement varDecl)
            {
                _output.Append("object ");
                _output.Append(EscapeIdentifier(varDecl.Name));
                _output.Append(" = ");
                TranspileExpression(varDecl.Initializer);
            }
            else if (forStmt.Initializer is ExpressionStatement exprStmt)
            {
                TranspileExpression(exprStmt.Expression);
            }
            else
            {
                // For other statement types, we need to handle them differently
                // This is a limitation - for loop initializer should be var decl or expression
                TranspileStatement(forStmt.Initializer);
                // Remove trailing semicolon and newline
                var currentLength = _output.Length;
                while (currentLength > 0 && (_output[currentLength - 1] == ';' || _output[currentLength - 1] == '\n' || _output[currentLength - 1] == '\r'))
                {
                    currentLength--;
                }
                _output.Length = currentLength;
            }
        }
        
        _output.Append("; ");
        
        if (forStmt.Condition != null)
        {
            _output.Append("RuntimeHelpers.CoerceToBool(");
            TranspileExpression(forStmt.Condition);
            _output.Append(")");
        }
        
        _output.Append("; ");
        
        if (forStmt.Increment != null)
        {
            TranspileExpression(forStmt.Increment);
        }
        
        _output.Append(")");
        AppendComment(nameof(TranspileFor));
        _output.AppendLine();
        TranspileStatement(forStmt.Body);
    }

    private void TranspileForIn(ForInStatement forInStmt)
    {
        _output.Append("foreach (object ");
        _output.Append(EscapeIdentifier(forInStmt.VariableName));
        _output.Append(" in RuntimeHelpers.GetArray(");
        TranspileExpression(forInStmt.Collection);
        _output.Append("))");
        AppendComment(nameof(TranspileForIn));
        _output.AppendLine();
        TranspileStatement(forInStmt.Body);
    }

    private void TranspileFunction(FunctionDeclaration funcDecl)
    {
        var returnType = ResolveTranspiledTypeHint(funcDecl.ReturnType);

        // Transpile decorators
        if (funcDecl.Decorators != null && funcDecl.Decorators.Count > 0)
        {
            foreach (var decorator in funcDecl.Decorators)
            {
                WriteIndent();
                TranspileDecorator(decorator);
                _output.AppendLine();
            }
        }

        WriteIndent();
        _output.Append("static async Task<");
        _output.Append(GetClrTypeName(returnType));
        _output.Append("> ");
        _output.Append(EscapeIdentifier(funcDecl.Name));
        _output.Append("(");

        PushTypedScope();
        for (int i = 0; i < funcDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");

            // Transpile parameter decorators
            // Note: ParameterDecorators is a flat list where decorators for parameter i are at index i
            if (funcDecl.ParameterDecorators != null && i < funcDecl.ParameterDecorators.Count)
            {
                var decorator = funcDecl.ParameterDecorators[i];
                if (decorator != null)
                {
                    TranspileDecorator(decorator);
                    _output.Append(" ");
                }
            }

            var parameterType = (funcDecl.ParameterTypeHints != null && i < funcDecl.ParameterTypeHints.Count)
                ? ResolveTranspiledTypeHint(funcDecl.ParameterTypeHints[i])
                : TranspiledClrType.Object;
            RegisterTypedVariable(funcDecl.Parameters[i], parameterType);
            _output.Append(GetClrTypeName(parameterType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier(funcDecl.Parameters[i]));
        }
        
        _output.Append(")");
        AppendComment(nameof(TranspileFunction));
        _output.AppendLine();
        var previousCanAwait = _canAwait;
        _canAwait = true;
        _currentFunctionReturnType.Push(returnType);
        TranspileFunctionBlock(funcDecl.Body, funcDecl.Name, funcDecl.Line, appendImplicitNullReturn: true);
        _currentFunctionReturnType.Pop();
        PopTypedScope();
        _canAwait = previousCanAwait;
    }

    private void GenerateTranspiledPropertyRegistry(List<PropertyDeclaration> properties)
    {
        WriteIndent();
        _output.AppendLine("public sealed class TranspiledPropertyMetadata");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("public string Name { get; init; } = string.Empty;");
        WriteIndent();
        _output.AppendLine("public string MethodName { get; init; } = string.Empty;");
        WriteIndent();
        _output.AppendLine("public string[] Parameters { get; init; } = Array.Empty<string>();");
        WriteIndent();
        _output.AppendLine("public string[] RequiredCapabilities { get; init; } = Array.Empty<string>();");
        WriteIndent();
        _output.AppendLine("public string[] TargetModes { get; init; } = Array.Empty<string>();");
        WriteIndent();
        _output.AppendLine("public int SourceLine { get; init; }");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static readonly System.Collections.Generic.IReadOnlyList<TranspiledPropertyMetadata> __transpiledProperties = new List<TranspiledPropertyMetadata>");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        foreach (var propertyDecl in properties)
        {
            var requiredCapabilities = propertyDecl.GetRequiredCapabilities();
            var targetModes = propertyDecl.GetTargetModes();
            if (targetModes.Count == 0)
            {
                // Keep existing default behavior: parity properties target interpreter and transpiled C#.
                targetModes = new[] { "interpreter", "csharp" };
            }

            WriteIndent();
            _output.Append("new TranspiledPropertyMetadata { Name = ");
            _output.Append(ToQuotedString(propertyDecl.Name));
            _output.Append(", MethodName = ");
            _output.Append(ToQuotedString(GetPropertyRunnerMethodName(propertyDecl.Name)));
            _output.Append(", Parameters = new[] { ");
            for (var i = 0; i < propertyDecl.Parameters.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                _output.Append(ToQuotedString(propertyDecl.Parameters[i]));
            }
            _output.Append(" }, RequiredCapabilities = ");
            if (requiredCapabilities.Count == 0)
            {
                _output.Append("Array.Empty<string>()");
            }
            else
            {
                _output.Append("new[] { ");
                for (var i = 0; i < requiredCapabilities.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append(ToQuotedString(requiredCapabilities[i]));
                }
                _output.Append(" }");
            }

            _output.Append(", TargetModes = ");
            if (targetModes.Count == 0)
            {
                _output.Append("Array.Empty<string>()");
            }
            else
            {
                _output.Append("new[] { ");
                for (var i = 0; i < targetModes.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append(ToQuotedString(targetModes[i]));
                }
                _output.Append(" }");
            }
            _output.Append(", SourceLine = ");
            _output.Append(propertyDecl.Line.ToString());
            _output.AppendLine(" },");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static System.Collections.Generic.IReadOnlyList<TranspiledPropertyMetadata> GetTranspiledProperties()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return __transpiledProperties;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static async Task<object> InvokeTranspiledProperty(string propertyName, object[] args)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (propertyName == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new ArgumentNullException(nameof(propertyName));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("var __propertyMeta = __transpiledProperties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal));");
        WriteIndent();
        _output.AppendLine("if (__propertyMeta == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException($\"Unknown property '{propertyName}'.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("args ??= Array.Empty<object>();");
        WriteIndent();
        _output.AppendLine("if (args.Length != __propertyMeta.Parameters.Length)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new ArgumentException($\"Property '{propertyName}' expects {__propertyMeta.Parameters.Length} argument(s), got {args.Length}.\", nameof(args));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("switch (propertyName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        foreach (var propertyDecl in properties)
        {
            var methodName = EscapeIdentifier(GetPropertyRunnerMethodName(propertyDecl.Name));
            WriteIndent();
            _output.Append("case ");
            _output.Append(ToQuotedString(propertyDecl.Name));
            _output.AppendLine(":");
            _indentLevel++;
            WriteIndent();
            _output.Append("return await ");
            _output.Append(methodName);
            _output.Append("(");
            for (var i = 0; i < propertyDecl.Parameters.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                _output.Append("args[");
                _output.Append(i.ToString());
                _output.Append("]");
            }
            _output.AppendLine(");");
            _indentLevel--;
        }
        WriteIndent();
        _output.AppendLine("default:");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException($\"Unknown property '{propertyName}'.\");");
        _indentLevel--;
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileProperty(PropertyDeclaration propertyDecl)
    {
        WriteIndent();
        _output.Append("public static async Task<object> ");
        _output.Append(EscapeIdentifier(GetPropertyRunnerMethodName(propertyDecl.Name)));
        _output.Append("(");
        for (var i = 0; i < propertyDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("object ");
            _output.Append(EscapeIdentifier(propertyDecl.Parameters[i]));
        }
        _output.AppendLine(")");
        var previousCanAwait = _canAwait;
        _canAwait = true;
        TranspileFunctionBlock(propertyDecl.Body, GetPropertyRunnerMethodName(propertyDecl.Name), propertyDecl.Line, appendImplicitNullReturn: true);
        _canAwait = previousCanAwait;
    }

    private static string ToQuotedString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private void GenerateSchemaRegistration(List<SchemaDeclaration> schemas)
    {
        if (schemas.Count == 0)
            return;

        foreach (var schemaDecl in schemas)
        {
            WriteIndent();
            _output.Append("MaldaLang.BuiltIns.SchemaRegistry.RegisterCompiled(\"");
            _output.Append(schemaDecl.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.Append("\", ");
            EmitParseJsonSchemaLiteral(SchemaRegistry.BuildSchema(schemaDecl));
            _output.AppendLine(");");
        }

        _output.AppendLine();
    }

    private void EmitParseJsonSchemaLiteral(MaldaLang.Interpreter.RuntimeValue schema)
    {
        var schemaJson = BuiltInFunctions.SerializeToJson(schema);
        var escaped = schemaJson.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _output.Append("(MaldaLang.Interpreter.RuntimeValue)MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"parseJSON\", new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(\"");
        _output.Append(escaped);
        _output.Append("\") }, null)");
    }

    private static int? TryGetPromptWithinTimeoutMs(PromptDeclaration promptDecl)
    {
        var decorator = promptDecl.Decorators.FirstOrDefault(d =>
            string.Equals(d.Name, "within", StringComparison.OrdinalIgnoreCase));
        if (decorator == null || decorator.Arguments.Count != 1)
            return null;

        if (decorator.Arguments[0] is not LiteralExpression literal)
            return null;

        var ms = literal.Value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => 0
        };

        return ms > 0 ? ms : null;
    }

    private static bool IsBuiltInPromptReturnType(string? returnType)
    {
        if (string.IsNullOrWhiteSpace(returnType)) return true;
        var n = returnType.Trim();
        return n is "string" or "String" or "int" or "Int" or "integer" or "Integer"
            or "float" or "Float" or "double" or "Double" or "number" or "Number"
            or "bool" or "Bool" or "boolean" or "Boolean"
            or "array" or "Array" or "list" or "List"
            or "object" or "Object" or "json" or "Json"
            or "Plan";
    }

    private void TranspilePrompt(PromptDeclaration promptDecl, List<ClassDeclaration> classes, List<SchemaDeclaration> schemas)
    {
        WriteIndent();
        _output.Append("static object ");
        _output.Append(EscapeIdentifier(promptDecl.Name));
        _output.Append("(");
        
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("object ");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
        }
        
        _output.Append(")");
        AppendComment(nameof(TranspilePrompt));
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        
        if (promptDecl.BodyType == PromptBodyType.ObjectLiteral)
        {
            // Evaluate the prompt body (object literal)
            WriteIndent();
            _output.Append("var bodyValue = ");
            TranspileExpression(promptDecl.ObjectBody!);
            _output.AppendLine(";");
        
        // Extract fields from body
        WriteIndent();
        _output.AppendLine("if (bodyValue == null || !RuntimeHelpers.IsObject(bodyValue))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Prompt body must evaluate to an object.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("var bodyObj = RuntimeHelpers.UnwrapRuntimeValue(bodyValue);");
        WriteIndent();
        _output.AppendLine("if (bodyObj is not MaldaLang.BuiltIns.JsonObject and not MaldaLang.Interpreter.DictionaryInstance and not System.Collections.Generic.Dictionary<string, object?>)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Prompt body must be a JSON object.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        // Extract fields
        WriteIndent();
        _output.AppendLine("var systemValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"system\");");
        WriteIndent();
        _output.AppendLine("var userValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"user\");");
        WriteIndent();
        _output.AppendLine("var modelValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"model\");");
        WriteIndent();
        _output.AppendLine("var temperatureValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"temperature\");");
        WriteIndent();
        _output.AppendLine("var toolsValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"tools\");");
        WriteIndent();
        _output.AppendLine("var maxTokensValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"maxTokens\");");
        WriteIndent();
        _output.AppendLine("var examplesValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"examples\");");
        WriteIndent();
        _output.AppendLine("var examples = MaldaLang.BuiltIns.PromptExampleHelpers.ParseExamplesOrNull(examplesValue);");
        
        // Validate user is required
        WriteIndent();
        _output.AppendLine("if (userValue.Type == MaldaLang.Interpreter.ValueType.Null || userValue.Type != MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Prompt body must have a 'user' field of type string.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        // Extract values
        WriteIndent();
        _output.AppendLine("string? system = null;");
        WriteIndent();
        _output.AppendLine("if (systemValue.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("system = RuntimeHelpers.CoerceToString(systemValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("string user = RuntimeHelpers.CoerceToString(userValue);");
        
        WriteIndent();
        _output.AppendLine("string? model = null;");
        WriteIndent();
        _output.AppendLine("if (modelValue.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("model = RuntimeHelpers.CoerceToString(modelValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("double? temperature = null;");
        WriteIndent();
        _output.AppendLine("if (temperatureValue.Type != MaldaLang.Interpreter.ValueType.Null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (temperatureValue.Type == MaldaLang.Interpreter.ValueType.Float)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("temperature = (double)RuntimeHelpers.CoerceToFloat(temperatureValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (temperatureValue.Type == MaldaLang.Interpreter.ValueType.Integer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("temperature = (double)RuntimeHelpers.CoerceToInt(temperatureValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("System.Collections.Generic.List<string>? tools = null;");
        WriteIndent();
        _output.AppendLine("if (toolsValue.Type == MaldaLang.Interpreter.ValueType.Array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("tools = new System.Collections.Generic.List<string>();");
        WriteIndent();
        _output.AppendLine("foreach (var tool in RuntimeHelpers.UnwrapRuntimeValue(toolsValue) as System.Collections.Generic.List<RuntimeValue>)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (tool.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("tools.Add(RuntimeHelpers.CoerceToString(tool));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("int? maxTokens = null;");
        WriteIndent();
        _output.AppendLine("if (maxTokensValue.Type != MaldaLang.Interpreter.ValueType.Null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (maxTokensValue.Type == MaldaLang.Interpreter.ValueType.Integer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("maxTokens = (int)RuntimeHelpers.CoerceToInt(maxTokensValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (maxTokensValue.Type == MaldaLang.Interpreter.ValueType.Float)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("maxTokens = (int)RuntimeHelpers.CoerceToFloat(maxTokensValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        }
        else
        {
            // Statement-based body: evaluate statements and extract values
            WriteIndent();
            _output.AppendLine("string? system = null;");
            WriteIndent();
            _output.AppendLine("string? user = null;");
            WriteIndent();
            _output.AppendLine("string? model = null;");
            WriteIndent();
            _output.AppendLine("double? temperature = null;");
            WriteIndent();
            _output.AppendLine("System.Collections.Generic.List<string>? tools = null;");
            WriteIndent();
            _output.AppendLine("int? maxTokens = null;");
            WriteIndent();
            _output.AppendLine("System.Collections.Generic.List<MaldaLang.BuiltIns.PromptExample>? examples = null;");
            _output.AppendLine();
            
            if (promptDecl.StatementBody != null)
            {
                foreach (var stmt in promptDecl.StatementBody)
                {
                    if (stmt is PromptBodyStatement bodyStmt)
                    {
                        WriteIndent();
                        var keyword = bodyStmt.Keyword;
                        _output.Append("var ");
                        _output.Append(keyword);
                        _output.Append("Value = ");
                        TranspileExpression(bodyStmt.Expression);
                        _output.AppendLine(";");
                        
                        WriteIndent();
                        _output.Append("if (");
                        _output.Append(keyword);
                        _output.AppendLine("Value != null)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        
                        switch (keyword)
                        {
                            case "system":
                                _output.AppendLine("system = RuntimeHelpers.CoerceToString(" + keyword + "Value);");
                                break;
                            case "user":
                                _output.AppendLine("user = RuntimeHelpers.CoerceToString(" + keyword + "Value);");
                                break;
                            case "model":
                                _output.AppendLine("model = RuntimeHelpers.CoerceToString(" + keyword + "Value);");
                                break;
                            case "temperature":
                                _output.AppendLine("if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Float) temperature = (double)RuntimeHelpers.CoerceToFloat(" + keyword + "Value);");
                                WriteIndent();
                                _output.AppendLine("else if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Integer) temperature = (double)RuntimeHelpers.CoerceToInt(" + keyword + "Value);");
                                break;
                            case "tools":
                                _output.AppendLine("if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Array)");
                                WriteIndent();
                                _output.AppendLine("{");
                                _indentLevel++;
                                WriteIndent();
                                _output.AppendLine("tools = new System.Collections.Generic.List<string>();");
                                WriteIndent();
                                _output.AppendLine("foreach (var tool in RuntimeHelpers.UnwrapRuntimeValue(" + keyword + "Value) as System.Collections.Generic.List<RuntimeValue>)");
                                WriteIndent();
                                _output.AppendLine("{");
                                _indentLevel++;
                                WriteIndent();
                                _output.AppendLine("if (tool.Type == MaldaLang.Interpreter.ValueType.String) tools.Add(RuntimeHelpers.CoerceToString(tool));");
                                _indentLevel--;
                                WriteIndent();
                                _output.AppendLine("}");
                                _indentLevel--;
                                WriteIndent();
                                _output.AppendLine("}");
                                break;
                            case "maxTokens":
                                _output.AppendLine("if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Integer) maxTokens = (int)RuntimeHelpers.CoerceToInt(" + keyword + "Value);");
                                WriteIndent();
                                _output.AppendLine("else if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Float) maxTokens = (int)RuntimeHelpers.CoerceToFloat(" + keyword + "Value);");
                                break;
                            case "examples":
                                _output.AppendLine("examples = MaldaLang.BuiltIns.PromptExampleHelpers.ParseExamplesOrNull(" + keyword + "Value)?.ToList();");
                                break;
                        }
                        
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                    }
                }
            }
            
        WriteIndent();
        _output.AppendLine("if (user == null) throw new Exception(\"Prompt body must have a 'user' field.\");");
        }

        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            var paramName = promptDecl.Parameters[i];
            WriteIndent();
            _output.Append("var __placeholder_");
            _output.Append(i);
            _output.Append(" = \"{");
            _output.Append(paramName);
            _output.Append("}\";");
            _output.AppendLine();
            WriteIndent();
            _output.Append("var __replacement_");
            _output.Append(i);
            _output.Append(" = RuntimeHelpers.CoerceToString(");
            _output.Append(EscapeIdentifier(paramName));
            _output.Append(");");
            _output.AppendLine();
            WriteIndent();
            _output.AppendLine("if (system != null && system.Contains(__placeholder_" + i + "))");
            WriteIndent();
            _output.AppendLine("system = system.Replace(__placeholder_" + i + ", __replacement_" + i + ");");
            WriteIndent();
            _output.AppendLine("if (user != null && user.Contains(__placeholder_" + i + "))");
            WriteIndent();
            _output.AppendLine("user = user.Replace(__placeholder_" + i + ", __replacement_" + i + ");");
        }

        WriteIndent();
        _output.AppendLine("if (examples != null && examples.Count > 0)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.Append("MaldaLang.BuiltIns.PromptExampleHelpers.ApplyParameterInterpolation(examples, new System.Collections.Generic.List<string> { ");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("\"");
            _output.Append(promptDecl.Parameters[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.Append("\"");
        }
        _output.Append(" }, new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
            _output.Append(")");
        }
        _output.AppendLine(" });");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        // Build response_format schema when ReturnType is present and tools is empty (§4.10)
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? __responseFormatSchema = null;");
        WriteIndent();
        _output.Append("if (!string.IsNullOrWhiteSpace(\"");
        _output.Append((promptDecl.ReturnType ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.Append("\") && (tools == null || tools.Count == 0))");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        bool isCustomReturnType = !string.IsNullOrWhiteSpace(promptDecl.ReturnType) && !IsBuiltInPromptReturnType(promptDecl.ReturnType);
        SchemaDeclaration? schemaDecl = null;
        ClassDeclaration? schemaClassDecl = null;
        if (isCustomReturnType)
        {
            var returnName = promptDecl.ReturnType!.Trim();
            schemaDecl = schemas.FirstOrDefault(s => string.Equals(s.Name, returnName, StringComparison.Ordinal));
            if (schemaDecl == null)
            {
                var classesByName = classes.ToDictionary(c => c.Name, c => c);
                if (classesByName.TryGetValue(returnName, out var cd))
                    schemaClassDecl = cd;
            }
        }
        if (schemaDecl != null)
        {
            WriteIndent();
            _output.Append("var __schema = ");
            EmitParseJsonSchemaLiteral(SchemaRegistry.BuildSchema(schemaDecl));
            _output.AppendLine(";");
            WriteIndent();
            _output.AppendLine("__responseFormatSchema = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__schema);");
        }
        else if (schemaClassDecl != null)
        {
            var schema = TypedPromptSchemaResolver.BuildSchemaFromClassDeclaration(schemaClassDecl, classes.ToDictionary(c => c.Name, c => c));
            WriteIndent();
            _output.Append("var __schema = ");
            EmitParseJsonSchemaLiteral(schema);
            _output.AppendLine(";");
            WriteIndent();
            _output.AppendLine("__responseFormatSchema = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__schema);");
        }
        else if (!string.IsNullOrWhiteSpace(promptDecl.ReturnType))
        {
            WriteIndent();
            _output.Append("if (MaldaLang.BuiltIns.TypedPromptSchemaResolver.TryResolve(\"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\", null, out var __schema, out _))");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("__responseFormatSchema = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__schema);");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        var promptWithinMs = TryGetPromptWithinTimeoutMs(promptDecl);
        if (promptWithinMs is > 0)
        {
            WriteIndent();
            _output.Append("int? __withinTimeoutMs = ");
            _output.Append(promptWithinMs.Value);
            _output.AppendLine(";");
        }
        else
        {
            WriteIndent();
            _output.AppendLine("int? __withinTimeoutMs = null;");
        }

        // Create and return PromptInstance
        WriteIndent();
        _output.Append("return RuntimeHelpers.ToRuntimeValue(new MaldaLang.BuiltIns.PromptInstance(system, user, model, temperature, tools, maxTokens, __responseFormatSchema, examples, __withinTimeoutMs));");
        _output.AppendLine();
        
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        // Emit async execution helper so transpiled `await prompt(...)` matches interpreter behavior.
        WriteIndent();
        _output.Append("static async Task<object> ");
        _output.Append(EscapeIdentifier(promptDecl.Name));
        _output.Append("__ExecuteAsync(");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("object ");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
        }
        _output.AppendLine(")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;

        WriteIndent();
        _output.Append("var __promptRuntimeValue = RuntimeHelpers.ToRuntimeValue(");
        _output.Append(EscapeIdentifier(promptDecl.Name));
        _output.Append("(");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
        }
        _output.AppendLine("));");

        WriteIndent();
        _output.AppendLine("if (__promptRuntimeValue.Type != MaldaLang.Interpreter.ValueType.Object || RuntimeHelpers.UnwrapRuntimeValue(__promptRuntimeValue) is not MaldaLang.BuiltIns.PromptInstance __promptInstance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Failed to create PromptInstance.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("var __defaultClient = MaldaLang.BuiltIns.DefaultLocalLlm.GetDefaultLocalClient();");
        WriteIndent();
        _output.AppendLine("var __agent = new MaldaLang.BuiltIns.AgentInstance();");
        WriteIndent();
        _output.AppendLine("__agent.Initialize(\"PromptAgent\", \"AI Assistant\", \"You are a helpful AI assistant.\", null, __defaultClient, null, null);");

        WriteIndent();
        if (string.IsNullOrWhiteSpace(promptDecl.ReturnType))
        {
            _output.AppendLine("string? __typedReturnType = null;");
            _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = null;");
        }
        else
        {
            _output.Append("string? __typedReturnType = \"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\";");
            bool customReturnType = !IsBuiltInPromptReturnType(promptDecl.ReturnType);
            SchemaDeclaration? returnSchemaDecl = null;
            ClassDeclaration? returnClassDecl = null;
            if (customReturnType)
            {
                var returnName = promptDecl.ReturnType!.Trim();
                returnSchemaDecl = schemas.FirstOrDefault(s => string.Equals(s.Name, returnName, StringComparison.Ordinal));
                if (returnSchemaDecl == null)
                {
                    var classesByName = classes.ToDictionary(c => c.Name, c => c);
                    if (classesByName.TryGetValue(returnName, out var classDecl))
                        returnClassDecl = classDecl;
                }
            }
            if (returnSchemaDecl != null)
            {
                WriteIndent();
                _output.Append("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = ");
                EmitParseJsonSchemaLiteral(SchemaRegistry.BuildSchema(returnSchemaDecl));
                _output.AppendLine(";");
            }
            else if (returnClassDecl != null)
            {
                var schema = TypedPromptSchemaResolver.BuildSchemaFromClassDeclaration(returnClassDecl, classes.ToDictionary(c => c.Name, c => c));
                WriteIndent();
                _output.Append("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = ");
                EmitParseJsonSchemaLiteral(schema);
                _output.AppendLine(";");
            }
            else
            {
                WriteIndent();
                _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = null;");
            }
        }

        WriteIndent();
        _output.AppendLine("int __maxAttempts = __typedReturnType != null ? 3 : 1;");
        WriteIndent();
        _output.AppendLine("string __baseUser = __promptInstance.User;");
        WriteIndent();
        _output.AppendLine("string __lastError = \"Unknown validation error.\";");
        WriteIndent();
        _output.AppendLine("for (int __attempt = 1; __attempt <= __maxAttempts; __attempt++)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;

        WriteIndent();
        _output.AppendLine("if (__attempt > 1)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __repair = MaldaLang.BuiltIns.TypedPromptValidator.BuildRepairInstruction(__typedReturnType!, __lastError);");
        WriteIndent();
        _output.AppendLine("__promptInstance = new MaldaLang.BuiltIns.PromptInstance(__promptInstance.System, __baseUser + \"\\n\\n\" + __repair, __promptInstance.Model, __promptInstance.Temperature, __promptInstance.Tools, __promptInstance.MaxTokens, __promptInstance.ResponseFormatSchema, __promptInstance.Examples, __promptInstance.WithinTimeoutMs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("var __response = __agent.Think(RuntimeHelpers.ToRuntimeValue(__promptInstance));");
        WriteIndent();
        _output.AppendLine("string? __content = null;");
        WriteIndent();
        _output.AppendLine("if (__response.Type == MaldaLang.Interpreter.ValueType.Object && RuntimeHelpers.UnwrapRuntimeValue(__response) is MaldaLang.BuiltIns.JsonObject __responseObj)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __contentValue = __responseObj.Get(\"content\");");
        WriteIndent();
        _output.AppendLine("if (__contentValue.Type == MaldaLang.Interpreter.ValueType.String) __content = RuntimeHelpers.CoerceToString(__contentValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (__response.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__content = RuntimeHelpers.CoerceToString(__response);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (__typedReturnType == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (__content != null) return __content;");
        WriteIndent();
        _output.AppendLine("return __response.ToString() ?? \"\";");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (__content == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = \"No string content in LLM response.\";");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (!MaldaLang.BuiltIns.TypedPromptValidator.TryExtractJsonCandidate(__content, out var __jsonCandidate, out var __extractError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = __extractError;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (!MaldaLang.BuiltIns.TypedPromptValidator.TryParseJson(__jsonCandidate, out var __parsed, out var __parseError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = __parseError;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("string __validationError;");
        WriteIndent();
        _output.AppendLine("if (__resolvedSchema != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (!MaldaLang.BuiltIns.TypedPromptValidator.TryValidateReturnType(__parsed, __resolvedSchema!, out __validationError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = __validationError;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (!MaldaLang.BuiltIns.TypedPromptValidator.TryValidateReturnType(__parsed, __typedReturnType!, null, out __validationError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = __validationError;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("return RuntimeHelpers.UnwrapRuntimeValue(__parsed);");

        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("throw new Exception($\"Typed prompt output validation failed after {__maxAttempts} attempts. Return type: {__typedReturnType}. Last error: {__lastError}\");");

        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileReturn(ReturnStatement returnStmt)
    {
        var returnType = _currentFunctionReturnType.Count > 0 ? _currentFunctionReturnType.Peek() : TranspiledClrType.Object;
        _output.Append("return ");
        _output.Append(GetCoercionExpressionPrefix(returnType));
        if (returnStmt.Value != null)
        {
            TranspileExpression(returnStmt.Value);
        }
        else
        {
            _output.Append("null");
        }
        _output.Append(GetCoercionExpressionSuffix(returnType));
        _output.Append(";");
        AppendComment(nameof(TranspileReturn));
        _output.AppendLine();
    }

    private void TranspilePrint(PrintStatement printStmt)
    {
        _output.Append("Console.WriteLine(RuntimeHelpers.CoerceToString(");
        TranspileExpression(printStmt.Expression);
        _output.Append("));");
        AppendComment(nameof(TranspilePrint));
        _output.AppendLine();
    }

    private void TranspileFunctionBlock(BlockStatement block, string functionName, int line, bool appendImplicitNullReturn = false)
    {
        PushTypedScope();
        PushConstScope();
        var blockReturnType = _currentFunctionReturnType.Count > 0 ? _currentFunctionReturnType.Peek() : TranspiledClrType.Object;
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileFunctionBlock) + " (open)");
        _output.AppendLine();
        _indentLevel++;
        string? functionProfile = null;

        if (ProfilingEnabled)
        {
            functionProfile = EmitFunctionProfileStart(functionName, line, block.SourceFile);
            WriteIndent();
            _output.AppendLine("try");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
        }

        EmitDeferFramePrologue();

        var lastIsExpression = block.Statements.Count > 0 && block.Statements[^1] is ExpressionStatement;
        var useLastExprWins = appendImplicitNullReturn && lastIsExpression;

        for (int i = 0; i < block.Statements.Count; i++)
        {
            var stmt = block.Statements[i];
            var isLast = i == block.Statements.Count - 1;
            if (useLastExprWins && isLast && stmt is ExpressionStatement exprStmt)
            {
                WriteIndent();
                _output.Append("return ");
                _output.Append(GetCoercionExpressionPrefix(blockReturnType));
                TranspileExpression(exprStmt.Expression);
                _output.Append(GetCoercionExpressionSuffix(blockReturnType));
                _output.AppendLine(";");
            }
            else
            {
                TranspileStatement(stmt);
            }
        }

        if (appendImplicitNullReturn && !useLastExprWins && !BlockDefinitelyReturns(block))
        {
            WriteIndent();
            if (blockReturnType == TranspiledClrType.Double)
                _output.Append("return 0d;");
            else
                _output.Append("return null;");
            AppendComment(nameof(TranspileFunctionBlock) + " (implicit return)");
            _output.AppendLine();
        }

        EmitDeferFrameEpilogue();

        if (ProfilingEnabled)
        {
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
            WriteIndent();
            _output.AppendLine("finally");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            EmitFunctionProfileExit(functionProfile!);
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
        }
        
        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileFunctionBlock) + " (close)");
        _output.AppendLine();
        PopConstScope();
        PopTypedScope();
    }

    private static bool BlockDefinitelyReturns(BlockStatement block)
    {
        foreach (var statement in block.Statements)
        {
            if (StatementDefinitelyReturns(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementDefinitelyReturns(Statement statement)
    {
        return statement switch
        {
            ReturnStatement => true,
            BlockStatement block => BlockDefinitelyReturns(block),
            IfStatement ifStatement => ifStatement.ElseBranch != null &&
                                       StatementDefinitelyReturns(ifStatement.ThenBranch) &&
                                       StatementDefinitelyReturns(ifStatement.ElseBranch),
            TryStatement tryStatement => TryStatementDefinitelyReturns(tryStatement),
            _ => false
        };
    }

    private static bool TryStatementDefinitelyReturns(TryStatement tryStatement)
    {
        if (tryStatement.FinallyBlock != null && BlockDefinitelyReturns(tryStatement.FinallyBlock))
        {
            return true;
        }

        if (!BlockDefinitelyReturns(tryStatement.TryBlock))
        {
            return false;
        }

        if (tryStatement.CatchClauses.Count == 0)
        {
            return false;
        }

        foreach (var catchClause in tryStatement.CatchClauses)
        {
            if (!BlockDefinitelyReturns(catchClause.Body))
            {
                return false;
            }
        }

        return true;
    }

    private void EmitDeferFramePrologue()
    {
        WriteIndent();
        _output.AppendLine("RuntimeHelpers.PushDeferFrame();");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
    }

    private void EmitDeferFrameEpilogue()
    {
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        if (_canAwait)
            _output.AppendLine("await RuntimeHelpers.RunAndPopDeferFrameAsync();");
        else
            _output.AppendLine("RuntimeHelpers.RunAndPopDeferFrameAsync().GetAwaiter().GetResult();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileBlock(BlockStatement block, bool appendImplicitNullReturn = false)
    {
        PushTypedScope();
        PushConstScope();
        var blockReturnType = _currentFunctionReturnType.Count > 0 ? _currentFunctionReturnType.Peek() : TranspiledClrType.Object;
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileBlock) + " (open)");
        _output.AppendLine();
        _indentLevel++;
        EmitDeferFramePrologue();
        
        var lastIsExpression = block.Statements.Count > 0 && block.Statements[^1] is ExpressionStatement;
        var useLastExprWins = appendImplicitNullReturn && lastIsExpression;
        
        for (int i = 0; i < block.Statements.Count; i++)
        {
            var stmt = block.Statements[i];
            var isLast = (i == block.Statements.Count - 1);
            if (useLastExprWins && isLast && stmt is ExpressionStatement exprStmt)
            {
                WriteIndent();
                _output.Append("return ");
                _output.Append(GetCoercionExpressionPrefix(blockReturnType));
                TranspileExpression(exprStmt.Expression);
                _output.Append(GetCoercionExpressionSuffix(blockReturnType));
                _output.AppendLine(";");
            }
            else
            {
                TranspileStatement(stmt);
            }
        }

        if (appendImplicitNullReturn && !useLastExprWins && (block.Statements.Count == 0 || block.Statements[^1] is not ReturnStatement))
        {
            WriteIndent();
            if (blockReturnType == TranspiledClrType.Double)
                _output.Append("return 0d;");
            else
                _output.Append("return null;");
            AppendComment(nameof(TranspileBlock) + " (implicit return)");
            _output.AppendLine();
        }
        
        EmitDeferFrameEpilogue();
        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileBlock) + " (close)");
        _output.AppendLine();
        PopConstScope();
        PopTypedScope();
    }

    private void TranspileDefer(DeferStatement defer)
    {
        WriteIndent();
        _output.Append("RuntimeHelpers.RegisterDefer(async () => {");
        _output.AppendLine();
        _indentLevel++;
        foreach (var stmt in defer.Body.Statements)
        {
            TranspileStatement(stmt);
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("});");
    }

    private void TranspileUsingResource(UsingResourceStatement stmt)
    {
        WriteIndent();
        _output.Append("var ");
        _output.Append(EscapeIdentifier(stmt.VariableName));
        _output.Append(" = ");
        TranspileExpression(stmt.Initializer);
        _output.AppendLine(";");
        TranspileBlock(stmt.Body);
        WriteIndent();
        if (_canAwait)
            _output.Append("await RuntimeHelpers.DisposeResourceAsync(");
        else
            _output.Append("RuntimeHelpers.DisposeResourceAsync(");
        _output.Append(EscapeIdentifier(stmt.VariableName));
        if (_canAwait)
            _output.AppendLine(");");
        else
            _output.AppendLine(").GetAwaiter().GetResult();");
    }

    private void TranspileTry(TryStatement tryStmt)
    {
        _output.Append("try");
        AppendComment(nameof(TranspileTry));
        _output.AppendLine();
        TranspileBlock(tryStmt.TryBlock);
        
        // Transpile catch clauses (use C# when for filters so later catch clauses remain reachable)
        foreach (var catchClause in tryStmt.CatchClauses)
        {
            WriteIndent();
            _output.Append("catch (Exception ");
            
            var tempExceptionVar = "__spl_exception";
            _output.Append(tempExceptionVar);

            if (catchClause.Filter != null)
            {
                var bindParam = "__maldaCatchE";
                _output.Append(") when (RuntimeHelpers.MaldaCatchWhen(");
                _output.Append(tempExceptionVar);
                _output.Append(", ");
                _output.Append(bindParam);
                _output.Append(" => RuntimeHelpers.CoerceToBool(");
                if (catchClause.ExceptionVariable != null)
                {
                    _catchFilterRenameFrom = catchClause.ExceptionVariable;
                    _catchFilterRenameTo = bindParam;
                }
                TranspileExpression(catchClause.Filter);
                _catchFilterRenameFrom = null;
                _catchFilterRenameTo = null;
                _output.Append("))");
            }

            _output.Append(")");
            
            AppendComment(nameof(TranspileTry) + " (catch)");
            _output.AppendLine();
            
            WriteIndent();
            _output.Append("{");
            _output.AppendLine();
            _indentLevel++;
            
            if (catchClause.ExceptionVariable != null)
            {
                WriteIndent();
                _output.Append("var ");
                _output.Append(EscapeIdentifier(catchClause.ExceptionVariable));
                _output.Append(" = RuntimeHelpers.UnwrapRuntimeValue(RuntimeHelpers.UnwrapMaldaExceptionValue(");
                _output.Append(tempExceptionVar);
                _output.Append("));");
                _output.AppendLine();
            }
            
            TranspileBlock(catchClause.Body);
            
            _indentLevel--;
            WriteIndent();
            _output.Append("}");
            _output.AppendLine();
        }
        
        // Transpile finally block if present
        if (tryStmt.FinallyBlock != null)
        {
            WriteIndent();
            _output.Append("finally");
            AppendComment(nameof(TranspileTry) + " (finally)");
            _output.AppendLine();
            TranspileBlock(tryStmt.FinallyBlock);
        }
    }

    private void TranspileThrow(ThrowStatement throwStmt)
    {
        _output.Append("throw new MaldaLang.Interpreter.MALDAException(RuntimeHelpers.ToRuntimeValue(");
        TranspileExpression(throwStmt.Exception);
        _output.Append("));");
        AppendComment(nameof(TranspileThrow));
        _output.AppendLine();
    }

    private void TranspileClass(ClassDeclaration classDecl)
    {
        WriteIndent();
        _output.Append("public class ");
        _output.Append(EscapeIdentifier(classDecl.Name));
        
        if (classDecl.Superclass != null)
        {
            _output.Append(" : ");
            _output.Append(EscapeIdentifier(classDecl.Superclass));
        }
        
        _output.AppendLine();
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileClass) + " (open)");
        _output.AppendLine();
        _indentLevel++;
        
        foreach (var member in classDecl.Members)
        {
            TranspileClassMember(member);
        }
        
        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileClass) + " (close)");
        _output.AppendLine();
    }

    private void TranspileClassMember(ClassMember member)
    {
        WriteIndent();
        
        // Access modifier
        if (member.Access == AccessModifier.Public)
            _output.Append("public ");
        else if (member.Access == AccessModifier.Private)
            _output.Append("private ");
        
        if (member.IsStatic)
            _output.Append("static ");
        
        // Member type
        switch (member.Type)
        {
            case MemberType.Field:
                var fieldType = ResolveTranspiledTypeHint(member.TypeHint);
                _output.Append(GetClrTypeName(fieldType));
                _output.Append(" ");
                _output.Append(member.Name);
                if (member.Value != null && member.Value is Expression expr)
                {
                    _output.Append(" = ");
                    _output.Append(GetCoercionExpressionPrefix(fieldType));
                    TranspileExpression(expr);
                    _output.Append(GetCoercionExpressionSuffix(fieldType));
                }
                _output.AppendLine(";");
                break;
                
            case MemberType.Method:
                if (member.Value is FunctionDeclaration func)
                {
                    // Transpile method decorators
                    if (func.Decorators != null && func.Decorators.Count > 0)
                    {
                        foreach (var decorator in func.Decorators)
                        {
                            WriteIndent();
                            TranspileDecorator(decorator);
                            _output.AppendLine();
                        }
                    }

                    WriteIndent();
                    var methodReturnType = ResolveTranspiledTypeHint(func.ReturnType);
                    _output.Append(GetClrTypeName(methodReturnType));
                    _output.Append(" ");
                    _output.Append(EscapeIdentifier(member.Name));
                    _output.Append("(");
                    PushTypedScope();
                    for (int i = 0; i < func.Parameters.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");

                        // Parameter decorators
                        if (func.ParameterDecorators != null && i < func.ParameterDecorators.Count)
                        {
                            var decorator = func.ParameterDecorators[i];
                            if (decorator != null)
                            {
                                TranspileDecorator(decorator);
                                _output.Append(" ");
                            }
                        }

                        var parameterType = (func.ParameterTypeHints != null && i < func.ParameterTypeHints.Count)
                            ? ResolveTranspiledTypeHint(func.ParameterTypeHints[i])
                            : TranspiledClrType.Object;
                        RegisterTypedVariable(func.Parameters[i], parameterType);
                        _output.Append(GetClrTypeName(parameterType));
                        _output.Append(" ");
                        _output.Append(EscapeIdentifier(func.Parameters[i]));
                    }
                    _output.AppendLine(")");
                    var previousCanAwait = _canAwait;
                    _canAwait = false;
                    _currentFunctionReturnType.Push(methodReturnType);
                    TranspileFunctionBlock(func.Body, member.Name, func.Line, appendImplicitNullReturn: true);
                    _currentFunctionReturnType.Pop();
                    PopTypedScope();
                    _canAwait = previousCanAwait;
                }
                break;
                
            case MemberType.Constructor:
                if (member.Value is FunctionDeclaration ctor)
                {
                    // Transpile constructor decorators
                    if (ctor.Decorators != null && ctor.Decorators.Count > 0)
                    {
                        foreach (var decorator in ctor.Decorators)
                        {
                            WriteIndent();
                            TranspileDecorator(decorator);
                            _output.AppendLine();
                        }
                    }

                    WriteIndent();
                    // Default visibility for actor constructors should be public to allow spawn
                    if (member.Access == AccessModifier.Default)
                    {
                        _output.Append("public ");
                    }
                    _output.Append(EscapeIdentifier(member.Name));
                    _output.Append("(");
                    PushTypedScope();
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");

                        // Parameter decorators
                        if (ctor.ParameterDecorators != null && i < ctor.ParameterDecorators.Count)
                        {
                            var decorator = ctor.ParameterDecorators[i];
                            if (decorator != null)
                            {
                                TranspileDecorator(decorator);
                                _output.Append(" ");
                            }
                        }

                        var parameterType = (ctor.ParameterTypeHints != null && i < ctor.ParameterTypeHints.Count)
                            ? ResolveTranspiledTypeHint(ctor.ParameterTypeHints[i])
                            : TranspiledClrType.Object;
                        RegisterTypedVariable(ctor.Parameters[i], parameterType);
                        _output.Append(GetClrTypeName(parameterType));
                        _output.Append(" ");
                        _output.Append(EscapeIdentifier(ctor.Parameters[i]));
                    }
                    _output.AppendLine(")");
                    _currentFunctionReturnType.Push(TranspiledClrType.Object);
                    TranspileFunctionBlock(ctor.Body, member.Name + ".ctor", ctor.Line);
                    _currentFunctionReturnType.Pop();
                    PopTypedScope();
                }
                break;
        }
    }

    private void TranspileDecorator(Decorator decorator)
    {
        if (TargetPartitioner.IsCompileTimeTargetDecorator(decorator.Name))
        {
            return;
        }

        _output.Append("[");
        _output.Append(decorator.Name);
        _output.Append("Attribute");
        if (decorator.Arguments != null && decorator.Arguments.Count > 0)
        {
            _output.Append("(");
            for (int i = 0; i < decorator.Arguments.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                TranspileExpression(decorator.Arguments[i]);
            }
            _output.Append(")");
        }
        _output.Append("]");
    }

    private void TranspileExpression(Expression expr)
    {
        switch (expr)
        {
            case LiteralExpression literal:
                TranspileLiteral(literal);
                break;
            case IdentifierExpression identifier:
                if (_catchFilterRenameFrom != null &&
                    _catchFilterRenameTo != null &&
                    identifier.Name == _catchFilterRenameFrom)
                {
                    _output.Append(_catchFilterRenameTo);
                }
                else
                {
                    _output.Append(EscapeIdentifier(identifier.Name));
                }
                break;
            case BinaryExpression binary:
                TranspileBinary(binary);
                break;
            case UnaryExpression unary:
                TranspileUnary(unary);
                break;
            case PostfixExpression postfix:
                TranspilePostfix(postfix);
                break;
            case FunctionCallExpression call:
                TranspileFunctionCall(call);
                break;
            case MemberAccessExpression member:
                TranspileMemberAccess(member);
                break;
            case NewExpression newExpr:
                TranspileNew(newExpr);
                break;
            case ThisExpression:
                _output.Append("this");
                break;
            case SuperExpression:
                _output.Append("base");
                break;
            case SpawnExpression spawn:
                TranspileSpawn(spawn);
                break;
            case ReceiveExpression:
                // receive() in transpiled code maps to ActorsRuntime.ReceiveAsync()
                // and is synchronously awaited to match blocking semantics.
                _output.Append("ActorsRuntime.ReceiveAsync().GetAwaiter().GetResult()");
                break;
            case SelfExpression:
                // self maps to current actor reference in ActorsRuntime
                _output.Append("(object)ActorsRuntime.GetSelf()");
                break;
            case ArrayLiteralExpression array:
                TranspileArrayLiteral(array);
                break;
            case ObjectLiteralExpression obj:
                TranspileObjectLiteral(obj);
                break;
            case DictionaryLiteralExpression dict:
                TranspileDictionaryLiteral(dict);
                break;
            case GraphLiteralExpression graph:
                TranspileGraphLiteral(graph);
                break;
            case ArrayAccessExpression arrayAccess:
                TranspileArrayAccess(arrayAccess);
                break;
            case TernaryExpression ternary:
                TranspileTernary(ternary);
                break;
            case InterpolatedStringExpression interpolated:
                TranspileInterpolatedString(interpolated);
                break;
            case LambdaExpression lambda:
                TranspileLambda(lambda);
                break;
            case MatchExpression match:
                TranspileMatch(match);
                break;
            case PipeExpression pipe:
                TranspilePipe(pipe);
                break;
            case ListComprehensionExpression comprehension:
                TranspileListComprehension(comprehension);
                break;
            case DictComprehensionExpression dictComprehension:
                TranspileDictComprehension(dictComprehension);
                break;
            case AwaitExpression awaitExpr:
                if (awaitExpr.Expression is FunctionCallExpression awaitCall &&
                    awaitCall.Callee is IdentifierExpression awaitCallee &&
                    _promptNames.Contains(awaitCallee.Name))
                {
                    _output.Append("await ");
                    _output.Append(EscapeIdentifier(awaitCallee.Name));
                    _output.Append("__ExecuteAsync(");
                    for (int i = 0; i < awaitCall.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        TranspileExpression(awaitCall.Arguments[i]);
                    }
                    _output.Append(")");
                }
                else if (awaitExpr.Expression is PipeExpression awaitPipe)
                {
                    _output.Append("await MaldaLang.BuiltIns.AiPipelineHelpers.CoerceAwaitResultAsync(RuntimeHelpers.ToRuntimeValue(");
                    TranspilePipe(awaitPipe);
                    _output.Append("), null)");
                }
                else
                {
                    _output.Append("await RuntimeHelpers.UnwrapTaskAsync(");
                    TranspileExpression(awaitExpr.Expression);
                    _output.Append(")");
                }
                break;
            case AsyncExpression asyncExpr:
                if (asyncExpr.Expression is FunctionCallExpression asyncCall)
                {
                    _output.Append("MaldaLang.Interpreter.RuntimeValue.Task(");
                    _transpileCallAsTask = true;
                    TranspileFunctionCall(asyncCall);
                    _transpileCallAsTask = false;
                    _output.Append(")");
                }
                else
                {
                    _output.Append("MaldaLang.Interpreter.RuntimeValue.Task(System.Threading.Tasks.Task.FromResult(RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(asyncExpr.Expression);
                    _output.Append(")))");
                }
                break;
            default:
                throw new NotSupportedException($"Expression type {expr.GetType().Name} is not supported in transpiler");
        }
    }

    private static readonly HashSet<string> ArrayPipelineMethods = new(StringComparer.Ordinal)
    {
        "append", "pop", "shift", "concat", "popOrNull", "shiftOrNull", "get", "at",
        "map", "filter", "reduce", "forEach", "find", "findIndex", "some", "every",
        "sort", "reverse", "slice", "indexOf", "includes", "join", "sum", "average", "min", "max"
    };

    private void TranspilePipe(PipeExpression pipe)
    {
        var left = pipe.Left;
        var right = pipe.Right;

        switch (right)
        {
            case MemberAccessExpression member:
            {
                var memberCall = new FunctionCallExpression(member, [], pipe.Line, pipe.Column);
                TranspileFunctionCall(memberCall);
                return;
            }

            case FunctionCallExpression call when call.Callee is IdentifierExpression id:
                if (ArrayPipelineMethods.Contains(id.Name))
                {
                    var memberCall = new FunctionCallExpression(
                        new MemberAccessExpression(left, id.Name, line: pipe.Line, column: pipe.Column),
                        call.Arguments,
                        pipe.Line,
                        pipe.Column);
                    TranspileFunctionCall(memberCall);
                    return;
                }

                TranspilePipedIdentifierCall(id.Name, left, call.Arguments, pipe.Line, pipe.Column);
                return;

            case FunctionCallExpression call:
            {
                var args = new List<Expression> { left };
                args.AddRange(call.Arguments);
                var pipedCall = new FunctionCallExpression(call.Callee, args, pipe.Line, pipe.Column);
                TranspileFunctionCall(pipedCall);
                return;
            }

            case IdentifierExpression id:
                TranspilePipedIdentifierCall(id.Name, left, [], pipe.Line, pipe.Column);
                return;

            case LambdaExpression lambda:
                if (_canAwait)
                    _output.Append("await RuntimeHelpers.CallFunction(");
                else
                    _output.Append("RuntimeHelpers.BlockOn(RuntimeHelpers.CallFunction(");
                TranspileLambda(lambda);
                _output.Append(", ");
                TranspileExpression(left);
                _output.Append(")");
                if (!_canAwait)
                    _output.Append(")");
                return;

            default:
                throw new NotSupportedException(
                    $"Right side of |> must be a function call, identifier, or lambda (got {right.GetType().Name}).");
        }
    }

    private void TranspilePipedIdentifierCall(string name, Expression left, List<Expression> tailArgs, int line, int column)
    {
        if (_promptNames.Contains(name))
        {
            _output.Append(EscapeIdentifier(name));
            _output.Append("(");
            TranspileExpression(left);
            for (int i = 0; i < tailArgs.Count; i++)
            {
                _output.Append(", ");
                TranspileExpression(tailArgs[i]);
            }
            _output.Append(")");
            return;
        }

        if (IsBuiltInFunction(name))
        {
            var args = new List<Expression> { left };
            args.AddRange(tailArgs);
            TranspileBuiltInFunction(name, args);
            return;
        }

        if (_functionNames.Contains(name))
        {
            if (_canAwait)
                _output.Append("await ");
            else
                _output.Append("RuntimeHelpers.BlockOn(");
            _output.Append(EscapeIdentifier(name));
            _output.Append("(");
            TranspileExpression(left);
            for (int i = 0; i < tailArgs.Count; i++)
            {
                _output.Append(", ");
                TranspileExpression(tailArgs[i]);
            }
            _output.Append(")");
            if (!_canAwait)
                _output.Append(")");
            return;
        }

        if (_canAwait)
            _output.Append("await RuntimeHelpers.CallFunction(");
        else
            _output.Append("RuntimeHelpers.BlockOn(RuntimeHelpers.CallFunction(");
        _output.Append(EscapeIdentifier(name));
        _output.Append(", ");
        TranspileExpression(left);
        _output.Append(")");
        if (!_canAwait)
            _output.Append(")");
    }

    private void TranspileListComprehension(ListComprehensionExpression comp)
    {
        _output.Append("(new System.Func<List<object>>(() => { var __list = new List<object>(); foreach (object ");
        _output.Append(EscapeIdentifier(comp.Variable));
        _output.Append(" in RuntimeHelpers.GetArray(");
        TranspileExpression(comp.Iterable);
        _output.Append(")) {");

        if (comp.Filter != null)
        {
            _output.Append(" if (!RuntimeHelpers.CoerceToBool(");
            TranspileExpression(comp.Filter);
            _output.Append(")) continue;");
        }

        _output.Append(" __list.Add(");
        TranspileExpression(comp.Element);
        _output.Append("); } return __list; }))()");
    }

    private void TranspileDictComprehension(DictComprehensionExpression comp)
    {
        _output.Append("(new System.Func<System.Collections.Generic.Dictionary<string, object?>>(() => { var __dict = new System.Collections.Generic.Dictionary<string, object?>(); foreach (object ");
        _output.Append(EscapeIdentifier(comp.Variable));
        _output.Append(" in RuntimeHelpers.GetArray(");
        TranspileExpression(comp.Iterable);
        _output.Append(")) {");

        if (comp.Filter != null)
        {
            _output.Append(" if (!RuntimeHelpers.CoerceToBool(");
            TranspileExpression(comp.Filter);
            _output.Append(")) continue;");
        }

        _output.Append(" __dict[RuntimeHelpers.CoerceToString(");
        TranspileExpression(comp.Key);
        _output.Append(")] = ");
        TranspileExpression(comp.Value);
        _output.Append("; } return __dict; }))()");
    }

    private void TranspileLiteral(LiteralExpression literal)
    {
        if (literal.Value == null)
        {
            _output.Append("null");
        }
        else if (literal.Value is bool b)
        {
            _output.Append(b ? "true" : "false");
        }
        else if (literal.Value is string s)
        {
            _output.Append("\"");
            _output.Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r"));
            _output.Append("\"");
        }
        else if (literal.Value is int || literal.Value is long)
        {
            _output.Append(literal.Value);
        }
        else if (literal.Value is double || literal.Value is float)
        {
            _output.Append(literal.Value);
        }
        else
        {
            _output.Append(literal.Value);
        }
    }

    private void TranspileBinary(BinaryExpression binary)
    {
        if (IsNumericBinaryOperator(binary.Operator) &&
            ResolveExpressionType(binary.Left) == TranspiledClrType.Double &&
            ResolveExpressionType(binary.Right) == TranspiledClrType.Double)
        {
            _output.Append("(");
            TranspileExpression(binary.Left);
            _output.Append(" ");
            _output.Append(GetOperatorString(binary.Operator));
            _output.Append(" ");
            TranspileExpression(binary.Right);
            _output.Append(")");
            return;
        }

        string? helper = binary.Operator switch
        {
            TokenType.Plus => "OperatorAdd",
            TokenType.Minus => "OperatorSubtract",
            TokenType.Multiply => "OperatorMultiply",
            TokenType.Divide => "OperatorDivide",
            TokenType.Modulo => "OperatorModulo",
            TokenType.Equal => "OperatorEqual",
            TokenType.NotEqual => "OperatorNotEqual",
            TokenType.LessThan => "OperatorLessThan",
            TokenType.LessThanOrEqual => "OperatorLessThanOrEqual",
            TokenType.GreaterThan => "OperatorGreaterThan",
            TokenType.GreaterThanOrEqual => "OperatorGreaterThanOrEqual",
            _ => null
        };

        if (helper != null)
        {
            _output.Append("RuntimeHelpers.");
            _output.Append(helper);
            _output.Append("(");
            TranspileExpression(binary.Left);
            _output.Append(", ");
            TranspileExpression(binary.Right);
            _output.Append(")");
            return;
        }

        // Logical && / || must coerce operands: many MALDA values are `object` (e.g. dict members),
        // while comparisons use RuntimeHelpers and yield bool — raw C# &&/|| then mix types (CS0019).
        if (binary.Operator == TokenType.And)
        {
            _output.Append("(RuntimeHelpers.CoerceToBool(");
            TranspileExpression(binary.Left);
            _output.Append(") && RuntimeHelpers.CoerceToBool(");
            TranspileExpression(binary.Right);
            _output.Append("))");
            return;
        }

        if (binary.Operator == TokenType.Or)
        {
            _output.Append("(RuntimeHelpers.CoerceToBool(");
            TranspileExpression(binary.Left);
            _output.Append(") || RuntimeHelpers.CoerceToBool(");
            TranspileExpression(binary.Right);
            _output.Append("))");
            return;
        }

        _output.Append("(");
        TranspileExpression(binary.Left);
        _output.Append(" ");
        _output.Append(GetOperatorString(binary.Operator));
        _output.Append(" ");
        TranspileExpression(binary.Right);
        _output.Append(")");
    }

    private void TranspileUnary(UnaryExpression unary)
    {
        _output.Append("(");
        if (unary.Operator == TokenType.Not)
        {
            // For logical NOT, coerce to bool first, but don't double-wrap
            // The result is already a boolean, so we don't need CoerceToBool again
            _output.Append("!");
            _output.Append("RuntimeHelpers.CoerceToBool(");
            TranspileExpression(unary.Right);
            _output.Append(")");
        }
        else if (unary.Operator == TokenType.Minus)
        {
            _output.Append("RuntimeHelpers.OperatorNegate(");
            TranspileExpression(unary.Right);
            _output.Append(")");
        }
        else if (unary.Operator == TokenType.Increment || unary.Operator == TokenType.Decrement)
        {
            // Prefix ++x / --x: assign checked result and use as expression value
            var helper = unary.Operator == TokenType.Increment ? "CheckedIntIncrement" : "CheckedIntDecrement";
            TranspileExpression(unary.Right);
            _output.Append(" = RuntimeHelpers.").Append(helper).Append("((int)RuntimeHelpers.CoerceToInt(");
            TranspileExpression(unary.Right);
            _output.Append("))");
        }
        else
        {
            _output.Append(GetUnaryOperatorString(unary.Operator));
            TranspileExpression(unary.Right);
        }
        _output.Append(")");
    }
    
    private void TranspilePostfix(PostfixExpression postfix)
    {
        // For postfix increment/decrement on object types, we need to convert to assignment
        // i++ becomes: i = RuntimeHelpers.CheckedIntIncrement((int)RuntimeHelpers.CoerceToInt(i))
        // i-- becomes: i = RuntimeHelpers.CheckedIntDecrement((int)RuntimeHelpers.CoerceToInt(i))
        if (postfix.Operator == TokenType.Increment)
        {
            TranspileExpression(postfix.Left);
            _output.Append(" = RuntimeHelpers.CheckedIntIncrement((int)RuntimeHelpers.CoerceToInt(");
            TranspileExpression(postfix.Left);
            _output.Append("))");
        }
        else if (postfix.Operator == TokenType.Decrement)
        {
            TranspileExpression(postfix.Left);
            _output.Append(" = RuntimeHelpers.CheckedIntDecrement((int)RuntimeHelpers.CoerceToInt(");
            TranspileExpression(postfix.Left);
            _output.Append("))");
        }
        else
        {
            TranspileExpression(postfix.Left);
            _output.Append(GetUnaryOperatorString(postfix.Operator));
        }
    }

    private void TranspileFunctionCall(FunctionCallExpression call)
    {
        // Variant constructor call: Ok(expr) -> Ok(RuntimeHelpers.ToRuntimeValue(expr))
        if (call.Callee is IdentifierExpression variantCtorId && _variantConstructorNames.Contains(variantCtorId.Name))
        {
            _output.Append(EscapeIdentifier(variantCtorId.Name));
            _output.Append("(");
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                _output.Append("RuntimeHelpers.ToRuntimeValue(");
                TranspileExpression(call.Arguments[i]);
                _output.Append(")");
            }
            _output.Append(")");
            return;
        }

        // Check if this is a member access call (like arr.append(x))
        if (call.Callee is MemberAccessExpression memberAccess)
        {
            var memberName = memberAccess.Member;
            if (memberName == "append")
            {
                var memberObjectType = ResolveExpressionType(memberAccess.Object);
                if (call.Arguments.Count > 0)
                {
                    if (memberObjectType == TranspiledClrType.DoubleArray)
                    {
                        _output.Append("RuntimeHelpers.ArrayAppendDouble(RuntimeHelpers.CoerceToDoubleList(");
                        TranspileExpression(memberAccess.Object);
                        _output.Append("), ");
                        TranspileExpression(call.Arguments[0]);
                        _output.Append(")");
                        return;
                    }
                    // Check if the argument is a function call that needs to be awaited
                    var argExpr = call.Arguments[0];
                    bool isAsyncCall = false;
                    string? funcParamName = null;
                    Expression? funcArg = null;
                    
                    if (argExpr is FunctionCallExpression funcCall)
                    {
                        // Check if this is a function call that returns a Task
                        // This includes function parameters and lambda calls
                        if (funcCall.Callee is IdentifierExpression argIdExpr)
                        {
                            // If it's not a known function name, it's likely a function parameter
                            if (!_functionNames.Contains(argIdExpr.Name) && !IsBuiltInFunction(argIdExpr.Name))
                            {
                                // Function parameter call - needs to be awaited
                                isAsyncCall = true;
                                funcParamName = argIdExpr.Name;
                                funcArg = funcCall.Arguments.Count > 0 ? funcCall.Arguments[0] : null;
                            }
                            // If it's a known function, it's already async and returns Task<object>
                            else if (_functionNames.Contains(argIdExpr.Name))
                            {
                                // User-defined function - also returns Task, needs async handling
                                isAsyncCall = true;
                                funcParamName = null; // Will handle differently
                                funcArg = null;
                            }
                        }
                        // Lambda expressions also return Task<object>
                        else if (funcCall.Callee is LambdaExpression)
                        {
                            isAsyncCall = true;
                            funcParamName = null;
                            funcArg = null;
                        }
                    }
                    
                    if (isAsyncCall)
                    {
                        // Function call that returns Task - use async version
                        _output.Append("await RuntimeHelpers.ArrayAppendAsync(RuntimeHelpers.GetArray(");
                        TranspileExpression(memberAccess.Object);
                        _output.Append("), ");
                        
                        if (funcParamName != null)
                        {
                            // Function parameter call
                            _output.Append("RuntimeHelpers.CallFunction(");
                            _output.Append(EscapeIdentifier(funcParamName));
                            _output.Append(", ");
                            if (funcArg != null)
                                TranspileExpression(funcArg);
                            else
                                _output.Append("null");
                            _output.Append(")");
                        }
                        else
                        {
                            // User-defined function or lambda - transpile the call which will include await
                            // We need to wrap it to get the Task without the await
                            _output.Append("(");
                            // Transpile without await - we'll get the Task
                            var funcCallExpr = (FunctionCallExpression)argExpr;
                            if (funcCallExpr.Callee is IdentifierExpression funcIdExpr && _functionNames.Contains(funcIdExpr.Name))
                            {
                                // User-defined function - call directly to get Task
                                _output.Append(EscapeIdentifier(funcIdExpr.Name));
                                _output.Append("(");
                                for (int i = 0; i < funcCallExpr.Arguments.Count; i++)
                                {
                                    if (i > 0) _output.Append(", ");
                                    TranspileExpression(funcCallExpr.Arguments[i]);
                                }
                                _output.Append(")");
                            }
                            else
                            {
                                // For other cases, just transpile normally - ArrayAppendAsync will await it
                                TranspileExpression(argExpr);
                            }
                            _output.Append(")");
                        }
                        _output.Append(")");
                    }
                    else
                    {
                        // Regular argument - use synchronous version
                        _output.Append("RuntimeHelpers.ArrayAppend(RuntimeHelpers.GetArray(");
                        TranspileExpression(memberAccess.Object);
                        _output.Append("), ");
                        TranspileExpression(argExpr);
                        _output.Append(")");
                    }
                }
                else
                {
                    if (memberObjectType == TranspiledClrType.DoubleArray)
                    {
                        _output.Append("RuntimeHelpers.ArrayAppendDouble(RuntimeHelpers.CoerceToDoubleList(");
                        TranspileExpression(memberAccess.Object);
                        _output.Append("), 0d)");
                        return;
                    }
                    _output.Append("RuntimeHelpers.ArrayAppend(RuntimeHelpers.GetArray(");
                    TranspileExpression(memberAccess.Object);
                    _output.Append("), null)");
                }
                return;
            }
            else if (memberName == "pop")
            {
                _output.Append("RuntimeHelpers.ArrayPop(RuntimeHelpers.GetArray(");
                TranspileExpression(memberAccess.Object);
                _output.Append("))");
                return;
            }
            else if (memberName == "shift")
            {
                _output.Append("RuntimeHelpers.ArrayShift(RuntimeHelpers.GetArray(");
                TranspileExpression(memberAccess.Object);
                _output.Append("))");
                return;
            }
            else if (memberName == "concat")
            {
                _output.Append("RuntimeHelpers.ArrayConcat(RuntimeHelpers.GetArray(");
                TranspileExpression(memberAccess.Object);
                _output.Append("), RuntimeHelpers.GetArray(");
                if (call.Arguments.Count > 0)
                    TranspileExpression(call.Arguments[0]);
                else
                    _output.Append("new List<object>()");
                _output.Append("))");
                return;
            }
            else if ((memberName == "sum" || memberName == "average" || memberName == "min" || memberName == "max") &&
                     call.Arguments.Count == 0)
            {
                TranspileBuiltInFunction(memberName, new List<Expression> { memberAccess.Object });
                return;
            }
            else if (memberAccess.Object is IdentifierExpression taIdExpr &&
                     taIdExpr.Name == "ta" &&
                     OptionalPackTranspilerBuiltIns.IsTimeseriesName(memberName))
            {
                TranspileBuiltInFunction(memberName, call.Arguments);
                return;
            }
        }

        // Check if this is a built-in function call
        if (call.Callee is IdentifierExpression identifier)
        {
            var funcName = identifier.Name;
            if (IsBuiltInFunction(funcName))
            {
                TranspileBuiltInFunction(funcName, call.Arguments);
                return;
            }
        }

        // Check if this is a method call on an ObjectInstance or actor reference (e.g., server.start(), actor.stop())
        if (call.Callee is MemberAccessExpression memberAccess2)
        {
            var methodName = memberAccess2.Member;

            // Special handling for .stop() that may target either an actor (ActorRef) or
            // a regular object (e.g., HttpServerInstance, MCPServerInstance).
            if (methodName == "stop" && call.Arguments.Count == 0)
            {
                _output.Append("RuntimeHelpers.CallActorOrVoidStop(");
                TranspileExpression(memberAccess2.Object);
                _output.Append(")");
                return;
            }

            // For HttpServerInstance, call public methods directly
            if (methodName == "start" || methodName == "stop" || methodName == "clearCache" || methodName == "getRoutes")
            {
                if (methodName == "getRoutes")
                {
                    // getRoutes returns a RuntimeValue that needs to be unwrapped
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(");
                    _output.Append("((");
                    TranspileExpression(memberAccess2.Object);
                    _output.Append(") as MaldaLang.BuiltIns.HttpServerInstance)?.GetRoutes() ?? await RuntimeHelpers.CallObjectMethod(");
                    TranspileExpression(memberAccess2.Object);
                    _output.Append(", \"getRoutes\", new List<object>())");
                    _output.Append(")");
                }
                else
                {
                    // void methods - use helper
                    _output.Append("RuntimeHelpers.CallVoidMethod(");
                    TranspileExpression(memberAccess2.Object);
                    _output.Append(", \"");
                    _output.Append(methodName);
                    _output.Append("\")");
                }
                return;
            }

            // math.* / Math.* (deprecated alias) — map to existing built-ins
            if (memberAccess2.Object is IdentifierExpression mathIdExpr &&
                (mathIdExpr.Name == StdLibNamespaces.MathModule || mathIdExpr.Name == StdLibNamespaces.DeprecatedMathModuleAlias) &&
                StdLibNamespaces.MathMethodNames.Contains(memberAccess2.Member))
            {
                TranspileBuiltInFunction(memberAccess2.Member, call.Arguments);
                return;
            }
            if (memberAccess2.Object is IdentifierExpression strIdExpr &&
                strIdExpr.Name == StdLibNamespaces.StrModule &&
                StdLibNamespaces.StrMethodNames.Contains(memberAccess2.Member))
            {
                TranspileBuiltInFunction(memberAccess2.Member, call.Arguments);
                return;
            }
            if (memberAccess2.Object is IdentifierExpression ioIdExpr &&
                ioIdExpr.Name == StdLibNamespaces.IoModule &&
                StdLibNamespaces.IoMethodNames.Contains(memberAccess2.Member))
            {
                TranspileBuiltInFunction(memberAccess2.Member, call.Arguments);
                return;
            }
            if (TryTranspileVariantStdLibCall(memberAccess2, call))
            {
                return;
            }
            if (memberAccess2.Object is IdentifierExpression taIdExpr &&
                taIdExpr.Name == "ta" &&
                OptionalPackTranspilerBuiltIns.IsTimeseriesName(memberAccess2.Member))
            {
                TranspileBuiltInFunction(memberAccess2.Member, call.Arguments);
                return;
            }
            // Special handling for AnsiConsole methods
            if (memberAccess2.Object is IdentifierExpression ansiConsoleIdExpr && ansiConsoleIdExpr.Name == "AnsiConsole")
            {
                var ansiMethodName = memberAccess2.Member;
                if (ansiMethodName == "markup")
                {
                    // Sync method - call Spectre.Console directly
                    if (call.Arguments.Count != 1)
                        throw new Exception("AnsiConsole.markup() expects 1 argument");
                    _output.Append("Spectre.Console.AnsiConsole.Markup(RuntimeHelpers.CoerceToString(");
                    TranspileExpression(call.Arguments[0]);
                    _output.Append("))");
                    return;
                }
                else if (ansiMethodName == "markupLine")
                {
                    // Sync method - call Spectre.Console directly
                    if (call.Arguments.Count != 1)
                        throw new Exception("AnsiConsole.markupLine() expects 1 argument");
                    _output.Append("Spectre.Console.AnsiConsole.MarkupLine(RuntimeHelpers.CoerceToString(");
                    TranspileExpression(call.Arguments[0]);
                    _output.Append("))");
                    return;
                }
                else if (ansiMethodName == "table")
                {
                    // Call the built-in function directly
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.BuiltInSpectreConsoleTable(new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        _output.Append("RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(call.Arguments[i]);
                        _output.Append(")");
                    }
                    _output.Append(" }))");
                    return;
                }
                else if (ansiMethodName == "panel")
                {
                    // Call the built-in function directly
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.BuiltInSpectreConsolePanel(new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        _output.Append("RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(call.Arguments[i]);
                        _output.Append(")");
                    }
                    _output.Append(" }))");
                    return;
                }
                else if (ansiMethodName == "tree")
                {
                    // Call the built-in function directly
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.BuiltInSpectreConsoleTree(new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        _output.Append("RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(call.Arguments[i]);
                        _output.Append(")");
                    }
                    _output.Append(" }))");
                    return;
                }
                else if (ansiMethodName == "status")
                {
                    // Async method with callback - transpile to Spectre.Console directly
                    if (call.Arguments.Count < 1)
                        throw new Exception("AnsiConsole.status() expects at least 1 argument");
                    
                    _output.Append("await Spectre.Console.AnsiConsole.Status().StartAsync(RuntimeHelpers.CoerceToString(");
                    TranspileExpression(call.Arguments[0]);
                    _output.Append("), async ctx => ");
                    
                    if (call.Arguments.Count > 1)
                    {
                        // Has callback - transpile it
                        var callbackArg = call.Arguments[1];
                        if (callbackArg is LambdaExpression lambda)
                        {
                            // Transpile lambda directly
                            _output.Append("{");
                            _indentLevel++;
                            _output.AppendLine();
                            WriteIndent();
                            
                            // Transpile lambda body
                            if (lambda.BlockBody != null)
                            {
                                foreach (var stmt in lambda.BlockBody.Statements)
                                {
                                    WriteIndent();
                                    TranspileStatement(stmt);
                                }
                            }
                            else if (lambda.ExpressionBody != null)
                            {
                                _output.Append("await ");
                                TranspileExpression(lambda.ExpressionBody);
                                _output.AppendLine(";");
                            }
                            
                            _indentLevel--;
                            WriteIndent();
                            _output.Append("}");
                        }
                        else
                        {
                            // Function reference - call it
                            _output.Append("{ await ");
                            TranspileExpression(callbackArg);
                            _output.Append("(null); }");
                        }
                    }
                    else
                    {
                        _output.Append("{ }");
                    }
                    _output.Append(")");
                    return;
                }
                else if (ansiMethodName == "prompt")
                {
                    // Prompt doesn't need interpreter - call built-in function directly
                    if (call.Arguments.Count < 1)
                        throw new Exception("AnsiConsole.prompt() expects at least 1 argument");
                    
                    _output.Append("await RuntimeHelpers.UnwrapRuntimeValueAsync(MaldaLang.BuiltIns.BuiltInFunctions.BuiltInSpectreConsolePromptAsync(new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(call.Arguments[0]);
                    _output.Append(")");
                    _output.Append(" }, null))");
                    return;
                }
                else if (ansiMethodName == "progress")
                {
                    // Transpile progress to call Spectre.Console directly with callback support
                    if (call.Arguments.Count < 1)
                        throw new Exception("AnsiConsole.progress() expects at least 1 argument");
                    
                    // Check if the first argument is a lambda (new callback syntax)
                    if (call.Arguments[0] is LambdaExpression callbackLambda)
                    {
                        // New callback-based syntax: AnsiConsole.progress((ctx) => { ... })
                        _output.Append("await Spectre.Console.AnsiConsole.Progress().StartAsync(async ctx =>");
                        _output.AppendLine();
                        WriteIndent();
                        _output.Append("{");
                        _indentLevel++;
                        _output.AppendLine();
                        
                        // Create task dictionary to map task names to ProgressTask instances
                        WriteIndent();
                        _output.AppendLine("var __taskDict = new System.Collections.Generic.Dictionary<string, Spectre.Console.ProgressTask>();");
                        _output.AppendLine();
                        
                        // Create wrapper instance (shared by all methods)
                        WriteIndent();
                        _output.AppendLine("var __progressCtxWrapper = new MaldaLang.BuiltIns.ProgressContextWrapper(ctx, __taskDict);");
                        _output.AppendLine();
                        
                        // Create wrapper object with addTask, increment, and isFinished methods
                        WriteIndent();
                        _output.AppendLine("var __progressCtx = new MaldaLang.BuiltIns.JsonObject();");
                        _output.AppendLine();
                        
                        // Add addTask method
                        WriteIndent();
                        _output.AppendLine("__progressCtx.Set(\"addTask\", MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue(null, null, false, null)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("BuiltInInstance = __progressCtxWrapper,");
                        WriteIndent();
                        _output.AppendLine("BuiltInMethod = \"addTask\"");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}));");
                        _output.AppendLine();
                        
                        // Add increment method
                        WriteIndent();
                        _output.AppendLine("__progressCtx.Set(\"increment\", MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue(null, null, false, null)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("BuiltInInstance = __progressCtxWrapper,");
                        WriteIndent();
                        _output.AppendLine("BuiltInMethod = \"increment\"");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}));");
                        _output.AppendLine();
                        
                        // Add isFinished method
                        WriteIndent();
                        _output.AppendLine("__progressCtx.Set(\"isFinished\", MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue(null, null, false, null)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("BuiltInInstance = __progressCtxWrapper,");
                        WriteIndent();
                        _output.AppendLine("BuiltInMethod = \"isFinished\"");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}));");
                        _output.AppendLine();
                        
                        // Transpile the lambda body with the wrapper context
                        WriteIndent();
                        _output.Append("// Execute callback with progress context wrapper");
                        _output.AppendLine();
                        if (callbackLambda.Parameters.Count > 0)
                        {
                            WriteIndent();
                            _output.Append("var ");
                            _output.Append(EscapeIdentifier(callbackLambda.Parameters[0]));
                            _output.Append(" = MaldaLang.Interpreter.RuntimeValue.Object(__progressCtx);");
                            _output.AppendLine();
                        }
                        
                        // Transpile lambda body
                        if (callbackLambda.BlockBody != null)
                        {
                            foreach (var stmt in callbackLambda.BlockBody.Statements)
                            {
                                WriteIndent();
                                TranspileStatement(stmt);
                            }
                        }
                        else if (callbackLambda.ExpressionBody != null)
                        {
                            WriteIndent();
                            _output.Append("await ");
                            TranspileExpression(callbackLambda.ExpressionBody);
                            _output.AppendLine(";");
                        }
                        
                        _indentLevel--;
                        WriteIndent();
                        _output.Append("}");
                        return;
                    }
                    
                    // Check if the first argument is an object literal with a lambda callback (old syntax)
                    LambdaExpression? actionLambda = null;
                    if (call.Arguments[0] is ObjectLiteralExpression objLit)
                    {
                        // Look for "action" property that is a lambda
                        foreach (var (key, value) in objLit.Properties)
                        {
                            if (key is LiteralExpression keyLit && keyLit.Value is string keyStr && keyStr == "action")
                            {
                                if (value is LambdaExpression lambda)
                                {
                                    actionLambda = lambda;
                                }
                                break;
                            }
                        }
                    }
                    
                    // The first argument is an object with "tasks" array and optional "action" callback
                    _output.Append("await Spectre.Console.AnsiConsole.Progress().StartAsync(async ctx =>");
                    _output.AppendLine();
                    WriteIndent();
                    _output.Append("{");
                    _indentLevel++;
                    _output.AppendLine();
                    
                    // Extract tasks array and action from the object
                    WriteIndent();
                    _output.Append("var __progressObj = RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(call.Arguments[0]);
                    _output.AppendLine(").AsObject() as MaldaLang.BuiltIns.JsonObject;");
                    
                    WriteIndent();
                    _output.AppendLine("var __tasksValue = __progressObj?.Get(\"tasks\", null);");
                    WriteIndent();
                    _output.AppendLine("var __tasksArray = __tasksValue != null && __tasksValue.Type == MaldaLang.Interpreter.ValueType.Array ? __tasksValue.AsArray() : new MaldaLang.Interpreter.ArrayInstance();");
                    WriteIndent();
                    _output.AppendLine("var __progressTasks = new System.Collections.Generic.List<Spectre.Console.ProgressTask>();");
                    _output.AppendLine();
                    
                    // Create progress tasks
                    WriteIndent();
                    _output.AppendLine("foreach (var __task in __tasksArray)");
                    WriteIndent();
                    _output.AppendLine("{");
                    _indentLevel++;
                    WriteIndent();
                    _output.AppendLine("if (__task.Type == MaldaLang.Interpreter.ValueType.Object && __task.AsObject() is MaldaLang.BuiltIns.JsonObject __taskObj)");
                    WriteIndent();
                    _output.AppendLine("{");
                    _indentLevel++;
                    WriteIndent();
                    _output.AppendLine("var __taskNameValue = __taskObj.Get(\"name\", null);");
                    WriteIndent();
                    _output.AppendLine("var __taskName = __taskNameValue != null && __taskNameValue.Type == MaldaLang.Interpreter.ValueType.String ? __taskNameValue.AsString() : \"\";");
                    WriteIndent();
                    _output.AppendLine("var __maxValueValue = __taskObj.Get(\"maxValue\", null);");
                    WriteIndent();
                    _output.AppendLine("var __maxValue = __maxValueValue != null && __maxValueValue.Type == MaldaLang.Interpreter.ValueType.Integer ? __maxValueValue.AsInteger() : (__maxValueValue != null && __maxValueValue.Type == MaldaLang.Interpreter.ValueType.Float ? (int)__maxValueValue.AsFloat() : 100);");
                    WriteIndent();
                    _output.AppendLine("var __progressTask = ctx.AddTask(__taskName, maxValue: __maxValue);");
                    WriteIndent();
                    _output.AppendLine("__progressTasks.Add(__progressTask);");
                    _indentLevel--;
                    WriteIndent();
                    _output.AppendLine("}");
                    _indentLevel--;
                    WriteIndent();
                    _output.AppendLine("}");
                    _output.AppendLine();
                    
                    // Execute action callback if provided
                    if (actionLambda != null)
                    {
                        // We have a lambda in the AST - transpile it directly
                        WriteIndent();
                        _output.AppendLine("// Create progress wrapper object for callback");
                        WriteIndent();
                        _output.AppendLine("var __progressWrapper = new MaldaLang.BuiltIns.JsonObject();");
                        WriteIndent();
                        _output.AppendLine("for (int __i = 0; __i < __progressTasks.Count; __i++)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("var __taskName = $\"Task{__i}\";");
                        WriteIndent();
                        _output.AppendLine("if (__i < __tasksArray.Count && __tasksArray[__i].Type == MaldaLang.Interpreter.ValueType.Object && __tasksArray[__i].AsObject() is MaldaLang.BuiltIns.JsonObject __t)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("var __nameValue = __t.Get(\"name\", null);");
                        WriteIndent();
                        _output.AppendLine("if (__nameValue != null && __nameValue.Type == MaldaLang.Interpreter.ValueType.String)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("__taskName = __nameValue.AsString();");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        WriteIndent();
                        _output.AppendLine("__progressWrapper.Set(__taskName, MaldaLang.Interpreter.RuntimeValue.Integer((int)__progressTasks[__i].Value));");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        _output.AppendLine();
                        
                        // Transpile the lambda body directly
                        WriteIndent();
                        _output.Append("// Call callback with progress object");
                        _output.AppendLine();
                        WriteIndent();
                        if (actionLambda.Parameters.Count > 0)
                        {
                            _output.Append("var ");
                            _output.Append(EscapeIdentifier(actionLambda.Parameters[0]));
                            _output.Append(" = MaldaLang.Interpreter.RuntimeValue.Object(__progressWrapper);");
                            _output.AppendLine();
                        }
                        
                        // Transpile lambda body
                        if (actionLambda.BlockBody != null)
                        {
                            foreach (var stmt in actionLambda.BlockBody.Statements)
                            {
                                WriteIndent();
                                TranspileStatement(stmt);
                            }
                        }
                        else if (actionLambda.ExpressionBody != null)
                        {
                            WriteIndent();
                            _output.Append("await ");
                            TranspileExpression(actionLambda.ExpressionBody);
                            _output.AppendLine(";");
                        }
                    }
                    else
                    {
                        // Fallback: extract action at runtime and call it
                        WriteIndent();
                        _output.AppendLine("var __actionValue = __progressObj?.Get(\"action\", null);");
                        WriteIndent();
                        _output.AppendLine("if (__actionValue != null && __actionValue.Type == MaldaLang.Interpreter.ValueType.Function)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("// Create progress wrapper object");
                        WriteIndent();
                        _output.AppendLine("var __progressWrapper = new MaldaLang.BuiltIns.JsonObject();");
                        WriteIndent();
                        _output.AppendLine("for (int __i = 0; __i < __progressTasks.Count; __i++)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("var __taskName = $\"Task{__i}\";");
                        WriteIndent();
                        _output.AppendLine("if (__i < __tasksArray.Count && __tasksArray[__i].Type == MaldaLang.Interpreter.ValueType.Object && __tasksArray[__i].AsObject() is MaldaLang.BuiltIns.JsonObject __t)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("var __nameValue = __t.Get(\"name\", null);");
                        WriteIndent();
                        _output.AppendLine("if (__nameValue != null && __nameValue.Type == MaldaLang.Interpreter.ValueType.String)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("__taskName = __nameValue.AsString();");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        WriteIndent();
                        _output.AppendLine("__progressWrapper.Set(__taskName, MaldaLang.Interpreter.RuntimeValue.Integer((int)__progressTasks[__i].Value));");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        WriteIndent();
                        _output.AppendLine("// Note: Function callbacks in transpiled code require runtime function calling");
                        WriteIndent();
                        _output.AppendLine("// This may not work for all cases - prefer using lambda expressions");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                    }
                    
                    _indentLevel--;
                    WriteIndent();
                    _output.Append("}");
                    return;
                }
            }
            
            // Special handling for ui.* methods
            if (memberAccess2.Object is IdentifierExpression uiIdExpr && uiIdExpr.Name == "ui")
            {
                var uiMethodName = memberAccess2.Member;
                var builtInMethodName = "ui" + char.ToUpper(uiMethodName[0]) + uiMethodName.Substring(1);
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(");
                if (uiMethodName == "generate")
                {
                    if (_canAwait)
                        _output.Append("await MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltInAsync(\"");
                    else
                        _output.Append("MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltInAsync(\"");
                }
                else
                {
                    _output.Append("MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                }
                _output.Append(builtInMethodName);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(call.Arguments[i]);
                    _output.Append(")");
                }
                if (uiMethodName == "generate" && !_canAwait)
                    _output.Append(" }, null).GetAwaiter().GetResult())");
                else
                    _output.Append(" }, null))");
                return;
            }
            
            // Extension-style string methods (receiver may be string primitive: s.upper(), s.substring(i, len), etc.)
            if (IsStringExtensionMethod(memberAccess2.Member))
            {
                TranspileStringExtensionCall(memberAccess2.Object, memberAccess2.Member, call.Arguments);
                return;
            }
            
            // For other ObjectInstance methods, use CallObjectMethod
            if (call.Callee is MemberAccessExpression memberAccess3 && memberAccess3.Object != null)
            {
                if (_canAwait)
                    _output.Append("await RuntimeHelpers.CallObjectMethod(");
                else
                    _output.Append("RuntimeHelpers.BlockOn(RuntimeHelpers.CallObjectMethod(");
                TranspileExpression(memberAccess3.Object);
                _output.Append(", \"");
                _output.Append(memberAccess3.Member);
                _output.Append("\", new List<object> { ");
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    if (call.Arguments[i] is IdentifierExpression argIdExpr && _functionNames.Contains(argIdExpr.Name))
                    {
                        // ObjectInstance methods (for example HttpServer.use) can accept function-name strings
                        // in transpiled mode. Emitting a method-group here would be converted to null by
                        // RuntimeHelpers.ToRuntimeValue, so pass the function name explicitly.
                        _output.Append("\"");
                        _output.Append(argIdExpr.Name);
                        _output.Append("\"");
                    }
                    else
                    {
                        TranspileExpression(call.Arguments[i]);
                    }
                }
                _output.Append(" })");
                if (!_canAwait)
                    _output.Append(")");
                return;
            }
        }
        
        // Regular function call
        // Check if this is a user-defined function vs built-in vs function parameter
        if (call.Callee is IdentifierExpression idExpr)
        {
            var funcName = idExpr.Name;
            
            // If it's a built-in, handle it
            if (IsBuiltInFunction(funcName))
            {
                TranspileBuiltInFunction(funcName, call.Arguments);
                return;
            }
            
            // If it's a known prompt name, call it directly (prompts are synchronous)
            if (_promptNames.Contains(funcName))
            {
                _output.Append(EscapeIdentifier(funcName));
                _output.Append("(");
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspileExpression(call.Arguments[i]);
                }
                _output.Append(")");
                return;
            }
            
            // If it's a known function name, call it directly
            if (_functionNames.Contains(funcName))
            {
                _functionParameterTypes.TryGetValue(funcName, out var typedParameters);
                if (_transpileCallAsTask)
                {
                    _output.Append("RuntimeHelpers.WrapObjectTaskAsRuntimeValueTask(");
                    _output.Append(EscapeIdentifier(funcName));
                    _output.Append("(");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        var paramType = (typedParameters != null && i < typedParameters.Count)
                            ? typedParameters[i]
                            : TranspiledClrType.Object;
                        _output.Append(GetCoercionExpressionPrefix(paramType));
                        TranspileExpression(call.Arguments[i]);
                        _output.Append(GetCoercionExpressionSuffix(paramType));
                    }
                    _output.Append("))");
                }
                else
                {
                    if (_canAwait)
                    {
                        _output.Append("await ");
                        _output.Append(EscapeIdentifier(funcName));
                        _output.Append("(");
                    }
                    else
                    {
                        _output.Append("RuntimeHelpers.BlockOn(");
                        _output.Append(EscapeIdentifier(funcName));
                        _output.Append("(");
                    }
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        var paramType = (typedParameters != null && i < typedParameters.Count)
                            ? typedParameters[i]
                            : TranspiledClrType.Object;
                        _output.Append(GetCoercionExpressionPrefix(paramType));
                        TranspileExpression(call.Arguments[i]);
                        _output.Append(GetCoercionExpressionSuffix(paramType));
                    }
                    _output.Append(")");
                    if (!_canAwait)
                        _output.Append(")");
                }
                return;
            }
            
            // Otherwise, it's likely a function parameter - use helper
            // For single-parameter functions, use CallFunction helper
            if (call.Arguments.Count == 1)
            {
                if (_transpileCallAsTask)
                    _output.Append("RuntimeHelpers.WrapObjectTaskAsRuntimeValueTask(RuntimeHelpers.CallFunction(");
                else
                    _output.Append(_canAwait ? "await RuntimeHelpers.CallFunction(" : "RuntimeHelpers.BlockOn(RuntimeHelpers.CallFunction(");
                _output.Append(EscapeIdentifier(funcName));
                _output.Append(", ");
                TranspileExpression(call.Arguments[0]);
                _output.Append(_transpileCallAsTask ? "))" : ")");
                if (!_transpileCallAsTask && !_canAwait)
                    _output.Append(")");
                return;
            }
            
            // For multiple parameters, we'd need a different helper
            // For now, throw an error or use reflection
            if (_transpileCallAsTask)
                _output.Append("RuntimeHelpers.WrapObjectTaskAsRuntimeValueTask(RuntimeHelpers.CallFunction(");
            else
                _output.Append(_canAwait ? "await RuntimeHelpers.CallFunction(" : "RuntimeHelpers.BlockOn(RuntimeHelpers.CallFunction(");
            _output.Append(EscapeIdentifier(funcName));
            _output.Append(", ");
            // This fallback path is for non-standard dynamic call shapes.
            // Avoid indexing into argument lists here; pass null to prevent
            // transpiler crashes when argument metadata is inconsistent.
            _output.Append("null");
            _output.Append(_transpileCallAsTask ? "))" : ")");
            if (!_transpileCallAsTask && !_canAwait)
                _output.Append(")");
            return;
        }
        
        // Fallback for other expression types as callee (e.g., lambda expressions)
        // Other expression types (shouldn't happen often)
        if (_transpileCallAsTask)
            _output.Append("RuntimeHelpers.WrapObjectTaskAsRuntimeValueTask(");
        else
            _output.Append(_canAwait ? "await " : "RuntimeHelpers.BlockOn(");
        TranspileExpression(call.Callee);
        _output.Append("(");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            TranspileExpression(call.Arguments[i]);
        }
        _output.Append(_transpileCallAsTask ? "))" : ")");
        if (!_transpileCallAsTask && !_canAwait)
            _output.Append(")");
    }

    private bool IsBuiltInFunction(string name)
    {
        return BuiltInRegistry.IsTranspilerBuiltIn(name)
            || OptionalPackTranspilerBuiltIns.IsName(name);
    }

    private static bool IsStringExtensionMethod(string name)
    {
        return name == "length" ||
               name == "upper" ||
               name == "lower" ||
               name == "trim" ||
               name == "substring" ||
               name == "indexOf" ||
               name == "replace" ||
               name == "split" ||
               name == "startsWith" ||
               name == "endsWith" ||
               name == "padStart" ||
               name == "padEnd" ||
               name == "repeat";
    }

    private bool ExpressionContainsAwaitingCall(Expression expression)
    {
        return expression switch
        {
            AwaitExpression => true,
            AsyncExpression => true,
            ReceiveExpression => true,
            SpawnExpression spawn => spawn.Arguments.Any(ExpressionContainsAwaitingCall),
            NewExpression created => created.Arguments.Any(ExpressionContainsAwaitingCall),
            FunctionCallExpression call => FunctionCallContainsAwait(call),
            BinaryExpression binary => ExpressionContainsAwaitingCall(binary.Left) || ExpressionContainsAwaitingCall(binary.Right),
            UnaryExpression unary => ExpressionContainsAwaitingCall(unary.Right),
            PostfixExpression postfix => ExpressionContainsAwaitingCall(postfix.Left),
            TernaryExpression ternary => ExpressionContainsAwaitingCall(ternary.Condition) || ExpressionContainsAwaitingCall(ternary.ThenBranch) || ExpressionContainsAwaitingCall(ternary.ElseBranch),
            ArrayLiteralExpression array => array.Elements.Any(ExpressionContainsAwaitingCall),
            ObjectLiteralExpression obj => obj.Properties.Any(pair => ExpressionContainsAwaitingCall(pair.Key) || ExpressionContainsAwaitingCall(pair.Value)),
            DictionaryLiteralExpression dictionary => dictionary.Entries.Any(pair => ExpressionContainsAwaitingCall(pair.Key) || ExpressionContainsAwaitingCall(pair.Value)),
            ArrayAccessExpression access => ExpressionContainsAwaitingCall(access.Array) || ExpressionContainsAwaitingCall(access.Index),
            MemberAccessExpression member => ExpressionContainsAwaitingCall(member.Object),
            InterpolatedStringExpression interpolated => interpolated.Segments.Any(segment => segment.IsExpression && segment.Expression != null && ExpressionContainsAwaitingCall(segment.Expression)),
            GraphLiteralExpression graph => (graph.NodesExpression != null && ExpressionContainsAwaitingCall(graph.NodesExpression)) || (graph.EdgesExpression != null && ExpressionContainsAwaitingCall(graph.EdgesExpression)),
            MatchExpression match => true,
            LambdaExpression => false,
            _ => false
        };
    }

    private bool FunctionCallContainsAwait(FunctionCallExpression call)
    {
        if (call.Callee is IdentifierExpression identifier)
        {
            var descriptor = BuiltInRegistry.GetDescriptor(identifier.Name);
            if (descriptor?.IsAlwaysSynchronousForCodegen == true)
            {
                return call.Arguments.Any(ExpressionContainsAwaitingCall);
            }
        }

        if (call.Callee is MemberAccessExpression memberAccess &&
            IsStringExtensionMethod(memberAccess.Member))
        {
            return ExpressionContainsAwaitingCall(memberAccess.Object) ||
                   call.Arguments.Any(ExpressionContainsAwaitingCall);
        }

        return true;
    }

    private void TranspileStringExtensionCall(Expression objectExpr, string methodName, List<Expression> arguments)
    {
        switch (methodName)
        {
            case "length":
                _output.Append("(object)(RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").Length)");
                break;
            case "upper":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").ToUpper()");
                break;
            case "lower":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").ToLower()");
                break;
            case "trim":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").Trim()");
                break;
            case "substring":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").Substring((int)RuntimeHelpers.CoerceToInt(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("), (int)RuntimeHelpers.CoerceToInt(");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]);
                else
                    _output.Append("0");
                _output.Append("))");
                break;
            case "indexOf":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").IndexOf(RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "replace":
            case "split":
            case "startsWith":
            case "endsWith":
            case "padStart":
            case "padEnd":
            case "repeat":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(methodName);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { RuntimeHelpers.ToRuntimeValue(");
                TranspileExpression(objectExpr);
                _output.Append(")");
                for (int i = 0; i < arguments.Count; i++)
                {
                    _output.Append(", RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            default:
                throw new Exception($"Unknown string extension method: {methodName}");
        }
    }

    private void TranspileBuiltInFunction(string name, List<Expression> arguments)
    {
        if (name == "sort" && arguments.Count == 2 && arguments[1] is LambdaExpression)
        {
            _output.Append("RuntimeHelpers.UnwrapRuntimeValue(RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.ArraySortWithCompare(RuntimeHelpers.GetArray(");
            TranspileExpression(arguments[0]);
            _output.Append("), ");
            TranspileExpression(arguments[1]);
            _output.Append(")))");
            return;
        }

        if (OptionalPackTranspilerEmit.TryEmit(new OptionalPackEmitContext(_output, TranspileExpression), name, arguments))
        {
            return;
        }

        // Typed fast paths for hot numeric built-ins in transpiled code.
        if (TypedTranspileEnabled && name == "float" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            TranspileExpression(arguments[0]);
            return;
        }
        if (TypedTranspileEnabled && name == "abs" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Abs(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "sqrt" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Sqrt(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "floor" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Floor(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "ceil" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Ceiling(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "round" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Round(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "trunc" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Truncate(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "sign" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("(double)Math.Sign(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "exp" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Exp(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "log" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Log(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "log10" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Log10(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "log2" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Log(");
            TranspileExpression(arguments[0]);
            _output.Append(", 2.0)");
            return;
        }

        if (TypedTranspileEnabled && name == "sin" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Sin(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "cos" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Cos(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "tan" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Tan(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "asin" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Asin(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "acos" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Acos(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "atan" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Atan(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "min" && arguments.Count == 2 &&
            ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
            ResolveExpressionType(arguments[1]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Min(");
            TranspileExpression(arguments[0]);
            _output.Append(", ");
            TranspileExpression(arguments[1]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "max" && arguments.Count == 2 &&
            ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
            ResolveExpressionType(arguments[1]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Max(");
            TranspileExpression(arguments[0]);
            _output.Append(", ");
            TranspileExpression(arguments[1]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "pow" && arguments.Count > 1 &&
            ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
            ResolveExpressionType(arguments[1]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Pow(");
            TranspileExpression(arguments[0]);
            _output.Append(", ");
            TranspileExpression(arguments[1]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "atan2" && arguments.Count > 1 &&
            ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
            ResolveExpressionType(arguments[1]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Atan2(");
            TranspileExpression(arguments[0]);
            _output.Append(", ");
            TranspileExpression(arguments[1]);
            _output.Append(")");
            return;
        }

        switch (name)
        {
            case "print":
                if (ProfilingEnabled)
                {
                    // async lambda so arguments may use await (sync Func<> would be CS4034)
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync(\"print\", async () => { Console.WriteLine(RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")); })");
                }
                else
                {
                    _output.Append("Console.WriteLine(RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                break;
            case "input":
                if (ProfilingEnabled)
                {
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync<string>(\"input\", async () => RuntimeHelpers.ReadLineWithPrompt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    else
                        _output.Append("null");
                    _output.Append("))");
                }
                else
                {
                    _output.Append("RuntimeHelpers.ReadLineWithPrompt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    else
                        _output.Append("null");
                    _output.Append(")");
                }
                break;
            case "int":
                var intArgumentNeedsAsync = arguments.Count > 0 && ExpressionContainsAwaitingCall(arguments[0]);
                if (ProfilingEnabled && !intArgumentNeedsAsync)
                {
                    _output.Append("MaldaProfiler.ProfileBuiltIn<object>(\"int\", () => RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else if (ProfilingEnabled)
                {
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync<object>(\"int\", async () => RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else
                {
                    _output.Append("RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")");
                }
                break;
            case "float":
                var floatArgumentNeedsAsync = arguments.Count > 0 && ExpressionContainsAwaitingCall(arguments[0]);
                if (ProfilingEnabled && !floatArgumentNeedsAsync)
                {
                    _output.Append("MaldaProfiler.ProfileBuiltIn<object>(\"float\", () => RuntimeHelpers.CoerceToFloat(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else if (ProfilingEnabled)
                {
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync<object>(\"float\", async () => RuntimeHelpers.CoerceToFloat(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else
                {
                    _output.Append("RuntimeHelpers.CoerceToFloat(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")");
                }
                break;
            case "string":
                var stringArgumentNeedsAsync = arguments.Count > 0 && ExpressionContainsAwaitingCall(arguments[0]);
                if (ProfilingEnabled && !stringArgumentNeedsAsync)
                {
                    _output.Append("MaldaProfiler.ProfileBuiltIn<object>(\"string\", () => RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else if (ProfilingEnabled)
                {
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync<object>(\"string\", async () => RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else
                {
                    _output.Append("RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")");
                }
                break;
            case "formatNumber":
                // Call BuiltInFunctions.CallBuiltIn for formatNumber
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("formatNumber");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "getWorkflowStatus":
            case "getWorkflow":
            case "getWorkflowSteps":
            case "getWorkflowEvents":
            case "getWorkflowMetrics":
            case "listWorkflows":
            case "listWorkflowDeadLetters":
            case "requeueDeadLetter":
            case "cancelWorkflow":
            case "resumeWorkflow":
            case "retryWorkflow":
            case "approveWorkflowStep":
            case "signalWorkflow":
            case "runProperty":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "startWorkflow":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltInAsync(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null).GetAwaiter().GetResult())");
                break;
            case "runWorkflowInstance":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltInAsync(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null).GetAwaiter().GetResult())");
                break;
            case "getEnv":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("getEnv");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "getCommandLineArgs":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("getCommandLineArgs");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "hasEnv":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("hasEnv");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "getHostPlatform":
            case "reportRalphStatus":
            case "enableAgentVerboseLogging":
            case "setAgentVerbosePhase":
            case "setAgentStatusBanner":
            case "getFileName":
            case "getDirectoryName":
            case "gitStatus":
            case "gitAdd":
            case "gitCommit":
            case "gitDiff":
            case "gitLog":
            case "gitBranch":
            case "gitCheckout":
            case "gitPush":
            case "gitPull":
            case "loadAssembly":
            case "getDotNetType":
            case "dotnetNew":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "abs":
                _output.Append("Math.Abs((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "sum":
            case "average":
            case "max":
            case "min":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "pow":
                _output.Append("Math.Pow((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("), (double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]);
                _output.Append("))");
                break;
            case "sqrt":
                _output.Append("Math.Sqrt((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            // Extended math: rounding and sign
            case "floor":
                _output.Append("Math.Floor((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "ceil":
                _output.Append("Math.Ceiling((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "round":
                _output.Append("Math.Round((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "trunc":
                _output.Append("Math.Truncate((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "sign":
                _output.Append("(double)Math.Sign((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            // Extended math: exponential and logarithm
            case "exp":
                _output.Append("Math.Exp((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "log":
                _output.Append("Math.Log((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "log10":
                _output.Append("Math.Log10((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "log2":
                _output.Append("Math.Log((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("), 2.0)");
                break;
            // Extended math: trigonometry
            case "sin":
                _output.Append("Math.Sin((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "cos":
                _output.Append("Math.Cos((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "tan":
                _output.Append("Math.Tan((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "asin":
                _output.Append("Math.Asin((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "acos":
                _output.Append("Math.Acos((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "atan":
                _output.Append("Math.Atan((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "atan2":
                _output.Append("Math.Atan2((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]); // y
                _output.Append("), (double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]); // x
                _output.Append("))");
                break;
            // Extended math: utility
            case "hypot":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("hypot");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "clamp":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("clamp");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "degToRad":
                _output.Append("((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(") * Math.PI / 180.0)");
                break;
            case "radToDeg":
                _output.Append("((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(") * 180.0 / Math.PI)");
                break;
            case "reply":
                _output.Append("ActorsRuntime.Reply(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                else
                    _output.Append("null");
                _output.Append(")");
                break;
            case "length":
                // length() on arrays must use element count, not stringified array length
                _output.Append("(object)RuntimeHelpers.BuiltInLength(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                else
                    _output.Append("null");
                _output.Append(")");
                break;
            case "upper":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").ToUpper()");
                break;
            case "lower":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").ToLower()");
                break;
            case "trim":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").Trim()");
                break;
            case "substring":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").Substring((int)RuntimeHelpers.CoerceToInt(");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]);
                _output.Append("), (int)RuntimeHelpers.CoerceToInt(");
                if (arguments.Count > 2)
                    TranspileExpression(arguments[2]);
                _output.Append("))");
                break;
            case "indexOf":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").IndexOf(RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]);
                _output.Append("))");
                break;
            case "sleep":
                // In actor handlers (void methods), use .Wait() instead of await
                if (_isInActorHandler)
                {
                    _output.Append("Task.Delay((int)RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")).Wait()");
                }
                else if (_transpileCallAsTask)
                {
                    _output.Append("System.Threading.Tasks.Task.Delay((int)RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")).ContinueWith(_ => MaldaLang.Interpreter.RuntimeValue.Null())");
                }
                else
                {
                    _output.Append("await Task.Delay((int)RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                break;
            case "typeOf":
                _output.Append("RuntimeHelpers.TypeOfValue(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                else
                    _output.Append("null");
                _output.Append(")");
                break;
            case "isTag":
                _output.Append("RuntimeHelpers.IsTag(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                else
                    _output.Append("null");
                _output.Append(", ");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]);
                else
                    _output.Append("null");
                _output.Append(")");
                break;
            case "createNativeCallback":
                if (arguments.Count != 1)
                    throw new InvalidOperationException("createNativeCallback() expects exactly 1 argument.");
                _output.Append("new MaldaLang.BuiltIns.DotNetObjectInstance(new MaldaLang.BuiltIns.NativeCallbackBridge(");
                TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "runCommand":
            case "loadNativeModule":
            case "createRunCommandTool":
            case "createWebSearchTool":
            case "createReadFileTool":
            case "createWriteFileTool":
            case "createReplaceInFileTool":
            case "createListDirectoryTool":
            case "createAskUserTool":
            case "createGrepTool":
            case "createGlobTool":
            case "createInsertAtLineTool":
            case "createEditFileTool":
            case "createGitStatusTool":
            case "createGitAddTool":
            case "createGitCommitTool":
            case "createGitLogTool":
            case "createGitDiffTool":
            case "createGitBranchTool":
            case "createGitCheckoutTool":
            case "createGitPushTool":
            case "createGitPullTool":
            case "uiGenerate":
            case "runMALDA":
            case "createRunMALDATool":
            case "compileMALDA":
            case "createCompileMALDATool":
            case "getSymbols":
            case "createGetSymbolsTool":
            case "getParseErrors":
            case "createGetParseErrorsTool":
            case "createMcpAgentScript":
            case "createCreateMcpAgentScriptTool":
            case "createSubmitPlanTool":
            case "executePlan":
            case "decomposeTask":
            case "extractHTML":
            case "renderTemplate":
            case "componentFragment":
            case "componentLiveEmit":
            case "componentStateGet":
            case "componentStateSet":
            case "componentStateObject":
            case "componentStateClear":
            case "componentStateConfigure":
            case "uiRow":
            case "uiColumn":
            case "uiStack":
            case "uiSpacer":
            case "uiPanel":
            case "uiText":
            case "uiHeading":
            case "uiImage":
            case "uiIcon":
            case "uiButton":
            case "uiTextField":
            case "uiCheckbox":
            case "uiSelect":
            case "uiSlider":
            case "uiDatePicker":
            case "uiList":
            case "uiTable":
            case "uiAlert":
            case "uiProgress":
            case "uiModal":
            case "uiForm":
            case "uiField":
            case "uiTextArea":
            case "uiRadioGroup":
            case "uiSwitch":
            case "uiTabs":
            case "uiAccordion":
            case "uiBreadcrumbs":
            case "uiDrawer":
            case "uiDataGrid":
            case "uiTreeView":
            case "uiPaginator":
            case "uiEmptyState":
            case "uiBadge":
            case "uiToast":
            case "uiSkeleton":
            case "uiSpinner":
            case "uiErrorBoundary":
            case "uiSlot":
            case "uiWithSlot":
            case "uiWhen":
            case "uiChoose":
            case "uiEach":
            case "uiTemplate":
            case "uiPartial":
            case "uiLayout":
            case "uiRenderList":
            case "uiCrudModel":
            case "uiCrudControls":
            case "uiCrudSchema":
            case "uiMount":
            case "uiMountEnvelope":
            case "uiRender":
            case "uiDispatchEvent":
            case "uiPullEvent":
            case "uiState":
            case "uiSetState":
            case "uiInvalidate":
            case "uiOnInit":
            case "uiOnPreRender":
            case "uiOnLoad":
            case "uiOnDispose":
            case "uiOnMount":
            case "uiOnUpdate":
            case "uiOnUnmount":
            case "uiOnError":
            case "uiConfigure":
            case "uiSnapshot":
            case "uiResync":
            case "uiSessionId":
            case "uiRedirectWithSession":
            case "redirect":
            case "RedirectTo":
            case "httpGet":
            case "httpPost":
            case "httpPut":
            case "httpDelete":
            case "httpPatch":
            case "httpBearerToken":
            case "httpCookieToken":
            case "httpAuthToken":
            case "webSearch":
            // Date/Time functions
            case "now":
            case "formatDate":
            case "parseDate":
            case "addDays":
            case "addHours":
            // Random functions
            case "random":
            case "randomInt":
            case "randomFloat":
            // LLM-oriented math helpers
            case "rsqrt":
            case "randn":
            case "argmax":
            case "argmin":
            case "logSumExp":
            case "softmax":
            case "crossEntropyFromLogits":
            case "randomChoiceWeighted":
            case "seed":
            // Type checking functions
            case "isNumber":
            case "isString":
            case "isArray":
            case "isObject":
            // Array utilities
            case "join":
            case "split":
            case "normalizeText":
            case "tokenize":
            case "tokenOverlap":
            case "similarity":
            case "extractNumbers":
            case "replace":
            case "regexMatch":
            case "regexReplace":
            case "regexFind":
            case "reverse":
            case "sort":
            case "includes":
            // Encoding/Decoding
            case "base64Encode":
            case "base64Decode":
            case "urlEncode":
            case "urlDecode":
            // Hash functions
            case "md5":
            case "sha256":
            case "hashPassword":
            case "verifyPassword":
            case "createJwt":
            case "verifyJwt":
            case "generateCsrfToken":
            case "verifyCsrfToken":
            case "createSecureCookie":
            case "readSecureCookie":
            // Path manipulation
            case "pathJoin":
            case "pathNormalize":
            case "pathExists":
            case "pathGetExtension":
            // Range generation
            case "range":
            // Error handling
            case "exit":
            case "error":
            case "assert":
            // Additional string utilities
            case "startsWith":
            case "endsWith":
            case "padStart":
            case "padEnd":
            case "repeat":
            case "toIntOr":
            case "toIntOrNull":
            case "toCsv":
            case "getMaldaHome":
            case "getProgramDirectory":
            case "getMaldaConfig":
            case "getAssistantMemory":
            case "getSkillNames":
            case "loadSkill":
            case "loadSkillsFromDir":
            case "parseJSON":
            case "toJSON":
            case "readFile":
            case "readTextFileLines":
            case "writeFile":
            case "writeFileBase64":
            case "readFileBase64":
            case "hasFile":
            case "deleteFile":
            case "hasDirectory":
            case "listDirectory":
            case "hasEmbeddedFolder":
            case "embeddedFolderRoot":
            case "glob":
            case "grep":
            case "replaceInFile":
            case "editFile":
            case "insertAtLine":
            case "ensureDir":
                // These built-ins need to call BuiltInFunctions.CallBuiltIn
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "all":
                // all(...) returns a Task RuntimeValue composed from its arguments.
                // In normal expressions, we want the RuntimeValue itself.
                // When transpiling as a Task (inside async expr), return the underlying Task<RuntimeValue>.
                _output.Append("MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"all\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null)");
                if (_transpileCallAsTask)
                {
                    _output.Append(".AsTask()");
                }
                break;
            default:
                if (BuiltInRegistry.IsTranspilerBuiltIn(name))
                {
                    var builtInDescriptor = BuiltInRegistry.GetDescriptor(name);
                    var useAsyncDispatch = builtInDescriptor != null && !builtInDescriptor.IsAlwaysSynchronousForCodegen;
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.");
                    _output.Append(useAsyncDispatch ? "CallBuiltInAsync(\"" : "CallBuiltIn(\"");
                    _output.Append(name);
                    _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    for (int i = 0; i < arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        _output.Append("RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(arguments[i]);
                        _output.Append(")");
                    }
                    _output.Append(" }, null)");
                    if (useAsyncDispatch)
                    {
                        _output.Append(".GetAwaiter().GetResult()");
                    }
                    _output.Append(")");
                    break;
                }

                // Fallback for non-built-in identifiers (should not happen for vetted built-ins).
                _output.Append(name);
                _output.Append("(");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspileExpression(arguments[i]);
                }
                _output.Append(")");
                break;
        }
    }

    private void TranspileMemberAccess(MemberAccessExpression member)
    {
        // Special handling for Math.* members (constants)
        if (member.Object is IdentifierExpression mathIdExpr && mathIdExpr.Name == "Math")
        {
            var mathMember = member.Member;
            if (mathMember == "PI")
            {
                _output.Append("System.Math.PI");
                return;
            }
            if (mathMember == "E")
            {
                _output.Append("System.Math.E");
                return;
            }
            if (mathMember == "TAU")
            {
                _output.Append("(2 * System.Math.PI)");
                return;
            }
            if (mathMember == "INF")
            {
                _output.Append("double.PositiveInfinity");
                return;
            }
            if (mathMember == "NaN")
            {
                _output.Append("double.NaN");
                return;
            }
            // Unknown Math member - fall through to generic handling
        }

        // Check if this is an array method or property
        var memberName = member.Member;
        if (memberName == "length")
        {
            if (ResolveExpressionType(member.Object) == TranspiledClrType.DoubleArray)
            {
                _output.Append("RuntimeHelpers.CoerceToDoubleList(");
                TranspileExpression(member.Object);
                _output.Append(").Count");
            }
            else
            {
                _output.Append("RuntimeHelpers.GetArray(");
                TranspileExpression(member.Object);
                _output.Append(").Count");
            }
            return;
        }
        else if (memberName == "append" || memberName == "pop" || memberName == "shift" || memberName == "concat")
        {
            // This will be handled when the method is called
            // Note: These are built-in array methods, but we don't escape them because
            // they're accessed through ObjectInstance which uses string-based reflection
            TranspileExpression(member.Object);
            _output.Append(".");
            _output.Append(member.Member);
            return;
        }

        var memberHelper = member.IsNullConditional ? "RuntimeHelpers.GetObjectMemberNullSafe" : "RuntimeHelpers.GetObjectMember";
        _output.Append(memberHelper);
        _output.Append("(");
        TranspileExpression(member.Object);
        _output.Append(", \"");
        _output.Append(memberName);
        _output.Append("\")");
    }

    private void TranspileNew(NewExpression newExpr)
    {
        var className = newExpr.ClassName;
        var mappedClassName = MapBuiltInClassName(className);
        
        // Handle special cases that require parameterless constructors + initialization
        if (className == "LlamaCppClient" && newExpr.Arguments.Count == 0)
        {
            _output.Append("(new MaldaLang.BuiltIns.LlamaCppClientInstance())");
            return;
        }

        if (className == "LlamaCppClient" && newExpr.Arguments.Count == 1)
        {
            // LlamaCppClient(modelPath) -> new LlamaCppClientInstance() { ModelPath = modelPath }
            _output.Append("(new MaldaLang.BuiltIns.LlamaCppClientInstance() { ModelPath = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(") })");
            return;
        }
        
        if (className == "LlamaEmbedder" && newExpr.Arguments.Count == 1)
        {
            // LlamaEmbedder(modelPath) -> new LlamaEmbedderInstance() { ModelPath = modelPath }
            _output.Append("(new MaldaLang.BuiltIns.LlamaEmbedderInstance() { ModelPath = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(") })");
            return;
        }
        
        if (className == "LLMClient" && newExpr.Arguments.Count == 3)
        {
            // LLMClient(apiUrl, apiKey, model) — instance uses parameterless ctor + property init (interpreter parity).
            _output.Append("(new System.Func<MaldaLang.BuiltIns.LLMClientInstance>(() => { ");
            _output.Append("var __llm = new MaldaLang.BuiltIns.LLMClientInstance(); ");
            _output.Append("__llm.ApiUrl = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append("); ");
            _output.Append("__llm.ApiKey = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append("); ");
            _output.Append("__llm.Model = RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append("); ");
            _output.Append("return __llm; })())");
            return;
        }

        if (className == "Conversation" && newExpr.Arguments.Count == 2)
        {
            // Conversation(client, systemPrompt) -> new ConversationInstance() then Initialize()
            _output.Append("(new System.Func<MaldaLang.BuiltIns.ConversationInstance>(() => { ");
            _output.Append("var __conv = new MaldaLang.BuiltIns.ConversationInstance(); ");
            _output.Append("object __clientObj = ");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append("; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance? __bridgeClient = null; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance __bridge) __bridgeClient = __bridge; ");
            _output.Append("__conv.Initialize(__llmClient, __llamaClient, __bridgeClient, RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append("), null); ");
            _output.Append("return __conv; })())");
            return;
        }
        
        if (className == "Agent" && newExpr.Arguments.Count == 4)
        {
            // Agent(name, role, instructions, client) -> new AgentInstance() then Initialize()
            _output.Append("(new System.Func<MaldaLang.BuiltIns.AgentInstance>(() => { ");
            _output.Append("var __agent = new MaldaLang.BuiltIns.AgentInstance(); ");
            _output.Append("object __clientObj = ");
            TranspileExpression(newExpr.Arguments[3]);
            _output.Append("; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance? __bridgeClient = null; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance __bridge) __bridgeClient = __bridge; ");
            _output.Append("__agent.Initialize(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(", ");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append(", ");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append(", __llmClient, __llamaClient, __bridgeClient, null); ");
            _output.Append("__agent.SetInterpreter(MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()); ");
            _output.Append("return __agent; })())");
            return;
        }

        if (className == "DevAgent" && newExpr.Arguments.Count == 7)
        {
            // DevAgent(name, role, instructions, client, workingDirectory, includeSymbols, prdAuthorOnly)
            _output.Append("(new System.Func<MaldaLang.BuiltIns.DevAgentInstance>(() => { ");
            _output.Append("object __clientObj = ");
            TranspileExpression(newExpr.Arguments[3]);
            _output.Append("; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
            _output.Append("var __devAgent = new MaldaLang.BuiltIns.DevAgentInstance(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(", ");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append(", RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append("), __llmClient, __llamaClient, null, RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[4]);
            _output.Append("), RuntimeHelpers.CoerceToBool(");
            TranspileExpression(newExpr.Arguments[5]);
            _output.Append("), null, false, RuntimeHelpers.CoerceToBool(");
            TranspileExpression(newExpr.Arguments[6]);
            _output.Append(")); __devAgent.SetInterpreter(MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()); return __devAgent; })())");
            return;
        }

        if (className == "DevAgent" && newExpr.Arguments.Count == 6)
        {
            // DevAgent(name, role, instructions, client, workingDirectory, includeSymbols)
            _output.Append("(new System.Func<MaldaLang.BuiltIns.DevAgentInstance>(() => { ");
            _output.Append("object __clientObj = ");
            TranspileExpression(newExpr.Arguments[3]);
            _output.Append("; ");
            _output.Append("MaldaLang.BuiltIns.LLMClientInstance? __llmClient = null; ");
            _output.Append("MaldaLang.BuiltIns.LlamaCppClientInstance? __llamaClient = null; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LLMClientInstance __llm) __llmClient = __llm; ");
            _output.Append("if (__clientObj is MaldaLang.BuiltIns.LlamaCppClientInstance __llama) __llamaClient = __llama; ");
            _output.Append("var __devAgent = new MaldaLang.BuiltIns.DevAgentInstance(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(", ");
            TranspileExpression(newExpr.Arguments[1]);
            _output.Append(", RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[2]);
            _output.Append("), __llmClient, __llamaClient, null, RuntimeHelpers.CoerceToString(");
            TranspileExpression(newExpr.Arguments[4]);
            _output.Append("), RuntimeHelpers.CoerceToBool(");
            TranspileExpression(newExpr.Arguments[5]);
            _output.Append(")); __devAgent.SetInterpreter(MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()); return __devAgent; })())");
            return;
        }

        // HttpServer(port, webDirectory?, pathBase?) needs explicit coercion because top-level vars are object-typed.
        if (className == "HttpServer" && newExpr.Arguments.Count >= 1)
        {
            _output.Append("new MaldaLang.BuiltIns.HttpServerInstance(RuntimeHelpers.CoerceToInt(");
            TranspileExpression(newExpr.Arguments[0]);
            _output.Append(")");

            if (newExpr.Arguments.Count >= 2)
            {
                _output.Append(", RuntimeHelpers.CoerceToString(");
                TranspileExpression(newExpr.Arguments[1]);
                _output.Append(")");
            }
            else
            {
                _output.Append(", (string)null");
            }

            // Transpiled code has no interpreter; pathBase is 3rd MALDA arg.
            _output.Append(", (MaldaLang.Interpreter.Interpreter)null");
            if (newExpr.Arguments.Count >= 3)
            {
                _output.Append(", ");
                TranspileExpression(newExpr.Arguments[2]);
            }
            else
            {
                _output.Append(", (object)null");
            }
            _output.Append(")");
            return;
        }
        
        // Default: use constructor with arguments
        _output.Append("new ");
        _output.Append(mappedClassName);
        
        _output.Append("(");
        for (int i = 0; i < newExpr.Arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            TranspileExpression(newExpr.Arguments[i]);
        }
        _output.Append(")");
    }

    private string MapBuiltInClassName(string className)
    {
        return className switch
        {
            "LLMClient" => "MaldaLang.BuiltIns.LLMClientInstance",
            "OpenRouterClient" => "MaldaLang.BuiltIns.OpenRouterClientInstance",
            "LlamaCppClient" => "MaldaLang.BuiltIns.LlamaCppClientInstance",
            "LlamaEmbedder" => "MaldaLang.BuiltIns.LlamaEmbedderInstance",
            "Conversation" => "MaldaLang.BuiltIns.ConversationInstance",
            "Tool" => "MaldaLang.BuiltIns.ToolInstance",
            "Agent" => "MaldaLang.BuiltIns.AgentInstance",
            "CodingAgent" => "MaldaLang.BuiltIns.CodingAgentInstance",
            "GitAgent" => "MaldaLang.BuiltIns.GitAgentInstance",
            "DevAgent" => "MaldaLang.BuiltIns.DevAgentInstance",
            "GraphMemory" => "MaldaLang.BuiltIns.GraphMemoryInstance",
            "MALDACodingAgent" => "MaldaLang.BuiltIns.MALDACodingAgentInstance",
            "RestServer" => "MaldaLang.BuiltIns.RestServerInstance",
            "RestClient" => "MaldaLang.BuiltIns.RestClientInstance",
            "HttpServer" => "MaldaLang.BuiltIns.HttpServerInstance",
            "HTMLCache" => "MaldaLang.BuiltIns.HTMLCacheInstance",
            "MCPServer" => "MaldaLang.BuiltIns.MCPServerInstance",
            "MCPClient" => "MaldaLang.BuiltIns.MCPClientInstance",
            "ACPClient" => "MaldaLang.BuiltIns.ACP.ACPClientInstance",
            "ACPServer" => "MaldaLang.BuiltIns.ACP.ACPServerInstance",
            "ACPAgentTool" => "MaldaLang.BuiltIns.ACP.ACPAgentToolInstance",
            "LLMClientBridge" => "MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance",
            "LLMServer" => "MaldaLang.BuiltIns.LLMServerInstance",
            "SqlServerClient" => "MaldaLang.BuiltIns.SqlServerClientInstance",
            "PostgresClient" => "MaldaLang.BuiltIns.PostgresClientInstance",
            "SqliteClient" => "MaldaLang.BuiltIns.SqliteClientInstance",
            "SerialConnection" => "MaldaLang.BuiltIns.SerialConnectionInstance",
            "ArduinoConnection" => "MaldaLang.BuiltIns.ArduinoConnectionInstance",
            _ => className // User-defined classes use as-is
        };
    }

    private void TranspileArrayLiteral(ArrayLiteralExpression array)
    {
        _output.Append("new List<object> { ");
        for (int i = 0; i < array.Elements.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            TranspileExpression(array.Elements[i]);
        }
        _output.Append(" }");
    }

    private void TranspileDictionaryLiteral(DictionaryLiteralExpression dict)
    {
        _output.Append("new System.Collections.Generic.Dictionary<string, object?> { ");
        for (int i = 0; i < dict.Entries.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var (key, value) = dict.Entries[i];
            _output.Append("{ ");
            // Keys must be coerced to strings at runtime to match interpreter semantics
            _output.Append("RuntimeHelpers.CoerceToString(");
            TranspileExpression(key);
            _output.Append("), ");
            if (ShouldEmitTranspiledFunctionDelegate(value))
            {
                _output.Append("(System.Func<object, System.Threading.Tasks.Task<object>>)");
                TranspileExpression(value);
            }
            else
            {
                TranspileExpression(value);
            }
            _output.Append(" }");
        }
        _output.Append(" }");
    }

    private void TranspileGraphLiteral(GraphLiteralExpression graph)
    {
        // Create graph instance and initialize it using a lambda to handle initialization
        // This matches the interpreter's EvaluateGraphLiteralAsync behavior
        _output.Append("(new System.Func<object>(() => { ");
        _output.Append($"var __graph = new MaldaLang.Interpreter.GraphInstance({(graph.IsDirected ? "true" : "false")}); ");
        
        // Process nodes if provided
        if (graph.NodesExpression != null)
        {
            _output.Append("var __nodesVal = RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(graph.NodesExpression);
            _output.Append("); ");
            _output.Append("if (__nodesVal.Type == MaldaLang.Interpreter.ValueType.Array) { ");
            _output.Append("var __nodes = __nodesVal.AsArray(); ");
            _output.Append("foreach (var __node in __nodes) { ");
            _output.Append("var __nodeId = __node.Type == MaldaLang.Interpreter.ValueType.String ? __node.AsString() : __node.ToString(); ");
            _output.Append("__graph.CallMethod(\"addNode\", new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(__nodeId) }, null); ");
            _output.Append("} } ");
        }
        
        // Process edges if provided
        if (graph.EdgesExpression != null)
        {
            _output.Append("var __edgesVal = RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(graph.EdgesExpression);
            _output.Append("); ");
            _output.Append("if (__edgesVal.Type == MaldaLang.Interpreter.ValueType.Array) { ");
            _output.Append("var __edges = __edgesVal.AsArray(); ");
            _output.Append("foreach (var __edgeVal in __edges) { ");
            _output.Append("if (__edgeVal.Type == MaldaLang.Interpreter.ValueType.Object) { ");
            _output.Append("var __edgeObj = __edgeVal.AsObject(); ");
            _output.Append("var __fromVal = __edgeObj.Get(\"from\", null); ");
            _output.Append("var __toVal = __edgeObj.Get(\"to\", null); ");
            _output.Append("var __from = __fromVal.Type == MaldaLang.Interpreter.ValueType.String ? __fromVal.AsString() : \"\"; ");
            _output.Append("var __to = __toVal.Type == MaldaLang.Interpreter.ValueType.String ? __toVal.AsString() : \"\"; ");
            _output.Append("var __weightVal = MaldaLang.Interpreter.RuntimeValue.Null(); ");
            _output.Append("try { var __w = __edgeObj.Get(\"weight\", null); if (__w.Type != MaldaLang.Interpreter.ValueType.Null) __weightVal = __w; } catch { } ");
            _output.Append("var __propsVal = MaldaLang.Interpreter.RuntimeValue.Null(); ");
            _output.Append("try { var __p = __edgeObj.Get(\"properties\", null); if (__p.Type == MaldaLang.Interpreter.ValueType.Object && __p.Value is MaldaLang.Interpreter.DictionaryInstance) __propsVal = __p; } catch { } ");
            _output.Append("var __args = new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(__from), MaldaLang.Interpreter.RuntimeValue.String(__to), __weightVal, __propsVal }; ");
            _output.Append("__graph.CallMethod(\"addEdge\", __args, null); ");
            _output.Append("} } } ");
        }
        
        _output.Append("return __graph; }))()");
    }

    private void TranspileObjectLiteral(ObjectLiteralExpression obj)
    {
        // Use a native dictionary so values can preserve arbitrary CLR objects.
        // Emit a direct initializer (instead of a non-async lambda wrapper) so await
        // expressions inside object values can compile in async contexts.
        _output.Append("new System.Collections.Generic.Dictionary<string, object?> { ");
        for (int i = 0; i < obj.Properties.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var (key, value) = obj.Properties[i];
            _output.Append("{ ");
            if (key is LiteralExpression keyLiteral && keyLiteral.Value is string keyStr)
            {
                _output.Append($"\"{keyStr}\"");
            }
            else
            {
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(key);
                _output.Append(")");
            }
            _output.Append(", ");
            if (ShouldEmitTranspiledFunctionDelegate(value))
            {
                _output.Append("(System.Func<object, System.Threading.Tasks.Task<object>>)");
                TranspileExpression(value);
            }
            else
            {
                TranspileExpression(value);
            }
            _output.Append(" }");
        }
        _output.Append(" }");
    }

    private bool ShouldEmitTranspiledFunctionDelegate(Expression value)
    {
        if (value is IdentifierExpression id && _functionNames.Contains(id.Name))
            return true;
        return false;
    }

    private void TranspileArrayAccess(ArrayAccessExpression arrayAccess)
    {
        if (ResolveExpressionType(arrayAccess.Array) == TranspiledClrType.DoubleArray)
        {
            _output.Append("RuntimeHelpers.GetIndexedDouble(");
            _output.Append("RuntimeHelpers.CoerceToDoubleList(");
            TranspileExpression(arrayAccess.Array);
            _output.Append("), ");
            TranspileExpression(arrayAccess.Index);
            _output.Append(")");
            return;
        }
        var indexHelper = arrayAccess.IsNullConditional ? "RuntimeHelpers.GetIndexedNullSafe" : "RuntimeHelpers.GetIndexed";
        _output.Append(indexHelper);
        _output.Append("(");
        TranspileExpression(arrayAccess.Array);
        _output.Append(", ");
        TranspileExpression(arrayAccess.Index);
        _output.Append(")");
    }

    private void TranspileTernary(TernaryExpression ternary)
    {
        _output.Append("(RuntimeHelpers.CoerceToBool(");
        TranspileExpression(ternary.Condition);
        _output.Append(") ? ");
        TranspileExpression(ternary.ThenBranch);
        _output.Append(" : ");
        TranspileExpression(ternary.ElseBranch);
        _output.Append(")");
    }

    private void TranspileInterpolatedString(InterpolatedStringExpression interpolated)
    {
        _output.Append("$\"");
        foreach (var segment in interpolated.Segments)
        {
            if (segment.IsExpression)
            {
                _output.Append("{");
                TranspileExpression(segment.Expression!);
                _output.Append("}");
            }
            else
            {
                // Match TranspileLiteral: lexer-decoded escapes (\n, \t, …) must be re-escaped
                // into the C# source. Forgetting this turns $"a\nb" into a broken multi-line
                // C# interpolated string while the interpreter still runs fine.
                var text = segment.Text ?? "";
                _output.Append(text
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t")
                    .Replace("\r", "\\r")
                    .Replace("{", "{{")
                    .Replace("}", "}}"));
            }
        }
        _output.Append("\"");
    }

    private void TranspileLambda(LambdaExpression lambda)
    {
        // We define the delegate as async. C# will handle the Task wrapping automatically.
        // Use the correct Func type for 1 vs 2 parameters so two-parameter lambdas (e.g. sort compare) type-check.
        if (lambda.Parameters.Count == 2)
            _output.Append("new System.Func<object, object, System.Threading.Tasks.Task<object>>(async (");
        else
            _output.Append("new System.Func<object, System.Threading.Tasks.Task<object>>(async (");

        // Parameters
        for (int i = 0; i < lambda.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("object ");
            _output.Append(EscapeIdentifier(lambda.Parameters[i]));
        }

        _output.Append(") => ");

        // Lambda bodies are emitted as async delegates, so function calls inside them
        // should transpile in await-capable mode instead of BlockOn.
        var previousCanAwaitInLambda = _canAwait;
        _canAwait = true;

        // Body handling
        if (lambda.BlockBody != null)
        {
            _output.Append("{");
            _output.AppendLine();
            _indentLevel++;

            for (int i = 0; i < lambda.BlockBody.Statements.Count; i++)
            {
                var stmt = lambda.BlockBody.Statements[i];
                WriteIndent();

                if (stmt is ReturnStatement returnStmt)
                {
                    _output.Append("return ");
                    if (returnStmt.Value != null)
                    {
                        TranspileExpression(returnStmt.Value);
                    }
                    else
                    {
                        _output.Append("null");
                    }
                    _output.AppendLine(";");
                }
                else
                {
                    var isLast = (i == lambda.BlockBody.Statements.Count - 1);
                    if (isLast && stmt is ExpressionStatement exprStmt)
                    {
                        // Last expression wins: implicit return for lambda block bodies
                        _output.Append("return RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(exprStmt.Expression);
                        _output.AppendLine(");");
                    }
                    else
                    {
                        TranspileStatement(stmt);
                        // Ensure newlines for non-block statements
                        if (!(stmt is BlockStatement || stmt is IfStatement || stmt is WhileStatement || stmt is ForStatement))
                        {
                            _output.AppendLine();
                        }
                    }
                }
            }

            _indentLevel--;
            WriteIndent();
            _output.Append("})");
        }
        else if (lambda.ExpressionBody != null)
        {
            // For expression-bodied lambdas (no braces), C# implicitly returns the value.
            TranspileExpression(lambda.ExpressionBody);
            _output.Append(")");
        }

        _canAwait = previousCanAwaitInLambda;
    }

    private string GetOperatorString(TokenType op)
    {
        return op switch
        {
            TokenType.Plus => "+",
            TokenType.Minus => "-",
            TokenType.Multiply => "*",
            TokenType.Divide => "/",
            TokenType.Modulo => "%",
            TokenType.Equal => "==",
            TokenType.NotEqual => "!=",
            TokenType.LessThan => "<",
            TokenType.GreaterThan => ">",
            TokenType.LessThanOrEqual => "<=",
            TokenType.GreaterThanOrEqual => ">=",
            TokenType.And => "&&",
            TokenType.Or => "||",
            TokenType.PlusAssign => "+=",
            TokenType.MinusAssign => "-=",
            TokenType.MultiplyAssign => "*=",
            TokenType.DivideAssign => "/=",
            _ => throw new NotSupportedException($"Operator {op} not supported")
        };
    }

    private string GetUnaryOperatorString(TokenType op)
    {
        return op switch
        {
            TokenType.Minus => "-",
            TokenType.Not => "!",
            TokenType.Increment => "++",
            TokenType.Decrement => "--",
            _ => throw new NotSupportedException($"Unary operator {op} not supported")
        };
    }

    private string EscapeIdentifier(string name)
    {
        // C# reserved keywords and base types that need to be escaped
        var csharpKeywords = new HashSet<string>
        {
            // C# keywords
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while",
            // Contextual keywords
            "add", "alias", "and", "ascending", "async", "await", "by", "descending", "dynamic",
            "equals", "from", "get", "global", "group", "init", "into", "join", "let", "nameof",
            "not", "notnull", "on", "or", "orderby", "partial", "record", "remove", "select",
            "set", "unmanaged", "value", "var", "when", "where", "with", "yield"
        };

        if (csharpKeywords.Contains(name))
        {
            return "@" + name;
        }

        return name;
    }
    
    private void TranspileActor(ActorDeclaration actorDecl)
    {
        // Generate a concrete C# actor class implementing IActor.
        WriteIndent();
        _output.Append("public class ");
        _output.Append(EscapeIdentifier(actorDecl.Name));
        _output.Append(" : IActor");
        if (actorDecl.Messages != null && actorDecl.Messages.Count > 0)
        {
            _output.Append(", IActorMessageMetadata");
        }
        _output.AppendLine();
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileActor) + " (open)");
        _output.AppendLine();
        _indentLevel++;

        // Emit actor message declarations (actor sugar) as a comment for documentation.
        if (actorDecl.Messages != null && actorDecl.Messages.Count > 0)
        {
            WriteIndent();
            _output.Append("// Messages: ");
            for (int i = 0; i < actorDecl.Messages.Count; i++)
            {
                if (i > 0)
                {
                    _output.Append(", ");
                }

                var msg = actorDecl.Messages[i];
                _output.Append(msg.Name);
                _output.Append("(");
                for (int p = 0; p < msg.ParameterNames.Count; p++)
                {
                    if (p > 0) _output.Append(", ");
                    _output.Append(msg.ParameterNames[p]);
                }
                _output.Append(")");
                if (!string.IsNullOrEmpty(msg.ReturnType))
                {
                    _output.Append(" -> ");
                    _output.Append(msg.ReturnType);
                }
            }
            _output.Append(";");
            _output.AppendLine();

            WriteIndent();
            _output.Append("public bool IsDeclaredMessage(string name) => ");
            for (int i = 0; i < actorDecl.Messages.Count; i++)
            {
                if (i > 0)
                {
                    _output.Append(" || ");
                }

                _output.Append("string.Equals(name, \"");
                _output.Append(actorDecl.Messages[i].Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
                _output.Append("\", StringComparison.Ordinal)");
            }
            _output.Append(";");
            _output.AppendLine();
        }

        foreach (var member in actorDecl.Members)
        {
            TranspileActorMember(member);
        }

        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileActor) + " (close)");
        _output.AppendLine();
    }

    private void TranspileActorMember(ClassMember member)
    {
        WriteIndent();

        // Access modifier
        if (member.Access == AccessModifier.Public)
            _output.Append("public ");
        else if (member.Access == AccessModifier.Private)
            _output.Append("private ");

        if (member.IsStatic)
            _output.Append("static ");

        switch (member.Type)
        {
            case MemberType.Field:
                var fieldType = ResolveTranspiledTypeHint(member.TypeHint);
                _output.Append(GetClrTypeName(fieldType));
                _output.Append(" ");
                _output.Append(member.Name);
                if (member.Value != null && member.Value is Expression expr)
                {
                    _output.Append(" = ");
                    _output.Append(GetCoercionExpressionPrefix(fieldType));
                    TranspileExpression(expr);
                    _output.Append(GetCoercionExpressionSuffix(fieldType));
                }
                _output.AppendLine(";");
                break;

            case MemberType.Method:
                if (member.Value is FunctionDeclaration func)
                {
                    // Transpile method decorators
                    if (func.Decorators != null && func.Decorators.Count > 0)
                    {
                        foreach (var decorator in func.Decorators)
                        {
                            WriteIndent();
                            TranspileDecorator(decorator);
                            _output.AppendLine();
                        }
                    }

                    WriteIndent();
                    // Actor message handlers are fire-and-forget; use void
                    _output.Append("void ");
                    _output.Append(EscapeIdentifier(member.Name));
                    _output.Append("(");
                    PushTypedScope();
                    for (int i = 0; i < func.Parameters.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");

                        // Parameter decorators
                        if (func.ParameterDecorators != null && i < func.ParameterDecorators.Count)
                        {
                            var decorator = func.ParameterDecorators[i];
                            if (decorator != null)
                            {
                                TranspileDecorator(decorator);
                                _output.Append(" ");
                            }
                        }

                        var parameterType = (func.ParameterTypeHints != null && i < func.ParameterTypeHints.Count)
                            ? ResolveTranspiledTypeHint(func.ParameterTypeHints[i])
                            : TranspiledClrType.Object;
                        RegisterTypedVariable(func.Parameters[i], parameterType);
                        _output.Append(GetClrTypeName(parameterType));
                        _output.Append(" ");
                        _output.Append(EscapeIdentifier(func.Parameters[i]));
                    }
                    _output.AppendLine(")");
                    _output.AppendLine();
                    WriteIndent();
                    _output.AppendLine("{");
                    _indentLevel++;
                    
                    // Set flag to indicate we're in an actor handler
                    var previousInActorHandler = _isInActorHandler;
                    _isInActorHandler = true;
                    _currentFunctionReturnType.Push(TranspiledClrType.Object);
                    TranspileBlock(func.Body);
                    _currentFunctionReturnType.Pop();
                    PopTypedScope();
                    // Restore flag
                    _isInActorHandler = previousInActorHandler;
                    
                    _indentLevel--;
                    WriteIndent();
                    _output.AppendLine("}");
                }
                break;

            case MemberType.Constructor:
                if (member.Value is FunctionDeclaration ctor)
                {
                    // Transpile constructor decorators
                    if (ctor.Decorators != null && ctor.Decorators.Count > 0)
                    {
                        foreach (var decorator in ctor.Decorators)
                        {
                            WriteIndent();
                            TranspileDecorator(decorator);
                            _output.AppendLine();
                        }
                    }

                    WriteIndent();
                    // Default visibility for actor constructors should be public to allow spawn
                    if (member.Access == AccessModifier.Default)
                    {
                        _output.Append("public ");
                    }
                    _output.Append(EscapeIdentifier(member.Name));
                    _output.Append("(");
                    PushTypedScope();
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");

                        // Parameter decorators
                        if (ctor.ParameterDecorators != null && i < ctor.ParameterDecorators.Count)
                        {
                            var decorator = ctor.ParameterDecorators[i];
                            if (decorator != null)
                            {
                                TranspileDecorator(decorator);
                                _output.Append(" ");
                            }
                        }

                        var parameterType = (ctor.ParameterTypeHints != null && i < ctor.ParameterTypeHints.Count)
                            ? ResolveTranspiledTypeHint(ctor.ParameterTypeHints[i])
                            : TranspiledClrType.Object;
                        RegisterTypedVariable(ctor.Parameters[i], parameterType);
                        _output.Append(GetClrTypeName(parameterType));
                        _output.Append(" ");
                        _output.Append(EscapeIdentifier(ctor.Parameters[i]));
                    }
                    _output.AppendLine(")");
                    _currentFunctionReturnType.Push(TranspiledClrType.Object);
                    TranspileFunctionBlock(ctor.Body, member.Name + ".ctor", ctor.Line);
                    _currentFunctionReturnType.Pop();
                    PopTypedScope();
                }
                break;
        }
    }
    
    private void TranspileSpawn(SpawnExpression spawn)
    {
        // Spawn expression: returns an ActorRef boxed as object
        _output.Append("(object)ActorsRuntime.Spawn(new ");
        _output.Append(EscapeIdentifier(spawn.ActorName));
        _output.Append("(");
        for (int i = 0; i < spawn.Arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            TranspileExpression(spawn.Arguments[i]);
        }
        _output.Append("))");
    }
    
    private void TranspileSend(SendStatement send)
    {
        // Special handling for stop(): send target.stop() should call ActorsRuntime.Stop() directly
        if (send.HandlerName == "stop" && send.Arguments.Count == 0 && send.Callback == null && send.TimeoutMilliseconds == null)
        {
            WriteIndent();
            _output.Append("RuntimeHelpers.CallActorOrVoidStop(");
            TranspileExpression(send.Target);
            _output.Append(");");
            AppendComment(nameof(TranspileSend) + " (stop)");
            _output.AppendLine();
            return;
        }

        // Call-style send with callback: send target.handlerName(args...) then (result) { ... };
        if (send.Callback != null)
        {
            WriteIndent();
            _output.Append("{");
            AppendComment(nameof(TranspileSend) + " (call-style with callback)");
            _output.AppendLine();
            _indentLevel++;

            // Evaluate target once and cast to ActorRef
            WriteIndent();
            _output.Append("var __target = (ActorRef)(");
            TranspileExpression(send.Target);
            _output.Append(");");
            _output.AppendLine();

            // Get current actor reference (sender)
            WriteIndent();
            _output.Append("var __self = ActorsRuntime.GetSelf();");
            _output.AppendLine();

            // Build callback delegate
            var callback = send.Callback;
            WriteIndent();
            _output.Append("System.Func<object?, System.Threading.Tasks.Task> __callback = async (object? ");
            _output.Append(EscapeIdentifier(callback.ParameterName));
            _output.Append("Arg) =>");
            _output.AppendLine();
            WriteIndent();
            _output.Append("{");
            _output.AppendLine();
            _indentLevel++;

            // Map MALDA callback parameter name to local variable
            WriteIndent();
            _output.Append("object ");
            _output.Append(EscapeIdentifier(callback.ParameterName));
            _output.Append(" = ");
            _output.Append(EscapeIdentifier(callback.ParameterName));
            _output.Append("Arg;");
            _output.AppendLine();

            // Transpile callback body
            TranspileBlock(callback.Body);

            _indentLevel--;
            WriteIndent();
            _output.Append("};");
            _output.AppendLine();

            // Build timeout error handler delegate if provided
            if (send.TimeoutErrorHandler != null)
            {
                var errorHandler = send.TimeoutErrorHandler;
                WriteIndent();
                _output.Append("System.Func<object?, System.Threading.Tasks.Task>? __timeoutErrorHandler = async (object? ");
                _output.Append(EscapeIdentifier(errorHandler.ParameterName));
                _output.Append("Arg) =>");
                _output.AppendLine();
                WriteIndent();
                _output.Append("{");
                _output.AppendLine();
                _indentLevel++;

                // Map MALDA error handler parameter name to local variable
                WriteIndent();
                _output.Append("object ");
                _output.Append(EscapeIdentifier(errorHandler.ParameterName));
                _output.Append(" = ");
                _output.Append(EscapeIdentifier(errorHandler.ParameterName));
                _output.Append("Arg;");
                _output.AppendLine();

                // Transpile error handler body
                TranspileBlock(errorHandler.Body);

                _indentLevel--;
                WriteIndent();
                _output.Append("};");
                _output.AppendLine();
            }
            else
            {
                WriteIndent();
                _output.Append("System.Func<object?, System.Threading.Tasks.Task>? __timeoutErrorHandler = null;");
                _output.AppendLine();
            }

            // Evaluate timeout milliseconds if provided
            if (send.TimeoutMilliseconds != null)
            {
                WriteIndent();
                _output.Append("int? __timeoutMs = (int)(RuntimeHelpers.CoerceToInt(");
                TranspileExpression(send.TimeoutMilliseconds);
                _output.Append("));");
                _output.AppendLine();
            }
            else
            {
                WriteIndent();
                _output.Append("int? __timeoutMs = null;");
                _output.AppendLine();
            }

            // Send with callback through ActorsRuntime
            WriteIndent();
            _output.Append("ActorsRuntime.SendWithCallback(__self, __target, ");
            if (send.HandlerName != null)
            {
                _output.Append("\"");
                _output.Append(send.HandlerName);
                _output.Append("\"");
            }
            else
            {
                _output.Append("null");
            }
            _output.Append(", __callback, __timeoutMs, __timeoutErrorHandler");
            for (int i = 0; i < send.Arguments.Count; i++)
            {
                _output.Append(", ");
                TranspileExpression(send.Arguments[i]);
            }
            _output.Append(");");
            _output.AppendLine();

            _indentLevel--;
            WriteIndent();
            _output.Append("}");
            AppendComment(nameof(TranspileSend) + " (call-style with callback end)");
            _output.AppendLine();
            return;
        }

        // Call-style send without callback: send target.handlerName(args...);
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileSend) + " (call-style)");
        _output.AppendLine();
        _indentLevel++;

        // Evaluate target once and cast to ActorRef
        WriteIndent();
        _output.Append("var __target = (ActorRef)(");
        TranspileExpression(send.Target);
        _output.Append(");");
        _output.AppendLine();

        WriteIndent();
        _output.Append("ActorsRuntime.Send(__target, ");
        if (send.HandlerName != null)
        {
            _output.Append("\"");
            _output.Append(send.HandlerName);
            _output.Append("\"");
        }
        else
        {
            _output.Append("null");
        }
        for (int i = 0; i < send.Arguments.Count; i++)
        {
            _output.Append(", ");
            TranspileExpression(send.Arguments[i]);
        }
        _output.Append(");");
        _output.AppendLine();

        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileSend) + " (call-style end)");
        _output.AppendLine();
    }
    
    private void EmitVariantConstructorMethod(VariantConstructor ctor)
    {
        WriteIndent();
        _output.Append("private static MaldaLang.Interpreter.RuntimeValue ");
        _output.Append(EscapeIdentifier(ctor.Name));
        _output.Append("(");
        var n = ctor.ParameterNames.Count;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("MaldaLang.Interpreter.RuntimeValue a");
            _output.Append(i);
        }
        _output.Append(") => MaldaLang.Interpreter.RuntimeValue.Variant(\"");
        _output.Append(ctor.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.Append("\", new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("a");
            _output.Append(i);
        }
        _output.Append(" });");
        _output.AppendLine();
    }

    private void TranspileMatch(MatchExpression match)
    {
        _matchBindCounter = 0;
        var valueVar = "__matchValue";
        _output.Append("((");
        _output.Append("Func<MaldaLang.Interpreter.RuntimeValue>)(() => { var ");
        _output.Append(valueVar);
        _output.Append(" = RuntimeHelpers.ToRuntimeValue(");
        TranspileExpression(match.Value);
        _output.Append("); ");
        for (int i = 0; i < match.Cases.Count; i++)
        {
            var matchCase = match.Cases[i];
            if (i > 0) _output.Append(" else ");
            TranspileMatchCase(valueVar, matchCase.Pattern, matchCase.Body);
        }
        if (match.DefaultCase != null)
        {
            _output.Append(" else { ");
            TranspileMatchBody(match.DefaultCase);
            _output.Append("}");
        }
        else
        {
            _output.Append(" else { throw new RuntimeException(\"Match expression had no matching case and no default case.\"); }");
        }
        _output.Append(" }))()");
    }

    private void TranspileMatchCase(string valueVar, Pattern pattern, Statement body)
    {
        _output.Append("if (");
        TranspileMatchCondition(valueVar, pattern);
        _output.Append(") { ");
        TranspileMatchBindAndBody(valueVar, pattern, body);
        _output.Append(" }");
    }

    private void TranspileMatchCondition(string valueVar, Pattern pattern)
    {
        switch (pattern)
        {
            case LiteralPattern literal:
                if (literal.Value == null)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Null");
                }
                else if (literal.Value is int iVal)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Integer && ");
                    _output.Append(valueVar);
                    _output.Append(".AsInteger() == ");
                    _output.Append(iVal);
                }
                else if (literal.Value is long lVal)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Integer && ");
                    _output.Append(valueVar);
                    _output.Append(".AsInteger() == ");
                    _output.Append((int)lVal);
                }
                else if (literal.Value is double d)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Float && Math.Abs(");
                    _output.Append(valueVar);
                    _output.Append(".AsFloat() - ");
                    _output.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _output.Append(") < 0.0001");
                }
                else if (literal.Value is float f)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Float && Math.Abs(");
                    _output.Append(valueVar);
                    _output.Append(".AsFloat() - ");
                    _output.Append(((double)f).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _output.Append(") < 0.0001");
                }
                else if (literal.Value is string s)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.String && string.Equals(");
                    _output.Append(valueVar);
                    _output.Append(".AsString(), ");
                    _output.Append("\"");
                    _output.Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r"));
                    _output.Append("\")");
                }
                else if (literal.Value is bool b)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Boolean && ");
                    _output.Append(valueVar);
                    _output.Append(".AsBoolean() == ");
                    _output.Append(b ? "true" : "false");
                }
                else
                {
                    _output.Append("false");
                }
                break;
            case IdentifierPattern _:
                _output.Append("true");
                break;
            case WildcardPattern _:
                _output.Append("true");
                break;
            case VariantPattern variantPattern:
                _output.Append("RuntimeHelpers.IsVariant(");
                _output.Append(valueVar);
                _output.Append(") && string.Equals(RuntimeHelpers.GetVariantTag(");
                _output.Append(valueVar);
                _output.Append("), \"");
                _output.Append(variantPattern.Tag.Replace("\\", "\\\\").Replace("\"", "\\\""));
                _output.Append("\") && RuntimeHelpers.GetVariantPayload(");
                _output.Append(valueVar);
                _output.Append(").Count == ");
                _output.Append(variantPattern.PayloadPatterns.Count);
                break;
            case ArrayPattern arrayPattern:
                EmitMatchArrayPatternCondition(valueVar, arrayPattern);
                break;
            case ObjectPattern objectPattern:
                EmitMatchObjectPatternCondition(valueVar, objectPattern);
                break;
            default:
                _output.Append("true");
                break;
        }
    }

    private void EmitMatchArrayPatternCondition(string valueVar, ArrayPattern arrayPattern)
    {
        _output.Append("RuntimeHelpers.IsArray(");
        _output.Append(valueVar);
        _output.Append(") && RuntimeHelpers.GetArray(");
        _output.Append(valueVar);
        _output.Append(").Count ");
        _output.Append(arrayPattern.Rest == null ? "== " : ">= ");
        _output.Append(arrayPattern.Elements.Count);
        for (int i = 0; i < arrayPattern.Elements.Count; i++)
        {
            _output.Append(" && (");
            TranspileMatchCondition(
                $"RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.GetArray({valueVar})[{i}])",
                arrayPattern.Elements[i]);
            _output.Append(")");
        }
    }

    private void EmitMatchObjectPatternCondition(string valueVar, ObjectPattern objectPattern)
    {
        _output.Append("RuntimeHelpers.IsObject(");
        _output.Append(valueVar);
        _output.Append(")");
        var objExpr = $"RuntimeHelpers.UnwrapRuntimeValue({valueVar})";
        foreach (var prop in objectPattern.Properties)
        {
            var escapedKey = prop.Key.Replace("\\", "\\\\").Replace("\"", "\\\"");
            _output.Append(" && RuntimeHelpers.ObjectHasKey(");
            _output.Append(objExpr);
            _output.Append(", \"");
            _output.Append(escapedKey);
            _output.Append("\")");
            if (prop.Pattern != null)
            {
                _output.Append(" && (");
                TranspileMatchCondition(
                    $"RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.GetObjectMember({objExpr}, \"{escapedKey}\"))",
                    prop.Pattern);
                _output.Append(")");
            }
        }
    }

    private void TranspileMatchBindAndBody(string valueVar, Pattern pattern, Statement body)
    {
        EmitMatchPatternBindings(valueVar, pattern);
        TranspileMatchBody(body);
    }

    private void EmitMatchPatternBindings(string valueVar, Pattern pattern)
    {
        switch (pattern)
        {
            case IdentifierPattern identifier:
                _output.Append("var ");
                _output.Append(EscapeIdentifier(identifier.Name));
                _output.Append(" = ");
                _output.Append(valueVar);
                _output.Append("; ");
                break;
            case VariantPattern variantPattern:
                _output.Append("var __payload = RuntimeHelpers.GetVariantPayload(");
                _output.Append(valueVar);
                _output.Append("); ");
                for (int i = 0; i < variantPattern.PayloadPatterns.Count; i++)
                {
                    var sub = variantPattern.PayloadPatterns[i];
                    if (sub is IdentifierPattern idp)
                    {
                        _output.Append("var ");
                        _output.Append(EscapeIdentifier(idp.Name));
                        _output.Append(" = __payload[");
                        _output.Append(i);
                        _output.Append("]; ");
                    }
                    else
                    {
                        _output.Append("var __sub");
                        _output.Append(i);
                        _output.Append(" = __payload[");
                        _output.Append(i);
                        _output.Append("]; ");
                    }
                }
                break;
            case ArrayPattern arrayPattern:
                _output.Append("var __arr = RuntimeHelpers.GetArray(");
                _output.Append(valueVar);
                _output.Append("); ");
                for (int i = 0; i < arrayPattern.Elements.Count; i++)
                {
                    EmitMatchPatternBindings(
                        $"RuntimeHelpers.ToRuntimeValue(__arr[{i}])",
                        arrayPattern.Elements[i]);
                }
                if (arrayPattern.Rest?.Name != null)
                {
                    _output.Append("var ");
                    _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                    _output.Append(" = __arr.Skip(");
                    _output.Append(arrayPattern.Elements.Count);
                    _output.Append(").ToList(); ");
                }
                break;
            case ObjectPattern objectPattern:
            {
                var objVar = $"__obj{_matchBindCounter++}";
                _output.Append("var ");
                _output.Append(objVar);
                _output.Append(" = RuntimeHelpers.UnwrapRuntimeValue(");
                _output.Append(valueVar);
                _output.Append("); ");
                foreach (var prop in objectPattern.Properties)
                {
                    var escapedKey = prop.Key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    if (prop.Pattern != null)
                    {
                        EmitMatchPatternBindings(
                            $"RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.GetObjectMember({objVar}, \"{escapedKey}\"))",
                            prop.Pattern);
                    }
                    else if (prop.BindingName != null)
                    {
                        _output.Append("var ");
                        _output.Append(EscapeIdentifier(prop.BindingName));
                        _output.Append(" = RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.GetObjectMember(");
                        _output.Append(objVar);
                        _output.Append(", \"");
                        _output.Append(escapedKey);
                        _output.Append("\")); ");
                    }
                }
                break;
            }
        }
    }
    
    /// <summary>
    /// Emits code for a match case body. "Last expression wins": for blocks, the last statement's value is returned if it's an expression.
    /// </summary>
    private void TranspileMatchBody(Statement body)
    {
        if (body is ExpressionStatement exprStmt)
        {
            if (IsStatementOnlyMatchExpression(exprStmt.Expression))
            {
                var previousEmitLineDirectives = _emitLineDirectives;
                _emitLineDirectives = false;
                try
                {
                    TranspileStatement(exprStmt);
                }
                finally
                {
                    _emitLineDirectives = previousEmitLineDirectives;
                }

                _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
            }
            else
            {
                _output.Append("return RuntimeHelpers.ToRuntimeValue(");
                TranspileExpression(exprStmt.Expression);
                _output.Append("); ");
            }
        }
        else if (body is BlockStatement block)
        {
            if (block.Statements.Count == 0)
            {
                _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
            }
            else
            {
                for (int i = 0; i < block.Statements.Count - 1; i++)
                {
                    var previousEmitLineDirectives = _emitLineDirectives;
                    _emitLineDirectives = false;
                    try
                    {
                        TranspileStatement(block.Statements[i]);
                    }
                    finally
                    {
                        _emitLineDirectives = previousEmitLineDirectives;
                    }
                }
                var last = block.Statements[block.Statements.Count - 1];
                if (last is ExpressionStatement lastExpr)
                {
                    if (IsStatementOnlyMatchExpression(lastExpr.Expression))
                    {
                        var previousEmitLineDirectives = _emitLineDirectives;
                        _emitLineDirectives = false;
                        try
                        {
                            TranspileStatement(lastExpr);
                        }
                        finally
                        {
                            _emitLineDirectives = previousEmitLineDirectives;
                        }

                        _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
                    }
                    else
                    {
                        _output.Append("return RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(lastExpr.Expression);
                        _output.Append("); ");
                    }
                }
                else
                {
                    var previousEmitLineDirectives = _emitLineDirectives;
                    _emitLineDirectives = false;
                    try
                    {
                        TranspileStatement(last);
                    }
                    finally
                    {
                        _emitLineDirectives = previousEmitLineDirectives;
                    }
                    _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
                }
            }
        }
        else
        {
            var previousEmitLineDirectives = _emitLineDirectives;
            _emitLineDirectives = false;
            try
            {
                TranspileStatement(body);
            }
            finally
            {
                _emitLineDirectives = previousEmitLineDirectives;
            }
            _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
        }
    }

    private static bool IsStatementOnlyMatchExpression(Expression expression)
    {
        if (expression is FunctionCallExpression call &&
            call.Callee is IdentifierExpression identifier)
        {
            return identifier.Name == "reply";
        }

        return false;
    }
    
    private void TranspilePatternForMatch(Pattern pattern)
    {
        // Generate pattern representation for runtime matching
        // This will be used by RuntimeHelpers.MatchPattern
        _output.Append("new { Type = \"");
        _output.Append(pattern.GetType().Name.Replace("Pattern", ""));
        _output.Append("\", ");
        
        switch (pattern)
        {
            case LiteralPattern literal:
                _output.Append("Value = ");
                TranspileLiteral(new LiteralExpression(literal.Value, literal.Line, literal.Column));
                break;
                
            case IdentifierPattern identifier:
                _output.Append("Name = \"");
                _output.Append(EscapeIdentifier(identifier.Name));
                _output.Append("\"");
                break;
                
            case WildcardPattern:
                _output.Append("Wildcard = true");
                break;
                
            case ArrayPattern arrayPattern:
                _output.Append("Elements = new List<object> { ");
                for (int i = 0; i < arrayPattern.Elements.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspilePatternForMatch(arrayPattern.Elements[i]);
                }
                _output.Append(" }, Rest = ");
                if (arrayPattern.Rest != null)
                {
                    _output.Append("new { Name = ");
                    if (arrayPattern.Rest.Name != null)
                    {
                        _output.Append("\"");
                        _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                        _output.Append("\"");
                    }
                    else
                    {
                        _output.Append("null");
                    }
                    _output.Append(" }");
                }
                else
                {
                    _output.Append("null");
                }
                break;
                
            case ObjectPattern objectPattern:
                _output.Append("Properties = new Dictionary<string, object> { ");
                for (int i = 0; i < objectPattern.Properties.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    var prop = objectPattern.Properties[i];
                    _output.Append("{ \"");
                    _output.Append(EscapeIdentifier(prop.Key));
                    _output.Append("\", ");
                    if (prop.Pattern != null)
                    {
                        TranspilePatternForMatch(prop.Pattern);
                    }
                    else if (prop.BindingName != null)
                    {
                        _output.Append("new { Type = \"Identifier\", Name = \"");
                        _output.Append(EscapeIdentifier(prop.BindingName));
                        _output.Append("\" }");
                    }
                    else
                    {
                        _output.Append("null");
                    }
                    _output.Append(" }");
                }
                _output.Append(" }");
                break;

            case VariantPattern variantPattern:
                _output.Append("Tag = \"");
                _output.Append(EscapeIdentifier(variantPattern.Tag));
                _output.Append("\", PayloadPatterns = new List<object> { ");
                for (int i = 0; i < variantPattern.PayloadPatterns.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspilePatternForMatch(variantPattern.PayloadPatterns[i]);
                }
                _output.Append(" }");
                break;
        }
        
        _output.Append(" }");
    }
    
    private void TranspilePattern(Pattern pattern)
    {
        TranspilePatternForMatch(pattern);
    }
    
    private void TranspileDestructuringVarDecl(DestructuringVarDecl stmt)
    {
        WriteIndent();
        _output.Append("var __destructureValue = ");
        TranspileExpression(stmt.Initializer);
        _output.Append(";");
        _output.AppendLine();
        
        if (stmt.Pattern is ArrayDestructuringPattern arrayPattern)
        {
            TranspileInlineArrayDestructuring(arrayPattern);
        }
        else if (stmt.Pattern is ObjectDestructuringPattern objectPattern)
        {
            TranspileInlineObjectDestructuring(objectPattern);
        }
        else
        {
            // Fallback: throw at runtime (unsupported pattern in transpiled code)
            WriteIndent();
            _output.AppendLine("throw new System.Exception(\"Destructuring pattern did not match value.\");");
        }
    }
    
    private void TranspileInlineArrayDestructuring(ArrayDestructuringPattern arrayPattern)
    {
        var requiredCount = arrayPattern.Rest != null ? arrayPattern.Elements.Count : arrayPattern.Elements.Count;
        WriteIndent();
        _output.Append("var __destructureArr = RuntimeHelpers.GetArray(__destructureValue);");
        _output.AppendLine();
        WriteIndent();
        _output.Append("if (__destructureArr.Count ");
        _output.Append(arrayPattern.Rest != null ? "< " : "!= ");
        _output.Append(requiredCount);
        _output.Append(") throw new System.Exception(\"Destructuring pattern did not match value.\");");
        _output.AppendLine();
        for (int i = 0; i < arrayPattern.Elements.Count; i++)
        {
            if (arrayPattern.Elements[i] is IdentifierPattern idPattern)
            {
                WriteIndent();
                _output.Append("object ");
                _output.Append(EscapeIdentifier(idPattern.Name));
                _output.Append(" = __destructureArr[");
                _output.Append(i);
                _output.Append("];");
                _output.AppendLine();
            }
        }
        if (arrayPattern.Rest != null && arrayPattern.Rest.Name != null)
        {
            WriteIndent();
            _output.Append("var __restList = new List<object>();");
            _output.AppendLine();
            WriteIndent();
            _output.Append("for (int __ri = ");
            _output.Append(arrayPattern.Elements.Count);
            _output.Append("; __ri < __destructureArr.Count; __ri++) __restList.Add(__destructureArr[__ri]);");
            _output.AppendLine();
            WriteIndent();
            _output.Append("object ");
            _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
            _output.Append(" = __restList;");
            _output.AppendLine();
        }
    }
    
    private void TranspileInlineObjectDestructuring(ObjectDestructuringPattern objectPattern)
    {
        WriteIndent();
        _output.AppendLine("if (__destructureValue == null || !RuntimeHelpers.IsObject(__destructureValue)) throw new System.Exception(\"Destructuring pattern did not match value.\");");
        foreach (var prop in objectPattern.Properties)
        {
            var varName = prop.BindingName ?? prop.Key;
            WriteIndent();
            _output.Append("if (!RuntimeHelpers.ObjectHasKey(__destructureValue, \"");
            _output.Append(EscapeIdentifier(prop.Key));
            _output.Append("\")) throw new System.Exception(\"Destructuring pattern did not match value.\");");
            _output.AppendLine();
            WriteIndent();
            _output.Append("object ");
            _output.Append(EscapeIdentifier(varName));
            _output.Append(" = RuntimeHelpers.GetObjectMember(__destructureValue, \"");
            _output.Append(EscapeIdentifier(prop.Key));
            _output.Append("\");");
            _output.AppendLine();
        }
    }
    
    private void TranspileDestructuringAssignment(DestructuringAssignment stmt)
    {
        WriteIndent();
        _output.Append("var __destructureValue = ");
        TranspileExpression(stmt.Value);
        _output.Append(";");
        _output.AppendLine();
        
        if (stmt.Pattern is ArrayDestructuringPattern arrayPattern)
        {
            TranspileInlineArrayDestructuringAssignment(arrayPattern);
        }
        else if (stmt.Pattern is ObjectDestructuringPattern objectPattern)
        {
            TranspileInlineObjectDestructuringAssignment(objectPattern);
        }
    }
    
    private void TranspileInlineArrayDestructuringAssignment(ArrayDestructuringPattern arrayPattern)
    {
        var requiredCount = arrayPattern.Rest != null ? arrayPattern.Elements.Count : arrayPattern.Elements.Count;
        WriteIndent();
        _output.Append("var __destructureArr = RuntimeHelpers.GetArray(__destructureValue);");
        _output.AppendLine();
        WriteIndent();
        _output.Append("if (__destructureArr.Count ");
        _output.Append(arrayPattern.Rest != null ? "< " : "!= ");
        _output.Append(requiredCount);
        _output.Append(") throw new System.Exception(\"Destructuring pattern did not match value.\");");
        _output.AppendLine();
        for (int i = 0; i < arrayPattern.Elements.Count; i++)
        {
            if (arrayPattern.Elements[i] is IdentifierPattern idPattern)
            {
                WriteIndent();
                _output.Append(EscapeIdentifier(idPattern.Name));
                _output.Append(" = __destructureArr[");
                _output.Append(i);
                _output.Append("];");
                _output.AppendLine();
            }
        }
        if (arrayPattern.Rest != null && arrayPattern.Rest.Name != null)
        {
            WriteIndent();
            _output.Append("var __restList = new List<object>();");
            _output.AppendLine();
            WriteIndent();
            _output.Append("for (int __ri = ");
            _output.Append(arrayPattern.Elements.Count);
            _output.Append("; __ri < __destructureArr.Count; __ri++) __restList.Add(__destructureArr[__ri]);");
            _output.AppendLine();
            WriteIndent();
            _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
            _output.Append(" = __restList;");
            _output.AppendLine();
        }
    }
    
    private void TranspileInlineObjectDestructuringAssignment(ObjectDestructuringPattern objectPattern)
    {
        WriteIndent();
        _output.AppendLine("if (__destructureValue == null || !RuntimeHelpers.IsObject(__destructureValue)) throw new System.Exception(\"Destructuring pattern did not match value.\");");
        foreach (var prop in objectPattern.Properties)
        {
            var varName = prop.BindingName ?? prop.Key;
            WriteIndent();
            _output.Append("if (!RuntimeHelpers.ObjectHasKey(__destructureValue, \"");
            _output.Append(EscapeIdentifier(prop.Key));
            _output.Append("\")) throw new System.Exception(\"Destructuring pattern did not match value.\");");
            _output.AppendLine();
            WriteIndent();
            _output.Append(EscapeIdentifier(varName));
            _output.Append(" = RuntimeHelpers.GetObjectMember(__destructureValue, \"");
            _output.Append(EscapeIdentifier(prop.Key));
            _output.Append("\");");
            _output.AppendLine();
        }
    }
    
    private void TranspileDestructuringPattern(DestructuringPattern pattern)
    {
        if (pattern is ArrayDestructuringPattern arrayPattern)
        {
            _output.Append("new { Type = \"ArrayDestructuring\", Elements = new List<object> { ");
            for (int i = 0; i < arrayPattern.Elements.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                TranspilePatternForMatch(arrayPattern.Elements[i]);
            }
            if (arrayPattern.Rest != null)
            {
                if (arrayPattern.Elements.Count > 0) _output.Append(", ");
                _output.Append("new { Type = \"Rest\", Name = ");
                if (arrayPattern.Rest.Name != null)
                {
                    _output.Append("\"");
                    _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                    _output.Append("\"");
                }
                else
                {
                    _output.Append("null");
                }
                _output.Append(" }");
            }
            _output.Append(" } }");
        }
        else if (pattern is ObjectDestructuringPattern objectPattern)
        {
            _output.Append("new { Type = \"ObjectDestructuring\", Properties = new Dictionary<string, object> { ");
            for (int i = 0; i < objectPattern.Properties.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                var prop = objectPattern.Properties[i];
                _output.Append("{ \"");
                _output.Append(EscapeIdentifier(prop.Key));
                _output.Append("\", ");
                if (prop.Pattern != null)
                {
                    TranspilePatternForMatch(prop.Pattern);
                }
                else if (prop.BindingName != null)
                {
                    _output.Append("new { Type = \"Identifier\", Name = \"");
                    _output.Append(EscapeIdentifier(prop.BindingName));
                    _output.Append("\" }");
                }
                else
                {
                    _output.Append("null");
                }
                _output.Append(" }");
            }
            _output.Append(" } }");
        }
    }
    
    private void ExtractAndDeclareVariables(DestructuringPattern pattern, string bindingsVar)
    {
        // Extract variable names and generate declarations
        // This is simplified - full implementation would handle nested patterns
        if (pattern is ArrayDestructuringPattern arrayPattern)
        {
            for (int i = 0; i < arrayPattern.Elements.Count; i++)
            {
                if (arrayPattern.Elements[i] is IdentifierPattern idPattern)
                {
                    WriteIndent();
                    _output.Append("object ");
                    _output.Append(EscapeIdentifier(idPattern.Name));
                    _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                    _output.Append(bindingsVar);
                    _output.Append("[\"");
                    _output.Append(idPattern.Name);
                    _output.Append("\"]);");
                    _output.AppendLine();
                }
            }
            if (arrayPattern.Rest != null && arrayPattern.Rest.Name != null)
            {
                WriteIndent();
                _output.Append("object ");
                _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                _output.Append(bindingsVar);
                _output.Append("[\"");
                _output.Append(arrayPattern.Rest.Name);
                _output.Append("\"]);");
                _output.AppendLine();
            }
        }
        else if (pattern is ObjectDestructuringPattern objectPattern)
        {
            foreach (var prop in objectPattern.Properties)
            {
                var varName = prop.BindingName ?? prop.Key;
                WriteIndent();
                _output.Append("object ");
                _output.Append(EscapeIdentifier(varName));
                _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                _output.Append(bindingsVar);
                _output.Append("[\"");
                _output.Append(varName);
                _output.Append("\"]);");
                _output.AppendLine();
            }
        }
    }
    
    private void ExtractAndAssignVariables(DestructuringPattern pattern, string bindingsVar)
    {
        // Similar to ExtractAndDeclareVariables but uses assignment
        if (pattern is ArrayDestructuringPattern arrayPattern)
        {
            for (int i = 0; i < arrayPattern.Elements.Count; i++)
            {
                if (arrayPattern.Elements[i] is IdentifierPattern idPattern)
                {
                    WriteIndent();
                    _output.Append(EscapeIdentifier(idPattern.Name));
                    _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                    _output.Append(bindingsVar);
                    _output.Append("[\"");
                    _output.Append(idPattern.Name);
                    _output.Append("\"]);");
                    _output.AppendLine();
                }
            }
            if (arrayPattern.Rest != null && arrayPattern.Rest.Name != null)
            {
                WriteIndent();
                _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                _output.Append(bindingsVar);
                _output.Append("[\"");
                _output.Append(arrayPattern.Rest.Name);
                _output.Append("\"]);");
                _output.AppendLine();
            }
        }
        else if (pattern is ObjectDestructuringPattern objectPattern)
        {
            foreach (var prop in objectPattern.Properties)
            {
                var varName = prop.BindingName ?? prop.Key;
                WriteIndent();
                _output.Append(EscapeIdentifier(varName));
                _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                _output.Append(bindingsVar);
                _output.Append("[\"");
                _output.Append(varName);
                _output.Append("\"]);");
                _output.AppendLine();
            }
        }
    }

    private bool TryTranspileVariantStdLibCall(MemberAccessExpression memberAccess, FunctionCallExpression call)
    {
        if (memberAccess.Object is not IdentifierExpression moduleId)
            return false;

        string? staticMethod = (moduleId.Name, memberAccess.Member) switch
        {
            (StdLibNamespaces.ResultModule, "ok") => nameof(VariantStdLib.ResultOk),
            (StdLibNamespaces.ResultModule, "err") => nameof(VariantStdLib.ResultErr),
            (StdLibNamespaces.ResultModule, "map") => nameof(VariantStdLib.ResultMap),
            (StdLibNamespaces.ResultModule, "unwrapOr") => nameof(VariantStdLib.ResultUnwrapOr),
            (StdLibNamespaces.ResultModule, "isOk") => nameof(VariantStdLib.ResultIsOk),
            (StdLibNamespaces.ResultModule, "isErr") => nameof(VariantStdLib.ResultIsErr),
            (StdLibNamespaces.OptionModule, "some") => nameof(VariantStdLib.OptionSome),
            (StdLibNamespaces.OptionModule, "none") => nameof(VariantStdLib.OptionNone),
            (StdLibNamespaces.OptionModule, "map") => nameof(VariantStdLib.OptionMap),
            (StdLibNamespaces.OptionModule, "unwrapOr") => nameof(VariantStdLib.OptionUnwrapOr),
            (StdLibNamespaces.OptionModule, "isSome") => nameof(VariantStdLib.OptionIsSome),
            (StdLibNamespaces.OptionModule, "isNone") => nameof(VariantStdLib.OptionIsNone),
            _ => null
        };

        if (staticMethod == null)
            return false;

        if (memberAccess.Member == "map" && call.Arguments.Count == 2)
        {
            var (successTag, failureTag) = moduleId.Name == StdLibNamespaces.ResultModule
                ? ("Ok", "Err")
                : ("Some", "None");
            _output.Append("RuntimeHelpers.MapVariantWithDelegate(RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[0]);
            _output.Append("), ");
            TranspileExpression(call.Arguments[1]);
            _output.Append(", \"");
            _output.Append(successTag);
            _output.Append("\", \"");
            _output.Append(failureTag);
            _output.Append("\")");
            return true;
        }

        _output.Append("MaldaLang.BuiltIns.VariantStdLib.");
        _output.Append(staticMethod);
        _output.Append("(new List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
                _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[i]);
            _output.Append(")");
        }

        _output.Append(" })");
        return true;
    }
}
