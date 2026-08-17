// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

public readonly record struct SearchMatch(int Offset, int Length);

/// <summary>
/// Applies find/replace edits without depending on WPF.
/// Replace-all walks matches in reverse offset order so later spans stay valid.
/// </summary>
public static class SearchReplaceService
{
    public static string ReplaceAll(string source, IReadOnlyList<SearchMatch> matches, string replacement)
    {
        ArgumentNullException.ThrowIfNull(source);
        replacement ??= "";
        if (matches == null || matches.Count == 0)
        {
            return source;
        }

        var ordered = matches
            .Where(match => match.Offset >= 0 && match.Length > 0 && match.Offset + match.Length <= source.Length)
            .OrderByDescending(match => match.Offset)
            .ToList();

        var result = source;
        foreach (var match in ordered)
        {
            result = result.Remove(match.Offset, match.Length).Insert(match.Offset, replacement);
        }

        return result;
    }

    public static string ReplaceAt(string source, SearchMatch match, string replacement)
    {
        ArgumentNullException.ThrowIfNull(source);
        replacement ??= "";
        if (match.Offset < 0 || match.Length <= 0 || match.Offset + match.Length > source.Length)
        {
            return source;
        }

        return source.Remove(match.Offset, match.Length).Insert(match.Offset, replacement);
    }
}
