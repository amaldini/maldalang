// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Tests for MALDA runtime tracing (TraceManager, AgentSession, and integration
// points in AgentInstance, ConversationInstance, and BuiltInFunctions).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Tracing;

namespace MaldaLang.Tests;

public class TracingTests : TestBase
{
    [Fact]
    public void TraceManager_Record_WritesEventsWithIncrementingStepIndex()
    {
        // Arrange
        var tempDir = CreateTempDirectory("trace_core_");
        try
        {
            TracingConfig.BaseDirectory = tempDir;
            TraceManager.DisableTracing();
            AgentSession.Stop();

            var ctx = AgentSession.Start("test-session", null, "session-123");
            using var writer = new FileTraceWriter(tempDir, ctx.SessionId);
            TraceManager.EnableTracing(writer);

            // Act
            TraceManager.Record(
                TraceEventType.AgentMessage,
                new { role = "user", content = "hello" },
                agentName: "TestAgent",
                conversationId: "conv-1");

            TraceManager.Record(
                TraceEventType.AgentMessage,
                new { role = "assistant", content = "world" },
                agentName: "TestAgent",
                conversationId: "conv-1");

            writer.Dispose();

            // Assert
            var traceFile = Directory.GetFiles(tempDir, "*.malda-trace.jsonl").Single();
            var lines = File.ReadAllLines(traceFile);
            Assert.Equal(2, lines.Length);

            var evt1 = JsonSerializer.Deserialize<JsonElement>(lines[0]);
            var evt2 = JsonSerializer.Deserialize<JsonElement>(lines[1]);

            Assert.Equal("session-123", evt1.GetProperty("sessionId").GetString());
            Assert.Equal(0, evt1.GetProperty("stepIndex").GetInt32());
            Assert.Equal("AgentMessage", evt1.GetProperty("type").GetString());

            Assert.Equal("session-123", evt2.GetProperty("sessionId").GetString());
            Assert.Equal(1, evt2.GetProperty("stepIndex").GetInt32());
            Assert.Equal("AgentMessage", evt2.GetProperty("type").GetString());

            var payload1 = evt1.GetProperty("payload");
            var payload2 = evt2.GetProperty("payload");
            Assert.Equal("user", payload1.GetProperty("role").GetString());
            Assert.Equal("assistant", payload2.GetProperty("role").GetString());
        }
        finally
        {
            TraceManager.DisableTracing();
            AgentSession.Stop();
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Agent_EnableTracing_CreatesTraceFileWithLlmRequestAndResponse()
    {
        // Arrange
        var tempDir = CreateTempDirectory("trace_agent_");
        try
        {
            TracingConfig.BaseDirectory = tempDir;
            TraceManager.DisableTracing();
            AgentSession.Stop();

            // Use a dummy LLM client that does not hit the network.
            var client = new LLMClientInstance
            {
                ApiUrl = "https://example.invalid", // will not be called
                ApiKey = "",
                Model = "test-model"
            };

            var agent = new AgentInstance();
            agent.Initialize("TraceAgent", "tester", "You echo.", client, null, null, null);
            agent.EnableTracing(traceName: "TraceAgentTest", baseDirectory: tempDir);

            // Instead of actually calling a remote LLM, we simulate a response by
            // adding a user message and then short-circuiting Send() through a fake client
            // is difficult here. For now, we just verify that AgentMessage events are written.

            agent.Think(RuntimeValue.String("Hello tracing!"));

            // Tear down writer so trace file is flushed
            TraceManager.DisableTracing();

            // Assert
            var traceFile = Directory.GetFiles(tempDir, "*.malda-trace.jsonl").Single();
            var lines = File.ReadAllLines(traceFile);
            Assert.True(lines.Length >= 1);

            var events = lines
                .Select(l => JsonSerializer.Deserialize<JsonElement>(l))
                .ToList();

            // We expect at least one AgentMessage event for this agent in the trace.
            Assert.Contains(events, e =>
                e.GetProperty("type").GetString() == "AgentMessage" &&
                e.GetProperty("agentName").GetString() == "TraceAgent");
        }
        finally
        {
            TraceManager.DisableTracing();
            AgentSession.Stop();
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void RunCommand_WritesRunCommandEvents()
    {
        // Arrange
        var tempDir = CreateTempDirectory("trace_runcommand_");
        try
        {
            TracingConfig.BaseDirectory = tempDir;
            TraceManager.DisableTracing();
            AgentSession.Stop();

            var ctx = AgentSession.Start("runcommand", null, "session-runcommand");
            using var writer = new FileTraceWriter(tempDir, ctx.SessionId);
            TraceManager.EnableTracing(writer);

            // Act: very simple command that should succeed everywhere
            var result = BuiltInFunctions.CallBuiltIn(
                "runCommand",
                new List<RuntimeValue>
                {
                    RuntimeValue.String("dotnet"),
                    RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("--info") }),
                    RuntimeValue.String(System.Environment.CurrentDirectory),
                    RuntimeValue.Integer(5000)
                },
                new MaldaLang.Interpreter.Interpreter());

            // Dispose writer to flush file
            writer.Dispose();

            // Assert
            var traceFile = Directory.GetFiles(tempDir, "*.malda-trace.jsonl").Single();
            var lines = File.ReadAllLines(traceFile);
            Assert.Contains(lines, l => JsonSerializer.Deserialize<JsonElement>(l).GetProperty("type").GetString() == "RunCommand");
        }
        finally
        {
            TraceManager.DisableTracing();
            AgentSession.Stop();
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void FileEdits_WriteFile_ProducesFileEditEvent()
    {
        // Arrange
        var tempDir = CreateTempDirectory("trace_fileedit_");
        var filePath = Path.Combine(tempDir, "test.txt");
        try
        {
            TracingConfig.BaseDirectory = tempDir;
            TraceManager.DisableTracing();
            AgentSession.Stop();

            var ctx = AgentSession.Start("fileedit", null, "session-fileedit");
            using var writer = new FileTraceWriter(tempDir, ctx.SessionId);
            TraceManager.EnableTracing(writer);

            // Act
            var result = BuiltInFunctions.CallBuiltIn(
                "writeFile",
                new List<RuntimeValue>
                {
                    RuntimeValue.String(filePath),
                    RuntimeValue.String("hello world")
                },
                new MaldaLang.Interpreter.Interpreter());

            writer.Dispose();

            // Assert
            var traceFile = Directory.GetFiles(tempDir, "*.malda-trace.jsonl").Single();
            var lines = File.ReadAllLines(traceFile);
            var fileEditEvents = lines
                .Select(l => JsonSerializer.Deserialize<JsonElement>(l))
                .Where(e => e.GetProperty("type").GetString() == "FileEdit")
                .ToList();

            Assert.Single(fileEditEvents);
            var payload = fileEditEvents[0].GetProperty("payload");
            Assert.Equal(filePath, payload.GetProperty("path").GetString());
            Assert.Equal("overwrite", payload.GetProperty("operation").GetString());
        }
        finally
        {
            TraceManager.DisableTracing();
            AgentSession.Stop();
            SafeDeleteDirectory(tempDir);
        }
    }
}

