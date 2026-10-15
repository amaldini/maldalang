// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Linting;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Severity of a <c>malda lint</c> finding. Only <see cref="Warning"/> and <see cref="Error"/> fail the CLI.</summary>
public enum LintSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// One stable lint rule. <see cref="Gotcha"/> names the row of <c>docs/llm/malda-gotchas.md</c>
/// the rule comes from; that row carries the matching <c>Lint: MLxxxx</c> marker.
/// </summary>
public sealed record LintRule(string Code, LintSeverity Severity, string Title, string Gotcha);

/// <summary>A rule violation at a 1-based line / column.</summary>
public sealed record LintFinding(
    string Code,
    LintSeverity Severity,
    string Message,
    int Line,
    int Column,
    int Length,
    string? Hint = null);

/// <summary>The catalog of <c>malda lint</c> rules. Codes are stable: never renumber or reuse one.</summary>
public static class LintRules
{
    public const string ParseFailed = "ML0000";
    public const string PlainStringInterpolation = "ML0001";
    public const string UncheckedModelJson = "ML0002";
    public const string ModeBPrompt = "ML0003";
    public const string WorkflowEffectOutsideStep = "ML0004";
    public const string LambdaAssignmentDropped = "ML0005";
    public const string StepInLoop = "ML0006";
    public const string CaseBindsVar = "ML0007";
    public const string AlwaysTruthyCondition = "ML0008";
    public const string MapReturnsWrapper = "ML0009";
    public const string StringFunctionOnGetEnv = "ML0010";
    public const string RepeatFractionalCount = "ML0011";
    public const string DispatchEventNumericSession = "ML0012";
    public const string UnreachableCaseAfterCatchAll = "ML0013";

    public static readonly IReadOnlyList<LintRule> All =
    [
        new(ParseFailed, LintSeverity.Error,
            "The file does not parse; lint rules did not run (run malda check for the parser errors)",
            "(none: infrastructure)"),
        new(PlainStringInterpolation, LintSeverity.Warning,
            "A plain (non-$) string contains {name.field}, {name[i]} or {f(x)}; it prints literally",
            "print(\"n is {n}\")"),
        new(UncheckedModelJson, LintSeverity.Warning,
            "parseJSON of model output whose result is never passed to validate / asVariant",
            "Tool / think() JSON used without validate"),
        new(ModeBPrompt, LintSeverity.Info,
            "Prompt combines tools: with -> Type (Mode B: structured output only if the backend supports it)",
            "prompt p() -> MySchema { tools: [...]; ... }"),
        new(WorkflowEffectOutsideStep, LintSeverity.Warning,
            "Model call, network, file or job effect in a workflow body outside a step (beyond the WF1001/WF1002 deny-list)",
            "await solve(expr) in a workflow body"),
        new(LambdaAssignmentDropped, LintSeverity.Warning,
            "Assignment in an expression-bodied lambda; the parser keeps only the right-hand side",
            "items.forEach(x => x.done = true)"),
        new(StepInLoop, LintSeverity.Warning,
            "Workflow step inside a loop; steps are memoized by name, so later iterations replay the first result",
            "step inside while / for expecting N executions"),
        new(CaseBindsVar, LintSeverity.Warning,
            "case NAME: where NAME is a var, not a const; the pattern binds and always matches",
            "case BUY: with var BUY = 1 expecting equality"),
        new(AlwaysTruthyCondition, LintSeverity.Warning,
            "Condition is always true: 0, \"\", [] and .length values are truthy",
            "if (n) / while (n) when n is 0"),
        new(MapReturnsWrapper, LintSeverity.Warning,
            "result.map / option.map callback returns Ok / Err / Some / None; the payload gets wrapped twice",
            "result.map(r, (x) => result.ok(x + 1))"),
        new(StringFunctionOnGetEnv, LintSeverity.Warning,
            "str.* call on io.getEnv(...) directly; getEnv returns null when the variable is unset",
            "str.trim(io.getEnv(\"MISSING\"))"),
        new(RepeatFractionalCount, LintSeverity.Warning,
            "str.repeat count is a / division, which yields a float; an odd numerator fails at run time",
            "str.repeat(\"-\", n / 2) when n is odd"),
        new(DispatchEventNumericSession, LintSeverity.Warning,
            "ui.dispatchEvent second argument is a number; it is the session id (string), not a sequence",
            "ui.dispatchEvent(event, 1) treating the second arg as a sequence"),
        new(UnreachableCaseAfterCatchAll, LintSeverity.Warning,
            "case after an unguarded identifier / _ pattern is unreachable; an if inside a case body does not fall through",
            "case x: { if (x > 10) … } expecting later cases to run"),
    ];

    private static readonly Dictionary<string, LintRule> ByCode =
        All.ToDictionary(r => r.Code, StringComparer.Ordinal);

    public static LintRule Get(string code) => ByCode[code];

    public static bool IsKnown(string code) => ByCode.ContainsKey(code);
}
