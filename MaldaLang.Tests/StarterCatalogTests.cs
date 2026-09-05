// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

public class StarterCatalogTests
{
    [Fact]
    public void StudentTrack_FollowsOneIdeaPerStepOrder()
    {
        var ids = StarterCatalog.GetByTrack("student").Select(starter => starter.Id).ToList();

        Assert.Equal(
            [
                "hello-world",
                "variables",
                "conditionals",
                "loops",
                "for-loop",
                "functions",
                "complete-starter",
                "input-output",
                "lists",
                "dictionaries",
                "recursion"
            ],
            ids);
    }

    [Fact]
    public void StudentTrack_HighlightsUseNamespacedIo()
    {
        var student = StarterCatalog.GetByTrack("student");
        var hello = student.Single(starter => starter.Id == "hello-world");
        var input = student.Single(starter => starter.Id == "input-output");

        Assert.Contains("io.print", hello.Highlights);
        Assert.DoesNotContain("print()", hello.Highlights);
        Assert.Contains("io.input", input.Highlights);
        Assert.DoesNotContain("input()", input.Highlights);
    }

    [Fact]
    public void GetNextStudentStarter_WalksThePathThenStops()
    {
        Assert.Equal("variables", StarterCatalog.GetNextStudentStarter("Basics/hello_world.malda")?.Id);
        Assert.Equal("for-loop", StarterCatalog.GetNextStudentStarter(@"Basics\while_loop.malda")?.Id);
        Assert.Equal("input-output", StarterCatalog.GetNextStudentStarter("Basics/complete_starter_program.malda")?.Id);
        Assert.Equal("lists", StarterCatalog.GetNextStudentStarter("Basics/input_example.malda")?.Id);
        Assert.Equal("recursion", StarterCatalog.GetNextStudentStarter("Basics/dictionary_example.malda")?.Id);
        Assert.Null(StarterCatalog.GetNextStudentStarter("Basics/recursion.malda"));
        Assert.Null(StarterCatalog.GetNextStudentStarter("Basics/first_look.malda"));
    }

    [Fact]
    public void IsLastStudentStarter_OnlyRecursion()
    {
        Assert.False(StarterCatalog.IsLastStudentStarter("Basics/hello_world.malda"));
        Assert.False(StarterCatalog.IsLastStudentStarter("Basics/input_example.malda"));
        Assert.True(StarterCatalog.IsLastStudentStarter("Basics/recursion.malda"));
        Assert.False(StarterCatalog.IsLastStudentStarter("Prompts/basic_prompt.malda"));
    }

    [Fact]
    public void AlgorithmsBranch_OpensBinarySearch()
    {
        var branch = StarterCatalog.GetBranches().Single(item => item.Id == "algorithms-track");
        Assert.Equal("Algorithms/binary_search.malda", StarterCatalog.NormalizeExamplePath(branch.RelativeExamplePath));
        Assert.Equal("student", branch.AudienceTrack);
    }

    [Fact]
    public void FirstFunctionExample_TeachesAddWithoutRecursion()
    {
        var source = File.ReadAllText(PlanningPaths.ResolveRepoPath("Examples", "Basics", "functions.malda"));
        Assert.Contains("function add", source, StringComparison.Ordinal);
        Assert.DoesNotContain("factorial", source, StringComparison.Ordinal);
    }
}
