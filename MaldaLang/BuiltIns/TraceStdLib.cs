// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Runtime.Journal;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// In-language run journal: <c>trace.span</c>, <c>trace.journal</c>, <c>trace.lastUsage</c>.
/// </summary>
public static class TraceStdLib
{
    public static RuntimeValue Span(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("span", args, 2, 2, "name, fn");
        if (args[0].Type != ValueType.String)
            throw new RuntimeException("trace.span() name must be a string");
        if (args[1].Type != ValueType.Function)
            throw new RuntimeException("trace.span() second argument must be a function");

        var name = args[0].AsString();
        using (RunJournal.Current.PushSpan(name))
        {
            var fn = args[1].AsFunction();
            if (interpreter == null)
                throw new RuntimeException("trace.span() requires an interpreter");
            return interpreter.CallFunctionAsync(fn, new List<RuntimeValue>()).GetAwaiter().GetResult();
        }
    }

    public static async Task<RuntimeValue> SpanAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        BuiltInArity.Require("span", args, 2, 2, "name, fn");
        if (args[0].Type != ValueType.String)
            throw new RuntimeException("trace.span() name must be a string");
        if (args[1].Type != ValueType.Function)
            throw new RuntimeException("trace.span() second argument must be a function");

        var name = args[0].AsString();
        using (RunJournal.Current.PushSpan(name))
        {
            return await interpreter.CallFunctionAsync(args[1].AsFunction(), new List<RuntimeValue>());
        }
    }

    public static RuntimeValue Journal(List<RuntimeValue> args)
    {
        BuiltInArity.Require("journal", args, 0, 0, "");
        return RunJournal.Current.ToRuntimeArray();
    }

    public static RuntimeValue LastUsage(List<RuntimeValue> args)
    {
        BuiltInArity.Require("lastUsage", args, 0, 0, "");
        var usage = RunJournal.Current.LastUsage;
        return usage == null ? RuntimeValue.Null() : usage.ToRuntimeValue();
    }
}

public sealed class TraceInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.TraceMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        methodName switch
        {
            "span" => TraceStdLib.Span(args, interpreter),
            "journal" => TraceStdLib.Journal(args),
            "lastUsage" => TraceStdLib.LastUsage(args),
            _ => throw new Exception($"Unknown trace method: {methodName}")
        };

    public override Task<RuntimeValue> CallMethodAsync(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        methodName switch
        {
            "span" => TraceStdLib.SpanAsync(args, interpreter),
            _ => Task.FromResult(CallMethod(methodName, args, interpreter))
        };
}
