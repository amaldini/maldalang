// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Statements;
using Lexer = MaldaLang.Lexer;
using Parser = MaldaLang.Parser.Parser;
using Token = MaldaLang.Token;
using TokenType = MaldaLang.TokenType;

/// <summary>
/// REPL line patch: <c>editline</c> replaces one source line of a recorded
/// definition (one line, or several lines from the continuation prompt).
/// <c>insert</c> splices lines after a source line. A one-line
/// <c>function name(...) expr;</c> is wrapped in a block so the new lines
/// sit before the original expression as <c>return</c>.
/// </summary>
public static class ReplSessionPatch
{
    public const string EditLineAction = "editline";
    public const string InsertAction = "insert";
    public const string UsageAction = "patch-usage";

    public const string EditLineUsage =
        "Usage: editline <name> <line> [text]\n" +
        "       Omit text to type the replacement lines; a blank line finishes.";

    public const string InsertUsage =
        "Usage: insert <name> after <line>\n" +
        "       The following lines are inserted; a blank line finishes.";

    private static readonly Regex EditLinePattern = new(
        @"^(?<name><lambda>|[A-Za-z_][A-Za-z0-9_]*)(?:\([^)]*\))?\s+(?<line>\d+)(?:\s+(?<text>.*))?$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex InsertPattern = new(
        @"^(?<name><lambda>|[A-Za-z_][A-Za-z0-9_]*)(?:\([^)]*\))?\s+after\s+(?<line>\d+)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public readonly struct Parse
    {
        public bool IsUsage { get; init; }
        public string Usage { get; init; }
        public string Action { get; init; }
        public string Name { get; init; }
        public int LineNumber { get; init; }
        public string? InlineText { get; init; }
        public bool NeedsBody { get; init; }
    }

    /// <summary>
    /// True when <paramref name="line"/> is an <c>editline</c> or <c>insert</c>
    /// command. Assignments such as <c>insert = 1</c> return false.
    /// </summary>
    public static bool TryParseCommand(string? line, out Parse command)
    {
        command = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        var splitAt = IndexOfWhitespace(trimmed);
        var verb = splitAt < 0 ? trimmed : trimmed.Substring(0, splitAt);
        var rest = splitAt < 0 ? "" : trimmed.Substring(splitAt).TrimStart();

        string usage;
        Regex pattern;
        string action;
        var insert = false;
        if (verb.Equals(EditLineAction, StringComparison.OrdinalIgnoreCase))
        {
            usage = EditLineUsage;
            pattern = EditLinePattern;
            action = EditLineAction;
        }
        else if (verb.Equals(InsertAction, StringComparison.OrdinalIgnoreCase))
        {
            usage = InsertUsage;
            pattern = InsertPattern;
            action = InsertAction;
            insert = true;
        }
        else
        {
            return false;
        }

        if (rest.Length == 0)
        {
            command = Usage(usage);
            return true;
        }

        var first = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        if (!LooksLikeNameToken(first))
            return false;

        var match = pattern.Match(rest);
        if (!match.Success || !int.TryParse(match.Groups["line"].Value, out var lineNumber))
        {
            command = Usage(usage, action, ReplSessionEdit.NormalizeName(first));
            return true;
        }

        var text = match.Groups["text"].Success ? match.Groups["text"].Value : "";
        var needsBody = insert || text.Length == 0;
        command = new Parse
        {
            Action = action,
            Name = match.Groups["name"].Value,
            LineNumber = lineNumber,
            InlineText = needsBody ? null : text,
            NeedsBody = needsBody
        };
        return true;
    }

    public static bool TrySplice(
        string source,
        string action,
        int lineNumber,
        string newText,
        out string patched,
        out string? error)
    {
        patched = "";
        error = null;
        var lines = SplitLines(source);
        if (lines.Count == 0)
        {
            error = "Patch removed the whole definition.";
            return false;
        }

        if (lineNumber < 1 || lineNumber > lines.Count)
        {
            error = "Line " + lineNumber + " is outside the definition (" + lines.Count + " line"
                + (lines.Count == 1 ? "" : "s") + ").";
            return false;
        }

        var added = SplitLines(newText);
        if (action == InsertAction)
        {
            if (added.Count == 0)
            {
                error = "Nothing to insert.";
                return false;
            }

            if (lines.Count == 1
                && lineNumber == 1
                && TryExpandCompactFunction(lines[0], added, out patched))
                return true;

            lines.InsertRange(lineNumber, added);
        }
        else
        {
            lines.RemoveAt(lineNumber - 1);
            lines.InsertRange(lineNumber - 1, added);
            if (lines.Count == 0)
            {
                error = "Patch removed the whole definition.";
                return false;
            }
        }

        patched = string.Join("\n", lines);
        return true;
    }

    public static void Write(
        TextWriter writer,
        string action,
        string name,
        int lineNumber,
        string newText,
        Interpreter interpreter,
        ISet<string>? hostNames,
        ReplSessionSource sessionSource,
        Action<string> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        name = ReplSessionEdit.NormalizeName(name);
        if (string.IsNullOrWhiteSpace(name) || lineNumber < 1)
        {
            writer.WriteLine(action == InsertAction ? InsertUsage : EditLineUsage);
            return;
        }

        if (hostNames != null && hostNames.Contains(name))
        {
            writer.WriteLine("Cannot edit host name '" + name + "'.");
            return;
        }

        var previous = sessionSource.Peek(name);
        if (previous == null)
        {
            writer.WriteLine("No recorded source for '" + name + "'.");
            return;
        }

        if (action == InsertAction && string.IsNullOrWhiteSpace(newText))
        {
            writer.WriteLine("Nothing to insert.");
            return;
        }

        if (!TrySplice(previous, action, lineNumber, newText ?? "", out var patched, out var spliceError))
        {
            writer.WriteLine(spliceError);
            writer.WriteLine(Numbered(previous));
            return;
        }

        if (!TryValidate(patched, name, out var validateError))
        {
            writer.WriteLine("Kept the previous definition.");
            writer.WriteLine(validateError);
            return;
        }

        if (!interpreter.TryDropUserDefinition(
                name, hostNames, out var droppedName, out var label, out var dropError))
        {
            writer.WriteLine(dropError ?? "Cannot update '" + name + "'.");
            return;
        }

        if (string.IsNullOrEmpty(label))
            label = "definition";
        var shownName = string.IsNullOrEmpty(droppedName) ? name : droppedName;
        sessionSource.TryRemove(droppedName);
        if (!string.Equals(droppedName, name, StringComparison.Ordinal))
            sessionSource.TryRemove(name);

        try
        {
            execute(patched);
        }
        catch (Exception ex)
        {
            writer.WriteLine("Kept the previous definition.");
            writer.WriteLine(ex.Message);
            try
            {
                interpreter.TryDropUserDefinition(shownName, hostNames, out _, out _, out _);
                execute(previous);
                sessionSource.Record(previous);
            }
            catch (Exception restoreEx)
            {
                writer.WriteLine(restoreEx.Message);
            }

            return;
        }

        sessionSource.Record(patched);
        writer.WriteLine("Updated " + label + " '" + shownName + "'.");
        if (label == "class")
            writer.WriteLine("Existing instances keep the previous class object.");
        writer.WriteLine(Numbered(sessionSource.Peek(shownName) ?? patched));
    }

    private static bool TryValidate(string source, string expectedName, out string? error)
    {
        List<Statement> statements;
        try
        {
            var tokens = new Lexer(source).Tokenize();
            var parser = new Parser(tokens);
            statements = parser.Parse();
            if (parser.Errors.Count > 0)
            {
                error = parser.Errors[0].Message;
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        var defs = ReplSessionSource.ExtractDefinitions(source, statements);
        if (defs.Count == 0 || defs.Count != statements.Count)
        {
            error = "Patch must stay a definition named '" + expectedName + "'.";
            return false;
        }

        foreach (var def in defs)
        {
            if (!string.Equals(def.Name, expectedName, StringComparison.Ordinal))
            {
                error = "Patch must keep the name '" + expectedName + "'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Wraps <c>function name(...) expr;</c> so <paramref name="inserted"/> runs
    /// before the original expression, which stays the final <c>return</c>.
    /// </summary>
    private static bool TryExpandCompactFunction(string sourceLine, List<string> inserted, out string expanded)
    {
        expanded = "";
        var source = sourceLine.Trim();
        if (source.Length == 0 || source.Contains('\n'))
            return false;

        List<Token> tokens;
        try
        {
            tokens = new Lexer(source).Tokenize();
        }
        catch
        {
            return false;
        }

        if (tokens.Count > 0 && tokens[^1].Type == TokenType.EOF)
            tokens.RemoveAt(tokens.Count - 1);
        if (tokens.Count == 0 || tokens.Any(t => t.Type == TokenType.LeftBrace))
            return false;

        var fn = 0;
        if (tokens[0].Type == TokenType.Export)
            fn = 1;
        if (fn >= tokens.Count || tokens[fn].Type != TokenType.Function)
            return false;

        var close = -1;
        var depth = 0;
        for (var i = fn; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.LeftParen)
                depth++;
            else if (tokens[i].Type == TokenType.RightParen)
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
        }

        if (close < 0 || close + 1 >= tokens.Count || tokens[^1].Type != TokenType.Semicolon)
            return false;

        var exprStart = close + 1;
        if (exprStart < tokens.Count && tokens[exprStart].Type == TokenType.Arrow)
        {
            exprStart++;
            if (exprStart < tokens.Count && tokens[exprStart].Type == TokenType.Identifier)
                exprStart++;
        }

        if (exprStart >= tokens.Count - 1)
            return false;

        var signature = Slice(source, tokens[0], tokens[exprStart - 1]).TrimEnd();
        var expression = Slice(source, tokens[exprStart], tokens[^2]).Trim();
        if (signature.Length == 0 || expression.Length == 0)
            return false;

        var sb = new StringBuilder();
        sb.Append(signature);
        sb.Append(" {\n");
        foreach (var line in inserted)
        {
            if (line.Length == 0)
            {
                sb.Append('\n');
                continue;
            }

            sb.Append(char.IsWhiteSpace(line[0]) ? line : "    " + line);
            sb.Append('\n');
        }

        sb.Append("    return ");
        sb.Append(expression);
        sb.Append(";\n}");
        expanded = sb.ToString();
        return true;
    }

    private static string Slice(string source, Token start, Token endInclusive)
    {
        var startIndex = start.Column - 1;
        var endIndex = endInclusive.Column - 1 + (endInclusive.Lexeme?.Length ?? 0);
        if (startIndex < 0 || endIndex > source.Length || endIndex < startIndex)
            return "";
        return source.Substring(startIndex, endIndex - startIndex);
    }

    /// <summary>
    /// Prints the recorded definition with the 1-based line numbers
    /// <c>editline</c> and <c>insert</c> use. A missing name prints
    /// <paramref name="usage"/>; a known name prints only the listing.
    /// </summary>
    public static void WriteListing(
        TextWriter writer,
        string usage,
        string name,
        ISet<string>? hostNames,
        ReplSessionSource sessionSource)
    {
        name = ReplSessionEdit.NormalizeName(name);
        if (name.Length == 0)
        {
            writer.WriteLine(usage);
            return;
        }

        if (hostNames != null && hostNames.Contains(name))
        {
            writer.WriteLine("Cannot edit host name '" + name + "'.");
            return;
        }

        var previous = sessionSource.Peek(name);
        if (previous == null)
        {
            writer.WriteLine("No recorded source for '" + name + "'.");
            writer.WriteLine(usage);
            return;
        }

        writer.WriteLine(Numbered(previous));
    }

    public static string Numbered(string source)
    {
        var lines = SplitLines(source);
        var width = Math.Max(1, lines.Count.ToString().Length);
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                sb.AppendLine();
            sb.Append((i + 1).ToString().PadLeft(width));
            sb.Append("| ");
            sb.Append(lines[i]);
        }

        return sb.ToString();
    }

    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<string>();

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();
        if (lines.Count > 1 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static Parse Usage(string usage, string action = "", string name = "") =>
        new() { IsUsage = true, Usage = usage, Action = action, Name = name };

    private static int IndexOfWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }

    private static bool LooksLikeNameToken(string token)
    {
        if (token.Length == 0)
            return false;
        if (char.IsLetter(token[0]) || token[0] == '_')
            return true;
        return token.Length >= 3 && token[0] == '<' && char.IsLetter(token[1]);
    }
}
