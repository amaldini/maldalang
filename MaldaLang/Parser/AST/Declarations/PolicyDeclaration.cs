// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

public sealed class PolicyRule
{
    public string Domain { get; }
    public string Action { get; }
    public List<string> Arguments { get; }

    public PolicyRule(string domain, string action, List<string> arguments)
    {
        Domain = domain;
        Action = action;
        Arguments = arguments ?? new List<string>();
    }
}

/// <summary>
/// File-level <c>policy { fs: readOnly under "./work"; … }</c>.
/// </summary>
public sealed class PolicyDeclaration : Statement
{
    public List<PolicyRule> Rules { get; }

    public PolicyDeclaration(List<PolicyRule> rules, int line = 0, int column = 0)
        : base(line, column)
    {
        Rules = rules ?? new List<PolicyRule>();
    }
}
