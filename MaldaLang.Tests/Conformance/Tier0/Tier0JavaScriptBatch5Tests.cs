// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests.Conformance.Tier0;

[Collection("Tier0JavaScriptSerial")]
public class Tier0JavaScriptBatch5Tests
{
    public static IEnumerable<object[]> Batch5Cases =>
    [
        ["actor-send-order.malda"],
        ["defer-lifo.malda"],
        ["dict-comprehension-map.malda"],
        ["list-comprehension-filter.malda"],
        ["option-some-map.malda"],
        ["option-unwrap-none.malda"],
        ["pipe-sort.malda"],
        ["result-err-unwrapor.malda"],
        ["result-is-err-true.malda"],
        ["result-map-unwrap.malda"],
        ["result-andthen-chain.malda"],
        ["run-property-stable.malda"],
        ["using-dispose.malda"]
    ];

    [Theory]
    [MemberData(nameof(Batch5Cases))]
    public async Task Batch5Case_PassesJavaScript(string file)
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
