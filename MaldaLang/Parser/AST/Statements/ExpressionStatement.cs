// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class ExpressionStatement : Statement
{
    public Expression Expression { get; }
    
    public ExpressionStatement(Expression expression, int line = 0, int column = 0)
        : base(line, column)
    {
        Expression = expression;
    }
}