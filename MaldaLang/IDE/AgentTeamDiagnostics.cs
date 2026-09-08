// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Static team/plan checks for literal <c>agents.team</c> / <c>executePlan</c> graphs.
/// </summary>
public static class AgentTeamDiagnostics
{
    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics)
    {
        foreach (var stmt in statements)
            Walk(stmt, diagnostics);
    }

    private static void Walk(Statement stmt, List<Diagnostic> diagnostics)
    {
        switch (stmt)
        {
            case ExpressionStatement expr when expr.Expression is FunctionCallExpression call:
                CheckCall(call, diagnostics);
                break;
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    Walk(inner, diagnostics);
                break;
            case FunctionDeclaration fn:
                Walk(fn.Body, diagnostics);
                break;
        }
    }

    private static void CheckCall(FunctionCallExpression call, List<Diagnostic> diagnostics)
    {
        if (call.Callee is not MemberAccessExpression member)
            return;
        if (member.Object is not IdentifierExpression id)
            return;
        if (!string.Equals(id.Name, "agents", StringComparison.Ordinal)
            && !string.Equals(member.Member, "executePlan", StringComparison.Ordinal)
            && !string.Equals(member.Member, "team", StringComparison.Ordinal))
            return;

        if (string.Equals(member.Member, "team", StringComparison.Ordinal) && call.Arguments.Count >= 2
            && call.Arguments[1] is GraphLiteralExpression graph
            && graph.NodesExpression == null)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Message = "agents.team graph has no statically visible nodes.",
                Line = Math.Max(0, call.Line - 1),
                Column = Math.Max(0, call.Column - 1),
                Source = "malda-agents"
            });
        }
    }
}
