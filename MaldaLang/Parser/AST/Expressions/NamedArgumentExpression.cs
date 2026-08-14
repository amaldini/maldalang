// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

/// <summary>
/// Decorator named argument: <c>tokens: 4000</c> inside <c>@budget(tokens: 4000, tools: 8)</c>.
/// Parsed only in decorator argument lists, not in general call sites.
/// </summary>
public class NamedArgumentExpression : Expression
{
    public string Name { get; }
    public Expression Value { get; }

    public NamedArgumentExpression(string name, Expression value, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
        Value = value;
    }
}
