// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;
using System.Reflection;
using System.IO;
using System;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using MaldaLang.PackageManager;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Interpreter.Debug;

public partial class Interpreter
{
    internal Environment _globals;
    private static readonly AsyncLocal<InterpreterActivation?> ActivationLocal = new();
    private readonly InterpreterActivation _rootActivation;
    internal BuiltIns.AgentInstance? _defaultAgent = null;
    internal Dictionary<string, ClassDefinition> _classes = new();
    internal Dictionary<string, ActorDefinition> _actors = new();
    internal Dictionary<string, WorkflowDeclaration> _workflows = new();
    internal Dictionary<string, PropertyDeclaration> _properties = new();
    private IDebuggerHook? _debuggerHook;
    private CancellationToken _interpretCancellation = CancellationToken.None;
    private IInputProvider? _inputProvider;
    private Action? _outputUpdateCallback;
    private Action<string>? _outputCallback;
    private readonly Dictionary<Guid, CallbackInfo> _pendingCallbacks = new();
    private Message? _currentMessage;
    private readonly Dictionary<string, Environment> _importedModules = new();
    private MaldaLang.PackageManager.ModuleLoader? _moduleLoader;
    private MaldaLang.PackageManager.DotNetPackageWrapper? _dotNetWrapper;
    private string? _sourceCode = null;
    private static int _interpretNesting;

    internal InterpreterActivation GetActivation()
    {
        var current = ActivationLocal.Value;
        if (current != null && ReferenceEquals(current.Owner, this))
            return current;
        return _rootActivation;
    }

    /// <summary>Cancellation token for the current activation (within scopes, cancelWorkflow).</summary>
    public static CancellationToken CurrentCancelToken =>
        ActivationLocal.Value?.CancelToken ?? CancellationToken.None;

    private void SetActivation(InterpreterActivation activation)
    {
        ActivationLocal.Value = activation;
    }

    /// <summary>
    /// Current task's environment. Backed by <see cref="InterpreterActivation"/> so
    /// overlapping <c>async</c> calls do not share a frame.
    /// </summary>
    internal Environment _environment
    {
        get => GetActivation().Environment;
        set => GetActivation().Environment = value;
    }

    private ObjectInstance? _currentObject
    {
        get => GetActivation().CurrentObject;
        set => GetActivation().CurrentObject = value;
    }

    private ClassDefinition? _currentClass
    {
        get => GetActivation().CurrentClass;
        set => GetActivation().CurrentClass = value;
    }

    /// <summary>
    /// The class that declares the innermost executing function, i.e. the lexically enclosing
    /// class. Unlike <see cref="_currentClass"/> (the runtime class of <c>this</c>), this is
    /// what <c>super</c> must resolve against; using the runtime class makes
    /// <c>super</c> recurse into the current class for multi-level hierarchies.
    /// </summary>
    private ClassDefinition? _currentDeclaringClass
    {
        get
        {
            foreach (var frame in _executionStack)
            {
                if (frame is FunctionFrame functionFrame
                    && functionFrame.Function.ClassName != null
                    && _classes.TryGetValue(functionFrame.Function.ClassName, out var declaringClass))
                {
                    return declaringClass;
                }
            }
            return null;
        }
    }

    private ActorInstance? _currentActor
    {
        get => GetActivation().CurrentActor;
        set => GetActivation().CurrentActor = value;
    }

    private List<InterpreterCallStackFrame> _callStack => GetActivation().CallStack;

    private string? _currentFile
    {
        get => GetActivation().CurrentFile;
        set => GetActivation().CurrentFile = value;
    }

    private Stack<ExecutionFrame> _executionStack => GetActivation().ExecutionStack;

    private WorkflowExecutionContext? _workflowContext
    {
        get => GetActivation().WorkflowContext;
        set => GetActivation().WorkflowContext = value;
    }

