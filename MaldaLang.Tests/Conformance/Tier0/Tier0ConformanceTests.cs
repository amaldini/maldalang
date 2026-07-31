using Xunit;

namespace MaldaLang.Tests.Conformance.Tier0;

/// <summary>
/// Spec anchor tests — delegates to the file-driven suite under <c>conformance/tier0/cases/</c>.
/// Replaces duplicated inline sources; see <see cref="Tier0MaldaConformanceTests"/> for the full matrix.
/// </summary>
[Collection("Sequential")]
public class Tier0ConformanceTests
{
    /// <summary>Former inline tests and spec §15 anchors (T0-01…T0-06).</summary>
    public static IEnumerable<object[]> SpecAnchorFiles() =>
    [
        ["match-literal.malda"],
        ["dict-missing-null.malda"],
        ["typeof-int.malda"],
        ["typeof-dict.malda"],
        ["typeof-bool.malda"],
        ["typeof-variant.malda"],
        ["typeof-task.malda"],
        ["is-tag-legacy.malda"],
        ["sum-type-match.malda"],
        ["is-number.malda"],
        ["async-await.malda"]
    ];

    [Theory]
    [MemberData(nameof(SpecAnchorFiles))]
    public async Task SpecAnchor_InterpreterMatchesExpected(string file)
    {
        var testCase = Tier0ConformanceManifest.LoadCases().First(c => c.File == file);
        var result = await Tier0ConformanceRunner.RunCaseAsync(testCase, Tier0BackendKind.Interpreter);
        Assert.True(result.Passed, FormatFailure(result));
    }

    private static string FormatFailure(Tier0CaseRunResult result) =>
        $"{result.Case.Id} ({result.Case.File}) failed: {result.Error ?? "output mismatch"}";
}
