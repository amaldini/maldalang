// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Compiler;
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
        AssertContains(markdown, "`asVariant()`", "schema/validate/asVariant row");
        AssertContains(markdown, "`evalPrompt()`", "schema/validate/asVariant/evalPrompt row");
        AssertContains(markdown, "Typed prompt", "typed prompts row");
        AssertContains(markdown, "Gather-then-extract", "gather-then-extract prompts row");
        AssertContains(markdown, "`attachments:`", "prompt attachments row");
        AssertContains(markdown, "`runProgram`", "closed api / program(Api) / runProgram row");
        AssertContains(markdown, "`program(Api)`", "closed api / program(Api) / runProgram row");
        AssertContains(markdown, "`@budget`", "@budget resource bounds row");
        AssertContains(markdown, "`grounded.wrap`", "grounded values row");
        AssertContains(markdown, "`new VectorDB`", "VectorDB row");
        AssertContains(markdown, "`new OnnxModel`", "OnnxModel row");
        AssertContains(markdown, "`cap.fileRead`", "capability tokens row");
        AssertContains(markdown, "`agents.team`", "agent teams row");
        AssertContains(markdown, "call-graph determinism", "workflow call-graph determinism row");
        AssertContains(markdown, "HttpServer", "HttpServer row");
        AssertContains(markdown, "UIHost", "UIHost row");
        AssertContains(markdown, "Jobs", "Jobs row");
        AssertContains(markdown, "`enqueueJob`", "Jobs row");
    }

    /// <summary>
    /// I4: every row the matrix marks <c>no</c> for JavaScript has a <see cref="JsCapabilityChecker"/>
    /// diagnostic and a fixture that raises it, and the doc lists every diagnostic code.
    /// </summary>
    [Fact]
    public void EveryJavaScriptNoRow_HasPreCheckDiagnosticAndFixture()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "spec", "backend-capability-matrix.md");
        var markdown = File.ReadAllText(path);

        // `actors` gates @property runs only; the JavaScript backend runs actors process-local
        // (product row "Actors" is yes), so the pre-check must not reject them.
        var exempt = new HashSet<string>(StringComparer.Ordinal) { "`actors`" };

        var noRows = markdown.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|') && line.EndsWith('|'))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length == 4 && cells[3] == "no")
            .Select(cells => cells[0])
            .Where(row => !exempt.Contains(row))
            .ToList();

        Assert.True(noRows.Count >= 10, "Expected the matrix to list the JavaScript 'no' rows; found: " + string.Join(", ", noRows));

        var problems = new List<string>();
        foreach (var row in noRows)
        {
            var rules = JsCapabilityChecker.Rules
                .Where(rule => rule.MatrixRows.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal)))
                .ToList();
            if (rules.Count == 0)
            {
                problems.Add($"no JS0xxx rule for row '{row}'");
                continue;
            }

            foreach (var rule in rules.Where(rule => !JsCapabilityFixtures.ByCode.ContainsKey(rule.Code)))
            {
                problems.Add($"rule {rule.Code} (row '{row}') has no fixture");
            }
        }

        foreach (var rule in JsCapabilityChecker.Rules.Where(rule => !markdown.Contains("`" + rule.Code + "`", StringComparison.Ordinal)))
        {
            problems.Add($"diagnostic `{rule.Code}` is not listed in backend-capability-matrix.md");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static void AssertContains(string markdown, string marker, string rowLabel)
    {
        Assert.True(
            markdown.Contains(marker, StringComparison.Ordinal),
            $"backend-capability-matrix.md product features must mention '{marker}' ({rowLabel}).");
    }
}
