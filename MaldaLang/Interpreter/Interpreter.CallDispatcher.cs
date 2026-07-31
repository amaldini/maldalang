// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST.Expressions;

public partial class Interpreter
{
    /// <summary>
    /// Starts an async call for <c>async expr</c> without leaving the caller's environment on a child frame.
    /// Hot-started tasks run until their first await on the current interpreter thread.
    /// </summary>
    private RuntimeValue WrapCallAsTask(Func<Task<RuntimeValue>> startCall)
    {
        var previousEnv = _environment;
        var previousObject = _currentObject;
        var previousClass = _currentClass;
        var previousActor = _currentActor;
        try
        {
            return RuntimeValue.Task(startCall());
        }
        finally
        {
            _environment = previousEnv;
            _currentObject = previousObject;
            _currentClass = previousClass;
            _currentActor = previousActor;
        }
    }

    private async Task<RuntimeValue> EvaluateCallViaDispatcherAsync(FunctionCallExpression expr, bool returnTask = false)
    {
        ObjectInstance? instance = null;
        RuntimeValue callee;
        string? functionName = null;

        var arguments = new List<RuntimeValue>();
        foreach (var arg in expr.Arguments)
        {
            arguments.Add(await EvaluateAsync(arg));
        }

        if (expr.Callee is MemberAccessExpression memberExpr)
        {
            if (memberExpr.Object is SuperExpression)
            {
                if (_currentClass == null || _currentClass.Superclass == null)
                    throw new RuntimeException("Cannot use 'super' outside of a class or without a superclass.");
                if (_currentObject == null)
                    throw new RuntimeException("Cannot use 'super' outside of an instance method.");

                var superMethod = _currentClass.Superclass.FindMethod(memberExpr.Member);
                if (superMethod == null)
                    throw new RuntimeException($"Superclass has no method '{memberExpr.Member}'.");

                instance = _currentObject;
                callee = RuntimeValue.Function(superMethod);
            }
            else
            {
                var obj = await EvaluateAsync(memberExpr.Object);
                if (obj.Type == ValueType.Object)
                {
                    instance = obj.AsObject();
                    callee = instance.Get(memberExpr.Member);
                }
                else if (obj.Type == ValueType.Array)
                {
                    var arrayInstance = obj.AsArrayInstance();
                    instance = arrayInstance;
                    callee = arrayInstance.Get(memberExpr.Member);
                }
                else if (obj.Type == ValueType.Class)
                {
                    var klass = obj.AsClass();
                    if (klass.StaticMethods.ContainsKey(memberExpr.Member))
                    {
                        callee = RuntimeValue.Function(klass.StaticMethods[memberExpr.Member]);
                    }
                    else
                    {
                        throw new RuntimeException($"Class {klass.Name} has no static method '{memberExpr.Member}'.");
                    }
                }
                else if (obj.Type == ValueType.String && IsStringExtensionMethod(memberExpr.Member))
                {
                    var args = new List<RuntimeValue> { obj };
                    args.AddRange(arguments);
                    try
                    {
                        if (returnTask)
                            return WrapCallAsTask(() => BuiltInFunctions.CallBuiltInAsync(memberExpr.Member, args, this));
                        return await BuiltInFunctions.CallBuiltInAsync(memberExpr.Member, args, this);
                    }
                    catch (System.Exception ex) when (!(ex is RuntimeException))
                    {
                        throw new RuntimeException(ex.Message);
                    }
                }
                else
                {
                    throw new RuntimeException("Can only call methods on objects and classes.", expr.Line, _currentFile);
                }
            }
        }
        else if (expr.Callee is SuperExpression)
        {
            if (_currentClass == null || _currentClass.Superclass == null)
                throw new RuntimeException("Cannot use 'super' outside of a class or without a superclass.");
            if (_currentObject == null)
                throw new RuntimeException("Cannot use 'super()' outside of a constructor.");

            var superclass = _currentClass.Superclass;
            if (superclass.Constructor == null)
                throw new RuntimeException($"Superclass '{superclass.Name}' has no constructor.");

            if (returnTask)
                return WrapCallAsTask(() => CallFunctionAsync(superclass.Constructor, arguments, _currentObject));
            return await CallFunctionAsync(superclass.Constructor, arguments, _currentObject);
        }
        else if (expr.Callee is IdentifierExpression idExpr)
        {
            functionName = idExpr.Name;
            if (IsBuiltIn(functionName))
            {
                try
                {
                    if (returnTask)
                        return WrapCallAsTask(() => BuiltInFunctions.CallBuiltInAsync(functionName, arguments, this));
                    return await BuiltInFunctions.CallBuiltInAsync(functionName, arguments, this);
                }
                catch (System.Exception ex) when (!(ex is RuntimeException))
                {
                    throw new RuntimeException(ex.Message);
                }
            }
            callee = await EvaluateAsync(expr.Callee);
        }
        else
        {
            callee = await EvaluateAsync(expr.Callee);
        }

        if (callee.Type == ValueType.Prompt)
        {
            try
            {
                var prompt = callee.AsPrompt();
                if (returnTask)
                    return WrapCallAsTask(() => prompt.Call(arguments, this));
                return await prompt.Call(arguments, this);
            }
            catch (RuntimeException ex)
            {
                if (ex.Line == null)
                {
                    throw new RuntimeException(ex.Message, expr.Line, _currentFile);
                }
                throw;
            }
        }
        else if (callee.Type == ValueType.Function)
        {
            try
            {
                var func = callee.AsFunction();
                if (func.BuiltInInstance != null && func.BuiltInMethod != null)
                {
                    var methodName = func.BuiltInMethod;
                    if (func.BuiltInInstance is AnsiConsoleInstance &&
                        (methodName == "status" || methodName == "prompt" || methodName == "progress"))
                    {
                        if (returnTask)
                            return WrapCallAsTask(() => CallBuiltInMethodAsync(func.BuiltInInstance, methodName, arguments));
                        return await CallBuiltInMethodAsync(func.BuiltInInstance, methodName, arguments);
                    }
                    if (func.BuiltInInstance is StdLibModuleInstance &&
                        StdLibModuleInstance.RequiresAsyncCall(methodName))
                    {
                        if (returnTask)
                            return WrapCallAsTask(() => CallBuiltInMethodAsync(func.BuiltInInstance, methodName, arguments));
                        return await CallBuiltInMethodAsync(func.BuiltInInstance, methodName, arguments);
                    }
                }
                if (returnTask)
                    return RuntimeValue.Task(CallFunctionAsync(func, arguments, instance));
                return await CallFunctionAsync(func, arguments, instance);
            }
            catch (RuntimeException ex)
            {
                if (ex.Line == null)
                {
                    throw new RuntimeException(ex.Message, expr.Line, _currentFile);
                }
                throw;
            }
        }
        else if (callee.Type == ValueType.Class)
        {
            if (returnTask)
                return WrapCallAsTask(() => CallConstructorAsync(callee.AsClass(), arguments));
            return await CallConstructorAsync(callee.AsClass(), arguments);
        }
        else
        {
            throw new RuntimeException("Can only call functions, prompts, and classes.", expr.Line, _currentFile);
        }
    }
}
