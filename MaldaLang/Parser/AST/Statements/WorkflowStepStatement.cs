// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;

public class WorkflowStepStatement : Statement
{
    public string StepId { get; }
    public Expression CallExpression { get; }
    public WorkflowStepOptions? Options { get; }

    public WorkflowStepStatement(string stepId, Expression callExpression, WorkflowStepOptions? options, int line = 0, int column = 0)
        : base(line, column)
    {
        StepId = stepId;
        CallExpression = callExpression;
        Options = options;
    }
}
