// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang;

using System.Text.RegularExpressions;

/// <summary>
/// Mechanical <c>malda check --fix</c> rewrites. Deprecated flat aliases go through
/// <see cref="Linting.FlatAliasFixer"/> (the same table and safety rules as
/// <c>malda lint --fix</c>: calls only, never inside strings or comments, never a name the
/// program binds), after the other edits so a source they repair is rewritten too.
/// </summary>
internal static class CheckFixer
{
    public static string Apply(string source, string? path = null)
    {
        var result = Regex.Replace(source, @"(?<!\$)""([^""]*\{[^}]+\}[^""]*)""", match =>
        {
            var inner = match.Groups[1].Value;
            if (inner.Contains('{') && inner.Contains('}'))
                return "$\"" + inner + "\"";
            return match.Value;
        });

        result = result.Replace(".length()", ".length", StringComparison.Ordinal);
        result = Regex.Replace(result, @"\barr\.append\(", "items.append(");
        return Linting.FlatAliasFixer.Fix(result, path).FixedSource;
    }
}
