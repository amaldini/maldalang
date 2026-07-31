// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;

public class CatchClause : Node
{
    public string? ExceptionVariable { get; }
    /// <summary>Optional guard: <c>catch (e if condition)</c> (Phase 4.5).</summary>
    public Expression? Filter { get; }
    public BlockStatement Body { get; }
    
    public CatchClause(
        string? exceptionVariable,
        BlockStatement body,
        Expression? filter = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        ExceptionVariable = exceptionVariable;
        Filter = filter;
        Body = body;
    }
}
