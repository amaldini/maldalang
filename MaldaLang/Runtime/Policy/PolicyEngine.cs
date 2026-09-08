// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Policy;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Runtime.Journal;

/// <summary>
/// Program-boundary capability policy. Enforced on cap consume regardless of host tools.
/// </summary>
public sealed class PolicyEngine
{
    private static readonly AsyncLocal<PolicyEngine?> CurrentLocal = new();
    private readonly List<PolicyRule> _rules;

    public PolicyEngine(IEnumerable<PolicyRule> rules)
    {
        _rules = rules.ToList();
    }

    public static PolicyEngine? Current
    {
        get => CurrentLocal.Value;
        set => CurrentLocal.Value = value;
    }

    public static void ResetForTesting()
    {
        CurrentLocal.Value = null;
        ApprovalCallback = null;
    }

    public static void Install(PolicyDeclaration? decl)
    {
        Current = decl == null ? null : new PolicyEngine(decl.Rules);
    }

    public void EnsureAllowed(string domain, string action, string target)
    {
        var rule = _rules.LastOrDefault(r => string.Equals(r.Domain, domain, StringComparison.Ordinal));
        if (rule == null)
            return;

        if (string.Equals(rule.Action, "deny", StringComparison.OrdinalIgnoreCase))
            Deny(domain, target, "policy deny");

        if (string.Equals(rule.Action, "approve", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryApprove(domain, target))
                Deny(domain, target, "approval required");
            return;
        }

        if (string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rule.Action, "readOnly", StringComparison.OrdinalIgnoreCase))
        {
            if (rule.Arguments.Count == 0)
                return;
            if (!rule.Arguments.Any(pattern => Matches(pattern, target)))
                Deny(domain, target, $"not allowed by policy ({rule.Action})");
        }

        if (string.Equals(rule.Action, "readOnly", StringComparison.OrdinalIgnoreCase)
            && string.Equals(action, "write", StringComparison.OrdinalIgnoreCase))
            Deny(domain, target, "policy is readOnly");
    }

    private static bool Matches(string pattern, string target)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;
        if (pattern.StartsWith("*."))
            return target.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        if (pattern.Contains('*', StringComparison.Ordinal))
        {
            var prefix = pattern.Split('*')[0];
            return target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return target.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Host hook for <c>policy { shell: approve }</c>. Tests / Desktop IDE can set this.
    /// Env <c>MALDA_POLICY_APPROVE=1</c> auto-approves (dev / replay).
    /// </summary>
    public static Func<string, string, bool>? ApprovalCallback { get; set; }

    private static bool TryApprove(string domain, string target)
    {
        var env = System.Environment.GetEnvironmentVariable("MALDA_POLICY_APPROVE");
        if (string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        return ApprovalCallback?.Invoke(domain, target) ?? false;
    }

    private static void Deny(string domain, string target, string reason)
    {
        RunJournal.Current.Append(new JournalEvent
        {
            Kind = JournalKind.Cap,
            Name = domain,
            Ok = false,
            Error = reason
        });
        throw new RuntimeException($"ToolDenied: {domain} {target}: {reason}");
    }
}
