// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// IDE checks for <c>schema</c> field types, sum-type constructor payload types,
/// and optional <c>api</c> method parameter types
/// (primitives, declared/imported schemas and sum types).
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
            if (stmt is SchemaDeclaration schema)
            {
                foreach (var field in schema.Fields)
                {
                    ValidateTypeName(
                        field.TypeName,
                        $"'{schema.Name}.{field.Name}'",
                        "schema field type",
                        schema.Line,
                        schema.Column,
                        index,
                        diagnostics,
                        options);
                }
            }
            else if (stmt is ApiDeclaration apiDecl)
            {
                foreach (var method in apiDecl.Methods)
                {
                    for (var i = 0; i < method.ParameterNames.Count; i++)
                    {
                        var paramType = method.ParameterTypeAt(i);
                        if (string.IsNullOrEmpty(paramType))
                            continue;

                        ValidateTypeName(
                            paramType,
                            $"'{apiDecl.Name}.{method.Name}({method.ParameterNames[i]})'",
                            "api parameter type",
                            apiDecl.Line,
                            apiDecl.Column,
                            index,
                            diagnostics,
                            options);
                    }
                }
            }
            else if (stmt is TypeDeclaration typeDecl)
            {
                foreach (var ctor in typeDecl.Constructors)
                {
                    for (var i = 0; i < ctor.ParameterNames.Count; i++)
                    {
                        var payloadType = ctor.ParameterTypeAt(i);
                        if (string.IsNullOrEmpty(payloadType))
                            continue;

                        ValidateTypeName(
                            payloadType,
                            $"'{typeDecl.TypeName}.{ctor.Name}({ctor.ParameterNames[i]})'",
                            "constructor payload type",
                            typeDecl.Line,
                            typeDecl.Column,
                            index,
                            diagnostics,
                            options);
                    }
                }
            }
        }
    }

    private static void ValidateTypeName(
        string typeName,
        string site,
        string kind,
        int line,
        int column,
        TypeHintNameIndex index,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        typeName = typeName?.Trim() ?? "";
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
                ? $"Unknown {kind} '{typeName}' on {site}."
                : $"Unknown {kind} '{typeName}' on {site}. Use a JSON primitive, a declared schema name, or a declared sum type.",
            Line = line,
            Column = column,
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
