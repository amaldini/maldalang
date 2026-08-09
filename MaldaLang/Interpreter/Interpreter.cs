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

/// <summary>Context when executing inside a workflow body (for step journaling and WF1001/WF1002 checks).</summary>
internal sealed class WorkflowExecutionContext
{
    public string InstanceId { get; }
    public string WorkflowName { get; }
    public Environment BodyEnv { get; }
    public List<CompensableStepRegistration> CompensableSteps { get; } = new();

    public WorkflowExecutionContext(string instanceId, string workflowName, Environment bodyEnv)
    {
        InstanceId = instanceId;
        WorkflowName = workflowName;
        BodyEnv = bodyEnv;
    }

    public void UpsertCompensableStep(string stepId, Expression compensateExpression)
    {
        CompensableSteps.RemoveAll(s => s.StepId == stepId);
        CompensableSteps.Add(new CompensableStepRegistration(stepId, compensateExpression));
    }
}

internal sealed class WorkflowPausedException : Exception
{
    public WorkflowPausedException(string message) : base(message) { }
}

internal sealed class CompensableStepRegistration
{
    public string StepId { get; }
    public Expression CompensateExpression { get; }

    public CompensableStepRegistration(string stepId, Expression compensateExpression)
    {
        StepId = stepId;
        CompensateExpression = compensateExpression;
    }
}

public partial class Interpreter
{
    internal Environment _globals;
    internal Environment _environment;
    internal BuiltIns.AgentInstance? _defaultAgent = null;
    internal Dictionary<string, ClassDefinition> _classes = new();
    internal Dictionary<string, ActorDefinition> _actors = new();
    internal Dictionary<string, WorkflowDeclaration> _workflows = new();
    internal Dictionary<string, PropertyDeclaration> _properties = new();
    private ObjectInstance? _currentObject = null;
    private ClassDefinition? _currentClass = null;
    private ActorInstance? _currentActor = null;
    private IDebuggerHook? _debuggerHook;
    private List<InterpreterCallStackFrame> _callStack = new();
    private string? _currentFile = null;
    private IInputProvider? _inputProvider;
    private Stack<ExecutionFrame> _executionStack = new();
    private Action? _outputUpdateCallback;
    private Action<string>? _outputCallback;
    private readonly Dictionary<Guid, CallbackInfo> _pendingCallbacks = new();
    private Message? _currentMessage;
    private readonly Dictionary<string, Environment> _importedModules = new();
    private WorkflowExecutionContext? _workflowContext;
    private bool _insideWorkflowStep;
    private MaldaLang.PackageManager.ModuleLoader? _moduleLoader;
    private MaldaLang.PackageManager.DotNetPackageWrapper? _dotNetWrapper;
    private string? _sourceCode = null;
    
    public Interpreter(IDebuggerHook? debuggerHook = null, string? currentFile = null, IInputProvider? inputProvider = null)
    {
        _globals = new Environment();
        _environment = _globals;
        _debuggerHook = debuggerHook;
        _currentFile = currentFile;
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
    }
    
    public void SetInputProvider(IInputProvider inputProvider)
    {
        _inputProvider = inputProvider;
    }
    
    public IInputProvider? GetInputProvider()
    {
        return _inputProvider;
    }
    
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
    
    public async Task InterpretAsync(List<Statement> statements)
    {
        try
        {
            // For standalone script execution (CLI/tests), always treat each InterpretAsync call
            // as a fresh top-level run so that declarations are re-collected.
            // This also guarantees that prompt declarations are registered before use.
            _executionStack.Clear();
            
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
                else if (stmt is WorkflowDeclaration wfDecl)
                {
                    DefineWorkflow(wfDecl);
                }
                else if (stmt is PropertyDeclaration propertyDecl)
                {
                    DefineProperty(propertyDecl);
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
                
                if (stmt is not ClassDeclaration && stmt is not FunctionDeclaration && stmt is not PromptDeclaration && stmt is not TypeDeclaration && stmt is not SchemaDeclaration && stmt is not WorkflowDeclaration && stmt is not PropertyDeclaration)
                {
                    topLevelFrame.StatementIndex = i;
                    await ExecuteAsync(stmt);
                }
            }
        }
        catch (RuntimeException)
        {
            throw;
        }
        catch (System.Exception)
        {
            throw;
        }
    }
    
