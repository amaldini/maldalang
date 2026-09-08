// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang;

using System.Text.RegularExpressions;

/// <summary>
/// Mechanical <c>malda check --fix</c> rewrites.
/// </summary>
internal static class CheckFixer
{
    private static readonly (string From, string To)[] FlatAliases =
    [
        ("print(", "io.print("),
        ("readFile(", "io.readFile("),
        ("writeFile(", "io.writeFile("),
        ("sqrt(", "math.sqrt("),
        ("abs(", "math.abs("),
        ("upper(", "str.upper("),
        ("lower(", "str.lower("),
        ("trim(", "str.trim("),
        ("split(", "str.split("),
        ("join(", "str.join(")
    ];

    public static string Apply(string source)
    {
        var result = source;
        foreach (var (from, to) in FlatAliases)
        {
            result = Regex.Replace(result, $@"(?<![.\w]){Regex.Escape(from)}", to);
        }

        result = Regex.Replace(result, @"(?<!\$)""([^""]*\{[^}]+\}[^""]*)""", match =>
        {
            var inner = match.Groups[1].Value;
            if (inner.Contains('{') && inner.Contains('}'))
                return "$\"" + inner + "\"";
            return match.Value;
        });

        result = result.Replace(".length()", ".length", StringComparison.Ordinal);
        result = Regex.Replace(result, @"\barr\.append\(", "items.append(");
        return result;
    }
}
