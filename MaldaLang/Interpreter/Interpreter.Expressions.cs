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
    internal async Task<RuntimeValue> EvaluateInEnvironmentAsync(Expression expr, Environment env, ObjectInstance? thisObject = null)
    {
        var previousEnv = _environment;
        var previousObject = _currentObject;
        _environment = env;
        _currentObject = thisObject;
        // REPL / debugger expression: a built-in name bound in this environment means the binding.
        foreach (var node in AstNodeWalker.Descendants(expr))
        {
            if (node is IdentifierExpression { ShadowsBuiltIn: false } id && IsBuiltIn(id.Name) && env.TryGet(id.Name, out _))
                id.ShadowsBuiltIn = true;
        }

        try
        {
            return await EvaluateAsync(expr);
        }
        finally
        {
            _environment = previousEnv;
            _currentObject = previousObject;
        }
    }

    internal Environment GlobalsEnvironment => _globals;

    /// <summary>For an actor's interpreter: the globals of the program that spawned it.</summary>
    internal Environment? ActorProgramGlobals { get; set; }
    internal IReadOnlyDictionary<string, ActorDefinition> DefinedActors => _actors;
    internal IReadOnlyDictionary<string, WorkflowDeclaration> DefinedWorkflows => _workflows;
    internal Environment CurrentEnvironment => _environment;
    internal ObjectInstance? CurrentThisObject => _currentObject;

    private async Task<RuntimeValue> EvaluateCoreAsync(Expression expr, bool returnTask)
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
            NamedArgumentExpression => throw new RuntimeException("Named decorator arguments cannot be evaluated as expressions."),
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
        var memberFrame = CurrentClassMemberFrame();
        if (memberFrame != null)
        {
            // Locals / parameters of this call, then implicit members, then globals.
            if (TryGetFunctionLocal(expr.Name, memberFrame, out var local))
                return local;
            if (TryReadImplicitMember(expr.Name, out var member))
                return member;
        }

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
            var funcValue = new FunctionValue(funcDecl, _globals) { BuiltInFunctionName = expr.Name };
            return RuntimeValue.Function(funcValue);
        }

        // Non-member fallback: implicit this only when the name is unbound.
        if (memberFrame == null && TryReadImplicitMember(expr.Name, out var leakedMember))
            return leakedMember;
        
        throw new RuntimeException($"Undefined variable '{expr.Name}'.", expr.Line, _currentFile);
    }

    /// <summary>
    /// <see cref="LookUpVariable"/> without the built-in fallback: the user binding of
    /// <paramref name="name"/> (local, parameter, implicit member, or environment), if any.
    /// </summary>
    private bool TryLookUpUserBinding(string name, out RuntimeValue value)
    {
        var memberFrame = CurrentClassMemberFrame();
        if (memberFrame != null)
        {
            if (TryGetFunctionLocal(name, memberFrame, out value))
                return true;
            if (TryReadImplicitMember(name, out value))
                return true;
        }

        if (_environment.TryGet(name, out value))
            return true;

        return memberFrame == null && TryReadImplicitMember(name, out value);
    }

    /// <summary>
    /// Innermost class/actor member on the stack. Lambdas are skipped so a method's
    /// implicit fields stay in scope; a named free function stops the walk.
    /// Precomputed per frame (<see cref="FunctionFrame.ClassMemberFrame"/>): this runs on
    /// every variable read and write.
    /// </summary>
    private FunctionFrame? CurrentClassMemberFrame() =>
        GetActivation().CurrentFunctionFrame?.ClassMemberFrame;

    private FunctionFrame? ClassMemberFrameFor(FunctionFrame frame)
    {
        if (!string.IsNullOrEmpty(frame.Function.ClassName))
            return frame;
        if (frame.Function.Declaration?.Name == "<lambda>")
            return CurrentClassMemberFrame();
        return null;
    }

    private bool TryGetFunctionLocal(string name, FunctionFrame memberFrame, out RuntimeValue value)
    {
        return _environment.TryGetUntil(name, memberFrame.Environment.GetEnclosing(), out value);
    }

    private bool TryAssignFunctionLocal(string name, RuntimeValue value, FunctionFrame memberFrame)
    {
        return _environment.TryAssignUntil(name, value, memberFrame.Environment.GetEnclosing());
    }

    private bool TryReadImplicitMember(string name, out RuntimeValue value)
    {
        value = default;
        if (_currentObject != null)
        {
            var field = _currentObject.Class?.FindField(name);
            if (field != null)
            {
                if (field.IsStatic && _currentObject.Class!.StaticFields.TryGetValue(name, out var classStatic))
                {
                    value = classStatic;
                    return true;
                }
                value = _currentObject.Get(name, _currentClass);
                return true;
            }
        }

        if (_currentClass != null && _currentClass.StaticFields.TryGetValue(name, out var staticValue))
        {
            value = staticValue;
            return true;
        }

        return false;
    }

    private bool TryWriteImplicitMember(string name, RuntimeValue value, int? line)
    {
        if (_currentObject != null)
        {
            var field = _currentObject.Class?.FindField(name);
            if (field != null)
            {
                if (field.Access == AccessModifier.Private && _currentClass != _currentObject.Class)
                    throw new RuntimeException($"Cannot access private field '{name}' from outside {_currentObject.Class.Name}.", line, _currentFile);
                if (field.IsStatic)
                {
                    _currentObject.Class!.StaticFields[name] = value;
                    return true;
                }
                _currentObject.Set(name, value);
                return true;
            }
        }

        if (_currentClass != null && _currentClass.StaticFields.ContainsKey(name))
        {
            _currentClass.StaticFields[name] = value;
            return true;
        }

        return false;
    }
    
    private async Task<RuntimeValue> EvaluateInterpolatedStringAsync(InterpolatedStringExpression expr)
    {
        var result = new System.Text.StringBuilder();
        MaldaLang.Runtime.GroundedMeta? grounded = null;
        var mixed = false;
        
        foreach (var segment in expr.Segments)
        {
            if (segment.IsExpression)
            {
                var value = await EvaluateAsync(segment.Expression!);
                result.Append(value.ToString());
                if (value.Grounded != null)
                    grounded = grounded == null ? value.Grounded : MaldaLang.Runtime.GroundedMeta.Union(grounded, value.Grounded);
                else
                    mixed = true;
            }
            else
            {
                result.Append(segment.Text ?? "");
                if (!string.IsNullOrEmpty(segment.Text))
                    mixed = true;
            }
        }

        var text = RuntimeValue.String(result.ToString());
        if (grounded == null)
            return text;
        return text.WithGrounded(mixed ? MaldaLang.Runtime.GroundedMeta.Mix(grounded, result.ToString()) : grounded);
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

        if (TryBufferIndex(array, index, expr.Line, out var buffer, out var bufferIndex))
            return buffer.GetAt(bufferIndex);
        
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

                properties = GraphLiteralEdges.CollectExtraProperties(edgeObj, properties);
                
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
    
    /// <summary>An int result computed in long; out of int range is MALDA's integer overflow error.</summary>
    private RuntimeValue IntegerResult(long result, int? line)
    {
        if (result < int.MinValue || result > int.MaxValue)
            throw new RuntimeException("Integer overflow.", line, _currentFile);
        return RuntimeValue.Integer((int)result);
    }
}
