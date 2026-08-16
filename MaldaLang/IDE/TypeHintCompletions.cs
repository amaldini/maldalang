// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.RegularExpressions;

namespace MaldaLang.IDE;

/// <summary>
/// Phase 4.1: completion after ':' (param/var) or '-&gt;' / '=&gt;' (return type).
/// </summary>
public static class TypeHintCompletions
{
    private static readonly Regex ReturnTypePartialRegex = new(
        @"(->|=>)\s*(\w*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex TypeHintColonPartialRegex = new(
        @":\s*(\w*)$",
        RegexOptions.CultureInvariant);

    public static string? GetTypeHintPartialPrefix(string source, int line, int column)
    {
        try
        {
            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;

            var lineText = lines[line];
            if (column < 0)
                column = 0;
            if (column > lineText.Length)
                column = lineText.Length;

            var beforeCursor = lineText.Substring(0, column);
            if (string.IsNullOrWhiteSpace(beforeCursor))
                return null;

            if (IsInsideStringLiteral(beforeCursor))
                return null;

            var returnMatch = ReturnTypePartialRegex.Match(beforeCursor);
            if (returnMatch.Success)
                return returnMatch.Groups[2].Value;

            var colonMatch = TypeHintColonPartialRegex.Match(beforeCursor);
            if (!colonMatch.Success)
                return null;

            var prefixBeforeColon = beforeCursor.Substring(0, colonMatch.Index).TrimEnd();
            if (IsTypeHintColonContext(prefixBeforeColon) ||
                (IsBareSchemaFieldName(prefixBeforeColon) && IsInsideSchemaBody(source, line, column)))
            {
                return colonMatch.Groups[1].Value;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTypeHintColonContext(string prefixBeforeColon)
    {
        if (string.IsNullOrWhiteSpace(prefixBeforeColon))
            return false;

        if (prefixBeforeColon.Contains('"') || prefixBeforeColon.Contains('\''))
            return false;

        if (Regex.IsMatch(prefixBeforeColon, @"\bvar\s+[\w]+$", RegexOptions.CultureInvariant))
            return true;

        if (Regex.IsMatch(prefixBeforeColon, @"\bfunction\s+[\w]+\s*\([^)]*$", RegexOptions.CultureInvariant))
            return true;

        if (Regex.IsMatch(prefixBeforeColon, @"\b(public|private|static)\s+var\s+[\w]+$", RegexOptions.CultureInvariant))
            return true;

        if (Regex.IsMatch(prefixBeforeColon, @"\b(public|private|static)\s+[\w]+$", RegexOptions.CultureInvariant))
            return true;

        return false;
    }

    private static bool IsBareSchemaFieldName(string prefixBeforeColon) =>
        Regex.IsMatch(prefixBeforeColon, @"^\s*\w+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// True when the cursor is inside a <c>schema Name { ... }</c> body so
    /// <c>field:</c> can offer type-hint completions.
    /// </summary>
    private static bool IsInsideSchemaBody(string source, int line, int column)
    {
        var offset = 0;
        var currentLine = 0;
        while (offset < source.Length && currentLine < line)
        {
            if (source[offset] == '\n')
                currentLine++;
            offset++;
        }

        offset = Math.Min(offset + Math.Max(column, 0), source.Length);
        var prefix = source.Substring(0, offset);
        var lastSchema = LastIndexOfKeyword(prefix, "schema");
        if (lastSchema < 0)
            return false;

        var brace = prefix.IndexOf('{', lastSchema);
        if (brace < 0)
            return false;

        var depth = 0;
        for (var i = brace; i < prefix.Length; i++)
        {
            var ch = prefix[i];
            if (ch == '{')
                depth++;
            else if (ch == '}')
                depth--;
        }

        return depth > 0;
    }

    private static int LastIndexOfKeyword(string prefix, string keyword)
    {
        for (var i = prefix.Length - keyword.Length; i >= 0; i--)
        {
            if (!prefix.AsSpan(i, keyword.Length).Equals(keyword, StringComparison.Ordinal))
                continue;
            var beforeOk = i == 0 || !IsIdentifierContinue(prefix[i - 1]);
            var afterOk = i + keyword.Length >= prefix.Length ||
                          !IsIdentifierContinue(prefix[i + keyword.Length]);
            if (beforeOk && afterOk)
                return i;
        }

        return -1;
    }

    private static bool IsIdentifierContinue(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private static bool IsInsideStringLiteral(string text)
    {
        var inDouble = false;
        var inSingle = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && !inSingle)
                inDouble = !inDouble;
            else if (c == '\'' && !inDouble)
                inSingle = !inSingle;
        }

        return inDouble || inSingle;
    }
}
