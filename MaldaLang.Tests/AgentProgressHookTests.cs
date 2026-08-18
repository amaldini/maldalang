// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class AgentProgressHookTests
{
    [Fact]
    public void OnAgentProgress_Handler_ReceivesEventShape()
    {
        var received = new List<RuntimeValue>();
        try
        {
            ConversationInstance.SetAgentProgressHandler(evt => received.Add(evt));

            var payload = new JsonObject();
            payload.Set("phase", RuntimeValue.String("tool_calls"));
            payload.Set("round", RuntimeValue.Integer(2));
            payload.Set("message", RuntimeValue.String("Running tools: read_file"));
            payload.Set("tools", RuntimeValue.Array(new List<RuntimeValue>
            {
                RuntimeValue.String("read_file")
            }));
            ConversationInstance.DeliverAgentProgress(RuntimeValue.Object(payload));

            Assert.Single(received);
            var obj = received[0].AsObject();
            Assert.Equal("tool_calls", obj.Get("phase", null)!.AsString());
            Assert.Equal(2, obj.Get("round", null)!.AsInteger());
            Assert.Equal("Running tools: read_file", obj.Get("message", null)!.AsString());
            Assert.Equal("read_file", obj.Get("tools", null)!.AsArray()[0].AsString());
        }
        finally
        {
            ConversationInstance.ClearAgentProgressHandler();
        }
    }

    [Fact]
    public void OnAgentProgress_ChannelForm_RegistersWithoutInterpreter()
    {
        try
        {
            var result = BuiltInFunctions.CallBuiltIn(
                "onAgentProgress",
                new List<RuntimeValue> { RuntimeValue.String("ask") },
                null);
            Assert.Equal(ValueType.Null, result.Type);

            var received = new List<RuntimeValue>();
            ConversationInstance.SetAgentProgressHandler(evt => received.Add(evt));

            var payload = new JsonObject();
            payload.Set("phase", RuntimeValue.String("round_start"));
            payload.Set("round", RuntimeValue.Integer(1));
            payload.Set("message", RuntimeValue.String("Calling LLM…"));
            ConversationInstance.DeliverAgentProgress(RuntimeValue.Object(payload));

            Assert.Single(received);
            Assert.Equal("round_start", received[0].AsObject().Get("phase", null)!.AsString());
        }
        finally
        {
            BuiltInFunctions.CallBuiltIn("clearAgentProgress", new List<RuntimeValue>(), null);
        }
    }

    [Fact]
    public void ClearAgentProgress_RemovesHandler()
    {
        var received = new List<RuntimeValue>();
        ConversationInstance.SetAgentProgressHandler(evt => received.Add(evt));
        BuiltInFunctions.CallBuiltIn("clearAgentProgress", new List<RuntimeValue>(), null);

        var payload = new JsonObject();
        payload.Set("phase", RuntimeValue.String("done"));
        payload.Set("round", RuntimeValue.Integer(1));
        ConversationInstance.DeliverAgentProgress(RuntimeValue.Object(payload));

        Assert.Empty(received);
    }

    [Fact]
    public async Task OnAgentProgress_LiveChannel_IsIsolatedPerAsyncFlow()
    {
        var seen = new ConcurrentDictionary<string, string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task RunFlow(string channel)
        {
            ConversationInstance.SetAgentProgressLiveChannel(channel);
            await gate.Task.ConfigureAwait(false);
            // Prefer AsyncLocal over the process-wide fallback while peers run.
            seen[channel] = ConversationInstance.GetAgentProgressLiveChannel() ?? "";
            var payload = new JsonObject();
            payload.Set("phase", RuntimeValue.String("round_start"));
            payload.Set("round", RuntimeValue.Integer(1));
            payload.Set("message", RuntimeValue.String(channel));
            // Deliver must still resolve this flow's channel (not the peer's).
            ConversationInstance.DeliverAgentProgress(RuntimeValue.Object(payload));
            Assert.Equal(channel, ConversationInstance.GetAgentProgressLiveChannel());
            // Clear only this async flow's AsyncLocal; do not await peers first.
            ConversationInstance.SetAgentProgressLiveChannel(null);
        }

        try
        {
            var a = Task.Run(() => RunFlow("ask-aaa"));
            var b = Task.Run(() => RunFlow("ask-bbb"));
            await Task.Delay(50);
            gate.SetResult();
            await Task.WhenAll(a, b);

            Assert.Equal("ask-aaa", seen["ask-aaa"]);
            Assert.Equal("ask-bbb", seen["ask-bbb"]);
        }
        finally
        {
            ConversationInstance.ClearAgentProgressHandler();
        }
    }

    [Fact]
    public void LiveDraft_ContentDeltas_EmitThrottledDraftPhase()
    {
        var received = new List<RuntimeValue>();
        var previousInterval = ConversationInstance.LiveDraftMinIntervalMs;
        ConversationInstance.LiveDraftMinIntervalMs = 60_000;
        try
        {
            ConversationInstance.SetAgentProgressHandler(evt => received.Add(evt));
            ConversationInstance.SetAgentProgressLiveChannel("ask-draft");
            ConversationInstance.ResetLiveDraft();
            var conv = new ConversationInstance();

            conv.HandleLiveDraftDelta(new LlmStreamDelta("reasoning", "hidden"));
            conv.HandleLiveDraftDelta(new LlmStreamDelta("tool_arguments", "{}"));
            conv.HandleLiveDraftDelta(new LlmStreamDelta("content", "Hello"));
            conv.HandleLiveDraftDelta(new LlmStreamDelta("content", " world"));

            Assert.Single(received);
            var first = received[0].AsObject();
            Assert.Equal("draft", first.Get("phase", null)!.AsString());
            Assert.Equal("Hello", first.Get("text", null)!.AsString());

            conv.FlushLiveDraft();
            Assert.Equal(2, received.Count);
            Assert.Equal("Hello world", received[1].AsObject().Get("text", null)!.AsString());
        }
        finally
        {
            ConversationInstance.LiveDraftMinIntervalMs = previousInterval;
            ConversationInstance.ClearAgentProgressHandler();
        }
    }

    [Fact]
    public void LiveDraft_WithoutLiveChannel_DoesNotEmit()
    {
        var received = new List<RuntimeValue>();
        var previousInterval = ConversationInstance.LiveDraftMinIntervalMs;
        ConversationInstance.LiveDraftMinIntervalMs = 0;
        try
        {
            ConversationInstance.SetAgentProgressHandler(evt => received.Add(evt));
            ConversationInstance.ResetLiveDraft();
            var conv = new ConversationInstance();
            conv.HandleLiveDraftDelta(new LlmStreamDelta("content", "nope"));
            conv.FlushLiveDraft();
            Assert.Empty(received);
        }
        finally
        {
            ConversationInstance.LiveDraftMinIntervalMs = previousInterval;
            ConversationInstance.ClearAgentProgressHandler();
        }
    }

    [Fact]
    public async Task LiveDraft_IsIsolatedPerAsyncFlow()
    {
        var texts = new ConcurrentDictionary<string, string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousInterval = ConversationInstance.LiveDraftMinIntervalMs;
        ConversationInstance.LiveDraftMinIntervalMs = 0;
        try
        {
            ConversationInstance.SetAgentProgressHandler(evt =>
            {
                var text = evt.AsObject().Get("text", null)?.AsString() ?? "";
                if (text.Length > 0)
                    texts[text] = text;
            });

            async Task RunFlow(string channel, string token)
            {
                ConversationInstance.SetAgentProgressLiveChannel(channel);
                ConversationInstance.ResetLiveDraft();
                var conv = new ConversationInstance();
                conv.HandleLiveDraftDelta(new LlmStreamDelta("content", token));
                await gate.Task.ConfigureAwait(false);
                conv.HandleLiveDraftDelta(new LlmStreamDelta("content", token));
                conv.FlushLiveDraft();
                ConversationInstance.SetAgentProgressLiveChannel(null);
            }

            var a = Task.Run(() => RunFlow("ask-aaa", "AAA"));
            var b = Task.Run(() => RunFlow("ask-bbb", "BBB"));
            await Task.Delay(50);
            gate.SetResult();
            await Task.WhenAll(a, b);

            Assert.Contains("AAAAAA", texts.Keys);
            Assert.Contains("BBBBBB", texts.Keys);
            Assert.DoesNotContain(texts.Keys, key => key.Contains("AAA", StringComparison.Ordinal) && key.Contains("BBB", StringComparison.Ordinal));
        }
        finally
        {
            ConversationInstance.LiveDraftMinIntervalMs = previousInterval;
            ConversationInstance.ClearAgentProgressHandler();
        }
    }
}
