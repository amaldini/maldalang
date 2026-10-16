// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class IdentifierExpression : Expression
{
    public string Name { get; }

    /// <summary>
    /// True when <see cref="Name"/> is a flat built-in but a user binding (function, variable,
    /// parameter, import, pattern) with that name is lexically in scope here, so the use means
    /// the user's binding. Set by <see cref="MaldaLang.BuiltIns.BuiltInShadowAnalysis"/>.
    /// </summary>
    public bool ShadowsBuiltIn { get; set; }

    public IdentifierExpression(string name, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
    }
}