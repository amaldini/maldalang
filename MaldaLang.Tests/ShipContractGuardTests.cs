// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Reflection;
using System.Text.RegularExpressions;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Keeps <c>docs/spec/ship-contract.md</c> aligned with smoke/pair InlineData,
/// <c>malda new</c> templates, and README showcases.
/// </summary>
public class ShipContractGuardTests
{
    private static readonly Regex TableRow = new(
        @"^\|\s*`([^`]+)`\s*\|\s*(pair|trace|n/a)\s*\|\s*([^|]+)\|\s*(.*?)\s*\|",
        RegexOptions.Compiled);

    private static readonly string[] ReadmeShowcases =
    [
        "Examples/Basics/first_look.malda",
        "Examples/Agents/secondbrain_semantic.malda",
        "Examples/RalphWiggum/RalphWiggum.malda"
    ];

    [Fact]
    public void Registry_CoversSmokePairTemplatesAndShowcases()
    {
        var rows = ParseRegistry();
        var required = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in SmokeExamplePaths())
            required.Add(path);
        foreach (var path in PairExamplePaths())
            required.Add(path);
        foreach (var path in TemplateAppPaths())
            required.Add(path);
        foreach (var path in ReadmeShowcases)
            required.Add(path);

        var missing = required.Where(p => !rows.ContainsKey(p)).ToArray();
        Assert.True(
            missing.Length == 0,
            "docs/spec/ship-contract.md is missing rows for:" + Environment.NewLine
            + string.Join(Environment.NewLine, missing.Select(p => "  " + p)));
    }

    [Fact]
    public void Registry_RowsAreWellFormedAndFilesExist()
    {
        var rows = ParseRegistry();
        Assert.True(rows.Count > 0, "ship-contract.md has no parseable table rows");

        var problems = new List<string>();
        foreach (var (path, row) in rows)
        {
            var full = Path.Combine(PlanningPaths.RepoRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                problems.Add($"{path}: file does not exist");

            if (row.Kind == "n/a" && string.IsNullOrWhiteSpace(row.Notes))
                problems.Add($"{path}: n/a requires a reason in Notes");

            if (row.Kind == "pair" && row.Oracle.IndexOf("InterpretTranspilePairTests", StringComparison.Ordinal) < 0)
                problems.Add($"{path}: pair oracle must name InterpretTranspilePairTests");
        }

        var pairFiles = new HashSet<string>(PairExamplePaths(), StringComparer.Ordinal);
        foreach (var path in pairFiles)
        {
            if (rows.TryGetValue(path, out var row) && row.Kind != "pair")
                problems.Add($"{path}: listed in InterpretTranspilePairTests but registry kind is '{row.Kind}'");
        }

        Assert.True(
            problems.Count == 0,
            "ship-contract.md problems:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private static Dictionary<string, RegistryRow> ParseRegistry()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "spec", "ship-contract.md");
        Assert.True(File.Exists(path), $"Missing ship-contract registry: {path}");

        var rows = new Dictionary<string, RegistryRow>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var match = TableRow.Match(line);
            if (!match.Success)
                continue;
            var file = match.Groups[1].Value.Trim();
            if (file.Equals("Path", StringComparison.Ordinal))
                continue;
            rows[file] = new RegistryRow(
                file,
                match.Groups[2].Value.Trim(),
                match.Groups[3].Value.Trim(),
                match.Groups[4].Value.Trim());
        }

        return rows;
    }

    private static IEnumerable<string> SmokeExamplePaths() =>
        InlineDataPaths(typeof(TranspileSmokeTests), nameof(TranspileSmokeTests.Example_TranspileToCSharp_Succeeds));

    private static IEnumerable<string> PairExamplePaths() =>
        InlineDataPaths(typeof(InterpretTranspilePairTests), nameof(InterpretTranspilePairTests.Example_InterpretAndTranspile_SameStdout));

    private static IEnumerable<string> TemplateAppPaths()
    {
        var root = Path.Combine(PlanningPaths.RepoRoot, "Templates");
        Assert.True(Directory.Exists(root), $"Missing Templates/: {root}");
        foreach (var file in Directory.EnumerateFiles(root, "app.malda", SearchOption.AllDirectories))
        {
            yield return Path.GetRelativePath(PlanningPaths.RepoRoot, file).Replace('\\', '/');
        }
    }

    private static IEnumerable<string> InlineDataPaths(Type type, string methodName)
    {
        var method = type.GetMethod(methodName);
        Assert.NotNull(method);
        foreach (var attr in method!.GetCustomAttributes<InlineDataAttribute>())
        {
            if (attr.GetData(null).FirstOrDefault() is object[] row && row.Length > 0 && row[0] is string path)
                yield return path.Replace('\\', '/');
        }
    }

    private readonly record struct RegistryRow(string Path, string Kind, string Oracle, string Notes);
}
