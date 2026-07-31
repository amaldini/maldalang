// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;

public class WorkflowApprovalStatement : Statement
{
    public string ApprovalId { get; }
    public Expression ApprovalNameExpr { get; }
    public Expression PayloadExpr { get; }
    public int? TimeoutMs { get; }
    public Expression? OnReject { get; }

    public WorkflowApprovalStatement(string approvalId, Expression approvalNameExpr, Expression payloadExpr, int? timeoutMs, Expression? onReject, int line = 0, int column = 0)
        : base(line, column)
    {
        ApprovalId = approvalId;
        ApprovalNameExpr = approvalNameExpr;
        PayloadExpr = payloadExpr;
        TimeoutMs = timeoutMs;
        OnReject = onReject;
    }
}
