// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Formatting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>Options for <see cref="MaldaFormatter"/>.</summary>
public sealed class MaldaFormatOptions
{
    public int IndentSize { get; init; } = 4;
    public bool InsertSpaces { get; init; } = true;
    /// <summary>Longest run of blank lines kept between two non-blank lines.</summary>
    public int MaxBlankLines { get; init; } = 1;

    public static MaldaFormatOptions Default { get; } = new();
}

/// <summary>Outcome of <see cref="MaldaFormatter.Format"/>.</summary>
public sealed class MaldaFormatResult
{
    public MaldaFormatResult(string original, string text, string? error)
    {
        Original = original;
        Text = text;
        Error = error;
    }

    public string Original { get; }
    /// <summary>Formatted text, or <see cref="Original"/> when <see cref="Error"/> is set.</summary>
    public string Text { get; }
    /// <summary>Why the source was left unchanged (lexer error, or the safety check refused the output).</summary>
    public string? Error { get; }
    public bool Success => Error == null;
    public bool Changed => !string.Equals(Original, Text, StringComparison.Ordinal);
}

/// <summary>A text replacement with 0-based, end-exclusive line/column positions.</summary>
public sealed class MaldaFormatEdit
{
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string NewText { get; init; } = string.Empty;
}

/// <summary>
/// Conservative, comment-preserving formatter behind <c>malda fmt</c>, <c>malda-lsp</c>
/// formatting and the Desktop IDE "Format Document" command.
/// <para>
/// It works on the lossless <see cref="SourceTriviaScanner"/> stream, one logical line at a
/// time, and never joins or splits lines. It normalizes indentation (brackets, <c>match</c> case
/// bodies, operator continuation lines), spacing between tokens, trailing whitespace, runs of
/// blank lines and the final newline. String literals of every kind (including interpolation
/// holes and triple-quoted prompt bodies) are copied byte for byte.
/// </para>
/// <para>
/// Safety: the output is re-lexed with <see cref="Lexer"/>; if the token stream (types and
/// lexemes) differs from the input in any way, the input is returned unchanged with an error.
/// Sources the lexer rejects are never formatted.
/// </para>
/// </summary>
public static class MaldaFormatter
{
    private const char Bom = '﻿';

    /// <summary>Tokens that always get one space on both sides.</summary>
    private static readonly HashSet<string> AlwaysSpaced = new(StringComparer.Ordinal)
    {
        "=", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "=>", "->", "&&", "||", "??", "|>"
    };

    /// <summary>Operators that are binary when the previous token is an operand.</summary>
    private static readonly HashSet<string> MaybeBinary = new(StringComparer.Ordinal)
    {
        "+", "-", "*", "/", "%", "<", ">"
    };

    private static readonly HashSet<string> ControlKeywords = new(StringComparer.Ordinal)
    {
        "if", "while", "for", "foreach", "catch", "match"
    };

    private static readonly HashSet<string> OperandKeywords = new(StringComparer.Ordinal)
    {
        "this", "self", "super", "true", "false", "null"
    };

    /// <summary>A line starting with one of these continues the previous expression.</summary>
    private static readonly HashSet<string> ContinuationStarts = new(StringComparer.Ordinal)
    {
        ".", "|>", "|", "&&", "||", "??", "and", "or", "?", ":", "=>", "->",
        "+", "*", "/", "%", "==", "!=", "<=", ">=", "="
    };

    /// <summary>A line ending with one of these continues on the next line.</summary>
    private static readonly HashSet<string> ContinuationEnds = new(StringComparer.Ordinal)
    {
        "=", "+=", "-=", "*=", "/=", "==", "!=", "<", ">", "<=", ">=", "&&", "||", "??", "|>",
        "+", "-", "*", "/", "%", "and", "or", "not", "!", "=>", "->", "?", "."
    };

