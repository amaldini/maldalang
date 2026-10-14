// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class WhileStatement : Statement
{
    public Expression Condition { get; }
    public Statement Body { get; }

    /// <summary>
    /// The parser's lowering of a <c>for</c> with an increment: <see cref="Body"/> is
    /// <c>Block { userBody, increment }</c> and <c>continue</c> in the user body still runs
    /// the increment. A user-written <c>while</c> whose block happens to hold two statements
    /// is not one.
    /// </summary>
    public bool IsDesugaredFor { get; init; }

    /// <summary>The increment of a desugared <c>for</c>, or null for any other loop.</summary>
    public Statement? DesugaredForIncrement =>
        IsDesugaredFor && Body is BlockStatement { Statements.Count: 2 } block ? block.Statements[1] : null;

    public WhileStatement(Expression condition, Statement body, int line = 0, int column = 0)
        : base(line, column)
    {
        Condition = condition;
        Body = body;
    }
}