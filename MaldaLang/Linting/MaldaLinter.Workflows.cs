// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Linting;

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

public static partial class MaldaLinter
{
    /// <summary>
    /// Effects that replay re-runs when they sit in a workflow body outside a <c>step</c>, and that the
    /// WF1001/WF1002 deny-list (<see cref="BuiltInRegistry.WorkflowNonDeterministicBuiltIns"/>,
    /// <see cref="BuiltInRegistry.WorkflowSideEffectingBuiltIns"/>) does not cover. Names are the
    /// flat built-in names; <c>io.*</c> / <c>pdf.*</c> / <c>doc.*</c> calls resolve to them.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> WorkflowEffectBuiltIns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["runPrompt"] = "model call",
        ["executePlan"] = "model call",
        ["decomposeTask"] = "model call",
        ["generateUI"] = "model call",
        ["uiGenerate"] = "model call",
        ["stream"] = "model call",
        ["webFetch"] = "network call",
        ["webSearch"] = "network call",
        ["readFile"] = "file read",
        ["readFileBase64"] = "file read",
        ["readTextFileLines"] = "file read",
        ["readPng"] = "file read",
        ["listDirectory"] = "file read",
        ["glob"] = "file read",
        ["grep"] = "file read",
        ["extractPdfText"] = "file read",
        ["extractDocxText"] = "file read",
        ["loadDocuments"] = "file read",
        ["input"] = "console input",
        ["writeFileBase64"] = "file write",
        ["ensureDir"] = "file write",
        ["insertAtLine"] = "file write",
        ["gitStatus"] = "repository read",
        ["gitDiff"] = "repository read",
        ["gitLog"] = "repository read",
        ["gitAdd"] = "repository write",
        ["gitCommit"] = "repository write",
        ["gitBranch"] = "repository write",
        ["gitCheckout"] = "repository write",
        ["gitPull"] = "repository write",
        ["gitPush"] = "repository write",
        ["enqueueJob"] = "job enqueue",
        ["startWorkflow"] = "workflow start",
        ["signalWorkflow"] = "workflow signal",
        ["cancelWorkflow"] = "workflow cancel",
    };

    // ML0004 — effects beyond the deny-list in a workflow body outside a step, directly or via
    // same-file helpers (the same call-graph walk WF1001/WF1002 use, bounded depth).
    private static void CheckWorkflowEffects(LintContext context)
    {
        foreach (var workflow in context.Nodes().OfType<WorkflowDeclaration>())
            WalkWorkflowCode(context, workflow, workflow.Body, new List<string>());
    }

    private static void WalkWorkflowCode(LintContext context, WorkflowDeclaration workflow, Node root, List<string> helpers)
    {
        // onReject handlers run under step rules, like the step call itself.
        var stepRuled = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        bool Descend(Node n) => n is not WorkflowStepStatement and not FunctionDeclaration && !stepRuled.Contains(n);

        foreach (var node in AstNodeWalker.Descendants(root, Descend))
        {
            if (node is WorkflowApprovalStatement { OnReject: not null } approval)
                stepRuled.Add(approval.OnReject);
            if (stepRuled.Contains(node) || node is not FunctionCallExpression call)
                continue;

            switch (call.Callee)
            {
                case IdentifierExpression id:
                    AnalyzeWorkflowCallee(context, workflow, call, id.Name, helpers);
                    break;
                case MemberAccessExpression { Object: IdentifierExpression ns } member
                    when ns.Name is StdLibNamespaces.IoModule or StdLibNamespaces.PdfModule or StdLibNamespaces.DocModule
                         && StdLibNamespaces.IsStdLibModuleMethod(ns.Name, member.Member):
                    var flat = ns.Name switch
                    {
                        StdLibNamespaces.PdfModule => StdLibNamespaces.ResolvePdfBuiltInName(member.Member),
                        StdLibNamespaces.DocModule => StdLibNamespaces.ResolveDocBuiltInName(member.Member),
                        _ => member.Member
                    };
                    if (WorkflowEffectBuiltIns.TryGetValue(flat, out var kind))
                        ReportWorkflowEffect(context, workflow, call, ns.Name + "." + member.Member, kind, helpers);
                    break;
                case MemberAccessExpression member when ModelMethodNames.Contains(member.Member):
                    ReportWorkflowEffect(context, workflow, call, "." + member.Member + "(…)", "model call", helpers);
                    break;
            }
        }
    }

    private static void AnalyzeWorkflowCallee(
        LintContext context,
        WorkflowDeclaration workflow,
        FunctionCallExpression call,
        string name,
        List<string> helpers)
    {
        if (context.Prompts.ContainsKey(name))
        {
            ReportWorkflowEffect(context, workflow, call, name, "prompt (model call)", helpers);
            return;
        }

        if (context.Functions.TryGetValue(name, out var function))
        {
            if (helpers.Count >= WorkflowDeterminismDiagnostics.MaxCalleeDepth || helpers.Contains(name, StringComparer.Ordinal))
                return;
            helpers.Add(name);
            WalkWorkflowCode(context, workflow, function.Body, helpers);
            helpers.RemoveAt(helpers.Count - 1);
            return;
        }

        if (WorkflowEffectBuiltIns.TryGetValue(name, out var kind))
            ReportWorkflowEffect(context, workflow, call, name, kind, helpers);
    }

    private static void ReportWorkflowEffect(
        LintContext context,
        WorkflowDeclaration workflow,
        FunctionCallExpression call,
        string name,
        string kind,
        List<string> helpers)
    {
        var via = helpers.Count == 0 ? "" : $" (via {string.Join(" → ", helpers.Select(h => $"'{h}'"))})";
        context.Report(
            LintRules.WorkflowEffectOutsideStep,
            call,
            Math.Max(1, name.Length),
            $"'{name}' ({kind}) runs in workflow '{workflow.Name}' outside a step{via}. Replay re-runs the body, so it runs again on every resume; WF1001/WF1002 do not cover it.",
            "Move it into a step: step result = helper(...);");
    }

    // ML0006 — step inside while / for / for-in in a workflow body: memoized by name.
    private static void CheckStepsInLoops(LintContext context)
    {
        var reported = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        foreach (var workflow in context.Nodes().OfType<WorkflowDeclaration>())
        {
            foreach (var loop in AstNodeWalker.Descendants(workflow.Body, n => n is not FunctionDeclaration and not LambdaExpression))
            {
                Statement? body = loop switch
                {
                    WhileStatement w => w.Body,
                    ForStatement f => f.Body,
                    ForInStatement f => f.Body,
                    _ => null
                };
                if (body == null)
                    continue;

                foreach (var step in AstNodeWalker.Descendants(body, n => n is not FunctionDeclaration and not LambdaExpression)
                             .OfType<WorkflowStepStatement>())
                {
                    if (!reported.Add(step))
                        continue;
                    context.Report(
                        LintRules.StepInLoop,
                        step,
                        "step".Length,
                        $"step '{step.StepId}' is inside a loop in workflow '{workflow.Name}'. Steps are memoized by name: after the first success, later iterations replay the journaled result and the call does not run again.",
                        "Put the loop inside the step callee, or give each iteration its own step");
                }
            }
        }
    }
}
