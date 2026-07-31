// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// RunnableSequence-style composed pipeline returned by <c>composePipe(...)</c>.
/// </summary>
public sealed class ComposedPipeInstance : ObjectInstance
{
    private readonly List<RuntimeValue> _steps;
    private readonly Interpreter? _creationInterpreter;

    public ComposedPipeInstance(List<RuntimeValue> steps, Interpreter? creationInterpreter)
        : base(null)
    {
        _steps = steps;
        _creationInterpreter = creationInterpreter;
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (methodName != "call")
            throw new RuntimeException($"Unknown composed pipeline method '{methodName}'.");

        if (arguments.Count < 1)
            throw new RuntimeException("Composed pipeline expects an input argument.");

        return RunAsync(arguments[0], interpreter ?? _creationInterpreter)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<RuntimeValue> RunAsync(RuntimeValue input, Interpreter? interpreter)
    {
        var current = input;
        foreach (var step in _steps)
        {
            current = await AiPipelineHelpers.InvokePipelineCallableAsync(
                step,
                new List<RuntimeValue> { current },
                interpreter ?? _creationInterpreter);
            current = await AiPipelineHelpers.CoerceAwaitResultAsync(current, interpreter ?? _creationInterpreter);
        }

        return current;
    }
}
