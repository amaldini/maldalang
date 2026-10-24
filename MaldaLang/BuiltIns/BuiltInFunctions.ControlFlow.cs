// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInAll(List<RuntimeValue> args)
    {
        // all(tasks...) best-effort structured concurrency:
        // - Accepts either a single array argument or multiple arguments
        // - Each element/argument is treated as a task; non-task values are treated as already completed
        // - Returns a Task RuntimeValue whose result is an array of results in input order
        //
        // Best-effort semantics:
        // - All child tasks are awaited; no cancellation is requested on failure
        // - If one or more tasks fail, the first error is rethrown after all have completed
        //
        // Note: this function only composes existing tasks; it does not start them itself.

        // Normalize arguments into a flat list of RuntimeValues
        List<RuntimeValue> taskValues;
        if (args.Count == 1 && args[0].Type == ValueType.Array)
        {
            taskValues = args[0].AsArray();
        }
        else
        {
            taskValues = args;
        }

        var taskList = new List<Task<RuntimeValue>>(taskValues.Count);

        foreach (var v in taskValues)
        {
            if (v.Type == ValueType.Task)
            {
                taskList.Add(v.AsTask());
            }
            else
            {
                // Treat non-task values as already-completed tasks
                taskList.Add(System.Threading.Tasks.Task.FromResult(v));
            }
        }

        async Task<RuntimeValue> ImplAsync()
        {
            if (taskList.Count == 0)
            {
                return RuntimeValue.Array(new List<RuntimeValue>());
            }

            var tasks = taskList.ToArray();

            var results = new List<RuntimeValue>(tasks.Length);
            foreach (var task in tasks)
            {
                try
                {
                    results.Add(await task.ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    results.Add(AgentErrorStdLib.Upstream(0, ex.Message));
                }
            }

            return RuntimeValue.Array(results);
        }

        return RuntimeValue.Task(ImplAsync());
    }

    private static RuntimeValue BuiltInReply(List<RuntimeValue> args, Interpreter interpreter)
    {
        if (args.Count != 1)
            throw new Exception("reply() expects exactly 1 argument.");

        var currentActor = interpreter.GetCurrentActor();
        if (currentActor == null)
            throw new Exception("reply() can only be called from within an actor message handler.");

        var currentMessage = interpreter.GetCurrentMessage();
        if (currentMessage == null)
            throw new Exception("reply() can only be used while handling a message.");

        if (currentMessage.Sender == null)
            throw new Exception("reply() cannot be used when there is no sender to reply to.");

        var replyTo = currentMessage.Sender;
        var payload = args[0];
        if (replyTo.Instance.Home != currentActor.Home)
            payload = WorkerBoundary.Cross(payload, (currentActor.Home ?? replyTo.Instance.Home!).Actor.Name);

        // Create reply message with correlation id pointing back to the original request
        var replyMessage = new Message(
            payload,
            sender: new ActorReference(currentActor, currentActor.Id),
            handlerName: null,
            correlationId: currentMessage.Id,
            arguments: new List<RuntimeValue> { payload });

        replyTo.Instance.Mailbox.Send(replyMessage);

        return RuntimeValue.Null();
    }
    
    // ========== Error Handling & Control Flow ==========
    
    private static RuntimeValue BuiltInExit(List<RuntimeValue> args)
    {
        BuiltInArity.Require("exit", args, 0, 1, "code?");
        int exitCode = 0;
        if (args.Count > 0)
        {
            if (args[0].Type == ValueType.Integer)
                exitCode = args[0].AsInteger();
            else
                throw new Exception("exit() code must be an integer");
        }
        System.Environment.Exit(exitCode);
        return RuntimeValue.Null(); // Never reached
    }
    
    private static RuntimeValue BuiltInError(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("error() expects 1 argument: (message)");
        if (args[0].Type != ValueType.String)
            throw new Exception("error() message must be a string");
        
        var message = args[0].AsString();
        throw new RuntimeException(message);
    }
    
    private static RuntimeValue BuiltInAssert(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("assert() expects 1-2 arguments: (condition, message?)");
        
        var condition = args[0];
        var isTrue = condition.IsTruthy();
        
        if (!isTrue)
        {
            var message = args.Count > 1 && args[1].Type == ValueType.String 
                ? args[1].AsString() 
                : "Assertion failed";
            throw new RuntimeException(message);
        }
        
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInExpect(List<RuntimeValue> args)
    {
        BuiltInArity.Require("expect", args, 1, 2, "condition, message?");
        if (!args[0].IsTruthy())
        {
            var message = args.Count > 1 && args[1].Type == ValueType.String
                ? args[1].AsString()
                : "Expectation failed";
            throw new RuntimeException(message);
        }

        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInRace(List<RuntimeValue> args)
    {
        BuiltInArity.Require("race", args, 1, BuiltInArity.Unbounded, "tasks...");
        var tasks = FlattenTaskArgs(args);
        return RaceOrFirst(tasks, firstOk: false);
    }

    private static RuntimeValue BuiltInFirstOk(List<RuntimeValue> args)
    {
        BuiltInArity.Require("firstOk", args, 1, BuiltInArity.Unbounded, "tasks...");
        var tasks = FlattenTaskArgs(args);
        return RaceOrFirst(tasks, firstOk: true);
    }

    private static List<RuntimeValue> FlattenTaskArgs(List<RuntimeValue> args)
    {
        if (args.Count == 1 && args[0].Type == ValueType.Array)
            return args[0].AsArray();
        return args;
    }

    private static RuntimeValue RaceOrFirst(List<RuntimeValue> taskValues, bool firstOk)
    {
        Exception? firstError = null;
        foreach (var value in taskValues)
        {
            try
            {
                var result = value.Type == ValueType.Task
                    ? value.AsTask().GetAwaiter().GetResult()
                    : value;
                if (!firstOk || result.Type != ValueType.Variant || result.AsVariant().Tag != "Timeout")
                    return result;
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        if (firstError != null)
            throw firstError;
        return RuntimeValue.Null();
    }
}
