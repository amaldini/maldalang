// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

public class AgentActivityStatusTests
{
    [Fact]
    public void BuildActivityRewrite_PadsShorterLine()
    {
        var rewrite = ConversationInstance.BuildActivityRewrite("ok", previousWidth: 5, consoleWidth: 80);

        Assert.Equal("\rok   ", rewrite.Text);
        Assert.Equal(5, rewrite.VisibleLength);
    }

    [Fact]
    public void BuildActivityRewrite_ClipsToConsoleWidth()
    {
        var rewrite = ConversationInstance.BuildActivityRewrite(
            "read_file src/very/long/path.malda",
            previousWidth: 0,
            consoleWidth: 10);

        Assert.Equal("\rread_f...", rewrite.Text);
        Assert.Equal(9, rewrite.VisibleLength);
    }

    [Fact]
    public void BuildActivityRewrite_ZeroWidthFallsBackTo80()
    {
        var rewrite = ConversationInstance.BuildActivityRewrite(new string('a', 200), previousWidth: 0, consoleWidth: 0);

        Assert.Equal(79, rewrite.VisibleLength);
        Assert.StartsWith("\r", rewrite.Text);
        Assert.EndsWith("...", rewrite.Text);
    }

    [Fact]
    public void BuildActivityErase_CoversPreviousLine()
    {
        Assert.Equal("\r     \r", ConversationInstance.BuildActivityErase(5));
        Assert.Equal("", ConversationInstance.BuildActivityErase(0));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("OFF")]
    public void IsAgentActivityStatusEnabled_OffValues(string statusEnv)
    {
        Assert.False(ConversationInstance.IsAgentActivityStatusEnabled(
            verboseEnabled: false,
            outputRedirected: false,
            statusEnv));
    }

    [Fact]
    public void IsAgentActivityStatusEnabled_OnByDefaultForTerminal()
    {
        Assert.True(ConversationInstance.IsAgentActivityStatusEnabled(false, false, null));
        Assert.True(ConversationInstance.IsAgentActivityStatusEnabled(false, false, ""));
        Assert.True(ConversationInstance.IsAgentActivityStatusEnabled(false, false, "line"));
    }

    [Fact]
    public void IsAgentActivityStatusEnabled_OffWhenVerboseOrRedirected()
    {
        Assert.False(ConversationInstance.IsAgentActivityStatusEnabled(true, false, null));
        Assert.False(ConversationInstance.IsAgentActivityStatusEnabled(false, true, null));
    }

    [Fact]
    public void FormatToolActivity_IncludesNameTargetAndResult()
    {
        Assert.Equal(
            "read_file src/App.malda",
            ConversationInstance.FormatToolActivity("read_file", "src/App.malda"));
        Assert.Equal(
            "read_file src/App.malda · 120 lines",
            ConversationInstance.FormatToolActivity("read_file", "src/App.malda", "120 lines"));
        Assert.Equal("thinking… round 2", ConversationInstance.FormatThinkingActivity(2));
    }
}
