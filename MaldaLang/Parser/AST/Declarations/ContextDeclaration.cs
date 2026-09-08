// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

/// <summary>
/// <c>context Session { budget: 8000 tokens; … }</c>
/// </summary>
public sealed class ContextDeclaration : Statement
{
    public string Name { get; }
    public int? BudgetTokens { get; }
    public List<string> Pin { get; }
    public int? RetainLast { get; }
    public string Evict { get; }
    public string? CompactPromptName { get; }

    public ContextDeclaration(
        string name,
        int? budgetTokens,
        List<string> pin,
        int? retainLast,
        string evict,
        string? compactPromptName,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Name = name;
        BudgetTokens = budgetTokens;
        Pin = pin ?? new List<string>();
        RetainLast = retainLast;
        Evict = string.IsNullOrEmpty(evict) ? "oldest" : evict;
        CompactPromptName = compactPromptName;
    }
}
