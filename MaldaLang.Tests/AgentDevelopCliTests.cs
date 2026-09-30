// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

public class AgentDevelopCliTests
{
    [Fact]
    public void ParseAgentLaunch_PlainAgent_HasNoDevelopKind()
    {
        var launch = Program.ParseAgentLaunch(new[] { "agent", "-m", "hello", "-b", "local-llama" });
        Assert.Null(launch.Error);
        Assert.Null(launch.DevelopKind);
        Assert.Equal("hello", launch.Message);
        Assert.Equal("local-llama", launch.Backend);
    }

    [Fact]
    public void ParseAgentLaunch_Develop_DefaultsToGenericDevAgent()
    {
        var launch = Program.ParseAgentLaunch(new[] { "agent", "develop", "--workdir", "src" });
        Assert.Null(launch.Error);
        Assert.Equal("dev", launch.DevelopKind);
        Assert.Equal("src", launch.Workdir);
    }

    [Fact]
    public void ParseAgentLaunch_DevelopMalda_SelectsMaldaCodingAgent()
    {
        var launch = Program.ParseAgentLaunch(new[] { "agent", "develop", "malda", "-m", "add a test" });
        Assert.Null(launch.Error);
        Assert.Equal("malda", launch.DevelopKind);
        Assert.Equal("add a test", launch.Message);
    }

    [Fact]
    public void ParseAgentLaunch_KindFlag_MatchesPositional()
    {
        var launch = Program.ParseAgentLaunch(new[] { "agent", "--kind", "generic" });
        Assert.Null(launch.Error);
        Assert.Equal("dev", launch.DevelopKind);
    }

    [Fact]
    public void ParseAgentLaunch_ConflictingKind_IsAnError()
    {
        var launch = Program.ParseAgentLaunch(new[] { "agent", "develop", "malda", "--kind", "generic" });
        Assert.Contains("Conflicting", launch.Error);
        Assert.Null(launch.DevelopKind);
    }

    [Fact]
    public void ParseAgentLaunch_WorkdirWithoutDevelop_IsAnError()
    {
        var launch = Program.ParseAgentLaunch(new[] { "agent", "--workdir", "." });
        Assert.Contains("--workdir", launch.Error);
    }

    [Fact]
    public void ParseAgentLaunch_Help_ShortCircuits()
    {
        var launch = Program.ParseAgentLaunch(new[] { "agent", "develop", "--help" });
        Assert.True(launch.ShowHelp);
    }
}
