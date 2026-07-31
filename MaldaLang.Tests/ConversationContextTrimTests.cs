// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

public class ConversationContextTrimTests
{
    [Fact]
    public void TrimContextIfOverBudget_KeepsSystemPromptAndLastUserMessage()
    {
        var previousBudget = System.Environment.GetEnvironmentVariable("MALDA_AGENT_CONTEXT_BUDGET_TOKENS");
        var previousAutoTrim = System.Environment.GetEnvironmentVariable("MALDA_AGENT_CONTEXT_AUTO_TRIM");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_CONTEXT_BUDGET_TOKENS", "100");
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_CONTEXT_AUTO_TRIM", "true");

            var conversation = new ConversationInstance();
            conversation.Initialize((LLMClientInstance?)null, "system instructions", (IInputProvider?)null);
            conversation.AddUserMessage("first task");
            conversation.AddAssistantMessage(new string('a', 2000));
            conversation.AddUserMessage("current task");

            var trim = typeof(ConversationInstance).GetMethod(
                "TrimContextIfOverBudget",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            trim.Invoke(conversation, null);

            var messages = conversation.GetMessages().AsArray();
            Assert.Equal(2, messages.Count);

            var system = messages[0].AsObject();
            Assert.Equal("system", system.Get("role", null)!.AsString());
            Assert.Equal("system instructions", system.Get("content", null)!.AsString());

            var user = messages[1].AsObject();
            var content = user.Get("content", null)!.AsString();
            Assert.Equal("user", user.Get("role", null)!.AsString());
            Assert.Contains("Context trimmed", content);
            Assert.Contains("current task", content);
            Assert.DoesNotContain("first task", content);
            Assert.DoesNotContain("old assistant reply", content);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_CONTEXT_BUDGET_TOKENS", previousBudget);
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_CONTEXT_AUTO_TRIM", previousAutoTrim);
        }
    }

    [Fact]
    public void TrimContextIfOverBudget_InjectsHandoffNote()
    {
        var previousBudget = System.Environment.GetEnvironmentVariable("MALDA_AGENT_CONTEXT_BUDGET_TOKENS");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_CONTEXT_BUDGET_TOKENS", "100");

            var conversation = new ConversationInstance();
            conversation.Initialize((LLMClientInstance?)null, "system", (IInputProvider?)null);
            conversation.SetContextTrimHandoffNote("Iteration 3 summary");
            conversation.AddUserMessage(new string('x', 500));

            var trim = typeof(ConversationInstance).GetMethod(
                "TrimContextIfOverBudget",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            trim.Invoke(conversation, null);

            var content = conversation.GetMessages().AsArray()[1].AsObject().Get("content", null)!.AsString();
            Assert.Contains("Iteration 3 summary", content);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_CONTEXT_BUDGET_TOKENS", previousBudget);
        }
    }

    [Fact]
    public void EstimateContextTokens_IncreasesWithLongerMessages()
    {
        var conversation = new ConversationInstance();
        conversation.Initialize((LLMClientInstance?)null, "system", (IInputProvider?)null);
        conversation.AddUserMessage("short");

        var small = conversation.EstimateContextTokens();
        conversation.AddAssistantMessage(new string('a', 4000));

        var larger = conversation.EstimateContextTokens();
        Assert.True(larger > small);
    }
}
