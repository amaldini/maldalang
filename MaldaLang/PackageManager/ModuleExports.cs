// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System.Collections.Generic;
using System.Linq;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public static class ModuleExports
{
    /// <summary>
    /// Names marked with <c>export</c>. Null means an open module (no <c>export</c> at all).
    /// For <c>export type</c>, includes the type name and all constructor names.
    /// For <c>export schema</c>, includes the schema name.
    /// </summary>
    public static HashSet<string>? CollectExplicitExports(IEnumerable<Statement> statements)
    {
        var exports = new HashSet<string>(System.StringComparer.Ordinal);
        var any = false;

        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case FunctionDeclaration fd when fd.IsExported:
                    any = true;
                    exports.Add(fd.Name);
                    break;
                case VarDeclStatement vd when vd.IsExported:
                    any = true;
                    exports.Add(vd.Name);
                    break;
                case ClassDeclaration cd when cd.IsExported:
                    any = true;
                    exports.Add(cd.Name);
                    break;
                case TypeDeclaration td when td.IsExported:
                    any = true;
                    exports.Add(td.TypeName);
                    foreach (var ctor in td.Constructors)
                        exports.Add(ctor.Name);
                    break;
                case SchemaDeclaration sd when sd.IsExported:
                    any = true;
                    exports.Add(sd.Name);
                    break;
            }
        }

        return any ? exports : null;
    }

    /// <summary>
    /// Names that may appear on the export surface without a runtime binding
    /// (schema / sum-type names). Open modules include every schema and type name.
    /// </summary>
    public static HashSet<string> CollectNonValueExportNames(IEnumerable<Statement> statements)
    {
        var names = new HashSet<string>(System.StringComparer.Ordinal);
        var explicitExports = CollectExplicitExports(statements);
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case TypeDeclaration td when explicitExports == null || td.IsExported:
                    names.Add(td.TypeName);
                    break;
                case SchemaDeclaration sd when explicitExports == null || sd.IsExported:
                    names.Add(sd.Name);
                    break;
            }
        }

        return names;
    }

    /// <summary>
    /// Expand selective import names so that selecting a sum type also pulls its constructors,
    /// and selecting any constructor of an exported type keeps the type declaration in scope
    /// for tooling (caller still filters values separately).
    /// </summary>
    public static List<string> ExpandSelectedNames(
        IReadOnlyList<string> selectedNames,
        IEnumerable<Statement> statements)
    {
        var explicitExports = CollectExplicitExports(statements);
        var expanded = new HashSet<string>(selectedNames, System.StringComparer.Ordinal);

        foreach (var stmt in statements)
        {
            if (stmt is not TypeDeclaration td)
                continue;
            if (explicitExports != null && !td.IsExported)
                continue;

            var ctorNames = td.Constructors.Select(c => c.Name).ToList();
            var typeSelected = expanded.Contains(td.TypeName);
            var ctorSelected = ctorNames.Any(expanded.Contains);
            if (!typeSelected && !ctorSelected)
                continue;

            expanded.Add(td.TypeName);
            foreach (var ctor in ctorNames)
                expanded.Add(ctor);
        }

        return expanded.ToList();
    }

    public static Dictionary<string, RuntimeValue> FilterExportedSymbols(
        Dictionary<string, RuntimeValue> allSymbols,
        HashSet<string>? explicitExports)
    {
        if (explicitExports == null)
            return allSymbols;

        var filtered = new Dictionary<string, RuntimeValue>(System.StringComparer.Ordinal);
        foreach (var name in explicitExports)
        {
            if (allSymbols.TryGetValue(name, out var value))
                filtered[name] = value;
        }

        return filtered;
    }

    /// <summary>
    /// Intersect an export surface with a selective <c>import { … } from</c> name list.
    /// Throws if any requested name is absent from the value surface and is not a
    /// schema/type-only export name.
    /// </summary>
    public static Dictionary<string, RuntimeValue> FilterSelectedSymbols(
        Dictionary<string, RuntimeValue> exportSurface,
        IReadOnlyList<string> selectedNames,
        IReadOnlySet<string>? nonValueExportNames = null)
    {
        var filtered = new Dictionary<string, RuntimeValue>(System.StringComparer.Ordinal);
        foreach (var name in selectedNames)
        {
            if (exportSurface.TryGetValue(name, out var value))
            {
                filtered[name] = value;
                continue;
            }

            if (nonValueExportNames != null && nonValueExportNames.Contains(name))
                continue;

            throw new RuntimeException(
                $"Selective import: '{name}' is not exported by the module " +
                "(missing or not marked export when the module uses export).");
        }

        return filtered;
    }
}
