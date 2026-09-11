// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

public class ExampleProgramsCatalogTests
{
    [Fact]
    public void GetExamples_PlacesCanvasAndGraphicsSamplesInGamesCategory()
    {
        Assert.True(File.Exists(PlanningPaths.ResolveRepoPath("Examples", "Games", "game_bounce.malda")));
        Assert.False(File.Exists(PlanningPaths.ResolveRepoPath("Examples", "Web", "js", "game_bounce.malda")));

        var bounce = ExampleProgramsService.GetExampleByRelativePath("Games/game_bounce.malda");
        Assert.NotNull(bounce);
        Assert.Equal("Games", bounce!.Category);
        Assert.Contains("game_bounce.malda", bounce.FilePath.Replace('\\', '/'), StringComparison.Ordinal);

        var webBounce = ExampleProgramsService.GetExampleByRelativePath("Web/js/game_bounce.malda");
        Assert.Null(webBounce);

        var categories = ExampleProgramsService.GetCategoriesSorted();
        var webIndex = categories.IndexOf("Web");
        var gamesIndex = categories.IndexOf("Games");
        Assert.True(webIndex >= 0, "Web category missing from catalog");
        Assert.True(gamesIndex >= 0, "Games category missing from catalog");
        Assert.True(gamesIndex > webIndex, "Games should sort after Web");
    }

    [Fact]
    public void GetExamples_SeparatesAiTheoryFromLlmClientsAndAgents()
    {
        var xor = ExampleProgramsService.GetExampleByRelativePath("AI_Theory/xor_neural_net.malda");
        Assert.NotNull(xor);
        Assert.Equal("AI_Theory", xor!.Category);

        var sarsa = ExampleProgramsService.GetExampleByRelativePath("AI_Theory/sarsa_cliff.malda");
        Assert.NotNull(sarsa);
        Assert.Equal("AI_Theory", sarsa!.Category);

        Assert.Null(ExampleProgramsService.GetExampleByRelativePath("AI_LLM/xor_neural_net.malda"));
        Assert.Null(ExampleProgramsService.GetExampleByRelativePath("AI_LLM/sarsa_cliff.malda"));
        Assert.Null(ExampleProgramsService.GetExampleByRelativePath("AI_LLM/microgpt.malda"));

        var localLlm = ExampleProgramsService.GetExampleByRelativePath("AI_LLM/local_llm_example.malda");
        Assert.NotNull(localLlm);
        Assert.Equal("AI_LLM", localLlm!.Category);

        var categories = ExampleProgramsService.GetCategoriesSorted();
        var algorithms = categories.IndexOf("Algorithms");
        var theory = categories.IndexOf("AI_Theory");
        var agents = categories.IndexOf("Agents");
        var llm = categories.IndexOf("AI_LLM");
        Assert.True(algorithms >= 0 && theory > algorithms, "AI_Theory should sort after Algorithms");
        Assert.True(agents > theory && llm > theory, "Applied AI folders should sort after AI_Theory");
    }
}
