// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Formatting;
using MaldaLang.Linting;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guard: <c>Examples/**/*.malda</c> and the <c>Templates/</c> sources users copy stay
/// <c>malda fmt</c>-clean and free of deprecated flat aliases that <c>malda lint --fix</c> can
/// rewrite (<c>print(</c> → <c>io.print(</c>, …). Examples are what people and LLMs learn from.
/// File enumeration is <see cref="ExamplesParseGuardTests.EnumerateExamples"/>. A flat-alias call
/// the fixer leaves alone on purpose (the program declares a function or variable of that name)
/// still gets the <c>malda-style</c> warning from <c>malda check</c> but does not fail this guard.
/// To fix a failure: <c>malda lint --fix Examples Templates</c> then <c>malda fmt Examples Templates</c>.
/// </summary>
public class ExamplesStyleGuardTests
{
    private static List<string> Sources()
    {
        var root = PlanningPaths.RepoRoot;
        var files = ExamplesParseGuardTests.EnumerateExamples(Path.Combine(root, "Examples"))
            .Concat(ExamplesParseGuardTests.EnumerateExamples(Path.Combine(root, "Templates")))
            .ToList();
        Assert.True(files.Count > 100, $"Expected the Examples catalog, found only {files.Count} .malda files.");
        return files;
    }

    /// <summary>Scaffolding templates with <c>{{#FLAG}}</c> sections are not MALDA until rendered.</summary>
    private static bool IsMustacheTemplate(string source) => source.Contains("{{#", StringComparison.Ordinal);

    [Fact]
    public void EveryExample_IsFormatted()
    {
        var root = PlanningPaths.RepoRoot;
        var failures = Sources()
            .AsParallel()
            .Select(f => (File: f, Source: File.ReadAllText(f)))
            .Where(f => !IsMustacheTemplate(f.Source))
            .Select(f => (f.File, Result: MaldaFormatter.Format(f.Source)))
            .Where(f => !f.Result.Success || f.Result.Changed)
            .Select(f => ExamplesParseGuardTests.RelativePath(root, f.File) + (f.Result.Success ? "" : ": " + f.Result.Error))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} file(s) need formatting (run: malda fmt Examples Templates):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EveryExample_HasNoFixableDeprecatedAlias()
    {
        var root = PlanningPaths.RepoRoot;
        var sources = Sources()
            .Select(f => (Path: f, Source: File.ReadAllText(f)))
            .Where(f => !IsMustacheTemplate(f.Source))
            .ToList();

        var fixes = FlatAliasFixer.FixFiles(sources);
        var failures = fixes
            .SelectMany(fix => fix.Edits.Select(e =>
                $"{ExamplesParseGuardTests.RelativePath(root, fix.Path)}:{e.Line}:{e.Column}: '{e.OldText}' -> '{e.NewText}'"))
            .ToList();

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} deprecated flat alias call(s) (run: malda lint --fix Examples Templates):{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures.Take(50)));
        Assert.All(fixes, fix => Assert.True(fix.Parsed, "Does not parse: " + fix.Path));
    }
}
