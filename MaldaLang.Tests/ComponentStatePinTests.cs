// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

/// <summary>
/// HttpServer component-state peek / pin semantics (TTL + LRU exemptions).
/// </summary>
[Collection("Sequential")]
public class ComponentStatePinTests
{
    private static void ResetStore()
    {
        HttpServerInstance.ClearAllComponentState();
        HttpServerInstance.ConfigureComponentStatePolicy(512, 128, 1_800_000);
    }

    [Fact]
    public void UiGetState_Peek_DoesNotPersistDefault()
    {
        ResetStore();
        try
        {
            var peeked = BuiltInFunctions.CallBuiltIn(
                "uiGetState",
                new List<RuntimeValue>
                {
                    RuntimeValue.String("board"),
                    RuntimeValue.String("missing"),
                    RuntimeValue.String("default-only")
                },
                null);
            Assert.Equal(ValueType.String, peeked.Type);
            Assert.Equal("default-only", peeked.AsString());

            var again = BuiltInFunctions.CallBuiltIn(
                "uiGetState",
                new List<RuntimeValue>
                {
                    RuntimeValue.String("board"),
                    RuntimeValue.String("missing")
                },
                null);
            Assert.Equal(ValueType.Null, again.Type);

            var objectPeek = BuiltInFunctions.CallBuiltIn(
                "uiGetState",
                new List<RuntimeValue>
                {
                    RuntimeValue.String("board"),
                    RuntimeValue.String("missing"),
                    RuntimeValue.Object(new JsonObject()),
                    RuntimeValue.String("scopeA")
                },
                null);
            Assert.Equal(ValueType.Object, objectPeek.Type);

            var scopedAgain = HttpServerInstance.GetComponentState("scopeA::board", "missing");
            Assert.Equal(ValueType.Null, scopedAgain.Type);
        }
        finally
        {
            ResetStore();
        }
    }

    [Fact]
    public void UiState_GetOrCreate_PersistsEmptyDefault()
    {
        ResetStore();
        try
        {
            var created = BuiltInFunctions.CallBuiltIn(
                "uiState",
                new List<RuntimeValue>
                {
                    RuntimeValue.String("board"),
                    RuntimeValue.String("session"),
                    RuntimeValue.Object(new JsonObject())
                },
                null);
            Assert.Equal(ValueType.Object, created.Type);

            var stored = HttpServerInstance.GetComponentState("board", "session");
            Assert.Equal(ValueType.Object, stored.Type);
        }
        finally
        {
            ResetStore();
        }
    }

    [Fact]
    public void Pin_SurvivesShortTtl_WhileUnpinnedExpires()
    {
        ResetStore();
        try
        {
            HttpServerInstance.ConfigureComponentStatePolicy(64, 128, 40);
            HttpServerInstance.SetComponentState("ephemeral", "v", RuntimeValue.String("gone-soon"));
            HttpServerInstance.PinComponentState("durable");
            HttpServerInstance.SetComponentState("durable", "v", RuntimeValue.String("keep"));

            Thread.Sleep(80);

            // Any access runs cleanup of expired unpinned entries.
            Assert.Equal(ValueType.Null, HttpServerInstance.GetComponentState("ephemeral", "v").Type);
            Assert.Equal("keep", HttpServerInstance.GetComponentState("durable", "v").AsString());
            Assert.True(HttpServerInstance.IsComponentStatePinned("durable"));
        }
        finally
        {
            ResetStore();
        }
    }

    [Fact]
    public void Pin_SurvivesLruEviction()
    {
        ResetStore();
        try
        {
            HttpServerInstance.ConfigureComponentStatePolicy(3, 128, 1_800_000);
            HttpServerInstance.PinComponentState("keep");
            HttpServerInstance.SetComponentState("keep", "v", RuntimeValue.String("pinned-value"));
            HttpServerInstance.SetComponentState("u1", "v", RuntimeValue.String("1"));
            Thread.Sleep(5);
            HttpServerInstance.SetComponentState("u2", "v", RuntimeValue.String("2"));
            Thread.Sleep(5);
            // Store is full (keep, u1, u2). Next insert must evict oldest unpinned (u1), not keep.
            HttpServerInstance.SetComponentState("u3", "v", RuntimeValue.String("3"));

            Assert.Equal("pinned-value", HttpServerInstance.GetComponentState("keep", "v").AsString());
            Assert.Equal(ValueType.Null, HttpServerInstance.GetComponentState("u1", "v").Type);
            Assert.Equal("3", HttpServerInstance.GetComponentState("u3", "v").AsString());
        }
        finally
        {
            ResetStore();
        }
    }

    [Fact]
    public void Pin_CapacityFullOfPinned_ThrowsOnNewEntry()
    {
        ResetStore();
        try
        {
            HttpServerInstance.ConfigureComponentStatePolicy(2, 128, 1_800_000);
            BuiltInFunctions.CallBuiltIn(
                "componentStatePin",
                new List<RuntimeValue> { RuntimeValue.String("a") },
                null);
            BuiltInFunctions.CallBuiltIn(
                "uiPinState",
                new List<RuntimeValue> { RuntimeValue.String("b") },
                null);

            var ex = Assert.Throws<Exception>(() =>
                HttpServerInstance.SetComponentState("c", "v", RuntimeValue.String("nope")));
            Assert.Contains("pinned", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("componentStateConfigure", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            ResetStore();
        }
    }

    [Fact]
    public void Unpin_AllowsLaterEviction()
    {
        ResetStore();
        try
        {
            HttpServerInstance.ConfigureComponentStatePolicy(2, 128, 1_800_000);
            HttpServerInstance.PinComponentState("a");
            HttpServerInstance.SetComponentState("a", "v", RuntimeValue.String("A"));
            HttpServerInstance.SetComponentState("b", "v", RuntimeValue.String("B"));

            BuiltInFunctions.CallBuiltIn(
                "uiUnpinState",
                new List<RuntimeValue> { RuntimeValue.String("a") },
                null);
            Assert.False(HttpServerInstance.IsComponentStatePinned("a"));

            HttpServerInstance.SetComponentState("c", "v", RuntimeValue.String("C"));
            // One of a/b was evicted; c exists; a may or may not depending on LRU order.
            Assert.Equal("C", HttpServerInstance.GetComponentState("c", "v").AsString());
        }
        finally
        {
            ResetStore();
        }
    }
}
