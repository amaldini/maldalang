// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Stack of <c>@budget</c> frames. Token/tool/cost usage is recorded on every active
/// frame so nested function and prompt bounds each trip independently.
/// </summary>
public static class ResourceBoundsContext
{
    private static readonly AsyncLocal<Stack<Frame>?> Frames = new();
    private static readonly object Sync = new();

    private sealed class Frame
    {
        public required ResourceBudget Budget { get; init; }
        public required string Label { get; init; }
        public int UsedTokens;
        public int UsedTools;
        public double UsedCost;
    }

    public static bool HasActiveBound
    {
        get
        {
            var stack = Frames.Value;
            return stack != null && stack.Count > 0;
        }
    }

    public static void Push(ResourceBudget budget, string? label = null)
    {
        if (budget == null || !budget.HasAnyBound)
            return;

        lock (Sync)
        {
            var stack = Frames.Value ??= new Stack<Frame>();
            stack.Push(new Frame
            {
                Budget = budget,
                Label = string.IsNullOrEmpty(label) ? "Call" : label
            });
        }
    }

    public static void Pop()
    {
        lock (Sync)
        {
            var stack = Frames.Value;
            if (stack == null || stack.Count == 0)
                return;
            stack.Pop();
            if (stack.Count == 0)
                Frames.Value = null;
        }
    }

    public static void RecordTokens(int tokens)
    {
        if (tokens <= 0)
            return;
        MutateFrames(frame =>
        {
            frame.UsedTokens += tokens;
            if (frame.Budget.MaxTokens is int max && frame.UsedTokens > max)
                ThrowExceeded(frame, "tokens");
        });
    }

    public static void RecordToolInvocation()
    {
        MutateFrames(frame =>
        {
            frame.UsedTools++;
            if (frame.Budget.MaxTools is int max && frame.UsedTools > max)
                ThrowExceeded(frame, "tools");
        });
    }

    public static void RecordCost(double cost)
    {
        if (cost <= 0)
            return;
        MutateFrames(frame =>
        {
            frame.UsedCost += cost;
            if (frame.Budget.MaxCost is double max && frame.UsedCost > max)
                ThrowExceeded(frame, "cost");
        });
    }

    private static void MutateFrames(Action<Frame> action)
    {
        lock (Sync)
        {
            var stack = Frames.Value;
            if (stack == null || stack.Count == 0)
                return;

            foreach (var frame in stack)
                action(frame);
        }
    }

    private static void ThrowExceeded(Frame frame, string key)
    {
        throw new RuntimeException($"{frame.Label} exceeded @budget {key} bound.");
    }
}
