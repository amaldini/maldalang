// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

public class ConversationToolRoundContentTests
{
    [Fact]
    public void BuildAssistantToolCallHistoryMessage_OmitsContentByDefault()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT", null);

            var conversation = new ConversationInstance();
            var response = new JsonObject();
            response.Set("content", RuntimeValue.String("I'll read the files first."));
            response.Set("tool_calls", RuntimeValue.Array(new List<RuntimeValue>()));

            var toolCall = new JsonObject();
            toolCall.Set("id", RuntimeValue.String("call_1"));
            toolCall.Set("type", RuntimeValue.String("function"));
            var function = new JsonObject();
            function.Set("name", RuntimeValue.String("read_file"));
            function.Set("arguments", RuntimeValue.String("{\"filePath\":\"PRD.md\"}"));
            toolCall.Set("function", RuntimeValue.Object(function));

            var message = conversation.BuildAssistantToolCallHistoryMessage(
                response,
                new List<RuntimeValue> { RuntimeValue.Object(toolCall) });

            Assert.Equal("assistant", message.Get("role", null)!.AsString());
            Assert.False(message.GetProperties().ContainsKey("content"));
            Assert.Single(message.Get("tool_calls", null)!.AsArray());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT", previous);
        }
    }

    [Fact]
    public void BuildAssistantToolCallHistoryMessage_KeepsContentWhenOptIn()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT", "true");

            var conversation = new ConversationInstance();
            var response = new JsonObject();
            response.Set("content", RuntimeValue.String("Planning text"));

            var toolCall = new JsonObject();
            toolCall.Set("id", RuntimeValue.String("call_1"));
            toolCall.Set("type", RuntimeValue.String("function"));
            var function = new JsonObject();
            function.Set("name", RuntimeValue.String("grep"));
            function.Set("arguments", RuntimeValue.String("{\"pattern\":\"TODO\"}"));
            toolCall.Set("function", RuntimeValue.Object(function));

            var message = conversation.BuildAssistantToolCallHistoryMessage(
                response,
                new List<RuntimeValue> { RuntimeValue.Object(toolCall) });

            Assert.Equal("Planning text", message.Get("content", null)!.AsString());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT", previous);
        }
    }

    [Fact]
    public void BuildAssistantToolCallHistoryMessage_PreservesReasoningForReplay()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT", null);

            var conversation = new ConversationInstance();
            var response = new JsonObject();
            response.Set("content", RuntimeValue.String("ignored by default strip"));
            response.Set("reasoning", RuntimeValue.String("I should call read_file next."));

            var toolCall = new JsonObject();
            toolCall.Set("id", RuntimeValue.String("call_1"));
            toolCall.Set("type", RuntimeValue.String("function"));
            var function = new JsonObject();
            function.Set("name", RuntimeValue.String("read_file"));
            function.Set("arguments", RuntimeValue.String("{\"filePath\":\"PRD.md\"}"));
            toolCall.Set("function", RuntimeValue.Object(function));

            var message = conversation.BuildAssistantToolCallHistoryMessage(
                response,
                new List<RuntimeValue> { RuntimeValue.Object(toolCall) });

            Assert.False(message.GetProperties().ContainsKey("content"));
            Assert.Equal("I should call read_file next.", message.Get("reasoning", null)!.AsString());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_KEEP_TOOL_ROUND_CONTENT", previous);
        }
    }
}
