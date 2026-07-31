// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.RegularExpressions;
using MaldaLang.BuiltIns;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Keeps <c>docs/llm/malda-builtins.tsv</c> aligned with the engine. A signature table an
/// agent cannot trust is worse than no table at all: a row naming a built-in that does not
/// exist teaches the agent to invent it, and a missing row sends it back to the HTML manual.
/// Regenerate with <c>scripts/sync-llm-builtins-tsv.ps1</c>.
/// </summary>
public class LlmBuiltinsTsvGuardTests
{
    private const string RegenerateHint =
        "Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/sync-llm-builtins-tsv.ps1";

    private sealed record BuiltinRow(string Name, string Call, string Arguments, string Notes, string Returns);

    private static IReadOnlyList<BuiltinRow> LoadRows()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "llm", "malda-builtins.tsv");
        var rows = new List<BuiltinRow>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#", StringComparison.Ordinal) || line.Trim().Length == 0)
                continue;

            var fields = line.Split('\t');
            Assert.True(
                fields.Length == 5,
                $"malda-builtins.tsv rows need exactly 5 tab-separated fields, got {fields.Length}: '{line}'");
            rows.Add(new BuiltinRow(fields[0], fields[1], fields[2], fields[3], fields[4]));
        }

        return rows;
    }

    private static IReadOnlySet<string> AnsiConsoleMethodNames()
    {
        var source = File.ReadAllText(
            PlanningPaths.ResolveRepoFile("MaldaLang", "BuiltIns", "AnsiConsoleInstance.cs"));
        var accessor = Regex.Match(
            source,
            @"public override RuntimeValue Get\(.*?throw new Exception",
            RegexOptions.Singleline);
        Assert.True(accessor.Success, "Could not locate AnsiConsoleInstance.Get");

        return Regex.Matches(accessor.Value, @"name == ""([A-Za-z_][A-Za-z0-9_]*)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> KnownEngineSymbols()
    {
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        symbols.UnionWith(BuiltInRegistryInventoryLoader.LoadSymbolsFromRegistrySource());
        symbols.UnionWith(StdLibNamespaces.MathMethodNames);
        symbols.UnionWith(StdLibNamespaces.StrMethodNames);
        symbols.UnionWith(StdLibNamespaces.IoMethodNames);
        symbols.UnionWith(AnsiConsoleMethodNames());
        return symbols;
    }

    [Fact]
    public void EveryRow_NamesASymbolTheEngineActuallyHas()
    {
        var known = KnownEngineSymbols();
        var invented = LoadRows()
            .Select(row => row.Name)
            .Where(name => !known.Contains(name) && !BuiltInRegistry.IsInterpreterBuiltIn(name))
            .ToList();

        Assert.True(
            invented.Count == 0,
            $"malda-builtins.tsv lists symbols the engine does not define: {string.Join(", ", invented)}. {RegenerateHint}");
    }

    [Fact]
    public void EveryEngineSymbol_HasARow()
    {
        var listed = LoadRows().Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var missing = KnownEngineSymbols().Where(name => !listed.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            $"malda-builtins.tsv is missing built-ins: {string.Join(", ", missing)}. {RegenerateHint}");
    }

    private static readonly HashSet<string> ArrayMethodNames =
        new(StringComparer.Ordinal) { "append", "pop", "shift" };

    [Fact]
    public void PreferredCall_UsesTheNamespaceTheLanguageServerRecommends()
    {
        var ansiNames = AnsiConsoleMethodNames();
        var wrong = new List<string>();

        foreach (var row in LoadRows())
        {
            var expected = StdLibNamespaces.MathMethodNames.Contains(row.Name) ? $"math.{row.Name}"
                : StdLibNamespaces.StrMethodNames.Contains(row.Name) ? $"str.{row.Name}"
                : StdLibNamespaces.IoMethodNames.Contains(row.Name) ? $"io.{row.Name}"
                : ansiNames.Contains(row.Name) ? $"AnsiConsole.{row.Name}"
                : ArrayMethodNames.Contains(row.Name) ? $"<array>.{row.Name}"
                : row.Name;

            if (!string.Equals(row.Call, expected, StringComparison.Ordinal))
                wrong.Add($"{row.Name}: expected '{expected}', found '{row.Call}'");
        }

        Assert.True(
            wrong.Count == 0,
            $"malda-builtins.tsv preferred-call column is stale: {string.Join("; ", wrong)}. {RegenerateHint}");
    }

    private static IReadOnlyList<string> EngineSources()
    {
        var engineDirectory = Path.GetDirectoryName(Path.GetDirectoryName(
            PlanningPaths.ResolveRepoFile("MaldaLang", "BuiltIns", "BuiltInFunctions.cs"))!)!;

        return Directory
            .EnumerateFiles(engineDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .ToList();
    }

    /// <summary>
    /// The wording a <c>BuiltInArity.Require</c> call site produces, keyed by built-in name.
    /// Calling the engine's own formatter is what keeps the PowerShell generator honest: if
    /// the two ever disagree about how to phrase an arity, the table stops matching here.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ArityCallSiteDescriptions()
    {
        var pattern = new Regex(
            """
            BuiltInArity\.Require\(\s*"(?<name>[A-Za-z_][A-Za-z0-9_]*)"\s*,\s*[A-Za-z_][A-Za-z0-9_]*\s*,\s*(?<min>\d+)\s*,\s*(?<max>BuiltInArity\.Unbounded|-?\d+)\s*(?:,\s*"(?<sig>[^"]*)"\s*)?\)
            """,
            RegexOptions.Compiled);

        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in EngineSources())
        {
            foreach (Match match in pattern.Matches(source))
            {
                var maxText = match.Groups["max"].Value;
                var maximum = maxText == "BuiltInArity.Unbounded" ? BuiltInArity.Unbounded : int.Parse(maxText);

                descriptions[match.Groups["name"].Value] = BuiltInArity.DescribeArguments(
                    int.Parse(match.Groups["min"].Value),
                    maximum,
                    match.Groups["sig"].Value);
            }
        }

        return descriptions;
    }

    /// <summary>
    /// The arguments column is the text the built-in itself puts after "name() expects", so a
    /// reworded message or a retuned arity shows up here as a failure rather than as a quietly
    /// wrong signature.
    /// </summary>
    [Fact]
    public void ArgumentColumn_StillMatchesTheEngineErrorText()
    {
        var sources = EngineSources();
        var fromArity = ArityCallSiteDescriptions();

        var stale = new List<string>();
        foreach (var row in LoadRows())
        {
            if (row.Arguments.Length == 0)
                continue;

            if (fromArity.TryGetValue(row.Name, out var declared))
            {
                if (!string.Equals(declared, row.Arguments, StringComparison.Ordinal))
                    stale.Add($"{row.Name}: engine says '{declared}', table says '{row.Arguments}'");
                continue;
            }

            var expected = $"{row.Name}() expects {row.Arguments}";
            if (!sources.Any(source => source.Contains(expected, StringComparison.Ordinal)))
                stale.Add(row.Name);
        }

        Assert.True(
            stale.Count == 0,
            $"malda-builtins.tsv argument text no longer matches the engine for: {string.Join("; ", stale)}. {RegenerateHint}");
    }

    /// <summary>
    /// A built-in that checks its arity but does not say what it wanted sends the caller back
    /// to the manual. New checks go through <see cref="BuiltInArity"/>, which cannot omit the
    /// parameter names.
    /// </summary>
    [Fact]
    public void ArityCallSites_NameTheirParameters()
    {
        var silent = ArityCallSiteDescriptions()
            .Where(pair => pair.Value != "0 arguments" && !pair.Value.Contains('('))
            .Select(pair => $"{pair.Key} ('{pair.Value}')")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            silent.Count == 0,
            $"These BuiltInArity.Require call sites state a count but no parameter names: {string.Join(", ", silent)}.");
    }

    [Fact]
    public void HighTrafficBuiltins_CarryTheirGotchaNote()
    {
        var rows = LoadRows().ToDictionary(row => row.Name, StringComparer.Ordinal);

        foreach (var name in new[]
                 {
                     "randomInt", "markup", "markupLine", "panel", "input", "string",
                     "floor", "append", "table", "getEnv", "prompt", "progress", "status", "tree"
                 })
        {
            Assert.True(rows.ContainsKey(name), $"malda-builtins.tsv has no row for '{name}'.");
            Assert.False(
                string.IsNullOrWhiteSpace(rows[name].Notes),
                $"'{name}' is a documented footgun and must keep a note in malda-builtins.tsv.");
        }

        Assert.Contains("null", rows["getEnv"].Returns, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            string.IsNullOrWhiteSpace(rows["prompt"].Returns),
            "AnsiConsole.prompt must document its return type in the returns column.");
    }
}
