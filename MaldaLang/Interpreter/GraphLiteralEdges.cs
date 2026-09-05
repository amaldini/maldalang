// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.BuiltIns;

/// <summary>
/// Graph literals accept extra edge keys (<c>rel</c>, <c>contract</c>, …) besides
/// <c>from</c> / <c>to</c> / <c>weight</c> / <c>properties</c>. Those keys land on
/// <see cref="GraphEdge.Properties"/> so <c>agents.team</c> can read relations.
/// </summary>
public static class GraphLiteralEdges
{
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.Ordinal)
    {
        "from", "to", "weight", "properties"
    };

    public static RuntimeValue MergeProperties(object? edgeObj, RuntimeValue existingPropsVal)
    {
        DictionaryInstance? existing = null;
        if (existingPropsVal.Type == ValueType.Object && existingPropsVal.AsObject() is DictionaryInstance dict)
            existing = dict;

        var merged = CollectExtraProperties(edgeObj as ObjectInstance, existing);
        return merged == null ? RuntimeValue.Null() : RuntimeValue.Object(merged);
    }

    public static DictionaryInstance? CollectExtraProperties(ObjectInstance? edgeObj, DictionaryInstance? existing)
    {
        if (edgeObj == null)
            return existing;

        DictionaryInstance? merged = existing;
        var cloned = false;
        foreach (var (key, value) in EnumerateEntries(edgeObj))
        {
            if (ReservedKeys.Contains(key))
                continue;
            if (value.Type == ValueType.Null)
                continue;

            if (merged == null)
            {
                merged = new DictionaryInstance();
            }
            else if (!cloned && existing != null)
            {
                merged = Clone(existing);
                cloned = true;
            }

            merged.SetEntry(key, value);
        }

        return merged;
    }

    private static DictionaryInstance Clone(DictionaryInstance source)
    {
        var copy = new DictionaryInstance();
        foreach (var pair in source.GetEntries())
            copy.SetEntry(pair.Key, pair.Value);
        return copy;
    }

    private static IEnumerable<KeyValuePair<string, RuntimeValue>> EnumerateEntries(ObjectInstance edgeObj)
    {
        if (edgeObj is JsonObject json)
            return json.GetProperties();
        if (edgeObj is DictionaryInstance dict)
            return dict.GetEntries();
        return Array.Empty<KeyValuePair<string, RuntimeValue>>();
    }
}
