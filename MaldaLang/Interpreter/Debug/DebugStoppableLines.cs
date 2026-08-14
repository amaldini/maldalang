// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter.Debug;

using System.Collections.Generic;
using System.IO;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Collects 1-based lines of stoppable statements for DAP breakpoint mapping.
/// </summary>
public static class DebugStoppableLines
{
    public static SortedSet<int> Collect(IEnumerable<Statement> statements, string? file = null)
    {
        var lines = new SortedSet<int>();
        foreach (var stmt in statements)
            Walk(stmt, file, lines);
        return lines;
    }

    /// <summary>
    /// Smallest stoppable line <c>&gt;= requested</c>, or null if none.
    /// </summary>
    public static int? MapToStoppable(IEnumerable<int> stoppableLines, int requested)
    {
        foreach (var line in stoppableLines)
        {
            if (line >= requested)
                return line;
        }

        return null;
    }

    private static void Walk(Statement? stmt, string? fileFilter, SortedSet<int> lines)
    {
        if (stmt == null)
            return;

        if (DebugStatementClassifier.IsStoppable(stmt) && stmt.Line > 0 && FileMatches(stmt, fileFilter))
            lines.Add(stmt.Line);

        switch (stmt)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                    Walk(child, fileFilter, lines);
                break;
            case FunctionDeclaration fn:
                Walk(fn.Body, fileFilter, lines);
                break;
            case PropertyDeclaration prop:
                Walk(prop.Body, fileFilter, lines);
                break;
            case WorkflowDeclaration workflow:
                Walk(workflow.Body, fileFilter, lines);
                break;
            case ClassDeclaration cls:
                WalkClassMembers(cls.Members, fileFilter, lines);
                break;
            case ActorDeclaration actor:
                WalkClassMembers(actor.Members, fileFilter, lines);
                break;
            case PromptDeclaration prompt when prompt.StatementBody != null:
                foreach (var child in prompt.StatementBody)
                    Walk(child, fileFilter, lines);
                break;
            case IfStatement ifStmt:
                Walk(ifStmt.ThenBranch, fileFilter, lines);
                Walk(ifStmt.ElseBranch, fileFilter, lines);
                break;
            case WhileStatement whileStmt:
                Walk(whileStmt.Body, fileFilter, lines);
                break;
            case ForStatement forStmt:
                Walk(forStmt.Initializer, fileFilter, lines);
                Walk(forStmt.Body, fileFilter, lines);
                break;
            case ForInStatement forIn:
                Walk(forIn.Body, fileFilter, lines);
                break;
            case TryStatement tryStmt:
                Walk(tryStmt.TryBlock, fileFilter, lines);
                foreach (var catchClause in tryStmt.CatchClauses)
                    Walk(catchClause.Body, fileFilter, lines);
                Walk(tryStmt.FinallyBlock, fileFilter, lines);
                break;
            case UsingResourceStatement usingRes:
                Walk(usingRes.Body, fileFilter, lines);
                break;
            case DeferStatement defer:
                Walk(defer.Body, fileFilter, lines);
                break;
            case ExpressionStatement exprStmt:
                WalkExpression(exprStmt.Expression, fileFilter, lines);
                break;
        }
    }

    private static void WalkClassMembers(List<ClassMember> members, string? fileFilter, SortedSet<int> lines)
    {
        foreach (var member in members)
        {
            if (member.Value is FunctionDeclaration method)
                Walk(method, fileFilter, lines);
            else if (member.Value is Statement memberStmt)
                Walk(memberStmt, fileFilter, lines);
        }
    }

    private static void WalkExpression(Expression? expression, string? fileFilter, SortedSet<int> lines)
    {
        if (expression is LambdaExpression lambda && lambda.BlockBody != null)
            Walk(lambda.BlockBody, fileFilter, lines);
    }

    private static bool FileMatches(Statement stmt, string? fileFilter)
    {
        if (string.IsNullOrEmpty(fileFilter) || string.IsNullOrEmpty(stmt.SourceFile))
            return true;

        return PathEquals(DebugSession.NormalizeFile(stmt.SourceFile), DebugSession.NormalizeFile(fileFilter));
    }

    private static bool PathEquals(string a, string b)
    {
        return string.Equals(a, b, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }
}
