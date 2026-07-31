// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guards that the OSS <c>Examples/</c> tree stays free of vertical pack sample trees.
/// </summary>
public class CoreExamplesGuardTests
{
    private static readonly string[] ForbiddenPathFragments =
    [
        "Examples/Trading",
        "Examples\\Trading",
        "malda-trading",
        "malda-timeseries",
        "packages/malda-trading",
        "packages/malda-timeseries",
        "loadNativeModule(\"trading\")",
        "loadNativeModule(\"timeseries\")",
        "loadNativeModule('trading')",
        "loadNativeModule('timeseries')"
    ];

    [Fact]
    public void Examples_DoesNotContainTradingDirectory()
    {
        var tradingDir = Path.Combine(PlanningPaths.RepoRoot, "Examples", "Trading");
        Assert.False(
            Directory.Exists(tradingDir),
            $"Unexpected trading examples directory: {tradingDir}");
    }

    [Fact]
    public void ExamplesMaldaFiles_DoNotReferenceVerticalPackPaths()
    {
        var examplesRoot = Path.Combine(PlanningPaths.RepoRoot, "Examples");
        Assert.True(Directory.Exists(examplesRoot), $"Missing Examples/: {examplesRoot}");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(examplesRoot, "*.malda", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var fragment in ForbiddenPathFragments)
            {
                if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{Path.GetRelativePath(PlanningPaths.RepoRoot, file)}: contains '{fragment}'");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Examples still reference vertical packs:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}