    /// <summary>Format <paramref name="source"/>. Never throws for bad input; see <see cref="MaldaFormatResult.Error"/>.</summary>
    public static MaldaFormatResult Format(string source, MaldaFormatOptions? options = null)
    {
        options ??= MaldaFormatOptions.Default;
        source ??= string.Empty;

        var hasBom = source.Length > 0 && source[0] == Bom;
        var body = hasBom ? source.Substring(1) : source;

        if (!TryLex(body, out var originalTokens, out var lexError))
            return new MaldaFormatResult(source, source, lexError);

        var lines = SourceTriviaScanner.ScanLines(body);
        var formatted = FormatLines(lines, options);
        var newline = lines.Select(l => l.Newline).FirstOrDefault(n => n.Length > 0) ?? "\n";

        var sb = new StringBuilder(body.Length + 16);
        var pending = new List<FormattedLine>();
        var seenContent = false;
        var blankRun = 0;
        foreach (var line in formatted)
        {
            if (line.IsBlank)
            {
                if (!seenContent)
                    continue;
                blankRun++;
                if (blankRun <= Math.Max(0, options.MaxBlankLines))
                    pending.Add(line);
                continue;
            }

            foreach (var blank in pending)
                sb.Append(blank.Source.Newline.Length > 0 ? blank.Source.Newline : newline);
            pending.Clear();
            blankRun = 0;
            seenContent = true;
            sb.Append(line.Text);
            sb.Append(line.Source.Newline.Length > 0 ? line.Source.Newline : newline);
        }

        var text = sb.ToString();
        if (!SameTokens(originalTokens, text, out var mismatch))
            return new MaldaFormatResult(source, source, "formatter safety check failed (token stream changed" + mismatch + "); file left unchanged");

        if (hasBom)
            text = Bom + text;
        return new MaldaFormatResult(source, text, null);
    }

    /// <summary>True when <paramref name="source"/> is already formatted (and lexes).</summary>
    public static bool IsFormatted(string source, MaldaFormatOptions? options = null)
    {
        var result = Format(source, options);
        return result.Success && !result.Changed;
    }

    /// <summary>
    /// Whole-document edits for an editor: one replacement covering the changed lines
    /// (common leading and trailing lines are left alone so the caret does not jump).
    /// Empty when the source is formatted or cannot be formatted.
    /// </summary>
    public static List<MaldaFormatEdit> GetDocumentEdits(string source, MaldaFormatOptions? options = null)
    {
        var result = Format(source, options);
        if (!result.Success || !result.Changed)
            return new List<MaldaFormatEdit>();
        return new List<MaldaFormatEdit> { DiffLines(source, result.Text) };
    }

    /// <summary>
    /// Edits for the logical lines touching 0-based physical lines
    /// <paramref name="startLine"/>..<paramref name="endLineInclusive"/>. Blank lines are not
    /// collapsed in range mode. Empty when the source cannot be formatted.
    /// </summary>
    public static List<MaldaFormatEdit> GetRangeEdits(string source, int startLine, int endLineInclusive, MaldaFormatOptions? options = null)
    {
        options ??= MaldaFormatOptions.Default;
        var edits = new List<MaldaFormatEdit>();
        source ??= string.Empty;
        var bomOffset = source.Length > 0 && source[0] == Bom ? 1 : 0;
        var body = bomOffset == 1 ? source.Substring(1) : source;
        if (!TryLex(body, out var originalTokens, out _))
            return edits;

        var lines = SourceTriviaScanner.ScanLines(body);
        var formatted = FormatLines(lines, options);
        var newText = new StringBuilder();
        foreach (var line in formatted)
        {
            var first = line.Source.FirstLine - 1;
            var last = first + line.Source.PhysicalLineCount - 1;
            var original = line.Source.Text;
            var replacement = line.IsBlank ? string.Empty : line.Text;
            var inRange = last >= startLine && first <= endLineInclusive;
            if (inRange && !string.Equals(original, replacement, StringComparison.Ordinal))
            {
                var lastNewline = original.LastIndexOf('\n');
                var endColumn = lastNewline < 0 ? original.Length : original.Length - lastNewline - 1;
                edits.Add(new MaldaFormatEdit
                {
                    StartLine = first,
                    StartColumn = first == 0 ? bomOffset : 0,
                    EndLine = last,
                    EndColumn = (lastNewline < 0 && first == 0 ? bomOffset : 0) + endColumn,
                    NewText = replacement
                });
                newText.Append(replacement);
            }
            else
            {
                newText.Append(original);
            }

            newText.Append(line.Source.Newline);
        }

        if (edits.Count > 0 && !SameTokens(originalTokens, newText.ToString(), out _))
            edits.Clear();
        return edits;
    }

