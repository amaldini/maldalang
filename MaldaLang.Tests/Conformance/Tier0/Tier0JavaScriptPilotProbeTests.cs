// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using Xunit.Abstractions;

namespace MaldaLang.Tests.Conformance.Tier0;

/// <summary>
/// Opt-in probe for expanding the JS Tier 0 pilot. Set <c>MALDA_JS_PROBE=1</c> to run.
/// </summary>
[Collection("Tier0JavaScriptSerial")]
public class Tier0JavaScriptPilotProbeTests
{
    private readonly ITestOutputHelper _output;

    public Tier0JavaScriptPilotProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Probe_NonPilotCases_ReportJavaScriptParity()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MALDA_JS_PROBE"), "1", StringComparison.Ordinal))
            return;

        if (!Tier0JavaScriptRunner.IsAvailable(out var reason))
        {
            _output.WriteLine("JS runtime unavailable: " + reason);
            return;
        }

        var passed = new List<string>();
        var failed = new List<(string File, string Error)>();

        foreach (var testCase in Tier0ConformanceManifest.LoadCases().Where(c => !c.Backends.JavaScript))
        {
            var result = await Tier0ConformanceRunner.RunCaseAsync(testCase, Tier0BackendKind.JavaScript);
            if (result.Passed)
                passed.Add(testCase.File);
            else
                failed.Add((testCase.File, result.Error ?? "output mismatch"));
        }

        _output.WriteLine($"PASS ({passed.Count}):");
        foreach (var file in passed.OrderBy(f => f, StringComparer.Ordinal))
            _output.WriteLine("  " + file);

        _output.WriteLine($"FAIL ({failed.Count}):");
        foreach (var (file, error) in failed.OrderBy(f => f.File, StringComparer.Ordinal))
            _output.WriteLine($"  {file}: {error}");
    }
}
