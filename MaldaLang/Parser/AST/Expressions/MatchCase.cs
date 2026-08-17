// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

using MaldaLang.Parser.AST.Statements;

public class MatchCase : Node
{
    public Pattern Pattern { get; }
    /// <summary>Optional guard: <c>case pattern if condition:</c>.</summary>
    public Expression? Guard { get; }
    public Statement Body { get; }

    public MatchCase(Pattern pattern, Statement body, int line = 0, int column = 0, Expression? guard = null)
        : base(line, column)
    {
        Pattern = pattern;
        Guard = guard;
        Body = body;
    }
}
