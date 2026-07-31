// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

internal static class WithinBoundsContext
{
    private static readonly AsyncLocal<Stack<DateTime>?> Deadlines = new();

    public static void Push(int timeoutMs)
    {
        var stack = Deadlines.Value ??= new Stack<DateTime>();
        stack.Push(DateTime.UtcNow.AddMilliseconds(timeoutMs));
    }

    public static void Pop()
    {
        var stack = Deadlines.Value;
        if (stack == null || stack.Count == 0)
            return;
        stack.Pop();
        if (stack.Count == 0)
            Deadlines.Value = null;
    }

    public static void EnsureWithinBound(string? functionName = null)
    {
        var stack = Deadlines.Value;
        if (stack == null || stack.Count == 0)
            return;

        if (DateTime.UtcNow <= stack.Peek())
            return;

        var label = string.IsNullOrEmpty(functionName) ? "Function" : $"Function '{functionName}'";
        throw new RuntimeException($"{label} exceeded @within bound.");
    }
}