    /// <summary>Apply <paramref name="edits"/> (non-overlapping) to <paramref name="text"/>.</summary>
    public static string ApplyEdits(string text, IEnumerable<MaldaFormatEdit> edits)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lineStarts.Add(i + 1);
        }

        int Offset(int line, int column)
        {
            if (line >= lineStarts.Count)
                return text.Length;
            return Math.Min(text.Length, lineStarts[line] + column);
        }

        foreach (var edit in edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            var start = Offset(edit.StartLine, edit.StartColumn);
            var end = Offset(edit.EndLine, edit.EndColumn);
            text = text.Substring(0, start) + edit.NewText + text.Substring(end);
        }

        return text;
    }

    private sealed class FormattedLine
    {
        public FormattedLine(SourceLine source, string text, bool isBlank)
        {
            Source = source;
            Text = text;
            IsBlank = isBlank;
        }

        public SourceLine Source { get; }
        public string Text { get; }
        public bool IsBlank { get; }
    }

    private sealed class Frame
    {
        public Frame(string opener, int indent, bool isMatch)
        {
            Opener = opener;
            Indent = indent;
            IsMatch = isMatch;
        }

        public string Opener { get; }
        public int Indent { get; }
        public bool IsMatch { get; }
        /// <summary>The <c>(</c> right after <c>if</c> / <c>while</c> / <c>for</c> / <c>foreach</c> / <c>catch</c> / <c>match</c>.</summary>
        public bool IsCondition { get; init; }
        /// <summary>Inside a <c>match</c> block after a <c>case …:</c> / <c>default:</c> line that ends with <c>:</c>.</summary>
        public bool CaseBody { get; set; }
    }

    private static List<FormattedLine> FormatLines(List<SourceLine> lines, MaldaFormatOptions options)
    {
        var result = new List<FormattedLine>(lines.Count);
        var stack = new List<Frame>();
        TriviaToken? previousLast = null;

        foreach (var line in lines)
        {
            var significant = new List<TriviaToken>();
            var gaps = new List<string>();
            var leading = string.Empty;
            var gap = string.Empty;
            foreach (var token in line.Tokens)
            {
                if (token.Kind == TriviaKind.Whitespace)
                {
                    if (significant.Count == 0)
                        leading += token.Text;
                    else
                        gap += token.Text;
                    continue;
                }

                significant.Add(token);
                gaps.Add(gap);
                gap = string.Empty;
            }

            if (significant.Count == 0)
            {
                result.Add(new FormattedLine(line, string.Empty, isBlank: true));
                continue;
            }

            var code = significant.Where(t => t.IsCode).ToList();
            var first = significant[0];
            var top = stack.Count > 0 ? stack[^1] : null;
            var startsWithCase = first.Kind == TriviaKind.Word && first.Text is "case" or "default";

            int indent;
            if (first.IsCode && IsCloser(first))
            {
                indent = top?.Indent ?? 0;
            }
            else
            {
                indent = top == null ? 0 : top.Indent + 1;
                // A wrapped `if (a ||` / `while (` condition sits one level deeper than the body.
                if (top is { IsCondition: true })
                    indent++;
                if (top is { CaseBody: true } && !startsWithCase)
                    indent++;
                // Wrapped operator lines get one extra level at statement level. Inside ( ) and [ ]
                // the bracket already indents them, so wrapped operands line up with sibling arguments.
                if ((top == null || top.Opener == "{") && IsContinuation(previousLast, first.IsCode ? first : null))
                    indent++;
            }

            var depthBefore = stack.Count;
            var lineHasMatch = false;
            // Openers after a closer that ends an earlier line's bracket (`b) {` closing a wrapped
            // `if (a ||`) belong to the statement that bracket started, not to this wrapped line.
            var pushIndent = indent;
            for (var i = 0; i < code.Count; i++)
            {
                var token = code[i];
                if (token.Kind == TriviaKind.Word && token.Text == "match" && (i == 0 || !code[i - 1].Is(".")))
                    lineHasMatch = true;
                if (IsOpener(token))
                {
                    var isCondition = token.Text == "(" && i > 0 && code[i - 1].Kind == TriviaKind.Word
                        && ControlKeywords.Contains(code[i - 1].Text) && (i < 2 || !code[i - 2].Is("."));
                    stack.Add(new Frame(token.Text, pushIndent, token.Text == "{" && lineHasMatch) { IsCondition = isCondition });
                }
                else if (IsCloser(token) && stack.Count > 0)
                {
                    pushIndent = Math.Min(pushIndent, stack[^1].Indent);
                    stack.RemoveAt(stack.Count - 1);
                }
            }

            if (startsWithCase && depthBefore > 0 && stack.Count >= depthBefore)
            {
                var frame = stack[depthBefore - 1];
                if (frame.IsMatch)
                    frame.CaseBody = stack.Count == depthBefore && code.Count > 0 && code[^1].Is(":");
            }

            var keepColumnZero = first.Kind == TriviaKind.LineComment && first.Column == 1 && IsSectionMarker(first.Text);
            var indentText = keepColumnZero
                ? string.Empty
                : options.InsertSpaces
                    ? new string(' ', indent * Math.Max(1, options.IndentSize))
                    : new string('\t', indent);

            // `import { x } from malda-demo-math`: package names are bare words joined by '-'.
            var moduleDirective = code.Count > 0 && code[0].Kind == TriviaKind.Word
                && (code[0].Text is "import" or "using" or "include"
                    || (code[0].Text == "export" && code.Any(t => t.Kind == TriviaKind.Word && t.Text == "from")));

            var sb = new StringBuilder(indentText);
            for (var i = 0; i < significant.Count; i++)
            {
                var token = significant[i];
                if (i > 0)
                    sb.Append(Separator(significant, i, gaps[i], moduleDirective));

                if (token.Kind == TriviaKind.LineComment)
                    sb.Append(token.Text.TrimEnd(' ', '\t'));
                else if (token.Kind == TriviaKind.BlockComment && i == 0 && token.Text.IndexOf('\n') >= 0)
                    sb.Append(ShiftBlockComment(token.Text, indentText.Length - leading.Length));
                else
                    sb.Append(token.Text);
            }

            result.Add(new FormattedLine(line, sb.ToString(), isBlank: false));
            if (code.Count > 0)
                previousLast = code[^1];
        }

        return result;
    }

    private static string Separator(List<TriviaToken> tokens, int index, string gap, bool moduleDirective)
    {
        var prev = tokens[index - 1];
        var next = tokens[index];
        var hadGap = gap.Length > 0;

        if (next.Kind == TriviaKind.LineComment)
            return hadGap ? gap : string.Empty;
        if (next.Kind == TriviaKind.BlockComment || prev.IsComment)
            return hadGap ? " " : string.Empty;

        if (prev.Is("(") || prev.Is("["))
            return string.Empty;
        if (next.Is(")") || next.Is("]") || next.Is(",") || next.Is(";"))
            return string.Empty;
        if (prev.Is(",") || prev.Is(";"))
            return " ";
        if ((prev.Kind == TriviaKind.Punct && AlwaysSpaced.Contains(prev.Text))
            || (next.Kind == TriviaKind.Punct && AlwaysSpaced.Contains(next.Text)))
            return " ";
        if (!moduleDirective && (IsBinary(tokens, index) || IsBinary(tokens, index - 1)))
            return " ";
        if (next.Is("{") && (prev.Is(")") || prev.Kind == TriviaKind.Word))
            return " ";
        if (prev.Is("}") && next.Kind == TriviaKind.Word)
            return " ";
        if (next.Is("(") && prev.Kind == TriviaKind.Word && ControlKeywords.Contains(prev.Text)
            && (index < 2 || !tokens[index - 2].Is(".")))
            return " ";
        return hadGap ? " " : string.Empty;
    }

    /// <summary>An arithmetic / comparison operator whose left neighbour on the same line is an operand.</summary>
    private static bool IsBinary(List<TriviaToken> tokens, int index)
    {
        var token = tokens[index];
        if (token.Kind != TriviaKind.Punct || !MaybeBinary.Contains(token.Text))
            return false;
        var left = index - 1;
        while (left >= 0 && tokens[left].IsComment)
            left--;
        if (left < 0)
            return false;
        var before = left - 1;
        while (before >= 0 && tokens[before].IsComment)
            before--;
        return IsOperand(tokens[left], before >= 0 ? tokens[before] : null);
    }

    private static bool IsOperand(TriviaToken token, TriviaToken? before)
    {
        switch (token.Kind)
        {
            case TriviaKind.Number:
            case TriviaKind.String:
                return true;
            case TriviaKind.Punct:
                return token.Text is ")" or "]";
            case TriviaKind.Word:
                if (before != null && before.Is("."))
                    return true;
                return !Lexer.IsReservedWord(token.Text) || OperandKeywords.Contains(token.Text);
            default:
                return false;
        }
    }

    private static bool IsContinuation(TriviaToken? previousLast, TriviaToken? first)
    {
        if (previousLast == null)
            return false;
        if (IsOpener(previousLast) || previousLast.Is(",") || previousLast.Is(";"))
            return false;
        if (first != null && (first.Kind == TriviaKind.Punct || first.Kind == TriviaKind.Word) && ContinuationStarts.Contains(first.Text))
            return true;
        return (previousLast.Kind == TriviaKind.Punct || previousLast.Kind == TriviaKind.Word)
            && ContinuationEnds.Contains(previousLast.Text);
    }

    private static bool IsOpener(TriviaToken token) =>
        token.Kind == TriviaKind.Punct && token.Text is "(" or "[" or "{";

    private static bool IsCloser(TriviaToken token) =>
        token.Kind == TriviaKind.Punct && token.Text is ")" or "]" or "}";

    private static bool IsSectionMarker(string comment)
    {
        var rest = comment.Substring(2).TrimStart();
        return rest.StartsWith("@malda-section", StringComparison.Ordinal);
    }

    /// <summary>Move the continuation lines of a leading block comment by the indent change of its first line.</summary>
    private static string ShiftBlockComment(string text, int delta)
    {
        if (delta == 0)
            return text;
        var parts = text.Split('\n');
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Trim().Length == 0)
                continue;
            if (delta > 0)
            {
                parts[i] = new string(' ', delta) + part;
            }
            else
            {
                var remove = 0;
                while (remove < -delta && remove < part.Length && part[remove] is ' ' or '\t')
                    remove++;
                parts[i] = part.Substring(remove);
            }
        }

        return string.Join("\n", parts);
    }

    private static bool TryLex(string source, out List<Token> tokens, out string? error)
    {
        try
        {
            tokens = new Lexer(source).Tokenize();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            tokens = new List<Token>();
            error = ex.Message;
            return false;
        }
    }

    private static bool SameTokens(List<Token> original, string formatted, out string mismatch)
    {
        mismatch = string.Empty;
        if (!TryLex(formatted, out var after, out var error))
        {
            mismatch = ": " + error;
            return false;
        }

        if (after.Count != original.Count)
        {
            mismatch = $": {original.Count} tokens became {after.Count}";
            return false;
        }

        for (var i = 0; i < original.Count; i++)
        {
            if (original[i].Type != after[i].Type || !string.Equals(original[i].Lexeme, after[i].Lexeme, StringComparison.Ordinal))
            {
                mismatch = $": token {i} '{original[i].Lexeme}' at line {original[i].Line} became '{after[i].Lexeme}'";
                return false;
            }
        }

        return true;
    }

    private static MaldaFormatEdit DiffLines(string original, string formatted)
    {
        var a = SplitKeepingNewlines(original);
        var b = SplitKeepingNewlines(formatted);
        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix])
            suffix++;

        int endLine;
        int endColumn;
        if (a.Count - suffix >= a.Count)
        {
            endLine = 0;
            endColumn = 0;
            for (var i = 0; i < original.Length; i++)
            {
                if (original[i] == '\n')
                {
                    endLine++;
                    endColumn = 0;
                }
                else
                {
                    endColumn++;
                }
            }
        }
        else
        {
            endLine = a.Count - suffix;
            endColumn = 0;
        }

        return new MaldaFormatEdit
        {
            StartLine = prefix,
            StartColumn = 0,
            EndLine = endLine,
            EndColumn = endColumn,
            NewText = string.Concat(b.Skip(prefix).Take(b.Count - prefix - suffix))
        };
    }

    private static List<string> SplitKeepingNewlines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(text.Substring(start, i + 1 - start));
                start = i + 1;
            }
        }

        if (start < text.Length)
            lines.Add(text.Substring(start));
        return lines;
    }
}
