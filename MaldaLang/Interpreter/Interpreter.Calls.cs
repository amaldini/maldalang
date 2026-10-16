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
        
        // A built-in used as a value (see LookUpVariable). A user function named like a
        // built-in has no BuiltInFunctionName and runs its own body below.
        if (function.BuiltInFunctionName != null)
        {
            try
            {
                return await BuiltInFunctions.CallBuiltInAsync(function.BuiltInFunctionName, arguments, this);
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

        // Calls that complete synchronously nest their async frames on one thread stack, so a
        // few hundred levels of user recursion used to kill the process with an uncatchable
        // stack overflow (C# and JavaScript go thousands deep). Near the limit, continue on a
        // fresh stack instead.
        if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            await Task.Yield();
        }

        if (function.Declaration.Decorators != null
            && function.Declaration.Decorators.Any(d => d.Name == "requiresCitations"))
        {
            foreach (var arg in arguments)
            {
                if (arg.Type == ValueType.String && (arg.Grounded == null || !arg.Grounded.HasCitations))
                    throw new RuntimeException("@requiresCitations: argument is not fully grounded.");
            }
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
        functionFrame.ClassMemberFrame = ClassMemberFrameFor(functionFrame);
        var activation = GetActivation();
        var callerFunctionFrame = activation.CurrentFunctionFrame;
        _executionStack.Push(functionFrame);
        
        // Track call stack for debugger
        var callStackFrame = new InterpreterCallStackFrame
        {
            FunctionName = function.Declaration.Name,
            ClassName = function.ClassName,
            Line = function.Declaration.Line,
            File = functionFile ?? "main.malda",
            Environment = environment
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

        var budget = DeclarationBounds.TryGetResourceBudget(function.Declaration);
        var pushedBudget = budget != null && budget.HasAnyBound;
        if (pushedBudget)
            ResourceBoundsContext.Push(budget!, function.Declaration.Name);

        PushDeferFrame();
        try
        {
            _environment = environment;
            activation.CurrentFunctionFrame = functionFrame;
            
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
            
            callStackFrame.ThisObject = _currentObject;

            for (int i = 0; i < function.Declaration.Parameters.Count; i++)
            {
                environment.Define(function.Declaration.Parameters[i], arguments[i]);
            }
            
            // Execute function body from saved statement index.
            // Last-expression-wins still uses ExecuteAsync so the debug pause
            // gate (and profiler) run on the trailing expression statement.
            var body = function.Declaration.Body;
            RuntimeValue? lastExprValue = null;
            var applyLastExprWins = !function.IsConstructor;
            var returned = false;
            RuntimeValue? returnedValue = null;
            for (int i = functionFrame.StatementIndex; i < body.Statements.Count; i++)
            {
                WithinBoundsContext.EnsureWithinBound(function.Declaration.Name);
                functionFrame.StatementIndex = i;
                var stmt = body.Statements[i];
                var isLast = (i == body.Statements.Count - 1);
                if (!TryExecuteSync(stmt, out var executed))
                    executed = await ExecuteStatementAsync(stmt);
                if (_pendingControl != PendingControl.None)
                {
                    if (_pendingControl != PendingControl.Return)
                        ThrowPendingControl(); // break/continue outside a loop keeps the exception path
                    returned = true;
                    returnedValue = TakePendingReturnValue();
                    break;
                }
                if (applyLastExprWins && isLast && stmt is ExpressionStatement)
                    lastExprValue = executed;
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
            if (returned)
            {
                // Same value as the ReturnException path below (constructors included).
                result = returnedValue ?? RuntimeValue.Null();
            }
            else if (function.IsConstructor)
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
            PopExecutionFrame(functionFrame);
            activation.CurrentFunctionFrame = callerFunctionFrame;
            await RunAndPopDeferFrameAsync();

            if (withinMs is > 0)
                WithinBoundsContext.Pop();

            if (pushedBudget)
                ResourceBoundsContext.Pop();

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

    /// <summary>
    /// Built-in names the program binds at top level, kept across <see cref="InterpretAsync"/>
    /// calls so a later REPL line still sees an earlier <c>function join(...)</c>.
    /// </summary>
    private readonly HashSet<string> _programBuiltInBindings = new(StringComparer.Ordinal);

    /// <summary>
    /// Built-in names merged into scope by an import that does not name them (whole-module
    /// <c>import</c> / <c>using</c>), which the static analysis cannot see.
    /// </summary>
    private readonly HashSet<string> _mergedBuiltInBindings = new(StringComparer.Ordinal);

    /// <summary>Marks uses of built-in names that a user binding shadows (see <see cref="BuiltInShadowAnalysis"/>).</summary>
    private void AnalyzeBuiltInShadowing(List<Statement> statements)
    {
        var result = BuiltInShadowAnalysis.Analyze(statements, outerNames: _programBuiltInBindings);
        _programBuiltInBindings.UnionWith(result.ProgramScopeNames);
    }

    private void NoteMergedBinding(string name)
    {
        if (IsBuiltIn(name))
            _mergedBuiltInBindings.Add(name);
    }

    /// <summary>
    /// For an identifier whose name is a built-in: the user binding it resolves to, when one
    /// shadows the built-in here. Cheap when nothing is shadowed (a flag and a count check).
    /// An unbound name (a <c>var</c> not declared yet) falls back to the built-in.
    /// </summary>
    private bool TryResolveShadowingBinding(IdentifierExpression id, out RuntimeValue value)
    {
        if (!id.ShadowsBuiltIn && (_mergedBuiltInBindings.Count == 0 || !_mergedBuiltInBindings.Contains(id.Name)))
        {
            value = default;
            return false;
        }

        return TryLookUpUserBinding(id.Name, out value);
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
        if (klass is MaldaLang.BuiltIns.DeclaredContextClass contextClass)
        {
            return RuntimeValue.Object(new MaldaLang.BuiltIns.DeclaredContextInstance(contextClass.Declaration, this));
        }

        if (klass == GraphMemoryClassDefinition.Instance)
        {
            if (arguments.Count != 0)
                throw new RuntimeException("GraphMemory() expects no arguments");
            
            var graphMemory = new GraphMemoryInstance();
            graphMemory.SetInterpreter(this);
            return RuntimeValue.Object(graphMemory);
        }
        
        var instance = new ObjectInstance(klass);
        
        // Run instance field initializers, base class first. C# runs derived initializers
        // before base ones, but MALDA evaluates `this.x` in a derived initializer against the
        // base value, so base-first is the order that keeps `y = this.x + 1` meaningful.
        // Static fields are owned by the class and are initialized when it is declared, so
        // they must be skipped here or each `new` would reset them.
        for (var declaringClass = klass; declaringClass != null; declaringClass = declaringClass.Superclass)
        {
            foreach (var field in declaringClass.Fields.Values)
            {
                if (field.IsStatic || field.Value is not Expression initExpr)
                    continue;

                var previousObject = _currentObject;
                var previousClass = _currentClass;
                _currentObject = instance;
                _currentClass = declaringClass;
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

        // A subclass that declares no constructor of its own inherits the nearest ancestor's
        // constructor, forwarding the arguments it was constructed with. This matches the
        // JavaScript backend, where a derived class without a constructor forwards to super.
        for (var inherited = klass.Superclass; inherited != null; inherited = inherited.Superclass)
        {
            if (inherited.Constructor != null)
            {
                return await CallFunctionAsync(inherited.Constructor, arguments, instance);
            }
        }

        return RuntimeValue.Object(instance);
    }
}
