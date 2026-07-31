// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.IDE.Models;

/// <summary>
/// Phase 4.1: informational validation of Tier 0 type hints (no runtime enforcement).
/// </summary>
public static class TypeHintDiagnostics
{
    public static void Validate(
        IEnumerable<Statement> statements,
        List<Diagnostic> diagnostics,
        StrictTypesOptions? options = null)
    {
        options ??= StrictTypesOptions.Default;
        foreach (var stmt in statements)
            ValidateStatement(stmt, diagnostics, options);
    }

    private static void ValidateStatement(Statement stmt, List<Diagnostic> diagnostics, StrictTypesOptions options)
    {
        switch (stmt)
        {
            case FunctionDeclaration funcDecl:
                if (funcDecl.ReturnType != null)
                    ValidateTypeName(funcDecl.ReturnType, funcDecl.Line, funcDecl.Column, "return type", diagnostics, options);
                if (funcDecl.ParameterTypeHints != null)
                {
                    for (var i = 0; i < funcDecl.ParameterTypeHints.Count; i++)
                    {
                        var hint = funcDecl.ParameterTypeHints[i];
                        if (hint != null)
                        {
                            var paramName = i < funcDecl.Parameters.Count ? funcDecl.Parameters[i] : "parameter";
                            ValidateTypeName(hint, funcDecl.Line, funcDecl.Column, $"parameter '{paramName}'", diagnostics, options);
                        }
                    }
                }
                foreach (var inner in funcDecl.Body.Statements)
                    ValidateStatement(inner, diagnostics, options);
                break;
            case VarDeclStatement varDecl:
                ValidateVarDecl(varDecl, diagnostics, options);
                break;
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.TypeHint != null)
                        ValidateTypeName(member.TypeHint, classDecl.Line, classDecl.Column, $"field '{member.Name}'", diagnostics, options);
                    if (member.Value is FunctionDeclaration method)
                    {
                        foreach (var inner in method.Body.Statements)
                            ValidateStatement(inner, diagnostics, options);
                    }
                }
                break;
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    ValidateStatement(inner, diagnostics, options);
                break;
        }
    }

    public static void ValidateVarDecl(VarDeclStatement varDecl, List<Diagnostic> diagnostics, StrictTypesOptions options)
    {
        if (varDecl.TypeHint != null)
            ValidateTypeName(varDecl.TypeHint, varDecl.Line, varDecl.Column, "variable", diagnostics, options);
    }

    private static void ValidateTypeName(
        string typeName,
        int line,
        int column,
        string context,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        if (Tier0TypeHints.IsKnown(typeName))
            return;

        var strict = options.StrictTypes;
        diagnostics.Add(new Diagnostic
        {
            Severity = strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Info,
            Message = strict
                ? $"Unknown type hint '{typeName}' on {context}."
                : $"Unknown type hint '{typeName}' on {context}. Hints are informational until --strict-types.",
            Line = line,
            Column = column,
            Length = typeName.Length,
            Source = "malda-types"
        });
    }
}
