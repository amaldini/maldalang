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
/// Static UI footgun checks: dispatch without pull before render (UI1001),
/// mixed @PAGE / ui.* surface (UI1002 Info), and poison get-or-create defaults
/// on <c>ui.state</c> (UI1003). Heuristic name walk; no call-graph / dataflow.
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
        CheckPoisonStateDefaults(list, diagnostics);

        AnalyzeStatementList(list, diagnostics);
        foreach (var stmt in list)
        {
            if (stmt is FunctionDeclaration func)
                AnalyzeStatementList(func.Body.Statements, diagnostics);
        }
    }

    /// <summary>
    /// UI1003: <c>ui.state(id, key, null)</c> / <c>ui.state(id, key, {})</c> persist the
    /// default on miss — after TTL/LRU eviction that poisons the store. Literals only.
    /// </summary>
    private static void CheckPoisonStateDefaults(IList<Statement> statements, List<Diagnostic> diagnostics)
    {
        foreach (var stmt in statements)
            CollectPoisonStateDefaults(stmt, diagnostics);
    }

    private static void CollectPoisonStateDefaults(Statement statement, List<Diagnostic> diagnostics)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    CollectPoisonStateDefaults(inner, diagnostics);
                break;
            case FunctionDeclaration func:
                foreach (var inner in func.Body.Statements)
                    CollectPoisonStateDefaults(inner, diagnostics);
                break;
            case ExpressionStatement exprStmt:
                CollectPoisonStateDefaultsInExpression(exprStmt.Expression, diagnostics);
                break;
            case PrintStatement print:
                CollectPoisonStateDefaultsInExpression(print.Expression, diagnostics);
                break;
            case VarDeclStatement varDecl when varDecl.Initializer != null:
                CollectPoisonStateDefaultsInExpression(varDecl.Initializer, diagnostics);
                break;
            case AssignmentStatement assign:
                CollectPoisonStateDefaultsInExpression(assign.Value, diagnostics);
                break;
            case ReturnStatement ret when ret.Value != null:
                CollectPoisonStateDefaultsInExpression(ret.Value, diagnostics);
                break;
            case IfStatement ifStmt:
                CollectPoisonStateDefaultsInExpression(ifStmt.Condition, diagnostics);
                CollectPoisonStateDefaults(ifStmt.ThenBranch, diagnostics);
                if (ifStmt.ElseBranch != null)
                    CollectPoisonStateDefaults(ifStmt.ElseBranch, diagnostics);
                break;
            case WhileStatement whileStmt:
                CollectPoisonStateDefaultsInExpression(whileStmt.Condition, diagnostics);
                CollectPoisonStateDefaults(whileStmt.Body, diagnostics);
                break;
            case ForStatement forStmt:
                if (forStmt.Initializer != null)
                    CollectPoisonStateDefaults(forStmt.Initializer, diagnostics);
                if (forStmt.Condition != null)
                    CollectPoisonStateDefaultsInExpression(forStmt.Condition, diagnostics);
                if (forStmt.Increment != null)
                    CollectPoisonStateDefaultsInExpression(forStmt.Increment, diagnostics);
                CollectPoisonStateDefaults(forStmt.Body, diagnostics);
                break;
            case ForInStatement forIn:
                CollectPoisonStateDefaultsInExpression(forIn.Collection, diagnostics);
                CollectPoisonStateDefaults(forIn.Body, diagnostics);
                break;
            case TryStatement tryStmt:
                CollectPoisonStateDefaults(tryStmt.TryBlock, diagnostics);
                foreach (var clause in tryStmt.CatchClauses)
                    CollectPoisonStateDefaults(clause.Body, diagnostics);
                if (tryStmt.FinallyBlock != null)
                    CollectPoisonStateDefaults(tryStmt.FinallyBlock, diagnostics);
                break;
        }
    }

    private static void CollectPoisonStateDefaultsInExpression(Expression expression, List<Diagnostic> diagnostics)
    {
        if (expression is FunctionCallExpression call &&
            TryGetUiStateGetOrCreateCall(call, out var displayName) &&
            call.Arguments.Count >= 3)
        {
            var defaultArg = call.Arguments[2];
            if (IsPoisonDefaultLiteral(defaultArg))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message =
                        "UI1003: " + displayName + " get-or-create with null/{} default persists that value on miss — " +
                        "after TTL/LRU eviction the store is poisoned. Prefer ui.getState (peek) for optional reads, " +
                        "or a non-null initializer ([] / 0 / \"\"); pin process-lifetime data with ui.pinState.",
                    Line = Math.Max(0, call.Line - 1),
                    Column = Math.Max(0, call.Column - 1),
                    Length = Math.Max(1, displayName.Length),
                    Source = "UI1003",
                    RelatedExamplePath = "Examples/Web/ui_state_lifecycle.malda",
                    RelatedExampleTitle = "UI state lifecycle",
                    RelatedDocumentationPath = "docs/ui-framework.md",
                    RelatedDocumentationTitle = "UI state model (pin / TTL)"
                });
            }
        }

        switch (expression)
        {
            case FunctionCallExpression callExpr:
                CollectPoisonStateDefaultsInExpression(callExpr.Callee, diagnostics);
                foreach (var arg in callExpr.Arguments)
                    CollectPoisonStateDefaultsInExpression(arg, diagnostics);
                break;
            case MemberAccessExpression memberAccess:
                CollectPoisonStateDefaultsInExpression(memberAccess.Object, diagnostics);
                break;
            case BinaryExpression binary:
                CollectPoisonStateDefaultsInExpression(binary.Left, diagnostics);
                CollectPoisonStateDefaultsInExpression(binary.Right, diagnostics);
                break;
            case UnaryExpression unary:
                CollectPoisonStateDefaultsInExpression(unary.Right, diagnostics);
                break;
            case TernaryExpression ternary:
                CollectPoisonStateDefaultsInExpression(ternary.Condition, diagnostics);
                CollectPoisonStateDefaultsInExpression(ternary.ThenBranch, diagnostics);
                CollectPoisonStateDefaultsInExpression(ternary.ElseBranch, diagnostics);
                break;
            case ArrayLiteralExpression arrayLit:
                foreach (var el in arrayLit.Elements)
                    CollectPoisonStateDefaultsInExpression(el, diagnostics);
                break;
            case ObjectLiteralExpression objectLit:
                foreach (var (_, value) in objectLit.Properties)
                    CollectPoisonStateDefaultsInExpression(value, diagnostics);
                break;
            case DictionaryLiteralExpression dictLit:
                foreach (var (_, value) in dictLit.Entries)
                    CollectPoisonStateDefaultsInExpression(value, diagnostics);
                break;
            case AwaitExpression awaitExpr:
                CollectPoisonStateDefaultsInExpression(awaitExpr.Expression, diagnostics);
                break;
            case LambdaExpression lambda:
                if (lambda.ExpressionBody != null)
                    CollectPoisonStateDefaultsInExpression(lambda.ExpressionBody, diagnostics);
                if (lambda.BlockBody != null)
                    CollectPoisonStateDefaults(lambda.BlockBody, diagnostics);
                break;
        }
    }

    private static bool TryGetUiStateGetOrCreateCall(FunctionCallExpression call, out string displayName)
    {
        displayName = "";
        if (TryGetUiMemberCall(call, out var member) &&
            string.Equals(member, "state", StringComparison.Ordinal))
        {
            displayName = "ui.state";
            return true;
        }

        if (call.Callee is IdentifierExpression id &&
            string.Equals(id.Name, "uiState", StringComparison.Ordinal))
        {
            displayName = "uiState";
            return true;
        }

        return false;
    }

    private static bool IsPoisonDefaultLiteral(Expression expression)
    {
        if (expression is LiteralExpression lit && lit.Value is null)
            return true;

        if (expression is ObjectLiteralExpression obj && obj.Properties.Count == 0)
            return true;

        if (expression is DictionaryLiteralExpression dict && dict.Entries.Count == 0)
            return true;

        return false;
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
                "(@PAGE HTML vs ui.* trees). Intentional hybrids need clear boundaries — see ReferenceManual/23-web-ui-hub.html.",
            Line = Math.Max(0, pageDecorator.Line - 1),
            Column = Math.Max(0, pageDecorator.Column - 1),
            Length = Math.Max(1, pageDecorator.Name.Length + 1),
            Source = "UI1002",
            RelatedDocumentationPath = "ReferenceManual/23-web-ui-hub.html",
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
