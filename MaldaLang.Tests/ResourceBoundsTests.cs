// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

public class ResourceBoundsTests
{
    [Fact]
    public void RecordTokens_ExceedsLimit_ThrowsDedicatedMessage()
    {
        ResourceBoundsContext.Push(new ResourceBudget(maxTokens: 10), "answer");
        try
        {
            ResourceBoundsContext.RecordTokens(4);
            var ex = Assert.Throws<RuntimeException>(() => ResourceBoundsContext.RecordTokens(7));
            Assert.Contains("exceeded @budget tokens", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("answer", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ResourceBoundsContext.Pop();
        }
    }

    [Fact]
    public void RecordToolInvocation_ExceedsLimit_ThrowsDedicatedMessage()
    {
        ResourceBoundsContext.Push(new ResourceBudget(maxTools: 2), "answer");
        try
        {
            ResourceBoundsContext.RecordToolInvocation();
            ResourceBoundsContext.RecordToolInvocation();
            var ex = Assert.Throws<RuntimeException>(() => ResourceBoundsContext.RecordToolInvocation());
            Assert.Contains("exceeded @budget tools", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ResourceBoundsContext.Pop();
        }
    }

    [Fact]
    public void RecordCost_ExceedsLimit_ThrowsDedicatedMessage()
    {
        ResourceBoundsContext.Push(new ResourceBudget(maxCost: 1.25), "answer");
        try
        {
            ResourceBoundsContext.RecordCost(1.0);
            var ex = Assert.Throws<RuntimeException>(() => ResourceBoundsContext.RecordCost(0.5));
            Assert.Contains("exceeded @budget cost", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ResourceBoundsContext.Pop();
        }
    }

    [Fact]
    public void Record_WithNoActiveBound_IsNoOp()
    {
        ResourceBoundsContext.RecordTokens(1000);
        ResourceBoundsContext.RecordToolInvocation();
        ResourceBoundsContext.RecordCost(9.99);
    }

    [Fact]
    public void NestedFrames_EachBoundTripsIndependently()
    {
        ResourceBoundsContext.Push(new ResourceBudget(maxTokens: 100), "outer");
        ResourceBoundsContext.Push(new ResourceBudget(maxTokens: 5), "inner");
        try
        {
            var ex = Assert.Throws<RuntimeException>(() => ResourceBoundsContext.RecordTokens(6));
            Assert.Contains("inner", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("tokens", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ResourceBoundsContext.Pop();
            ResourceBoundsContext.Pop();
        }
    }
}
