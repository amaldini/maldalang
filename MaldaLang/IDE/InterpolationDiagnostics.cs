// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// DT4: plain strings do not interpolate. Warn when a non-prompt string looks like <c>{ident}</c>.
/// Prompt bodies keep <c>{name}</c> template syntax and are skipped.
/// </summary>
public static class InterpolationDiagnostics
{
    private static readonly Regex BraceIdent = new(
        @"\{[A-Za-z_][A-Za-z0-9_]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics)
    {
        foreach (var statement in statements)
            VisitStatement(statement, diagnostics);
    }

    private static void VisitStatement(Statement statement, List<Diagnostic> diagnostics)
    {
        switch (statement)
        {
            case PromptDeclaration:
            case PromptBodyStatement:
            case ImportStatement:
            case UsingStatement:
                return;
            case FunctionDeclaration func:
                VisitBlock(func.Body, diagnostics);
                break;
            case ClassDeclaration cls:
                foreach (var member in cls.Members)
                {
                    if (member.Type == MemberType.Method && member.Value is FunctionDeclaration method)
                        VisitBlock(method.Body, diagnostics);
                }
                break;
            case BlockStatement block:
                VisitBlock(block, diagnostics);
                break;
            case IfStatement ifStmt:
                VisitExpression(ifStmt.Condition, diagnostics);
                VisitStatement(ifStmt.ThenBranch, diagnostics);
                if (ifStmt.ElseBranch != null)
                    VisitStatement(ifStmt.ElseBranch, diagnostics);
                break;
            case WhileStatement whileStmt:
                VisitExpression(whileStmt.Condition, diagnostics);
                VisitStatement(whileStmt.Body, diagnostics);
                break;
            case ForStatement forStmt:
                if (forStmt.Initializer != null)
                    VisitStatement(forStmt.Initializer, diagnostics);
                if (forStmt.Condition != null)
                    VisitExpression(forStmt.Condition, diagnostics);
                if (forStmt.Increment != null)
                    VisitExpression(forStmt.Increment, diagnostics);
                VisitStatement(forStmt.Body, diagnostics);
                break;
            case ForInStatement forIn:
                VisitExpression(forIn.Collection, diagnostics);
                VisitStatement(forIn.Body, diagnostics);
                break;
            case TryStatement tryStmt:
                VisitBlock(tryStmt.TryBlock, diagnostics);
                foreach (var clause in tryStmt.CatchClauses)
                    VisitBlock(clause.Body, diagnostics);
                if (tryStmt.FinallyBlock != null)
                    VisitBlock(tryStmt.FinallyBlock, diagnostics);
                break;
            case ExpressionStatement exprStmt:
                VisitExpression(exprStmt.Expression, diagnostics);
                break;
            case ReturnStatement ret when ret.Value != null:
                VisitExpression(ret.Value, diagnostics);
                break;
            case VarDeclStatement varDecl when varDecl.Initializer != null:
                VisitExpression(varDecl.Initializer, diagnostics);
                break;
            case AssignmentStatement assign:
                VisitExpression(assign.Target, diagnostics);
                VisitExpression(assign.Value, diagnostics);
                break;
            case PrintStatement print:
                VisitExpression(print.Expression, diagnostics);
                break;
            case ThrowStatement thr:
                VisitExpression(thr.Exception, diagnostics);
                break;
        }
    }

    private static void VisitBlock(BlockStatement? block, List<Diagnostic> diagnostics)
    {
        if (block == null)
            return;
        foreach (var statement in block.Statements)
            VisitStatement(statement, diagnostics);
    }

    private static void VisitExpression(Expression? expression, List<Diagnostic> diagnostics)
    {
        if (expression == null)
            return;

        switch (expression)
        {
            case LiteralExpression literal when literal.Value is string text && BraceIdent.IsMatch(text):
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message =
                        "malda-interp: a plain string does not interpolate {name}. " +
                        "Use $\"n is {n}\" or string concatenation. Prompt bodies still use {name} templates.",
                    Line = literal.Line,
                    Column = literal.Column,
                    Length = Math.Max(1, text.Length),
                    Source = "malda-interp"
                });
                break;
            case FunctionCallExpression call:
                VisitExpression(call.Callee, diagnostics);
                foreach (var arg in call.Arguments)
                    VisitExpression(arg, diagnostics);
                break;
            case MemberAccessExpression member:
                VisitExpression(member.Object, diagnostics);
                break;
            case BinaryExpression binary:
                VisitExpression(binary.Left, diagnostics);
                VisitExpression(binary.Right, diagnostics);
                break;
            case UnaryExpression unary:
                VisitExpression(unary.Right, diagnostics);
                break;
            case TernaryExpression ternary:
                VisitExpression(ternary.Condition, diagnostics);
                VisitExpression(ternary.ThenBranch, diagnostics);
                VisitExpression(ternary.ElseBranch, diagnostics);
                break;
            case AwaitExpression awaitExpr:
                VisitExpression(awaitExpr.Expression, diagnostics);
                break;
            case ArrayAccessExpression arrayAccess:
                VisitExpression(arrayAccess.Array, diagnostics);
                VisitExpression(arrayAccess.Index, diagnostics);
                break;
            case ArrayLiteralExpression arrayLit:
                foreach (var element in arrayLit.Elements)
                    VisitExpression(element, diagnostics);
                break;
            case ObjectLiteralExpression objectLit:
                foreach (var (key, value) in objectLit.Properties)
                {
                    VisitExpression(key, diagnostics);
                    VisitExpression(value, diagnostics);
                }
                break;
            case DictionaryLiteralExpression dictLit:
                foreach (var (key, value) in dictLit.Entries)
                {
                    VisitExpression(key, diagnostics);
                    VisitExpression(value, diagnostics);
                }
                break;
            case LambdaExpression lambda:
                if (lambda.ExpressionBody != null)
                    VisitExpression(lambda.ExpressionBody, diagnostics);
                if (lambda.BlockBody != null)
                    VisitBlock(lambda.BlockBody, diagnostics);
                break;
            case MatchExpression match:
                VisitExpression(match.Value, diagnostics);
                foreach (var arm in match.Cases)
                {
                    if (arm.Guard != null)
                        VisitExpression(arm.Guard, diagnostics);
                    VisitStatement(arm.Body, diagnostics);
                }
                if (match.DefaultCase != null)
                    VisitStatement(match.DefaultCase, diagnostics);
                break;
            case InterpolatedStringExpression interpolated:
                foreach (var segment in interpolated.Segments)
                {
                    if (segment.Expression != null)
                        VisitExpression(segment.Expression, diagnostics);
                }
                break;
            case NewExpression newExpr:
                foreach (var arg in newExpr.Arguments)
                    VisitExpression(arg, diagnostics);
                break;
            case AsyncExpression asyncExpr:
                VisitExpression(asyncExpr.Expression, diagnostics);
                break;
            case SpawnExpression spawn:
                foreach (var arg in spawn.Arguments)
                    VisitExpression(arg, diagnostics);
                break;
        }
    }
}
