// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

/// <summary>
/// <c>within (30s) budget(tokens: 50000) { … }</c>
/// </summary>
public sealed class WithinStatement : Statement
{
    public int TimeoutMs { get; }
    public int? BudgetTokens { get; }
    public int? BudgetTools { get; }
    public double? BudgetCost { get; }
    public BlockStatement Body { get; }

    public bool HasBudget => BudgetTokens is > 0 || BudgetTools is > 0 || BudgetCost is > 0;

    public WithinStatement(
        int timeoutMs,
        int? budgetTokens,
        int? budgetTools,
        double? budgetCost,
        BlockStatement body,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        TimeoutMs = timeoutMs;
        BudgetTokens = budgetTokens;
        BudgetTools = budgetTools;
        BudgetCost = budgetCost;
        Body = body;
    }
}
