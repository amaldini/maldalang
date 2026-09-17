// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Simulated annealing: minimize (or maximize) a cost lambda over a neighbor lambda.
/// </summary>
internal static class MathAnneal
{
    public static RuntimeValue Run(List<RuntimeValue> args, Interpreter? interpreter) =>
        RunAsync(args, interpreter).GetAwaiter().GetResult();

    public static async Task<RuntimeValue> RunAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("anneal", args, 3, 4, "initial, cost, neighbor, options?");

        var costFn = RequireUnaryFunction(args[1], "cost");
        var neighborFn = RequireUnaryFunction(args[2], "neighbor");

        var steps = 1000;
        var temp = 1.0;
        var coolingFactor = 0.995;
        RuntimeValue? scheduleFn = null;
        var maximize = false;
        var copy = true;

        if (args.Count == 4)
            ReadOptions(args[3], ref steps, ref temp, ref coolingFactor, ref scheduleFn, ref maximize, ref copy);

        if (steps < 0)
            throw new RuntimeException("anneal() steps must be >= 0");
        if (double.IsNaN(temp) || double.IsInfinity(temp) || temp < 0)
            throw new RuntimeException("anneal() temp must be a finite number >= 0");
        if (scheduleFn == null && (double.IsNaN(coolingFactor) || double.IsInfinity(coolingFactor)))
            throw new RuntimeException("anneal() cooling must be a finite number or a function");

        var current = copy ? CloneState(args[0]) : args[0];
        var currentCost = await EvaluateCostAsync(costFn, current, interpreter);
        var best = copy ? CloneState(current) : current;
        var bestCost = currentCost;

        for (var step = 0; step < steps; step++)
        {
            var candidate = await InvokeUnaryAsync(neighborFn, copy ? CloneState(current) : current, interpreter);
            var candidateCost = await EvaluateCostAsync(costFn, candidate, interpreter);
            var delta = maximize ? currentCost - candidateCost : candidateCost - currentCost;
            if (Accept(delta, temp, interpreter))
            {
                current = candidate;
                currentCost = candidateCost;
                var improved = maximize ? currentCost > bestCost : currentCost < bestCost;
                if (improved)
                {
                    best = copy ? CloneState(current) : current;
                    bestCost = currentCost;
                }
            }

            temp = await NextTemperatureAsync(temp, coolingFactor, scheduleFn, interpreter);
        }

