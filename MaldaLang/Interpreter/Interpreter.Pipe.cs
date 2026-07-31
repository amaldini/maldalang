// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Collections.Frozen;
using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST.Expressions;

public partial class Interpreter
{
    private static readonly FrozenSet<string> ArrayPipelineMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "append", "pop", "shift", "concat", "popOrNull", "shiftOrNull", "get", "at",
        "map", "filter", "reduce", "forEach", "find", "findIndex", "some", "every",
        "sort", "reverse", "slice", "indexOf", "includes", "join", "sum", "average", "min", "max"
    }.ToFrozenSet();

    private async Task<RuntimeValue> EvaluatePipeAsync(PipeExpression pipe) =>
        await InvokePipedValue(await EvaluateAsync(pipe.Left), pipe.Right);

    private async Task<RuntimeValue> InvokePipedValue(RuntimeValue left, Expression right) =>
        right switch
        {
            FunctionCallExpression call => await EvaluatePipedCallAsync(left, call),
            MemberAccessExpression member => await EvaluatePipedCallAsync(
                left,
                new FunctionCallExpression(member, [], member.Line, member.Column)),
            IdentifierExpression id => await InvokePipedIdentifierAsync(left, id),
            LambdaExpression lambda => await InvokePipedLambdaAsync(left, lambda),
            _ => throw new RuntimeException("Right side of |> must be a function call, identifier, or lambda.")
        };

    private async Task<RuntimeValue> InvokePipedIdentifierAsync(RuntimeValue left, IdentifierExpression id)
    {
        var callee = LookUpVariable(id);
        if (callee.Type == ValueType.Prompt)
            return await callee.AsPrompt().Call(new List<RuntimeValue> { left }, this);

        return await InvokeCallableByName(id.Name, left, []);
    }

    private async Task<RuntimeValue> InvokePipedLambdaAsync(RuntimeValue left, LambdaExpression lambda)
    {
        var fn = await EvaluateLambdaAsync(lambda);
        return await CallFunctionAsync(fn.AsFunction(), [left]);
    }

    private async Task<RuntimeValue> EvaluatePipedCallAsync(RuntimeValue left, FunctionCallExpression call)
    {
        var callArgs = new List<RuntimeValue>();
        foreach (var arg in call.Arguments)
            callArgs.Add(await EvaluateAsync(arg));

        if (call.Callee is IdentifierExpression id)
        {
            if (left.Type == ValueType.Array && left.AsObject() is ArrayInstance array &&
                ArrayPipelineMethods.Contains(id.Name))
                return array.CallMethod(id.Name, callArgs, this);

            var callee = LookUpVariable(id);
            if (callee.Type == ValueType.Prompt)
            {
                var promptArgs = new List<RuntimeValue> { left };
                promptArgs.AddRange(callArgs);
                return await callee.AsPrompt().Call(promptArgs, this);
            }

            return await InvokeCallableByName(id.Name, left, callArgs);
        }

        var evaluatedCallee = await EvaluateAsync(call.Callee);
        if (evaluatedCallee.Type == ValueType.Function &&
            evaluatedCallee.AsFunction().BuiltInInstance is RetrieverInstance retriever)
        {
            var retrieverArgs = new List<RuntimeValue> { left };
            retrieverArgs.AddRange(callArgs);
            var methodName = evaluatedCallee.AsFunction().BuiltInMethod ?? "get";
            return retriever.CallMethod(methodName, retrieverArgs, this);
        }

        if (evaluatedCallee.Type != ValueType.Function)
            throw new RuntimeException("Right side of |> must be callable.");

        var args = new List<RuntimeValue> { left };
        args.AddRange(callArgs);
        return await CallFunctionAsync(evaluatedCallee.AsFunction(), args);
    }

    internal async Task<RuntimeValue> InvokeComposedPipelineFunctionAsync(FunctionValue function, List<RuntimeValue> arguments) =>
        await CallFunctionAsync(function, arguments);

    internal RuntimeValue InvokeBuiltInInstanceMethod(ObjectInstance instance, string methodName, List<RuntimeValue> arguments) =>
        CallBuiltInMethod(instance, methodName, arguments);

    private async Task<RuntimeValue> InvokeCallableByName(string name, RuntimeValue left, List<RuntimeValue> tailArgs)
    {
        var args = new List<RuntimeValue> { left };
        args.AddRange(tailArgs);

        if (IsBuiltIn(name))
        {
            try
            {
                return await BuiltInFunctions.CallBuiltInAsync(name, args, this);
            }
            catch (Exception ex) when (ex is not RuntimeException)
            {
                throw new RuntimeException(ex.Message);
            }
        }

        var fn = LookUpVariable(new IdentifierExpression(name));
        if (fn.Type != ValueType.Function)
            throw new RuntimeException($"'{name}' is not callable.");

        return await CallFunctionAsync(fn.AsFunction(), args);
    }

    private async Task<RuntimeValue> EvaluateListComprehensionAsync(ListComprehensionExpression comp)
    {
        var iterableValue = await EvaluateAsync(comp.Iterable);
        if (iterableValue.Type != ValueType.Array)
            throw new RuntimeException("List comprehension requires an iterable array.");

        var source = iterableValue.AsArray();
        var result = new List<RuntimeValue>();

        var previous = _environment;
        var loopEnv = new Environment(previous);
        _environment = loopEnv;

        try
        {
            foreach (var item in source)
            {
                loopEnv.Define(comp.Variable, item);

                if (comp.Filter != null)
                {
                    var keep = await EvaluateAsync(comp.Filter);
                    if (!keep.IsTruthy())
                        continue;
                }

                result.Add(await EvaluateAsync(comp.Element));
            }
        }
        finally
        {
            _environment = previous;
        }

        return RuntimeValue.Array(result);
    }

    private async Task<RuntimeValue> EvaluateDictComprehensionAsync(DictComprehensionExpression comp)
    {
        var iterableValue = await EvaluateAsync(comp.Iterable);
        if (iterableValue.Type != ValueType.Array)
            throw new RuntimeException("Dict comprehension requires an iterable array.");

        var source = iterableValue.AsArray();
        var entries = new Dictionary<string, RuntimeValue>();

        var previous = _environment;
        var loopEnv = new Environment(previous);
        _environment = loopEnv;

        try
        {
            foreach (var item in source)
            {
                loopEnv.Define(comp.Variable, item);

                if (comp.Filter != null)
                {
                    var keep = await EvaluateAsync(comp.Filter);
                    if (!keep.IsTruthy())
                        continue;
                }

                var keyValue = await EvaluateAsync(comp.Key);
                if (keyValue.Type != ValueType.String)
                    throw new RuntimeException("Dict comprehension keys must evaluate to strings.");

                var value = await EvaluateAsync(comp.Value);
                entries[keyValue.AsString()] = value;
            }
        }
        finally
        {
            _environment = previous;
        }

        return RuntimeValue.Object(new DictionaryInstance(entries));
    }
}
