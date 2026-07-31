// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class TernaryExpression : Expression
{
    public Expression Condition { get; }
    public Expression ThenBranch { get; }
    public Expression ElseBranch { get; }
    
    public TernaryExpression(Expression condition, Expression thenBranch, Expression elseBranch, int line = 0, int column = 0)
        : base(line, column)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseBranch = elseBranch;
    }
}