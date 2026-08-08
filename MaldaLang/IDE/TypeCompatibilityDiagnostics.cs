// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Checks type hints vs literal values on var/field initializers, call arguments, and returns.
/// Default severity is Warning (IDE/LSP). Under <c>--strict-types</c> mismatches are Errors.
/// Does not enforce hints at runtime.
/// </summary>
public static class TypeCompatibilityDiagnostics
{
    private sealed record FunctionHints(
        IReadOnlyList<string?> ParameterHints,
        string? ReturnType);

    public static void Validate(
        IEnumerable<Statement> statements,
        List<Diagnostic> diagnostics,
        StrictTypesOptions? options = null)
    {
        options ??= StrictTypesOptions.Default;
        var list = statements as IList<Statement> ?? statements.ToList();
        var functions = new Dictionary<string, FunctionHints>(StringComparer.Ordinal);
        CollectFunctions(list, functions);

        foreach (var stmt in list)
            VisitStatement(stmt, functions, currentReturn: null, diagnostics, options);
    }

    private static void CollectFunctions(
        IEnumerable<Statement> statements,
        Dictionary<string, FunctionHints> functions)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case FunctionDeclaration funcDecl:
                    functions[funcDecl.Name] = new FunctionHints(
                        funcDecl.ParameterTypeHints ?? new List<string?>(),
                        funcDecl.ReturnType);
                    break;
                case ClassDeclaration classDecl:
                    foreach (var member in classDecl.Members)
                    {
                        if (member.Value is FunctionDeclaration method)
                        {
                            // Index unqualified method name for same-file simple calls; first wins.
                            if (!functions.ContainsKey(method.Name))
                            {
                                functions[method.Name] = new FunctionHints(
                                    method.ParameterTypeHints ?? new List<string?>(),
                                    method.ReturnType);
                            }
                        }
                    }
                    break;
                case BlockStatement block:
                    CollectFunctions(block.Statements, functions);
                    break;
            }
        }
    }

    private static void VisitStatement(
        Statement stmt,
        Dictionary<string, FunctionHints> functions,
        string? currentReturn,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        switch (stmt)
        {
            case FunctionDeclaration funcDecl:
                foreach (var inner in funcDecl.Body.Statements)
                    VisitStatement(inner, functions, funcDecl.ReturnType, diagnostics, options);
                break;
            case VarDeclStatement varDecl:
                if (varDecl.TypeHint != null)
                {
                    CheckLiteralCompatibility(
                        varDecl.TypeHint,
                        varDecl.Initializer,
                        $"variable '{varDecl.Name}'",
                        varDecl.Line,
                        varDecl.Column,
                        diagnostics,
                        options);
                }
                VisitExpression(varDecl.Initializer, functions, diagnostics, options);
                break;
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.TypeHint != null && member.Value is Expression fieldInit)
                    {
                        CheckLiteralCompatibility(
                            member.TypeHint,
                            fieldInit,
                            $"field '{member.Name}'",
                            classDecl.Line,
                            classDecl.Column,
                            diagnostics,
                            options);
                        VisitExpression(fieldInit, functions, diagnostics, options);
                    }
                    if (member.Value is FunctionDeclaration method)
                    {
                        foreach (var inner in method.Body.Statements)
                            VisitStatement(inner, functions, method.ReturnType, diagnostics, options);
                    }
                }
                break;
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    VisitStatement(inner, functions, currentReturn, diagnostics, options);
                break;
            case ExpressionStatement exprStmt:
                VisitExpression(exprStmt.Expression, functions, diagnostics, options);
                break;
            case ReturnStatement returnStmt:
                if (currentReturn != null && returnStmt.Value != null)
                {
                    CheckLiteralCompatibility(
                        currentReturn,
                        returnStmt.Value,
                        "return value",
                        returnStmt.Line,
                        returnStmt.Column,
                        diagnostics,
                        options);
                }
                if (returnStmt.Value != null)
                    VisitExpression(returnStmt.Value, functions, diagnostics, options);
                break;
            case IfStatement ifStmt:
                VisitExpression(ifStmt.Condition, functions, diagnostics, options);
                VisitStatement(ifStmt.ThenBranch, functions, currentReturn, diagnostics, options);
                if (ifStmt.ElseBranch != null)
                    VisitStatement(ifStmt.ElseBranch, functions, currentReturn, diagnostics, options);
                break;
            case WhileStatement whileStmt:
                VisitExpression(whileStmt.Condition, functions, diagnostics, options);
                VisitStatement(whileStmt.Body, functions, currentReturn, diagnostics, options);
                break;
            case ForStatement forStmt:
                if (forStmt.Initializer != null)
                    VisitStatement(forStmt.Initializer, functions, currentReturn, diagnostics, options);
                if (forStmt.Condition != null)
                    VisitExpression(forStmt.Condition, functions, diagnostics, options);
                if (forStmt.Increment != null)
                    VisitExpression(forStmt.Increment, functions, diagnostics, options);
                VisitStatement(forStmt.Body, functions, currentReturn, diagnostics, options);
                break;
            case ForInStatement forInStmt:
                VisitExpression(forInStmt.Collection, functions, diagnostics, options);
                VisitStatement(forInStmt.Body, functions, currentReturn, diagnostics, options);
                break;
            case TryStatement tryStmt:
                foreach (var inner in tryStmt.TryBlock.Statements)
                    VisitStatement(inner, functions, currentReturn, diagnostics, options);
                foreach (var catchClause in tryStmt.CatchClauses)
                    VisitStatement(catchClause.Body, functions, currentReturn, diagnostics, options);
                if (tryStmt.FinallyBlock != null)
                {
                    foreach (var inner in tryStmt.FinallyBlock.Statements)
                        VisitStatement(inner, functions, currentReturn, diagnostics, options);
                }
                break;
            case AssignmentStatement assign:
                VisitExpression(assign.Value, functions, diagnostics, options);
                break;
        }
    }

    private static void VisitExpression(
        Expression expression,
        Dictionary<string, FunctionHints> functions,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        switch (expression)
        {
            case FunctionCallExpression call:
                if (call.Callee is IdentifierExpression id &&
                    functions.TryGetValue(id.Name, out var hints))
                {
                    for (var i = 0; i < call.Arguments.Count; i++)
                    {
                        string? paramHint = null;
                        if (hints.ParameterHints != null && i < hints.ParameterHints.Count)
                            paramHint = hints.ParameterHints[i];
                        if (paramHint == null)
                            continue;

                        var paramName = $"argument {i + 1} of '{id.Name}'";
                        CheckLiteralCompatibility(
                            paramHint,
                            call.Arguments[i],
                            paramName,
                            call.Arguments[i].Line > 0 ? call.Arguments[i].Line : call.Line,
                            call.Arguments[i].Column > 0 ? call.Arguments[i].Column : call.Column,
                            diagnostics,
                            options);
                    }
                }
                foreach (var arg in call.Arguments)
                    VisitExpression(arg, functions, diagnostics, options);
                VisitExpression(call.Callee, functions, diagnostics, options);
                break;
            case BinaryExpression binary:
                VisitExpression(binary.Left, functions, diagnostics, options);
                VisitExpression(binary.Right, functions, diagnostics, options);
                break;
            case UnaryExpression unary:
                VisitExpression(unary.Right, functions, diagnostics, options);
                break;
            case TernaryExpression ternary:
                VisitExpression(ternary.Condition, functions, diagnostics, options);
                VisitExpression(ternary.ThenBranch, functions, diagnostics, options);
                VisitExpression(ternary.ElseBranch, functions, diagnostics, options);
                break;
            case ArrayLiteralExpression array:
                foreach (var el in array.Elements)
                    VisitExpression(el, functions, diagnostics, options);
                break;
            case ObjectLiteralExpression obj:
                foreach (var (_, value) in obj.Properties)
                    VisitExpression(value, functions, diagnostics, options);
                break;
            case DictionaryLiteralExpression dict:
                foreach (var (key, value) in dict.Entries)
                {
                    VisitExpression(key, functions, diagnostics, options);
                    VisitExpression(value, functions, diagnostics, options);
                }
                break;
            case ArrayAccessExpression access:
                VisitExpression(access.Array, functions, diagnostics, options);
                VisitExpression(access.Index, functions, diagnostics, options);
                break;
            case MemberAccessExpression member:
                VisitExpression(member.Object, functions, diagnostics, options);
                break;
            case InterpolatedStringExpression interpolated:
                foreach (var segment in interpolated.Segments)
                {
                    if (segment.IsExpression && segment.Expression != null)
                        VisitExpression(segment.Expression, functions, diagnostics, options);
                }
                break;
            case AwaitExpression awaitExpr:
                VisitExpression(awaitExpr.Expression, functions, diagnostics, options);
                break;
            case AsyncExpression asyncExpr:
                VisitExpression(asyncExpr.Expression, functions, diagnostics, options);
                break;
        }
    }

    private static void CheckLiteralCompatibility(
        string typeHint,
        Expression initializer,
        string bindingName,
        int line,
        int column,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        if (string.Equals(typeHint, "any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "void", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var expected = Tier0TypeTags.NormalizeToCanonical(typeHint);
        if (expected == null)
            return; // unknown hint names are handled by TypeHintDiagnostics

        var actual = InferLiteralTag(initializer);
        if (actual == null)
            return; // non-literal — out of scope

        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;

        // int/float: a float hint accepts int literals.
        if (expected == "float" && actual == "int")
            return;

        diagnostics.Add(new Diagnostic
        {
            Severity = options.StrictTypes ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            Message = options.StrictTypes
                ? $"Type hint '{typeHint}' on {bindingName} does not match literal (got {actual})."
                : $"Type hint '{typeHint}' on {bindingName} does not match literal (got {actual}). Hints are not enforced at runtime.",
            Line = line,
            Column = column,
            Length = Math.Max(1, typeHint.Length),
            Source = "malda-types"
        });
    }

    /// <summary>
    /// Returns a canonical Tier 0 tag for a literal expression, or null if not a checked literal.
    /// </summary>
    internal static string? InferLiteralTag(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression lit:
                if (lit.Value is null)
                    return "null";
                if (lit.Value is bool)
                    return "bool";
                if (lit.Value is string)
                    return "string";
                if (lit.Value is int or long or byte or sbyte or short or ushort or uint)
                    return "int";
                if (lit.Value is float or double or decimal)
                    return "float";
                if (lit.Value is IConvertible convertible && lit.Value is not string)
                {
                    var typeCode = convertible.GetTypeCode();
                    if (typeCode is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
                        or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64)
                        return "int";
                    if (typeCode is TypeCode.Single or TypeCode.Double or TypeCode.Decimal)
                        return "float";
                }
                return null;
            case ArrayLiteralExpression arr when arr.Elements.Count == 0:
                return "array";
            case DictionaryLiteralExpression dict when dict.Entries.Count == 0:
                return "dict";
            default:
                return null;
        }
    }
}
