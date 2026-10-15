// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Formatting;

using System.Collections.Generic;
using System.Text;

/// <summary>Kind of a lossless source piece produced by <see cref="SourceTriviaScanner"/>.</summary>
public enum TriviaKind
{
    /// <summary>Spaces, tabs and lone <c>\r</c> between tokens on one line.</summary>
    Whitespace,
    /// <summary><c>\n</c> or <c>\r\n</c>.</summary>
    Newline,
    /// <summary><c>// …</c> up to (not including) the line break.</summary>
    LineComment,
    /// <summary><c>/* … */</c>, possibly spanning lines (unterminated runs to end of file, like the lexer).</summary>
    BlockComment,
    /// <summary>Identifier, keyword or <c>_</c>.</summary>
    Word,
    /// <summary>Integer or float literal.</summary>
    Number,
    /// <summary>Any string literal: <c>"…"</c>, <c>'…'</c>, <c>"""…"""</c>, <c>$"…"</c>, <c>$"""…"""</c>. Opaque: never rewritten.</summary>
    String,
    /// <summary>Operator or punctuation, with the same longest-match rules as <see cref="Lexer"/>.</summary>
    Punct,
    /// <summary>A character the lexer rejects (or an unterminated literal).</summary>
    Error
}

/// <summary>
/// One piece of source text. Concatenating <see cref="Text"/> of every token returned by
/// <see cref="SourceTriviaScanner.Scan"/> reproduces the input exactly.
/// </summary>
public sealed class TriviaToken
{
    public TriviaToken(TriviaKind kind, string text, int start, int line, int column)
    {
        Kind = kind;
        Text = text;
        Start = start;
        Line = line;
        Column = column;
    }

    public TriviaKind Kind { get; }
    public string Text { get; }
    /// <summary>0-based offset in the source.</summary>
    public int Start { get; }
    /// <summary>1-based line of the first character.</summary>
    public int Line { get; }
    /// <summary>1-based column of the first character.</summary>
    public int Column { get; }

    public int End => Start + Text.Length;
    public bool IsComment => Kind is TriviaKind.LineComment or TriviaKind.BlockComment;
    /// <summary>True for tokens the parser sees (not whitespace, newlines or comments).</summary>
    public bool IsCode => Kind is TriviaKind.Word or TriviaKind.Number or TriviaKind.String or TriviaKind.Punct or TriviaKind.Error;
    public bool IsTrivia => Kind is TriviaKind.Whitespace or TriviaKind.Newline;
    public bool Is(string text) => (Kind == TriviaKind.Punct || Kind == TriviaKind.Word) && Text == text;

    public override string ToString() => $"{Kind} '{Text}' @{Line}:{Column}";
}

/// <summary>
/// A logical source line: the tokens between two line breaks. A multi-line string or block
/// comment belongs to the line where it starts, so one logical line can span several
/// physical lines (<see cref="PhysicalLineCount"/>).
/// </summary>
public sealed class SourceLine
{
    public SourceLine(int firstLine, IReadOnlyList<TriviaToken> tokens, string newline)
    {
        FirstLine = firstLine;
        Tokens = tokens;
        Newline = newline;
    }

    /// <summary>1-based physical line where this logical line starts.</summary>
    public int FirstLine { get; }
    /// <summary>Every token on the line including whitespace, excluding the terminating newline.</summary>
    public IReadOnlyList<TriviaToken> Tokens { get; }
    /// <summary>The terminating <c>\n</c> / <c>\r\n</c>, or empty for the last line without one.</summary>
    public string Newline { get; }

    public int PhysicalLineCount
    {
        get
        {
            var count = 1;
            foreach (var token in Tokens)
            {
                foreach (var ch in token.Text)
                {
                    if (ch == '\n')
                        count++;
                }
            }

            return count;
        }
    }

    /// <summary>Tokens other than whitespace (code and comments).</summary>
    public IEnumerable<TriviaToken> Significant
    {
        get
        {
            foreach (var token in Tokens)
            {
                if (token.Kind != TriviaKind.Whitespace)
                    yield return token;
            }
        }
    }

    public bool IsBlank
    {
        get
        {
            foreach (var token in Tokens)
            {
                if (token.Kind != TriviaKind.Whitespace)
                    return false;
            }

            return true;
        }
    }

    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var token in Tokens)
                sb.Append(token.Text);
            return sb.ToString();
        }
    }
}

