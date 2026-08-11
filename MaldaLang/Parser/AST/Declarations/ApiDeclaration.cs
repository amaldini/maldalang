// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Closed API for typed prompt programs: <c>api Calc { function add(a, b); }</c>.
/// Method bodies live as top-level functions with the same name.
/// </summary>
public sealed class ApiMethodSignature
{
    public string Name { get; }
    public List<string> ParameterNames { get; }

    public ApiMethodSignature(string name, List<string> parameterNames)
    {
        Name = name;
        ParameterNames = parameterNames ?? new List<string>();
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
