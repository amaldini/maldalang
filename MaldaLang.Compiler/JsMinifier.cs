// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Text;

namespace MaldaLang.Compiler;

/// <summary>
/// <c>--minify</c> for JavaScript output. With esbuild (<c>MALDA_ESBUILD</c>, on <c>PATH</c>, or in a
/// <c>node_modules/.bin</c> above the working or output folder) every file is minified and the
/// program's source map is chained, so it still points at the <c>.malda</c> lines. Without it the
/// runtime files lose comments and indentation (<see cref="StripCommentsAndIndentation"/>) and the
/// program stays as written, keeping its map exact.
/// </summary>
public static class JsMinifier
{
    /// <summary>The esbuild executable to use, or null when none is installed.</summary>
    public static string? FindEsbuild(params string[] startFolders)
    {
        var configured = Environment.GetEnvironmentVariable("MALDA_ESBUILD");
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : null;

        var names = OperatingSystem.IsWindows() ? new[] { "esbuild.cmd", "esbuild.exe" } : new[] { "esbuild" };
        foreach (var start in startFolders.Append(Environment.CurrentDirectory))
        {
            for (var folder = new DirectoryInfo(Path.GetFullPath(start)); folder != null; folder = folder.Parent)
            {
                foreach (var name in names)
                {
                    var candidate = Path.Combine(folder.FullName, "node_modules", ".bin", name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(folder.Trim('"'), name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Minifies <paramref name="path"/> in place with esbuild; with <paramref name="sourceMap"/> it reads
    /// the file's own map and writes the chained one beside it.
    /// </summary>
    public static void MinifyWithEsbuild(string esbuild, string path, bool sourceMap)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = esbuild,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory
        };
        startInfo.ArgumentList.Add(Path.GetFileName(path));
        startInfo.ArgumentList.Add("--minify");
        startInfo.ArgumentList.Add("--legal-comments=none");
        startInfo.ArgumentList.Add("--allow-overwrite");
        startInfo.ArgumentList.Add("--log-level=warning");
        if (sourceMap)
            startInfo.ArgumentList.Add("--sourcemap");
        startInfo.ArgumentList.Add("--outfile=" + Path.GetFileName(path));

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {esbuild}.");
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"esbuild did not finish minifying {Path.GetFileName(path)} in 3 minutes.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"esbuild failed on {Path.GetFileName(path)}: {stderr.Result.Trim()}");
    }

    /// <summary>
    /// Drops comments, indentation, trailing spaces and blank lines, and keeps every other newline,
    /// so automatic semicolon insertion sees the same code. Strings, template literals and regular
    /// expressions are copied as they are. Line comments that start with <c>//#</c> or <c>//@</c>
    /// (source map links) stay.
    /// </summary>
    public static string StripCommentsAndIndentation(string js)
    {
        var output = new StringBuilder(js.Length);
        var line = new StringBuilder();
        // The last significant character written, to tell a regex literal from a division.
        var previous = '\0';
        var previousWord = string.Empty;
        var templateDepth = new Stack<int>(); // brace depth at each `${` of an open template
        var braceDepth = 0;

        void EndLine()
        {
            var text = line.ToString().TrimEnd();
            if (text.Length > 0)
                output.Append(text).Append('\n');
            line.Clear();
        }

        var i = 0;
        while (i < js.Length)
        {
            var c = js[i];
            if (c == '\r')
            {
                i++;
                continue;
            }
            if (c == '\n')
            {
                EndLine();
                i++;
                continue;
            }
            if ((c == ' ' || c == '\t') && (line.Length == 0 || line[^1] == ' '))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < js.Length && js[i + 1] == '/')
            {
                var end = js.IndexOf('\n', i);
                if (end < 0)
                    end = js.Length;
                if (i + 2 < js.Length && (js[i + 2] == '#' || js[i + 2] == '@'))
                    line.Append(js, i, end - i);
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < js.Length && js[i + 1] == '*')
            {
                var end = js.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var comment = end < 0 ? js[i..] : js[i..(end + 2)];
                i = end < 0 ? js.Length : end + 2;
                // A block comment that spanned lines still separates them.
                if (comment.Contains('\n'))
                    EndLine();
                else if (line.Length > 0 && line[^1] != ' ')
                    line.Append(' ');
                continue;
            }

            if (c is '"' or '\'')
            {
                i = CopyQuoted(js, i, c, line);
                previous = c;
                previousWord = string.Empty;
                continue;
            }
            if (c == '`' || (c == '}' && templateDepth.Count > 0 && templateDepth.Peek() == braceDepth))
            {
                if (c == '}')
                    templateDepth.Pop();
                i = CopyTemplate(js, i, line, output, templateDepth, braceDepth);
                previous = '`';
                previousWord = string.Empty;
                continue;
            }
            if (c == '/' && RegexCanStart(previous, previousWord))
            {
                i = CopyRegex(js, i, line);
                previous = '/';
                previousWord = string.Empty;
                continue;
            }

            if (c == '{')
                braceDepth++;
            else if (c == '}')
                braceDepth--;

            line.Append(c);
            if (!char.IsWhiteSpace(c))
            {
                previousWord = char.IsLetterOrDigit(c) || c == '_' || c == '$'
                    ? (char.IsLetterOrDigit(previous) || previous == '_' || previous == '$' ? previousWord + c : c.ToString())
                    : string.Empty;
                previous = c;
            }
            i++;
        }

        EndLine();
        return output.ToString();
    }

    private static readonly HashSet<string> KeywordsBeforeRegex = new(StringComparer.Ordinal)
    {
        "return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case", "do", "else", "yield", "await"
    };

    private static bool RegexCanStart(char previous, string previousWord)
    {
        if (previousWord.Length > 0)
            return KeywordsBeforeRegex.Contains(previousWord);
        return previous == '\0' || "(,=:[!&|?{};+-*%<>~^".IndexOf(previous) >= 0;
    }

    private static int CopyQuoted(string js, int start, char quote, StringBuilder line)
    {
        var i = start + 1;
        while (i < js.Length && js[i] != quote && js[i] != '\n')
        {
            if (js[i] == '\\')
                i++;
            i++;
        }
        var end = Math.Min(js.Length, i + 1);
        line.Append(js, start, end - start);
        return end;
    }

    private static int CopyRegex(string js, int start, StringBuilder line)
    {
        var i = start + 1;
        var inClass = false;
        while (i < js.Length && js[i] != '\n')
        {
            var c = js[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == '[')
                inClass = true;
            else if (c == ']')
                inClass = false;
            else if (c == '/' && !inClass)
                break;
            i++;
        }
        i++;
        while (i < js.Length && char.IsLetter(js[i]))
            i++;
        var end = Math.Min(js.Length, i);
        line.Append(js, start, end - start);
        return end;
    }

    /// <summary>
    /// Copies template text from a backtick (or the <c>}</c> closing a <c>${</c>) up to the closing
    /// backtick or the next <c>${</c>, newlines included: they are part of the string.
    /// </summary>
    private static int CopyTemplate(string js, int start, StringBuilder line, StringBuilder output, Stack<int> templateDepth, int braceDepth)
    {
        var i = start + 1;
        while (i < js.Length)
        {
            var c = js[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == '`')
            {
                i++;
                break;
            }
            if (c == '$' && i + 1 < js.Length && js[i + 1] == '{')
            {
                templateDepth.Push(braceDepth);
                i += 2;
                break;
            }
            i++;
        }
        var end = Math.Min(js.Length, i);
        var text = js[start..end].Replace("\r", string.Empty);
        var lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            line.Append(text);
        }
        else
        {
            // The template's own lines go out untouched; the line after its last newline continues.
            output.Append(line).Append(text, 0, lastNewline + 1);
            line.Clear().Append(text, lastNewline + 1, text.Length - lastNewline - 1);
        }
        return end;
    }
}
