// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Deterministic executor for <see cref="ProgramInstance"/> values from typed prompts.
/// </summary>
public static class ProgramRunner
{
    public static RuntimeValue Run(RuntimeValue programValue, Interpreter? interpreter)
    {
        var program = CoerceProgram(programValue);
        if (!ApiRegistry.TryGet(program.ApiName, out var apiDef))
            throw new Exception($"runProgram: unknown api '{program.ApiName}'.");

        var bindings = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
        foreach (var step in program.Steps)
        {
            if (!apiDef.TryGetMethod(step.Call, out var method))
                throw new Exception($"runProgram: '{step.Call}' is not a method on api '{program.ApiName}'.");

            if (step.Args.Count != method.ParameterNames.Count)
            {
                throw new Exception(
                    $"runProgram: '{step.Call}' expects {method.ParameterNames.Count} argument(s), got {step.Args.Count}.");
            }

            var resolvedArgs = new List<RuntimeValue>();
            foreach (var arg in step.Args)
                resolvedArgs.Add(ResolveArg(arg, bindings));

            var result = InvokeMethod(step.Call, resolvedArgs, interpreter);
            bindings[step.Alias] = result;
        }

        return ResolveArg(program.ReturnValue, bindings);
    }

    private static ProgramInstance CoerceProgram(RuntimeValue programValue)
    {
        if (programValue.Type == ValueType.Object && programValue.AsObject() is ProgramInstance program)
            return program;

        if (programValue.Type == ValueType.Object && programValue.AsObject() is JsonObject)
        {
            if (!ApiRegistry.TryResolveApiNameFromProgramJson(programValue, out var apiName))
                throw new Exception("runProgram() JSON program requires string @api (or exactly one registered api).");

            if (!ApiRegistry.TryResolveProgramSchema(apiName, out var schema))
                throw new Exception($"runProgram() unknown api '{apiName}'.");

            if (!TypedPromptValidator.TryValidateReturnType(programValue, schema, out var validated, out var error))
                throw new Exception("runProgram() could not coerce JSON to a program: " + error);

            if (validated.Type == ValueType.Object && validated.AsObject() is ProgramInstance coerced)
                return coerced;

            throw new Exception("runProgram() validation did not produce a program instance.");
        }

        throw new Exception("runProgram() expects a program value from await prompt(...) -> program(Api), or equivalent JSON.");
    }

    private static RuntimeValue ResolveArg(RuntimeValue arg, Dictionary<string, RuntimeValue> bindings)
    {
        if (ProgramJsonNormalizer.IsAliasRef(arg, out var alias))
        {
            if (!bindings.TryGetValue(alias, out var bound))
                throw new Exception($"runProgram: unknown step alias '${alias}'.");
            return bound;
        }

        return arg;
    }

    private static RuntimeValue InvokeMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (interpreter != null)
        {
            if (!interpreter._globals.TryGet(methodName, out var fnVal) || fnVal.Type != ValueType.Function)
            {
                throw new Exception(
                    $"runProgram: no top-level function '{methodName}' implementing the api method.");
            }

            return interpreter.CallFunctionAsync(fnVal.AsFunction(), args)
                .GetAwaiter()
                .GetResult();
        }

        if (ApiRegistry.TryInvokeBound(methodName, args, out var boundResult))
            return boundResult;

        throw new Exception(
            $"runProgram: no bound implementation for '{methodName}' (transpile bindings missing).");
    }
}