    private bool _insideWorkflowStep
    {
        get => GetActivation().InsideWorkflowStep;
        set => GetActivation().InsideWorkflowStep = value;
    }

    private Stack<List<Func<Task>>> _deferFrames => GetActivation().DeferFrames;
    
    public Interpreter(IDebuggerHook? debuggerHook = null, string? currentFile = null, IInputProvider? inputProvider = null)
    {
        _globals = new Environment();
        _rootActivation = new InterpreterActivation(this, _globals)
        {
            CurrentFile = currentFile
        };
        RootFile = currentFile;
        _debuggerHook = debuggerHook;
        _inputProvider = inputProvider;
        BuiltInFunctions.RegisterBuiltIns(_globals);
        
        // Register built-in VectorDB class
        _globals.Define("VectorDB", RuntimeValue.Class(VectorDBClassDefinition.Instance));
        
        // Register built-in GraphMemory class
        _globals.Define("GraphMemory", RuntimeValue.Class(GraphMemoryClassDefinition.Instance));
        
        // Clear user-defined tools from previous script executions to ensure a fresh environment
        // Persistent tools (e.g., IDE-managed MCP server tools) are preserved
        ToolRegistry.Instance.ClearUserDefinedTools();
        
        // Initialize module loader and .NET wrapper
        _moduleLoader = new MaldaLang.PackageManager.ModuleLoader();
        _dotNetWrapper = new MaldaLang.PackageManager.DotNetPackageWrapper();

        if (debuggerHook is DebugSession debugSession)
            debugSession.Bind(this);
        else if (debuggerHook is IHasDebugSession hasSession)
            hasSession.Session.Bind(this);
    }
    
    public void SetInputProvider(IInputProvider inputProvider)
    {
        _inputProvider = inputProvider;
    }
    
    public IInputProvider? GetInputProvider()
    {
        return _inputProvider;
    }
    
    /// <summary>The program file this interpreter was created for (the source of <see cref="GetSourceCode"/>).</summary>
    public string? RootFile { get; }

    public string? GetCurrentFile()
    {
        return _currentFile;
    }
    
    public void SetSourceCode(string? sourceCode)
    {
        _sourceCode = sourceCode;
    }
    
    public string? GetSourceCode()
    {
        return _sourceCode;
    }
    
    public string? GetSourceLine(int lineNumber)
    {
        if (_sourceCode == null || lineNumber < 1)
            return null;
        
        var lines = _sourceCode.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        if (lineNumber > lines.Length)
            return null;
        
        return lines[lineNumber - 1];
    }

    private static string GetStatementProfileName(Statement stmt)
    {
        var typeName = stmt.GetType().Name;
        return typeName.EndsWith("Statement", StringComparison.Ordinal)
            ? typeName[..^"Statement".Length]
            : typeName;
    }

    private string GetFunctionProfileName(FunctionValue function)
    {
        if (function.Declaration == null)
        {
            return function.ClassName ?? "<anonymous>";
        }

        return string.IsNullOrWhiteSpace(function.ClassName)
            ? function.Declaration.Name
            : $"{function.ClassName}.{function.Declaration.Name}";
    }
    
    public void SetOutputUpdateCallback(Action? callback)
    {
        _outputUpdateCallback = callback;
    }
    
    public void TriggerOutputUpdate()
    {
        _outputUpdateCallback?.Invoke();
    }
    
    public void SetOutputCallback(Action<string>? callback)
    {
        _outputCallback = callback;
    }
    
    public Action<string>? GetOutputCallback()
    {
        return _outputCallback;
    }
    
    public IDebuggerHook? GetDebuggerHook()
    {
        return _debuggerHook;
    }
    
    public List<InterpreterCallStackFrame> GetCallStack()
    {
        return new List<InterpreterCallStackFrame>(_callStack);
    }
    
    public List<(FunctionValue Function, string FunctionName)> GetDecoratedFunctions(string decoratorName)
    {
        var functions = new List<(FunctionValue, string)>();
        ScanEnvironmentForDecoratedFunctions(_globals, decoratorName, functions);
        return functions;
    }
    
