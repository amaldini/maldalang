// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class AlgorithmsTrackTests : TestBase
{
    private static readonly (string File, string OkLine)[] Track =
    [
        ("lists_indexing.malda", "lists ok"),
        ("recursion.malda", "recursion ok")
    ];

    private static readonly (string File, string OkLine)[] Algorithms =
    [
        ("binary_search.malda", "binary search ok"),
        ("merge_sort.malda", "merge sort ok"),
        ("bfs_dfs.malda", "bfs dfs ok"),
        ("knapsack.malda", "knapsack ok"),
        ("union_find.malda", "union-find ok"),
        ("qlearn_grid.malda", "q-learning ok"),
        ("simulated_annealing.malda", "simulated annealing ok")
    ];

    [Fact]
    public void Catalog_ListsSevenOfflineStudentExamples()
    {
        var examples = ExampleProgramsService.GetExamples()
            .Where(example => example.Category == "Algorithms")
            .ToList();

        Assert.Equal(7, examples.Count);
        Assert.All(examples, example =>
        {
            Assert.Equal("student", example.Track);
            Assert.True(ExampleProgramsService.IsOfflineFriendly(example));
            Assert.False(string.IsNullOrWhiteSpace(example.Next));
        });

        Assert.Equal(8, ExampleProgramsService.GetCategoryOrder("Algorithms"));
        var categories = ExampleProgramsService.GetCategoriesSorted();
        Assert.True(categories.IndexOf("Basics") < categories.IndexOf("Algorithms"));
        Assert.True(categories.IndexOf("Algorithms") < categories.IndexOf("Graphs"));
    }

    [Fact]
    public void Catalog_NextChain_EndsAtXorNeuralNet()
    {
        var first = ExampleProgramsService.GetExampleByRelativePath("Algorithms/binary_search.malda");
        Assert.NotNull(first);
        Assert.Equal("Algorithms/merge_sort.malda", first!.Next);

        var last = ExampleProgramsService.GetExampleByRelativePath("Algorithms/simulated_annealing.malda");
        Assert.NotNull(last);
        Assert.Equal("AI_LLM/xor_neural_net.malda", last!.Next);
    }

    [Fact]
    public async Task Cs101Extras_PrintOkLines()
    {
        foreach (var (file, okLine) in Track)
        {
            var path = PlanningPaths.ResolveRepoPath("Examples", "Basics", file);
            var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
            Assert.Contains(okLine, output);
        }
    }

    [Fact]
    public async Task AlgorithmsExamples_PrintOkLines()
    {
        foreach (var (file, okLine) in Algorithms)
        {
            var path = PlanningPaths.ResolveRepoPath("Examples", "Algorithms", file);
            var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
            Assert.Contains(okLine, output);
        }
    }
}
