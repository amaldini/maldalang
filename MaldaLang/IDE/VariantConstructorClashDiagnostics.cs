// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using System.Linq;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Warns (errors under <c>--strict-types</c>) when two user sum types share a constructor tag.
/// Bare <c>Ok(...)</c> is a global last-wins binding; <c>r.Ok(...)</c> picks a type.
/// </summary>
public static class VariantConstructorClashDiagnostics
{
    public static void Validate(
        IEnumerable<Statement> statements,
        StrictTypesOptions options,
        List<Diagnostic> diagnostics,
        string? sourceFileName = null)
    {
        var localTypes = new List<TypeDeclaration>();
        CollectLocalTypes(statements, localTypes);
        if (localTypes.Count == 0)
            return;

        var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var typeDecl in localTypes)
            RegisterOwners(typeDecl, owners);

        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            try
            {
                var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, sourceFileName);
                foreach (var typeDecl in imported.Types)
                    RegisterOwners(typeDecl, owners);
            }
            catch
            {
                // Best-effort: broken imports are reported elsewhere.
            }
        }

        var severity = options.StrictTypes
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning;

        foreach (var typeDecl in localTypes)
        {
            var clashing = typeDecl.Constructors
                .Select(c => c.Name)
                .Where(name => owners.TryGetValue(name, out var types) && types.Count > 1)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (clashing.Count == 0)
                continue;

            foreach (var ctorName in clashing)
            {
                var typeNames = owners[ctorName];
                var others = typeNames
                    .Where(n => !string.Equals(n, typeDecl.TypeName, StringComparison.Ordinal))
                    .ToList();
                var qualified = string.Join(
                    " or ",
                    typeNames.Select(t => $"{t}.{ctorName}(...)"));

                diagnostics.Add(new Diagnostic
                {
                    Severity = severity,
                    Message =
                        $"Constructor '{ctorName}' is declared on more than one sum type " +
                        $"({string.Join(", ", typeNames)}). Bare {ctorName}(...) uses the last " +
                        $"declaration; write {qualified} to choose.",
                    Line = Math.Max(0, typeDecl.Line - 1),
                    Column = Math.Max(0, typeDecl.Column - 1),
                    Length = Math.Max(1, typeDecl.TypeName.Length),
                    Source = "malda-types",
                    SuggestedFix = others.Count > 0
                        ? $"{typeDecl.TypeName}.{ctorName}(...)"
                        : string.Empty
                });
            }
        }
    }

    private static void CollectLocalTypes(IEnumerable<Statement> statements, List<TypeDeclaration> types)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case TypeDeclaration typeDecl:
                    types.Add(typeDecl);
                    break;
                case FunctionDeclaration funcDecl:
                    CollectLocalTypes(funcDecl.Body.Statements, types);
                    break;
                case ClassDeclaration classDecl:
                    foreach (var member in classDecl.Members)
                    {
                        if (member.Value is FunctionDeclaration method)
                            CollectLocalTypes(method.Body.Statements, types);
                    }
                    break;
                case BlockStatement block:
                    CollectLocalTypes(block.Statements, types);
                    break;
            }
        }
    }

    private static void RegisterOwners(TypeDeclaration typeDecl, Dictionary<string, List<string>> owners)
    {
        foreach (var ctor in typeDecl.Constructors)
        {
            if (!owners.TryGetValue(ctor.Name, out var types))
            {
                types = new List<string>();
                owners[ctor.Name] = types;
            }

            if (!types.Contains(typeDecl.TypeName, StringComparer.Ordinal))
                types.Add(typeDecl.TypeName);
        }
    }
}
