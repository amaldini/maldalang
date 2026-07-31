// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using System.Collections.Generic;

public class VariantConstructor
{
    public string Name { get; }
    public List<string> ParameterNames { get; }

    public VariantConstructor(string name, List<string> parameterNames)
    {
        Name = name;
        ParameterNames = parameterNames ?? new List<string>();
    }
}
