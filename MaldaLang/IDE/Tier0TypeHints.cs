// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.IDE.Models;

/// <summary>
/// Tier 0 informational type names (Phase 4.1). Shared by diagnostics and completions.
/// </summary>
public static class Tier0TypeHints
{
    public static readonly string[] All =
    {
        "int", "integer", "float", "double", "string", "bool", "boolean",
        "array", "object", "dict", "dictionary", "null", "variant", "task",
        "void", "any"
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string typeName) => Known.Contains(typeName);

    public static List<CompletionItem> GetCompletions(string? partialPrefix)
    {
        var partial = partialPrefix?.Trim() ?? string.Empty;
        var items = new List<CompletionItem>();

        foreach (var typeName in All)
        {
            if (!string.IsNullOrEmpty(partial) &&
                !typeName.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new CompletionItem
            {
                Label = typeName,
                Kind = "type",
                Detail = "Type hint (informational)",
                InsertText = typeName
            });
        }

        return items.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
