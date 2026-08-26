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
/// Phase 4.3: under <c>--strict-types</c>, require variant cases for all constructors when matching a sum type.
/// </summary>
public static class MatchExhaustivenessDiagnostics
{
    public static void Validate(
        IEnumerable<Statement> statements,
        SumTypeIndex index,
        StrictTypesOptions options,
        List<Diagnostic> diagnostics)
    {
        if (!options.StrictTypes)
            return;

        VisitStatements(statements, new VariableTypes(index), index, diagnostics);
    }

    private static void VisitStatements(
        IEnumerable<Statement> statements,
        VariableTypes types,
        SumTypeIndex index,
        List<Diagnostic> diagnostics)
    {
        foreach (var stmt in statements)
            VisitStatement(stmt, types, index, diagnostics);
    }

    private static void VisitStatement(
        Statement stmt,
        VariableTypes types,
        SumTypeIndex index,
        List<Diagnostic> diagnostics)
    {
        switch (stmt)
        {
            case TypeDeclaration:
                break;
            case VarDeclStatement varDecl:
                types.RecordDeclaration(varDecl);
                if (varDecl.Initializer != null)
                    VisitExpression(varDecl.Initializer, types, index, diagnostics);
                break;
            case AssignmentStatement assign when assign.Operator == TokenType.Assign:
                types.RecordAssignment(assign);
                VisitExpression(assign.Target, types, index, diagnostics);
                VisitExpression(assign.Value, types, index, diagnostics);
                break;
            case FunctionDeclaration funcDecl:
            {
                var scoped = types.Clone();
                VisitBlock(funcDecl.Body, scoped, index, diagnostics);
                break;
            }
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration method)
                        VisitBlock(method.Body, types.Clone(), index, diagnostics);
                }
                break;
            case BlockStatement block:
                VisitBlock(block, types.Clone(), index, diagnostics);
                break;
            case IfStatement ifStmt:
                VisitStatement(ifStmt.ThenBranch, types.Clone(), index, diagnostics);
                if (ifStmt.ElseBranch != null)
                    VisitStatement(ifStmt.ElseBranch, types.Clone(), index, diagnostics);
                break;
            case WhileStatement whileStmt:
                VisitStatement(whileStmt.Body, types.Clone(), index, diagnostics);
                break;
            case ForStatement forStmt:
                if (forStmt.Initializer != null)
                    VisitStatement(forStmt.Initializer, types, index, diagnostics);
                VisitStatement(forStmt.Body, types.Clone(), index, diagnostics);
                break;
            case ForInStatement forIn:
                VisitStatement(forIn.Body, types.Clone(), index, diagnostics);
                break;
            case TryStatement tryStmt:
                VisitBlock(tryStmt.TryBlock, types.Clone(), index, diagnostics);
                foreach (var clause in tryStmt.CatchClauses)
                    VisitBlock(clause.Body, types.Clone(), index, diagnostics);
                if (tryStmt.FinallyBlock != null)
                    VisitBlock(tryStmt.FinallyBlock, types.Clone(), index, diagnostics);
                break;
            case ReturnStatement ret when ret.Value != null:
                VisitExpression(ret.Value, types, index, diagnostics);
                break;
            case ExpressionStatement exprStmt:
                VisitExpression(exprStmt.Expression, types, index, diagnostics);
                break;
            case PrintStatement print:
                VisitExpression(print.Expression, types, index, diagnostics);
                break;
            case ThrowStatement thr:
                VisitExpression(thr.Exception, types, index, diagnostics);
                break;
        }
    }

    private static void VisitBlock(
        BlockStatement block,
        VariableTypes types,
        SumTypeIndex index,
        List<Diagnostic> diagnostics)
    {
        foreach (var stmt in block.Statements)
            VisitStatement(stmt, types, index, diagnostics);
    }

    private static void VisitExpression(
        Expression expression,
        VariableTypes types,
        SumTypeIndex index,
        List<Diagnostic> diagnostics)
    {
        switch (expression)
        {
            case MatchExpression match:
                ValidateMatch(match, types, index, diagnostics);
                VisitExpression(match.Value, types, index, diagnostics);
                foreach (var arm in match.Cases)
                {
                    if (arm.Guard != null)
                        VisitExpression(arm.Guard, types.Clone(), index, diagnostics);
                    VisitStatement(arm.Body, types.Clone(), index, diagnostics);
                }
                if (match.DefaultCase != null)
                    VisitStatement(match.DefaultCase, types.Clone(), index, diagnostics);
                break;
            case FunctionCallExpression call:
                VisitExpression(call.Callee, types, index, diagnostics);
                foreach (var arg in call.Arguments)
                    VisitExpression(arg, types, index, diagnostics);
                break;
            case BinaryExpression binary:
                VisitExpression(binary.Left, types, index, diagnostics);
                VisitExpression(binary.Right, types, index, diagnostics);
                break;
            case UnaryExpression unary:
                VisitExpression(unary.Right, types, index, diagnostics);
                break;
            case TernaryExpression ternary:
                VisitExpression(ternary.Condition, types, index, diagnostics);
                VisitExpression(ternary.ThenBranch, types, index, diagnostics);
                VisitExpression(ternary.ElseBranch, types, index, diagnostics);
                break;
            case AwaitExpression awaitExpr:
                VisitExpression(awaitExpr.Expression, types, index, diagnostics);
                break;
            case ArrayAccessExpression arrayAccess:
                VisitExpression(arrayAccess.Array, types, index, diagnostics);
                VisitExpression(arrayAccess.Index, types, index, diagnostics);
                break;
            case ArrayLiteralExpression arrayLit:
                foreach (var element in arrayLit.Elements)
                    VisitExpression(element, types, index, diagnostics);
                break;
            case ObjectLiteralExpression objectLit:
                foreach (var (key, value) in objectLit.Properties)
                {
                    VisitExpression(key, types, index, diagnostics);
                    VisitExpression(value, types, index, diagnostics);
                }
                break;
            case DictionaryLiteralExpression dictLit:
                foreach (var (key, value) in dictLit.Entries)
                {
                    VisitExpression(key, types, index, diagnostics);
                    VisitExpression(value, types, index, diagnostics);
                }
                break;
            case LambdaExpression lambda:
                if (lambda.ExpressionBody != null)
                    VisitExpression(lambda.ExpressionBody, types.Clone(), index, diagnostics);
                if (lambda.BlockBody != null)
                    VisitBlock(lambda.BlockBody, types.Clone(), index, diagnostics);
                break;
            case InterpolatedStringExpression interpolated:
                foreach (var segment in interpolated.Segments)
                {
                    if (segment.Expression != null)
                        VisitExpression(segment.Expression, types, index, diagnostics);
                }
                break;
            case NewExpression newExpr:
                foreach (var arg in newExpr.Arguments)
                    VisitExpression(arg, types, index, diagnostics);
                break;
            case MemberAccessExpression member:
                VisitExpression(member.Object, types, index, diagnostics);
                break;
        }
    }

    private static void ValidateMatch(
        MatchExpression match,
        VariableTypes types,
        SumTypeIndex index,
        List<Diagnostic> diagnostics)
    {
        if (match.DefaultCase != null)
            return;

        if (HasCatchAllPattern(match, index))
            return;

        if (!types.TryResolveSumType(match.Value, out var sumTypeName))
            return;

        var required = index.GetConstructors(sumTypeName);
        if (required.Count == 0)
            return;

        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arm in match.Cases)
        {
            if (arm.Guard != null)
                continue;
            if (arm.Pattern is VariantPattern variant)
                covered.Add(variant.Tag);
            else if (arm.Pattern is IdentifierPattern id &&
                     index.TryGetSumTypeForConstructor(id.Name, out _))
                covered.Add(id.Name);
        }

        var missing = required.Where(c => !covered.Contains(c)).ToList();
        if (missing.Count == 0)
            return;

        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message =
                $"Non-exhaustive match on sum type '{sumTypeName}': missing case(s) {string.Join(", ", missing)}. " +
                "Add variant cases or a default branch.",
            // LanguageService / LSP / Desktop IDE all use 0-based coordinates.
            Line = Math.Max(0, match.Line - 1),
            Column = Math.Max(0, match.Column - 1),
            Length = "match".Length,
            Source = "malda-match"
        });
    }

    private static bool HasCatchAllPattern(MatchExpression match, SumTypeIndex index)
    {
        foreach (var arm in match.Cases)
        {
            if (arm.Guard != null)
                continue;
            if (arm.Pattern is WildcardPattern)
                return true;
            if (arm.Pattern is IdentifierPattern id &&
                !index.TryGetSumTypeForConstructor(id.Name, out _))
                return true;
        }

        return false;
    }

    private sealed class VariableTypes
    {
        private readonly Dictionary<string, string> _sumTypes = new(StringComparer.Ordinal);
        private readonly SumTypeIndex _index;

        public VariableTypes(SumTypeIndex index) => _index = index;

        private VariableTypes(SumTypeIndex index, Dictionary<string, string> sumTypes)
        {
            _index = index;
            foreach (var kv in sumTypes)
                _sumTypes[kv.Key] = kv.Value;
        }

        public VariableTypes Clone() => new(_index, _sumTypes);

        public void RecordDeclaration(VarDeclStatement varDecl)
        {
            if (!string.IsNullOrEmpty(varDecl.TypeHint) && _index.IsSumType(varDecl.TypeHint))
                _sumTypes[varDecl.Name] = varDecl.TypeHint;
            else if (varDecl.Initializer != null &&
                     TryInferSumType(varDecl.Initializer, out var inferred))
                _sumTypes[varDecl.Name] = inferred;
        }

        public void RecordAssignment(AssignmentStatement assign)
        {
            if (assign.Target is not IdentifierExpression id)
                return;

            if (TryInferSumType(assign.Value, out var inferred))
                _sumTypes[id.Name] = inferred;
        }

        public bool TryResolveSumType(Expression value, out string sumTypeName)
        {
            sumTypeName = string.Empty;
            if (value is IdentifierExpression id && _sumTypes.TryGetValue(id.Name, out var known))
            {
                sumTypeName = known;
                return true;
            }

            return false;
        }

        private bool TryInferSumType(Expression expression, out string sumTypeName)
        {
            sumTypeName = string.Empty;
            if (expression is not FunctionCallExpression call ||
                call.Callee is not IdentifierExpression ctorId)
            {
                return false;
            }

            return _index.TryGetSumTypeForConstructor(ctorId.Name, out sumTypeName);
        }
    }
}
