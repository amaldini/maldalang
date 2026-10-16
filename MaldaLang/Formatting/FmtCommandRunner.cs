// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Formatting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MaldaLang.Tooling;

/// <summary>
/// <c>malda fmt</c>: rewrite files in place, <c>--check</c> for CI, <c>--stdin</c> for editors.
/// </summary>
internal sealed class FmtCommandRunner
{
    public const int ExitOk = 0;
    /// <summary><c>--check</c> found unformatted files, or a file could not be formatted.</summary>
    public const int ExitNeedsFormatting = 1;
    public const int ExitUsage = 2;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  malda fmt <file.malda|dir>... [--check] [--indent-size N] [--use-tabs]");
        output.WriteLine("  malda fmt --stdin [--check]");
        output.WriteLine();
        output.WriteLine("Formats MALDA source: indentation, spacing between tokens, trailing whitespace,");
        output.WriteLine("blank-line runs and the final newline. Comments are kept; string literals, interpolation");
        output.WriteLine("and triple-quoted prompt bodies are never changed. A file the lexer rejects is left alone.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --check          Do not write; list unformatted files and exit 1 if any (for CI)");
        output.WriteLine("  --stdin          Read source from stdin and write the formatted source to stdout");
        output.WriteLine("  --indent-size N  Spaces per indent level (default 4)");
        output.WriteLine("  --use-tabs       Indent with tabs");
        output.WriteLine();
        output.WriteLine(MaldaSourceFiles.UsageNote);
        output.WriteLine("Exit codes: 0 ok, 1 unformatted (--check) or a file could not be formatted, 2 usage error.");
    }

    public int Run(string[] args, TextWriter output, TextWriter error, TextReader? stdin = null)
    {
        var check = false;
        var useStdin = false;
        var indentSize = 4;
        var useTabs = false;
        var paths = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                case "help":
                    PrintUsage(output);
                    return ExitOk;
                case "--check":
                    check = true;
                    break;
                case "--stdin":
                    useStdin = true;
                    break;
                case "--use-tabs":
                    useTabs = true;
                    break;
                case "--indent-size":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out indentSize) || indentSize < 1 || indentSize > 16)
                    {
                        error.WriteLine("malda fmt: --indent-size needs a number from 1 to 16");
                        return ExitUsage;
                    }

                    i++;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        error.WriteLine($"malda fmt: unknown option '{arg}'");
                        PrintUsage(error);
                        return ExitUsage;
                    }

                    paths.Add(arg);
                    break;
            }
        }

        var options = new MaldaFormatOptions { IndentSize = indentSize, InsertSpaces = !useTabs };

        if (useStdin)
        {
            if (paths.Count > 0)
            {
                error.WriteLine("malda fmt: --stdin does not take file arguments");
                return ExitUsage;
            }

            var source = (stdin ?? Console.In).ReadToEnd();
            var result = MaldaFormatter.Format(source, options);
            if (!result.Success)
            {
                error.WriteLine($"<stdin>: {result.Error}");
                return ExitNeedsFormatting;
            }

            if (check)
            {
                if (result.Changed)
                {
                    output.WriteLine("<stdin>");
                    return ExitNeedsFormatting;
                }

                return ExitOk;
            }

            output.Write(result.Text);
            output.Flush();
            return ExitOk;
        }

        if (paths.Count == 0)
        {
            error.WriteLine("malda fmt: expected a file, a directory, or --stdin");
            PrintUsage(error);
            return ExitUsage;
        }

        var files = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                files.AddRange(MaldaSourceFiles.Enumerate(path));
            }
            else
            {
                error.WriteLine($"malda fmt: path not found: {path}");
                return ExitUsage;
            }
        }

        var unformatted = 0;
        var failed = 0;
        var rewritten = 0;
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var source = Utf8NoBom.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
            var result = MaldaFormatter.Format(source, options);
            if (!result.Success)
            {
                failed++;
                error.WriteLine($"{file}: {result.Error}");
                continue;
            }

            if (!result.Changed)
                continue;

            unformatted++;
            if (check)
            {
                output.WriteLine(file);
                continue;
            }

            var encoded = Utf8NoBom.GetBytes(result.Text);
            if (hasBom)
                encoded = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(encoded).ToArray();
            File.WriteAllBytes(file, encoded);
            rewritten++;
            output.WriteLine($"formatted {file}");
        }

        if (check)
        {
            if (unformatted > 0)
                error.WriteLine($"{unformatted} of {files.Count} file(s) need formatting (run: malda fmt <path>)");
            return unformatted > 0 || failed > 0 ? ExitNeedsFormatting : ExitOk;
        }

        if (files.Count > 1 || rewritten == 0)
            output.WriteLine($"{rewritten} file(s) formatted, {files.Count - rewritten - failed} already formatted" + (failed > 0 ? $", {failed} failed" : ""));
        return failed > 0 ? ExitNeedsFormatting : ExitOk;
    }
}
