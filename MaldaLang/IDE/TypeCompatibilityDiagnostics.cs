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
/// Warning-only checks for type hints vs literal initializers (always on in the IDE).
/// Does not enforce hints at runtime.
/// </summary>
public static class TypeCompatibilityDiagnostics
{
    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics)
    {
        foreach (var stmt in statements)
            ValidateStatement(stmt, diagnostics);
    }

    private static void ValidateStatement(Statement stmt, List<Diagnostic> diagnostics)
    {
        switch (stmt)
        {
            case FunctionDeclaration funcDecl:
                foreach (var inner in funcDecl.Body.Statements)
                    ValidateStatement(inner, diagnostics);
                break;
            case VarDeclStatement varDecl:
                ValidateVarDecl(varDecl, diagnostics);
                break;
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.TypeHint != null && member.Value is Expression fieldInit)
                        CheckLiteralCompatibility(member.TypeHint, fieldInit, member.Name, classDecl.Line, classDecl.Column, diagnostics);
                    if (member.Value is FunctionDeclaration method)
                    {
                        foreach (var inner in method.Body.Statements)
                            ValidateStatement(inner, diagnostics);
                    }
                }
                break;
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    ValidateStatement(inner, diagnostics);
                break;
        }
    }

    private static void ValidateVarDecl(VarDeclStatement varDecl, List<Diagnostic> diagnostics)
    {
        if (varDecl.TypeHint == null)
            return;
        CheckLiteralCompatibility(
            varDecl.TypeHint,
            varDecl.Initializer,
            varDecl.Name,
            varDecl.Line,
            varDecl.Column,
            diagnostics);
    }

    private static void CheckLiteralCompatibility(
        string typeHint,
        Expression initializer,
        string bindingName,
        int line,
        int column,
        List<Diagnostic> diagnostics)
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
            return; // non-literal initializer — out of scope for this slice

        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;

        // int/float: whole-number literals are integers; a float hint accepts int literals.
        if (expected == "float" && actual == "int")
            return;

        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Message = $"Type hint '{typeHint}' on '{bindingName}' does not match literal initializer (got {actual}). Hints are not enforced at runtime.",
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
                // Lexer may box integers as Int64 / doubles as Double already covered;
                // also accept System.Int32 etc. via Convert if needed.
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
