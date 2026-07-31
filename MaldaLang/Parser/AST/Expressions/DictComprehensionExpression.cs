// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class DictComprehensionExpression : Expression
{
    public Expression Key { get; }
    public Expression Value { get; }
    public string Variable { get; }
    public Expression Iterable { get; }
    public Expression? Filter { get; }

    public DictComprehensionExpression(
        Expression key,
        Expression value,
        string variable,
        Expression iterable,
        Expression? filter = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Key = key;
        Value = value;
        Variable = variable;
        Iterable = iterable;
        Filter = filter;
    }
}
