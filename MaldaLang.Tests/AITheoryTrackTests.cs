// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class AITheoryTrackTests : TestBase
{
    [Fact]
    public void Catalog_ListsFiveOfflineStudentExamples()
    {
        var examples = ExampleProgramsService.GetExamples()
            .Where(example => example.Category == "AI_Theory")
            .ToList();

        Assert.Equal(5, examples.Count);
        Assert.All(examples, example =>
        {
            Assert.Equal("student", example.Track);
            Assert.True(ExampleProgramsService.IsOfflineFriendly(example));
        });

        Assert.Equal(9, ExampleProgramsService.GetCategoryOrder("AI_Theory"));
    }

    [Fact]
    public void Catalog_NextChain_StartsAtSarsaCliff()
    {
        var sarsa = ExampleProgramsService.GetExampleByRelativePath("AI_Theory/sarsa_cliff.malda");
        Assert.NotNull(sarsa);
        Assert.Equal("AI_Theory/xor_neural_net.malda", sarsa!.Next);
        Assert.Contains("Algorithms/qlearn_grid.malda", sarsa.Prerequisites);

        var xor = ExampleProgramsService.GetExampleByRelativePath("AI_Theory/xor_neural_net.malda");
        Assert.NotNull(xor);
        Assert.Contains("AI_Theory/sarsa_cliff.malda", xor!.Prerequisites);
    }

    [Fact]
    public async Task SarsaCliff_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "sarsa_cliff.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("q-learning hugs the cliff", output);
        Assert.Contains("sarsa stays inland", output);
        Assert.Contains("sarsa cliff ok", output);
    }
}
