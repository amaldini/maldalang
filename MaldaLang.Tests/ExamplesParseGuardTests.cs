// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guard: every <c>Examples/**/*.malda</c> must parse (lexer + parser, with <c>include</c>
/// resolved relative to the file) — the same parse step <c>malda check</c> runs through
/// <c>LanguageService.GetDiagnostics</c>. Warnings and semantic diagnostics are out of scope,
/// and no program is executed.
/// </summary>
public class ExamplesParseGuardTests
{
    /// <summary>
    /// Examples that intentionally contain parse errors (repo-relative, forward slashes).
    /// Empty: no example is a deliberate syntax-error demo today. Add an entry with a comment
    /// saying why, rather than weakening the guard.
    /// </summary>
    private static readonly HashSet<string> IntentionallyBroken = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    /// <summary>
    /// Build/publish/runtime output folders that may sit under Examples locally. Dot-folders
    /// (e.g. <c>.malda-play</c> staging copies) are skipped too.
    /// </summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "dist", "publish", "runtimes", "node_modules"
    };

    [Fact]
    public void EveryExample_Parses()
    {
        var root = PlanningPaths.RepoRoot;
        var examplesDir = Path.Combine(root, "Examples");
        var files = EnumerateExamples(examplesDir).ToList();
        Assert.True(files.Count > 100, $"Expected the Examples catalog, found only {files.Count} .malda files under {examplesDir}.");

        var failures = files
            .AsParallel()
            .Where(f => !IntentionallyBroken.Contains(RelativePath(root, f)))
            .SelectMany(f => ParseErrors(File.ReadAllText(f), f)
                .Select(e => $"{RelativePath(root, e.File ?? f)}:{e.Line}:{e.Column}: {e.Message}"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} parse error(s) in Examples ({files.Count} files checked):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void AllowList_EntriesStillExistAndStillFail()
    {
        var root = PlanningPaths.RepoRoot;
        foreach (var relative in IntentionallyBroken)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Allow-listed example no longer exists: {relative}");
            Assert.NotEmpty(ParseErrors(File.ReadAllText(path), path));
        }
    }

    [Fact]
    public void ParseErrors_ReportsBrokenSource()
    {
        var errors = ParseErrors("function f( {\n  print(1)\n}\n", "broken.malda");
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.True(e.Line > 0));
    }

    [Fact]
    public void ParseErrors_ReportsBrokenIncludedFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "malda_examples_guard_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib.malda"), "function broken( {\n");
            var main = Path.Combine(dir, "main.malda");
            const string source = "include \"lib.malda\";\nprint(\"hi\");\n";
            File.WriteAllText(main, source);

            Assert.NotEmpty(ParseErrors(source, main));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ParseErrors_AcceptsValidSourceAndIgnoresWarnings()
    {
        Assert.Empty(ParseErrors("var x = 1;\nprint(x);\n", "ok.malda"));
    }

    internal sealed record ParseError(string? File, int Line, int Column, string Message);

    /// <summary>
    /// Lexer + parser errors for <paramref name="source"/>, with includes resolved relative to
    /// <paramref name="path"/>. Mirrors the parse half of <c>LanguageService.GetDiagnostics</c>.
    /// </summary>
    internal static List<ParseError> ParseErrors(string source, string path)
    {
        try
        {
            var tokens = new Lexer(source, path).Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, path);
            parser.Parse();
            return parser.Errors.Select(FromException).ToList();
        }
        catch (ParseException ex)
        {
            return [FromException(ex)];
        }
        catch (Exception ex)
        {
            return [new ParseError(path, 0, 0, $"{ex.GetType().Name}: {ex.Message}")];
        }
    }

    private static ParseError FromException(ParseException ex)
    {
        // Drop the "Parse error in <file> at line X, column Y: " prefix, as LanguageService does.
        var message = ex.Message;
        var colon = message.LastIndexOf(": ", StringComparison.Ordinal);
        if (colon >= 0 && colon < message.Length - 2)
            message = message[(colon + 2)..];
        return new ParseError(ex.SourceFileName, ex.Line, ex.Column, message);
    }

    private static IEnumerable<string> EnumerateExamples(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.malda"))
            yield return file;

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') || SkippedDirectories.Contains(name))
                continue;
            foreach (var file in EnumerateExamples(sub))
                yield return file;
        }
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
}
