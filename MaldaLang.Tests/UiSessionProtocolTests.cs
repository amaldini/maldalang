// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.UI;
using Xunit;

namespace MaldaLang.Tests;

public class UiSessionProtocolTests
{
    [Fact]
    public void Session_MountEnvelope_ContainsProtocolMetadata()
    {
        var session = new UiSession("s1");
        var root = new UiNode("text", new Dictionary<string, RuntimeValue> { ["value"] = RuntimeValue.String("ok") });

        var envelope = session.Mount(root);
        var obj = Assert.IsType<JsonObject>(envelope.AsObject());
        Assert.Equal("mount", obj.Get("type", null).AsString());
        Assert.Equal("1.0", obj.Get("version", null).AsString());
        Assert.Equal("s1", obj.Get("sessionId", null).AsString());
        Assert.True(obj.Get("sequence", null).AsInteger() >= 1);
    }

    [Fact]
    public void Session_SequenceValidation_DetectsGapAndDuplicate()
    {
        var session = new UiSession("s2");

        Assert.True(session.TryAcceptInboundSequence(1, out _));
        Assert.False(session.TryAcceptInboundSequence(1, out var duplicateReason));
        Assert.Contains("duplicate", duplicateReason ?? string.Empty);
        Assert.False(session.TryAcceptInboundSequence(3, out var gapReason));
        Assert.Contains("gap", gapReason ?? string.Empty);
        Assert.True(session.TryAcceptInboundSequence(2, out _));
    }

    [Fact]
    public void Session_LifecycleHook_EnqueuesLifecycleEvent()
    {
        var session = new UiSession("s3");
        session.RegisterLifecycleHook("onMount", "CounterRoot");

        var props = new Dictionary<string, RuntimeValue>
        {
            ["componentId"] = RuntimeValue.String("CounterRoot")
        };
        var root = new UiNode("column", props, new List<UiNode>());
        _ = session.Mount(root);

        Assert.True(session.TryDequeueEvent(out var evt));
        Assert.NotNull(evt);
        Assert.Equal("lifecycle", evt!.Type);
    }

    [Fact]
    public void Session_NewLifecycleHooks_EmitInDeterministicOrder()
    {
        var session = new UiSession("s4");
        var componentId = "CounterRoot";
        session.RegisterLifecycleHook("onInit", componentId);
        session.RegisterLifecycleHook("onMount", componentId);
        session.RegisterLifecycleHook("onPreRender", componentId);
        session.RegisterLifecycleHook("onLoad", componentId);
        session.RegisterLifecycleHook("onUpdate", componentId);
        session.RegisterLifecycleHook("onUnmount", componentId);
        session.RegisterLifecycleHook("onDispose", componentId);

        var root = new UiNode("column", new Dictionary<string, RuntimeValue> { ["componentId"] = RuntimeValue.String(componentId) }, new List<UiNode>());
        _ = session.Mount(root);
        _ = session.Render(root);
        _ = session.Render(new UiNode("text", new Dictionary<string, RuntimeValue> { ["value"] = RuntimeValue.String("empty") }, new List<UiNode>()));

        var eventNames = new List<string>();
        while (session.TryDequeueEvent(out var evt) && evt != null)
        {
            var payload = Assert.IsType<JsonObject>(evt.Payload.AsObject());
            eventNames.Add(payload.Get("eventName", null).AsString());
        }

        Assert.Equal(
            new[] { "onInit", "onMount", "onPreRender", "onUpdate", "onUnmount", "onDispose" },
            eventNames);
    }

    [Fact]
    public void Session_OnErrorHook_EmitsLifecycleEventWhenTriggered()
    {
        var session = new UiSession("s4_error");
        var componentId = "CounterRoot";
        session.RegisterLifecycleHook("onError", componentId);

        var payload = new JsonObject();
        payload.Set("message", RuntimeValue.String("boom"));
        session.EmitLifecycleHook("onError", componentId, RuntimeValue.Object(payload));

        Assert.True(session.TryDequeueEvent(out var evt));
        Assert.NotNull(evt);
        Assert.Equal("lifecycle", evt!.Type);

        var lifecyclePayload = Assert.IsType<JsonObject>(evt.Payload.AsObject());
        Assert.Equal("onError", lifecyclePayload.Get("eventName", null).AsString());
        Assert.Equal(componentId, lifecyclePayload.Get("componentId", null).AsString());
        var errorPayload = Assert.IsType<JsonObject>(lifecyclePayload.Get("payload", null).AsObject());
        Assert.Equal("boom", errorPayload.Get("message", null).AsString());
    }

    [Fact]
    public void Session_DisposeTrackedComponents_EmitsOnDisposeForTrackedIds()
    {
        var session = new UiSession("s5");
        var componentId = "CounterRoot";
        session.RegisterLifecycleHook("onDispose", componentId);

        var root = new UiNode("column", new Dictionary<string, RuntimeValue> { ["componentId"] = RuntimeValue.String(componentId) }, new List<UiNode>());
        _ = session.Mount(root);
        _ = session.Render(root);
        session.DisposeTrackedComponents();

        var foundDispose = false;
        while (session.TryDequeueEvent(out var evt) && evt != null)
        {
            var payload = evt.Payload.AsObject() as JsonObject;
            if (payload == null)
            {
                continue;
            }

            if (payload.Get("eventName", null).Type == MaldaLang.Interpreter.ValueType.String &&
                payload.Get("eventName", null).AsString() == "onDispose")
            {
                foundDispose = true;
                break;
            }
        }

        Assert.True(foundDispose);
    }

    [Fact]
    public void Session_MountEnvelope_EnforcesMaxPayloadBytes()
    {
        var session = new UiSession("s6")
        {
            MaxPayloadSizeBytes = 64
        };

        var bigValue = new string('x', 512);
        var root = new UiNode("text", new Dictionary<string, RuntimeValue>
        {
            ["value"] = RuntimeValue.String(bigValue)
        });

        var ex = Assert.Throws<Exception>(() => session.Mount(root));
        Assert.Contains("maxPayloadBytes", ex.Message);
    }
}
