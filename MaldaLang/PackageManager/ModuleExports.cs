// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System.Collections.Generic;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public static class ModuleExports
{
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
            }
        }

        return any ? exports : null;
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
    /// Throws if any requested name is absent.
    /// </summary>
    public static Dictionary<string, RuntimeValue> FilterSelectedSymbols(
        Dictionary<string, RuntimeValue> exportSurface,
        IReadOnlyList<string> selectedNames)
    {
        var filtered = new Dictionary<string, RuntimeValue>(System.StringComparer.Ordinal);
        foreach (var name in selectedNames)
        {
            if (!exportSurface.TryGetValue(name, out var value))
            {
                throw new RuntimeException(
                    $"Selective import: '{name}' is not exported by the module " +
                    "(missing or not marked export when the module uses export).");
            }

            filtered[name] = value;
        }

        return filtered;
    }
}
