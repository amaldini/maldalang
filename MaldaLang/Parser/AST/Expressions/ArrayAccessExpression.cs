// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class ArrayAccessExpression : Expression
{
    public Expression Array { get; }
    public Expression Index { get; }
    /// <summary>Null-conditional index (<c>?[</c>, Phase 4.4).</summary>
    public bool IsNullConditional { get; }

    public ArrayAccessExpression(Expression array, Expression index, bool isNullConditional = false, int line = 0, int column = 0)
        : base(line, column)
    {
        Array = array;
        Index = index;
        IsNullConditional = isNullConditional;
    }
}