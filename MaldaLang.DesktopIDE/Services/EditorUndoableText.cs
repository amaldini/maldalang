// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using ICSharpCode.AvalonEdit.Document;
using MaldaLang.IDE.Models;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Applies editor buffer changes through <see cref="TextDocument"/> so AvalonEdit
/// can undo them. Do not assign <c>TextEditor.Text</c> for in-place edits: AvalonEdit
/// 6.3 calls <c>UndoStack.ClearAll()</c> in that setter.
/// </summary>
public static class EditorUndoableText
{
    public static void ReplaceAll(TextDocument document, string newText)
    {
        ArgumentNullException.ThrowIfNull(document);
        newText ??= string.Empty;
        if (string.Equals(document.Text, newText, StringComparison.Ordinal))
        {
            return;
        }

        document.Replace(0, document.TextLength, newText);
    }

    public static int ApplyEdits(TextDocument document, IReadOnlyList<TextEditInfo> edits)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (edits == null || edits.Count == 0)
        {
            return 0;
        }

        var orderedEdits = edits
            .OrderByDescending(edit => edit.Span.Line)
            .ThenByDescending(edit => edit.Span.Column)
            .ToList();

        var applied = 0;
        document.BeginUpdate();
        try
        {
            foreach (var edit in orderedEdits)
            {
                if (!TryGetOffsetFromLocation(document, edit.Span.Line, edit.Span.Column, out var startOffset))
                {
                    continue;
                }

                var maxLength = Math.Max(0, document.TextLength - startOffset);
                var replaceLength = Math.Min(edit.Span.Length, maxLength);
                document.Replace(startOffset, replaceLength, edit.NewText ?? string.Empty);
                applied++;
            }
        }
        finally
        {
            document.EndUpdate();
        }

        return applied;
    }

    public static bool TryGetOffsetFromLocation(TextDocument document, int zeroBasedLine, int zeroBasedColumn, out int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        offset = 0;
        var lineNumber = zeroBasedLine + 1;
        if (lineNumber <= 0 || lineNumber > document.LineCount)
        {
            return false;
        }

        try
        {
            var line = document.GetLineByNumber(lineNumber);
            offset = Math.Min(line.Offset + Math.Max(0, zeroBasedColumn), line.EndOffset);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
