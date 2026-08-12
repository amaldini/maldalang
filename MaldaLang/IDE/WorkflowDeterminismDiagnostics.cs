// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Static WF1001/WF1002 checks for direct deny-listed built-in calls in a workflow body
/// outside <c>step</c> / <c>onReject</c>. Same fixed name list as runtime; no call-graph analysis.
/// </summary>
public static class WorkflowDeterminismDiagnostics
{
    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics)
    {
        foreach (var stmt in statements)
        {
            if (stmt is WorkflowDeclaration workflow)
                VisitStatement(workflow.Body, checkCalls: true, diagnostics);
        }
    }

    private static void VisitStatement(Statement statement, bool checkCalls, List<Diagnostic> diagnostics)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    VisitStatement(inner, checkCalls, diagnostics);
                break;

            case WorkflowStepStatement:
                // Step call + compensate run under step rules — not checked.
                break;

            case WorkflowApprovalStatement approval:
                VisitExpression(approval.ApprovalNameExpr, checkCalls, diagnostics);
                VisitExpression(approval.PayloadExpr, checkCalls, diagnostics);
                // onReject runs under step rules.
                break;

            case WorkflowAwaitSignalStatement wait:
                VisitExpression(wait.SignalNameExpr, checkCalls, diagnostics);
                VisitExpression(wait.PayloadExpr, checkCalls, diagnostics);
                break;

            case IfStatement ifStmt:
                VisitExpression(ifStmt.Condition, checkCalls, diagnostics);
                VisitStatement(ifStmt.ThenBranch, checkCalls, diagnostics);
                if (ifStmt.ElseBranch != null)
                    VisitStatement(ifStmt.ElseBranch, checkCalls, diagnostics);
                break;

            case WhileStatement whileStmt:
                VisitExpression(whileStmt.Condition, checkCalls, diagnostics);
                VisitStatement(whileStmt.Body, checkCalls, diagnostics);
                break;

            case ForStatement forStmt:
                if (forStmt.Initializer != null)
                    VisitStatement(forStmt.Initializer, checkCalls, diagnostics);
                if (forStmt.Condition != null)
                    VisitExpression(forStmt.Condition, checkCalls, diagnostics);
                if (forStmt.Increment != null)
                    VisitExpression(forStmt.Increment, checkCalls, diagnostics);
                VisitStatement(forStmt.Body, checkCalls, diagnostics);
                break;

            case ForInStatement forIn:
                VisitExpression(forIn.Collection, checkCalls, diagnostics);
                VisitStatement(forIn.Body, checkCalls, diagnostics);
                break;

            case TryStatement tryStmt:
                VisitStatement(tryStmt.TryBlock, checkCalls, diagnostics);
                foreach (var clause in tryStmt.CatchClauses)
                    VisitStatement(clause.Body, checkCalls, diagnostics);
                if (tryStmt.FinallyBlock != null)
                    VisitStatement(tryStmt.FinallyBlock, checkCalls, diagnostics);
                break;

            case ExpressionStatement exprStmt:
                VisitExpression(exprStmt.Expression, checkCalls, diagnostics);
                break;

            case ReturnStatement ret when ret.Value != null:
                VisitExpression(ret.Value, checkCalls, diagnostics);
                break;

            case VarDeclStatement varDecl when varDecl.Initializer != null:
                VisitExpression(varDecl.Initializer, checkCalls, diagnostics);
                break;

            case AssignmentStatement assign:
                VisitExpression(assign.Target, checkCalls, diagnostics);
                VisitExpression(assign.Value, checkCalls, diagnostics);
                break;

            case ThrowStatement thr:
                VisitExpression(thr.Exception, checkCalls, diagnostics);
                break;

            case PrintStatement print:
                VisitExpression(print.Expression, checkCalls, diagnostics);
                break;

            case FunctionDeclaration:
                // Nested function bodies are not analyzed (no interprocedural / call-graph).
                break;
        }
    }

    private static void VisitExpression(Expression expression, bool checkCalls, List<Diagnostic> diagnostics)
    {
        switch (expression)
        {
            case FunctionCallExpression call:
                if (checkCalls &&
                    call.Callee is IdentifierExpression id)
                {
                    var behavior = BuiltInRegistry.GetWorkflowBehavior(id.Name);
                    if (behavior == WorkflowBuiltInBehavior.NonDeterministic)
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Message = $"WF1001: Non-deterministic built-in '{id.Name}' in deterministic workflow section. Move it inside a step.",
                            Line = id.Line - 1,
                            Column = id.Column - 1,
                            Length = Math.Max(1, id.Name.Length),
                            Source = "WF1001"
                        });
                    }
                    else if (behavior == WorkflowBuiltInBehavior.SideEffecting)
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Message = $"WF1002: Side-effecting operation '{id.Name}' outside step boundary. Move it inside a step.",
                            Line = id.Line - 1,
                            Column = id.Column - 1,
                            Length = Math.Max(1, id.Name.Length),
                            Source = "WF1002"
                        });
                    }
                }

                VisitExpression(call.Callee, checkCalls, diagnostics);
                foreach (var arg in call.Arguments)
                    VisitExpression(arg, checkCalls, diagnostics);
                break;

            case MemberAccessExpression member:
                VisitExpression(member.Object, checkCalls, diagnostics);
                break;

            case BinaryExpression binary:
                VisitExpression(binary.Left, checkCalls, diagnostics);
                VisitExpression(binary.Right, checkCalls, diagnostics);
                break;

            case UnaryExpression unary:
                VisitExpression(unary.Right, checkCalls, diagnostics);
                break;

            case TernaryExpression ternary:
                VisitExpression(ternary.Condition, checkCalls, diagnostics);
                VisitExpression(ternary.ThenBranch, checkCalls, diagnostics);
                VisitExpression(ternary.ElseBranch, checkCalls, diagnostics);
                break;

            case AwaitExpression awaitExpr:
                VisitExpression(awaitExpr.Expression, checkCalls, diagnostics);
                break;

            case ArrayAccessExpression arrayAccess:
                VisitExpression(arrayAccess.Array, checkCalls, diagnostics);
                VisitExpression(arrayAccess.Index, checkCalls, diagnostics);
                break;

            case ArrayLiteralExpression arrayLit:
                foreach (var element in arrayLit.Elements)
                    VisitExpression(element, checkCalls, diagnostics);
                break;

            case ObjectLiteralExpression objectLit:
                foreach (var (key, value) in objectLit.Properties)
                {
                    VisitExpression(key, checkCalls, diagnostics);
                    VisitExpression(value, checkCalls, diagnostics);
                }
                break;

            case DictionaryLiteralExpression dictLit:
                foreach (var (key, value) in dictLit.Entries)
                {
                    VisitExpression(key, checkCalls, diagnostics);
                    VisitExpression(value, checkCalls, diagnostics);
                }
                break;

            case LambdaExpression lambda:
                if (lambda.ExpressionBody != null)
                    VisitExpression(lambda.ExpressionBody, checkCalls, diagnostics);
                if (lambda.BlockBody != null)
                    VisitStatement(lambda.BlockBody, checkCalls, diagnostics);
                break;

            case MatchExpression match:
                VisitExpression(match.Value, checkCalls, diagnostics);
                foreach (var arm in match.Cases)
                    VisitStatement(arm.Body, checkCalls, diagnostics);
                if (match.DefaultCase != null)
                    VisitStatement(match.DefaultCase, checkCalls, diagnostics);
                break;

            case InterpolatedStringExpression interpolated:
                foreach (var segment in interpolated.Segments)
                {
                    if (segment.Expression != null)
                        VisitExpression(segment.Expression, checkCalls, diagnostics);
                }
                break;

            case NewExpression newExpr:
                foreach (var arg in newExpr.Arguments)
                    VisitExpression(arg, checkCalls, diagnostics);
                break;

            case AsyncExpression asyncExpr:
                VisitExpression(asyncExpr.Expression, checkCalls, diagnostics);
                break;

            case SpawnExpression spawn:
                foreach (var arg in spawn.Arguments)
                    VisitExpression(arg, checkCalls, diagnostics);
                break;
        }
    }
}