    public void ResetExecutionState()
    {
        // Clear execution stack to reset execution state
        _executionStack.Clear();
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
    
    private async Task DefineActorAsync(ActorDeclaration decl)
    {
        var actorDef = new ActorDefinition(decl.Name);
        
        // Process members (fields, constructors, methods/handlers)
        foreach (var member in decl.Members)
        {
            if (member.Type == MemberType.Field)
            {
                actorDef.Fields[member.Name] = member;
                if (member.IsStatic && member.Value is Expression initExpr)
                {
                    actorDef.StaticFields[member.Name] = await EvaluateAsync(initExpr);
                }
            }
            else if (member.Type == MemberType.Method)
            {
                var funcDecl = (FunctionDeclaration)member.Value!;
                var handler = new FunctionValue(funcDecl, _globals, false, decl.Name);
                handler.Decorators = funcDecl.Decorators;
                handler.ParameterDecorators = funcDecl.ParameterDecorators;
                if (member.IsStatic)
                {
                    actorDef.StaticMethods[member.Name] = handler;
                }
                else
                {
                    // Message handler
                    actorDef.MessageHandlers[member.Name] = handler;
                }
            }
            else if (member.Type == MemberType.Constructor)
            {
                var funcDecl = (FunctionDeclaration)member.Value!;
                actorDef.Constructor = new FunctionValue(funcDecl, _globals, false, decl.Name);
            }
        }

        // Process actor message declarations (actor sugar)
        foreach (var message in decl.Messages)
        {
            if (actorDef.Messages.ContainsKey(message.Name))
            {
                throw new RuntimeException($"Duplicate message declaration '{message.Name}' in actor '{decl.Name}'.");
            }
            actorDef.Messages[message.Name] = message;
        }
        
        _actors[decl.Name] = actorDef;
    }
    
    private async Task<RuntimeValue> SpawnActorAsync(SpawnExpression expr)
    {
        var actorName = expr.ActorName;
        if (!_actors.ContainsKey(actorName))
        {
            throw new RuntimeException($"Actor '{actorName}' not defined.");
        }
        
        var actorDef = _actors[actorName];
        
        // Evaluate constructor arguments
        var constructorArgs = new List<RuntimeValue>();
        foreach (var argExpr in expr.Arguments)
        {
            constructorArgs.Add(await EvaluateAsync(argExpr));
        }
        
        // Spawn actor via ActorRuntime
        var actorRef = ActorRuntime.Instance.SpawnActor(actorDef, this, constructorArgs);
        
        return RuntimeValue.ActorReference(actorRef);
    }
    
    private async Task<RuntimeValue?> ExecuteSendAsync(SendStatement stmt)
    {
        await SendMessageAsync(stmt);
        return null;
    }
    
    private async Task SendMessageAsync(SendStatement stmt)
    {
        var targetValue = await EvaluateAsync(stmt.Target);
        if (targetValue.Type != ValueType.ActorReference)
        {
            throw new RuntimeException("Can only send messages to actor references.");
        }
        
        var targetRef = targetValue.AsActorReference();
        
        // Get self reference if we're in an actor context
        ActorReference? sender = GetSelfReference();

        // External actor stop is modeled as a built-in method on ActorReference in
        // transpiled mode; match that behavior in the interpreter instead of routing
        // it through actor message dispatch.
        if (stmt.HandlerName == "stop" && stmt.Arguments.Count == 0 && stmt.Callback == null)
        {
            targetRef.Stop();
            return;
        }

        // Determine if this send should use actor message-sugar semantics.
        // If the target actor has a declared message with this name, we treat
        // the send as constructing a tagged message value that can be handled
        // via `receive()` + pattern matching inside the actor.
        bool isMessageBasedSend = false;
        RuntimeValue payloadForMessage = RuntimeValue.Null();
        List<RuntimeValue>? argumentsForMessage = null;
        string? handlerNameForMessage = stmt.HandlerName;

        var argsForVariant = new List<RuntimeValue>();

        // Call-style send: send target.handlerName(args...) [then (result) { ... }]
        var argValues = new List<RuntimeValue>();
        foreach (var argExpr in stmt.Arguments)
        {
            argValues.Add(await EvaluateAsync(argExpr));
        }

        if (stmt.HandlerName != null)
        {
            var actorDef = targetRef.Instance.Actor;
            if (actorDef.Messages.TryGetValue(stmt.HandlerName, out _))
            {
                // Actor declares this message name: build a variant payload
                // with the same tag and the evaluated arguments as payload.
                isMessageBasedSend = true;
                payloadForMessage = RuntimeValue.Variant(stmt.HandlerName, argValues);
                argumentsForMessage = null; // payload carries all data for receive()
                handlerNameForMessage = null; // avoid routing through handler-based dispatch
            }
        }

        if (!isMessageBasedSend)
        {
            // Preserve existing semantics: payload is unused (null), arguments are separate.
            payloadForMessage = RuntimeValue.Null();
            argumentsForMessage = argValues;
            handlerNameForMessage = stmt.HandlerName;
        }

        Guid? correlationId = null;

        // If there is a callback, register it and use the message's Id as the correlation key
        if (stmt.Callback != null)
        {
            // We need to create the message explicitly to know its Id
            var msg = new Message(
                payloadForMessage,
                sender,
                handlerNameForMessage,
                null,
                argumentsForMessage);

            var callbackEnv = new Environment(_environment);
            
            // Evaluate timeout milliseconds if provided
            int? timeoutMs = null;
            if (stmt.TimeoutMilliseconds != null)
            {
                var timeoutValue = await EvaluateAsync(stmt.TimeoutMilliseconds);
                if (timeoutValue.Type == ValueType.Integer)
                {
                    timeoutMs = timeoutValue.AsInteger();
                }
                else if (timeoutValue.Type == ValueType.Float)
                {
                    timeoutMs = (int)timeoutValue.AsFloat();
                }
                else
                {
                    throw new RuntimeException("Timeout must be an integer (milliseconds).");
                }
            }
            
            CancellationTokenSource? timeoutCts = null;
            if (timeoutMs.HasValue && timeoutMs.Value > 0)
            {
                timeoutCts = new CancellationTokenSource();
                
                // Start timeout task
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(timeoutMs.Value, timeoutCts.Token);
                        
                        // Timeout occurred - try to remove callback and invoke error handler
                        if (_pendingCallbacks.TryGetValue(msg.Id, out var callbackInfo) && _pendingCallbacks.Remove(msg.Id))
                        {
                            timeoutCts.Dispose();
                            
                            if (callbackInfo.TimeoutErrorHandler != null && callbackInfo.TargetRef != null)
                            {
                                var senderInstance = sender?.Instance;
                                if (senderInstance != null)
                                {
                                    var errorMessage = RuntimeValue.String(
                                        $"Request to {callbackInfo.TargetRef}.{callbackInfo.HandlerName} timed out after {timeoutMs.Value}ms");
                                    
                                    // Create error message to invoke timeout error handler
                                    var errorMsg = new Message(
                                        errorMessage,
                                        null,
                                        null,
                                        null,
                                        new List<RuntimeValue> { errorMessage });
                                    
                                    // Invoke timeout error handler in sender's actor context
                                    var previousActor = _currentActor;
                                    SetCurrentActor(senderInstance);
                                    
                                    try
                                    {
                                        var errorHandlerEnv = new Environment(callbackInfo.Environment);
                                        errorHandlerEnv.Define(
                                            callbackInfo.TimeoutErrorHandler.ParameterName, 
                                            errorMessage);
                                        await ExecuteBlockAsync(callbackInfo.TimeoutErrorHandler.Body, errorHandlerEnv);
                                    }
                                    finally
                                    {
                                        SetCurrentActor(previousActor);
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout was cancelled (reply arrived), ignore
                        timeoutCts?.Dispose();
                    }
                });
            }
            
            _pendingCallbacks[msg.Id] = new CallbackInfo(
                stmt.Callback.ParameterName, 
                stmt.Callback.Body, 
                callbackEnv,
                timeoutMs,
                stmt.TimeoutErrorHandler,
                targetRef,
                stmt.HandlerName,
                timeoutCts);

            // Send message directly to target actor's mailbox using its instance
            targetRef.Instance.Mailbox.Send(msg);
        }
        else
        {
            // Fire-and-forget call-style send without callback
            targetRef.Send(payloadForMessage, sender, handlerNameForMessage, correlationId: null, arguments: argumentsForMessage);
        }
    }
    
    private RuntimeValue EvaluateSelf()
    {
        var selfRef = GetSelfReference();
        if (selfRef == null)
        {
            throw new RuntimeException("'self' can only be used within an actor message handler.");
        }
        return RuntimeValue.ActorReference(selfRef);
    }
    
    private async Task<RuntimeValue> ReceiveMessageAsync()
    {
        if (_currentActor == null)
        {
            throw new RuntimeException("receive() can only be called from within an actor message handler.");
        }

        if (_currentMessage != null &&
            !_currentMessage.ReceiveConsumed &&
            _currentMessage.Arguments != null &&
            _currentMessage.Arguments.Count > 0)
        {
            _currentMessage.ReceiveConsumed = true;
            return _currentMessage.Arguments[0];
        }

        var message = await _currentActor.Mailbox.ReceiveAsync(CancellationToken.None);
        // When using actor-sugar with receive() loops, the message consumed here
        // becomes the current message for built-ins like reply().
        SetCurrentMessage(message);

        // Prefer the first argument (if any) as the received value, falling back
        // to the payload. This mirrors callback handling semantics and makes
        // `send target(value)` compatible with `receive()`.
        if (message.Arguments != null && message.Arguments.Count > 0)
        {
            return message.Arguments[0];
        }

        return message.Payload;
    }
    
    private async Task DefineClassAsync(ClassDeclaration decl)
    {
        ClassDefinition? superclass = null;
        if (decl.Superclass != null)
        {
            if (!_classes.ContainsKey(decl.Superclass))
                throw new RuntimeException($"Superclass '{decl.Superclass}' not found.");
            superclass = _classes[decl.Superclass];
        }
        
        var klass = new ClassDefinition(decl.Name, superclass);
        
        // Process members
        foreach (var member in decl.Members)
        {
            if (member.Type == MemberType.Field)
            {
                klass.Fields[member.Name] = member;
                if (member.IsStatic && member.Value is Expression initExpr)
                {
                    klass.StaticFields[member.Name] = await EvaluateAsync(initExpr);
                }
            }
            else if (member.Type == MemberType.Method)
            {
                var funcDecl = (FunctionDeclaration)member.Value!;
                var method = new FunctionValue(funcDecl, _globals, false, decl.Name);
                method.Decorators = funcDecl.Decorators;
                method.ParameterDecorators = funcDecl.ParameterDecorators;
                if (member.IsStatic)
                {
                    klass.StaticMethods[member.Name] = method;
                    klass.StaticMethodAccess[member.Name] = member.Access;
                }
                else
                {
                    klass.Methods[member.Name] = method;
                    klass.MethodAccess[member.Name] = member.Access;
                }
            }
            else if (member.Type == MemberType.Constructor)
            {
                var funcDecl = (FunctionDeclaration)member.Value!;
                var constructor = new FunctionValue(funcDecl, _globals, true, decl.Name);
                constructor.Decorators = funcDecl.Decorators;
                constructor.ParameterDecorators = funcDecl.ParameterDecorators;
                klass.Constructor = constructor;
            }
        }
        
        _classes[decl.Name] = klass;
        _globals.Define(decl.Name, RuntimeValue.Class(klass));
    }
    
    private void DefineFunction(FunctionDeclaration decl)
    {
        // Capture the global environment for function closure
        // This ensures functions can access other functions and global variables
        var function = new FunctionValue(decl, _globals);
        function.Decorators = decl.Decorators;
        function.ParameterDecorators = decl.ParameterDecorators;
        // Always define functions in the global environment to ensure they're accessible
        _globals.Define(decl.Name, RuntimeValue.Function(function));
        
        // Check for @Tool decorator and register tool
        if (decl.Decorators != null)
        {
            var toolDecorator = decl.Decorators.FirstOrDefault(d => d.Name == "Tool");
            if (toolDecorator != null)
            {
                RegisterToolFromDecorator(function, toolDecorator, decl);
            }
        }
    }
    
    private void DefinePrompt(PromptDeclaration decl)
    {
        // Capture the global environment for prompt closure
        // This ensures prompts can access other prompts, functions, and global variables
        var prompt = new PromptValue(decl, _globals);
        // Always define prompts in the global environment to ensure they're accessible
        _globals.Define(decl.Name, RuntimeValue.Prompt(prompt));
    }

    private void DefineWorkflow(WorkflowDeclaration decl)
    {
        _workflows[decl.Name] = decl;
    }

    private void DefineProperty(PropertyDeclaration decl)
    {
        if (_properties.ContainsKey(decl.Name))
        {
            throw new RuntimeException($"Property '{decl.Name}' is already defined");
        }

        _properties[decl.Name] = decl;
    }

    internal WorkflowDeclaration? GetWorkflow(string name) => _workflows.TryGetValue(name, out var w) ? w : null;
    internal PropertyDeclaration? GetProperty(string name) => _properties.TryGetValue(name, out var p) ? p : null;

    /// <summary>Runs a workflow body with the given input and instance ID. Used by startWorkflow built-in.</summary>
    internal async Task<RuntimeValue?> RunWorkflowBodyAsync(WorkflowDeclaration decl, RuntimeValue input, string instanceId)
    {
        var env = new Environment(_globals);
        if (decl.Parameters.Count > 0)
            env.Define(decl.Parameters[0], input);
        var prevCtx = _workflowContext;
        _workflowContext = new WorkflowExecutionContext(instanceId, decl.Name, env);
        var engine = WorkflowEngine.Instance;
        try
        {
            var prevEnv = _environment;
            _environment = env;
            try
            {
                // Execute workflow body statements directly (no BlockFrame) so step results land in env.
                foreach (var st in decl.Body.Statements)
                    await ExecuteAsync(st);
                engine.CompleteInstance(instanceId, null);
                return null;
            }
            catch (WorkflowPausedException)
            {
                // Expected pause point for approval/signal waits; instance remains in waiting state.
                return null;
            }
            catch (ReturnException ret)
            {
                var resultJson = ret.Value != null ? BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { ret.Value }, this).AsString() : null;
                engine.CompleteInstance(instanceId, resultJson);
                return ret.Value;
            }
            catch (Exception ex)
            {
                var err = System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message, type = ex.GetType().Name });
                var compensated = await ExecuteCompensationAsync(_workflowContext!, err);
                if (!compensated)
                    engine.FailInstance(instanceId, err);
                throw;
            }
            finally
            {
                _environment = prevEnv;
            }
        }
        finally
        {
            _workflowContext = prevCtx;
        }
    }

    internal bool IsInWorkflowContext => _workflowContext != null;
    internal bool IsInsideWorkflowStep => _insideWorkflowStep;

    private void EnsureWorkflowContext()
    {
        if (_workflowContext == null)
            throw new RuntimeException("Workflow step can only be executed inside a workflow body");
    }

    private async Task<RuntimeValue?> ExecuteWorkflowStepAsync(WorkflowStepStatement stmt)
    {
        EnsureWorkflowContext();
        var ctx = _workflowContext!;
        var engine = WorkflowEngine.Instance;
        var maxAttempts = (stmt.Options?.RetryCount ?? 0) + 1;
        if (maxAttempts < 1) maxAttempts = 1;
        var timeoutMs = stmt.Options?.TimeoutMs;
        var backoff = stmt.Options?.Backoff;
        var delayMs = stmt.Options?.DelayMs;
        var maxDelayMs = stmt.Options?.MaxDelayMs;

        // Define step result in workflow body env so it is visible to subsequent statements (print, return).
        var targetEnv = ctx.BodyEnv;

        // Replay contract: if prior successful result exists, use it (no re-exec)
        var replay = engine.GetReplayResult(ctx.InstanceId, stmt.StepId);
        if (replay != null)
        {
            var json = replay.OutputJson ?? "null";
            var parsed = BuiltInFunctions.CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(json) }, this);
            targetEnv.Define(stmt.StepId, parsed);
            if (stmt.Options?.Compensate != null && replay.State == StepState.Succeeded)
                ctx.UpsertCompensableStep(stmt.StepId, stmt.Options.Compensate);
            return null;
        }

        var latestAttempt = engine.GetLatestStepAttempt(ctx.InstanceId, stmt.StepId);
        var attempt = latestAttempt != null ? latestAttempt.Attempt + 1 : 1;
        while (attempt <= maxAttempts)
        {
            var stepId = Guid.NewGuid().ToString("N");
            engine.JournalStepStart(stepId, ctx.InstanceId, stmt.StepId, attempt, maxAttempts, timeoutMs, "{}", null);

            try
            {
                _insideWorkflowStep = true;
                var (timedOut, result) = await EvaluateWorkflowStepWithTimeoutAsync(stmt.CallExpression, timeoutMs);
                if (timedOut)
                {
                    var timeoutError = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        type = "StepTimeoutError",
                        step = stmt.StepId,
                        attempt,
                        timeoutMs,
                        isRetryable = attempt < maxAttempts,
                        message = $"Step '{stmt.StepId}' timed out after {timeoutMs}ms"
                    });
                    engine.JournalStepTimeout(stepId, ctx.InstanceId, stmt.StepId, attempt, timeoutError);
                    if (attempt >= maxAttempts)
                    {
                        throw new RuntimeException($"Step '{stmt.StepId}' timed out after {timeoutMs}ms (max attempts reached)");
                    }

                    var retryDelayMs = engine.ComputeRetryDelayMs(ctx.InstanceId, stmt.StepId, attempt, backoff, delayMs, maxDelayMs);
                    engine.JournalStepRetryScheduled(ctx.InstanceId, stmt.StepId, attempt, attempt + 1, retryDelayMs, "timeout");
                    if (retryDelayMs > 0)
                        await Task.Delay(retryDelayMs);
                    attempt++;
                    continue;
                }

                var outputJson = BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { result! }, this).AsString();
                engine.JournalStepSuccess(stepId, ctx.InstanceId, stmt.StepId, attempt, outputJson);
                targetEnv.Define(stmt.StepId, result!);
                if (stmt.Options?.Compensate != null)
                    ctx.UpsertCompensableStep(stmt.StepId, stmt.Options.Compensate);
                return null;
            }
            catch (Exception ex)
            {
                var err = System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message, type = ex.GetType().Name, attempt, isRetryable = attempt < maxAttempts });
                engine.JournalStepFailure(stepId, ctx.InstanceId, stmt.StepId, attempt, err);
                if (attempt >= maxAttempts)
                    throw;

                var retryDelayMs = engine.ComputeRetryDelayMs(ctx.InstanceId, stmt.StepId, attempt, backoff, delayMs, maxDelayMs);
                engine.JournalStepRetryScheduled(ctx.InstanceId, stmt.StepId, attempt, attempt + 1, retryDelayMs, "failure");
                if (retryDelayMs > 0)
                    await Task.Delay(retryDelayMs);
                attempt++;
            }
            finally
            {
                _insideWorkflowStep = false;
            }
        }

        throw new RuntimeException($"Step '{stmt.StepId}' failed after {maxAttempts} attempts");
    }

    private async Task<(bool timedOut, RuntimeValue? result)> EvaluateWorkflowStepWithTimeoutAsync(Expression expression, int? timeoutMs)
    {
        if (!timeoutMs.HasValue || timeoutMs.Value <= 0)
        {
            var result = await EvaluateAsync(expression);
            return (false, result);
        }

        var stepTask = EvaluateAsync(expression);
        var completed = await Task.WhenAny(stepTask, Task.Delay(timeoutMs.Value));
        if (completed != stepTask)
            return (true, null);

        return (false, await stepTask);
    }

    private async Task<RuntimeValue?> ExecuteWorkflowApprovalAsync(WorkflowApprovalStatement stmt)
    {
        EnsureWorkflowContext();
        var ctx = _workflowContext!;
        var engine = WorkflowEngine.Instance;
        var targetEnv = ctx.BodyEnv;

        var latest = engine.GetLatestStepAttempt(ctx.InstanceId, stmt.ApprovalId);
        if (latest != null)
        {
            if (latest.State == StepState.Succeeded && !string.IsNullOrWhiteSpace(latest.OutputJson))
            {
                var resolved = BuiltInFunctions.CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(latest.OutputJson) }, this);
                targetEnv.Define(stmt.ApprovalId, resolved);
                var decision = TryGetStringFieldFromJson(latest.OutputJson, "decision");
                if (decision == "reject")
                {
                    if (stmt.OnReject != null)
                    {
                        _insideWorkflowStep = true;
                        try
                        {
                            await EvaluateAsync(stmt.OnReject);
                        }
                        finally
                        {
                            _insideWorkflowStep = false;
                        }
                    }
                    else
                    {
                        throw new RuntimeException($"Approval '{stmt.ApprovalId}' was rejected.");
                    }
                }

                if (decision == "timeout")
                    throw new RuntimeException($"Approval '{stmt.ApprovalId}' timed out.");

                return null;
            }

            if (latest.State == StepState.TimedOut)
                throw new RuntimeException($"Approval '{stmt.ApprovalId}' timed out.");
        }

        var approvalNameValue = await EvaluateAsync(stmt.ApprovalNameExpr);
        var approvalName = approvalNameValue.Type == ValueType.String ? approvalNameValue.AsString() : approvalNameValue.ToString();
        var payload = await EvaluateAsync(stmt.PayloadExpr);
        var payloadJson = BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { payload }, this).AsString();
        if (!engine.EnterApprovalWait(ctx.InstanceId, stmt.ApprovalId, approvalName, stmt.TimeoutMs, payloadJson, out var enterError))
            throw new RuntimeException(enterError ?? $"Failed to enter approval wait for '{stmt.ApprovalId}'.");

        if (latest != null && latest.State == StepState.Running && IsWaitTimedOut(latest.StartedAtUtc, stmt.TimeoutMs))
        {
            if (engine.TimeoutWaitingStep(ctx.InstanceId, stmt.ApprovalId, "approval", "ApprovalTimeoutError", out var timeoutError))
                throw new RuntimeException($"Approval '{stmt.ApprovalId}' timed out.");
            throw new RuntimeException(timeoutError ?? $"Approval '{stmt.ApprovalId}' wait timeout handling failed.");
        }

        throw new WorkflowPausedException($"Workflow paused waiting for approval '{stmt.ApprovalId}'.");
    }

    private async Task<RuntimeValue?> ExecuteWorkflowAwaitSignalAsync(WorkflowAwaitSignalStatement stmt)
    {
        EnsureWorkflowContext();
        var ctx = _workflowContext!;
        var engine = WorkflowEngine.Instance;
        var targetEnv = ctx.BodyEnv;

        var latest = engine.GetLatestStepAttempt(ctx.InstanceId, stmt.SignalId);
        if (latest != null)
        {
            if (latest.State == StepState.Succeeded && !string.IsNullOrWhiteSpace(latest.OutputJson))
            {
                var payloadFieldJson = TryGetJsonField(latest.OutputJson, "payload") ?? "null";
                var payloadValue = BuiltInFunctions.CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(payloadFieldJson) }, this);
                targetEnv.Define(stmt.SignalId, payloadValue);
                return null;
            }

            if (latest.State == StepState.TimedOut)
                throw new RuntimeException($"Signal wait '{stmt.SignalId}' timed out.");
        }

        var signalNameValue = await EvaluateAsync(stmt.SignalNameExpr);
        var signalName = signalNameValue.Type == ValueType.String ? signalNameValue.AsString() : signalNameValue.ToString();
        var correlation = await EvaluateAsync(stmt.PayloadExpr);
        var correlationJson = BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { correlation }, this).AsString();
        if (!engine.EnterSignalWait(ctx.InstanceId, stmt.SignalId, signalName, stmt.TimeoutMs, correlationJson, out var enterError))
            throw new RuntimeException(enterError ?? $"Failed to enter signal wait for '{stmt.SignalId}'.");

        if (latest != null && latest.State == StepState.Running && IsWaitTimedOut(latest.StartedAtUtc, stmt.TimeoutMs))
        {
            if (engine.TimeoutWaitingStep(ctx.InstanceId, stmt.SignalId, "signal_wait", "SignalTimeoutError", out var timeoutError))
                throw new RuntimeException($"Signal wait '{stmt.SignalId}' timed out.");
            throw new RuntimeException(timeoutError ?? $"Signal wait '{stmt.SignalId}' timeout handling failed.");
        }

        throw new WorkflowPausedException($"Workflow paused waiting for signal '{stmt.SignalId}'.");
    }

    private async Task<bool> ExecuteCompensationAsync(WorkflowExecutionContext ctx, string rootErrorJson)
    {
        if (ctx.CompensableSteps.Count == 0)
            return false;

        var engine = WorkflowEngine.Instance;
        engine.BeginCompensation(ctx.InstanceId, rootErrorJson);
        var allSucceeded = true;
        var diagnostics = new List<Dictionary<string, object?>>();

        for (var i = ctx.CompensableSteps.Count - 1; i >= 0; i--)
        {
            var compensable = ctx.CompensableSteps[i];
            var compensationStepName = $"{compensable.StepId}__compensate";
            var replay = engine.GetLatestStepAttempt(ctx.InstanceId, compensationStepName);
            if (replay != null && replay.State == StepState.Compensated)
                continue;

            var latestAttempt = engine.GetLatestStepAttempt(ctx.InstanceId, compensationStepName);
            var attempt = latestAttempt != null ? latestAttempt.Attempt + 1 : 1;
            var compensationStepId = Guid.NewGuid().ToString("N");
            engine.JournalCompensationStart(compensationStepId, ctx.InstanceId, compensationStepName, attempt, "{}");

            try
            {
                _insideWorkflowStep = true;
                var result = await EvaluateAsync(compensable.CompensateExpression);
                var outputJson = BuiltInFunctions.CallBuiltIn("toJSON", new List<RuntimeValue> { result }, this).AsString();
                engine.JournalCompensationSuccess(compensationStepId, ctx.InstanceId, compensationStepName, attempt, outputJson);
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                var compError = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "CompensationError",
                    step = compensable.StepId,
                    compensationStep = compensationStepName,
                    attempt,
                    message = ex.Message
                });
                diagnostics.Add(new Dictionary<string, object?>
                {
                    ["step"] = compensable.StepId,
                    ["compensationStep"] = compensationStepName,
                    ["attempt"] = attempt,
                    ["message"] = ex.Message
                });
                engine.JournalCompensationFailure(compensationStepId, ctx.InstanceId, compensationStepName, attempt, compError);
            }
            finally
            {
                _insideWorkflowStep = false;
            }
        }

        var diagnosticsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "CompensationSummary",
            allSucceeded,
            failures = diagnostics
        });
        engine.FinishCompensation(ctx.InstanceId, allSucceeded, diagnosticsJson);
        return true;
    }

    private static bool IsWaitTimedOut(string? startedAtUtc, int? timeoutMs)
    {
        if (!timeoutMs.HasValue || timeoutMs.Value <= 0 || string.IsNullOrWhiteSpace(startedAtUtc))
            return false;
        if (!DateTime.TryParse(startedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
            return false;
        if (started.Kind == DateTimeKind.Unspecified)
            started = DateTime.SpecifyKind(started, DateTimeKind.Utc);
        return (DateTime.UtcNow - started.ToUniversalTime()).TotalMilliseconds >= timeoutMs.Value;
    }

    private static string? TryGetStringFieldFromJson(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(fieldName, out var field) && field.ValueKind == JsonValueKind.String)
                return field.GetString();
        }
        catch
        {
            // ignore malformed payloads and let caller apply fallback behavior
        }

        return null;
    }

    private static string? TryGetJsonField(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(fieldName, out var field))
                return field.GetRawText();
        }
        catch
        {
            // ignore malformed payloads and let caller apply fallback behavior
        }

        return null;
    }

    private void DefineSumType(TypeDeclaration decl)
    {
        foreach (var ctor in decl.Constructors)
        {
            var fv = new FunctionValue(null, null)
            {
                VariantConstructorTag = ctor.Name,
                VariantConstructorArity = ctor.ParameterNames.Count
            };
            _globals.Define(ctor.Name, RuntimeValue.Function(fv));
        }
    }

    private void RegisterToolFromDecorator(FunctionValue function, Decorator decorator, FunctionDeclaration decl)
    {
        try
        {
            // @Tool decorator should have at least 2 arguments: name and description
            if (decorator.Arguments == null || decorator.Arguments.Count < 2)
            {
                throw new RuntimeException($"@Tool decorator requires at least 2 arguments: name and description");
            }
            
            // Evaluate name (first argument)
            var nameValue = EvaluateDecoratorArgumentSync(decorator.Arguments[0]);
            if (nameValue.Type != ValueType.String)
            {
                throw new RuntimeException("@Tool decorator first argument (name) must be a string");
            }
            var toolName = nameValue.AsString();
            
            // Evaluate description (second argument)
            var descValue = EvaluateDecoratorArgumentSync(decorator.Arguments[1]);
            if (descValue.Type != ValueType.String)
            {
                throw new RuntimeException("@Tool decorator second argument (description) must be a string");
            }
            var toolDescription = descValue.AsString();
            
            // Evaluate optional schema (third argument, if present)
            RuntimeValue? schema = null;
            if (decorator.Arguments.Count >= 3)
            {
                try
                {
                    schema = EvaluateDecoratorArgumentSync(decorator.Arguments[2]);
                    // If it's a string, try to parse it as JSON
                    if (schema.Type == ValueType.String)
                    {
                        var jsonStr = schema.AsString();
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                            schema = JsonToRuntimeValue(doc.RootElement);
                        }
                        catch
                        {
                            throw new RuntimeException("@Tool decorator third argument (schema) must be a valid JSON object string or will be auto-generated");
                        }
                    }
                    else if (schema.Type != ValueType.Object)
                    {
                        throw new RuntimeException("@Tool decorator third argument (schema) must be an object or JSON string");
                    }
                }
                catch (RuntimeException)
                {
                    // If evaluation fails, schema will be auto-generated
                    schema = null;
                }
            }
            
            // Generate schema if not provided
            var finalSchema = ToolSchemaGenerator.GenerateSchema(decl, schema);
            
            // Create ToolInstance
            var tool = new ToolInstance();
            tool.Initialize(toolName, toolDescription, finalSchema, null, "");
            tool.SetFunctionHandler(function, this);
            
            // Register in ToolRegistry
            ToolRegistry.Instance.RegisterTool(tool);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"Error registering tool from @Tool decorator: {ex.Message}", decl.Line, _currentFile);
        }
    }
    
    private RuntimeValue JsonToRuntimeValue(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var jsonObj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    jsonObj.Set(prop.Name, JsonToRuntimeValue(prop.Value));
                }
                return RuntimeValue.Object(jsonObj);
            
            case System.Text.Json.JsonValueKind.Array:
                var arr = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(JsonToRuntimeValue(item));
                }
                return RuntimeValue.Array(arr);
            
            case System.Text.Json.JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            
            case System.Text.Json.JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return RuntimeValue.Integer(intVal);
                return RuntimeValue.Float(element.GetDouble());
            
            case System.Text.Json.JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            
            case System.Text.Json.JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            
            case System.Text.Json.JsonValueKind.Null:
                return RuntimeValue.Null();
            
            default:
                return RuntimeValue.Null();
        }
    }
    
    private RuntimeValue EvaluateDecoratorArgumentSync(Expression expr)
    {
        // Handle literal expressions
        if (expr is LiteralExpression literal)
        {
            return RuntimeValueFromLiteral(literal);
        }
        
        // For object literals, we'd need to parse them, but MALDA doesn't have object literal syntax
        // Instead, schema should be provided as a JSON string that can be parsed, or we auto-generate
        // For now, we only support string literals for name/description
        // Schema will be auto-generated if not provided as a simple object
        throw new RuntimeException($"Decorator argument must be a literal (string, number, boolean, null). For schema, use auto-generation or provide via other means.");
    }
    
    internal async Task<RuntimeValue?> ExecuteAsync(Statement stmt)
    {
        var previousFile = _currentFile;
        var statementFile = stmt.SourceFile ?? _currentFile;
        _currentFile = statementFile;
        var profileToken = MaldaProfiler.EnterStatement(statementFile, stmt.Line, GetStatementProfileName(stmt));
        // Check debugger hook before executing
        try
        {
            if (_debuggerHook != null)
            {
                // Convert statement line from 1-based (token line) to 0-based (breakpoint line)
                // Tokens use 1-based line numbers, but breakpoints are stored as 0-based
                var statementLine = stmt.Line - 1;
                if (!_debuggerHook.OnStatement(statementLine, _currentFile))
                {
                    // Execution should pause - notify debugger and wait
                    // Use 1-based line for display (original statement line)
                    _debuggerHook.OnPause(stmt.Line, _currentFile);
                    
                    // Wait for debugger to continue (with timeout to avoid infinite wait)
                    var timeout = DateTime.Now.AddSeconds(300); // 5 minute timeout
                    while (_debuggerHook.GetDebugMode() == DebugMode.Paused && DateTime.Now < timeout)
                    {
                        await Task.Delay(50); // Use async delay instead of Thread.Sleep
                    }
                }
            }

            return stmt switch
            {
                VarDeclStatement varDecl => await ExecuteVarDeclAsync(varDecl),
                DestructuringVarDecl destVarDecl => await ExecuteDestructuringVarDeclAsync(destVarDecl),
                AssignmentStatement assign => await ExecuteAssignmentAsync(assign),
                DestructuringAssignment destAssign => await ExecuteDestructuringAssignmentAsync(destAssign),
                ExpressionStatement expr => await ExecuteExpressionAsync(expr),
                PrintStatement print => await ExecutePrintAsync(print),
                BlockStatement block => await ExecuteBlockAsync(block),
                IfStatement ifStmt => await ExecuteIfAsync(ifStmt),
                WhileStatement whileStmt => await ExecuteWhileAsync(whileStmt),
                ForStatement forStmt => await ExecuteForAsync(forStmt),
                ForInStatement forInStmt => await ExecuteForInAsync(forInStmt),
                ReturnStatement returnStmt => await ExecuteReturnAsync(returnStmt),
                BreakStatement => throw new BreakException(),
                ContinueStatement => throw new ContinueException(),
                TryStatement tryStmt => await ExecuteTryAsync(tryStmt),
                ThrowStatement throwStmt => await ExecuteThrowAsync(throwStmt),
                SendStatement sendStmt => await ExecuteSendAsync(sendStmt),
                UsingStatement usingStmt => await ExecuteUsingAsync(usingStmt),
                UsingResourceStatement usingResource => await ExecuteUsingResourceAsync(usingResource),
                DeferStatement deferStmt => ExecuteDefer(deferStmt),
                ImportStatement importStmt => await ExecuteImportAsync(importStmt),
                FunctionDeclaration funcDecl => null, // Already handled
                ClassDeclaration classDecl => null, // Already handled
                ActorDeclaration actorDecl => null, // Already handled
                PromptDeclaration => null, // Already handled
                TypeDeclaration => null, // Already handled
                SchemaDeclaration => null, // Already handled
                WorkflowDeclaration => null, // Already handled in declaration pass
                PropertyDeclaration => null, // Already handled in declaration pass
                WorkflowStepStatement stepStmt => await ExecuteWorkflowStepAsync(stepStmt),
                WorkflowApprovalStatement approvalStmt => await ExecuteWorkflowApprovalAsync(approvalStmt),
                WorkflowAwaitSignalStatement awaitSignalStmt => await ExecuteWorkflowAwaitSignalAsync(awaitSignalStmt),
                _ => throw new RuntimeException($"Unknown statement type: {stmt.GetType()}")
            };
        }
        finally
        {
            MaldaProfiler.Exit(profileToken);
            _currentFile = previousFile;
        }
    }
    
    private async Task<RuntimeValue?> ExecuteExpressionAsync(ExpressionStatement stmt)
    {
        await EvaluateAsync(stmt.Expression);
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteVarDeclAsync(VarDeclStatement stmt)
    {
        var value = await EvaluateAsync(stmt.Initializer);
        _environment.Define(stmt.Name, value, stmt.IsConst);
        return null;
    }

    private void EnsureMutableIdentifier(string name, int line)
    {
        if (_environment.IsConst(name))
            throw new RuntimeException($"Cannot assign to const '{name}'.", line, _currentFile);
    }
    
    private async Task<RuntimeValue?> ExecuteAssignmentAsync(AssignmentStatement stmt)
    {
        RuntimeValue value;
        
        // Handle compound assignment operators
        if (stmt.Operator != TokenType.Assign)
        {
            // Get current value of target
            var currentValue = await GetLvalueAsync(stmt.Target);
            var rightValue = await EvaluateAsync(stmt.Value);
            
            // Perform the operation
            switch (stmt.Operator)
            {
                case TokenType.PlusAssign:
                    value = EvaluatePlus(currentValue, rightValue, stmt.Line);
                    break;
                case TokenType.MinusAssign:
                    value = EvaluateMinus(currentValue, rightValue, stmt.Line);
                    break;
                case TokenType.MultiplyAssign:
                    value = EvaluateMultiply(currentValue, rightValue, stmt.Line);
                    break;
                case TokenType.DivideAssign:
                    value = EvaluateDivide(currentValue, rightValue, stmt.Line);
                    break;
                default:
                    throw new RuntimeException($"Unknown compound assignment operator: {stmt.Operator}", stmt.Line, _currentFile);
            }
        }
        else
        {
            // Regular assignment
            value = await EvaluateAsync(stmt.Value);
        }
        
        // Use the helper method to set the value
        await SetLvalueAsync(stmt.Target, value);
        
        return null;
    }
    
    private async Task<RuntimeValue?> ExecutePrintAsync(PrintStatement stmt)
    {
        var value = await EvaluateAsync(stmt.Expression);
        var text = value.ToString();
        if (_outputCallback != null)
            _outputCallback(text);
        else
            Console.WriteLine(text);
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteBlockAsync(BlockStatement stmt)
    {
        return await ExecuteBlockAsync(stmt, new Environment(_environment));
    }
    
    private async Task<RuntimeValue?> ExecuteBlockAsync(BlockStatement stmt, Environment environment)
    {
        // Check if we're resuming a block execution
        BlockFrame? existingFrame = null;
        if (_executionStack.Count > 0 && _executionStack.Peek() is BlockFrame frame && frame.Statement == stmt)
        {
            existingFrame = frame;
        }
        
        BlockFrame blockFrame;
        if (existingFrame != null)
        {
            // Resuming - use existing frame
            blockFrame = existingFrame;
            environment = blockFrame.Environment;
        }
        else
        {
            // New block - create frame
            blockFrame = new BlockFrame
            {
                Statement = stmt,
                Environment = environment,
                StatementIndex = 0
            };
            _executionStack.Push(blockFrame);
        }
        
        var previous = _environment;
        PushDeferFrame();
        try
        {
            _environment = environment;
            
            // Execute statements starting from saved index
            for (int i = blockFrame.StatementIndex; i < stmt.Statements.Count; i++)
            {
                blockFrame.StatementIndex = i;
                await ExecuteAsync(stmt.Statements[i]);
            }
            
            // Block completed - pop frame
            if (_executionStack.Count > 0 && _executionStack.Peek() == blockFrame)
            {
                _executionStack.Pop();
            }
        }
        finally
        {
            await RunAndPopDeferFrameAsync();
            _environment = previous;
        }
        return null;
    }

    private RuntimeValue? ExecuteDefer(DeferStatement defer)
    {
        RegisterDeferAction(defer);
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteIfAsync(IfStatement stmt)
    {
        if ((await EvaluateAsync(stmt.Condition)).IsTruthy())
        {
            await ExecuteAsync(stmt.ThenBranch);
        }
        else if (stmt.ElseBranch != null)
        {
            await ExecuteAsync(stmt.ElseBranch);
        }
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteWhileAsync(WhileStatement stmt)
    {
        // Check if we're resuming from an existing frame
        WhileLoopFrame? existingFrame = null;
        if (_executionStack.Count > 0 && _executionStack.Peek() is WhileLoopFrame frame && frame.Statement == stmt)
        {
            existingFrame = frame;
        }
        
        WhileLoopFrame whileFrame;
        if (existingFrame != null)
        {
            // Resuming - use existing frame
            whileFrame = existingFrame;
        }
        else
        {
            // New loop - create frame
            whileFrame = new WhileLoopFrame
            {
                Statement = stmt,
                Environment = _environment,
                StatementIndex = 0,
                LoopIteration = 0,
                ConditionEvaluated = false
            };
            
            // Check if body is a block for statement tracking
            if (stmt.Body is BlockStatement bodyBlock)
            {
                whileFrame.BodyBlock = bodyBlock;
            }
            
            _executionStack.Push(whileFrame);
        }
        
        try
        {
            // If resuming (StatementIndex > 0), continue from where we left off without re-evaluating condition
            if (whileFrame.StatementIndex > 0)
            {
                // Resuming: continue from saved StatementIndex
                // Execute from StatementIndex (where we left off)
                if (whileFrame.BodyBlock != null)
                {
                    // Execute block statements from StatementIndex
                    for (int i = whileFrame.StatementIndex; i < whileFrame.BodyBlock.Statements.Count; i++)
                    {
                        whileFrame.StatementIndex = i;
                        await ExecuteAsync(whileFrame.BodyBlock.Statements[i]);
                    }
                    // Iteration completed - reset statement index for next iteration
                    whileFrame.StatementIndex = 0;
                }
                else
                {
                    // Single statement body - already executed, so this shouldn't happen
                    // But if it does, just reset
                    whileFrame.StatementIndex = 0;
                }
            }
            
            // Continue looping (or start new loop if StatementIndex was 0)
            // Keep looping until condition is false
            while ((await EvaluateAsync(stmt.Condition)).IsTruthy())
            {
                // Only increment LoopIteration for new iterations (StatementIndex == 0)
                if (whileFrame.StatementIndex == 0)
                {
                    whileFrame.LoopIteration++;
                }
                whileFrame.ConditionEvaluated = true;
                
                try
                {
                    // Execute from StatementIndex (0 for new iteration)
                    if (whileFrame.BodyBlock != null)
                    {
                        // Execute block statements from StatementIndex
                        for (int i = whileFrame.StatementIndex; i < whileFrame.BodyBlock.Statements.Count; i++)
                        {
                            whileFrame.StatementIndex = i;
                            await ExecuteAsync(whileFrame.BodyBlock.Statements[i]);
                        }
                        // Iteration completed - reset statement index for next iteration
                        whileFrame.StatementIndex = 0;
                    }
                    else
                    {
                        // Single statement body
                        await ExecuteAsync(stmt.Body);
                        // Iteration completed - reset statement index for next iteration
                        whileFrame.StatementIndex = 0;
                    }
                }
                catch (ContinueException)
                {
                    // Continue to next iteration
                    // For desugared for loops, the while body is a Block with exactly 2 statements: [body, increment]
                    // When continue is thrown from the body (first statement), we need to execute the increment
                    // before continuing to the next iteration
                    if (whileFrame.BodyBlock != null 
                        && whileFrame.BodyBlock.Statements.Count == 2 
                        && whileFrame.StatementIndex == 0)
                    {
                        // This is likely a desugared for loop - execute the increment (second statement)
                        whileFrame.StatementIndex = 1;
                        await ExecuteAsync(whileFrame.BodyBlock.Statements[1]);
                    }
                    // Reset statement index for next iteration
                    whileFrame.StatementIndex = 0;
                    continue;
                }
                
                // Reset statement index for next iteration
                whileFrame.StatementIndex = 0;
            }
            
            // Loop completed - pop frame
            if (_executionStack.Count > 0 && _executionStack.Peek() == whileFrame)
            {
                _executionStack.Pop();
            }
        }
        catch (ContinueException)
        {
            // Continue to next iteration - frame state is preserved
            throw; // Re-throw to be handled by outer loop
        }
        catch (BreakException)
        {
            // Break out of loop - pop frame
            if (_executionStack.Count > 0 && _executionStack.Peek() == whileFrame)
            {
                _executionStack.Pop();
            }
        }
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteForAsync(ForStatement stmt)
    {
        // For loops are desugared to while loops in the parser
        // The desugared structure is: Block { initializer, While { condition, Block { body, increment } } }
        // Since for loops are desugared, the execution stack will handle the nested while loop
        // We just need to execute the desugared body
        return await ExecuteAsync(stmt.Body);
    }
    
    private async Task<RuntimeValue?> ExecuteForInAsync(ForInStatement stmt)
    {
        var collection = await EvaluateAsync(stmt.Collection);
        
        if (collection.Type != ValueType.Array)
            throw new RuntimeException("for-in loop requires an array.");
        
        var array = collection.AsArray();
        
        // Create a new scope for the loop variable
        _environment = new Environment(_environment);
        
        try
        {
            foreach (var element in array)
            {
                // Assign the current element to the loop variable
                _environment.Define(stmt.VariableName, element);
                
                try
                {
                    await ExecuteAsync(stmt.Body);
                }
                catch (BreakException)
                {
                    break;
                }
                catch (ContinueException)
                {
                    continue;
                }
            }
        }
        finally
        {
            // Restore the previous environment
            _environment = _environment.GetEnclosing()!;
        }
        
        return null;
    }

    private async Task<RuntimeValue?> ExecuteReturnAsync(ReturnStatement stmt)
    {
        RuntimeValue? value = null;
        if (stmt.Value != null)
        {
            value = await EvaluateAsync(stmt.Value);
        }
        throw new ReturnException(value);
    }
    
    private async Task<RuntimeValue?> ExecuteThrowAsync(ThrowStatement stmt)
    {
        var exceptionValue = await EvaluateAsync(stmt.Exception);
        throw new MALDAException(exceptionValue, stmt.Line, _currentFile);
    }
    
    private async Task<RuntimeValue?> ExecuteUsingAsync(UsingStatement stmt)
    {
        return await ExecuteUsingViaImportExecutorAsync(stmt);
    }

    private async Task<RuntimeValue?> ExecuteImportAsync(ImportStatement stmt)
    {
        return await ExecuteImportViaImportExecutorAsync(stmt);
    }
    
    private async Task<RuntimeValue?> ExecuteTryAsync(TryStatement stmt)
    {
        Exception? caughtException = null;
        bool exceptionHandled = false;
        
        try
        {
            await ExecuteBlockAsync(stmt.TryBlock);
        }
        catch (BreakException)
        {
            // Control flow - execute finally then rethrow
            await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
            throw; // Rethrow control flow exception
        }
        catch (ContinueException)
        {
            // Control flow - execute finally then rethrow
            await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
            throw; // Rethrow control flow exception
        }
        catch (ReturnException)
        {
            // Control flow - execute finally then rethrow
            await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
            throw; // Rethrow control flow exception
        }
        catch (MALDAException ex)
        {
            caughtException = ex;
        }
        catch (RuntimeException ex)
        {
            caughtException = ex;
        }
        
        // If an exception was caught, try to match it against catch clauses (Phase 4.5: optional filter)
        if (caughtException != null)
        {
            foreach (var catchClause in stmt.CatchClauses)
            {
                if (!await MatchesCatchClauseAsync(catchClause, caughtException))
                    continue;

                exceptionHandled = true;
                await ExecuteCatchBodyAsync(catchClause, caughtException);
                break;
            }
            
            // If exception wasn't caught by any clause, execute finally and rethrow
            if (!exceptionHandled)
            {
                await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
                throw caughtException;
            }
        }
        
        // Execute finally block (always executes, whether exception occurred or not)
        await ExecuteFinallyBlockAsync(stmt.FinallyBlock);
        
        return null;
    }
    
    private async Task ExecuteFinallyBlockAsync(BlockStatement? finallyBlock)
    {
        if (finallyBlock != null)
        {
            try
            {
                await ExecuteBlockAsync(finallyBlock);
            }
            catch (BreakException)
            {
                // Control flow exceptions from finally are rethrown
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
            // Other exceptions from finally are also rethrown (they override the original exception)
        }
    }
    
    internal async Task<RuntimeValue> EvaluateAsync(Expression expr, bool returnTask = false)
    {
        return expr switch
        {
            LiteralExpression literal => RuntimeValueFromLiteral(literal),
            IdentifierExpression identifier => LookUpVariable(identifier),
            BinaryExpression binary => await EvaluateBinaryAsync(binary),
            UnaryExpression unary => await EvaluateUnaryAsync(unary),
            PostfixExpression postfix => await EvaluatePostfixAsync(postfix),
            TernaryExpression ternary => await EvaluateTernaryAsync(ternary),
            FunctionCallExpression call => await EvaluateCallAsync(call, returnTask),
            AwaitExpression awaitExpr => await EvaluateAwaitAsync(awaitExpr),
            AsyncExpression asyncExpr => await EvaluateAsyncExpressionAsync(asyncExpr),
            MemberAccessExpression member => await EvaluateMemberAccessAsync(member),
            ArrayAccessExpression array => await EvaluateArrayAccessAsync(array),
            NewExpression newExpr => await EvaluateNewAsync(newExpr),
            ThisExpression => EvaluateThis(),
            SuperExpression => await EvaluateSuperAsync(),
            ArrayLiteralExpression array => await EvaluateArrayLiteralAsync(array),
            ObjectLiteralExpression obj => await EvaluateObjectLiteralAsync(obj),
            DictionaryLiteralExpression dict => await EvaluateDictionaryLiteralAsync(dict),
            GraphLiteralExpression graph => await EvaluateGraphLiteralAsync(graph),
            InterpolatedStringExpression interpolated => await EvaluateInterpolatedStringAsync(interpolated),
            LambdaExpression lambda => await EvaluateLambdaAsync(lambda),
            SpawnExpression spawn => await SpawnActorAsync(spawn),
            ReceiveExpression => await ReceiveMessageAsync(),
            SelfExpression => EvaluateSelf(),
            MatchExpression match => await EvaluateMatchAsync(match),
            PipeExpression pipe => await EvaluatePipeAsync(pipe),
            ListComprehensionExpression comprehension => await EvaluateListComprehensionAsync(comprehension),
            DictComprehensionExpression dictComprehension => await EvaluateDictComprehensionAsync(dictComprehension),
            _ => throw new RuntimeException($"Unknown expression type: {expr.GetType()}")
        };
    }
    
    internal RuntimeValue RuntimeValueFromLiteral(LiteralExpression literal)
    {
        if (literal.Value == null)
            return RuntimeValue.Null();
        
        return literal.Value switch
        {
            int i => RuntimeValue.Integer(i),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            string s => RuntimeValue.String(s),
            bool b => RuntimeValue.Boolean(b),
            _ => throw new RuntimeException($"Unknown literal type: {literal.Value.GetType()}")
        };
    }
    
    private RuntimeValue LookUpVariable(IdentifierExpression expr)
    {
        // First try to get from environment
        if (_environment.TryGet(expr.Name, out var value))
        {
            return value;
        }
        
        // If not found in environment, check if it's a built-in function
        // Built-in functions can be used as function references (e.g., passed to VectorDB constructor)
        if (IsBuiltIn(expr.Name))
        {
            // Create a FunctionValue wrapper for the built-in function
            // This allows built-in functions to be passed as arguments
            var funcDecl = new MaldaLang.Parser.AST.Declarations.FunctionDeclaration(
                expr.Name,
                new List<string>(), // Parameters will be validated when called
                new MaldaLang.Parser.AST.Statements.BlockStatement(new List<MaldaLang.Parser.AST.Statements.Statement>(), expr.Line, expr.Column),
                null,
                null,
                null,   // parameterTypeHints
                null,   // returnType
                false,
                expr.Line,
                expr.Column
            );
            var funcValue = new FunctionValue(funcDecl, _globals);
            return RuntimeValue.Function(funcValue);
        }
        
        // If not found in environment and we're in a method context,
        // try to access it as a member variable on the current object
        if (_currentObject != null)
        {
            // Check if it's actually a field on the class
            var field = _currentObject.Class?.FindField(expr.Name);
            if (field != null)
            {
                // Access the field through the object instance
                return _currentObject.Get(expr.Name, _currentClass);
            }
        }
        
        // Variable not found anywhere
        throw new RuntimeException($"Undefined variable '{expr.Name}'.", expr.Line, _currentFile);
    }
    
    private async Task<RuntimeValue> EvaluateInterpolatedStringAsync(InterpolatedStringExpression expr)
    {
        var result = new System.Text.StringBuilder();
        
        foreach (var segment in expr.Segments)
        {
            if (segment.IsExpression)
            {
                // Evaluate the expression and convert to string
                var value = await EvaluateAsync(segment.Expression!);
                result.Append(value.ToString());
            }
            else
            {
                // Add the text segment
                result.Append(segment.Text ?? "");
            }
        }
        
        return RuntimeValue.String(result.ToString());
    }
    
    private async Task<RuntimeValue> EvaluateTernaryAsync(TernaryExpression expr)
    {
        var condition = await EvaluateAsync(expr.Condition);
        if (condition.IsTruthy())
        {
            return await EvaluateAsync(expr.ThenBranch);
        }
        else
        {
            return await EvaluateAsync(expr.ElseBranch);
        }
    }
    
    private async Task<RuntimeValue> EvaluateBinaryAsync(BinaryExpression expr)
    {
        // Handle short-circuiting operators first
        if (expr.Operator == TokenType.And)
        {
            var left = await EvaluateAsync(expr.Left);
            if (!left.IsTruthy())
            {
                return RuntimeValue.Boolean(false);
            }
            var right = await EvaluateAsync(expr.Right);
            return RuntimeValue.Boolean(right.IsTruthy());
        }
        
        if (expr.Operator == TokenType.Or)
        {
            var left = await EvaluateAsync(expr.Left);
            if (left.IsTruthy())
            {
                return RuntimeValue.Boolean(true);
            }
            var right = await EvaluateAsync(expr.Right);
            return RuntimeValue.Boolean(right.IsTruthy());
        }
        
        // For all other operators, evaluate both sides
        var leftVal = await EvaluateAsync(expr.Left);
        var rightVal = await EvaluateAsync(expr.Right);

        if (leftVal.Type == ValueType.Object)
        {
            var overloadedResult = await TryBinaryOperatorOverload(expr.Operator, leftVal, rightVal);
            if (overloadedResult != null)
                return overloadedResult;
        }

        if (rightVal.Type == ValueType.Object)
        {
            var reversedResult = await TryReversedBinaryOperatorOverload(expr.Operator, leftVal, rightVal);
            if (reversedResult != null)
                return reversedResult;
        }
        
        switch (expr.Operator)
        {
            case TokenType.Plus:
                return EvaluatePlus(leftVal, rightVal, expr.Line);
            case TokenType.Minus:
                return EvaluateMinus(leftVal, rightVal, expr.Line);
            case TokenType.Multiply:
                return EvaluateMultiply(leftVal, rightVal, expr.Line);
            case TokenType.Divide:
                return EvaluateDivide(leftVal, rightVal, expr.Line);
            case TokenType.Modulo:
                return EvaluateModulo(leftVal, rightVal, expr.Line);
            case TokenType.Equal:
                return RuntimeValue.Boolean(IsEqual(leftVal, rightVal));
            case TokenType.NotEqual:
                return RuntimeValue.Boolean(!IsEqual(leftVal, rightVal));
            case TokenType.GreaterThan:
                return RuntimeValue.Boolean(EvaluateGreaterThan(leftVal, rightVal));
            case TokenType.GreaterThanOrEqual:
                return RuntimeValue.Boolean(EvaluateGreaterThanOrEqual(leftVal, rightVal));
            case TokenType.LessThan:
                return RuntimeValue.Boolean(EvaluateLessThan(leftVal, rightVal));
            case TokenType.LessThanOrEqual:
                return RuntimeValue.Boolean(EvaluateLessThanOrEqual(leftVal, rightVal));
            default:
                throw new RuntimeException($"Unknown binary operator: {expr.Operator}", expr.Line, _currentFile);
        }
    }

    private static string? GetBinaryOperatorMethodName(TokenType op)
    {
        return op switch
        {
            TokenType.Plus => "__add__",
            TokenType.Minus => "__sub__",
            TokenType.Multiply => "__mul__",
            TokenType.Divide => "__div__",
            TokenType.Modulo => "__mod__",
            TokenType.Equal => "__eq__",
            TokenType.NotEqual => "__neq__",
            TokenType.LessThan => "__lt__",
            TokenType.LessThanOrEqual => "__le__",
            TokenType.GreaterThan => "__gt__",
            TokenType.GreaterThanOrEqual => "__ge__",
            _ => null
        };
    }

    private static string? GetReversedBinaryOperatorMethodName(TokenType op)
    {
        return op switch
        {
            TokenType.Plus => "__radd__",
            TokenType.Minus => "__rsub__",
            TokenType.Multiply => "__rmul__",
            TokenType.Divide => "__rdiv__",
            TokenType.Modulo => "__rmod__",
            TokenType.Equal => "__req__",
            TokenType.NotEqual => "__rneq__",
            TokenType.LessThan => "__rlt__",
            TokenType.LessThanOrEqual => "__rle__",
            TokenType.GreaterThan => "__rgt__",
            TokenType.GreaterThanOrEqual => "__rge__",
            _ => null
        };
    }

    private async Task<RuntimeValue?> TryBinaryOperatorOverload(TokenType op, RuntimeValue leftVal, RuntimeValue rightVal)
    {
        var methodName = GetBinaryOperatorMethodName(op);
        if (methodName == null)
            return null;

        var receiver = leftVal.AsObject();
        if (!receiver.TryGet(methodName, out var method))
            return null;

        if (method == null || method.Type != ValueType.Function)
            return null;

        return await CallFunctionAsync(method.AsFunction(), new List<RuntimeValue> { rightVal }, receiver);
    }

    private async Task<RuntimeValue?> TryReversedBinaryOperatorOverload(TokenType op, RuntimeValue leftVal, RuntimeValue rightVal)
    {
        var methodName = GetReversedBinaryOperatorMethodName(op);
        if (methodName == null)
            return null;

        var receiver = rightVal.AsObject();
        if (!receiver.TryGet(methodName, out var method))
            return null;

        if (method == null || method.Type != ValueType.Function)
            return null;

        return await CallFunctionAsync(method.AsFunction(), new List<RuntimeValue> { leftVal }, receiver);
    }
    
    private RuntimeValue EvaluatePlus(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        if (left.Type == ValueType.String || right.Type == ValueType.String)
        {
            return RuntimeValue.String(left.ToString() + right.ToString());
        }
        if (left.Type == ValueType.Integer && right.Type == ValueType.Integer)
        {
            return CheckedIntegerResult(() => { checked { return left.AsInteger() + right.AsInteger(); } }, line, _currentFile);
        }
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return RuntimeValue.Float(leftVal + rightVal);
    }
    
    private RuntimeValue EvaluateMinus(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        CheckNumberOperands(left, right);
        if (left.Type == ValueType.Integer && right.Type == ValueType.Integer)
        {
            return CheckedIntegerResult(() => { checked { return left.AsInteger() - right.AsInteger(); } }, line, _currentFile);
        }
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return RuntimeValue.Float(leftVal - rightVal);
    }
    
    private RuntimeValue EvaluateMultiply(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        // Handle string repetition: string * number or number * string
        if (left.Type == ValueType.String && (right.Type == ValueType.Integer || right.Type == ValueType.Float))
        {
            var count = right.Type == ValueType.Integer ? right.AsInteger() : (int)right.AsFloat();
            if (count <= 0)
                return RuntimeValue.String("");
            var str = left.AsString();
            return RuntimeValue.String(string.Concat(Enumerable.Repeat(str, count)));
        }
        if ((left.Type == ValueType.Integer || left.Type == ValueType.Float) && right.Type == ValueType.String)
        {
            var count = left.Type == ValueType.Integer ? left.AsInteger() : (int)left.AsFloat();
            if (count <= 0)
                return RuntimeValue.String("");
            var str = right.AsString();
            return RuntimeValue.String(string.Concat(Enumerable.Repeat(str, count)));
        }
        
        // Numeric multiplication
        CheckNumberOperands(left, right);
        if (left.Type == ValueType.Integer && right.Type == ValueType.Integer)
        {
            return CheckedIntegerResult(() => { checked { return left.AsInteger() * right.AsInteger(); } }, line, _currentFile);
        }
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return RuntimeValue.Float(leftVal * rightVal);
    }
    
    private RuntimeValue EvaluateDivide(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        CheckNumberOperands(left, right);
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        if (rightVal == 0)
            throw new RuntimeException("Division by zero.", line, _currentFile);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        return RuntimeValue.Float(leftVal / rightVal);
    }
    
    private RuntimeValue EvaluateModulo(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        CheckNumberOperands(left, right);
        if (left.Type == ValueType.Integer && right.Type == ValueType.Integer)
        {
            if (right.AsInteger() == 0)
                throw new RuntimeException("Division by zero.", line, _currentFile);
            return CheckedIntegerResult(() => { checked { return left.AsInteger() % right.AsInteger(); } }, line, _currentFile);
        }
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        if (rightVal == 0)
            throw new RuntimeException("Division by zero.", line, _currentFile);
        return RuntimeValue.Float(leftVal % rightVal);
    }
    
    private bool EvaluateGreaterThan(RuntimeValue left, RuntimeValue right)
    {
        // Support string comparison
        if (left.Type == ValueType.String && right.Type == ValueType.String)
        {
            return string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal) > 0;
        }
        
        // Existing numeric comparison
        CheckNumberOperands(left, right);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return leftVal > rightVal;
    }
    
    private bool EvaluateGreaterThanOrEqual(RuntimeValue left, RuntimeValue right)
    {
        // Support string comparison
        if (left.Type == ValueType.String && right.Type == ValueType.String)
        {
            return string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal) >= 0;
        }
        
        // Existing numeric comparison
        CheckNumberOperands(left, right);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return leftVal >= rightVal;
    }
    
    private bool EvaluateLessThan(RuntimeValue left, RuntimeValue right)
    {
        // Support string comparison
        if (left.Type == ValueType.String && right.Type == ValueType.String)
        {
            return string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal) < 0;
        }
        
        // Existing numeric comparison
        CheckNumberOperands(left, right);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return leftVal < rightVal;
    }
    
    private bool EvaluateLessThanOrEqual(RuntimeValue left, RuntimeValue right)
    {
        // Support string comparison
        if (left.Type == ValueType.String && right.Type == ValueType.String)
        {
            return string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal) <= 0;
        }
        
        // Existing numeric comparison
        CheckNumberOperands(left, right);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return leftVal <= rightVal;
    }
    
    private bool IsEqual(RuntimeValue left, RuntimeValue right)
    {
        if (left.Type != right.Type) return false;
        if (left.Type == ValueType.Null) return true;
        return left.Value?.Equals(right.Value) ?? false;
    }
    
    private async Task<RuntimeValue> EvaluateUnaryAsync(UnaryExpression expr)
    {
        switch (expr.Operator)
        {
            case TokenType.Minus:
                var right = await EvaluateAsync(expr.Right);
                if (right.Type == ValueType.Object)
                {
                    var overloadedResult = await TryUnaryOperatorOverload(expr.Operator, right);
                    if (overloadedResult != null)
                        return overloadedResult;
                }
                CheckNumberOperand(right);
                if (right.Type == ValueType.Integer)
                    return CheckedIntegerResult(() => { checked { return -right.AsInteger(); } }, expr.Line, _currentFile);
                return RuntimeValue.Float(-right.AsFloat());
            case TokenType.Not:
                right = await EvaluateAsync(expr.Right);
                return RuntimeValue.Boolean(!right.IsTruthy());
            case TokenType.Increment:
            case TokenType.Decrement:
                return await EvaluatePrefixIncrementDecrementAsync(expr.Operator, expr.Right, expr.Line);
            default:
                throw new RuntimeException($"Unknown unary operator: {expr.Operator}", expr.Line, _currentFile);
        }
    }

    private static string? GetUnaryOperatorMethodName(TokenType op)
    {
        return op switch
        {
            TokenType.Minus => "__neg__",
            _ => null
        };
    }

    private async Task<RuntimeValue?> TryUnaryOperatorOverload(TokenType op, RuntimeValue operand)
    {
        var methodName = GetUnaryOperatorMethodName(op);
        if (methodName == null || operand.Type != ValueType.Object)
            return null;

        var receiver = operand.AsObject();
        if (!receiver.TryGet(methodName, out var method))
            return null;

        if (method == null || method.Type != ValueType.Function)
            return null;

        return await CallFunctionAsync(method.AsFunction(), new List<RuntimeValue>(), receiver);
    }
    
    private async Task<RuntimeValue> EvaluateAwaitAsync(AwaitExpression expr)
    {
        // Special handling for prompt calls: if awaiting a prompt call, execute it directly
        if (expr.Expression is FunctionCallExpression callExpr)
        {
            var callee = await EvaluateAsync(callExpr.Callee);
            if (callee.Type == ValueType.Prompt)
            {
                // Evaluate arguments
                var arguments = new List<RuntimeValue>();
                foreach (var arg in callExpr.Arguments)
                {
                    arguments.Add(await EvaluateAsync(arg));
                }
                
                // Call CallAsync to execute the prompt and return LLM response
                var prompt = callee.AsPrompt();
                return await prompt.CallAsync(arguments, this);
            }
        }

        if (expr.Expression is PipeExpression pipeExpr)
        {
            var piped = await EvaluatePipeAsync(pipeExpr);
            return await AiPipelineHelpers.CoerceAwaitResultAsync(piped, this);
        }
        
        // Normal await: evaluate expression and await the task
        var value = await EvaluateAsync(expr.Expression);
        if (value.Type != ValueType.Task)
            throw new RuntimeException("await requires a task value.", expr.Line, _currentFile);
        return await value.AsTask();
    }
    
    private async Task<RuntimeValue> EvaluateAsyncExpressionAsync(AsyncExpression expr)
    {
        if (expr.Expression is FunctionCallExpression callExpr)
        {
            return await EvaluateCallAsync(callExpr, returnTask: true);
        }
        var value = await EvaluateAsync(expr.Expression);
        return RuntimeValue.Task(System.Threading.Tasks.Task.FromResult(value));
    }
    
    private async Task<RuntimeValue> EvaluatePostfixAsync(PostfixExpression expr)
    {
        return await EvaluatePostfixIncrementDecrementAsync(expr.Operator, expr.Left, expr.Line);
    }
    
    private async Task<RuntimeValue> EvaluatePrefixIncrementDecrementAsync(TokenType op, Expression target, int? line = null)
    {
        // Get current value
        var currentValue = await GetLvalueAsync(target);
        CheckNumberOperand(currentValue);
        
        // Calculate new value
        RuntimeValue newValue;
        if (op == TokenType.Increment)
        {
            if (currentValue.Type == ValueType.Integer)
                newValue = CheckedIntegerResult(() => { checked { return currentValue.AsInteger() + 1; } }, line, _currentFile);
            else
                newValue = RuntimeValue.Float(currentValue.AsFloat() + 1.0);
        }
        else // Decrement
        {
            if (currentValue.Type == ValueType.Integer)
                newValue = CheckedIntegerResult(() => { checked { return currentValue.AsInteger() - 1; } }, line, _currentFile);
            else
                newValue = RuntimeValue.Float(currentValue.AsFloat() - 1.0);
        }
        
        // Assign new value back
        await SetLvalueAsync(target, newValue);
        
        // Return new value (prefix returns the new value)
        return newValue;
    }
    
    private async Task<RuntimeValue> EvaluatePostfixIncrementDecrementAsync(TokenType op, Expression target, int? line = null)
    {
        // Get current value
        var currentValue = await GetLvalueAsync(target);
        CheckNumberOperand(currentValue);
        
        // Store original value to return (postfix returns the old value)
        RuntimeValue originalValue;
        if (currentValue.Type == ValueType.Integer)
            originalValue = RuntimeValue.Integer(currentValue.AsInteger());
        else
            originalValue = RuntimeValue.Float(currentValue.AsFloat());
        
        // Calculate new value
        RuntimeValue newValue;
        if (op == TokenType.Increment)
        {
            if (currentValue.Type == ValueType.Integer)
                newValue = CheckedIntegerResult(() => { checked { return currentValue.AsInteger() + 1; } }, line, _currentFile);
            else
                newValue = RuntimeValue.Float(currentValue.AsFloat() + 1.0);
        }
        else // Decrement
        {
            if (currentValue.Type == ValueType.Integer)
                newValue = CheckedIntegerResult(() => { checked { return currentValue.AsInteger() - 1; } }, line, _currentFile);
            else
                newValue = RuntimeValue.Float(currentValue.AsFloat() - 1.0);
        }
        
        // Assign new value back
        await SetLvalueAsync(target, newValue);
        
        // Return original value (postfix returns the old value)
        return originalValue;
    }
    
    private async Task<RuntimeValue> GetLvalueAsync(Expression target)
    {
        if (target is IdentifierExpression idExpr)
        {
            if (_environment.TryGet(idExpr.Name, out var value))
                return value;
            
            if (_currentObject != null)
            {
                var field = _currentObject.Class?.FindField(idExpr.Name);
                if (field != null)
                {
                    if (field.Access == AccessModifier.Private && _currentClass != _currentObject.Class)
                        throw new RuntimeException($"Cannot access private field '{idExpr.Name}' from outside {_currentObject.Class.Name}.");
                    return _currentObject.Get(idExpr.Name);
                }
            }
            
            throw new RuntimeException($"Undefined variable '{idExpr.Name}'.", idExpr.Line, _currentFile);
        }
        else if (target is MemberAccessExpression memberExpr)
        {
            var obj = await EvaluateAsync(memberExpr.Object);
            if (obj.Type == ValueType.Object)
            {
                var instance = obj.AsObject();
                // Handle JsonObject specially (same as EvaluateMemberAccessAsync)
                if (instance is BuiltIns.JsonObject jsonObj)
                {
                    try
                    {
                        return jsonObj.Get(memberExpr.Member, null);
                    }
                    catch
                    {
                        return RuntimeValue.Null();
                    }
                }
                return instance.Get(memberExpr.Member);
            }
            else if (obj.Type == ValueType.Class)
            {
                var klass = obj.AsClass();
                if (klass.StaticFields.ContainsKey(memberExpr.Member))
                    return klass.StaticFields[memberExpr.Member];
                throw new RuntimeException($"Class {klass.Name} has no static field '{memberExpr.Member}'.");
            }
            throw new RuntimeException("Only objects and classes have properties.", memberExpr.Line, _currentFile);
        }
        else if (target is ArrayAccessExpression arrayExpr)
        {
            var array = await EvaluateAsync(arrayExpr.Array);
            var index = await EvaluateAsync(arrayExpr.Index);
            // Support object and dictionary indexing with string keys
            if (array.Type == ValueType.Object && index.Type == ValueType.String)
            {
                var obj = array.AsObject();
                var key = index.AsString();
                
                // Handle DictionaryInstance
                if (obj is DictionaryInstance dict)
                {
                    return dict.TryGetEntry(key, out var value) ? value : RuntimeValue.Null();
                }
                
                // Handle JsonObject (for JSON parsing results and query params)
                if (obj is BuiltIns.JsonObject jsonObj)
                {
                    try
                    {
                        return jsonObj.Get(key, null);
                    }
                    catch
                    {
                        return RuntimeValue.Null();
                    }
                }
                
                // For other object types, try to get the property
                try
                {
                    return obj.Get(key, null);
                }
                catch
                {
                    return RuntimeValue.Null();
                }
            }
            if (array.Type != ValueType.Array)
                throw new RuntimeException("Only arrays can be indexed.", arrayExpr.Line, _currentFile);
            if (!NumericCoercion.TryAsInteger(index, out var idx))
                throw new RuntimeException("Array index must be an integer.", arrayExpr.Line, _currentFile);
            var arr = array.AsArray();
            if (idx < 0 || idx >= arr.Count)
                throw new RuntimeException("Array index out of bounds.", arrayExpr.Line, _currentFile);
            return arr[idx];
        }
        else
        {
            throw new RuntimeException("Invalid lvalue for increment/decrement.", target?.Line, _currentFile);
        }
    }
    
    private async Task SetLvalueAsync(Expression target, RuntimeValue value)
    {
        if (target is IdentifierExpression idExpr)
        {
            EnsureMutableIdentifier(idExpr.Name, idExpr.Line);
            if (_environment.TryAssign(idExpr.Name, value))
                return;
            
            if (_currentObject != null)
            {
                var field = _currentObject.Class?.FindField(idExpr.Name);
                if (field != null)
                {
                    if (field.Access == AccessModifier.Private && _currentClass != _currentObject.Class)
                        throw new RuntimeException($"Cannot access private field '{idExpr.Name}' from outside {_currentObject.Class.Name}.");
                    _currentObject.Set(idExpr.Name, value);
                    return;
                }
            }
            
            throw new RuntimeException($"Undefined variable '{idExpr.Name}'.", idExpr.Line, _currentFile);
        }
        else if (target is MemberAccessExpression memberExpr)
        {
            var obj = await EvaluateAsync(memberExpr.Object);
            if (obj.Type == ValueType.Object)
            {
                obj.AsObject().Set(memberExpr.Member, value);
            }
            else if (obj.Type == ValueType.Class)
            {
                var klass = obj.AsClass();
                if (klass.StaticFields.ContainsKey(memberExpr.Member))
                {
                    klass.StaticFields[memberExpr.Member] = value;
                }
                else
                {
                    throw new RuntimeException($"Class {klass.Name} has no static field '{memberExpr.Member}'.");
                }
            }
            else
            {
                throw new RuntimeException("Only objects and classes have properties.", memberExpr.Line, _currentFile);
            }
        }
        else if (target is ArrayAccessExpression arrayExpr)
        {
            var array = await EvaluateAsync(arrayExpr.Array);
            var index = await EvaluateAsync(arrayExpr.Index);
            // Support object and dictionary indexing with string keys
            if (array.Type == ValueType.Object && index.Type == ValueType.String)
            {
                var obj = array.AsObject();
                var key = index.AsString();
                
                // Handle DictionaryInstance
                if (obj is DictionaryInstance dict)
                {
                    dict.SetEntry(key, value);
                    return;
                }
                
                // Handle JsonObject (for JSON parsing results and query params)
                if (obj is BuiltIns.JsonObject jsonObj)
                {
                    jsonObj.Set(key, value);
                    return;
                }
                
                // For other object types, try to set the property
                obj.Set(key, value);
                return;
            }
            if (array.Type != ValueType.Array)
                throw new RuntimeException("Only arrays can be indexed.", arrayExpr.Line, _currentFile);
            if (!NumericCoercion.TryAsInteger(index, out var idx))
                throw new RuntimeException("Array index must be an integer.", arrayExpr.Line, _currentFile);
            var arr = array.AsArray();
            if (idx < 0)
                throw new RuntimeException("Array index out of bounds.", arrayExpr.Line, _currentFile);
            
            // Arrays automatically grow when assigning to out-of-bounds indices
            // Extend array with null values if needed
            while (arr.Count <= idx)
            {
                arr.Add(RuntimeValue.Null());
            }
            
            arr[idx] = value;
        }
        else
        {
            throw new RuntimeException("Invalid lvalue for assignment.", target?.Line, _currentFile);
        }
    }
    
    private async Task<RuntimeValue> EvaluateCallAsync(FunctionCallExpression expr, bool returnTask = false)
    {
        return await EvaluateCallViaDispatcherAsync(expr, returnTask);
    }
    
    internal async Task<RuntimeValue> CallFunctionAsync(FunctionValue function, List<RuntimeValue> arguments, ObjectInstance? instance = null)
    {
        var previousFile = _currentFile;
        var functionFile = function.Declaration?.SourceFile ?? _currentFile;
        _currentFile = functionFile;
        var functionProfileToken = function.Declaration != null
            ? MaldaProfiler.EnterFunction(GetFunctionProfileName(function), functionFile, function.Declaration.Line)
            : default;

        // Handle variant constructor calls: return Variant(tag, arguments)
        if (function.VariantConstructorTag != null)
        {
            if (arguments.Count != function.VariantConstructorArity)
                throw new RuntimeException($"Variant constructor {function.VariantConstructorTag} expects {function.VariantConstructorArity} argument(s) but got {arguments.Count}.");
            return RuntimeValue.Variant(function.VariantConstructorTag, new List<RuntimeValue>(arguments));
        }

        // Handle built-in class method calls
        if (function.BuiltInInstance != null && function.BuiltInMethod != null)
        {
            var methodName = function.BuiltInMethod;
            if (function.BuiltInInstance is BuiltIns.ComposedPipeInstance composedPipe)
                return await composedPipe.RunAsync(arguments[0], this);
            // Check if it's an async method
            if (function.BuiltInInstance is BuiltIns.AnsiConsoleInstance && 
                (methodName == "status" || methodName == "prompt" || methodName == "progress"))
            {
                return await CallBuiltInMethodAsync(function.BuiltInInstance, methodName, arguments);
            }
            if (function.BuiltInInstance is BuiltIns.StdLibModuleInstance &&
                BuiltIns.StdLibModuleInstance.RequiresAsyncCall(methodName))
            {
                return await CallBuiltInMethodAsync(function.BuiltInInstance, methodName, arguments);
            }
            return CallBuiltInMethod(function.BuiltInInstance, methodName, arguments);
        }
        
        // Handle extension-style bound built-ins (e.g. str.upper when str is a string primitive)
        if (function.BoundReceiver != null && function.BoundBuiltInName != null)
        {
            var args = new List<RuntimeValue> { function.BoundReceiver! };
            args.AddRange(arguments);
            try
            {
                return await BuiltInFunctions.CallBuiltInAsync(function.BoundBuiltInName, args, this);
            }
            catch (System.Exception ex) when (!(ex is RuntimeException))
            {
                throw new RuntimeException(ex.Message);
            }
        }
        
        // Check if it's a built-in function
        if (function.Declaration != null && IsBuiltIn(function.Declaration.Name))
        {
            try
            {
                return await BuiltInFunctions.CallBuiltInAsync(function.Declaration.Name, arguments, this);
            }
            catch (System.Exception ex) when (!(ex is RuntimeException))
            {
                // Convert System.Exception to RuntimeException so it can be caught by try-catch blocks
                throw new RuntimeException(ex.Message);
            }
        }

        // Transpiled callbacks (e.g. GraphMemory custom embedders) have no Declaration.
        if (function.TranspiledDelegate != null)
            return await InvokeTranspiledDelegateAsync(function.TranspiledDelegate, arguments);
        
        if (function.Declaration == null)
        {
            throw new RuntimeException("Cannot call null function.");
        }
        
        if (arguments.Count != function.Declaration.Parameters.Count)
        {
            throw new RuntimeException($"Expected {function.Declaration.Parameters.Count} arguments but got {arguments.Count}.");
        }
        
        // FIX: Use the current active environment (_environment) directly
        // This is the correct environment to restore to after the recursive call returns
        // The _environment is already set to the correct active environment (which may be
        // a BlockFrame's environment that encloses the FunctionFrame's environment)
        // This ensures local variables defined in the current scope are preserved
        var previousForFrame = _environment;
        
        // New call - create frame
        // If we're in an actor context, use the current environment (actor's state) as the closure
        // Otherwise, use the function's closure
        var closure = _currentActor != null ? _environment : function.Closure;
        var environment = new Environment(closure);
        var functionFrame = new FunctionFrame
        {
            Function = function,
            Environment = environment,
            StatementIndex = 0,
            PreviousEnvironment = previousForFrame // Store the previous environment in the frame
        };
        _executionStack.Push(functionFrame);
        
        // Track call stack for debugger
        var callStackFrame = new InterpreterCallStackFrame
        {
            FunctionName = function.Declaration.Name,
            ClassName = function.ClassName,
            Line = function.Declaration.Line,
            File = functionFile ?? "main.malda"
        };
        _callStack.Add(callStackFrame);
        
        // Notify debugger of function entry
        if (_debuggerHook != null)
        {
            _debuggerHook.OnFunctionEnter(function.Declaration.Name, function.ClassName, function.Declaration.Line);
        }
        // Get the previous environment from the frame (stored when frame was created)
        var previous = functionFrame.PreviousEnvironment ?? _environment;
        var previousObject = _currentObject;
        var previousClass = _currentClass;
        var previousActor = _currentActor;
        
        var withinMs = DeclarationBounds.TryGetWithinTimeoutMs(function.Declaration);
        if (withinMs is > 0)
            WithinBoundsContext.Push(withinMs.Value);

        PushDeferFrame();
        try
        {
            _environment = environment;
            
            // Initialize function state
            if (function.IsConstructor)
            {
                ObjectInstance constructedObject;
                if (instance == null)
                {
                    if (function.ClassName == null || !_classes.TryGetValue(function.ClassName, out var classDef))
                    {
                        throw new RuntimeException($"Cannot construct: class '{function.ClassName}' not found.");
                    }
                    _currentClass = classDef;
                    _currentObject = new ObjectInstance(_currentClass);
                    constructedObject = _currentObject;
                }
                else
                {
                    _currentObject = instance;
                    _currentClass = instance.Class;
                    constructedObject = instance;
                }
                
                // Define 'this' in the environment after setting _currentObject
                environment.Define("this", RuntimeValue.Object(_currentObject));
            }
            else if (instance != null)
            {
                // Bind 'this' for method calls
                _currentObject = instance;
                _currentClass = instance.Class;
                environment.Define("this", RuntimeValue.Object(instance));
            }
            else if (function.ClassName != null)
            {
                // Static method call - set _currentClass so static field access works correctly
                // Only set _currentClass if the class exists (actors have ClassName set but aren't in _classes)
                if (_classes.TryGetValue(function.ClassName, out var classDef))
                {
                    _currentClass = classDef;
                }
                // If class doesn't exist, it's likely an actor handler - skip setting _currentClass
            }
            
            for (int i = 0; i < function.Declaration.Parameters.Count; i++)
            {
                environment.Define(function.Declaration.Parameters[i], arguments[i]);
            }
            
            // Execute function body from saved statement index
            var body = function.Declaration.Body;
            RuntimeValue? lastExprValue = null;
            var applyLastExprWins = !function.IsConstructor;
            for (int i = functionFrame.StatementIndex; i < body.Statements.Count; i++)
            {
                WithinBoundsContext.EnsureWithinBound(function.Declaration.Name);
                functionFrame.StatementIndex = i;
                var stmt = body.Statements[i];
                var isLast = (i == body.Statements.Count - 1);
                if (applyLastExprWins && isLast && stmt is ExpressionStatement exprStmt)
                {
                    lastExprValue = await EvaluateAsync(exprStmt.Expression);
                }
                else
                {
                    await ExecuteAsync(stmt);
                }
            }
            
            // Function completed - pop frame
            if (_executionStack.Count > 0 && _executionStack.Peek() == functionFrame)
            {
                _executionStack.Pop();
            }
            
            // Notify debugger of function exit
            if (_debuggerHook != null)
            {
                _debuggerHook.OnFunctionExit(function.Declaration.Name);
            }
            
            RuntimeValue result;
            if (function.IsConstructor)
            {
                result = RuntimeValue.Object(_currentObject);
            }
            else if (applyLastExprWins && lastExprValue != null)
            {
                result = lastExprValue;
            }
            else
            {
                result = RuntimeValue.Null();
            }
            
            // Clean up state for normal completion
            _environment = previous;
            _currentObject = previousObject;
            _currentClass = previousClass;
            _currentActor = previousActor;
            
            if (_callStack.Count > 0)
            {
                _callStack.RemoveAt(_callStack.Count - 1);
            }
            
            return result;
        }
        catch (ReturnException returnValue)
        {
            // Function completed - pop frame
            if (_executionStack.Count > 0 && _executionStack.Peek() == functionFrame)
            {
                _executionStack.Pop();
            }
            
            // Notify debugger of function exit
            if (_debuggerHook != null)
            {
                _debuggerHook.OnFunctionExit(function.Declaration.Name);
            }
            
            // Clean up state for normal return
            _environment = previous;
            _currentObject = previousObject;
            _currentClass = previousClass;
            _currentActor = previousActor;
            
            if (_callStack.Count > 0)
            {
                _callStack.RemoveAt(_callStack.Count - 1);
            }
            
            return returnValue.Value ?? RuntimeValue.Null();
        }
        finally
        {
            await RunAndPopDeferFrameAsync();

            if (withinMs is > 0)
                WithinBoundsContext.Pop();

            // Clean up environment if function completed normally (not InputRequiredException or ReturnException)
            // ReturnException and normal completion already cleaned up above
            MaldaProfiler.Exit(functionProfileToken);
            _currentFile = previousFile;
        }
    }

    private static async Task<RuntimeValue> InvokeTranspiledDelegateAsync(
        Func<object, Task<object>> delegateFn,
        List<RuntimeValue> arguments)
    {
        // GraphMemory / VectorDB calculators are unary; multi-arg transpiled funcs are rare here.
        object? input = null;
        if (arguments.Count > 0)
        {
            var arg = arguments[0];
            input = arg.Type switch
            {
                ValueType.Integer => arg.AsInteger(),
                ValueType.Float => arg.AsFloat(),
                ValueType.String => arg.AsString(),
                ValueType.Boolean => arg.AsBoolean(),
                ValueType.Array => arg.AsArray(),
                ValueType.Object => arg.AsObject(),
                ValueType.Function => arg.AsFunction(),
                ValueType.Null => null,
                _ => arg
            };
        }

        var result = await delegateFn(input!).ConfigureAwait(false);
        return CoerceTranspiledDelegateResult(result);
    }

    private static RuntimeValue CoerceTranspiledDelegateResult(object? result)
    {
        switch (result)
        {
            case RuntimeValue rv:
                return rv;
            case null:
                return RuntimeValue.Null();
            case int i:
                return RuntimeValue.Integer(i);
            case long l:
                return RuntimeValue.Integer((int)l);
            case double d:
                return RuntimeValue.Float(d);
            case float f:
                return RuntimeValue.Float(f);
            case string s:
                return RuntimeValue.String(s);
            case bool b:
                return RuntimeValue.Boolean(b);
            case FunctionValue fn:
                return RuntimeValue.Function(fn);
            case ObjectInstance oi:
                return RuntimeValue.Object(oi);
            case IList<RuntimeValue> runtimeList:
                return RuntimeValue.Array(runtimeList.ToList());
            case System.Collections.IEnumerable enumerable when result is not string:
                return RuntimeValue.Array(
                    enumerable.Cast<object?>().Select(CoerceTranspiledDelegateResult).ToList());
            default:
                return RuntimeValue.Object(new BuiltIns.DotNetObjectInstance(result));
        }
    }
    
    private bool IsChildEnvironment(Environment? child, Environment? parent)
    {
        if (child == null || parent == null) return false;
        if (child == parent) return true;
        
        // Walk up the environment chain to see if parent is an ancestor
        var current = child.GetEnclosing();
        while (current != null)
        {
            if (current == parent) return true;
            current = current.GetEnclosing();
        }
        return false;
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
    
    private bool IsBuiltIn(string name)
    {
        return BuiltInRegistry.IsInterpreterBuiltIn(name);
    }
    
    
    private async Task<RuntimeValue> CallConstructorAsync(ClassDefinition klass, List<RuntimeValue> arguments)
    {
        // Handle built-in VectorDB class
        if (klass == VectorDBClassDefinition.Instance)
        {
            if (arguments.Count != 2)
                throw new RuntimeException("VectorDB() expects 2 arguments: (dimension, precision)");
            
            var dimensionValue = arguments[0];
            var precisionValue = arguments[1];
            
            if (dimensionValue.Type != ValueType.Integer)
                throw new RuntimeException("VectorDB() first argument (dimension) must be an integer");
            
            if (precisionValue.Type != ValueType.String)
                throw new RuntimeException("VectorDB() second argument (precision) must be a string");
            
            var dimension = dimensionValue.AsInteger();
            var precision = precisionValue.AsString();
            
            var vectorDB = new VectorDBInstance(dimension, precision);
            return RuntimeValue.Object(vectorDB);
        }
        
        // Handle built-in GraphMemory class
        if (klass == GraphMemoryClassDefinition.Instance)
        {
            if (arguments.Count != 0)
                throw new RuntimeException("GraphMemory() expects no arguments");
            
            var graphMemory = new GraphMemoryInstance();
            graphMemory.SetInterpreter(this);
            return RuntimeValue.Object(graphMemory);
        }
        
        var instance = new ObjectInstance(klass);
        
        // Initialize fields with default values
        foreach (var field in klass.Fields.Values)
        {
            if (field.Value is Expression initExpr)
            {
                var previousObject = _currentObject;
                var previousClass = _currentClass;
                _currentObject = instance;
                _currentClass = klass;
                try
                {
                    var value = await EvaluateAsync(initExpr);
                    instance.Set(field.Name, value);
                }
                finally
                {
                    _currentObject = previousObject;
                    _currentClass = previousClass;
                }
            }
        }
        
        if (klass.Constructor != null)
        {
            return await CallFunctionAsync(klass.Constructor, arguments, instance);
        }
        
        return RuntimeValue.Object(instance);
    }
    
    private async Task<RuntimeValue> EvaluateMemberAccessAsync(MemberAccessExpression expr)
    {
        return await EvaluateMemberAccessViaResolverAsync(expr);
    }
    
    private async Task<RuntimeValue> EvaluateArrayAccessAsync(ArrayAccessExpression expr)
    {
        var array = await EvaluateAsync(expr.Array);
        if (expr.IsNullConditional && array.Type == ValueType.Null)
            return RuntimeValue.Null();

        var index = await EvaluateAsync(expr.Index);
        
        // Support array indexing with integer
        if (array.Type == ValueType.Array)
        {
            if (!NumericCoercion.TryAsInteger(index, out var idx))
                throw new RuntimeException("Array index must be an integer.", expr.Line, _currentFile);

            var arr = array.AsArray();
            if (idx < 0 || idx >= arr.Count)
                throw new RuntimeException("Array index out of bounds.", expr.Line, _currentFile);
            
            return arr[idx];
        }
        
        // Support object and dictionary indexing with string keys
        if (array.Type == ValueType.Object && index.Type == ValueType.String)
        {
            var obj = array.AsObject();
            var key = index.AsString();
            
            // Handle DictionaryInstance
            if (obj is DictionaryInstance dict)
            {
                return dict.TryGetEntry(key, out var value) ? value : RuntimeValue.Null();
            }
            
            // Handle JsonObject (for JSON parsing results and query params)
            if (obj is BuiltIns.JsonObject jsonObj)
            {
                try
                {
                    return jsonObj.Get(key, null);
                }
                catch
                {
                    return RuntimeValue.Null();
                }
            }
            
            // For other object types, try to get the property
            try
            {
                return obj.Get(key, null);
            }
            catch
            {
                return RuntimeValue.Null();
            }
        }
        
        throw new RuntimeException("Only arrays can be indexed with integers, or objects/dictionaries can be indexed with strings.", expr.Line, _currentFile);
    }
    
    private async Task<RuntimeValue> EvaluateNewAsync(NewExpression expr)
    {
        // Check for built-in classes first
        if (expr.ClassName == "LLMClient")
        {
            return await CreateLLMClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "OpenRouterClient")
        {
            return await CreateOpenRouterClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "LlamaCppClient")
        {
            return await CreateLlamaCppClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "LlamaEmbedder")
        {
            return await CreateLlamaEmbedderAsync(expr.Arguments);
        }
        else if (expr.ClassName == "Conversation")
        {
            return await CreateConversationAsync(expr.Arguments);
        }
        else if (expr.ClassName == "Tool")
        {
            return await CreateToolAsync(expr.Arguments);
        }
        else if (expr.ClassName == "Agent")
        {
            return await CreateAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "CodingAgent")
        {
            return await CreateCodingAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "GitAgent")
        {
            return await CreateGitAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "DevAgent")
        {
            return await CreateDevAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "MALDACodingAgent")
        {
            return await CreateMALDACodingAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "HumanAgent")
        {
            return await CreateHumanAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "HTMLCache")
        {
            return await CreateHTMLCacheAsync(expr.Arguments);
        }
        else if (expr.ClassName == "RestServer")
        {
            return await CreateRestServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "RestClient")
        {
            return await CreateRestClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "HttpServer")
        {
            return await CreateHttpServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "MCPServer")
        {
            return await CreateMCPServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "MCPClient")
        {
            return await CreateMCPClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "ACPClient")
        {
            return await CreateACPClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "ACPServer")
        {
            return await CreateACPServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "ACPAgentTool")
        {
            return await CreateACPAgentToolAsync(expr.Arguments);
        }
        else if (expr.ClassName == "LLMClientBridge")
        {
            return await CreateLLMClientBridgeAsync(expr.Arguments);
        }
        else if (expr.ClassName == "LLMServer")
        {
            return await CreateLLMServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "SqlServerClient")
        {
            return await CreateSqlServerClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "PostgresClient")
        {
            return await CreatePostgresClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "SqliteClient")
        {
            return await CreateSqliteClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "SerialConnection")
        {
            return await CreateSerialConnectionAsync(expr.Arguments);
        }
        else if (expr.ClassName == "ArduinoConnection")
        {
            return await CreateArduinoConnectionAsync(expr.Arguments);
        }
        else if (expr.ClassName == "VectorDB")
        {
            var vectorDbArguments = new List<RuntimeValue>();
            foreach (var arg in expr.Arguments)
            {
                vectorDbArguments.Add(await EvaluateAsync(arg));
            }
            return await CallConstructorAsync(VectorDBClassDefinition.Instance, vectorDbArguments);
        }
        else if (expr.ClassName == "GraphMemory")
        {
            var graphMemoryArguments = new List<RuntimeValue>();
            foreach (var arg in expr.Arguments)
            {
                graphMemoryArguments.Add(await EvaluateAsync(arg));
            }
            return await CallConstructorAsync(GraphMemoryClassDefinition.Instance, graphMemoryArguments);
        }
        
        if (!_classes.ContainsKey(expr.ClassName))
            throw new RuntimeException($"Class '{expr.ClassName}' not found.");
        
        var klass = _classes[expr.ClassName];
        var arguments = new List<RuntimeValue>();
        foreach (var arg in expr.Arguments)
        {
            arguments.Add(await EvaluateAsync(arg));
        }
        
        return await CallConstructorAsync(klass, arguments);
    }
    
    private async Task<RuntimeValue> CreateLLMClientAsync(List<Expression> args)
    {
        if (args.Count < 3)
            throw new RuntimeException("LLMClient() expects 3 arguments: (apiUrl, apiKey, model)");
        
        var apiUrl = await EvaluateAsync(args[0]);
        var apiKey = await EvaluateAsync(args[1]);
        var model = await EvaluateAsync(args[2]);
        
        if (apiUrl.Type != ValueType.String || apiKey.Type != ValueType.String || model.Type != ValueType.String)
            throw new RuntimeException("LLMClient() expects (string, string, string)");
        
        var client = new BuiltIns.LLMClientInstance();
        client.ApiUrl = apiUrl.AsString();
        client.ApiKey = apiKey.AsString();
        client.Model = model.AsString();
        
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateOpenRouterClientAsync(List<Expression> args)
    {
        // OpenRouterClient takes 0 or 1 argument (optional model name)
        if (args.Count > 1)
            throw new RuntimeException("OpenRouterClient() expects 0 or 1 argument: (model?)");
        
        string? model = null;
        if (args.Count == 1)
        {
            var modelValue = await EvaluateAsync(args[0]);
            if (modelValue.Type != ValueType.String)
                throw new RuntimeException("OpenRouterClient() model argument must be a string");
            model = modelValue.AsString();
        }
        
        var client = new BuiltIns.OpenRouterClientInstance(model);
        
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateLlamaCppClientAsync(List<Expression> args)
    {
        var client = new BuiltIns.LlamaCppClientInstance();
        
        if (args.Count == 0)
            return RuntimeValue.Object(client);
        
        if (args.Count != 1)
            throw new RuntimeException("LlamaCppClient() expects 0 or 1 argument: (modelPath?)");
        
        var modelPath = await EvaluateAsync(args[0]);
        if (modelPath.Type != ValueType.String)
            throw new RuntimeException("LlamaCppClient() expects an optional string modelPath");
        
        client.ModelPath = modelPath.AsString();
        
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateLlamaEmbedderAsync(List<Expression> args)
    {
        // LlamaEmbedder takes 1 argument: modelPath (string)
        if (args.Count < 1)
            throw new RuntimeException("LlamaEmbedder() expects 1 argument: (modelPath)");
        
        var modelPath = await EvaluateAsync(args[0]);
        if (modelPath.Type != ValueType.String)
            throw new RuntimeException("LlamaEmbedder() expects (string)");
        
        var embedder = new BuiltIns.LlamaEmbedderInstance();
        embedder.ModelPath = modelPath.AsString();
        
        return RuntimeValue.Object(embedder);
    }
    
    private async Task<RuntimeValue> CreateConversationAsync(List<Expression> args)
    {
        if (args.Count < 2)
            throw new RuntimeException("Conversation() expects 2 arguments: (client, systemPrompt)");
        
        var client = await EvaluateAsync(args[0]);
        var systemPrompt = await EvaluateAsync(args[1]);
        
        if (client.Type != ValueType.Object || systemPrompt.Type != ValueType.String)
            throw new RuntimeException("Conversation() expects (LLMClient, string)");
        
        var clientObj = client.AsObject();
        BuiltIns.LLMClientInstance? llmClient = null;
        BuiltIns.LlamaCppClientInstance? llamaClient = null;
        BuiltIns.LLMClientBridge.LLMClientBridgeInstance? bridgeClient = null;
        
        if (clientObj is BuiltIns.LLMClientInstance llm)
        {
            llmClient = llm;
        }
        else if (clientObj is BuiltIns.LlamaCppClientInstance llama)
        {
            llamaClient = llama;
        }
        else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridge)
        {
            bridgeClient = bridge;
        }
        else
        {
            throw new RuntimeException("Conversation() first argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
        }
        
        var conv = new BuiltIns.ConversationInstance();
        conv.Initialize(llmClient, llamaClient, bridgeClient, systemPrompt.AsString(), _inputProvider);
        
        return RuntimeValue.Object(conv);
    }
    
    private async Task<RuntimeValue> CreateToolAsync(List<Expression> args)
    {
        if (args.Count < 4)
            throw new RuntimeException("Tool() expects 4 arguments: (name, description, parameters, handler)");
        
        var name = await EvaluateAsync(args[0]);
        var description = await EvaluateAsync(args[1]);
        var parameters = await EvaluateAsync(args[2]);
        var handler = await EvaluateAsync(args[3]);
        
        if (name.Type != ValueType.String || description.Type != ValueType.String || 
            parameters.Type != ValueType.Object)
            throw new RuntimeException("Tool() expects (string, string, object, function?)");
        
        var tool = new BuiltIns.ToolInstance();
        // Keep schema on Initialize; wire the callable handler separately so Conversation
        // / tool.execute() can invoke it (same path as @Tool registration).
        tool.Initialize(name.AsString(), description.AsString(), parameters, null);
        if (handler.Type == ValueType.Function)
            tool.SetFunctionHandler(handler.AsFunction(), this);

        return RuntimeValue.Object(tool);
    }
    
    private RuntimeValue HandleBuiltInMemberAccess(ObjectInstance instance, string member)
    {
        // Handle property access
        if (instance is BuiltIns.LLMClientInstance llmClient)
        {
            if (member == "apiUrl")
                return RuntimeValue.String(llmClient.ApiUrl);
            if (member == "apiKey")
                return RuntimeValue.String(llmClient.ApiKey);
            if (member == "model")
                return RuntimeValue.String(llmClient.Model);
            if (member == "temperature")
                return RuntimeValue.Float(llmClient.Temperature);
            if (member == "maxTokens")
                return RuntimeValue.Integer(llmClient.MaxTokens);
        }
        else if (instance is BuiltIns.LlamaCppClientInstance llamaClient)
        {
            if (member == "modelPath")
                return RuntimeValue.String(llamaClient.ModelPath);
            if (member == "temperature")
                return RuntimeValue.Float(llamaClient.Temperature);
            if (member == "maxTokens")
                return RuntimeValue.Integer(llamaClient.MaxTokens);
        }
        else if (instance is BuiltIns.LLMServerInstance llmServer)
        {
            if (member == "port")
                return llmServer.Get("port", null);
            if (member == "host")
                return llmServer.Get("host", null);
            if (member == "isRunning")
                return llmServer.Get("isRunning", null);
            if (member == "bridge")
                return llmServer.Get("bridge", null);
        }
        else if (instance is BuiltIns.ConversationInstance conv)
        {
            // Conversation doesn't expose properties directly
        }
        else if (instance is BuiltIns.ToolInstance tool)
        {
            if (member == "name")
                return RuntimeValue.String(tool.Name);
            if (member == "description")
                return RuntimeValue.String(tool.Description);
        }
        else if (instance is BuiltIns.AgentInstance agent)
        {
            if (member == "name")
                return RuntimeValue.String(agent.Name);
            if (member == "role")
                return RuntimeValue.String(agent.Role);
            if (member == "instructions")
                return RuntimeValue.String(agent.Instructions);
        }
        else if (instance is BuiltIns.CodingAgentInstance codingAgent)
        {
            // CodingAgentInstance inherits from AgentInstance, so it has the same properties
            if (member == "name")
                return RuntimeValue.String(codingAgent.Name);
            if (member == "role")
                return RuntimeValue.String(codingAgent.Role);
            if (member == "instructions")
                return RuntimeValue.String(codingAgent.Instructions);
        }
        else if (instance is BuiltIns.GitAgentInstance gitAgent)
        {
            // GitAgentInstance inherits from AgentInstance, so it has the same properties
            if (member == "name")
                return RuntimeValue.String(gitAgent.Name);
            if (member == "role")
                return RuntimeValue.String(gitAgent.Role);
            if (member == "instructions")
                return RuntimeValue.String(gitAgent.Instructions);
        }
        else if (instance is BuiltIns.DevAgentInstance devAgent)
        {
            // DevAgentInstance inherits from AgentInstance, so it has the same properties
            if (member == "name")
                return RuntimeValue.String(devAgent.Name);
            if (member == "role")
                return RuntimeValue.String(devAgent.Role);
            if (member == "instructions")
                return RuntimeValue.String(devAgent.Instructions);
        }
        else if (instance is BuiltIns.MALDACodingAgentInstance splCodingAgent)
        {
            // MALDACodingAgentInstance inherits from AgentInstance, so it has the same properties
            if (member == "name")
                return RuntimeValue.String(splCodingAgent.Name);
            if (member == "role")
                return RuntimeValue.String(splCodingAgent.Role);
            if (member == "instructions")
                return RuntimeValue.String(splCodingAgent.Instructions);
        }
        else if (instance is BuiltIns.SqlServerClientInstance sqlServerClient)
        {
            if (member == "isConnected")
                return sqlServerClient.Get("isConnected", null);
        }
        else if (instance is BuiltIns.PostgresClientInstance postgresClient)
        {
            if (member == "isConnected")
                return postgresClient.Get("isConnected", null);
        }
        else if (instance is BuiltIns.SqliteClientInstance sqliteClient)
        {
            if (member == "isConnected")
                return sqliteClient.Get("isConnected", null);
        }
        else if (instance is BuiltIns.DotNetObjectInstance dotNetObj)
        {
            // DotNetObjectInstance properties are handled earlier; if we get here,
            // treat as a method and fall through to wrapper creation.
        }
        else if (instance is BuiltIns.DotNetTypeInstance dotNetType)
        {
            // DotNetTypeInstance only exposes static methods via wrapper.
        }

        // Create a wrapper function that calls the built-in method
        var wrapper = new FunctionValue(null, null, false, null);
        wrapper.BuiltInInstance = instance;
        wrapper.BuiltInMethod = member;
        return RuntimeValue.Function(wrapper);
    }
    
    private RuntimeValue CallBuiltInMethod(ObjectInstance instance, string methodName, List<RuntimeValue> arguments)
    {
        if (instance is BuiltIns.LLMClientInstance llmClient)
        {
            return llmClient.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.OpenRouterClientInstance openRouterClient)
        {
            // OpenRouterClientInstance inherits from LLMClientInstance, so it can use the same CallMethod
            return openRouterClient.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LlamaCppClientInstance llamaClient)
        {
            return llamaClient.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LlamaEmbedderInstance llamaEmbedder)
        {
            return llamaEmbedder.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
        {
            return bridgeClient.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LLMServerInstance llmServer)
        {
            return llmServer.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ConversationInstance conv)
        {
            return conv.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.ToolInstance tool)
        {
            return tool.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.AgentInstance agent)
        {
            return agent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.CodingAgentInstance codingAgent)
        {
            // CodingAgentInstance inherits from AgentInstance, so it can use the same CallMethod
            return codingAgent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.GitAgentInstance gitAgent)
        {
            // GitAgentInstance inherits from AgentInstance, so it can use the same CallMethod
            return gitAgent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.DevAgentInstance devAgent)
        {
            // DevAgentInstance inherits from AgentInstance, so it can use the same CallMethod
            return devAgent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.MALDACodingAgentInstance splCodingAgent)
        {
            // MALDACodingAgentInstance inherits from AgentInstance, so it can use the same CallMethod
            return splCodingAgent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.MCPClientInstance mcpClient)
        {
            return mcpClient.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.HTMLCacheInstance htmlCache)
        {
            return htmlCache.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RestServerInstance restServer)
        {
            return restServer.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.HttpServerInstance httpServer)
        {
            return httpServer.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RequestContextInstance requestContext)
        {
            return requestContext.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RequestAuthContextInstance requestAuthContext)
        {
            return requestAuthContext.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RequestSessionContextInstance requestSession)
        {
            return requestSession.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.ResponseContextInstance responseContext)
        {
            return responseContext.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.MiddlewareNextCallbackInstance nextCallback)
        {
            return nextCallback.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.MCPServerInstance mcpServer)
        {
            return mcpServer.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.SqlServerClientInstance sqlServerClient)
        {
            return sqlServerClient.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.PostgresClientInstance postgresClient)
        {
            return postgresClient.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.SqliteClientInstance sqliteClient)
        {
            return sqliteClient.CallMethod(methodName, arguments);
        }
        else if (instance is ArrayInstance array)
        {
            return array.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.JsonObject jsonObject)
        {
            return jsonObject.CallMethod(methodName, arguments, this);
        }
        else if (instance is DictionaryInstance dict)
        {
            return dict.CallMethod(methodName, arguments, this);
        }
        else if (instance is GraphInstance graph)
        {
            return graph.CallMethod(methodName, arguments, this);
        }
        else if (instance is VectorDBInstance vectorDB)
        {
            return vectorDB.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.RetrieverInstance retriever)
        {
            return retriever.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ComposedPipeInstance composedPipe)
        {
            return composedPipe.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.GraphMemoryInstance graphMemory)
        {
            return graphMemory.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.PromptInstance promptInstance)
        {
            return promptInstance.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ProgressContextWrapper progressCtx)
        {
            return progressCtx.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.MathInstance math)
        {
            return math.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.StrInstance strModule)
        {
            return strModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.IoInstance ioModule)
        {
            return ioModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ResultInstance resultModule)
        {
            return resultModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.OptionInstance optionModule)
        {
            return optionModule.CallMethod(methodName, arguments, this);
        }
        else if (instance.GetType().FullName == "MaldaLang.Timeseries.TaInstance")
        {
            var result = instance.GetType().GetMethod("CallMethod")?.Invoke(instance, new object[] { methodName, arguments, this });
            return result is RuntimeValue runtimeValue ? runtimeValue : RuntimeValue.Null();
        }
        else if (instance is BuiltIns.UiFrameworkInstance uiFramework)
        {
            return uiFramework.CallMethod(methodName, arguments);
        }
        else if (instance is ActorReferenceWrapper actorRefWrapper)
        {
            if (methodName == "stop")
            {
                if (arguments.Count != 0)
                {
                    throw new RuntimeException("stop() expects 0 arguments");
                }
                actorRefWrapper.ActorReference.Stop();
                return RuntimeValue.Null();
            }
            throw new RuntimeException($"ActorReference has no method '{methodName}'. Available methods: stop()");
        }
        else if (instance is BuiltIns.DotNetObjectInstance dotNetObj)
        {
            return dotNetObj.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.DotNetTypeInstance dotNetType)
        {
            return dotNetType.CallStaticMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.AnsiConsoleInstance ansiConsole)
        {
            // Check if it's an async method
            if (methodName == "status" || methodName == "prompt" || methodName == "progress")
            {
                throw new RuntimeException($"AnsiConsole.{methodName}() is an async method and must be awaited");
            }
            return ansiConsole.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ArduinoConnectionInstance arduinoConnection)
        {
            return arduinoConnection.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.SerialConnectionInstance serialConnection)
        {
            return serialConnection.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RestClientInstance restClient)
        {
            return restClient.CallMethod(methodName, arguments);
        }
        throw new RuntimeException($"Unknown built-in method: {methodName}");
    }
    
    private async Task<RuntimeValue> CallBuiltInMethodAsync(ObjectInstance instance, string methodName, List<RuntimeValue> arguments)
    {
        if (instance is BuiltIns.AnsiConsoleInstance ansiConsole)
        {
            if (methodName == "status" || methodName == "prompt" || methodName == "progress")
            {
                return await ansiConsole.CallMethodAsync(methodName, arguments, this);
            }
            // Fall back to sync method
            return ansiConsole.CallMethod(methodName, arguments, this);
        }
        if (instance is BuiltIns.StdLibModuleInstance stdLibModule &&
            BuiltIns.StdLibModuleInstance.RequiresAsyncCall(methodName))
        {
            return await stdLibModule.CallMethodAsync(methodName, arguments, this);
        }
        // For other instances, try sync method
        return CallBuiltInMethod(instance, methodName, arguments);
    }
    
    private async Task<RuntimeValue> CreateAgentAsync(List<Expression> args)
    {
        // Agent(name, role, instructions, client?) — client optional; defaults to local LLM
        // (same convention as CodingAgent / GitAgent / HumanAgent).
        if (args.Count < 3 || args.Count > 4)
            throw new RuntimeException("Agent() expects 3 or 4 arguments: (name, role, instructions, client?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("Agent() expects (string, string, string, LLMClient?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        BuiltIns.LlamaCppClientInstance? llamaClient = null;
        BuiltIns.LLMClientBridge.LLMClientBridgeInstance? bridgeClient = null;

        if (args.Count == 4)
        {
            var client = await EvaluateAsync(args[3]);
            if (client.Type == ValueType.Null)
            {
                // Omitted client → default local below.
            }
            else if (client.Type != ValueType.Object)
            {
                throw new RuntimeException("Agent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
            }
            else
            {
                var clientObj = client.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance llm)
                {
                    llmClient = llm;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llama)
                {
                    llamaClient = llama;
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridge)
                {
                    bridgeClient = bridge;
                }
                else
                {
                    throw new RuntimeException("Agent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
            }
        }

        if (llmClient == null && llamaClient == null && bridgeClient == null)
            llamaClient = BuiltIns.DefaultLocalLlm.GetDefaultLocalClient();
        
        var agent = new BuiltIns.AgentInstance();
        agent.Initialize(name.AsString(), role.AsString(), instructions.AsString(), llmClient, llamaClient, bridgeClient, _inputProvider);
        agent.SetInterpreter(this);
        
        return RuntimeValue.Object(agent);
    }
    
    private async Task<RuntimeValue> CreateCodingAgentAsync(List<Expression> args)
    {
        // CodingAgent(name, role, instructions, client?, workingDirectory?)
        if (args.Count < 3)
            throw new RuntimeException("CodingAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        if (args.Count > 5)
            throw new RuntimeException("CodingAgent() expects at most 5 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("CodingAgent() expects (string, string, string, LLMClient?, string?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        
        // Determine which arguments are provided
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Create CodingAgent with LlamaCppClient
                    var llamaCodingAgent = new BuiltIns.CodingAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        _inputProvider
                    );
                    llamaCodingAgent.SetInterpreter(this);
                    return RuntimeValue.Object(llamaCodingAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Create CodingAgent with LLMClientBridge
                    var bridgeCodingAgent = new BuiltIns.CodingAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        _inputProvider
                    );
                    bridgeCodingAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeCodingAgent);
                }
                else
                {
                    throw new RuntimeException("CodingAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count == 5)
                {
                    var workingDirValue = await EvaluateAsync(args[4]);
                    if (workingDirValue.Type != ValueType.String)
                        throw new RuntimeException("CodingAgent() workingDirectory argument must be a string");
                    workingDirectory = workingDirValue.AsString();
                }
            }
            else
            {
                throw new RuntimeException("CodingAgent() fourth argument must be an LLMClient or string (workingDirectory)");
            }
        }
        
        var codingAgent = new BuiltIns.CodingAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            _inputProvider
        );
        codingAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(codingAgent);
    }
    
    private async Task<RuntimeValue> CreateGitAgentAsync(List<Expression> args)
    {
        // GitAgent(name, role, instructions, client?, workingDirectory?)
        if (args.Count < 3)
            throw new RuntimeException("GitAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        if (args.Count > 5)
            throw new RuntimeException("GitAgent() expects at most 5 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("GitAgent() expects (string, string, string, LLMClient?, string?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        
        // Determine which arguments are provided
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Create GitAgent with LlamaCppClient
                    var llamaGitAgent = new BuiltIns.GitAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        _inputProvider
                    );
                    llamaGitAgent.SetInterpreter(this);
                    return RuntimeValue.Object(llamaGitAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Create GitAgent with LLMClientBridge
                    var bridgeGitAgent = new BuiltIns.GitAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        _inputProvider
                    );
                    bridgeGitAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeGitAgent);
                }
                else
                {
                    throw new RuntimeException("GitAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count == 5)
                {
                    var workingDirValue = await EvaluateAsync(args[4]);
                    if (workingDirValue.Type != ValueType.String)
                        throw new RuntimeException("GitAgent() workingDirectory argument must be a string");
                    workingDirectory = workingDirValue.AsString();
                }
            }
            else
            {
                throw new RuntimeException("GitAgent() fourth argument must be an LLMClient or string (workingDirectory)");
            }
        }
        
        var gitAgent = new BuiltIns.GitAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            _inputProvider
        );
        gitAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(gitAgent);
    }
    
    private async Task<RuntimeValue> CreateHumanAgentAsync(List<Expression> args)
    {
        // HumanAgent(name, role, instructions, client?, workingDirectory?)
        if (args.Count < 3)
            throw new RuntimeException("HumanAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        if (args.Count > 5)
            throw new RuntimeException("HumanAgent() expects at most 5 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("HumanAgent() expects (string, string, string, LLMClient?, string?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        
        // Determine which arguments are provided
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Create HumanAgent with LlamaCppClient
                    var llamaHumanAgent = new BuiltIns.HumanAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        _inputProvider
                    );
                    return RuntimeValue.Object(llamaHumanAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Create HumanAgent with LLMClientBridge
                    var bridgeHumanAgent = new BuiltIns.HumanAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        _inputProvider
                    );
                    bridgeHumanAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeHumanAgent);
                }
                else
                {
                    throw new RuntimeException("HumanAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count == 5)
                {
                    var workingDirValue = await EvaluateAsync(args[4]);
                    if (workingDirValue.Type != ValueType.String)
                        throw new RuntimeException("HumanAgent() workingDirectory argument must be a string");
                    workingDirectory = workingDirValue.AsString();
                }
            }
            else
            {
                throw new RuntimeException("HumanAgent() fourth argument must be an LLMClient or string (workingDirectory)");
            }
        }
        
        var humanAgent = new BuiltIns.HumanAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            _inputProvider
        );
        humanAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(humanAgent);
    }
    
    private async Task<RuntimeValue> CreateDevAgentAsync(List<Expression> args)
    {
        // DevAgent(name, role, instructions, client?, workingDirectory?, includeSymbols?, prdAuthorOnly?)
        if (args.Count < 3)
            throw new RuntimeException("DevAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?, includeSymbols?, prdAuthorOnly?)");
        
        if (args.Count > 7)
            throw new RuntimeException("DevAgent() expects at most 7 arguments: (name, role, instructions, client?, workingDirectory?, includeSymbols?, prdAuthorOnly?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("DevAgent() expects (string, string, string, LLMClient?, string?, bool?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        bool includeSymbols = false;
        bool prdAuthorOnly = false;
        
        if (args.Count == 7)
        {
            var prdAuthorArg = await EvaluateAsync(args[6]);
            if (prdAuthorArg.Type != ValueType.Boolean)
                throw new RuntimeException("DevAgent() prdAuthorOnly argument must be a boolean");
            prdAuthorOnly = prdAuthorArg.AsBoolean();
        }
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
                
                // Check for 5th argument (includeSymbols)
                if (args.Count >= 5)
                {
                    var arg4 = await EvaluateAsync(args[4]);
                    if (arg4.Type != ValueType.Boolean)
                        throw new RuntimeException("DevAgent() includeSymbols argument must be a boolean");
                    includeSymbols = arg4.AsBoolean();
                }
            }
            // If 4th argument is a boolean, it's includeSymbols (client and workingDirectory were omitted)
            else if (arg3.Type == ValueType.Boolean)
            {
                includeSymbols = arg3.AsBoolean();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Handle workingDirectory and includeSymbols for LlamaCppClient
                    if (args.Count >= 5)
                    {
                        var arg4 = await EvaluateAsync(args[4]);
                        if (arg4.Type == ValueType.String)
                        {
                            workingDirectory = arg4.AsString();
                            
                            // Check for 6th argument (includeSymbols)
                            if (args.Count >= 6)
                            {
                                var arg5 = await EvaluateAsync(args[5]);
                                if (arg5.Type != ValueType.Boolean)
                                    throw new RuntimeException("DevAgent() includeSymbols argument must be a boolean");
                                includeSymbols = arg5.AsBoolean();
                            }
                        }
                        else if (arg4.Type == ValueType.Boolean)
                        {
                            includeSymbols = arg4.AsBoolean();
                        }
                        else
                        {
                            throw new RuntimeException("DevAgent() fifth argument must be a string (workingDirectory) or bool (includeSymbols)");
                        }
                    }
                    
                    // Create DevAgent with LlamaCppClient
                    var llamaDevAgent = new BuiltIns.DevAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        includeSymbols,
                        _inputProvider,
                        readOnly: false,
                        prdAuthorOnly: prdAuthorOnly
                    );
                    llamaDevAgent.SetInterpreter(this);
                    return RuntimeValue.Object(llamaDevAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Handle workingDirectory and includeSymbols for LLMClientBridge
                    if (args.Count >= 5)
                    {
                        var arg4 = await EvaluateAsync(args[4]);
                        if (arg4.Type == ValueType.String)
                        {
                            workingDirectory = arg4.AsString();
                            
                            // Check for 6th argument (includeSymbols)
                            if (args.Count >= 6)
                            {
                                var arg5 = await EvaluateAsync(args[5]);
                                if (arg5.Type != ValueType.Boolean)
                                    throw new RuntimeException("DevAgent() includeSymbols argument must be a boolean");
                                includeSymbols = arg5.AsBoolean();
                            }
                        }
                        else if (arg4.Type == ValueType.Boolean)
                        {
                            includeSymbols = arg4.AsBoolean();
                        }
                        else
                        {
                            throw new RuntimeException("DevAgent() fifth argument must be a string (workingDirectory) or bool (includeSymbols)");
                        }
                    }
                    
                    // Create DevAgent with LLMClientBridge
                    var bridgeDevAgent = new BuiltIns.DevAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        includeSymbols,
                        _inputProvider,
                        readOnly: false,
                        prdAuthorOnly: prdAuthorOnly
                    );
                    bridgeDevAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeDevAgent);
                }
                else
                {
                    throw new RuntimeException("DevAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count >= 5)
                {
                    var arg4 = await EvaluateAsync(args[4]);
                    if (arg4.Type == ValueType.String)
                    {
                        workingDirectory = arg4.AsString();
                        
                        // Check for 6th argument (includeSymbols)
                        if (args.Count >= 6)
                        {
                            var arg5 = await EvaluateAsync(args[5]);
                            if (arg5.Type != ValueType.Boolean)
                                throw new RuntimeException("DevAgent() includeSymbols argument must be a boolean");
                            includeSymbols = arg5.AsBoolean();
                        }
                    }
                    else if (arg4.Type == ValueType.Boolean)
                    {
                        includeSymbols = arg4.AsBoolean();
                    }
                    else
                    {
                        throw new RuntimeException("DevAgent() fifth argument must be a string (workingDirectory) or bool (includeSymbols)");
                    }
                }
            }
            else
            {
                throw new RuntimeException("DevAgent() fourth argument must be an LLMClient, string (workingDirectory), or bool (includeSymbols)");
            }
        }
        
        var devAgent = new BuiltIns.DevAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            includeSymbols,
            _inputProvider,
            readOnly: false,
            prdAuthorOnly: prdAuthorOnly
        );
        devAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(devAgent);
    }
    
    private async Task<RuntimeValue> CreateMALDACodingAgentAsync(List<Expression> args)
    {
        // MALDACodingAgent(name, role, instructions, client?, workingDirectory?)
        if (args.Count < 3)
            throw new RuntimeException("MALDACodingAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        if (args.Count > 5)
            throw new RuntimeException("MALDACodingAgent() expects at most 5 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("MALDACodingAgent() expects (string, string, string, LLMClient?, string?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        
        // Determine which arguments are provided
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Create MALDACodingAgent with LlamaCppClient
                    var llamaSplCodingAgent = new BuiltIns.MALDACodingAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        _inputProvider
                    );
                    llamaSplCodingAgent.SetInterpreter(this);
                    return RuntimeValue.Object(llamaSplCodingAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Create MALDACodingAgent with LLMClientBridge
                    var bridgeSplCodingAgent = new BuiltIns.MALDACodingAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        _inputProvider
                    );
                    bridgeSplCodingAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeSplCodingAgent);
                }
                else
                {
                    throw new RuntimeException("MALDACodingAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count == 5)
                {
                    var workingDirValue = await EvaluateAsync(args[4]);
                    if (workingDirValue.Type != ValueType.String)
                        throw new RuntimeException("MALDACodingAgent() workingDirectory argument must be a string");
                    workingDirectory = workingDirValue.AsString();
                }
            }
            else
            {
                throw new RuntimeException("MALDACodingAgent() fourth argument must be an LLMClient or string (workingDirectory)");
            }
        }
        
        var splCodingAgent = new BuiltIns.MALDACodingAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            _inputProvider
        );
        splCodingAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(splCodingAgent);
    }
    
    private async Task<RuntimeValue> CreateRestServerAsync(List<Expression> args)
    {
        if (args.Count > 2)
            throw new RuntimeException("RestServer() expects 0-2 arguments: (port?, host?)");

        if (args.Count == 0)
        {
            var deferred = new BuiltIns.RestServerInstance(0, null, this);
            return RuntimeValue.Object(deferred);
        }
        
        var port = await EvaluateAsync(args[0]);
        if (port.Type != ValueType.Integer)
            throw new RuntimeException("RestServer() port must be an integer");
        
        var portNum = port.AsInteger();
        if (portNum != 0 && (portNum < 1 || portNum > 65535))
            throw new RuntimeException("RestServer() port must be 0 (deferred/mounted) or between 1 and 65535");
        
        string? host = null;
        if (args.Count == 2)
        {
            var hostValue = await EvaluateAsync(args[1]);
            if (hostValue.Type != ValueType.String)
                throw new RuntimeException("RestServer() host must be a string");
            host = hostValue.AsString();
        }
        
        var server = new BuiltIns.RestServerInstance(portNum, host, this);
        return RuntimeValue.Object(server);
    }
    
    private async Task<RuntimeValue> CreateRestClientAsync(List<Expression> args)
    {
        string? baseUrl = null;
        int? timeout = null;
        
        if (args.Count > 0)
        {
            var baseUrlValue = await EvaluateAsync(args[0]);
            if (baseUrlValue.Type != ValueType.String)
                throw new RuntimeException("RestClient() baseUrl must be a string");
            baseUrl = baseUrlValue.AsString();
        }
        
        if (args.Count > 1)
        {
            var timeoutValue = await EvaluateAsync(args[1]);
            if (timeoutValue.Type != ValueType.Integer)
                throw new RuntimeException("RestClient() timeout must be an integer");
            timeout = timeoutValue.AsInteger();
        }
        
        var client = new BuiltIns.RestClientInstance(baseUrl, timeout);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateHttpServerAsync(List<Expression> args)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new RuntimeException("HttpServer() expects 1-3 arguments: (port, webDirectory?, pathBase?)");
        
        var port = await EvaluateAsync(args[0]);
        if (port.Type != ValueType.Integer)
            throw new RuntimeException("HttpServer() port must be an integer");
        
        var portNum = port.AsInteger();
        if (portNum < 1 || portNum > 65535)
            throw new RuntimeException("HttpServer() port must be between 1 and 65535");
        
        string? webDirectory = null;
        if (args.Count >= 2)
        {
            var webDirValue = await EvaluateAsync(args[1]);
            if (webDirValue.Type != ValueType.Null && webDirValue.Type != ValueType.String)
                throw new RuntimeException("HttpServer() webDirectory must be a string or null");
            webDirectory = webDirValue.Type == ValueType.String ? webDirValue.AsString() : null;
        }
        
        string? pathBase = null;
        if (args.Count >= 3)
        {
            var pathBaseValue = await EvaluateAsync(args[2]);
            if (pathBaseValue.Type != ValueType.Null && pathBaseValue.Type != ValueType.String)
                throw new RuntimeException("HttpServer() pathBase must be a string or null");
            pathBase = pathBaseValue.Type == ValueType.String ? pathBaseValue.AsString() : null;
        }
        
        var server = new BuiltIns.HttpServerInstance(portNum, webDirectory, this, pathBase);
        return RuntimeValue.Object(server);
    }
    
    private async Task<RuntimeValue> CreateMCPServerAsync(List<Expression> args)
    {
        // MCPServer(transportType?, port?)
        // transportType: "stdio" (default) or "http"
        // port: Required for HTTP transport
        
        string? transportType = null;
        int? port = null;
        
        if (args.Count > 0)
        {
            var transportValue = await EvaluateAsync(args[0]);
            if (transportValue.Type != ValueType.String)
                throw new RuntimeException("MCPServer() transportType must be a string");
            transportType = transportValue.AsString();
            
            if (transportType != "stdio" && transportType != "http")
                throw new RuntimeException("MCPServer() transportType must be 'stdio' or 'http'");
        }
        
        if (args.Count > 1)
        {
            var portValue = await EvaluateAsync(args[1]);
            if (portValue.Type != ValueType.Integer)
                throw new RuntimeException("MCPServer() port must be an integer");
            
            var portNum = portValue.AsInteger();
            if (portNum < 1 || portNum > 65535)
                throw new RuntimeException("MCPServer() port must be between 1 and 65535");
            
            port = portNum;
        }
        
        if (transportType == "http" && port == null)
            throw new RuntimeException("MCPServer() port is required when using 'http' transport");
        
        var server = new BuiltIns.MCPServerInstance(transportType, port, this);
        return RuntimeValue.Object(server);
    }
    
    private async Task<RuntimeValue> CreateMCPClientAsync(List<Expression> args)
    {
        // MCPClient(serverName)
        if (args.Count != 1)
            throw new RuntimeException("MCPClient() expects 1 argument: (serverName)");
        
        var serverNameValue = await EvaluateAsync(args[0]);
        if (serverNameValue.Type != ValueType.String)
            throw new RuntimeException("MCPClient() serverName must be a string");
        
        var serverName = serverNameValue.AsString();
        if (string.IsNullOrWhiteSpace(serverName))
            throw new RuntimeException("MCPClient() serverName cannot be empty");
        
        var client = new BuiltIns.MCPClientInstance(serverName);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateACPClientAsync(List<Expression> args)
    {
        // ACPClient(baseUrl, apiKey?)
        if (args.Count < 1)
            throw new RuntimeException("ACPClient() expects at least 1 argument: (baseUrl, apiKey?)");
        
        var baseUrlValue = await EvaluateAsync(args[0]);
        if (baseUrlValue.Type != ValueType.String)
            throw new RuntimeException("ACPClient() baseUrl must be a string");
        
        var baseUrl = baseUrlValue.AsString();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new RuntimeException("ACPClient() baseUrl cannot be empty");
        
        string? apiKey = null;
        if (args.Count > 1)
        {
            var apiKeyValue = await EvaluateAsync(args[1]);
            if (apiKeyValue.Type != ValueType.String)
                throw new RuntimeException("ACPClient() apiKey must be a string");
            apiKey = apiKeyValue.AsString();
        }
        
        var client = new BuiltIns.ACP.ACPClientInstance(baseUrl, apiKey);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateACPServerAsync(List<Expression> args)
    {
        // ACPServer(port)
        if (args.Count != 1)
            throw new RuntimeException("ACPServer() expects 1 argument: (port)");
        
        var portValue = await EvaluateAsync(args[0]);
        if (portValue.Type != ValueType.Integer)
            throw new RuntimeException("ACPServer() port must be an integer");
        
        var port = portValue.AsInteger();
        if (port < 1 || port > 65535)
            throw new RuntimeException("ACPServer() port must be between 1 and 65535");
        
        var server = new BuiltIns.ACP.ACPServerInstance(port);
        return RuntimeValue.Object(server);
    }
    
    private async Task<RuntimeValue> CreateACPAgentToolAsync(List<Expression> args)
    {
        // ACPAgentTool(acpClient, agentId, description)
        if (args.Count != 3)
            throw new RuntimeException("ACPAgentTool() expects 3 arguments: (acpClient, agentId, description)");
        
        var clientValue = await EvaluateAsync(args[0]);
        if (clientValue.Type != ValueType.Object)
            throw new RuntimeException("ACPAgentTool() acpClient must be an ACPClient instance");
        
        var clientObj = clientValue.AsObject();
        if (clientObj is not BuiltIns.ACP.ACPClientInstance acpClient)
            throw new RuntimeException("ACPAgentTool() acpClient must be an ACPClient instance");
        
        var agentIdValue = await EvaluateAsync(args[1]);
        if (agentIdValue.Type != ValueType.String)
            throw new RuntimeException("ACPAgentTool() agentId must be a string");
        
        var descriptionValue = await EvaluateAsync(args[2]);
        if (descriptionValue.Type != ValueType.String)
            throw new RuntimeException("ACPAgentTool() description must be a string");
        
        var agentId = agentIdValue.AsString();
        var description = descriptionValue.AsString();
        
        var tool = new BuiltIns.ACP.ACPAgentToolInstance(acpClient, agentId, description);
        return RuntimeValue.Object(tool);
    }
    
    private async Task<RuntimeValue> CreateLLMClientBridgeAsync(List<Expression> args)
    {
        if (args.Count == 0)
            throw new RuntimeException("LLMClientBridge() expects at least 1 argument");
        
        var bridge = new BuiltIns.LLMClientBridge.LLMClientBridgeInstance();
        BuiltIns.LLMClientBridge.IBackendAdapter? adapter = null;
        
        // Detect backend type from arguments
        var firstArg = await EvaluateAsync(args[0]);
        
        if (firstArg.Type == ValueType.String)
        {
            var firstStr = firstArg.AsString();
            
            // Check for "openrouter"
            if (firstStr == "openrouter")
            {
                // OpenRouter: LLMClientBridge("openrouter", model?)
                string? model = null;
                if (args.Count > 1)
                {
                    var modelValue = await EvaluateAsync(args[1]);
                    if (modelValue.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() model argument must be a string");
                    model = modelValue.AsString();
                }
                
                var openRouterClient = new BuiltIns.OpenRouterClientInstance(model);
                adapter = new BuiltIns.LLMClientBridge.BackendAdapters.OpenRouterAdapter(openRouterClient);
            }
            // Check for "local"
            else if (firstStr == "local")
            {
                // Direct local: LLMClientBridge("local", modelPath, gpuLayers?)
                if (args.Count < 2)
                    throw new RuntimeException("LLMClientBridge() expects modelPath when using 'local'");
                
                var modelPathValue = await EvaluateAsync(args[1]);
                if (modelPathValue.Type != ValueType.String)
                    throw new RuntimeException("LLMClientBridge() modelPath must be a string");
                
                var llamaClient = new BuiltIns.LlamaCppClientInstance();
                llamaClient.ModelPath = modelPathValue.AsString();
                
                // Optional GPU layers
                if (args.Count > 2)
                {
                    var gpuLayersValue = await EvaluateAsync(args[2]);
                    if (gpuLayersValue.Type != ValueType.Integer)
                        throw new RuntimeException("LLMClientBridge() gpuLayers must be an integer");
                    llamaClient.GpuLayerCount = gpuLayersValue.AsInteger();
                }
                
                adapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
            }
            // Check for HTTP URL (local server)
            else if (firstStr.StartsWith("http://") || firstStr.StartsWith("https://"))
            {
                // Local server: LLMClientBridge(serverUrl, apiKey?)
                string? apiKey = null;
                if (args.Count > 1)
                {
                    var apiKeyValue = await EvaluateAsync(args[1]);
                    if (apiKeyValue.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() apiKey must be a string");
                    apiKey = apiKeyValue.AsString();
                }
                
                adapter = new BuiltIns.LLMClientBridge.BackendAdapters.LocalServerAdapter(firstStr, apiKey);
            }
            // Remote API: LLMClientBridge(apiUrl, apiKey, model)
            else if (args.Count >= 3)
            {
                var apiUrl = firstStr;
                var apiKeyValue = await EvaluateAsync(args[1]);
                var modelValue = await EvaluateAsync(args[2]);
                
                if (apiKeyValue.Type != ValueType.String || modelValue.Type != ValueType.String)
                    throw new RuntimeException("LLMClientBridge() expects (string, string, string) for remote API");
                
                var llmClient = new BuiltIns.LLMClientInstance();
                llmClient.ApiUrl = apiUrl;
                llmClient.ApiKey = apiKeyValue.AsString();
                llmClient.Model = modelValue.AsString();
                
                adapter = new BuiltIns.LLMClientBridge.BackendAdapters.RemoteApiAdapter(llmClient);
            }
            else
            {
                throw new RuntimeException("LLMClientBridge() invalid arguments. Expected: (serverUrl), (apiUrl, apiKey, model), (\"openrouter\", model?), or (\"local\", modelPath, gpuLayers?)");
            }
        }
        else if (firstArg.Type == ValueType.Array)
        {
            // Multiple backends: LLMClientBridge([{backend1}, {backend2}, ...])
            var backendsArray = firstArg.AsArray();
            
            if (backendsArray.Count == 0)
                throw new RuntimeException("LLMClientBridge() backend array cannot be empty");
            
            // Process each backend configuration
            foreach (var backendConfig in backendsArray)
            {
                if (backendConfig.Type != ValueType.Object)
                {
                    throw new RuntimeException("LLMClientBridge() backend configurations must be objects");
                }
                
                var backendObj = backendConfig.AsObject();
                
                // Helper to get property from object
                RuntimeValue? GetProp(string name)
                {
                    try
                    {
                        return backendObj.Get(name, null);
                    }
                    catch
                    {
                        return null;
                    }
                }
                
                var typeProp = GetProp("type");
                var urlProp = GetProp("url");
                
                if (typeProp == null || typeProp.Type != ValueType.String)
                {
                    throw new RuntimeException("LLMClientBridge() backend must have 'type' property");
                }
                
                    var backendType = typeProp.AsString();
                    BuiltIns.LLMClientBridge.IBackendAdapter? backendAdapter = null;
                
                if (backendType == "server")
                {
                    if (urlProp == null || urlProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() server backend must have 'url' property");
                    
                    var apiKeyProp = GetProp("apiKey");
                    var apiKey = apiKeyProp != null && apiKeyProp.Type == ValueType.String 
                        ? apiKeyProp.AsString() 
                        : null;
                    
                    backendAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.LocalServerAdapter(
                        urlProp.AsString(), apiKey);
                }
                else if (backendType == "api")
                {
                    if (urlProp == null || urlProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() api backend must have 'url' property");
                    
                    var apiKeyProp = GetProp("apiKey");
                    var modelProp = GetProp("model");
                    
                    if (apiKeyProp == null || apiKeyProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() api backend must have 'apiKey' property");
                    if (modelProp == null || modelProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() api backend must have 'model' property");
                    
                    var llmClient = new BuiltIns.LLMClientInstance();
                    llmClient.ApiUrl = urlProp.AsString();
                    llmClient.ApiKey = apiKeyProp.AsString();
                    llmClient.Model = modelProp.AsString();
                    
                    backendAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.RemoteApiAdapter(llmClient);
                }
                else if (backendType == "openrouter")
                {
                    var modelProp = GetProp("model");
                    var model = modelProp != null && modelProp.Type == ValueType.String 
                        ? modelProp.AsString() 
                        : null;
                    
                    var openRouterClient = new BuiltIns.OpenRouterClientInstance(model);
                    backendAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.OpenRouterAdapter(openRouterClient);
                }
                else if (backendType == "local")
                {
                    var modelPathProp = GetProp("modelPath");
                    if (modelPathProp == null || modelPathProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() local backend must have 'modelPath' property");
                    
                    var llamaClient = new BuiltIns.LlamaCppClientInstance();
                    llamaClient.ModelPath = modelPathProp.AsString();
                    
                    var gpuLayersProp = GetProp("gpuLayers");
                    if (gpuLayersProp != null && gpuLayersProp.Type == ValueType.Integer)
                    {
                        llamaClient.GpuLayerCount = gpuLayersProp.AsInteger();
                    }
                    
                    backendAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                }
                else
                {
                    throw new RuntimeException($"LLMClientBridge() unknown backend type: {backendType}");
                }
                
                if (backendAdapter is not null)
                {
                    bridge.AddBackend(backendAdapter);
                    
                    // First backend becomes primary
                    if (bridge._backends.Count == 1)
                    {
                        bridge.SetPrimaryAdapter(backendAdapter);
                    }
                    
                    // Add to failover handler
                    var priorityProp = GetProp("priority");
                    var priority = priorityProp != null && priorityProp.Type == ValueType.Integer
                        ? priorityProp.AsInteger()
                        : bridge._backends.Count - 1;
                    
                    // Initialize failover handler if not already done
                    if (bridge._failoverHandler == null)
                    {
                        bridge._failoverHandler = new BuiltIns.LLMClientBridge.Failover.FailoverHandler();
                    }
                    bridge._failoverHandler.AddBackend(bridge._backends.Count - 1, priority);
                }
            }
            
            // Initialize load balancer for multiple backends
            bridge._loadBalancer = new BuiltIns.LLMClientBridge.LoadBalancing.LoadBalancer();
            
            return RuntimeValue.Object(bridge);
        }
        else
        {
            throw new RuntimeException("LLMClientBridge() first argument must be a string or array");
        }
        
        if (adapter is not null)
        {
            bridge.SetPrimaryAdapter(adapter);
        }
        
        return RuntimeValue.Object(bridge);
    }
    
    private async Task<RuntimeValue> CreateSqlServerClientAsync(List<Expression> args)
    {
        // SqlServerClient() or SqlServerClient(connectionString?)
        if (args.Count > 1)
            throw new RuntimeException("SqlServerClient() expects 0 or 1 argument: (connectionString?)");
        
        string? connectionString = null;
        if (args.Count == 1)
        {
            var connStrValue = await EvaluateAsync(args[0]);
            if (connStrValue.Type != ValueType.String)
                throw new RuntimeException("SqlServerClient() connectionString argument must be a string");
            connectionString = connStrValue.AsString();
        }
        
        var client = new BuiltIns.SqlServerClientInstance(connectionString);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreatePostgresClientAsync(List<Expression> args)
    {
        // PostgresClient() or PostgresClient(connectionString?)
        if (args.Count > 1)
            throw new RuntimeException("PostgresClient() expects 0 or 1 argument: (connectionString?)");
        
        string? connectionString = null;
        if (args.Count == 1)
        {
            var connStrValue = await EvaluateAsync(args[0]);
            if (connStrValue.Type != ValueType.String)
                throw new RuntimeException("PostgresClient() connectionString argument must be a string");
            connectionString = connStrValue.AsString();
        }
        
        var client = new BuiltIns.PostgresClientInstance(connectionString);
        return RuntimeValue.Object(client);
    }

    private async Task<RuntimeValue> CreateSqliteClientAsync(List<Expression> args)
    {
        // SqliteClient() or SqliteClient(connectionString?)
        if (args.Count > 1)
            throw new RuntimeException("SqliteClient() expects 0 or 1 argument: (connectionString?)");

        string? connectionString = null;
        if (args.Count == 1)
        {
            var connStrValue = await EvaluateAsync(args[0]);
            if (connStrValue.Type != ValueType.String)
                throw new RuntimeException("SqliteClient() connectionString argument must be a string");
            connectionString = connStrValue.AsString();
        }

        var client = new BuiltIns.SqliteClientInstance(connectionString);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateSerialConnectionAsync(List<Expression> args)
    {
        // SerialConnection() - no arguments, connection is established via connect() method
        if (args.Count != 0)
            throw new RuntimeException("SerialConnection() expects 0 arguments. Use connect(portName, baudRate) to establish connection.");
        
        var connection = new BuiltIns.SerialConnectionInstance();
        return RuntimeValue.Object(connection);
    }
    
    private async Task<RuntimeValue> CreateArduinoConnectionAsync(List<Expression> args)
    {
        // ArduinoConnection(url) for HTTP mode
        // ArduinoConnection(port, baudRate) for serial mode
        if (args.Count < 1 || args.Count > 2)
            throw new RuntimeException("ArduinoConnection() expects 1 or 2 arguments: (url) for HTTP mode or (port, baudRate) for serial mode");
        
        if (args.Count == 1)
        {
            // HTTP mode: ArduinoConnection(url)
            var urlValue = await EvaluateAsync(args[0]);
            if (urlValue.Type != ValueType.String)
                throw new RuntimeException("ArduinoConnection() url argument must be a string");
            
            var connection = new BuiltIns.ArduinoConnectionInstance(urlValue.AsString());
            return RuntimeValue.Object(connection);
        }
        else
        {
            // Serial mode: ArduinoConnection(port, baudRate)
            var portValue = await EvaluateAsync(args[0]);
            var baudRateValue = await EvaluateAsync(args[1]);
            
            if (portValue.Type != ValueType.String)
                throw new RuntimeException("ArduinoConnection() port argument must be a string");
            if (baudRateValue.Type != ValueType.Integer)
                throw new RuntimeException("ArduinoConnection() baudRate argument must be an integer");
            
            var connection = new BuiltIns.ArduinoConnectionInstance(portValue.AsString(), baudRateValue.AsInteger());
            return RuntimeValue.Object(connection);
        }
    }
    
    private async Task<RuntimeValue> CreateLLMServerAsync(List<Expression> args)
    {
        if (args.Count < 2)
            throw new RuntimeException("LLMServer() expects at least 2 arguments");
        
        var server = new BuiltIns.LLMServerInstance();
        BuiltIns.LLMClientBridge.LLMClientBridgeInstance? bridge = null;
        BuiltIns.LLMClientBridge.IBackendAdapter? adapter = null;
        int port;
        string? host = null;
        
        // Evaluate port (should be second argument in all patterns)
        var portValue = await EvaluateAsync(args[1]);
        if (portValue.Type != ValueType.Integer)
            throw new RuntimeException("LLMServer() port must be an integer");
        port = portValue.AsInteger();
        
        // Pattern 1: (string modelPath, int port, object? config?)
        if (args[0] != null)
        {
            var firstArg = await EvaluateAsync(args[0]);
            
            if (firstArg.Type == ValueType.String)
            {
                // Model path string - create local bridge
                var modelPath = firstArg.AsString();
                
                // Optional config
                if (args.Count > 2)
                {
                    var configValue = await EvaluateAsync(args[2]);
                    if (configValue.Type == ValueType.Object)
                    {
                        var configObj = configValue.AsObject();
                        var llamaClient = new BuiltIns.LlamaCppClientInstance();
                        llamaClient.ModelPath = modelPath;
                        
                        // Apply config if provided
                        var temperature = GetPropertyFromObject(configObj, "temperature");
                        if (temperature != null && temperature.Type == ValueType.Float)
                        {
                            llamaClient.Temperature = temperature.AsFloat();
                        }
                        
                        var maxTokens = GetPropertyFromObject(configObj, "maxTokens");
                        if (maxTokens != null && maxTokens.Type == ValueType.Integer)
                        {
                            llamaClient.MaxTokens = maxTokens.AsInteger();
                        }
                        
                        var gpuLayers = GetPropertyFromObject(configObj, "gpuLayers");
                        if (gpuLayers != null && gpuLayers.Type == ValueType.Integer)
                        {
                            llamaClient.GpuLayerCount = gpuLayers.AsInteger();
                        }
                        
                        adapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                    }
                    else
                    {
                        // No config, use defaults
                        var llamaClient = new BuiltIns.LlamaCppClientInstance();
                        llamaClient.ModelPath = modelPath;
                        adapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                    }
                }
                else
                {
                    // No config, use defaults
                    var llamaClient = new BuiltIns.LlamaCppClientInstance();
                    llamaClient.ModelPath = modelPath;
                    adapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                }
                
                if (adapter != null)
                {
                    bridge = new BuiltIns.LLMClientBridge.LLMClientBridgeInstance();
                    bridge.SetPrimaryAdapter(adapter);
                    server.SetBridgeCreatedInternally(true);
                }
            }
            // Pattern 2: (LLMClientBridge bridge, int port, string? host?)
            else if (firstArg.Type == ValueType.Object && 
                     firstArg.AsObject() is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeInstance)
            {
                bridge = bridgeInstance;
                
                // Optional host
                if (args.Count > 2)
                {
                    var hostValue = await EvaluateAsync(args[2]);
                    if (hostValue.Type != ValueType.String)
                        throw new RuntimeException("LLMServer() host must be a string");
                    host = hostValue.AsString();
                }
            }
            // Pattern 3: (object config, int port, string? host?)
            else if (firstArg.Type == ValueType.Object)
            {
                // Parse config object and create bridge
                var configObj = firstArg.AsObject();
                
                // Optional host
                if (args.Count > 2)
                {
                    var hostValue = await EvaluateAsync(args[2]);
                    if (hostValue.Type == ValueType.String)
                    {
                        host = hostValue.AsString();
                    }
                }
                
                // Create bridge from config
                bridge = new BuiltIns.LLMClientBridge.LLMClientBridgeInstance();
                
                // Check if it's a single backend config
                var typeProp = GetPropertyFromObject(configObj, "type");
                
                if (typeProp != null && typeProp.Type == ValueType.String)
                {
                    // Single backend config
                    var backendType = typeProp.AsString();
                    BuiltIns.LLMClientBridge.IBackendAdapter? configAdapter = null;
                    
                    if (backendType == "local")
                    {
                        var modelPathProp = GetPropertyFromObject(configObj, "modelPath");
                        if (modelPathProp == null || modelPathProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() local backend must have 'modelPath' property");
                        
                        var llamaClient = new BuiltIns.LlamaCppClientInstance();
                        llamaClient.ModelPath = modelPathProp.AsString();
                        
                        var gpuLayersProp = GetPropertyFromObject(configObj, "gpuLayers");
                        if (gpuLayersProp != null && gpuLayersProp.Type == ValueType.Integer)
                        {
                            llamaClient.GpuLayerCount = gpuLayersProp.AsInteger();
                        }
                        
                        configAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                    }
                    else if (backendType == "server")
                    {
                        var urlProp = GetPropertyFromObject(configObj, "url");
                        if (urlProp == null || urlProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() server backend must have 'url' property");
                        
                        var apiKeyProp = GetPropertyFromObject(configObj, "apiKey");
                        var apiKey = apiKeyProp != null && apiKeyProp.Type == ValueType.String 
                            ? apiKeyProp.AsString() 
                            : null;
                        
                        configAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.LocalServerAdapter(
                            urlProp.AsString(), apiKey);
                    }
                    else if (backendType == "api")
                    {
                        var urlProp = GetPropertyFromObject(configObj, "url");
                        var apiKeyProp = GetPropertyFromObject(configObj, "apiKey");
                        var modelProp = GetPropertyFromObject(configObj, "model");
                        
                        if (urlProp == null || urlProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() api backend must have 'url' property");
                        if (apiKeyProp == null || apiKeyProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() api backend must have 'apiKey' property");
                        if (modelProp == null || modelProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() api backend must have 'model' property");
                        
                        var llmClient = new BuiltIns.LLMClientInstance();
                        llmClient.ApiUrl = urlProp.AsString();
                        llmClient.ApiKey = apiKeyProp.AsString();
                        llmClient.Model = modelProp.AsString();
                        
                        configAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.RemoteApiAdapter(llmClient);
                    }
                    else if (backendType == "openrouter")
                    {
                        var modelProp = GetPropertyFromObject(configObj, "model");
                        var model = modelProp != null && modelProp.Type == ValueType.String 
                            ? modelProp.AsString() 
                            : null;
                        
                        var openRouterClient = new BuiltIns.OpenRouterClientInstance(model);
                        configAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.OpenRouterAdapter(openRouterClient);
                    }
                    else
                    {
                        throw new RuntimeException($"LLMServer() unknown backend type: {backendType}");
                    }
                    
                    if (configAdapter != null)
                    {
                        bridge.SetPrimaryAdapter(configAdapter);
                    }
                    
                    server.SetBridgeCreatedInternally(true);
                }
                else
                {
                    throw new RuntimeException("LLMServer() config object must have 'type' property");
                }
            }
            else
            {
                throw new RuntimeException("LLMServer() first argument must be a string (modelPath), LLMClientBridge instance, or config object");
            }
        }
        
        if (bridge == null)
        {
            throw new RuntimeException("LLMServer() failed to create or initialize bridge");
        }
        
        // Initialize server
        server.Initialize(bridge, port, host, this);
        
        return RuntimeValue.Object(server);
    }
    
    private RuntimeValue? GetPropertyFromObject(ObjectInstance obj, string name)
    {
        try
        {
            return obj.Get(name, null);
        }
        catch
        {
            return null;
        }
    }
    
    private async Task<RuntimeValue> CreateHTMLCacheAsync(List<Expression> args)
    {
        if (args.Count > 3)
            throw new RuntimeException("HTMLCache() expects 0-3 arguments: (cacheDirectory?, maxSize?, expirationHours?)");
        
        string? cacheDir = null;
        int? maxSize = null;
        int? expirationHours = null;
        
        if (args.Count >= 1)
        {
            var dir = await EvaluateAsync(args[0]);
            if (dir.Type != ValueType.String)
                throw new RuntimeException("HTMLCache() cacheDirectory must be a string");
            cacheDir = dir.AsString();
        }
        
        if (args.Count >= 2)
        {
            var size = await EvaluateAsync(args[1]);
            if (size.Type != ValueType.Integer)
                throw new RuntimeException("HTMLCache() maxSize must be an integer");
            maxSize = size.AsInteger();
        }
        
        if (args.Count >= 3)
        {
            var hours = await EvaluateAsync(args[2]);
            if (hours.Type != ValueType.Integer)
                throw new RuntimeException("HTMLCache() expirationHours must be an integer");
            expirationHours = hours.AsInteger();
        }
        
        var cache = new BuiltIns.HTMLCacheInstance(cacheDir, maxSize, expirationHours);
        return RuntimeValue.Object(cache);
    }
    
    private RuntimeValue EvaluateThis()
    {
        if (_currentObject == null)
            throw new RuntimeException("Cannot use 'this' outside of a class.");
        return RuntimeValue.Object(_currentObject);
    }
    
    private Task<RuntimeValue> EvaluateSuperAsync()
    {
        if (_currentClass == null || _currentClass.Superclass == null)
            throw new RuntimeException("Cannot use 'super' outside of a class or without a superclass.");
        // Return the superclass - actual method resolution happens in EvaluateCall
        return Task.FromResult(RuntimeValue.Class(_currentClass.Superclass));
    }
    
    private async Task<RuntimeValue> EvaluateArrayLiteralAsync(ArrayLiteralExpression expr)
    {
        var elements = new List<RuntimeValue>();
        foreach (var element in expr.Elements)
        {
            elements.Add(await EvaluateAsync(element));
        }
        return RuntimeValue.Array(elements);
    }
    
    private async Task<RuntimeValue> EvaluateDictionaryLiteralAsync(DictionaryLiteralExpression expr)
    {
        var entries = new Dictionary<string, RuntimeValue>();
        foreach (var (keyExpr, valueExpr) in expr.Entries)
        {
            var keyValue = await EvaluateAsync(keyExpr);
            var value = await EvaluateAsync(valueExpr);
            
            if (keyValue.Type != ValueType.String)
                throw new RuntimeException("Dictionary literal keys must be strings.");
            
            var key = keyValue.AsString();
            entries[key] = value;
        }
        
        var dictInstance = new DictionaryInstance(entries);
        return RuntimeValue.Object(dictInstance);
    }
    
    private async Task<RuntimeValue> EvaluateGraphLiteralAsync(GraphLiteralExpression expr)
    {
        var graph = new GraphInstance(expr.IsDirected);
        
        // Process nodes if provided
        if (expr.NodesExpression != null)
        {
            var nodesValue = await EvaluateAsync(expr.NodesExpression);
            if (nodesValue.Type != ValueType.Array)
                throw new RuntimeException("Graph 'nodes' property must be an array");
            
            var nodesArray = nodesValue.AsArray();
            foreach (var nodeValue in nodesArray)
            {
                string nodeId;
                if (nodeValue.Type == ValueType.String)
                {
                    nodeId = nodeValue.AsString();
                }
                else
                {
                    // Try to convert to string
                    nodeId = nodeValue.ToString();
                }
                
                graph.CallMethod("addNode", new List<RuntimeValue> { RuntimeValue.String(nodeId) }, this);
            }
        }
        
        // Process edges if provided
        if (expr.EdgesExpression != null)
        {
            var edgesValue = await EvaluateAsync(expr.EdgesExpression);
            if (edgesValue.Type != ValueType.Array)
                throw new RuntimeException("Graph 'edges' property must be an array");
            
            var edgesArray = edgesValue.AsArray();
            foreach (var edgeValue in edgesArray)
            {
                if (edgeValue.Type != ValueType.Object)
                    throw new RuntimeException("Graph edge must be an object");
                
                var edgeObj = edgeValue.AsObject();
                
                // Get "from" property
                var fromValue = edgeObj.Get("from", null);
                if (fromValue.Type != ValueType.String)
                    throw new RuntimeException("Graph edge 'from' property must be a string");
                var from = fromValue.AsString();
                
                // Get "to" property
                var toValue = edgeObj.Get("to", null);
                if (toValue.Type != ValueType.String)
                    throw new RuntimeException("Graph edge 'to' property must be a string");
                var to = toValue.AsString();
                
                // Get optional "weight" property
                RuntimeValue? weightValue = null;
                try
                {
                    weightValue = edgeObj.Get("weight", null);
                    if (weightValue.Type == ValueType.Null)
                        weightValue = null;
                }
                catch
                {
                    weightValue = null;
                }
                
                // Get optional "properties" property
                RuntimeValue? propertiesValue = null;
                DictionaryInstance? properties = null;
                try
                {
                    propertiesValue = edgeObj.Get("properties", null);
                    if (propertiesValue.Type == ValueType.Object && propertiesValue.AsObject() is DictionaryInstance dict)
                        properties = dict;
                }
                catch
                {
                    // properties not provided
                }
                
                var args = new List<RuntimeValue>
                {
                    RuntimeValue.String(from),
                    RuntimeValue.String(to)
                };
                
                if (weightValue != null)
                    args.Add(weightValue);
                else
                    args.Add(RuntimeValue.Null());
                
                if (properties != null)
                    args.Add(RuntimeValue.Object(properties));
                else
                    args.Add(RuntimeValue.Null());
                
                graph.CallMethod("addEdge", args, this);
            }
        }
        
        return RuntimeValue.Object(graph);
    }
    
    private async Task<RuntimeValue> EvaluateObjectLiteralAsync(ObjectLiteralExpression expr)
    {
        var jsonObj = new BuiltIns.JsonObject();
        foreach (var (keyExpr, valueExpr) in expr.Properties)
        {
            var keyValue = await EvaluateAsync(keyExpr);
            var value = await EvaluateAsync(valueExpr);
            
            // Key must be a string
            string key;
            if (keyValue.Type == ValueType.String)
            {
                key = keyValue.AsString();
            }
            else
            {
                throw new RuntimeException("Object literal keys must be strings.");
            }
            
            jsonObj.Set(key, value);
        }
        return RuntimeValue.Object(jsonObj);
    }
    
    private async Task<RuntimeValue> EvaluateLambdaAsync(LambdaExpression lambda)
    {
        // Create function declaration from lambda
        BlockStatement body;
        if (lambda.ExpressionBody != null)
        {
            // Wrap expression in return statement
            body = new BlockStatement(new List<Statement> 
            { 
                new ReturnStatement(lambda.ExpressionBody, lambda.Line, lambda.Column) 
            }, lambda.Line, lambda.Column);
        }
        else
        {
            body = lambda.BlockBody!;
        }
        
        // Create anonymous function declaration
        var funcDecl = new FunctionDeclaration(
            "<lambda>",  // Anonymous name
            lambda.Parameters,
            body,
            null,  // No decorators
            null,  // No parameter decorators
            null,  // No parameter type hints
            null,  // No return type
            false,
            lambda.Line,
            lambda.Column
        );
        
        // Create function value with current environment as closure
        var funcValue = new FunctionValue(funcDecl, _environment);
        return RuntimeValue.Function(funcValue);
    }
    
    private void CheckNumberOperand(RuntimeValue operand)
    {
        if (operand.Type != ValueType.Integer && operand.Type != ValueType.Float)
            throw new RuntimeException("Operand must be a number.");
    }
    
    private void CheckNumberOperands(RuntimeValue left, RuntimeValue right)
    {
        if ((left.Type != ValueType.Integer && left.Type != ValueType.Float) ||
            (right.Type != ValueType.Integer && right.Type != ValueType.Float))
            throw new RuntimeException("Operands must be numbers.");
    }
    
    private static RuntimeValue CheckedIntegerResult(Func<int> op, int? line, string? file)
    {
        try { return RuntimeValue.Integer(op()); }
        catch (OverflowException) { throw new RuntimeException("Integer overflow.", line, file); }
    }
    
    private async Task<RuntimeValue> EvaluateMatchAsync(MatchExpression matchExpr)
    {
        var savedEnv = _environment;
        try
        {
            var value = await EvaluateAsync(matchExpr.Value);
            
            // Try each case in order
            foreach (var matchCase in matchExpr.Cases)
            {
                var bindings = MatchPattern(matchCase.Pattern, value);
                if (bindings != null)
                {
                    // Create a new environment scope for the case body with the bindings
                    var caseEnv = new Environment(_environment);
                    foreach (var binding in bindings)
                    {
                        caseEnv.Define(binding.Key, binding.Value);
                    }
                    
                    // Execute case body in the new environment
                    var previousEnv = _environment;
                    try
                    {
                        _environment = caseEnv;
                        return await EvaluateMatchBodyAsync(matchCase.Body);
                    }
                    finally
                    {
                        _environment = previousEnv;
                    }
                }
            }
            
            // No case matched, execute default case if present
            if (matchExpr.DefaultCase != null)
            {
                return await EvaluateMatchBodyAsync(matchExpr.DefaultCase);
            }
            
            throw new RuntimeException("Match expression had no matching case and no default case.");
        }
        finally
        {
            _environment = savedEnv;
        }
    }
    
    /// <summary>
    /// Evaluates a match case body (or default) and returns its value.
    /// "Last expression wins": for blocks, the last statement's value is returned if it's an expression.
    /// </summary>
    private async Task<RuntimeValue> EvaluateMatchBodyAsync(Statement body)
    {
        if (body is ExpressionStatement exprStmt)
        {
            return await EvaluateAsync(exprStmt.Expression);
        }
        if (body is BlockStatement block)
        {
            if (block.Statements.Count == 0)
                return RuntimeValue.Null();
            for (int i = 0; i < block.Statements.Count - 1; i++)
            {
                await ExecuteAsync(block.Statements[i]);
            }
            var last = block.Statements[block.Statements.Count - 1];
            if (last is ExpressionStatement lastExpr)
                return await EvaluateAsync(lastExpr.Expression);
            await ExecuteAsync(last);
            return RuntimeValue.Null();
        }
        await ExecuteAsync(body);
        return RuntimeValue.Null();
    }
    
    private Dictionary<string, RuntimeValue>? MatchPattern(Pattern pattern, RuntimeValue value)
    {
        var bindings = new Dictionary<string, RuntimeValue>();
        
        switch (pattern)
        {
            case LiteralPattern literalPattern:
                return MatchLiteralPattern(literalPattern, value) ? bindings : null;
                
            case IdentifierPattern identifierPattern:
                bindings[identifierPattern.Name] = value;
                return bindings;
                
            case WildcardPattern:
                return bindings; // Always matches, no bindings
                
            case ArrayPattern arrayPattern:
                return MatchArrayPattern(arrayPattern, value, bindings);
                
            case ObjectPattern objectPattern:
                return MatchObjectPattern(objectPattern, value, bindings);

            case VariantPattern variantPattern:
                return MatchVariantPattern(variantPattern, value, bindings);

            case RestPattern:
                // Rest pattern only valid inside array pattern; cannot match a single value
                return null;

            default:
                throw new RuntimeException($"Unknown pattern type: {pattern.GetType()}");
        }
    }

    private Dictionary<string, RuntimeValue>? MatchVariantPattern(VariantPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Variant)
            return null;
        var v = value.AsVariant();
        if (v.Tag != pattern.Tag || v.Payload.Count != pattern.PayloadPatterns.Count)
            return null;
        for (int i = 0; i < pattern.PayloadPatterns.Count; i++)
        {
            var sub = MatchPattern(pattern.PayloadPatterns[i], v.Payload[i]);
            if (sub == null)
                return null;
            foreach (var kv in sub)
                bindings[kv.Key] = kv.Value;
        }
        return bindings;
    }

    private bool MatchLiteralPattern(LiteralPattern pattern, RuntimeValue value)
    {
        if (pattern.Value == null)
        {
            return value.Type == ValueType.Null;
        }
        
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger() == (int)(pattern.Value ?? 0),
            ValueType.Float => Math.Abs(value.AsFloat() - (double)(pattern.Value ?? 0.0)) < 0.0001,
            ValueType.String => value.AsString() == (string)(pattern.Value ?? ""),
            ValueType.Boolean => value.AsBoolean() == (bool)(pattern.Value ?? false),
            ValueType.Null => pattern.Value == null,
            _ => false
        };
    }
    
    private Dictionary<string, RuntimeValue>? MatchArrayPattern(ArrayPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Array)
        {
            return null;
        }
        
        var array = value.AsArray();
        var requiredElements = pattern.Elements.Count;
        var hasRest = pattern.Rest != null;
        
        // Check if we have enough elements (or exactly enough if no rest)
        if (!hasRest && array.Count != requiredElements)
        {
            return null;
        }
        
        if (hasRest && array.Count < requiredElements)
        {
            return null;
        }
        
        // Match each element pattern
        for (int i = 0; i < requiredElements; i++)
        {
            var elementPattern = pattern.Elements[i];
            var elementValue = array[i];
            var elementBindings = MatchPattern(elementPattern, elementValue);
            if (elementBindings == null)
            {
                return null; // Element didn't match
            }
            
            // Merge bindings
            foreach (var binding in elementBindings)
            {
                bindings[binding.Key] = binding.Value;
            }
        }
        
        // Handle rest pattern
        if (hasRest && pattern.Rest != null)
        {
            var restElements = array.Skip(requiredElements).ToList();
            var restArray = RuntimeValue.Array(restElements);
            if (pattern.Rest.Name != null)
            {
                bindings[pattern.Rest.Name] = restArray;
            }
        }
        
        return bindings;
    }
    
    private Dictionary<string, RuntimeValue>? MatchObjectPattern(ObjectPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Object)
        {
            return null;
        }
        
        var obj = value.AsObject();
        var keySet = new HashSet<string>(obj.GetAllKeys());
        
        // Match each property pattern
        foreach (var prop in pattern.Properties)
        {
            if (!keySet.Contains(prop.Key))
                return null; // Required property missing
            RuntimeValue propValue;
            try
            {
                propValue = obj.Get(prop.Key, null);
            }
            catch
            {
                return null; // Property doesn't exist
            }
            
            if (prop.Pattern != null)
            {
                // Nested pattern matching
                var propBindings = MatchPattern(prop.Pattern, propValue);
                if (propBindings == null)
                {
                    return null; // Property pattern didn't match
                }
                
                // Merge bindings
                foreach (var binding in propBindings)
                {
                    bindings[binding.Key] = binding.Value;
                }
            }
            else if (prop.BindingName != null)
            {
                // Simple binding: { key } means bind obj.key to key
                bindings[prop.BindingName] = propValue;
            }
        }
        
        return bindings;
    }
    
    private async Task<RuntimeValue?> ExecuteDestructuringVarDeclAsync(DestructuringVarDecl stmt)
    {
        var value = await EvaluateAsync(stmt.Initializer);
        var bindings = MatchDestructuringPattern(stmt.Pattern, value);
        
        if (bindings == null)
        {
            throw new RuntimeException("Destructuring pattern did not match value.");
        }
        
        // Define all bound variables in the current environment
        foreach (var binding in bindings)
        {
            _environment.Define(binding.Key, binding.Value);
        }
        
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteDestructuringAssignmentAsync(DestructuringAssignment stmt)
    {
        var value = await EvaluateAsync(stmt.Value);
        var bindings = MatchDestructuringPattern(stmt.Pattern, value);
        
        if (bindings == null)
        {
            throw new RuntimeException("Destructuring pattern did not match value.");
        }
        
        // Assign all bound variables in the current environment
        foreach (var binding in bindings)
        {
            _environment.Assign(binding.Key, binding.Value);
        }
        
        return null;
    }
    
    private Dictionary<string, RuntimeValue>? MatchDestructuringPattern(DestructuringPattern pattern, RuntimeValue value)
    {
        var bindings = new Dictionary<string, RuntimeValue>();
        
        switch (pattern)
        {
            case ArrayDestructuringPattern arrayPattern:
                return MatchArrayDestructuringPattern(arrayPattern, value, bindings);
                
            case ObjectDestructuringPattern objectPattern:
                return MatchObjectDestructuringPattern(objectPattern, value, bindings);
                
            default:
                throw new RuntimeException($"Unknown destructuring pattern type: {pattern.GetType()}");
        }
    }
    
    private Dictionary<string, RuntimeValue>? MatchArrayDestructuringPattern(ArrayDestructuringPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Array)
        {
            return null;
        }
        
        var array = value.AsArray();
        var requiredElements = pattern.Elements.Count;
        var hasRest = pattern.Rest != null;
        
        if (!hasRest && array.Count != requiredElements)
        {
            return null;
        }
        
        if (hasRest && array.Count < requiredElements)
        {
            return null;
        }
        
        // Extract each element
        for (int i = 0; i < requiredElements; i++)
        {
            var elementPattern = pattern.Elements[i];
            var elementValue = array[i];
            
            if (elementPattern is IdentifierPattern idPattern)
            {
                bindings[idPattern.Name] = elementValue;
            }
            else if (elementPattern is WildcardPattern)
            {
                // Ignore wildcard
            }
            else
            {
                // Nested pattern - recursively match
                var elementBindings = MatchPattern(elementPattern, elementValue);
                if (elementBindings == null)
                {
                    return null;
                }
                foreach (var binding in elementBindings)
                {
                    bindings[binding.Key] = binding.Value;
                }
            }
        }
        
        // Handle rest pattern
        if (hasRest && pattern.Rest != null)
        {
            var restElements = array.Skip(requiredElements).ToList();
            var restArray = RuntimeValue.Array(restElements);
            if (pattern.Rest.Name != null)
            {
                bindings[pattern.Rest.Name] = restArray;
            }
        }
        
        return bindings;
    }
    
    private Dictionary<string, RuntimeValue>? MatchObjectDestructuringPattern(ObjectDestructuringPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Object)
        {
            return null;
        }
        
        var obj = value.AsObject();
        var keySet = new HashSet<string>(obj.GetAllKeys());
        
        foreach (var prop in pattern.Properties)
        {
            if (!keySet.Contains(prop.Key))
                return null; // Required property missing
            RuntimeValue propValue;
            try
            {
                propValue = obj.Get(prop.Key, null);
            }
            catch
            {
                return null; // Property doesn't exist
            }
            
            if (prop.Pattern != null)
            {
                // Nested pattern
                var propBindings = MatchPattern(prop.Pattern, propValue);
                if (propBindings == null)
                {
                    return null;
                }
                foreach (var binding in propBindings)
                {
                    bindings[binding.Key] = binding.Value;
                }
            }
            else if (prop.BindingName != null)
            {
                // Simple binding
                bindings[prop.BindingName] = propValue;
            }
        }
        
        return bindings;
    }
}

public class RuntimeException : Exception
{
    public int? Line { get; }
    public string? File { get; }
    public string? SourceLine { get; }
    
    public RuntimeException(string message) : base(message) 
    {
        Line = null;
        File = null;
        SourceLine = null;
    }
    
    public RuntimeException(string message, int? line, string? file) : base(message) 
    {
        Line = line;
        File = file;
        SourceLine = null;
    }
    
    public RuntimeException(string message, int? line, string? file, string? sourceLine) : base(message) 
    {
        Line = line;
        File = file;
        SourceLine = sourceLine;
    }

    public RuntimeException(string message, int? line, string? file, string? sourceLine, Exception? innerException)
        : base(message, innerException)
    {
        Line = line;
        File = file;
        SourceLine = sourceLine;
    }
}

public class ReturnException : Exception
{
    public RuntimeValue? Value { get; }
    public ReturnException(RuntimeValue? value) { Value = value; }
}

public class BreakException : Exception { }
public class ContinueException : Exception { }

public class MALDAException : Exception
{
    public RuntimeValue Value { get; }
    public int? Line { get; }
    public string? File { get; }
    
    public MALDAException(RuntimeValue value, int? line = null, string? file = null) 
        : base(value.ToString())
    {
        Value = value;
        Line = line;
        File = file;
    }
}

// Call stack frame for interpreter (separate from IDE model)
public class InterpreterCallStackFrame
{
    public string FunctionName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public int Line { get; set; }
    public string File { get; set; } = string.Empty;
}
