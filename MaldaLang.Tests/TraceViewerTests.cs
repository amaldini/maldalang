// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Tests for TraceViewerService, TraceSummaryHelper, TracePayloadHelper,
// and basic selection/step behavior (ReplayContext.StepTo when changing selection).

using System;
using System.IO;
using MaldaLang.Runtime.Tracing;
using MaldaLang.TraceViewer;

namespace MaldaLang.Tests;

public class TraceViewerTests : TestBase
{
    [Fact]
    public void TraceViewerService_LoadTrace_EventsCountMatchesParsed()
    {
        var tempDir = CreateTempDirectory("trace_viewer_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"role\":\"user\",\"content\":\"hi\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"FileEdit\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"path\":\"/x.txt\",\"operation\":\"overwrite\",\"afterContent\":\"hello\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":2,\"timestampUtc\":\"2026-01-27T10:15:32.123Z\",\"type\":\"RunCommand\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"command\":\"echo\",\"arguments\":[],\"workingDirectory\":\".\",\"exitCode\":0,\"stdout\":\"\",\"stderr\":\"\",\"durationMs\":5}}"
            };
            File.WriteAllLines(tracePath, lines);

            var session = TraceViewerService.LoadTrace(tracePath);

            Assert.NotNull(session.Events);
            Assert.Equal(3, session.Events.Count);
            Assert.NotNull(session.Context);
            Assert.Equal("s1", session.SessionId);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TraceViewerService_LoadTrace_SampleSummariesMatch()
    {
        var tempDir = CreateTempDirectory("trace_viewer_summary_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"FileEdit\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"path\":\"/foo/bar.txt\",\"operation\":\"overwrite\",\"afterContent\":\"x\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"RunCommand\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"command\":\"dotnet\",\"arguments\":[\"build\"],\"workingDirectory\":\".\",\"exitCode\":0,\"stdout\":\"\",\"stderr\":\"\",\"durationMs\":1200}}"
            };
            File.WriteAllLines(tracePath, lines);

            var session = TraceViewerService.LoadTrace(tracePath);

            Assert.Equal(2, session.Events.Count);
            Assert.Contains("file=", session.Events[0].Summary);
            Assert.Contains("/foo/bar.txt", session.Events[0].Summary);
            Assert.Contains("op=overwrite", session.Events[0].Summary);
            Assert.Contains("cmd=", session.Events[1].Summary);
            Assert.Contains("exit=0", session.Events[1].Summary);
            Assert.Contains("durMs=1200", session.Events[1].Summary);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TraceViewerService_LoadTraceFromContent_ProducesSameResultsAsLoadTrace()
    {
        var tempDir = CreateTempDirectory("trace_viewer_content_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var content = "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"role\":\"user\",\"content\":\"hi\"}}\n" +
                         "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"AgentMessage\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"role\":\"assistant\",\"content\":\"ok\"}}";
            File.WriteAllText(tracePath, content);

            var fromFile = TraceViewerService.LoadTrace(tracePath);
            var fromContent = TraceViewerService.LoadTraceFromContent(content);

            Assert.Equal(fromFile.Events.Count, fromContent.Events.Count);
            for (var i = 0; i < fromFile.Events.Count; i++)
            {
                Assert.Equal(fromFile.Events[i].StepIndex, fromContent.Events[i].StepIndex);
                Assert.Equal(fromFile.Events[i].Type, fromContent.Events[i].Type);
                Assert.Equal(fromFile.Events[i].Summary, fromContent.Events[i].Summary);
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TraceViewerService_SelectionStepTo_UpdatesReplayContextPosition()
    {
        var tempDir = CreateTempDirectory("trace_viewer_step_");
        try
        {
            var tracePath = Path.Combine(tempDir, "test.malda-trace.jsonl");
            var lines = new[]
            {
                "{\"sessionId\":\"s1\",\"stepIndex\":0,\"timestampUtc\":\"2026-01-27T10:15:30.123Z\",\"type\":\"LlmRequest\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":1,\"timestampUtc\":\"2026-01-27T10:15:31.123Z\",\"type\":\"LlmResponse\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"content\":\"ok\"}}",
                "{\"sessionId\":\"s1\",\"stepIndex\":2,\"timestampUtc\":\"2026-01-27T10:15:32.123Z\",\"type\":\"FileEdit\",\"agentName\":\"A\",\"conversationId\":\"c1\",\"payload\":{\"path\":\"/f\",\"operation\":\"overwrite\",\"afterContent\":\"x\"}}"
            };
            File.WriteAllLines(tracePath, lines);

            var session = TraceViewerService.LoadTrace(tracePath);

            Assert.Equal(3, session.Events.Count);

            session.Context.StepTo(0);
            Assert.Equal(0, session.Context.Position);
            Assert.NotNull(session.Context.CurrentEvent);
            Assert.Equal(TraceEventType.LlmRequest, session.Context.CurrentEvent!.Type);

            session.Context.StepTo(2);
            Assert.Equal(2, session.Context.Position);
            Assert.NotNull(session.Context.CurrentEvent);
            Assert.Equal(TraceEventType.FileEdit, session.Context.CurrentEvent!.Type);
            Assert.Single(session.Context.Files);
            Assert.Equal("x", session.Context.Files["/f"]);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TracePayloadHelper_Prettify_FormatsValidJson()
    {
        var compact = "{\"a\":1,\"b\":\"x\"}";
        var pretty = TracePayloadHelper.Prettify(compact);
        Assert.Contains("\"a\"", pretty);
        Assert.Contains("1", pretty);
        Assert.Contains("\"b\"", pretty);
        Assert.Contains("\"x\"", pretty);
    }

    [Fact]
    public void TracePayloadHelper_Prettify_HandlesInvalidJson()
    {
        var invalid = "{ invalid }";
        var result = TracePayloadHelper.Prettify(invalid);
        Assert.Contains(invalid, result);
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void TraceSummaryHelper_Summarize_FileEditReturnsPathAndOp()
    {
        var evt = new TraceEvent(
            "s1", 0, DateTime.UtcNow, TraceEventType.FileEdit,
            "{\"path\":\"/my/file.txt\",\"operation\":\"insert\",\"afterContent\":\"\"}",
            "Agent1", "c1");
        var summary = TraceSummaryHelper.Summarize(evt);
        Assert.Contains("file=", summary);
        Assert.Contains("/my/file.txt", summary);
        Assert.Contains("op=insert", summary);
    }
}
