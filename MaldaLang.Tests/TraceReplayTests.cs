// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Tests for TraceReplayEngine: RestoreStateToStep, PrepareAgentFromStep, RestoreConversationState.

using System;
using System.IO;
using System.Linq;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.TraceViewer;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class TraceReplayTests : TestBase
{
    [Fact]
    public void TraceReplayEngine_RestoreStateToStep_WritesFilesCorrectly()
    {
        var tempDir = CreateTempDirectory("trace_replay_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"FileEdit\",\"agentName\":\"A\",\"payload\":{\"path\":\"/a.txt\",\"operation\":\"overwrite\",\"afterContent\":\"one\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"FileEdit\",\"agentName\":\"A\",\"payload\":{\"path\":\"/b.txt\",\"operation\":\"overwrite\",\"afterContent\":\"two\"}}"
            };
            File.WriteAllLines(tracePath, lines);

            var session = TraceViewerService.LoadTrace(tracePath);
            var outDir = Path.Combine(tempDir, "out");
            Directory.CreateDirectory(outDir);

            var restored = TraceReplayEngine.RestoreStateToStep(session.Context, 1, outDir);

            Assert.Equal(2, restored.Count);
            Assert.True(File.Exists(Path.Combine(outDir, "a.txt")));
            Assert.True(File.Exists(Path.Combine(outDir, "b.txt")));
            Assert.Equal("one", File.ReadAllText(Path.Combine(outDir, "a.txt")));
            Assert.Equal("two", File.ReadAllText(Path.Combine(outDir, "b.txt")));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TraceReplayEngine_PrepareAgentFromStep_ExtractsMessages()
    {
        var tempDir = CreateTempDirectory("trace_replay_prepare_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"LlmRequest\",\"agentName\":\"MyAgent\",\"payload\":{\"model\":\"m\",\"systemPrompt\":\"You are helpful.\",\"messages\":[]}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"MyAgent\",\"payload\":{\"role\":\"user\",\"content\":\"hi\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":2,\"timestampUtc\":\"2026-01-27T10:15:32.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"MyAgent\",\"payload\":{\"role\":\"assistant\",\"content\":\"hello\"}}"
            };
            File.WriteAllLines(tracePath, lines);

            var session = TraceViewerService.LoadTrace(tracePath);
            var state = TraceReplayEngine.PrepareAgentFromStep(session, 2);

            Assert.NotNull(state.Messages);
            Assert.Equal(2, state.Messages.Count);
            Assert.Equal("You are helpful.", state.SystemPrompt);
            Assert.Equal("MyAgent", state.AgentName);

            var first = state.Messages[0];
            Assert.Equal(ValueType.Object, first.Type);
            var role1 = GetStr(first.AsObject(), "role");
            var content1 = GetStr(first.AsObject(), "content");
            Assert.Equal("user", role1);
            Assert.Equal("hi", content1);

            var second = state.Messages[1];
            Assert.Equal(ValueType.Object, second.Type);
            Assert.Equal("assistant", GetStr(second.AsObject(), "role"));
            Assert.Equal("hello", GetStr(second.AsObject(), "content"));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TraceReplayEngine_PrepareAgentFromStep_PopulatesToolsFromToolCallStart()
    {
        var tempDir = CreateTempDirectory("trace_replay_tools_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                // LLM request with system prompt
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"LlmRequest\",\"agentName\":\"Agent\",\"payload\":{\"model\":\"m\",\"systemPrompt\":\"Tool test.\",\"messages\":[]}}",
                // Tool call start for a known built-in tool
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"ToolCallStart\",\"agentName\":\"Agent\",\"payload\":{\"toolName\":\"read_file\",\"toolType\":\"file\",\"argumentsJson\":\"{}\",\"workingDirectory\":\"/sandbox\",\"correlationId\":\"id1\"}}"
            };
            File.WriteAllLines(tracePath, lines);

            var session = TraceViewerService.LoadTrace(tracePath);
            var state = TraceReplayEngine.PrepareAgentFromStep(session, 1);

            Assert.NotNull(state.Tools);
            Assert.True(state.Tools.Count >= 1);
            Assert.Contains(state.Tools.Keys, k => k == "read_file");

            var tool = state.Tools["read_file"];
            Assert.Equal("read_file", tool.Name);
            Assert.Equal("/sandbox", tool.WorkingDirectory);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TraceReplayEngine_RestoreConversationState_RestoresMessages()
    {
        var messages = new System.Collections.Generic.List<RuntimeValue>();
        var msg1 = new JsonObject();
        msg1.Set("role", RuntimeValue.String("user"));
        msg1.Set("content", RuntimeValue.String("hello"));
        messages.Add(RuntimeValue.Object(msg1));
        var msg2 = new JsonObject();
        msg2.Set("role", RuntimeValue.String("assistant"));
        msg2.Set("content", RuntimeValue.String("hi there"));
        messages.Add(RuntimeValue.Object(msg2));

        var state = new AgentReplayState(messages, "System: be kind", new System.Collections.Generic.Dictionary<string, ToolInstance>(), "TestAgent");

        var conv = new ConversationInstance();
        conv.Initialize((LLMClientInstance?)null, "");

        TraceReplayEngine.RestoreConversationState(conv, state);

        var got = conv.GetMessages();
        Assert.Equal(ValueType.Array, got.Type);
        var arr = got.AsArray();
        // Clear() adds system message, then we add user and assistant
        Assert.Equal(3, arr.Count);
        Assert.Equal("system", GetStr(arr[0].AsObject(), "role"));
        Assert.Equal("user", GetStr(arr[1].AsObject(), "role"));
        Assert.Equal("hello", GetStr(arr[1].AsObject(), "content"));
        Assert.Equal("assistant", GetStr(arr[2].AsObject(), "role"));
        Assert.Equal("hi there", GetStr(arr[2].AsObject(), "content"));
    }

    private static string? GetStr(ObjectInstance o, string key)
    {
        try
        {
            var v = o.Get(key, null);
            return v?.Type == ValueType.String ? v.AsString() : null;
        }
        catch
        {
            return null;
        }
    }
}
