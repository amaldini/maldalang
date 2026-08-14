// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Keeps <c>docs/spec/backend-capability-matrix.md</c> aligned with
/// <see cref="BackendCapabilityMatrix"/> capability tags.
/// </summary>
public class BackendCapabilityMatrixGuardTests
{
    [Fact]
    public void ProductMatrixDoc_MentionsEveryCapabilityTag()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "spec", "backend-capability-matrix.md");
        Assert.True(File.Exists(path), $"Missing capability matrix doc: {path}");
        var markdown = File.ReadAllText(path);

        var missing = BackendCapabilityMatrix.AllCapabilityTags()
            .Where(tag => markdown.IndexOf("`" + tag + "`", StringComparison.Ordinal) < 0)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "docs/spec/backend-capability-matrix.md must mention every BackendCapabilityMatrix tag in backticks. Missing: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void ProductMatrixDoc_MentionsEveryBackendColumn()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "spec", "backend-capability-matrix.md");
        var markdown = File.ReadAllText(path);
        foreach (var marker in new[] { "Interpreter", "C# transpile", "JavaScript" })
        {
            Assert.True(
                markdown.Contains(marker, StringComparison.Ordinal),
                $"backend-capability-matrix.md should mention '{marker}'");
        }
    }

    [Fact]
    public void ProductMatrixDoc_MentionsRequiredProductFeatureRows()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "spec", "backend-capability-matrix.md");
        var markdown = File.ReadAllText(path);

        // Stable markers for B1 product-feature rows (not property-test tags).
        AssertContains(markdown, "`schema`", "schema/validate row");
        AssertContains(markdown, "`validate()`", "schema/validate row");
        AssertContains(markdown, "Typed prompt", "typed prompts row");
        AssertContains(markdown, "Gather-then-extract", "gather-then-extract prompts row");
        AssertContains(markdown, "`@budget`", "@budget resource bounds row");
        AssertContains(markdown, "`grounded.wrap`", "grounded values row");
        AssertContains(markdown, "`cap.fileRead`", "capability tokens row");
        AssertContains(markdown, "call-graph determinism", "workflow call-graph determinism row");
        AssertContains(markdown, "HttpServer", "HttpServer row");
        AssertContains(markdown, "UIHost", "UIHost row");
        AssertContains(markdown, "Jobs", "Jobs row");
        AssertContains(markdown, "`enqueueJob`", "Jobs row");
    }

    private static void AssertContains(string markdown, string marker, string rowLabel)
    {
        Assert.True(
            markdown.Contains(marker, StringComparison.Ordinal),
            $"backend-capability-matrix.md product features must mention '{marker}' ({rowLabel}).");
    }
}
