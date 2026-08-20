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
}
