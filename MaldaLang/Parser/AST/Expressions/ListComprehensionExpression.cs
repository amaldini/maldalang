// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class ListComprehensionExpression : Expression
{
    public Expression Element { get; }
    public string Variable { get; }
    public Expression Iterable { get; }
    public Expression? Filter { get; }

    public ListComprehensionExpression(
        Expression element,
        string variable,
        Expression iterable,
        Expression? filter = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Element = element;
        Variable = variable;
        Iterable = iterable;
        Filter = filter;
    }
}
