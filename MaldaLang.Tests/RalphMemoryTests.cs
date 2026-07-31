// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RalphMemoryTests : TestBase
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void ResolveRalphMemoryScope_UsesProjectTitle()
    {
        var envPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "00-env.malda").Replace("\\", "/");
        var memoryPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "04-state-memory.malda").Replace("\\", "/");
        var source = $@"
var config = null;
include ""{envPath}"";
include ""{memoryPath}"";
print(resolveRalphMemoryScope(""snake-demo""));
";
        var output = RunProgram(source);
        Assert.Contains("ralph:snake-demo", output);
    }

    [Fact]
    public void ResolveRalphMemoryScope_RespectsEnvOverride()
    {
        var previous = Environment.GetEnvironmentVariable("MALDA_RALPH_MEMORY_SCOPE");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_RALPH_MEMORY_SCOPE", "ralph:custom");
            var envPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "00-env.malda").Replace("\\", "/");
            var memoryPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "04-state-memory.malda").Replace("\\", "/");
            var source = $@"
var config = null;
include ""{envPath}"";
include ""{memoryPath}"";
print(resolveRalphMemoryScope(""ignored""));
";
            var output = RunProgram(source);
            Assert.Contains("ralph:custom", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_RALPH_MEMORY_SCOPE", previous);
        }
    }

    [Fact]
    public void RalphMemoryMaintainEnabled_DefaultsOnForLongRuns()
    {
        var envPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "00-env.malda").Replace("\\", "/");
        var memoryPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "04-state-memory.malda").Replace("\\", "/");
        var source = $@"
var config = null;
include ""{envPath}"";
include ""{memoryPath}"";
print(string(ralphMemoryMaintainEnabled(10)));
print(string(ralphMemoryMaintainEnabled(3)));
";
        var output = RunProgram(source);
        Assert.Contains("true", output);
        Assert.Contains("false", output);
    }

    [Fact]
    public void QueryMemoryForPrompt_DefaultDisabled()
    {
        var envPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "00-env.malda").Replace("\\", "/");
        var memoryPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "04-state-memory.malda").Replace("\\", "/");
        var source = $@"
var config = null;
include ""{envPath}"";
include ""{memoryPath}"";
var memory = new GraphMemory();
memory.initialize();
memory.remember(""phase progress note"", """", {{ ""type"": ""progress"", ""phase"": ""F1"" }});
print(queryMemoryForPrompt(memory, ""F1"", ""ralph:test"") == """");
";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void BuildRalphQueryOptions_IncludesHybridLexicalAndTypes()
    {
        var envPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "00-env.malda").Replace("\\", "/");
        var memoryPath = Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "04-state-memory.malda").Replace("\\", "/");
        var source = $@"
var config = null;
include ""{envPath}"";
include ""{memoryPath}"";
var memory = new GraphMemory();
memory.initialize();
var opts = buildRalphQueryOptions(memory, ""F1-setup"", ""ralph:test"");
print(string(opts.hybridLexical));
print(string(opts.synapse));
print(opts.scope);
";
        var output = RunProgram(source);
        Assert.Contains("true", output);
        Assert.Contains("ralph:test", output);
    }
}
