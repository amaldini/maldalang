// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Closed API for typed prompt programs: <c>api Calc { function add(a, b); }</c>.
/// Method bodies live as top-level functions with the same name.
/// Parameter types are optional (<c>add(a: number, b: number)</c>) and feed
/// program JSON Schema / coercion; name-only stays permissive.
/// </summary>
public sealed class ApiMethodSignature
{
    public string Name { get; }
    public List<string> ParameterNames { get; }

    /// <summary>
    /// Optional types parallel to <see cref="ParameterNames"/>.
    /// A null entry means the argument is untyped (permissive JSON schema).
    /// Type strings match schema fields: primitives, <c>[]</c> arrays, schema or sum-type names.
    /// </summary>
    public List<string?> ParameterTypes { get; }

    /// <summary>
    /// Parallel to <see cref="ParameterNames"/>. False when declared with <c>?</c>.
    /// Program call arity is unchanged — every slot is still required.
    /// </summary>
    public List<bool> ParameterRequired { get; }

    public ApiMethodSignature(string name, List<string> parameterNames)
        : this(name, parameterNames, parameterTypes: null, parameterRequired: null)
    {
    }

    public ApiMethodSignature(
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

public sealed class ApiDeclaration : Statement
{
    public string Name { get; }
    public List<ApiMethodSignature> Methods { get; }

    public ApiDeclaration(string name, List<ApiMethodSignature> methods, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
        Methods = methods ?? new List<ApiMethodSignature>();
    }
}