/// <summary>
/// Lossless scanner for MALDA source: keeps whitespace, newlines and comments as trivia that
/// <see cref="Lexer"/> discards. Token boundaries mirror <see cref="Lexer"/> (same longest-match
/// operators, same string / interpolation / triple-quote rules) without changing the lexer the
/// parser and interpreter use. Shared by <c>malda fmt</c> and future <c>malda lint</c> rules
/// (comment-based suppressions, trivia-aware checks).
/// </summary>
public static class SourceTriviaScanner
{
    public static List<TriviaToken> Scan(string source)
    {
        var scanner = new Scanner(source);
        return scanner.Run();
    }

    /// <summary>Group <paramref name="tokens"/> into logical lines split at newline tokens.</summary>
    public static List<SourceLine> SplitLines(IReadOnlyList<TriviaToken> tokens)
    {
        var lines = new List<SourceLine>();
        var current = new List<TriviaToken>();
        var firstLine = 1;
        foreach (var token in tokens)
        {
            if (token.Kind == TriviaKind.Newline)
            {
                lines.Add(new SourceLine(firstLine, current, token.Text));
                current = new List<TriviaToken>();
                firstLine = token.Line + 1;
                continue;
            }

            current.Add(token);
        }

        lines.Add(new SourceLine(firstLine, current, string.Empty));
        return lines;
    }

    /// <summary>Scan and group into logical lines.</summary>
    public static List<SourceLine> ScanLines(string source) => SplitLines(Scan(source));

    private sealed class Scanner
    {
        private readonly string _s;
        private int _pos;
        private int _line = 1;
        private int _col = 1;
        private readonly List<TriviaToken> _tokens = new();

        public Scanner(string source) => _s = source;

        public List<TriviaToken> Run()
        {
            while (_pos < _s.Length)
                ScanOne();
            return _tokens;
        }

        private char Peek(int offset = 0) => _pos + offset < _s.Length ? _s[_pos + offset] : '\0';

        private void Emit(TriviaKind kind, int start, int line, int col)
        {
            _tokens.Add(new TriviaToken(kind, _s.Substring(start, _pos - start), start, line, col));
        }

        private void Advance(int count = 1)
        {
            for (var i = 0; i < count && _pos < _s.Length; i++)
            {
                if (_s[_pos] == '\n')
                {
                    _line++;
                    _col = 1;
                }
                else
                {
                    _col++;
                }

                _pos++;
            }
        }

        private void ScanOne()
        {
            var start = _pos;
            var line = _line;
            var col = _col;
            var c = _s[_pos];

            if (c == '\n')
            {
                Advance();
                Emit(TriviaKind.Newline, start, line, col);
                return;
            }

            if (c == '\r' && Peek(1) == '\n')
            {
                Advance(2);
                Emit(TriviaKind.Newline, start, line, col);
                return;
            }

            if (c is ' ' or '\t' or '\r')
            {
                while (_pos < _s.Length && (Peek() is ' ' or '\t' || (Peek() == '\r' && Peek(1) != '\n')))
                    Advance();
                Emit(TriviaKind.Whitespace, start, line, col);
                return;
            }

            if (c == '/' && Peek(1) == '/')
            {
                while (_pos < _s.Length && Peek() != '\n' && !(Peek() == '\r' && Peek(1) == '\n'))
                    Advance();
                Emit(TriviaKind.LineComment, start, line, col);
                return;
            }

            if (c == '/' && Peek(1) == '*')
            {
                Advance(2);
                while (_pos < _s.Length)
                {
                    if (Peek() == '*' && Peek(1) == '/')
                    {
                        Advance(2);
                        break;
                    }

                    Advance();
                }

                Emit(TriviaKind.BlockComment, start, line, col);
                return;
            }

            if (c == '$' && Peek(1) == '"')
            {
                var ok = Peek(2) == '"' && Peek(3) == '"'
                    ? ScanInterpolatedTriple()
                    : ScanInterpolated();
                Emit(ok ? TriviaKind.String : TriviaKind.Error, start, line, col);
                return;
            }

            if (c == '"')
            {
                var ok = Peek(1) == '"' && Peek(2) == '"' ? ScanTriple() : ScanQuoted('"');
                Emit(ok ? TriviaKind.String : TriviaKind.Error, start, line, col);
                return;
            }

            if (c == '\'')
            {
                var ok = ScanQuoted('\'');
                Emit(ok ? TriviaKind.String : TriviaKind.Error, start, line, col);
                return;
            }

            if (IsDigit(c))
            {
                ScanNumber();
                Emit(TriviaKind.Number, start, line, col);
                return;
            }

            if (IsAlpha(c))
            {
                while (IsAlphaNumeric(Peek()))
                    Advance();
                Emit(TriviaKind.Word, start, line, col);
                return;
            }

            var length = OperatorLength(c, Peek(1));
            if (length > 0)
            {
                Advance(length);
                Emit(TriviaKind.Punct, start, line, col);
                return;
            }

            Advance();
            Emit(TriviaKind.Error, start, line, col);
        }

