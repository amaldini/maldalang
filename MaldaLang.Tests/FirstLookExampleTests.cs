// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

public class FirstLookExampleTests : TestBase
{
    [Fact]
    public void Basics_FirstLook_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "Basics", "first_look.malda");
        var source = File.ReadAllText(path);
        var output = RunProgram(source);
        Assert.Contains("Review this javascript code", output, StringComparison.Ordinal);
        Assert.Contains("schema ok: Looks fine", output, StringComparison.Ordinal);
    }
}
