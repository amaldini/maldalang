// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests.Conformance.Tier0;

[Collection("Tier0JavaScriptSerial")]
public class Tier0JavaScriptBatch4Tests
{
    public static IEnumerable<object[]> Batch4Cases =>
    [
        ["array-append-length.malda"],
        ["for-continue-skip.malda"],
        ["null-conditional-member.malda"],
        ["null-conditional-index.malda"],
        ["foreach-sum.malda"],
        ["try-catch-string.malda"],
        ["catch-plain-string.malda"],
        ["catch-io-filter.malda"],
        ["catch-fallback-generic.malda"],
        ["catch-rethrow-nested.malda"],
        ["match-no-default-error.malda"]
    ];

    [Theory]
    [MemberData(nameof(Batch4Cases))]
    public async Task Batch4Case_PassesJavaScript(string file)
    {
        if (!Tier0JavaScriptRunner.IsAvailable(out var reason))
        {
            Assert.Fail("JS runtime unavailable: " + reason);
        }

        var testCase = Tier0ConformanceManifest.LoadCases().First(c => c.File == file);
        var result = await Tier0ConformanceRunner.RunCaseAsync(testCase, Tier0BackendKind.JavaScript);
        Assert.True(result.Passed, $"{file}: {result.Error ?? "output mismatch"}");
    }
}
