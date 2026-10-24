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
        Assert.Equal(UiProtocol.Version, obj.Get("version", null).AsString());
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

    private static UiNode Field(string key) =>
        new("textField", new Dictionary<string, RuntimeValue> { ["value"] = RuntimeValue.String(key) }, key: key);

    [Fact]
    public void Session_DropsEventWhoseTargetNodeChanged()
    {
        var session = new UiSession("stale-target");
        session.Mount(new UiNode("column", children: new List<UiNode> { Field("a"), Field("b") }));

        // Typed into "b" at /1/, then a render removed "a": /1/ is gone, "b" is at /0/.
        session.EnqueueEvent(new UiEvent("input", "/1/", RuntimeValue.Null(), "e1", "textField", "b"));
        session.Render(new UiNode("column", children: new List<UiNode> { Field("b") }));
        // Typed into "b" at /0/ on the new tree, but /0/ is now another node of the same type.
        session.EnqueueEvent(new UiEvent("input", "/0/", RuntimeValue.Null(), "e2", "textField", "b"));
        session.EnqueueEvent(new UiEvent("change", "/0/", RuntimeValue.Null(), "e3", "textField", "b"));
        session.Render(new UiNode("column", children: new List<UiNode> { Field("c") }));
        // No target named: kept, as for ui.dispatchEvent.
        session.EnqueueEvent(new UiEvent("click", "/9/", RuntimeValue.Null()));

        Assert.True(session.TryDequeueEvent(out var kept));
        Assert.Equal("click", kept!.Type);
        Assert.False(session.TryDequeueEvent(out _));
        var snapshot = Assert.IsType<JsonObject>(session.SnapshotAsRuntimeValue().AsObject());
        Assert.Equal(3, snapshot.Get("staleEventsDropped", null).AsInteger());
    }

    [Fact]
    public void Session_KeepsEventWhoseTargetNodeMoved_OnlyWhenPathAndKeyMatch()
    {
        var session = new UiSession("moved-target");
        session.Mount(new UiNode("column", children: new List<UiNode> { Field("a"), Field("b") }));
        session.EnqueueEvent(new UiEvent("input", "/1/", RuntimeValue.Null(), "e1", "textField", "b"));
        session.EnqueueEvent(new UiEvent("click", "/", RuntimeValue.Null(), "e2", "column", null));

        Assert.True(session.TryDequeueEvent(out var input));
        Assert.Equal("input", input!.Type);
        Assert.Equal("b", Assert.IsType<JsonObject>(input.ToRuntimeValue().AsObject()).Get("targetKey", null).AsString());
        Assert.True(session.TryDequeueEvent(out var click));
        Assert.Equal("click", click!.Type);
    }

    [Fact]
    public void Session_RenderAnswersTheLastPulledEvent()
    {
        var session = new UiSession("answers");
        var mount = Assert.IsType<JsonObject>(session.Mount(Field("a")).AsObject());
        Assert.False(mount.GetProperties().ContainsKey("answersEventId"));

        session.EnqueueEvent(new UiEvent("input", "/", RuntimeValue.Null(), "e1", "textField", "a"));
        session.EnqueueEvent(new UiEvent("input", "/", RuntimeValue.Null(), "e2", "textField", "a"));
        Assert.True(session.TryDequeueEvent(out _));
        var first = Assert.IsType<JsonObject>(session.Render(Field("a")).AsObject());
        Assert.Equal("e1", first.Get("answersEventId", null).AsString());

        Assert.True(session.TryDequeueEvent(out _));
        var second = Assert.IsType<JsonObject>(session.Render(Field("a")).AsObject());
        Assert.Equal("e2", second.Get("answersEventId", null).AsString());
    }

    [Fact]
    public void Session_FullQueueReportsDroppedEvents()
    {
        var session = new UiSession("overflow");
        session.ConfigureQueueDepth(32);
        for (var i = 0; i < 35; i++)
            session.EnqueueEvent(new UiEvent("click", "/", RuntimeValue.Integer(i)));

        // The oldest three were dropped: an overflow event says so before the remaining ones.
        Assert.True(session.TryDequeueEvent(out var overflow));
        Assert.Equal("overflow", overflow!.Type);
        Assert.Equal(3, Assert.IsType<JsonObject>(overflow.Payload.AsObject()).Get("dropped", null).AsInteger());
        Assert.True(session.TryDequeueEvent(out var next));
        Assert.Equal(3, next!.Payload.AsInteger());

        session.EnqueueEvent(new UiEvent("click", "/", RuntimeValue.Integer(35)));
        Assert.True(session.TryDequeueEvent(out var notOverflow));
        Assert.Equal("click", notOverflow!.Type);
        var snapshot = Assert.IsType<JsonObject>(session.SnapshotAsRuntimeValue().AsObject());
        Assert.Equal(3, snapshot.Get("droppedEvents", null).AsInteger());
    }

    [Fact]
    public void Session_TreeHashIsStableAndFollowsTheTree()
    {
        var first = Assert.IsType<JsonObject>(new UiSession("h1").Mount(Field("a")).AsObject());
        var again = Assert.IsType<JsonObject>(new UiSession("h2").Mount(Field("a")).AsObject());
        var other = Assert.IsType<JsonObject>(new UiSession("h3").Mount(Field("b")).AsObject());
        var hash = first.Get("treeHash", null).AsString();
        Assert.Matches("^[0-9a-f]{8}$", hash);
        Assert.Equal(hash, again.Get("treeHash", null).AsString());
        Assert.NotEqual(hash, other.Get("treeHash", null).AsString());
    }
}
