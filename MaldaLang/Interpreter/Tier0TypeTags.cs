// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

/// <summary>
/// Canonical <c>typeOf</c> tags (Phase 4.2), aligned with informational type hints.
/// Legacy tag names remain accepted by <see cref="MatchesTag"/> during deprecation.
/// </summary>
public static class Tier0TypeTags
{
    public static readonly string[] Canonical =
    {
        "int", "float", "string", "bool", "array", "dict", "object", "null",
        "variant", "task", "function", "class", "actor"
    };

    private static readonly HashSet<string> CanonicalSet = new(Canonical, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> LegacyToCanonical = new(StringComparer.OrdinalIgnoreCase)
    {
        ["integer"] = "int",
        ["boolean"] = "bool",
        ["dictionary"] = "dict",
    };

    public static string GetTag(RuntimeValue value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        return value.Type switch
        {
            ValueType.Integer => "int",
            ValueType.Float => "float",
            ValueType.String => "string",
            ValueType.Boolean => "bool",
            ValueType.Null => "null",
            ValueType.Array => "array",
            ValueType.Object => value.AsObject() is DictionaryInstance ? "dict" : "object",
            ValueType.Function => "function",
            ValueType.Class => "class",
            ValueType.ActorReference or ValueType.Actor => "actor",
            ValueType.Variant => "variant",
            ValueType.Task => "task",
            _ => "unknown"
        };
    }

    public static string? NormalizeToCanonical(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var trimmed = tag.Trim();
        if (CanonicalSet.Contains(trimmed))
            return trimmed;

        return LegacyToCanonical.TryGetValue(trimmed, out var canonical) ? canonical : null;
    }

    /// <summary>
    /// Returns true when <paramref name="actualTag"/> from <c>typeOf</c> matches
    /// <paramref name="expectedTag"/>, including deprecated legacy names.
    /// </summary>
    public static bool MatchesTag(string actualTag, string expectedTag)
    {
        var actual = NormalizeToCanonical(actualTag) ?? actualTag;
        var expected = NormalizeToCanonical(expectedTag) ?? expectedTag;
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    public static bool IsDeprecatedTag(string tag) =>
        LegacyToCanonical.ContainsKey(tag);
}