    public ToolRegistry GetToolRegistry()
    {
        return ToolRegistry.Instance;
    }
    
    public ToolInstance? GetRegisteredTool(string name)
    {
        return ToolRegistry.Instance.GetTool(name);
    }
    
    public Dictionary<string, ToolInstance> GetAllRegisteredTools()
    {
        return ToolRegistry.Instance.GetAllTools();
    }
    
    /// <summary>
    /// Creates a new interpreter instance with copied definitions (classes, functions) but fresh execution state.
    /// This allows concurrent request handling by isolating execution state per request.
    /// </summary>
    public Interpreter CreateExecutionInterpreter()
    {
        // Create new interpreter with same configuration
        var newInterpreter = new Interpreter(_debuggerHook, _currentFile, _inputProvider);
        newInterpreter.SetSourceCode(_sourceCode);
        
        // Copy classes (definitions are immutable)
        foreach (var kvp in _classes)
        {
            newInterpreter._classes[kvp.Key] = kvp.Value;
            newInterpreter._globals.Define(kvp.Key, RuntimeValue.Class(kvp.Value));
        }
        
        // Copy global functions and variables (recreate with new closures)
        CopyGlobalEnvironment(_globals, newInterpreter._globals);
        
        return newInterpreter;
    }
    
    /// <summary>
    /// Loads a skill module from the given file path: reads, parses, runs in an isolated environment,
    /// and returns an object whose properties are the module's global variables (e.g. tools, agent).
    /// </summary>
    public RuntimeValue LoadSkillModule(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return RuntimeValue.Null();
        var source = File.ReadAllText(path);
        var lexer = new Lexer(source, path);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, path);
        var statements = parser.Parse();
        if (parser.Errors.Count > 0)
            throw new InvalidOperationException($"Parse errors in skill at {path}: {string.Join(", ", parser.Errors.Select(e => e.Message))}");
        var moduleEnvironment = new Environment();
        var moduleInterpreter = new Interpreter();
        moduleInterpreter._environment = moduleEnvironment;
        moduleInterpreter._globals = moduleEnvironment;
        moduleInterpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        var obj = new ObjectInstance(null);
        foreach (var kvp in moduleEnvironment.GetAllVariables())
            obj.Set(kvp.Key, kvp.Value);
        return RuntimeValue.Object(obj);
    }
    
    /// <summary>
    /// Copies all variables and functions from source environment to target environment.
    /// Functions are recreated with new closures pointing to the target environment.
    /// </summary>
    private void CopyGlobalEnvironment(Environment source, Environment target)
    {
        var variables = source.GetAllVariables();
        foreach (var kvp in variables)
        {
            if (kvp.Value.Type == ValueType.Function)
            {
                var func = kvp.Value.AsFunction();
                if (func != null)
                {
                    // Recreate function with new closure pointing to target environment
                    var newFunc = new FunctionValue(func.Declaration, target, func.IsConstructor, func.ClassName);
                    newFunc.Decorators = func.Decorators;
                    newFunc.ParameterDecorators = func.ParameterDecorators;
                    newFunc.BuiltInInstance = func.BuiltInInstance;
                    newFunc.BuiltInMethod = func.BuiltInMethod;
                    newFunc.BuiltInFunctionName = func.BuiltInFunctionName;
                    target.Define(kvp.Key, RuntimeValue.Function(newFunc));
                }
            }
            else
            {
                // Clone mutable runtime data so request-specific execution interpreters
                // do not leak changes back into the source interpreter.
                target.Define(kvp.Key, CloneRuntimeValueForExecution(kvp.Value));
            }
        }
    }

    private RuntimeValue CloneRuntimeValueForExecution(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => RuntimeValue.Integer(value.AsInteger()),
            ValueType.Float => RuntimeValue.Float(value.AsFloat()),
            ValueType.String => RuntimeValue.String(value.AsString()),
            ValueType.Boolean => RuntimeValue.Boolean(value.AsBoolean()),
            ValueType.Null => RuntimeValue.Null(),
            ValueType.Array => RuntimeValue.Array(value.AsArray().Select(CloneRuntimeValueForExecution).ToList()),
            ValueType.Variant => CloneVariantValueForExecution(value.AsVariant()),
            ValueType.Object => CloneObjectValueForExecution(value.AsObject()),
            _ => value
        };
    }

    private RuntimeValue CloneVariantValueForExecution(VariantValue variant)
    {
        return RuntimeValue.Variant(
            variant.Tag,
            variant.Payload.Select(CloneRuntimeValueForExecution).ToList());
    }

    private RuntimeValue CloneObjectValueForExecution(ObjectInstance instance)
    {
        if (instance is BuiltIns.JsonObject jsonObject)
        {
            var clone = new BuiltIns.JsonObject();
            foreach (var kvp in jsonObject.GetProperties())
            {
                clone.Set(kvp.Key, CloneRuntimeValueForExecution(kvp.Value));
            }
            return RuntimeValue.Object(clone);
        }

        if (instance is DictionaryInstance dictionary)
        {
            var cloneEntries = dictionary.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => CloneRuntimeValueForExecution(kvp.Value));
            return RuntimeValue.Object(new DictionaryInstance(cloneEntries));
        }

        if (instance is ArrayInstance arrayInstance)
        {
            return RuntimeValue.Array(arrayInstance.Elements.Select(CloneRuntimeValueForExecution).ToList());
        }

        if (instance.GetType() == typeof(ObjectInstance))
        {
            var clone = new ObjectInstance(instance.Class);
            foreach (var key in instance.GetAllKeys())
            {
                clone.Set(key, CloneRuntimeValueForExecution(instance.Get(key)));
            }
            return RuntimeValue.Object(clone);
        }

        return RuntimeValue.Object(instance);
    }
    
    private void ScanEnvironmentForDecoratedFunctions(Environment env, string decoratorName, List<(FunctionValue, string)> functions)
    {
        if (env == null)
            return;
        
        var variables = env.GetAllVariables();
        foreach (var kvp in variables)
        {
            if (kvp.Value == null)
                continue;
            
            if (kvp.Value.Type == ValueType.Function)
            {
                var func = kvp.Value.AsFunction();
                if (func == null)
                    continue;
                
                if (func.Decorators != null && func.Decorators.Any(d => d != null && d.Name == decoratorName))
                {
                    functions.Add((func, kvp.Key));
                }
            }
        }
        
        // Also scan class methods
        foreach (var klass in _classes.Values)
        {
            foreach (var method in klass.Methods)
            {
                if (method.Value.Decorators != null && method.Value.Decorators.Any(d => d.Name == decoratorName))
                {
                    functions.Add((method.Value, $"{klass.Name}.{method.Key}"));
                }
            }
            foreach (var method in klass.StaticMethods)
            {
                if (method.Value.Decorators != null && method.Value.Decorators.Any(d => d.Name == decoratorName))
                {
                    functions.Add((method.Value, $"{klass.Name}.{method.Key}"));
                }
            }
        }
    }
    
    public Dictionary<string, object> GetVariables()
    {
        var variables = new Dictionary<string, object>();
        // Use _globals to get all global variables, not _environment which might be a local scope
        var runtimeVars = _globals.GetAllVariables();
        
        foreach (var kvp in runtimeVars)
        {
            // Handle null values safely
            try
            {
                var str = kvp.Value.ToString();
                variables[kvp.Key] = str ?? "null";
            }
            catch (Exception ex)
            {
                // If ToString() fails (e.g., ObjectInstance with null reference), use a safe representation
                variables[kvp.Key] = $"<{kvp.Value.Type} - ToString() failed: {ex.Message}>";
            }
        }
        
        return variables;
    }
    
    public async Task InterpretAsync(List<Statement> statements, CancellationToken cancellationToken = default)
    {
        // Process-wide api/schema/sum-type registries survive across Interpreter
        // instances. Clear them only on the outermost interpret so Desktop/Web IDE
        // can re-run the same example. Nested interpret (file imports, runMALDA,
        // skill modules) must keep the host program's registrations.
        var isTopLevel = Interlocked.Increment(ref _interpretNesting) == 1;
        try
        {
            if (isTopLevel)
                ResetProcessWideDeclarations();

            await InterpretAsyncCore(statements, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _interpretNesting);
        }
    }

    private static void ResetProcessWideDeclarations()
    {
        SchemaRegistry.Clear();
        SumTypeRegistry.Clear();
        ApiRegistry.Clear();
    }

    private async Task InterpretAsyncCore(List<Statement> statements, CancellationToken cancellationToken)
    {
        // Pin this flow to the current (or root) activation so `async` forks restore correctly
        // even when AsyncLocal was unset on this thread.
        SetActivation(GetActivation());
        _interpretCancellation = cancellationToken;
        AnalyzeBuiltInShadowing(statements);
        var scriptFile = _currentFile ?? "main.malda";
        var scriptLine = statements.Count > 0 && statements[0].Line > 0 ? statements[0].Line : 1;
        var scriptFrame = new InterpreterCallStackFrame
        {
            FunctionName = "<script>",
            Line = scriptLine,
            File = scriptFile,
            Environment = _environment
        };
        _callStack.Add(scriptFrame);
        try
        {
            // For standalone script execution (CLI/tests), always treat each InterpretAsync call
            // as a fresh top-level run so that declarations are re-collected.
            // This also guarantees that prompt declarations are registered before use.
            _executionStack.Clear();
            GetActivation().CurrentFunctionFrame = null;
            
            // First pass: collect class, actor, function, prompt, workflow, and property declarations
            foreach (var stmt in statements)
            {
                if (stmt is ClassDeclaration classDecl)
                {
                    await DefineClassAsync(classDecl);
                }
                else if (stmt is ActorDeclaration actorDecl)
                {
                    await DefineActorAsync(actorDecl);
                }
                else if (stmt is FunctionDeclaration funcDecl)
                {
                    DefineFunction(funcDecl);
                }
                else if (stmt is PromptDeclaration promptDecl)
                {
                    DefinePrompt(promptDecl);
                }
                else if (stmt is TypeDeclaration typeDecl)
                {
                    DefineSumType(typeDecl);
                }
                else if (stmt is SchemaDeclaration schemaDecl)
                {
                    SchemaRegistry.Register(schemaDecl);
                }
                else if (stmt is ApiDeclaration apiDecl)
                {
                    MaldaLang.BuiltIns.ApiRegistry.Register(apiDecl);
                }
                else if (stmt is WorkflowDeclaration wfDecl)
                {
                    DefineWorkflow(wfDecl);
                }
                else if (stmt is PropertyDeclaration propertyDecl)
                {
                    DefineProperty(propertyDecl);
                }
                else if (stmt is ContextDeclaration contextDecl)
                {
                    DefineContext(contextDecl);
                }
                else if (stmt is PolicyDeclaration policyDecl)
                {
                    MaldaLang.Runtime.Policy.PolicyEngine.Install(policyDecl);
                }
            }
            
            // Create a fresh top-level frame for this execution
            var topLevelFrame = new TopLevelFrame
            {
                Statements = statements,
                Environment = _environment,
                StatementIndex = 0
            };
            _executionStack.Push(topLevelFrame);
            
            // Execute all non-declaration statements
            for (int i = 0; i < statements.Count; i++)
            {
                var stmt = statements[i];
                
                if (stmt is not ClassDeclaration && stmt is not FunctionDeclaration && stmt is not PromptDeclaration && stmt is not TypeDeclaration && stmt is not SchemaDeclaration && stmt is not ApiDeclaration && stmt is not WorkflowDeclaration && stmt is not PropertyDeclaration && stmt is not SuiteDeclaration && stmt is not ContextDeclaration && stmt is not PolicyDeclaration)
                {
                    topLevelFrame.StatementIndex = i;
                    await ExecuteAsync(stmt);
                }
            }
        }
        catch (BreakException)
        {
            throw;
        }
        catch (ContinueException)
        {
            throw;
        }
        catch (ReturnException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RuntimeException ex)
        {
            // v1: pause only on uncaught interpret exceptions. MALDA try/catch
            // handles RuntimeException before this outer catch.
            await PauseForUncaughtExceptionAsync(ex.Message, ex.Line, ex.File);
            throw;
        }
        catch (MALDAException ex)
        {
            await PauseForUncaughtExceptionAsync(ex.Message, ex.Line, ex.File);
            throw;
        }
        finally
        {
            _callStack.Remove(scriptFrame);
            _interpretCancellation = CancellationToken.None;
        }
    }

    /// <summary>
    /// v1: pause on uncaught <see cref="RuntimeException"/> / <see cref="MALDAException"/>
    /// only. Control-flow exceptions and cancel are not paused. After Continue,
    /// the original exception is rethrown so the run still fails.
    /// </summary>
    private async Task PauseForUncaughtExceptionAsync(string message, int? line, string? file)
    {
        if (_debuggerHook == null)
            return;

        var pauseLine = line is > 0 ? line.Value : (_callStack.Count > 0 ? _callStack[_callStack.Count - 1].Line : 1);
        var pauseFile = file ?? _currentFile;

        var session = _debuggerHook as DebugSession
            ?? (_debuggerHook as IHasDebugSession)?.Session;
        session?.PauseForUncaughtException(message, pauseLine, pauseFile);

        _debuggerHook.OnPause(pauseLine, pauseFile);
        await _debuggerHook.WaitIfPausedAsync(_interpretCancellation);
    }
    
    public void ResetExecutionState()
    {
        // Clear execution stack to reset execution state
        _executionStack.Clear();
        GetActivation().CurrentFunctionFrame = null;
    }
    
    public void SetCurrentActor(ActorInstance? actor)
    {
        _currentActor = actor;
        if (actor != null)
        {
            // Switch to actor's isolated environment
            _environment = actor.State;
        }
    }
    
    public ActorInstance? GetCurrentActor()
    {
        return _currentActor;
    }
    
    public ActorReference? GetSelfReference()
    {
        if (_currentActor == null)
            return null;
        
        return new ActorReference(_currentActor, _currentActor.Id);
    }

    internal Message? GetCurrentMessage()
    {
        return _currentMessage;
    }

    internal void SetCurrentMessage(Message? message)
    {
        _currentMessage = message;
    }

    internal async Task<bool> TryHandleCallbackAsync(Message message)
    {
        if (!message.CorrelationId.HasValue)
        {
            return false;
        }

        if (!_pendingCallbacks.TryGetValue(message.CorrelationId.Value, out var callbackInfo))
        {
            return false;
        }

        _pendingCallbacks.Remove(message.CorrelationId.Value);

        // Cancel timeout if it exists (reply arrived before timeout)
        callbackInfo.TimeoutCancellation?.Cancel();
        callbackInfo.TimeoutCancellation?.Dispose();

        // Create a new environment for the callback, enclosing the captured environment
        var callbackEnv = new Environment(callbackInfo.Environment);
        var callbackValue =
            (message.Arguments != null && message.Arguments.Count > 0)
                ? message.Arguments[0]
                : message.Payload;
        callbackEnv.Define(callbackInfo.ParameterName, callbackValue);

        await ExecuteBlockAsync(callbackInfo.Body, callbackEnv);
        return true;
    }
}
