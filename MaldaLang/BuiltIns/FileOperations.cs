// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text;
using System.Text.RegularExpressions;

public static class FileOperations
{
    public static bool ReplaceInFile(string filePath, string oldText, string newText, int contextLines)
    {
        try
        {
            if (!System.IO.File.Exists(filePath))
                return false;

            if (string.IsNullOrEmpty(oldText))
                return false;

            var content = System.IO.File.ReadAllText(filePath);

            if (!TryReplaceInContent(content, oldText, newText, contextLines, out var result))
                return false;

            System.IO.File.WriteAllText(filePath, result);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReplaceInContent(string content, string oldText, string newText, int contextLines, out string result)
    {
        result = content;
        if (string.IsNullOrEmpty(oldText))
            return false;

        if (TryReplaceExact(content, oldText, newText, out result))
            return true;

        if (TryReplaceNormalized(content, oldText, newText, contextLines, out result))
            return true;

        return false;
    }

    private static bool TryReplaceExact(string content, string oldText, string newText, out string result)
    {
        result = content;
        var indices = FindAllIndices(content, oldText, StringComparison.Ordinal);
        if (indices.Count != 1)
            return false;

        var start = indices[0];
        result = content.Substring(0, start) + newText + content.Substring(start + oldText.Length);
        return true;
    }

    private static bool TryReplaceNormalized(string content, string oldText, string newText, int contextLines, out string result)
    {
        result = content;
        var (normalizedContent, normToOrig) = BuildNormalizationMap(content);
        var normalizedOld = NormalizeWhitespace(oldText);
        if (normalizedOld.Length == 0)
            return false;

        var indices = FindAllIndices(normalizedContent, normalizedOld, StringComparison.Ordinal);
        if (indices.Count == 0)
            return false;

        int chosenStart;
        if (indices.Count == 1)
        {
            chosenStart = indices[0];
        }
        else
        {
            var disambiguated = DisambiguateNormalizedMatches(normalizedContent, normalizedOld, indices, contextLines);
            if (disambiguated.Count != 1)
                return false;
            chosenStart = disambiguated[0];
        }

        var normEnd = chosenStart + normalizedOld.Length;
        if (chosenStart < 0 || normEnd > normToOrig.Length)
            return false;

        var origStart = normToOrig[chosenStart];
        var origEnd = normEnd < normToOrig.Length
            ? normToOrig[normEnd]
            : content.Length;

        result = content.Substring(0, origStart) + newText + content.Substring(origEnd);
        return true;
    }

    private static List<int> DisambiguateNormalizedMatches(string normalizedContent, string normalizedOld, List<int> indices, int contextLines)
    {
        if (indices.Count <= 1)
            return indices;

        var keys = new List<string>();
        foreach (var index in indices)
        {
            keys.Add(BuildContextKey(normalizedContent, index, index + normalizedOld.Length, contextLines));
        }

        var unique = new List<int>();
        for (var i = 0; i < indices.Count; i++)
        {
            var isUnique = true;
            for (var j = 0; j < indices.Count; j++)
            {
                if (i != j && keys[i] == keys[j])
                {
                    isUnique = false;
                    break;
                }
            }
            if (isUnique)
                unique.Add(indices[i]);
        }

        return unique;
    }

    private static string BuildContextKey(string normalizedContent, int matchStart, int matchEnd, int contextLines)
    {
        if (contextLines <= 0)
            return normalizedContent.Substring(matchStart, matchEnd - matchStart);

        var before = normalizedContent.Substring(0, matchStart);
        var after = normalizedContent.Substring(matchEnd);
        var beforeLines = before.Split('\n');
        var afterLines = after.Split('\n');

        var beforeContext = beforeLines.Length <= contextLines
            ? before
            : string.Join('\n', beforeLines.Skip(beforeLines.Length - contextLines));

        var afterContext = afterLines.Length <= contextLines
            ? after
            : string.Join('\n', afterLines.Take(contextLines));

        return beforeContext + normalizedContent.Substring(matchStart, matchEnd - matchStart) + afterContext;
    }

    private static List<int> FindAllIndices(string haystack, string needle, StringComparison comparison)
    {
        var indices = new List<int>();
        var start = 0;
        while (start <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, start, comparison);
            if (index < 0)
                break;
            indices.Add(index);
            start = index + (needle.Length > 0 ? needle.Length : 1);
        }
        return indices;
    }

    private static (string Normalized, int[] NormIndexToOrig) BuildNormalizationMap(string content)
    {
        var normalized = new StringBuilder();
        var map = new List<int>();

        var lineStart = 0;
        while (lineStart <= content.Length)
        {
            var lineEnd = lineStart;
            while (lineEnd < content.Length && content[lineEnd] != '\n' && content[lineEnd] != '\r')
                lineEnd++;

            var line = content.Substring(lineStart, lineEnd - lineStart);
            var normalizedLine = Regex.Replace(line.Replace('\t', ' '), @" +", " ");

            for (var i = 0; i < normalizedLine.Length; i++)
            {
                normalized.Append(normalizedLine[i]);
                map.Add(lineStart + Math.Min(i, line.Length));
            }

            if (lineEnd >= content.Length)
                break;

            var newlineLength = 1;
            if (content[lineEnd] == '\r' && lineEnd + 1 < content.Length && content[lineEnd + 1] == '\n')
                newlineLength = 2;

            normalized.Append('\n');
            map.Add(lineEnd);

            lineStart = lineEnd + newlineLength;
        }

        return (normalized.ToString(), map.ToArray());
    }

    private static string NormalizeWhitespace(string text)
    {
        text = text.Replace("\t", " ");
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        var lines = text.Split('\n');
        var normalizedLines = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                normalizedLines.Append('\n');
            normalizedLines.Append(Regex.Replace(lines[i], @" +", " "));
        }

        return normalizedLines.ToString();
    }

    internal static string PreviewEditText(string text, int maxLen = 50)
    {
        var preview = text.Length > maxLen ? text.Substring(0, maxLen) + "..." : text;
        return preview.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    public class FileEdit
    {
        public string OldText { get; set; } = "";
        public string NewText { get; set; } = "";
        public int ContextLines { get; set; } = 3;
    }

    public class EditResult
    {
        public bool Success { get; set; }
        public int Applied { get; set; }
        public int FailedEditIndex { get; set; }
        public int TotalEdits { get; set; }
        public string? Error { get; set; }
    }

    public static EditResult EditFile(string filePath, List<FileEdit> edits)
    {
        var result = new EditResult { Success = false, Applied = 0, TotalEdits = edits.Count };

        try
        {
            if (!System.IO.File.Exists(filePath))
            {
                result.Error = $"File not found: '{filePath}'";
                return result;
            }

            if (edits.Count == 0)
            {
                result.Success = true;
                return result;
            }

            var content = System.IO.File.ReadAllText(filePath);
            var working = content;

            for (var editIndex = 0; editIndex < edits.Count; editIndex++)
            {
                var edit = edits[editIndex];
                if (!TryReplaceInContent(working, edit.OldText, edit.NewText, edit.ContextLines, out var next))
                {
                    result.FailedEditIndex = editIndex + 1;
                    result.Applied = 0;
                    result.Error =
                        $"Edit {editIndex + 1}/{edits.Count} failed: oldText not found (preview: '{PreviewEditText(edit.OldText)}'). " +
                        "No changes were written — re-read the file and use a unique oldText snippet.";
                    return result;
                }

                working = next;
            }

            System.IO.File.WriteAllText(filePath, working);
            result.Applied = edits.Count;
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }
}
