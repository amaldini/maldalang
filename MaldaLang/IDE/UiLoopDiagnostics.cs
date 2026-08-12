// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Static UI loop footgun checks: dispatch without pull before render (UI1001),
/// and mixed @PAGE / ui.* surface in one file (UI1002 Info). Heuristic name walk; no call-graph.
/// </summary>
public static class UiLoopDiagnostics
{
    private enum UiCallKind
    {
        DispatchEvent,
        PullEvent,
        MountOrRender
    }

    private readonly record struct UiCallSite(UiCallKind Kind, int Line, int Column, int Length);

    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics)
    {
        var list = statements as IList<Statement> ?? statements.ToList();
        CheckMixedSurface(list, diagnostics);

        AnalyzeStatementList(list, diagnostics);
        foreach (var stmt in list)
        {
            if (stmt is FunctionDeclaration func)
                AnalyzeStatementList(func.Body.Statements, diagnostics);
        }
    }

    private static void CheckMixedSurface(IList<Statement> statements, List<Diagnostic> diagnostics)
    {
        Decorator? pageDecorator = null;
        Expression? uiMountOrRender = null;

        foreach (var stmt in statements)
        {
            if (stmt is FunctionDeclaration func)
            {
                foreach (var d in func.Decorators)
                {
                    if (string.Equals(d.Name, "PAGE", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(d.Name, "AIPAGE", StringComparison.OrdinalIgnoreCase))
                    {
                        pageDecorator ??= d;
                    }
                }
            }
        }

        CollectUiMountOrRender(statements, ref uiMountOrRender);
        if (pageDecorator == null || uiMountOrRender == null)
            return;

        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Info,
            Message =
                "UI1002: This file mixes @PAGE/@AIPAGE with ui.mount/ui.render. Prefer one model per surface " +
                "(@PAGE HTML vs ui.* trees). Intentional hybrids need clear boundaries — see ReferenceManual/16-web-ui-hub.html.",
            Line = Math.Max(0, pageDecorator.Line - 1),
            Column = Math.Max(0, pageDecorator.Column - 1),
            Length = Math.Max(1, pageDecorator.Name.Length + 1),
            Source = "UI1002",
            RelatedDocumentationPath = "ReferenceManual/16-web-ui-hub.html",
            RelatedDocumentationTitle = "Web UI Overview",
            RelatedExamplePath = "Examples/Web/ui_event_loop.malda",
            RelatedExampleTitle = "UI event loop"
        });
    }

    private static void CollectUiMountOrRender(IEnumerable<Statement> statements, ref Expression? found)
    {
        if (found != null)
            return;

        foreach (var stmt in statements)
        {
            CollectUiMountOrRenderInStatement(stmt, ref found);
            if (found != null)
                return;
        }
    }

    private static void CollectUiMountOrRenderInStatement(Statement statement, ref Expression? found)
    {
        if (found != null)
            return;

        switch (statement)
        {
            case BlockStatement block:
                CollectUiMountOrRender(block.Statements, ref found);
                break;
            case FunctionDeclaration func:
                CollectUiMountOrRender(func.Body.Statements, ref found);
                break;
            case ExpressionStatement exprStmt:
                CollectUiMountOrRenderInExpression(exprStmt.Expression, ref found);
                break;
            case PrintStatement print:
                CollectUiMountOrRenderInExpression(print.Expression, ref found);
                break;
            case VarDeclStatement varDecl when varDecl.Initializer != null:
                CollectUiMountOrRenderInExpression(varDecl.Initializer, ref found);
                break;
            case AssignmentStatement assign:
                CollectUiMountOrRenderInExpression(assign.Value, ref found);
                break;
            case ReturnStatement ret when ret.Value != null:
                CollectUiMountOrRenderInExpression(ret.Value, ref found);
                break;
            case IfStatement ifStmt:
                CollectUiMountOrRenderInStatement(ifStmt.ThenBranch, ref found);
                if (ifStmt.ElseBranch != null)
                    CollectUiMountOrRenderInStatement(ifStmt.ElseBranch, ref found);
                break;
            case WhileStatement whileStmt:
                CollectUiMountOrRenderInStatement(whileStmt.Body, ref found);
                break;
            case ForStatement forStmt:
                CollectUiMountOrRenderInStatement(forStmt.Body, ref found);
                break;
            case ForInStatement forIn:
                CollectUiMountOrRenderInStatement(forIn.Body, ref found);
                break;
        }
    }

    private static void CollectUiMountOrRenderInExpression(Expression expression, ref Expression? found)
    {
        if (found != null)
            return;

        if (TryGetUiMemberCall(expression, out var member) &&
            (string.Equals(member, "mount", StringComparison.Ordinal) ||
             string.Equals(member, "render", StringComparison.Ordinal)))
        {
            found = expression;
            return;
        }

        switch (expression)
        {
            case FunctionCallExpression call:
                CollectUiMountOrRenderInExpression(call.Callee, ref found);
                foreach (var arg in call.Arguments)
                    CollectUiMountOrRenderInExpression(arg, ref found);
                break;
            case MemberAccessExpression memberAccess:
                CollectUiMountOrRenderInExpression(memberAccess.Object, ref found);
                break;
            case BinaryExpression binary:
                CollectUiMountOrRenderInExpression(binary.Left, ref found);
                CollectUiMountOrRenderInExpression(binary.Right, ref found);
                break;
            case UnaryExpression unary:
                CollectUiMountOrRenderInExpression(unary.Right, ref found);
                break;
            case TernaryExpression ternary:
                CollectUiMountOrRenderInExpression(ternary.Condition, ref found);
                CollectUiMountOrRenderInExpression(ternary.ThenBranch, ref found);
                CollectUiMountOrRenderInExpression(ternary.ElseBranch, ref found);
                break;
            case ArrayLiteralExpression arrayLit:
                foreach (var el in arrayLit.Elements)
                    CollectUiMountOrRenderInExpression(el, ref found);
                break;
            case ObjectLiteralExpression objectLit:
                foreach (var (_, value) in objectLit.Properties)
                    CollectUiMountOrRenderInExpression(value, ref found);
                break;
            case DictionaryLiteralExpression dictLit:
                foreach (var (_, value) in dictLit.Entries)
                    CollectUiMountOrRenderInExpression(value, ref found);
                break;
            case AwaitExpression awaitExpr:
                CollectUiMountOrRenderInExpression(awaitExpr.Expression, ref found);
                break;
        }
    }

    private static void AnalyzeStatementList(IList<Statement> statements, List<Diagnostic> diagnostics)
    {
        var sites = new List<UiCallSite>();
        foreach (var stmt in statements)
            CollectUiCallSites(stmt, sites, recurseIntoNestedFunctions: false);

        var pendingDispatch = false;
        foreach (var site in sites)
        {
            switch (site.Kind)
            {
                case UiCallKind.DispatchEvent:
                    pendingDispatch = true;
                    break;
                case UiCallKind.PullEvent:
                    pendingDispatch = false;
                    break;
                case UiCallKind.MountOrRender when pendingDispatch:
                    diagnostics.Add(new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Message =
                            "UI1001: ui.dispatchEvent without ui.pullEvent before ui.render/ui.mount — " +
                            "the event stays queued and the next tree looks stuck. " +
                            "Order: pullEvent → update state → rebuild → render.",
                        Line = site.Line,
                        Column = site.Column,
                        Length = site.Length,
                        Source = "UI1001",
                        RelatedExamplePath = "Examples/Web/ui_event_loop.malda",
                        RelatedExampleTitle = "UI event loop",
                        RelatedDocumentationPath = "docs/ui-framework.md",
                        RelatedDocumentationTitle = "UI event loop contract"
                    });
                    break;
            }
        }
    }

    private static void CollectUiCallSites(Statement statement, List<UiCallSite> sites, bool recurseIntoNestedFunctions)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    CollectUiCallSites(inner, sites, recurseIntoNestedFunctions);
                break;

            case FunctionDeclaration func when recurseIntoNestedFunctions:
                foreach (var inner in func.Body.Statements)
                    CollectUiCallSites(inner, sites, recurseIntoNestedFunctions);
                break;

            case FunctionDeclaration:
                // Nested function bodies are analyzed separately at top level.
                break;

            case ExpressionStatement exprStmt:
                CollectUiCallSitesInExpression(exprStmt.Expression, sites);
                break;

            case PrintStatement print:
                CollectUiCallSitesInExpression(print.Expression, sites);
                break;

            case VarDeclStatement varDecl when varDecl.Initializer != null:
                CollectUiCallSitesInExpression(varDecl.Initializer, sites);
                break;

            case AssignmentStatement assign:
                CollectUiCallSitesInExpression(assign.Value, sites);
                break;

            case ReturnStatement ret when ret.Value != null:
                CollectUiCallSitesInExpression(ret.Value, sites);
                break;

            case IfStatement ifStmt:
                CollectUiCallSitesInExpression(ifStmt.Condition, sites);
                CollectUiCallSites(ifStmt.ThenBranch, sites, recurseIntoNestedFunctions);
                if (ifStmt.ElseBranch != null)
                    CollectUiCallSites(ifStmt.ElseBranch, sites, recurseIntoNestedFunctions);
                break;

            case WhileStatement whileStmt:
                CollectUiCallSitesInExpression(whileStmt.Condition, sites);
                CollectUiCallSites(whileStmt.Body, sites, recurseIntoNestedFunctions);
                break;

            case ForStatement forStmt:
                if (forStmt.Initializer != null)
                    CollectUiCallSites(forStmt.Initializer, sites, recurseIntoNestedFunctions);
                if (forStmt.Condition != null)
                    CollectUiCallSitesInExpression(forStmt.Condition, sites);
                if (forStmt.Increment != null)
                    CollectUiCallSitesInExpression(forStmt.Increment, sites);
                CollectUiCallSites(forStmt.Body, sites, recurseIntoNestedFunctions);
                break;

            case ForInStatement forIn:
                CollectUiCallSitesInExpression(forIn.Collection, sites);
                CollectUiCallSites(forIn.Body, sites, recurseIntoNestedFunctions);
                break;

            case TryStatement tryStmt:
                CollectUiCallSites(tryStmt.TryBlock, sites, recurseIntoNestedFunctions);
                foreach (var clause in tryStmt.CatchClauses)
                    CollectUiCallSites(clause.Body, sites, recurseIntoNestedFunctions);
                if (tryStmt.FinallyBlock != null)
                    CollectUiCallSites(tryStmt.FinallyBlock, sites, recurseIntoNestedFunctions);
                break;
        }
    }

    private static void CollectUiCallSitesInExpression(Expression expression, List<UiCallSite> sites)
    {
        if (expression is FunctionCallExpression call &&
            TryGetUiMemberCall(call, out var member))
        {
            var kind = member switch
            {
                "dispatchEvent" => UiCallKind.DispatchEvent,
                "pullEvent" => UiCallKind.PullEvent,
                "mount" or "render" => UiCallKind.MountOrRender,
                _ => (UiCallKind?)null
            };
            if (kind != null)
            {
                sites.Add(new UiCallSite(
                    kind.Value,
                    Math.Max(0, call.Line - 1),
                    Math.Max(0, call.Column - 1),
                    Math.Max(1, member.Length)));
            }
        }

        switch (expression)
        {
            case FunctionCallExpression callExpr:
                CollectUiCallSitesInExpression(callExpr.Callee, sites);
                foreach (var arg in callExpr.Arguments)
                    CollectUiCallSitesInExpression(arg, sites);
                break;
            case MemberAccessExpression memberAccess:
                CollectUiCallSitesInExpression(memberAccess.Object, sites);
                break;
            case BinaryExpression binary:
                CollectUiCallSitesInExpression(binary.Left, sites);
                CollectUiCallSitesInExpression(binary.Right, sites);
                break;
            case UnaryExpression unary:
                CollectUiCallSitesInExpression(unary.Right, sites);
                break;
            case TernaryExpression ternary:
                CollectUiCallSitesInExpression(ternary.Condition, sites);
                CollectUiCallSitesInExpression(ternary.ThenBranch, sites);
                CollectUiCallSitesInExpression(ternary.ElseBranch, sites);
                break;
            case ArrayLiteralExpression arrayLit:
                foreach (var el in arrayLit.Elements)
                    CollectUiCallSitesInExpression(el, sites);
                break;
            case ObjectLiteralExpression objectLit:
                foreach (var (_, value) in objectLit.Properties)
                    CollectUiCallSitesInExpression(value, sites);
                break;
            case DictionaryLiteralExpression dictLit:
                foreach (var (_, value) in dictLit.Entries)
                    CollectUiCallSitesInExpression(value, sites);
                break;
            case AwaitExpression awaitExpr:
                CollectUiCallSitesInExpression(awaitExpr.Expression, sites);
                break;
            case LambdaExpression lambda:
                if (lambda.ExpressionBody != null)
                    CollectUiCallSitesInExpression(lambda.ExpressionBody, sites);
                if (lambda.BlockBody != null)
                    CollectUiCallSites(lambda.BlockBody, sites, recurseIntoNestedFunctions: true);
                break;
        }
    }

    private static bool TryGetUiMemberCall(Expression expression, out string member)
    {
        member = "";
        if (expression is not FunctionCallExpression call)
            return false;
        if (call.Callee is not MemberAccessExpression access)
            return false;
        if (access.Object is not IdentifierExpression id ||
            !string.Equals(id.Name, "ui", StringComparison.Ordinal))
            return false;
        member = access.Member;
        return true;
    }
}
