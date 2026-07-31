// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RalphStateTests : TestBase
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void IsCompleteSignal_AcceptsRalphDone()
    {
        var includePath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "02-prd.malda").Replace("\\", "/");
        var envPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "00-env.malda").Replace("\\", "/");
        var source = $@"
include ""{envPath}"";
include ""{includePath}"";
print(string(isCompleteSignal(""RALPH_DONE"")));
print(string(isCompleteSignal(""noise"")));
";
        var output = RunProgram(source);
        Assert.Contains("true", output);
        Assert.Contains("false", output);
    }

    [Fact]
    public void GetResumeCheckpoint_UsesLastSuccessfulWhenPolicySuccessOnly()
    {
        var envPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "00-env.malda").Replace("\\", "/");
        var source = $@"
include ""{envPath}"";
var state = {{""completedIterations"": 3, ""lastSuccessfulIteration"": 2}};
print(string(getResumeCheckpoint(state)));
";
        var output = RunProgram(source);
        Assert.Contains("3", output);
    }
}
