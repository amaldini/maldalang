// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Offline fixture in/out for a <see cref="PromptInstance"/>. Same extract / parse /
/// <see cref="TypedPromptValidator.TryValidateReturnType"/> coerce path as
/// <c>await prompt … -&gt; Type</c>, with no LLM, no repair loop, and no gather round.
/// </summary>
public static class PromptEval
{
    public static RuntimeValue EvalPrompt(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("evalPrompt", args, 2, 3, "prompt, fixture, typeName?");
        if (args[0].Type != ValueType.Object || args[0].AsObject() is not PromptInstance instance)
            throw new Exception("evalPrompt() first argument must be a PromptInstance.");

        RuntimeValue? typeOverride = args.Count >= 3 ? args[2] : null;
        return Eval(instance, args[1], typeOverride, interpreter);
    }

    public static RuntimeValue Eval(
        PromptInstance instance,
        RuntimeValue fixture,
        RuntimeValue? typeNameOverride,
        Interpreter? interpreter)
    {
        var typeName = instance.ReturnType;
        if (typeNameOverride != null && typeNameOverride.Type != ValueType.Null)
        {
            if (typeNameOverride.Type != ValueType.String)
                throw new Exception("evalPrompt() typeName must be a string.");
            var overrideName = typeNameOverride.AsString().Trim();
            if (overrideName.Length > 0)
                typeName = overrideName;
        }

        if (!TryCoerceFixture(fixture, out var parsed, out var coerceError))
        {
            if (!string.IsNullOrWhiteSpace(typeName))
                return Fail(coerceError);
            return Ok(fixture);
        }

        if (string.IsNullOrWhiteSpace(typeName))
            return Ok(parsed);

        if (!TypedPromptValidator.TryValidateReturnType(
                parsed,
                typeName,
                interpreter,
                out var validated,
                out var error))
        {
            return Fail(error);
        }

        return Ok(validated);
    }

    private static bool TryCoerceFixture(RuntimeValue fixture, out RuntimeValue parsed, out string error)
    {
        parsed = fixture;
        error = "";

        if (fixture.Type != ValueType.String)
            return true;

        var content = fixture.AsString();
        if (!TypedPromptValidator.TryExtractJsonCandidate(content, out var json, out var extractError))
        {
            error = extractError;
            return false;
        }

        if (!TypedPromptValidator.TryParseJson(json, out parsed, out var parseError))
        {
            error = parseError;
            return false;
        }

        return true;
    }

    private static RuntimeValue Ok(RuntimeValue data)
    {
        var result = new JsonObject();
        result.Set("ok", RuntimeValue.Boolean(true));
        result.Set("data", data);
        return RuntimeValue.Object(result);
    }

    private static RuntimeValue Fail(string error)
    {
        var result = new JsonObject();
        result.Set("ok", RuntimeValue.Boolean(false));
        result.Set("error", RuntimeValue.String(error));
        return RuntimeValue.Object(result);
    }
}
