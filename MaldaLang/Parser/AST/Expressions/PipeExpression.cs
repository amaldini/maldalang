// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class PipeExpression : Expression
{
    public Expression Left { get; }
    public Expression Right { get; }

    public PipeExpression(Expression left, Expression right, int line = 0, int column = 0)
        : base(line, column)
    {
        Left = left;
        Right = right;
    }
}
