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
/// IDE checks for <c>@Tool</c> / <c>@MCPTool</c> third arguments that name a schema or sum type.
/// JSON object strings are left to runtime parse.
/// </summary>
public static class ToolDecoratorSchemaDiagnostics
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
                // Best-effort
            }
        }

        foreach (var stmt in list)
        {
            if (stmt is not FunctionDeclaration func)
                continue;
            if (func.Decorators == null)
                continue;

            foreach (var decorator in func.Decorators)
            {
                if (decorator.Name != "Tool" && decorator.Name != "MCPTool")
                    continue;
                if (decorator.Arguments == null || decorator.Arguments.Count < 3)
                    continue;

                if (!TryGetSchemaName(decorator.Arguments[2], out var schemaName, out var nameLine, out var nameColumn))
                    continue;

                if (index.IsDeclaredSchemaOrSumType(schemaName))
                    continue;

                var elevate = options.ElevateTypeSeverity;
                diagnostics.Add(new Diagnostic
                {
                    Severity = elevate ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    Message = elevate
                        ? $"Unknown schema '{schemaName}' on @{decorator.Name} '{func.Name}'."
                        : $"Unknown schema '{schemaName}' on @{decorator.Name} '{func.Name}'. Use a declared schema or sum-type name, or a JSON schema object string.",
                    Line = Math.Max(0, nameLine - 1),
                    Column = Math.Max(0, nameColumn - 1),
                    Length = Math.Max(1, schemaName.Length),
                    Source = "malda-schema"
                });
            }
        }
    }

    private static bool TryGetSchemaName(
        Expression argument,
        out string schemaName,
        out int line,
        out int column)
    {
        schemaName = "";
        line = argument.Line;
        column = argument.Column;

        if (argument is IdentifierExpression identifier)
        {
            schemaName = identifier.Name;
            line = identifier.Line;
            column = identifier.Column;
            return !string.IsNullOrWhiteSpace(schemaName);
        }

        if (argument is LiteralExpression literal && literal.Value is string text)
        {
            if (ToolSchemaResolver.LooksLikeJsonObject(text))
                return false;
            var trimmed = text.Trim();
            if (trimmed.Length == 0)
                return false;
            schemaName = trimmed;
            line = literal.Line;
            column = literal.Column;
            return true;
        }

        return false;
    }
}
