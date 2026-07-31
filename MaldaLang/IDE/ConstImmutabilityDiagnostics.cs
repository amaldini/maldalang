// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.IDE.Models;

/// <summary>
/// Phase 7.3: under <c>--strict-types</c>, reject assignments to <c>const</c> bindings.
/// </summary>
public static class ConstImmutabilityDiagnostics
{
    public static void Validate(
        IEnumerable<Statement> statements,
        StrictTypesOptions options,
        List<Diagnostic> diagnostics)
    {
        if (!options.StrictTypes)
            return;

        var scope = new ConstScope();
        foreach (var stmt in statements)
            VisitStatement(stmt, scope, diagnostics);
    }

    private sealed class ConstScope
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public ConstScope Parent { get; init; } = null!;

        public void Declare(string name) => _names.Add(name);

        public bool Contains(string name)
        {
            if (_names.Contains(name))
                return true;
            return Parent?.Contains(name) ?? false;
        }

        public ConstScope Child() => new() { Parent = this };
    }

    private static void VisitStatement(Statement stmt, ConstScope scope, List<Diagnostic> diagnostics)
    {
        switch (stmt)
        {
            case VarDeclStatement varDecl when varDecl.IsConst:
                scope.Declare(varDecl.Name);
                break;
            case AssignmentStatement assignment:
                ValidateAssignmentTarget(assignment.Target, assignment.Line, assignment.Column, scope, diagnostics);
                break;
            case BlockStatement block:
                var blockScope = scope.Child();
                foreach (var inner in block.Statements)
                    VisitStatement(inner, blockScope, diagnostics);
                return;
            case IfStatement ifStmt:
                VisitStatement(ifStmt.ThenBranch, scope.Child(), diagnostics);
                if (ifStmt.ElseBranch != null)
                    VisitStatement(ifStmt.ElseBranch, scope.Child(), diagnostics);
                return;
            case WhileStatement whileStmt:
                VisitStatement(whileStmt.Body, scope.Child(), diagnostics);
                return;
            case ForStatement forStmt:
                VisitStatement(forStmt.Body, scope.Child(), diagnostics);
                return;
            case ForInStatement forInStmt:
                VisitStatement(forInStmt.Body, scope.Child(), diagnostics);
                return;
            case TryStatement tryStmt:
                var tryScope = scope.Child();
                foreach (var inner in tryStmt.TryBlock.Statements)
                    VisitStatement(inner, tryScope, diagnostics);
                foreach (var catchClause in tryStmt.CatchClauses)
                    VisitStatement(catchClause.Body, scope.Child(), diagnostics);
                if (tryStmt.FinallyBlock != null)
                {
                    foreach (var inner in tryStmt.FinallyBlock.Statements)
                        VisitStatement(inner, scope.Child(), diagnostics);
                }
                return;
            case FunctionDeclaration funcDecl:
                var fnScope = scope.Child();
                foreach (var inner in funcDecl.Body.Statements)
                    VisitStatement(inner, fnScope, diagnostics);
                return;
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration method)
                    {
                        var methodScope = scope.Child();
                        foreach (var inner in method.Body.Statements)
                            VisitStatement(inner, methodScope, diagnostics);
                    }
                }
                return;
        }
    }

    private static void ValidateAssignmentTarget(
        Expression target,
        int line,
        int column,
        ConstScope scope,
        List<Diagnostic> diagnostics)
    {
        switch (target)
        {
            case IdentifierExpression id when scope.Contains(id.Name):
                diagnostics.Add(new Diagnostic
                {
                    Line = line,
                    Column = column,
                    Severity = DiagnosticSeverity.Error,
                    Source = "malda-const",
                    Message = $"Cannot assign to const '{id.Name}'."
                });
                break;
            case PostfixExpression postfix:
                ValidateAssignmentTarget(postfix.Left, postfix.Line, postfix.Column, scope, diagnostics);
                break;
            case UnaryExpression unary when unary.Operator is TokenType.Increment or TokenType.Decrement:
                ValidateAssignmentTarget(unary.Right, unary.Line, unary.Column, scope, diagnostics);
                break;
        }
    }
}
