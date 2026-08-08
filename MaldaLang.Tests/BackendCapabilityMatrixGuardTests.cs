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
}
