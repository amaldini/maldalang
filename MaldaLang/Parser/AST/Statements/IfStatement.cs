// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class IfStatement : Statement
{
    public Expression Condition { get; }
    public Statement ThenBranch { get; }
    public Statement? ElseBranch { get; }
    
    public IfStatement(Expression condition, Statement thenBranch, Statement? elseBranch = null, int line = 0, int column = 0)
        : base(line, column)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseBranch = elseBranch;
    }
}