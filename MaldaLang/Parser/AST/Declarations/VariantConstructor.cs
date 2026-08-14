// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using System.Collections.Generic;

public class VariantConstructor
{
    public string Name { get; }
    public List<string> ParameterNames { get; }

    /// <summary>
    /// Optional payload types parallel to <see cref="ParameterNames"/>.
    /// A null entry means the payload field is untyped (permissive JSON schema).
    /// Type strings match schema fields: primitives, <c>[]</c> arrays, schema or sum-type names.
    /// </summary>
    public List<string?> ParameterTypes { get; }

    /// <summary>
    /// Parallel to <see cref="ParameterNames"/>. False when the payload was declared with <c>?</c>
    /// (omitted from JSON Schema <c>required</c>). Constructor call arity is unchanged.
    /// </summary>
    public List<bool> ParameterRequired { get; }

    public VariantConstructor(string name, List<string> parameterNames)
        : this(name, parameterNames, parameterTypes: null, parameterRequired: null)
    {
    }

    public VariantConstructor(
        string name,
        List<string> parameterNames,
        List<string?>? parameterTypes,
        List<bool>? parameterRequired)
    {
        Name = name;
        ParameterNames = parameterNames ?? new List<string>();
        ParameterTypes = new List<string?>(ParameterNames.Count);
        ParameterRequired = new List<bool>(ParameterNames.Count);
        for (var i = 0; i < ParameterNames.Count; i++)
        {
            ParameterTypes.Add(
                parameterTypes != null && i < parameterTypes.Count ? parameterTypes[i] : null);
            ParameterRequired.Add(
                parameterRequired == null || i >= parameterRequired.Count || parameterRequired[i]);
        }
    }

    public string? ParameterTypeAt(int index) =>
        index >= 0 && index < ParameterTypes.Count ? ParameterTypes[index] : null;

    public bool ParameterRequiredAt(int index) =>
        index < 0 || index >= ParameterRequired.Count || ParameterRequired[index];

    public string FormatParameter(int index)
    {
        var name = ParameterNames[index];
        var type = ParameterTypeAt(index);
        if (string.IsNullOrEmpty(type))
            return name;
        return ParameterRequiredAt(index) ? $"{name}: {type}" : $"{name}: {type}?";
    }
}