        var result = new DictionaryInstance();
        result.SetEntry("state", best);
        result.SetEntry("cost", RuntimeValue.Float(bestCost));
        result.SetEntry("steps", RuntimeValue.Integer(steps));
        return RuntimeValue.Object(result);
    }

    private static void ReadOptions(
        RuntimeValue optionsValue,
        ref int steps,
        ref double temp,
        ref double coolingFactor,
        ref RuntimeValue? scheduleFn,
        ref bool maximize,
        ref bool copy)
    {
        if (optionsValue.Type != ValueType.Object)
            throw new RuntimeException("anneal() options must be a dictionary");

        if (TryGetOption(optionsValue, "steps", out var stepsValue))
        {
            if (!NumericCoercion.TryAsInteger(stepsValue, out steps))
                throw new RuntimeException("anneal() steps must be an integer");
        }

        if (TryGetOption(optionsValue, "temp", out var tempValue)
            || TryGetOption(optionsValue, "temperature", out tempValue))
        {
            if (!TryAsFloat(tempValue, out temp))
                throw new RuntimeException("anneal() temp must be a number");
        }

        if (TryGetOption(optionsValue, "schedule", out var scheduleValue) && scheduleValue.Type == ValueType.Function)
            scheduleFn = scheduleValue;

        if (TryGetOption(optionsValue, "cooling", out var coolingValue))
        {
            if (coolingValue.Type == ValueType.Function)
            {
                scheduleFn ??= coolingValue;
            }
            else if (TryAsFloat(coolingValue, out coolingFactor))
            {
                // geometric factor
            }
            else
            {
                throw new RuntimeException("anneal() cooling must be a number or a function");
            }
        }

        if (TryGetOption(optionsValue, "maximize", out var maximizeValue))
        {
            if (maximizeValue.Type != ValueType.Boolean)
                throw new RuntimeException("anneal() maximize must be a boolean");
            maximize = maximizeValue.AsBoolean();
        }

        if (TryGetOption(optionsValue, "copy", out var copyValue))
        {
            if (copyValue.Type != ValueType.Boolean)
                throw new RuntimeException("anneal() copy must be a boolean");
            copy = copyValue.AsBoolean();
        }
    }

    private static bool Accept(double delta, double temp, Interpreter? interpreter)
    {
        if (delta <= 0)
            return true;
        if (temp <= 0 || double.IsNaN(temp))
            return false;

        var roll = BuiltInFunctions.CallBuiltIn("random", new List<RuntimeValue>(), interpreter).AsFloat();
        return roll < Math.Exp(-delta / temp);
    }

    private static async Task<double> NextTemperatureAsync(
        double temp,
        double coolingFactor,
        RuntimeValue? scheduleFn,
        Interpreter? interpreter)
    {
        if (scheduleFn == null)
            return temp * coolingFactor;

        var next = await InvokeUnaryAsync(scheduleFn, RuntimeValue.Float(temp), interpreter);
        if (!TryAsFloat(next, out var nextTemp) || double.IsNaN(nextTemp) || double.IsInfinity(nextTemp) || nextTemp < 0)
            throw new RuntimeException("anneal() cooling/schedule must return a finite number >= 0");
        return nextTemp;
    }

    private static async Task<double> EvaluateCostAsync(
        RuntimeValue costFn,
        RuntimeValue state,
        Interpreter? interpreter)
    {
        var cost = await InvokeUnaryAsync(costFn, state, interpreter);
        if (!TryAsFloat(cost, out var number) || double.IsNaN(number) || double.IsInfinity(number))
            throw new RuntimeException("anneal() cost must return a finite number");
        return number;
    }

    private static Task<RuntimeValue> InvokeUnaryAsync(
        RuntimeValue callee,
        RuntimeValue argument,
        Interpreter? interpreter) =>
        AiPipelineHelpers.InvokePipelineCallableAsync(callee, new List<RuntimeValue> { argument }, interpreter);

    private static RuntimeValue RequireUnaryFunction(RuntimeValue value, string role)
    {
        if (value.Type != ValueType.Function)
            throw new RuntimeException($"anneal() {role} must be a function");
        return value;
    }

    private static bool TryGetOption(RuntimeValue source, string key, out RuntimeValue value)
    {
        value = RuntimeValue.Null();
        if (source.Type != ValueType.Object)
            return false;

        var obj = source.AsObject();
        if (obj is DictionaryInstance dict)
        {
            if (dict.TryGetEntry(key, out value) && value.Type != ValueType.Null)
                return true;
            foreach (var kvp in dict.GetEntries())
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && kvp.Value.Type != ValueType.Null)
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        if (obj is JsonObject json)
        {
            var direct = json.Get(key, null);
            if (direct.Type != ValueType.Null)
            {
                value = direct;
                return true;
            }
        }

        try
        {
            var direct = obj.Get(key, null);
            if (direct.Type != ValueType.Null)
            {
                value = direct;
                return true;
            }
        }
        catch (RuntimeException)
        {
        }

        return false;
    }

    private static bool TryAsFloat(RuntimeValue value, out double result)
    {
        switch (value.Type)
        {
            case ValueType.Integer:
                result = value.AsInteger();
                return true;
            case ValueType.Float:
                result = value.AsFloat();
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static RuntimeValue CloneState(RuntimeValue value)
    {
        if (value.Type == ValueType.Array)
        {
            var clone = new List<RuntimeValue>(value.AsArray().Count);
            foreach (var item in value.AsArray())
                clone.Add(CloneState(item));
            return RuntimeValue.Array(clone);
        }

        if (value.Type == ValueType.Object && value.AsObject() is DictionaryInstance dict)
        {
            var entries = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
            foreach (var kvp in dict.GetEntries())
                entries[kvp.Key] = CloneState(kvp.Value);
            return RuntimeValue.Object(new DictionaryInstance(entries));
        }

        return value;
    }
}
