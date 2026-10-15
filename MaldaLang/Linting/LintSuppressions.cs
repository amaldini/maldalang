// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Linting;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MaldaLang.Formatting;

/// <summary>
/// Comment directives read from the lossless <see cref="SourceTriviaScanner"/> scan:
/// <list type="bullet">
/// <item><c>// malda-lint-disable-next-line ML0001, ML0004</c> — the next physical line</item>
/// <item><c>// malda-lint-disable-line ML0001</c> — the line the comment is on (trailing comment)</item>
/// <item><c>// malda-lint-disable-file ML0003</c> — the whole file</item>
/// </list>
/// <c>malda-style</c> as a code keeps <c>malda lint --fix</c> from rewriting a deprecated alias there.
/// Without codes a directive silences every rule. Text after the codes (e.g. <c>-- reason</c>) is ignored.
/// Block comments work too. A directive inside a string literal is not a comment and is ignored.
/// </summary>
public sealed class LintSuppressions
{
    private static readonly Regex Directive = new(
        @"malda-lint-disable-(?<kind>next-line|line|file)\b(?<rest>[^\r\n]*)",
        RegexOptions.CultureInvariant);

    /// <summary>An <c>ML0xxx</c> rule, or <c>malda-style</c> (the deprecated-alias rewrite of <c>malda lint --fix</c>).</summary>
    private static readonly Regex Code = new(@"^(ML\d{4}|malda-style)$", RegexOptions.CultureInvariant);

    private const string AllRules = "*";

    private readonly Dictionary<int, HashSet<string>> _byLine = new();
    private readonly HashSet<string> _file = new(StringComparer.Ordinal);

    public static LintSuppressions Parse(string source)
    {
        var result = new LintSuppressions();
        List<TriviaToken> tokens;
        try
        {
            tokens = SourceTriviaScanner.Scan(source);
        }
        catch
        {
            return result;
        }

        foreach (var token in tokens)
        {
            if (!token.IsComment)
                continue;

            foreach (Match match in Directive.Matches(token.Text))
            {
                var codes = ReadCodes(match.Groups["rest"].Value);
                // Line of the directive itself (a block comment can span lines).
                var directiveLine = token.Line + CountNewlines(token.Text, match.Index);
                var commentEndLine = token.Line + CountNewlines(token.Text, token.Text.Length);
                switch (match.Groups["kind"].Value)
                {
                    case "file":
                        result._file.UnionWith(codes);
                        break;
                    case "line":
                        result.Add(directiveLine, codes);
                        break;
                    default:
                        result.Add(commentEndLine + 1, codes);
                        break;
                }
            }
        }

        return result;
    }

    public bool IsSuppressed(string code, int line)
    {
        if (_file.Contains(AllRules) || _file.Contains(code))
            return true;
        return _byLine.TryGetValue(line, out var codes) && (codes.Contains(AllRules) || codes.Contains(code));
    }

    private void Add(int line, IEnumerable<string> codes)
    {
        if (!_byLine.TryGetValue(line, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _byLine[line] = set;
        }

        set.UnionWith(codes);
    }

    private static List<string> ReadCodes(string rest)
    {
        var codes = new List<string>();
        foreach (var part in rest.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = part.TrimEnd('*', '/');
            if (!Code.IsMatch(candidate))
                break;
            codes.Add(candidate);
            if (candidate.Length != part.Length)
                break;
        }

        if (codes.Count == 0)
            codes.Add(AllRules);
        return codes;
    }

    private static int CountNewlines(string text, int end)
    {
        var count = 0;
        for (var i = 0; i < end && i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }

        return count;
    }
}
