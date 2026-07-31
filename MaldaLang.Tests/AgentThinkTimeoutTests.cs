// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

public class AgentThinkTimeoutTests
{
    [Fact]
    public void ResolveThinkTimeoutMs_ReadsAgentEnv()
    {
        var previousAgent = Environment.GetEnvironmentVariable("MALDA_AGENT_THINK_TIMEOUT_MS");
        var previousRalph = Environment.GetEnvironmentVariable("MALDA_RALPH_ITER_TIMEOUT_MS");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_RALPH_ITER_TIMEOUT_MS", null);
            Environment.SetEnvironmentVariable("MALDA_AGENT_THINK_TIMEOUT_MS", "90000");
            Assert.Equal(90000, ConversationInstance.ResolveThinkTimeoutMs());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_AGENT_THINK_TIMEOUT_MS", previousAgent);
            Environment.SetEnvironmentVariable("MALDA_RALPH_ITER_TIMEOUT_MS", previousRalph);
        }
    }

    [Fact]
    public void ResolveThinkTimeoutMs_FallsBackToRalphAlias()
    {
        var previousAgent = Environment.GetEnvironmentVariable("MALDA_AGENT_THINK_TIMEOUT_MS");
        var previousRalph = Environment.GetEnvironmentVariable("MALDA_RALPH_ITER_TIMEOUT_MS");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_AGENT_THINK_TIMEOUT_MS", null);
            Environment.SetEnvironmentVariable("MALDA_RALPH_ITER_TIMEOUT_MS", "60000");
            Assert.Equal(60000, ConversationInstance.ResolveThinkTimeoutMs());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_AGENT_THINK_TIMEOUT_MS", previousAgent);
            Environment.SetEnvironmentVariable("MALDA_RALPH_ITER_TIMEOUT_MS", previousRalph);
        }
    }

    [Fact]
    public void ResolveMaxLlmRounds_ReadsAgentEnv()
    {
        var previousAgent = Environment.GetEnvironmentVariable("MALDA_AGENT_MAX_LLM_ROUNDS");
        var previousRalph = Environment.GetEnvironmentVariable("MALDA_RALPH_MAX_LLM_ROUNDS");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_RALPH_MAX_LLM_ROUNDS", null);
            Environment.SetEnvironmentVariable("MALDA_AGENT_MAX_LLM_ROUNDS", "25");
            Assert.Equal(25, ConversationInstance.ResolveMaxLlmRounds());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_AGENT_MAX_LLM_ROUNDS", previousAgent);
            Environment.SetEnvironmentVariable("MALDA_RALPH_MAX_LLM_ROUNDS", previousRalph);
        }
    }

    [Fact]
    public void ResolveMaxLlmRounds_FallsBackToRalphAlias()
    {
        var previousAgent = Environment.GetEnvironmentVariable("MALDA_AGENT_MAX_LLM_ROUNDS");
        var previousRalph = Environment.GetEnvironmentVariable("MALDA_RALPH_MAX_LLM_ROUNDS");
        try
        {
            Environment.SetEnvironmentVariable("MALDA_AGENT_MAX_LLM_ROUNDS", null);
            Environment.SetEnvironmentVariable("MALDA_RALPH_MAX_LLM_ROUNDS", "30");
            Assert.Equal(30, ConversationInstance.ResolveMaxLlmRounds());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_AGENT_MAX_LLM_ROUNDS", previousAgent);
            Environment.SetEnvironmentVariable("MALDA_RALPH_MAX_LLM_ROUNDS", previousRalph);
        }
    }

    [Fact]
    public void EnsureWithinThinkDeadline_ThrowsWhenPastDeadline()
    {
        var previous = ConversationInstance.ThinkDeadlineUtc;
        try
        {
            ConversationInstance.ThinkDeadlineUtc = DateTime.UtcNow.AddSeconds(-1);
            var ex = Assert.Throws<InvalidOperationException>(() => ConversationInstance.EnsureWithinThinkDeadline());
            Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ConversationInstance.ThinkDeadlineUtc = previous;
        }
    }
}
