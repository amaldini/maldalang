// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Tests for TraceLog, ReplayContext, and TraceCli (malda trace commands).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MaldaLang;
using MaldaLang.Runtime.Tracing;

namespace MaldaLang.Tests;

public class TracingCliTests : TestBase
{
    [Fact]
    public void TraceLog_Load_ParsesEventsFromFile()
    {
        var tempDir = CreateTempDirectory("trace_log_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"role\":\"user\",\"content\":\"hi\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"role\":\"assistant\",\"content\":\"ok\"}}"
            };
            File.WriteAllLines(tracePath, lines);

            var events = TraceLog.Load(tracePath).ToList();
            Assert.Equal(2, events.Count);
            Assert.Equal("s1", events[0].SessionId);
            Assert.Equal(0, events[0].StepIndex);
            Assert.Equal(TraceEventType.AgentMessage, events[0].Type);
            Assert.Equal("A", events[0].AgentName);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ReplayContext_Step_ReconstructsFileEdits()
    {
        // Single FileEdit event with afterContent; Files[path] should reflect afterContent.
        var evtJson =
            "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"FileEdit\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"path\":\"/tmp/test.txt\",\"operation\":\"overwrite\",\"beforeContent\":null,\"afterContent\":\"hello\"}}";
        using var doc = JsonDocument.Parse(evtJson);
        var root = doc.RootElement;

        var evt = new TraceEvent(
            root.GetProperty("sessionId").GetString()!,
            root.GetProperty("stepIndex").GetInt32(),
            DateTime.Parse(root.GetProperty("timestampUtc").GetString()!),
            TraceEventType.FileEdit,
            root.GetProperty("payload").GetRawText(),
            root.GetProperty("agentName").GetString(),
            root.GetProperty("conversationId").GetString());

        var replay = new ReplayContext(new[] { evt });
        Assert.True(replay.Step());
        Assert.Single(replay.Files);
        Assert.Equal("hello", replay.Files["/tmp/test.txt"]);
    }

    [Fact]
    public void ReplayContext_StepTo_PopulatesLastLlmAndToolCalls()
    {
        var eventsJson = new[]
        {
            "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"LlmRequest\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}}",
            "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"LlmResponse\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"model\":\"m\",\"content\":\"ok\"}}",
            "{\"sessionId\":\"s1\",\"stepIndex\":2,\"timestampUtc\":\"2026-01-27T10:15:32.123Z\",\"type\":\"ToolCallStart\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"toolName\":\"read_file\",\"toolType\":\"file\",\"argumentsJson\":\"{}\",\"workingDirectory\":\".\",\"correlationId\":\"id1\"}}",
            "{\"sessionId\":\"s1\",\"stepIndex\":3,\"timestampUtc\":\"2026-01-27T10:15:33.123Z\",\"type\":\"ToolCallEnd\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"toolName\":\"read_file\",\"toolType\":\"file\",\"correlationId\":\"id1\",\"durationMs\":10,\"resultJson\":\"{}\",\"success\":true,\"error\":null}}"
        };

        var events = new List<TraceEvent>();
        foreach (var json in eventsJson)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            events.Add(
                new TraceEvent(
                    root.GetProperty("sessionId").GetString()!,
                    root.GetProperty("stepIndex").GetInt32(),
                    DateTime.Parse(root.GetProperty("timestampUtc").GetString()!),
                    Enum.Parse<TraceEventType>(root.GetProperty("type").GetString()!),
                    root.GetProperty("payload").GetRawText(),
                    root.GetProperty("agentName").GetString(),
                    root.GetProperty("conversationId").GetString()));
        }

        var replay = new ReplayContext(events);
        replay.StepTo(3);

        Assert.NotNull(replay.LastLlmRequest);
        Assert.NotNull(replay.LastLlmResponse);
        Assert.NotNull(replay.LastToolCallStart);
        Assert.NotNull(replay.LastToolCallEnd);
        Assert.Equal(TraceEventType.LlmRequest, replay.LastLlmRequest!.Type);
        Assert.Equal(TraceEventType.LlmResponse, replay.LastLlmResponse!.Type);
        Assert.Equal(TraceEventType.ToolCallStart, replay.LastToolCallStart!.Type);
        Assert.Equal(TraceEventType.ToolCallEnd, replay.LastToolCallEnd!.Type);
    }

    [Fact]
    public void TraceCli_Summary_PrintsCounts()
    {
        var tempDir = CreateTempDirectory("trace_cli_summary_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"role\":\"user\",\"content\":\"hi\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"RunCommand\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"command\":\"echo\",\"arguments\":[],\"workingDirectory\":\".\",\"exitCode\":0,\"stdout\":\"\",\"stderr\":\"\",\"durationMs\":5}}"
            };
            File.WriteAllLines(tracePath, lines);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var code = TraceCli.Summary(tracePath, output, error);
            Assert.Equal(0, code);

            var text = output.ToString();
            Assert.Contains("Total events: 2", text);
            Assert.Contains("AgentMessage", text);
            Assert.Contains("RunCommand", text);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TraceCli_Show_PrintsSlice()
    {
        var tempDir = CreateTempDirectory("trace_cli_show_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"role\":\"user\",\"content\":\"hi\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"role\":\"assistant\",\"content\":\"ok\"}}"
            };
            File.WriteAllLines(tracePath, lines);

            using var output = new StringWriter();
            using var error = new StringWriter();

            var code = TraceCli.Show(tracePath, 0, 1, null, output, error);
            Assert.Equal(0, code);

            var text = output.ToString();
            Assert.Contains("Trace events 0..1", text);
            Assert.Contains("AgentMessage", text);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TraceCli_Replay_RestoresFilesToOutputDirectory()
    {
        var tempDir = CreateTempDirectory("trace_cli_replay_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"FileEdit\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"path\":\"/x.txt\",\"operation\":\"overwrite\",\"afterContent\":\"hello\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"FileEdit\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"path\":\"/y.txt\",\"operation\":\"overwrite\",\"afterContent\":\"world\"}}"
            };
            File.WriteAllLines(tracePath, lines);

            var outDir = Path.Combine(tempDir, "out");

            using var output = new StringWriter();
            using var error = new StringWriter();

            var code = TraceCli.Replay(tracePath, outDir, output, error);
            Assert.Equal(0, code);

            var xPath = Path.Combine(outDir, "x.txt");
            var yPath = Path.Combine(outDir, "y.txt");
            Assert.True(File.Exists(xPath));
            Assert.True(File.Exists(yPath));
            Assert.Equal("hello", File.ReadAllText(xPath));
            Assert.Equal("world", File.ReadAllText(yPath));

            var text = output.ToString();
            Assert.Contains("Restored 2 file(s)", text);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}

