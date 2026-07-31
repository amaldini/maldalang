// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class ForStatement : Statement
{
    public Statement? Initializer { get; }
    public Expression? Condition { get; }
    public Expression? Increment { get; }
    public Statement Body { get; }
    
    public ForStatement(Statement? initializer, Expression? condition, Expression? increment, Statement body, int line = 0, int column = 0)
        : base(line, column)
    {
        Initializer = initializer;
        Condition = condition;
        Increment = increment;
        Body = body;
    }
}