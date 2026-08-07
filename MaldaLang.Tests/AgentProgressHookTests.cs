// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using Xunit;

namespace MaldaLang.Tests;

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
}
