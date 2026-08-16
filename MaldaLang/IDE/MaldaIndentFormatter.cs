// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.IDE.Models;

/// <summary>
/// Brace-depth indent formatter shared by Desktop IDE and <c>malda-lsp</c>.
/// </summary>
public static class MaldaIndentFormatter
{
    public static string FormatDocument(string text, int indentSize = 4, bool insertSpaces = true)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var edits = GetIndentEdits(lines, 0, lines.Length, indentSize, insertSpaces);
        return ApplyEdits(text, edits);
    }

    public static List<TextEditInfo> GetIndentEdits(
        string[] lines,
        int startLine,
        int endLine,
        int indentSize = 4,
        bool insertSpaces = true,
        CancellationToken cancellationToken = default)
    {
        if (indentSize <= 0)
        {
            indentSize = 4;
        }

        var edits = new List<TextEditInfo>();
        var depth = 0;
        for (var i = 0; i < startLine && i < lines.Length; i++)
        {
            UpdateDepth(lines[i], ref depth);
        }

        for (var i = startLine; i < endLine && i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[i];
            var trimmed = line.TrimStart();
            var expectedIndent = insertSpaces
                ? (depth * indentSize > 0 ? new string(' ', depth * indentSize) : "")
                : (depth > 0 ? new string('\t', depth) : "");

            var currentLeading = line.Length - trimmed.Length;
            var currentIndent = currentLeading > 0 ? line.Substring(0, currentLeading) : "";
            if (currentIndent != expectedIndent)
            {
                edits.Add(new TextEditInfo
                {
                    Span = new TextSpanInfo
                    {
                        Line = i,
                        Column = 0,
                        Length = currentLeading
                    },
                    NewText = expectedIndent
                });
            }

            UpdateDepth(line, ref depth);
        }

        return edits;
    }

    public static string ApplyEdits(string text, IReadOnlyList<TextEditInfo> edits)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return ApplyEdits(text, edits, newline);
    }

    private static string ApplyEdits(string text, IReadOnlyList<TextEditInfo> edits, string newline)
    {
        if (edits.Count == 0)
        {
            return text;
        }

        var ordered = edits
            .OrderByDescending(edit => edit.Span.Line)
            .ThenByDescending(edit => edit.Span.Column)
            .ToList();

        foreach (var edit in ordered)
        {
            var start = GetOffset(text, edit.Span.Line, edit.Span.Column);
            var maxLength = Math.Max(0, text.Length - start);
            var replaceLength = Math.Min(edit.Span.Length, maxLength);
            text = text.Remove(start, replaceLength).Insert(start, edit.NewText ?? string.Empty);
        }

        return text;
    }

    private static int GetOffset(string text, int zeroBasedLine, int zeroBasedColumn)
    {
        var line = 0;
        var i = 0;
        while (i < text.Length && line < zeroBasedLine)
        {
            if (text[i] == '\n')
            {
                line++;
            }

            i++;
        }

        return Math.Min(i + Math.Max(zeroBasedColumn, 0), text.Length);
    }

    private static void UpdateDepth(string line, ref int depth)
    {
        foreach (var c in line)
        {
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
        }

        if (depth < 0)
        {
            depth = 0;
        }
    }
}
