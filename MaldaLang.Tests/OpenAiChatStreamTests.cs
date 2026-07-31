// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

public class OpenAiChatStreamTests
{
    [Fact]
    public void ProcessSseDataLine_AccumulatesContentAndReasoningDeltas()
    {
        var deltas = new List<LlmStreamDelta>();
        var accumulator = new OpenAiChatStreamAccumulator
        {
            OnDelta = deltas.Add
        };

        accumulator.ProcessSseDataLine("""
            {"choices":[{"index":0,"delta":{"reasoning":"Plan"}}]}
            """);
        accumulator.ProcessSseDataLine("""
            {"choices":[{"index":0,"delta":{"reasoning":" ahead."}}]}
            """);
        accumulator.ProcessSseDataLine("""
            {"choices":[{"index":0,"delta":{"content":"I'll read PRD.md."}}]}
            """);

        var result = accumulator.ToResultObject();
        Assert.Equal("Plan ahead.", result.Get("reasoning", null)!.AsString());
        Assert.Equal("I'll read PRD.md.", result.Get("content", null)!.AsString());
        Assert.Equal(3, deltas.Count);
    }

    [Fact]
    public void ProcessSseDataLine_AccumulatesToolCallDeltas()
    {
        var accumulator = new OpenAiChatStreamAccumulator();

        accumulator.ProcessSseDataLine("""
            {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{\"file"}}]}}]}
            """);
        accumulator.ProcessSseDataLine("""
            {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"Path\":\"PRD.md\"}"}}]}}]}
            """);

        var result = accumulator.ToResultObject();
        var toolCalls = result.Get("tool_calls", null)!.AsArray();
        Assert.Single(toolCalls);

        var tc = toolCalls[0].AsObject();
        Assert.Equal("call_1", tc.Get("id", null)!.AsString());
        var func = tc.Get("function", null)!.AsObject();
        Assert.Equal("read_file", func.Get("name", null)!.AsString());
        Assert.Equal("{\"filePath\":\"PRD.md\"}", func.Get("arguments", null)!.AsString());
    }

    [Fact]
    public void ProcessSseDataLine_CapturesUsageFromFinalChunk()
    {
        var accumulator = new OpenAiChatStreamAccumulator();
        accumulator.ProcessSseDataLine("""
            {"choices":[{"index":0,"delta":{"content":"done"}}],"usage":{"prompt_tokens":120,"completion_tokens":45,"total_tokens":165}}
            """);

        var result = accumulator.ToResultObject();
        var usage = result.Get("usage", null)!.AsObject();
        Assert.Equal(120, usage.Get("promptTokens", null)!.AsInteger());
        Assert.Equal(45, usage.Get("completionTokens", null)!.AsInteger());
        Assert.Equal(165, usage.Get("totalTokens", null)!.AsInteger());
    }

    [Fact]
    public void ProcessSseDataLine_CapturesOpenRouterCostFromUsage()
    {
        var accumulator = new OpenAiChatStreamAccumulator();
        accumulator.ProcessSseDataLine("""
            {"choices":[{"index":0,"delta":{"content":"done"}}],"usage":{"prompt_tokens":120,"completion_tokens":45,"total_tokens":165,"cost":0.000123}}
            """);

        var result = accumulator.ToResultObject();
        var usage = result.Get("usage", null)!.AsObject();
        Assert.Equal(0.000123, usage.Get("cost", null)!.AsFloat(), 6);
    }

    [Fact]
    public void IsLlmStreamingEnabled_DefaultsToTrueWhenUnset()
    {
        var previousAgent = System.Environment.GetEnvironmentVariable("MALDA_AGENT_LLM_STREAM");
        var previousRalph = System.Environment.GetEnvironmentVariable("MALDA_RALPH_LLM_STREAM");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_LLM_STREAM", null);
            System.Environment.SetEnvironmentVariable("MALDA_RALPH_LLM_STREAM", null);
            ResetLlmStreamingCache();

            Assert.True(LLMClientInstance.IsLlmStreamingEnabled());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_LLM_STREAM", previousAgent);
            System.Environment.SetEnvironmentVariable("MALDA_RALPH_LLM_STREAM", previousRalph);
            ResetLlmStreamingCache();
        }
    }

    private static void ResetLlmStreamingCache()
    {
        typeof(LLMClientInstance)
            .GetField("_llmStreamingEnabled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
    }
}
