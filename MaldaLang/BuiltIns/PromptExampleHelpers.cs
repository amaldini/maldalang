// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;
using MaldaLang.Interpreter;
using MaldaLangValueType = MaldaLang.Interpreter.ValueType;

public static class PromptExampleHelpers
{
    public static List<PromptExample>? ParseExamplesOrNull(RuntimeValue value)
    {
        if (value.Type == MaldaLangValueType.Null)
            return null;

        if (value.Type != MaldaLangValueType.Array)
            throw new RuntimeException("Prompt 'examples' field must be an array of { input, output } objects.");

        var examples = new List<PromptExample>();
        foreach (var item in value.AsArray())
        {
            if (!TryParseExampleItem(item, out var example, out var error))
                throw new RuntimeException(error ?? "Invalid prompt example entry.");

            examples.Add(example);
        }

        return examples.Count > 0 ? examples : null;
    }

    public static RuntimeValue ToRuntimeArray(IReadOnlyList<PromptExample>? examples)
    {
        if (examples == null || examples.Count == 0)
            return RuntimeValue.Null();

        var items = new List<RuntimeValue>(examples.Count);
        foreach (var example in examples)
        {
            var obj = new JsonObject();
            obj.Set("input", RuntimeValue.String(example.Input));
            obj.Set("output", RuntimeValue.String(example.Output));
            items.Add(RuntimeValue.Object(obj));
        }

        return RuntimeValue.Array(items);
    }

    public static void ApplyParameterInterpolation(List<PromptExample> examples, List<string> paramNames, List<RuntimeValue> arguments)
    {
        for (int i = 0; i < examples.Count; i++)
        {
            var example = examples[i];
            var input = example.Input;
            var output = example.Output;

            for (int p = 0; p < paramNames.Count; p++)
            {
                var placeholder = "{" + paramNames[p] + "}";
                var replacement = arguments[p].ToString();
                if (input.Contains(placeholder))
                    input = input.Replace(placeholder, replacement);
                if (output.Contains(placeholder))
                    output = output.Replace(placeholder, replacement);
            }

            examples[i] = new PromptExample(input, output);
        }
    }

    private static bool TryParseExampleItem(RuntimeValue item, out PromptExample example, out string? error)
    {
        example = default;
        error = null;

        if (item.Type != MaldaLangValueType.Object)
        {
            error = "Each prompt example must be an object with 'input' and 'output' strings.";
            return false;
        }

        string? input = null;
        string? output = null;

        var obj = item.AsObject();
        if (obj is DictionaryInstance dict)
        {
            if (dict.TryGetEntry("input", out var inputValue) && inputValue.Type == MaldaLangValueType.String)
                input = inputValue.AsString();
            else if (dict.TryGetEntry("user", out var userValue) && userValue.Type == MaldaLangValueType.String)
                input = userValue.AsString();

            if (dict.TryGetEntry("output", out var outputValue) && outputValue.Type == MaldaLangValueType.String)
                output = outputValue.AsString();
            else if (dict.TryGetEntry("assistant", out var assistantValue) && assistantValue.Type == MaldaLangValueType.String)
                output = assistantValue.AsString();
        }
        else if (obj is JsonObject jsonObj)
        {
            input = GetStringField(jsonObj, "input") ?? GetStringField(jsonObj, "user");
            output = GetStringField(jsonObj, "output") ?? GetStringField(jsonObj, "assistant");
        }

        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
        {
            error = "Each prompt example must include non-empty 'input' and 'output' strings.";
            return false;
        }

        example = new PromptExample(input, output);
        return true;
    }

    private static string? GetStringField(JsonObject jsonObj, string name)
    {
        var value = jsonObj.Get(name);
        return value.Type == MaldaLangValueType.String ? value.AsString() : null;
    }
}
