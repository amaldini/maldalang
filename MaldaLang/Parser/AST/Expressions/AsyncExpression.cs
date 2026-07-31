// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

using MaldaLang.Parser.AST;

public class AsyncExpression : Expression
{
    public Expression Expression { get; }

    public AsyncExpression(Expression expression, int line = 0, int column = 0)
        : base(line, column)
    {
        Expression = expression;
    }
}
