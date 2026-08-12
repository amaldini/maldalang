// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// IDE checks for <c>schema</c> field type names (primitives, declared/imported schemas).
/// Complements runtime <see cref="MaldaLang.BuiltIns.SchemaRegistry"/> resolve errors.
/// </summary>
public static class SchemaDeclarationDiagnostics
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
            if (stmt is not SchemaDeclaration schema)
                continue;

            foreach (var field in schema.Fields)
                ValidateFieldType(schema, field, index, diagnostics, options);
        }
    }

    private static void ValidateFieldType(
        SchemaDeclaration schema,
        SchemaField field,
        TypeHintNameIndex index,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        var typeName = field.TypeName?.Trim() ?? "";
        if (typeName.Length == 0)
            return;

        var element = typeName;
        if (element.EndsWith("[]", StringComparison.Ordinal))
            element = element[..^2].Trim();

        if (IsJsonPrimitive(element) || index.IsKnown(element))
            return;

        var elevate = options.ElevateTypeSeverity;
        diagnostics.Add(new Diagnostic
        {
            Severity = elevate ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            Message = elevate
                ? $"Unknown schema field type '{field.TypeName}' on '{schema.Name}.{field.Name}'."
                : $"Unknown schema field type '{field.TypeName}' on '{schema.Name}.{field.Name}'. Use a JSON primitive or a declared schema name.",
            Line = schema.Line,
            Column = schema.Column,
            Length = Math.Max(1, typeName.Length),
            Source = "malda-schema"
        });
    }

    private static bool IsJsonPrimitive(string typeName) =>
        typeName.Trim().ToLowerInvariant() switch
        {
            "string" => true,
            "int" or "integer" => true,
            "float" or "double" or "number" => true,
            "bool" or "boolean" => true,
            "array" or "list" => true,
            "object" or "json" => true,
            "null" => true,
            _ => false
        };
}
