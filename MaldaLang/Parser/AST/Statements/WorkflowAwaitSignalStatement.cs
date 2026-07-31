// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;

public class WorkflowAwaitSignalStatement : Statement
{
    public string SignalId { get; }
    public Expression SignalNameExpr { get; }
    public Expression PayloadExpr { get; }
    public int? TimeoutMs { get; }

    public WorkflowAwaitSignalStatement(string signalId, Expression signalNameExpr, Expression payloadExpr, int? timeoutMs, int line = 0, int column = 0)
        : base(line, column)
    {
        SignalId = signalId;
        SignalNameExpr = signalNameExpr;
        PayloadExpr = payloadExpr;
        TimeoutMs = timeoutMs;
    }
}
