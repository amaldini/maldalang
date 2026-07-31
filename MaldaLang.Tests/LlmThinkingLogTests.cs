// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

public class LlmThinkingLogTests
{
    [Fact]
    public void ExtractReasoningFromMessage_ReadsReasoningString()
    {
        using var doc = JsonDocument.Parse("""{"reasoning":"Plan: read PRD then edit index.html."}""");
        var reasoning = LLMClientInstance.ExtractReasoningFromMessage(doc.RootElement);
        Assert.Equal("Plan: read PRD then edit index.html.", reasoning);
    }

    [Fact]
    public void ExtractReasoningFromMessage_ReadsReasoningDetailsArray()
    {
        using var doc = JsonDocument.Parse("""
            {
              "reasoning_details": [
                {"type":"reasoning.text","text":"Step one."},
                {"type":"reasoning.text","summary":"Step two."}
              ]
            }
            """);
        var reasoning = LLMClientInstance.ExtractReasoningFromMessage(doc.RootElement);
        Assert.Equal("Step one.\nStep two.", reasoning);
    }

    [Fact]
    public void ExtractThinkingFromResponse_PrefersNativeReasoning()
    {
        var response = new JsonObject();
        response.Set("reasoning", RuntimeValue.String("Native chain-of-thought."));
        response.Set("content", RuntimeValue.String("Also some content."));

        var thinking = ConversationInstance.ExtractThinkingFromResponse(response, hasToolCalls: true);
        Assert.Equal("Native chain-of-thought.", thinking);
    }

    [Fact]
    public void ExtractThinkingFromResponse_UsesContentOnToolCallRounds()
    {
        var response = new JsonObject();
        response.Set("content", RuntimeValue.String("I'll batch-read the project files first."));

        var thinking = ConversationInstance.ExtractThinkingFromResponse(response, hasToolCalls: true);
        Assert.Equal("I'll batch-read the project files first.", thinking);
    }

    [Fact]
    public void FormatThinkingPreview_CompactCollapsesNewlines()
    {
        var preview = ConversationInstance.FormatThinkingPreview(
            "Line one.\nLine two.",
            ConversationInstance.LlmThinkingMode.Compact);

        Assert.Equal("Line one. Line two.", preview);
    }

    [Fact]
    public void GetLlmThinkingMode_DefaultsToCompactWhenUnset()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_AGENT_LLM_THINKING");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_LLM_THINKING", null);
            System.Environment.SetEnvironmentVariable("MALDA_RALPH_LLM_THINKING", null);
            ResetLlmThinkingModeCache();

            Assert.Equal(ConversationInstance.LlmThinkingMode.Compact, ConversationInstance.GetLlmThinkingMode());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_LLM_THINKING", previous);
            ResetLlmThinkingModeCache();
        }
    }

    [Fact]
    public void GetLlmThinkingMode_CanBeDisabledViaEnv()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_AGENT_LLM_THINKING");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_LLM_THINKING", "off");
            ResetLlmThinkingModeCache();

            Assert.Equal(ConversationInstance.LlmThinkingMode.Off, ConversationInstance.GetLlmThinkingMode());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_LLM_THINKING", previous);
            ResetLlmThinkingModeCache();
        }
    }

    private static void ResetLlmThinkingModeCache()
    {
        var type = typeof(ConversationInstance);
        type.GetField("_llmThinkingModeResolved", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
        type.GetField("_llmThinkingMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, ConversationInstance.LlmThinkingMode.Compact);
    }
}