        /// <summary>Mirrors the single- and two-character cases of <c>Lexer.ScanToken</c>.</summary>
        private static int OperatorLength(char c, char next)
        {
            switch (c)
            {
                case '(': case ')': case '{': case '}': case '[': case ']':
                case ',': case '.': case ';': case '@': case '%': case ':':
                    return 1;
                case '+': return next is '+' or '=' ? 2 : 1;
                case '-': return next is '-' or '>' or '=' ? 2 : 1;
                case '*': return next == '=' ? 2 : 1;
                case '/': return next == '=' ? 2 : 1;
                case '?': return next == '?' ? 2 : 1;
                case '=': return next is '>' or '=' ? 2 : 1;
                case '!': return next == '=' ? 2 : 1;
                case '<': return next == '=' ? 2 : 1;
                case '>': return next == '=' ? 2 : 1;
                case '&': return next == '&' ? 2 : 0;
                case '|': return next is '|' or '>' ? 2 : 1;
                default: return 0;
            }
        }

        private void ScanNumber()
        {
            while (IsDigit(Peek()))
                Advance();
            if (Peek() == '.' && IsDigit(Peek(1)))
            {
                Advance();
                while (IsDigit(Peek()))
                    Advance();
            }

            if (Peek() is 'e' or 'E')
            {
                Advance();
                if (Peek() is '+' or '-')
                    Advance();
                while (IsDigit(Peek()))
                    Advance();
            }
        }

        private bool ScanQuoted(char quote)
        {
            Advance(); // opening quote
            while (_pos < _s.Length && Peek() != quote)
            {
                if (Peek() == '\\')
                    Advance(2);
                else
                    Advance();
            }

            if (_pos >= _s.Length)
                return false;
            Advance(); // closing quote
            return true;
        }

        private bool ScanTriple()
        {
            Advance(3);
            while (_pos < _s.Length)
            {
                if (Peek() == '"' && Peek(1) == '"' && Peek(2) == '"')
                {
                    Advance(3);
                    return true;
                }

                Advance();
            }

            return false;
        }

        private bool ScanInterpolated()
        {
            Advance(2); // $"
            while (_pos < _s.Length)
            {
                var ch = Peek();
                if (ch == '\\')
                {
                    Advance(2);
                }
                else if (ch == '{')
                {
                    if (Peek(1) == '{')
                        Advance(2);
                    else if (!ScanInterpolationHole())
                        return false;
                }
                else if (ch == '}')
                {
                    Advance(Peek(1) == '}' ? 2 : 1);
                }
                else if (ch == '"')
                {
                    Advance();
                    return true;
                }
                else
                {
                    Advance();
                }
            }

            return false;
        }

        private bool ScanInterpolatedTriple()
        {
            Advance(4); // $"""
            while (_pos < _s.Length)
            {
                var ch = Peek();
                if (ch == '"' && Peek(1) == '"' && Peek(2) == '"')
                {
                    Advance(3);
                    return true;
                }

                if (ch == '\\')
                {
                    Advance(2);
                }
                else if (ch == '{')
                {
                    if (Peek(1) == '{')
                        Advance(2);
                    else if (!ScanInterpolationHole())
                        return false;
                }
                else if (ch == '}')
                {
                    Advance(Peek(1) == '}' ? 2 : 1);
                }
                else
                {
                    Advance();
                }
            }

            return false;
        }

        /// <summary>At a single <c>{</c>: consume through the matching <c>}</c> like the lexer (quotes are not special).</summary>
        private bool ScanInterpolationHole()
        {
            Advance(); // {
            var depth = 1;
            while (depth > 0 && _pos < _s.Length)
            {
                var ch = Peek();
                if (ch == '\\')
                {
                    Advance(2);
                }
                else if (ch == '{')
                {
                    depth++;
                    Advance();
                }
                else if (ch == '}')
                {
                    depth--;
                    Advance();
                }
                else
                {
                    Advance();
                }
            }

            return depth == 0;
        }

        private static bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        private static bool IsDigit(char c) => c >= '0' && c <= '9';
        private static bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);
    }
}
