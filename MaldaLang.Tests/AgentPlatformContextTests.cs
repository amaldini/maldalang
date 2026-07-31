// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

public class AgentPlatformContextTests
{
    [Fact]
    public void DescribeForAgentPrompt_IncludesOsFamily()
    {
        var text = AgentPlatformContext.DescribeForAgentPrompt();
        Assert.Contains("OS:", text);
        Assert.Contains(AgentPlatformContext.OsFamily, text);
    }

    [Fact]
    public void DescribeForAgentPrompt_WarnsWhenAgentDirDiffersFromProcessCwd()
    {
        var agentDir = Path.Combine(Path.GetTempPath(), "malda-agent-workdir-test");
        var text = AgentPlatformContext.DescribeForAgentPrompt(agentDir);
        Assert.Contains("Agent project directory (file + git tools):", text);
        Assert.Contains(agentDir, text);
        Assert.Contains("Process launch directory:", text);
        Assert.Contains("not the agent workdir", text);
    }
}
