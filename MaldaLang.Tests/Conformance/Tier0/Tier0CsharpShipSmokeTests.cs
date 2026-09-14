// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests.Conformance.Tier0;

/// <summary>
/// PR-CI slice of Tier 0 C# transpile. The full matrix stays in
/// <c>tier0-nightly.yml</c> (<see cref="Tier0MaldaConformanceTests"/>).
/// </summary>
[Collection("Sequential")]
public class Tier0CsharpShipSmokeTests
{
    public static IEnumerable<object[]> SmokeCaseIds() =>
        new[] { "T0-004", "T0-005", "T0-025", "T0-033", "T0-043" }
            .Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(SmokeCaseIds))]
    public async Task CSharpTranspile_MatchesExpected(string caseId)
    {
        var testCase = Tier0ConformanceManifest.LoadCases().FirstOrDefault(c => c.Id == caseId);
        Assert.NotNull(testCase);
        Assert.True(testCase!.Backends.CSharp, $"{caseId} must stay enabled on the C# backend");

        var result = await Tier0ConformanceRunner.RunCaseAsync(testCase, Tier0BackendKind.CSharp);
        Assert.True(
            result.Passed,
            $"{result.Case.Id} (C#) failed."
            + (result.Error != null ? Environment.NewLine + "Error: " + result.Error : "")
            + (result.Expected != null ? Environment.NewLine + "Expected:" + Environment.NewLine + result.Expected : "")
            + (result.Actual != null ? Environment.NewLine + "Actual:" + Environment.NewLine + result.Actual : ""));
    }
}
