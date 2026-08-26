// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.IDE.Models;

/// <summary>
/// Phase 4.1: informational validation of type hints (no runtime enforcement).
/// Recognizes Tier 0 names, declared class/schema names, and built-in host classes.
/// </summary>
public static class TypeHintDiagnostics
{
    public static void Validate(
        IEnumerable<Statement> statements,
        List<Diagnostic> diagnostics,
        StrictTypesOptions? options = null,
        string? sourceFileName = null)
    {
        options ??= StrictTypesOptions.Default;
        var list = statements as IList<Statement> ?? statements.ToList();
        var index = TypeHintNameIndex.Build(list);
        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            try
            {
                var imported = ModuleSymbolResolver.LoadImportedSymbols(list, sourceFileName);
                index.MergeImported(imported);
            }
            catch
            {
                // Best-effort for IDE/CLI tooling
            }
        }

        foreach (var stmt in list)
            ValidateStatement(stmt, diagnostics, options, index);
    }

    private static void ValidateStatement(
        Statement stmt,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options,
        TypeHintNameIndex index)
    {
        switch (stmt)
        {
            case FunctionDeclaration funcDecl:
                if (ShaderFunction.IsMarked(funcDecl))
                    break;
                if (funcDecl.ReturnType != null)
                    ValidateTypeName(funcDecl.ReturnType, funcDecl.Line, funcDecl.Column, "return type", diagnostics, options, index);
                if (funcDecl.ParameterTypeHints != null)
                {
                    for (var i = 0; i < funcDecl.ParameterTypeHints.Count; i++)
                    {
                        var hint = funcDecl.ParameterTypeHints[i];
                        if (hint != null)
                        {
                            var paramName = i < funcDecl.Parameters.Count ? funcDecl.Parameters[i] : "parameter";
                            ValidateTypeName(hint, funcDecl.Line, funcDecl.Column, $"parameter '{paramName}'", diagnostics, options, index);
                        }
                    }
                }
                foreach (var inner in funcDecl.Body.Statements)
                    ValidateStatement(inner, diagnostics, options, index);
                break;
            case VarDeclStatement varDecl:
                ValidateVarDecl(varDecl, diagnostics, options, index);
                break;
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.TypeHint != null)
                        ValidateTypeName(member.TypeHint, classDecl.Line, classDecl.Column, $"field '{member.Name}'", diagnostics, options, index);
                    if (member.Value is FunctionDeclaration method)
                    {
                        foreach (var inner in method.Body.Statements)
                            ValidateStatement(inner, diagnostics, options, index);
                    }
                }
                break;
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    ValidateStatement(inner, diagnostics, options, index);
                break;
        }
    }

    public static void ValidateVarDecl(
        VarDeclStatement varDecl,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options,
        TypeHintNameIndex? index = null)
    {
        if (varDecl.TypeHint != null)
            ValidateTypeName(
                varDecl.TypeHint,
                varDecl.Line,
                varDecl.Column,
                "variable",
                diagnostics,
                options,
                index ?? new TypeHintNameIndex());
    }

    private static void ValidateTypeName(
        string typeName,
        int line,
        int column,
        string context,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options,
        TypeHintNameIndex index)
    {
        if (index.IsKnown(typeName))
            return;

        var elevate = options.ElevateTypeSeverity;
        diagnostics.Add(new Diagnostic
        {
            Severity = elevate ? DiagnosticSeverity.Error : DiagnosticSeverity.Info,
            Message = elevate
                ? $"Unknown type hint '{typeName}' on {context}."
                : $"Unknown type hint '{typeName}' on {context}. Hints are informational until type errors are enabled.",
            Line = Math.Max(0, line - 1),
            Column = Math.Max(0, column - 1),
            Length = typeName.Length,
            Source = "malda-types"
        });
    }
}
