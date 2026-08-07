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
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;

public static class BuiltInFunctions
{
    /// <summary>Windows treats env var names as case-insensitive; other platforms are case-sensitive.</summary>
    private static readonly StringComparer EnvVarNameComparer =
        System.Environment.OSVersion.Platform == PlatformID.Win32NT
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>First resolved value wins for the process lifetime (hot paths call getEnv millions of times).</summary>
    private static readonly ConcurrentDictionary<string, string?> GetEnvCache = new(EnvVarNameComparer);

    private static readonly object UiTemplateCacheLock = new();
    private static readonly Dictionary<string, string> UiTemplateCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Func<RuntimeValue, string, Task<RuntimeValue?>>> TranspiledWorkflowRunners = new(StringComparer.Ordinal);
    private static readonly AsyncLocal<int> TranspiledWorkflowDepth = new();
    private static readonly AsyncLocal<int> TranspiledWorkflowStepDepth = new();

    public sealed class TranspiledWorkflowPauseException : Exception
    {
        public TranspiledWorkflowPauseException(string message) : base(message) { }
    }

    public static void RegisterTranspiledWorkflowRunner(string workflowName, Func<RuntimeValue, string, Task<RuntimeValue?>> runner)
    {
        lock (TranspiledWorkflowRunners)
        {
            TranspiledWorkflowRunners[workflowName] = runner;
        }
    }

    public static void ClearTranspiledWorkflowRunners()
    {
        lock (TranspiledWorkflowRunners)
        {
            TranspiledWorkflowRunners.Clear();
        }
    }

    public static void EnterTranspiledWorkflowContext() => TranspiledWorkflowDepth.Value = TranspiledWorkflowDepth.Value + 1;
    public static void ExitTranspiledWorkflowContext() => TranspiledWorkflowDepth.Value = Math.Max(0, TranspiledWorkflowDepth.Value - 1);
    public static void EnterTranspiledWorkflowStep() => TranspiledWorkflowStepDepth.Value = TranspiledWorkflowStepDepth.Value + 1;
    public static void ExitTranspiledWorkflowStep() => TranspiledWorkflowStepDepth.Value = Math.Max(0, TranspiledWorkflowStepDepth.Value - 1);

    public static void RegisterBuiltIns(MaldaLang.Interpreter.Environment env)
    {
        // Built-ins are handled specially in the interpreter
        // We just need to mark them as available
        // The actual implementation is in CallBuiltIn
        
        // Configure Spectre.Console for non-terminal output (e.g., when running in IDE)
        // When output is redirected (like in IDE), disable ANSI codes for plain text output
        try
        {
            // Check if output is redirected or not a terminal
            if (System.Console.IsOutputRedirected || !AnsiConsole.Profile.Capabilities.IsTerminal)
            {
                // Force plain text output for IDE/non-terminal environments
                // Setting Ansi = false disables all ANSI codes including colors
                AnsiConsole.Profile.Capabilities.Ansi = false;
                AnsiConsole.Profile.Capabilities.Unicode = false;
            }
        }
        catch
        {
            // If detection fails, assume non-terminal and disable ANSI
            AnsiConsole.Profile.Capabilities.Ansi = false;
            AnsiConsole.Profile.Capabilities.Unicode = false;
        }
        
        // Register AnsiConsole as a global singleton instance
        var ansiConsole = new AnsiConsoleInstance();
        env.Define("AnsiConsole", RuntimeValue.Object(ansiConsole));
        
        // Register ui as a global singleton instance for server-driven UI composition.
        var uiFramework = new UiFrameworkInstance();
        env.Define("ui", RuntimeValue.Object(uiFramework));

        // math.* namespace (canonical); Math is a deprecated alias for one release
        var math = new MathInstance();
        math.Set("PI", RuntimeValue.Float(System.Math.PI));
        math.Set("E", RuntimeValue.Float(System.Math.E));
        math.Set("TAU", RuntimeValue.Float(2 * System.Math.PI));
        math.Set("INF", RuntimeValue.Float(double.PositiveInfinity));
        math.Set("NaN", RuntimeValue.Float(double.NaN));
        env.Define(StdLibNamespaces.MathModule, RuntimeValue.Object(math));
        env.Define(StdLibNamespaces.DeprecatedMathModuleAlias, RuntimeValue.Object(math));

        env.Define(StdLibNamespaces.StrModule, RuntimeValue.Object(new StrInstance()));
        env.Define(StdLibNamespaces.IoModule, RuntimeValue.Object(new IoInstance()));
        env.Define(StdLibNamespaces.ResultModule, RuntimeValue.Object(new ResultInstance()));
        env.Define(StdLibNamespaces.OptionModule, RuntimeValue.Object(new OptionInstance()));
    }
    
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

            try
            {
                var results = await System.Threading.Tasks.Task.WhenAll(tasks).ConfigureAwait(false);
                return RuntimeValue.Array(results.ToList());
            }
            catch (Exception ex)
            {
                // Task.WhenAll throws after all tasks have completed; preserve best-effort
                Exception? first = ex;

                if (ex is AggregateException agg)
                {
                    var flattened = agg.Flatten();
                    first = flattened.InnerExceptions.FirstOrDefault() ?? flattened;
                }

                // If the first inner is itself an AggregateException, unwrap one level
                if (first is AggregateException agg2)
                {
                    var flattened2 = agg2.Flatten();
                    first = flattened2.InnerExceptions.FirstOrDefault() ?? flattened2;
                }

                throw first!;
            }
        }

        return RuntimeValue.Task(ImplAsync());
    }

    private static async Task<RuntimeValue> BuiltInStartWorkflowAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("startWorkflow", args, 1, BuiltInArity.Unbounded, "name, input?, correlationId?");
        if (args[0].Type != ValueType.String) throw new Exception("startWorkflow name must be a string");
        var name = args[0].AsString();
        var input = args.Count > 1 ? args[1] : RuntimeValue.Null();
        var inputJson = input.Type == ValueType.Null ? null : CallBuiltIn("toJSON", new List<RuntimeValue> { input }, interpreter).AsString();
        var engine = WorkflowEngine.Instance;
        engine.EnsureWorkflowsEnabled("startWorkflow");
        var explicitCorrelationId = args.Count > 2 && args[2].Type == ValueType.String ? args[2].AsString() : null;
        var inferredCorrelationId = explicitCorrelationId ?? TryExtractCorrelationId(input);
        var instanceId = engine.CreateInstance(name, inputJson, inferredCorrelationId);
        if (!engine.StartInstance(instanceId)) throw new Exception($"Failed to start workflow instance {instanceId}");
        try
        {
            if (interpreter != null)
            {
                var wf = interpreter.GetWorkflow(name);
                if (wf == null) throw new Exception($"Workflow '{name}' not found");
                await interpreter.RunWorkflowBodyAsync(wf, input, instanceId);
            }
            else
            {
                Func<RuntimeValue, string, Task<RuntimeValue?>>? runner;
                lock (TranspiledWorkflowRunners)
                {
                    TranspiledWorkflowRunners.TryGetValue(name, out runner);
                }
                if (runner == null)
                    throw new Exception($"Workflow '{name}' not found");

                var result = await runner(input, instanceId);
                var inst = engine.GetInstance(instanceId);
                if (inst != null && inst.Status == WorkflowStatus.Running)
                {
                    var resultJson = result != null ? CallBuiltIn("toJSON", new List<RuntimeValue> { result }, null).AsString() : null;
                    engine.CompleteInstance(instanceId, resultJson);
                }
            }
        }
        catch (TranspiledWorkflowPauseException)
        {
            // Waiting approval/signal is a valid pause point.
        }
        catch
        {
            var inst = engine.GetInstance(instanceId);
            if (inst != null && inst.Status == WorkflowStatus.Running)
            {
                var err = JsonSerializer.Serialize(new { message = "Workflow execution failed", type = "WorkflowExecutionError" });
                engine.FailInstance(instanceId, err);
            }
            throw;
        }
        return RuntimeValue.String(instanceId);
    }

    private static RuntimeValue BuiltInGetWorkflowStatus(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getWorkflowStatus", args, 1, BuiltInArity.Unbounded, "instanceId");
        var id = args[0].AsString();
        var inst = WorkflowEngine.Instance.GetInstance(id);
        if (inst == null) return RuntimeValue.String("NOT_FOUND");
        return RuntimeValue.String(inst.Status);
    }

    private static RuntimeValue BuiltInGetWorkflow(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getWorkflow", args, 1, BuiltInArity.Unbounded, "instanceId");
        var id = args[0].AsString();
        var inst = WorkflowEngine.Instance.GetInstance(id);
        if (inst == null) return RuntimeValue.Null();
        var obj = new DictionaryInstance();
        obj.SetEntry("id", RuntimeValue.String(inst.Id));
        obj.SetEntry("name", RuntimeValue.String(inst.Name));
        obj.SetEntry("status", RuntimeValue.String(inst.Status));
        obj.SetEntry("input_json", inst.InputJson != null ? RuntimeValue.String(inst.InputJson) : RuntimeValue.Null());
        obj.SetEntry("result_json", inst.ResultJson != null ? RuntimeValue.String(inst.ResultJson) : RuntimeValue.Null());
        obj.SetEntry("error_json", inst.ErrorJson != null ? RuntimeValue.String(inst.ErrorJson) : RuntimeValue.Null());
        obj.SetEntry("created_at_utc", RuntimeValue.String(inst.CreatedAtUtc));
        obj.SetEntry("updated_at_utc", RuntimeValue.String(inst.UpdatedAtUtc));
        obj.SetEntry("started_at_utc", inst.StartedAtUtc != null ? RuntimeValue.String(inst.StartedAtUtc) : RuntimeValue.Null());
        obj.SetEntry("finished_at_utc", inst.FinishedAtUtc != null ? RuntimeValue.String(inst.FinishedAtUtc) : RuntimeValue.Null());
        obj.SetEntry("correlation_id", inst.CorrelationId != null ? RuntimeValue.String(inst.CorrelationId) : RuntimeValue.Null());
        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue BuiltInGetWorkflowSteps(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getWorkflowSteps", args, 1, BuiltInArity.Unbounded, "instanceId");
        var id = args[0].AsString();
        var steps = WorkflowEngine.Instance.GetSteps(id);
        var arr = new List<RuntimeValue>();
        foreach (var s in steps)
        {
            var obj = new DictionaryInstance();
            obj.SetEntry("id", RuntimeValue.String(s.Id));
            obj.SetEntry("step_name", RuntimeValue.String(s.StepName));
            obj.SetEntry("state", RuntimeValue.String(s.State));
            obj.SetEntry("workflow_instance_id", RuntimeValue.String(s.WorkflowInstanceId));
            obj.SetEntry("attempt", RuntimeValue.Integer(s.Attempt));
            obj.SetEntry("output_json", s.OutputJson != null ? RuntimeValue.String(s.OutputJson) : RuntimeValue.Null());
            obj.SetEntry("error_json", s.ErrorJson != null ? RuntimeValue.String(s.ErrorJson) : RuntimeValue.Null());
            arr.Add(RuntimeValue.Object(obj));
        }
        return RuntimeValue.Array(arr);
    }

    private static RuntimeValue BuiltInListWorkflows(List<RuntimeValue> args)
    {
        BuiltInArity.Require("listWorkflows", args, 0, 3, "status?, name?, limit?");
        var status = args.Count > 0 && args[0].Type == ValueType.String ? args[0].AsString() : null;
        var name = args.Count > 1 && args[1].Type == ValueType.String ? args[1].AsString() : null;
        var limit = args.Count > 2 && args[2].Type == ValueType.Integer ? args[2].AsInteger() : 100;
        var instances = WorkflowEngine.Instance.ListInstances(status, name, limit);
        var arr = new List<RuntimeValue>();
        foreach (var inst in instances)
        {
            var obj = new DictionaryInstance();
            obj.SetEntry("id", RuntimeValue.String(inst.Id));
            obj.SetEntry("name", RuntimeValue.String(inst.Name));
            obj.SetEntry("status", RuntimeValue.String(inst.Status));
            obj.SetEntry("created_at_utc", RuntimeValue.String(inst.CreatedAtUtc));
            obj.SetEntry("correlation_id", inst.CorrelationId != null ? RuntimeValue.String(inst.CorrelationId) : RuntimeValue.Null());
            arr.Add(RuntimeValue.Object(obj));
        }
        return RuntimeValue.Array(arr);
    }

    private static RuntimeValue BuiltInGetWorkflowEvents(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getWorkflowEvents", args, 1, BuiltInArity.Unbounded, "instanceId, limit?");
        var id = args[0].AsString();
        var limit = args.Count > 1 && args[1].Type == ValueType.Integer ? args[1].AsInteger() : 200;
        var events = WorkflowEngine.Instance.GetEvents(id, limit);
        var arr = new List<RuntimeValue>();
        foreach (var e in events)
        {
            var obj = new DictionaryInstance();
            obj.SetEntry("id", RuntimeValue.String(e.Id));
            obj.SetEntry("workflow_instance_id", RuntimeValue.String(e.WorkflowInstanceId));
            obj.SetEntry("event_type", RuntimeValue.String(e.EventType));
            obj.SetEntry("payload_json", RuntimeValue.String(e.PayloadJson));
            obj.SetEntry("created_at_utc", RuntimeValue.String(e.CreatedAtUtc));
            arr.Add(RuntimeValue.Object(obj));
        }

        return RuntimeValue.Array(arr);
    }

    private static RuntimeValue BuiltInGetWorkflowMetrics(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getWorkflowMetrics", args, 0, 0);
        var metrics = WorkflowEngine.Instance.GetMinimumMetricSnapshot();
        var obj = new DictionaryInstance();
        foreach (var kvp in metrics)
            obj.SetEntry(kvp.Key, RuntimeValue.Integer(kvp.Value));
        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue BuiltInCancelWorkflow(List<RuntimeValue> args)
    {
        BuiltInArity.Require("cancelWorkflow", args, 1, BuiltInArity.Unbounded, "instanceId, reason?");
        var id = args[0].AsString();
        var reason = args.Count > 1 ? args[1].AsString() : null;
        return RuntimeValue.Boolean(WorkflowEngine.Instance.CancelInstance(id, reason));
    }

    private static RuntimeValue BuiltInResumeWorkflow(List<RuntimeValue> args)
    {
        BuiltInArity.Require("resumeWorkflow", args, 1, BuiltInArity.Unbounded, "instanceId");
        var id = args[0].AsString();
        return RuntimeValue.Boolean(WorkflowEngine.Instance.ResumeInstance(id));
    }

    private static RuntimeValue BuiltInRetryWorkflow(List<RuntimeValue> args)
    {
        BuiltInArity.Require("retryWorkflow", args, 1, BuiltInArity.Unbounded, "instanceId");
        var id = args[0].AsString();
        return RuntimeValue.Boolean(WorkflowEngine.Instance.RetryInstance(id));
    }

    private static RuntimeValue BuiltInApproveWorkflowStep(List<RuntimeValue> args)
    {
        BuiltInArity.Require("approveWorkflowStep", args, 2, BuiltInArity.Unbounded, "instanceId, stepId, decision?, payload?");
        var instanceId = args[0].AsString();
        var stepId = args[1].AsString();
        var decision = args.Count > 2 ? args[2].AsString() : "approve";
        var payloadJson = args.Count > 3
            ? CallBuiltIn("toJSON", new List<RuntimeValue> { args[3] }, null).AsString()
            : null;
        return RuntimeValue.Boolean(WorkflowEngine.Instance.ResolveApproval(instanceId, stepId, decision, payloadJson, out _));
    }

    private static RuntimeValue BuiltInSignalWorkflow(List<RuntimeValue> args)
    {
        BuiltInArity.Require("signalWorkflow", args, 2, BuiltInArity.Unbounded, "instanceId, signalName, payload?");
        var instanceId = args[0].AsString();
        var signalName = args[1].AsString();
        var payloadJson = args.Count > 2
            ? CallBuiltIn("toJSON", new List<RuntimeValue> { args[2] }, null).AsString()
            : null;
        return RuntimeValue.Boolean(WorkflowEngine.Instance.DeliverSignal(instanceId, signalName, payloadJson, out _));
    }

    private static RuntimeValue BuiltInListWorkflowDeadLetters(List<RuntimeValue> args)
    {
        BuiltInArity.Require("listWorkflowDeadLetters", args, 0, 2, "limit?, includeRequeued?");
        var limit = args.Count > 0 && args[0].Type == ValueType.Integer ? args[0].AsInteger() : 100;
        var includeRequeued = args.Count <= 1 || args[1].Type != ValueType.Boolean || args[1].AsBoolean();
        var deadLetters = WorkflowEngine.Instance.ListDeadLetters(limit, includeRequeued);
        var arr = new List<RuntimeValue>();
        foreach (var dlq in deadLetters)
        {
            var obj = new DictionaryInstance();
            obj.SetEntry("id", RuntimeValue.String(dlq.Id));
            obj.SetEntry("workflow_instance_id", RuntimeValue.String(dlq.WorkflowInstanceId));
            obj.SetEntry("step_name", RuntimeValue.String(dlq.StepName));
            obj.SetEntry("reason", RuntimeValue.String(dlq.Reason));
            obj.SetEntry("payload_json", dlq.PayloadJson != null ? RuntimeValue.String(dlq.PayloadJson) : RuntimeValue.Null());
            obj.SetEntry("created_at_utc", RuntimeValue.String(dlq.CreatedAtUtc));
            obj.SetEntry("requeued_at_utc", dlq.RequeuedAtUtc != null ? RuntimeValue.String(dlq.RequeuedAtUtc) : RuntimeValue.Null());
            obj.SetEntry("requeue_reason", dlq.RequeueReason != null ? RuntimeValue.String(dlq.RequeueReason) : RuntimeValue.Null());
            obj.SetEntry("requeue_requested_by", dlq.RequeueRequestedBy != null ? RuntimeValue.String(dlq.RequeueRequestedBy) : RuntimeValue.Null());
            obj.SetEntry("requeue_correlation_id", dlq.RequeueCorrelationId != null ? RuntimeValue.String(dlq.RequeueCorrelationId) : RuntimeValue.Null());
            obj.SetEntry("requeue_attempts", RuntimeValue.Integer(dlq.RequeueAttempts));
            arr.Add(RuntimeValue.Object(obj));
        }
        return RuntimeValue.Array(arr);
    }

    private static RuntimeValue BuiltInRequeueDeadLetter(List<RuntimeValue> args)
    {
        BuiltInArity.Require("requeueDeadLetter", args, 1, BuiltInArity.Unbounded, "id, reason?, requestedBy?, requeueCorrelationId?");
        var id = args[0].AsString();
        var reason = args.Count > 1 && args[1].Type == ValueType.String ? args[1].AsString() : null;
        var requestedBy = args.Count > 2 && args[2].Type == ValueType.String ? args[2].AsString() : null;
        var requeueCorrelationId = args.Count > 3 && args[3].Type == ValueType.String ? args[3].AsString() : null;
        return RuntimeValue.Boolean(WorkflowEngine.Instance.RequeueDeadLetter(id, reason, requestedBy, requeueCorrelationId, out _));
    }

    private static async Task<RuntimeValue> BuiltInRunWorkflowInstanceAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("runWorkflowInstance", args, 1, BuiltInArity.Unbounded, "instanceId");
        var id = args[0].AsString();
        var engine = WorkflowEngine.Instance;
        engine.EnsureWorkflowsEnabled("runWorkflowInstance");
        var inst = engine.GetInstance(id);
        if (inst == null) return RuntimeValue.Null();
        if (inst.Status != WorkflowStatus.Running &&
            inst.Status != WorkflowStatus.Failed &&
            inst.Status != WorkflowStatus.WaitingApproval &&
            inst.Status != WorkflowStatus.WaitingSignal)
            return RuntimeValue.Boolean(false);
        if (inst.Status == WorkflowStatus.Failed && !engine.RetryInstance(id))
            return RuntimeValue.Boolean(false);
        if ((inst.Status == WorkflowStatus.WaitingApproval || inst.Status == WorkflowStatus.WaitingSignal) && !engine.ResumeInstance(id))
            return RuntimeValue.Boolean(false);
        var input = inst.InputJson != null
            ? CallBuiltIn("parseJSON", new List<RuntimeValue> { RuntimeValue.String(inst.InputJson) }, interpreter)
            : RuntimeValue.Null();
        try
        {
            if (interpreter != null)
            {
                var wf = interpreter.GetWorkflow(inst.Name);
                if (wf == null) throw new Exception($"Workflow '{inst.Name}' not found");
                await interpreter.RunWorkflowBodyAsync(wf, input, id);
            }
            else
            {
                Func<RuntimeValue, string, Task<RuntimeValue?>>? runner;
                lock (TranspiledWorkflowRunners)
                {
                    TranspiledWorkflowRunners.TryGetValue(inst.Name, out runner);
                }
                if (runner == null)
                    throw new Exception($"Workflow '{inst.Name}' not found");

                var result = await runner(input, id);
                var current = engine.GetInstance(id);
                if (current != null && current.Status == WorkflowStatus.Running)
                {
                    var resultJson = result != null ? CallBuiltIn("toJSON", new List<RuntimeValue> { result }, null).AsString() : null;
                    engine.CompleteInstance(id, resultJson);
                }
            }
        }
        catch (TranspiledWorkflowPauseException)
        {
            // Waiting approval/signal is a valid pause point.
        }
        catch
        {
            var current = engine.GetInstance(id);
            if (current != null && current.Status == WorkflowStatus.Running)
            {
                var err = JsonSerializer.Serialize(new { message = "Workflow execution failed", type = "WorkflowExecutionError" });
                engine.FailInstance(id, err);
            }
            throw;
        }
        return RuntimeValue.Boolean(true);
    }

    private static string? TryExtractCorrelationId(RuntimeValue input)
    {
        if (input.Type != ValueType.Object)
            return null;
        var obj = input.AsObject();
        if (obj is JsonObject jsonObj)
        {
            var candidate = jsonObj.Get("correlationId", null);
            if (candidate.Type == ValueType.String && !string.IsNullOrWhiteSpace(candidate.AsString()))
                return candidate.AsString();
        }

        if (obj is DictionaryInstance dict)
        {
            if (dict.TryGetEntry("correlationId", out var value) &&
                value.Type == ValueType.String &&
                !string.IsNullOrWhiteSpace(value.AsString()))
            {
                return value.AsString();
            }
        }

        return null;
    }

    private static RuntimeValue BuiltInRunProperty(List<RuntimeValue> args, Interpreter? interpreter)
    {
            BuiltInArity.Require("runProperty", args, 1, 3, "name, iterations?, seed?");
        if (args[0].Type != ValueType.String)
            throw new Exception("runProperty name must be a string");

        var propertyName = args[0].AsString();
        var iterations = PropertyRunOptions.DefaultIterations;
        var seed = PropertyRunOptions.DefaultSeed;

        if (args.Count > 1)
        {
            if (!NumericCoercion.TryAsInteger(args[1], out iterations))
                throw new Exception("runProperty iterations must be an integer");
            if (iterations <= 0)
                throw new Exception("runProperty iterations must be > 0");
        }

        if (args.Count > 2)
        {
            if (!NumericCoercion.TryAsInteger(args[2], out seed))
                throw new Exception("runProperty seed must be an integer");
        }

        return interpreter != null
            ? RunPropertyInInterpreterContext(interpreter, propertyName, iterations, seed)
            : RunPropertyInTranspiledContext(propertyName, iterations, seed);
    }

    private static RuntimeValue RunPropertyInInterpreterContext(Interpreter interpreter, string propertyName, int iterations, int seed)
    {
        var declaration = interpreter.GetProperty(propertyName);
        if (declaration == null)
            throw new Exception($"Property '{propertyName}' was not found.");

        var random = new Random(seed);
        var generators = declaration.Parameters.Select(CreatePropertyGeneratorForParameter).ToList();
        var timeoutMs = PropertyRunOptions.DefaultTrialTimeoutMs;

        for (var trial = 1; trial <= iterations; trial++)
        {
            var trialArgs = generators.Select(g => g.Next(random)).ToList();
            var counterexample = FormatPropertyArguments(trialArgs);
            var outcome = ExecutePropertyTrialInInterpreterWithTimeout(interpreter, declaration, trialArgs, timeoutMs);
            if (outcome.Passed)
                continue;

            return BuildRunPropertyResult(
                propertyName,
                passed: false,
                iterations: iterations,
                seed: seed,
                failedTrial: trial,
                error: outcome.ErrorMessage,
                counterexample: counterexample,
                shrunkCounterexample: null);
        }

        return BuildRunPropertyResult(
            propertyName,
            passed: true,
            iterations: iterations,
            seed: seed,
            failedTrial: null,
            error: null,
            counterexample: null,
            shrunkCounterexample: null);
    }

    private static RuntimeValue RunPropertyInTranspiledContext(string propertyName, int iterations, int seed)
    {
        var programType = ResolveTranspiledProgramType()
            ?? throw new Exception("runProperty() is unavailable: transpiled Program type was not found.");

        var metadataMethod = programType.GetMethod("GetTranspiledProperties", BindingFlags.Public | BindingFlags.Static)
            ?? throw new Exception("runProperty() is unavailable: GetTranspiledProperties() was not found.");
        var invokeMethod = programType.GetMethod("InvokeTranspiledProperty", BindingFlags.Public | BindingFlags.Static)
            ?? throw new Exception("runProperty() is unavailable: InvokeTranspiledProperty() was not found.");

        var metadataEnumerable = metadataMethod.Invoke(null, null) as System.Collections.IEnumerable
            ?? throw new Exception("runProperty() is unavailable: property metadata payload is invalid.");

        object? propertyMetadata = null;
        foreach (var item in metadataEnumerable)
        {
            if (item == null) continue;
            var itemName = item.GetType().GetProperty("Name")?.GetValue(item) as string;
            if (string.Equals(itemName, propertyName, StringComparison.Ordinal))
            {
                propertyMetadata = item;
                break;
            }
        }

        if (propertyMetadata == null)
            throw new Exception($"Property '{propertyName}' was not found in transpiled metadata.");

        var parameterNames = new List<string>();
        var parametersRaw = propertyMetadata.GetType().GetProperty("Parameters")?.GetValue(propertyMetadata) as System.Collections.IEnumerable;
        if (parametersRaw != null)
        {
            foreach (var parameter in parametersRaw)
            {
                if (parameter is string p)
                    parameterNames.Add(p);
            }
        }

        var random = new Random(seed);
        var generators = parameterNames.Select(CreatePropertyGeneratorForParameter).ToList();
        var timeoutMs = PropertyRunOptions.DefaultTrialTimeoutMs;

        for (var trial = 1; trial <= iterations; trial++)
        {
            var trialArgs = generators.Select(g => g.Next(random)).ToList();
            var counterexample = FormatPropertyArguments(trialArgs);
            var outcome = ExecutePropertyTrialInTranspiledWithTimeout(invokeMethod, propertyName, trialArgs, timeoutMs);
            if (outcome.Passed)
                continue;

            return BuildRunPropertyResult(
                propertyName,
                passed: false,
                iterations: iterations,
                seed: seed,
                failedTrial: trial,
                error: outcome.ErrorMessage,
                counterexample: counterexample,
                shrunkCounterexample: null);
        }

        return BuildRunPropertyResult(
            propertyName,
            passed: true,
            iterations: iterations,
            seed: seed,
            failedTrial: null,
            error: null,
            counterexample: null,
            shrunkCounterexample: null);
    }

    private static System.Type? ResolveTranspiledProgramType()
    {
        var entry = Assembly.GetEntryAssembly();
        var entryProgramType = entry?.GetType("GeneratedCode.Program") ?? entry?.GetType("Program");
        if (entryProgramType != null)
            return entryProgramType;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var programType = assembly.GetType("GeneratedCode.Program") ?? assembly.GetType("Program");
            if (programType != null)
                return programType;
        }

        return null;
    }

    private static PropertyTrialResult ExecutePropertyTrialInInterpreterWithTimeout(
        Interpreter interpreter,
        PropertyDeclaration declaration,
        List<RuntimeValue> trialArgs,
        int timeoutMs)
    {
        try
        {
            return ExecutePropertyTrialInInterpreterAsync(interpreter, declaration, trialArgs)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs))
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            return new PropertyTrialResult(false, $"Property trial exceeded timeout ({timeoutMs}ms).");
        }
    }

    private static async Task<PropertyTrialResult> ExecutePropertyTrialInInterpreterAsync(
        Interpreter interpreter,
        PropertyDeclaration declaration,
        List<RuntimeValue> trialArgs)
    {
        try
        {
            var functionDeclaration = new FunctionDeclaration(
                declaration.Name,
                declaration.Parameters,
                declaration.Body,
                line: declaration.Line,
                column: declaration.Column);
            var function = new FunctionValue(functionDeclaration, interpreter._globals);
            var returnValue = await interpreter.CallFunctionAsync(function, trialArgs);

            if (returnValue.Type == ValueType.Boolean && !returnValue.AsBoolean())
                return new PropertyTrialResult(false, "Property returned false.");

            return new PropertyTrialResult(true, null);
        }
        catch (RuntimeException ex)
        {
            return new PropertyTrialResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            return new PropertyTrialResult(false, ex.Message);
        }
    }

    private static PropertyTrialResult ExecutePropertyTrialInTranspiledWithTimeout(
        MethodInfo invokeMethod,
        string propertyName,
        List<RuntimeValue> trialArgs,
        int timeoutMs)
    {
        try
        {
            return ExecutePropertyTrialInTranspiledAsync(invokeMethod, propertyName, trialArgs)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs))
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            return new PropertyTrialResult(false, $"Property trial exceeded timeout ({timeoutMs}ms).");
        }
    }

    private static async Task<PropertyTrialResult> ExecutePropertyTrialInTranspiledAsync(
        MethodInfo invokeMethod,
        string propertyName,
        List<RuntimeValue> trialArgs)
    {
        try
        {
            var plainArgs = trialArgs.Select(ToPlainObject).ToArray();
            var taskObj = invokeMethod.Invoke(null, new object[] { propertyName, plainArgs });
            if (taskObj is not Task<object> task)
                return new PropertyTrialResult(false, "Transpiled property invocation did not return Task<object>.");

            var result = await task;
            if (result is bool b && !b)
                return new PropertyTrialResult(false, "Property returned false.");
            if (result is RuntimeValue rv && rv.Type == ValueType.Boolean && !rv.AsBoolean())
                return new PropertyTrialResult(false, "Property returned false.");
            return new PropertyTrialResult(true, null);
        }
        catch (TargetInvocationException ex)
        {
            return new PropertyTrialResult(false, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return new PropertyTrialResult(false, ex.Message);
        }
    }

    private static PropertyGenerator CreatePropertyGeneratorForParameter(string parameterName)
    {
        var name = parameterName.ToLowerInvariant();
        if (name.EndsWith("bool", StringComparison.Ordinal) ||
            name.StartsWith("is", StringComparison.Ordinal) ||
            name.StartsWith("has", StringComparison.Ordinal) ||
            name.Contains("flag", StringComparison.Ordinal))
        {
            return PropertyGenerators.Bool();
        }

        if (name.EndsWith("string", StringComparison.Ordinal) ||
            name.Contains("name", StringComparison.Ordinal) ||
            name.Contains("text", StringComparison.Ordinal))
        {
            return PropertyGenerators.String(16);
        }

        if (name.EndsWith("list", StringComparison.Ordinal) ||
            name.EndsWith("items", StringComparison.Ordinal) ||
            name.EndsWith("array", StringComparison.Ordinal) ||
            name == "xs")
        {
            return PropertyGenerators.List(PropertyGenerators.Int(-32, 32), 8);
        }

        if (name.Contains("any", StringComparison.Ordinal))
        {
            return PropertyGenerators.OneOf(
                PropertyGenerators.Int(-100, 100),
                PropertyGenerators.Bool(),
                PropertyGenerators.String(12),
                PropertyGenerators.List(PropertyGenerators.Int(-8, 8), 5));
        }

        return PropertyGenerators.Int(-100, 100);
    }

    private static string FormatPropertyArguments(IReadOnlyList<RuntimeValue> args)
    {
        var formatted = args.Select(arg =>
        {
            if (arg.Type == ValueType.String)
                return "\"" + arg.AsString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
            return arg.ToString();
        });
        return "[" + string.Join(", ", formatted) + "]";
    }

    private static object? ToPlainObject(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger(),
            ValueType.Float => value.AsFloat(),
            ValueType.Boolean => value.AsBoolean(),
            ValueType.String => value.AsString(),
            ValueType.Null => null,
            ValueType.Array => value.AsArray().Select(ToPlainObject).ToList(),
            _ => value.ToString()
        };
    }

    private static RuntimeValue BuildRunPropertyResult(
        string propertyName,
        bool passed,
        int iterations,
        int seed,
        int? failedTrial,
        string? error,
        string? counterexample,
        string? shrunkCounterexample)
    {
        var result = new DictionaryInstance();
        result.SetEntry("propertyName", RuntimeValue.String(propertyName));
        result.SetEntry("passed", RuntimeValue.Boolean(passed));
        result.SetEntry("iterations", RuntimeValue.Integer(iterations));
        result.SetEntry("seed", RuntimeValue.Integer(seed));
        result.SetEntry("failedTrial", failedTrial.HasValue ? RuntimeValue.Integer(failedTrial.Value) : RuntimeValue.Null());
        result.SetEntry("error", error != null ? RuntimeValue.String(error) : RuntimeValue.Null());
        result.SetEntry("counterexample", counterexample != null ? RuntimeValue.String(counterexample) : RuntimeValue.Null());
        result.SetEntry("shrunkCounterexample", shrunkCounterexample != null ? RuntimeValue.String(shrunkCounterexample) : RuntimeValue.Null());
        return RuntimeValue.Object(result);
    }

    private sealed class PropertyTrialResult
    {
        public bool Passed { get; }
        public string? ErrorMessage { get; }

        public PropertyTrialResult(bool passed, string? errorMessage)
        {
            Passed = passed;
            ErrorMessage = errorMessage;
        }
    }

    private static void CheckWorkflowDeterminism(string name, Interpreter? interpreter)
    {
        if (interpreter != null)
        {
            if (!interpreter.IsInWorkflowContext || interpreter.IsInsideWorkflowStep)
                return;
        }
        else
        {
            if (TranspiledWorkflowDepth.Value <= 0 || TranspiledWorkflowStepDepth.Value > 0)
                return;
        }

        var workflowBehavior = BuiltInRegistry.GetDescriptor(name)?.WorkflowBehavior
            ?? BuiltInRegistry.GetWorkflowBehavior(name);

        switch (workflowBehavior)
        {
            case WorkflowBuiltInBehavior.NonDeterministic:
                throw new MaldaLang.Interpreter.RuntimeException($"WF1001: Non-deterministic built-in '{name}' in deterministic workflow section");
            case WorkflowBuiltInBehavior.SideEffecting:
                throw new MaldaLang.Interpreter.RuntimeException($"WF1002: Side-effecting operation '{name}' outside step boundary");
        }
    }

    public static RuntimeValue CallBuiltIn(string name, List<RuntimeValue> args, Interpreter? interpreter)
    {
        CheckWorkflowDeterminism(name, interpreter);
        var profileToken = MaldaProfiler.EnterBuiltIn(name);
        try
        {
            return name switch
            {
            "int" => BuiltInInt(args),
            "toIntOr" => BuiltInToIntOr(args),
            "toIntOrNull" => BuiltInToIntOrNull(args),
            "float" => BuiltInFloat(args),
            "string" => BuiltInString(args),
            "formatNumber" => BuiltInFormatNumber(args),
            "abs" => BuiltInAbs(args),
            "sum" => BuiltInSum(args),
            "average" => BuiltInAverage(args),
            "max" => BuiltInMax(args),
            "min" => BuiltInMin(args),
            "pow" => BuiltInPow(args),
            "sqrt" => BuiltInSqrt(args),
            // Extended math: rounding and sign
            "floor" => BuiltInFloor(args),
            "ceil" => BuiltInCeil(args),
            "round" => BuiltInRound(args),
            "trunc" => BuiltInTrunc(args),
            "sign" => BuiltInSign(args),
            // Extended math: exponential and logarithm
            "exp" => BuiltInExp(args),
            "log" => BuiltInLog(args),
            "log10" => BuiltInLog10(args),
            "log2" => BuiltInLog2(args),
            // Extended math: trigonometry
            "sin" => BuiltInSin(args),
            "cos" => BuiltInCos(args),
            "tan" => BuiltInTan(args),
            "asin" => BuiltInAsin(args),
            "acos" => BuiltInAcos(args),
            "atan" => BuiltInAtan(args),
            "atan2" => BuiltInAtan2(args),
            // Extended math: utility
            "hypot" => BuiltInHypot(args),
            "clamp" => BuiltInClamp(args),
            "degToRad" => BuiltInDegToRad(args),
            "radToDeg" => BuiltInRadToDeg(args),
            // LLM-oriented math helpers
            "rsqrt" => BuiltInRsqrt(args),
            "randn" => BuiltInRandn(args),
            "argmax" => BuiltInArgmax(args),
            "argmin" => BuiltInArgmin(args),
            "logSumExp" => BuiltInLogSumExp(args),
            "softmax" => BuiltInSoftmax(args),
            "crossEntropyFromLogits" => BuiltInCrossEntropyFromLogits(args),
            "randomChoiceWeighted" => BuiltInRandomChoiceWeighted(args),
            "seed" => BuiltInSeed(args),
            "length" => BuiltInLength(args),
            "upper" => BuiltInUpper(args),
            "lower" => BuiltInLower(args),
            "trim" => BuiltInTrim(args),
            "substring" => BuiltInSubstring(args),
            "indexOf" => BuiltInIndexOf(args),
            "replace" => BuiltInReplace(args),
            "split" => BuiltInSplit(args),
            "normalizeText" => BuiltInNormalizeText(args),
            "tokenize" => BuiltInTokenize(args),
            "tokenOverlap" => BuiltInTokenOverlap(args),
            "similarity" => BuiltInSimilarity(args),
            "extractNumbers" => BuiltInExtractNumbers(args),
            "regexMatch" => BuiltInRegexMatch(args),
            "regexReplace" => BuiltInRegexReplace(args),
            "regexFind" => BuiltInRegexFind(args),
            "getFileName" => BuiltInGetFileName(args),
            "getDirectoryName" => BuiltInGetDirectoryName(args),
            "getMaldaHome" => BuiltInGetMaldaHome(args),
            "getProgramDirectory" => BuiltInGetProgramDirectory(args, interpreter),
            "getMaldaConfig" => BuiltInGetMaldaConfig(args),
            "getAssistantMemory" => BuiltInGetAssistantMemory(args, interpreter),
            "enableAgentVerboseLogging" => BuiltInEnableAgentVerboseLogging(args),
            "setAgentVerbosePhase" => BuiltInSetAgentVerbosePhase(args),
            "setAgentStatusBanner" => BuiltInSetAgentStatusBanner(args),
            "reportRalphStatus" => BuiltInReportRalphStatus(args),
            "getSkillNames" => BuiltInGetSkillNames(args),
            "loadSkill" => BuiltInLoadSkill(args, interpreter),
            "loadSkillsFromDir" => BuiltInLoadSkillsFromDir(args, interpreter),
            "print" => BuiltInPrint(args, interpreter),
            "input" => throw new Exception("input() must be called via CallBuiltInAsync"),
            "sleep" => throw new Exception("sleep() must be called via CallBuiltInAsync"),
            "domQuery" => BuiltInDomUnavailable("domQuery"),
            "domCreate" => BuiltInDomUnavailable("domCreate"),
            "domAppend" => BuiltInDomUnavailable("domAppend"),
            "domClear" => BuiltInDomUnavailable("domClear"),
            "domSetText" => BuiltInDomUnavailable("domSetText"),
            "domHtml" => BuiltInDomUnavailable("domHtml"),
            "domOn" => BuiltInDomUnavailable("domOn"),
            "getEnv" => BuiltInGetEnv(args),
            "getHostPlatform" => BuiltInGetHostPlatform(args),
            "getCommandLineArgs" => BuiltInGetCommandLineArgs(args),
            "hasEnv" => BuiltInHasEnv(args),
            "parseJSON" => BuiltInParseJSON(args),
            "parseJson" => BuiltInParseJsonTyped(args, interpreter),
            "runPrompt" => throw new Exception("runPrompt() must be called via CallBuiltInAsync"),
            "loadDocuments" => BuiltInLoadDocuments(args),
            "splitDocuments" => BuiltInSplitDocuments(args),
            "formatRetrievedDocs" => BuiltInFormatRetrievedDocs(args),
            "composePipe" => BuiltInComposePipe(args, interpreter),
            "parallelRun" => throw new Exception("parallelRun() must be called via CallBuiltInAsync"),
            "mergeRetrievedDocs" => BuiltInMergeRetrievedDocs(args),
            "withExamples" => BuiltInWithExamples(args),
            "indexInto" => BuiltInIndexInto(args, interpreter),
            "toJSON" => BuiltInToJSON(args),
            "loadNativeModule" => BuiltInLoadNativeModule(args),
            "createNativeCallback" => BuiltInCreateNativeCallback(args, interpreter),
            "readFile" => BuiltInReadFile(args),
            "readTextFileLines" => BuiltInReadTextFileLines(args),
            "writeFile" => BuiltInWriteFile(args),
            "writeFileBase64" => BuiltInWriteFileBase64(args),
            "readFileBase64" => BuiltInReadFileBase64(args),
            "hasFile" => BuiltInHasFile(args),
            "deleteFile" => BuiltInDeleteFile(args),
            "hasDirectory" => BuiltInHasDirectory(args),
            "ensureDir" => BuiltInEnsureDir(args),
            "listDirectory" => BuiltInListDirectory(args),
            "hasEmbeddedFolder" => BuiltInHasEmbeddedFolder(args),
            "embeddedFolderRoot" => BuiltInEmbeddedFolderRoot(args),
            "replaceInFile" => BuiltInReplaceInFile(args),
            "editFile" => BuiltInEditFile(args),
            "createReadFileTool" => BuiltInCreateReadFileTool(args),
            "createWriteFileTool" => BuiltInCreateWriteFileTool(args),
            "createReplaceInFileTool" => BuiltInCreateReplaceInFileTool(args),
            "createListDirectoryTool" => BuiltInCreateListDirectoryTool(args),
            "createAskUserTool" => BuiltInCreateAskUserTool(args),
            "createWebSearchTool" => BuiltInCreateWebSearchTool(args),
            "createGrepTool" => BuiltInCreateGrepTool(args),
            "grep" => BuiltInGrep(args),
            "createGlobTool" => BuiltInCreateGlobTool(args),
            "glob" => BuiltInGlob(args),
            "createInsertAtLineTool" => BuiltInCreateInsertAtLineTool(args),
            "insertAtLine" => BuiltInInsertAtLine(args),
            "createEditFileTool" => BuiltInCreateEditFileTool(args),
            "getSymbols" => BuiltInGetSymbols(args),
            "createGetSymbolsTool" => BuiltInCreateGetSymbolsTool(args),
            "getParseErrors" => BuiltInGetParseErrors(args),
            "createGetParseErrorsTool" => BuiltInCreateGetParseErrorsTool(args),
            "gitStatus" => BuiltInGitStatus(args),
            "gitAdd" => BuiltInGitAdd(args),
            "gitCommit" => BuiltInGitCommit(args),
            "gitLog" => BuiltInGitLog(args),
            "gitDiff" => BuiltInGitDiff(args),
            "gitBranch" => BuiltInGitBranch(args),
            "gitCheckout" => BuiltInGitCheckout(args),
            "gitPush" => BuiltInGitPush(args),
            "gitPull" => BuiltInGitPull(args),
            "createGitStatusTool" => BuiltInCreateGitStatusTool(args),
            "createGitAddTool" => BuiltInCreateGitAddTool(args),
            "createGitCommitTool" => BuiltInCreateGitCommitTool(args),
            "createGitLogTool" => BuiltInCreateGitLogTool(args),
            "createGitDiffTool" => BuiltInCreateGitDiffTool(args),
            "createGitBranchTool" => BuiltInCreateGitBranchTool(args),
            "createGitCheckoutTool" => BuiltInCreateGitCheckoutTool(args),
            "createGitPushTool" => BuiltInCreateGitPushTool(args),
            "createGitPullTool" => BuiltInCreateGitPullTool(args),
            "runCommand" => BuiltInRunCommand(args),
            "createRunCommandTool" => BuiltInCreateRunCommandTool(args),
            "runMALDA" => BuiltInRunMALDA(args),
            "createRunMALDATool" => BuiltInCreateRunMALDATool(args),
            "compileMALDA" => BuiltInCompileMALDA(args),
            "createCompileMALDATool" => BuiltInCreateCompileMALDATool(args),
            "createMcpAgentScript" => BuiltInCreateMcpAgentScript(args),
            "createCreateMcpAgentScriptTool" => BuiltInCreateCreateMcpAgentScriptTool(args),
            "createSubmitPlanTool" => BuiltInCreateSubmitPlanTool(args),
            "executePlan" => BuiltInExecutePlan(args),
            "decomposeTask" => BuiltInDecomposeTask(args),
            "extractHTML" => BuiltInExtractHTML(args),
            "markdownToHtml" => BuiltInMarkdownToHtml(args),
            "renderTemplate" => BuiltInRenderTemplate(args),
            "componentFragment" => BuiltInComponentFragment(args),
            "componentLiveEmit" => BuiltInComponentLiveEmit(args),
            "componentStateGet" => BuiltInComponentStateGet(args),
            "componentStateSet" => BuiltInComponentStateSet(args),
            "componentStateObject" => BuiltInComponentStateObject(args),
            "componentStateClear" => BuiltInComponentStateClear(args),
            "componentStateConfigure" => BuiltInComponentStateConfigure(args),
            "onAgentProgress" => BuiltInOnAgentProgress(args, interpreter),
            "clearAgentProgress" => BuiltInClearAgentProgress(args),
            "uiRow" => BuiltInUiRow(args),
            "uiColumn" => BuiltInUiColumn(args),
            "uiStack" => BuiltInUiStack(args),
            "uiSpacer" => BuiltInUiSpacer(args),
            "uiPanel" => BuiltInUiPanel(args),
            "uiText" => BuiltInUiText(args),
            "uiHeading" => BuiltInUiHeading(args),
            "uiImage" => BuiltInUiImage(args),
            "uiIcon" => BuiltInUiIcon(args),
            "uiButton" => BuiltInUiButton(args),
            "uiTextField" => BuiltInUiTextField(args),
            "uiCheckbox" => BuiltInUiCheckbox(args),
            "uiSelect" => BuiltInUiSelect(args),
            "uiSlider" => BuiltInUiSlider(args),
            "uiDatePicker" => BuiltInUiDatePicker(args),
            "uiList" => BuiltInUiList(args),
            "uiTable" => BuiltInUiTable(args),
            "uiAlert" => BuiltInUiAlert(args),
            "uiProgress" => BuiltInUiProgress(args),
            "uiModal" => BuiltInUiModal(args),
            "uiForm" => BuiltInUiForm(args),
            "uiField" => BuiltInUiField(args),
            "uiTextArea" => BuiltInUiTextArea(args),
            "uiRadioGroup" => BuiltInUiRadioGroup(args),
            "uiSwitch" => BuiltInUiSwitch(args),
            "uiTabs" => BuiltInUiTabs(args),
            "uiAccordion" => BuiltInUiAccordion(args),
            "uiBreadcrumbs" => BuiltInUiBreadcrumbs(args),
            "uiDrawer" => BuiltInUiDrawer(args),
            "uiDataGrid" => BuiltInUiDataGrid(args),
            "uiTreeView" => BuiltInUiTreeView(args),
            "uiPaginator" => BuiltInUiPaginator(args),
            "uiEmptyState" => BuiltInUiEmptyState(args),
            "uiBadge" => BuiltInUiBadge(args),
            "uiToast" => BuiltInUiToast(args),
            "uiSkeleton" => BuiltInUiSkeleton(args),
            "uiSpinner" => BuiltInUiSpinner(args),
            "uiErrorBoundary" => BuiltInUiErrorBoundary(args),
            "uiSlot" => BuiltInUiSlot(args),
            "uiWithSlot" => BuiltInUiWithSlot(args),
            "uiWhen" => BuiltInUiWhen(args),
            "uiChoose" => BuiltInUiChoose(args),
            "uiEach" => BuiltInUiEach(args),
            "uiTemplate" => BuiltInUiTemplate(args),
            "uiPartial" => BuiltInUiPartial(args),
            "uiLayout" => BuiltInUiLayout(args),
            "uiRenderList" => BuiltInUiRenderList(args),
            "uiCrudModel" => BuiltInUiCrudModel(args),
            "uiCrudControls" => BuiltInUiCrudControls(args),
            "uiCrudSchema" => BuiltInUiCrudSchema(args),
            "uiMount" => BuiltInUiMount(args),
            "uiMountEnvelope" => BuiltInUiMountEnvelope(args),
            "uiRender" => BuiltInUiRender(args),
            "uiDispatchEvent" => BuiltInUiDispatchEvent(args),
            "uiPullEvent" => BuiltInUiPullEvent(args),
            "uiState" => BuiltInUiState(args),
            "uiSetState" => BuiltInUiSetState(args),
            "uiInvalidate" => BuiltInUiInvalidate(args),
            "uiOnInit" => BuiltInUiOnInit(args),
            "uiOnPreRender" => BuiltInUiOnPreRender(args),
            "uiOnLoad" => BuiltInUiOnLoad(args),
            "uiOnDispose" => BuiltInUiOnDispose(args),
            "uiOnMount" => BuiltInUiOnMount(args),
            "uiOnUpdate" => BuiltInUiOnUpdate(args),
            "uiOnUnmount" => BuiltInUiOnUnmount(args),
            "uiOnError" => BuiltInUiOnError(args),
            "uiConfigure" => BuiltInUiConfigure(args),
            "uiSnapshot" => BuiltInUiSnapshot(args),
            "uiResync" => BuiltInUiResync(args),
            "uiSessionId" => BuiltInUiSessionId(args),
            "uiRedirectWithSession" => BuiltInUiRedirectWithSession(args),
            "uiGenerate" => BuiltInUiGenerate(args, interpreter),
            "redirect" => BuiltInRedirect(args),
            "RedirectTo" => BuiltInRedirectTo(args),
            "loadAssembly" => BuiltInLoadAssembly(args),
            "getDotNetType" => BuiltInGetDotNetType(args),
            "dotnetNew" => BuiltInDotNetNew(args),
            "httpGet" => BuiltInHttpGet(args),
            "httpPost" => BuiltInHttpPost(args),
            "httpPut" => BuiltInHttpPut(args),
            "httpDelete" => BuiltInHttpDelete(args),
            "httpPatch" => BuiltInHttpPatch(args),
            "httpBearerToken" => BuiltInHttpBearerToken(args),
            "httpCookieToken" => BuiltInHttpCookieToken(args),
            "httpAuthToken" => BuiltInHttpAuthToken(args),
            "webSearch" => BuiltInWebSearch(args),
            "reply" => BuiltInReply(args, interpreter),
            "embedBagOfWords" => BuiltInEmbedBagOfWords(args),
            "embedCharacterNGrams" => BuiltInEmbedCharacterNGrams(args),
            "embedHash" => BuiltInEmbedHash(args),
            "embedTFIDF" => BuiltInEmbedTFIDF(args),
            "embedFromFile" => BuiltInEmbedFromFile(args),
            "embedFromFiles" => BuiltInEmbedFromFiles(args),
            // Date/Time functions
            "now" => BuiltInNow(args),
            "formatDate" => BuiltInFormatDate(args),
            "parseDate" => BuiltInParseDate(args),
            "addDays" => BuiltInAddDays(args),
            "addHours" => BuiltInAddHours(args),
            // Random functions
            "random" => BuiltInRandom(args),
            "randomInt" => BuiltInRandomInt(args),
            "randomFloat" => BuiltInRandomFloat(args),
            // Type checking functions
            "isNumber" => BuiltInIsNumber(args),
            "isString" => BuiltInIsString(args),
            "isArray" => BuiltInIsArray(args),
            "isObject" => BuiltInIsObject(args),
            "typeOf" => BuiltInTypeOf(args),
            "isTag" => BuiltInIsTag(args),
            "validate" => BuiltInValidate(args),
            // Array utilities
            "join" => BuiltInJoin(args),
            "toCsv" => BuiltInToCsv(args),
            "reverse" => BuiltInReverse(args),
            "sort" => BuiltInSort(args, interpreter),
            "includes" => BuiltInIncludes(args),
            // Encoding/Decoding
            "base64Encode" => BuiltInBase64Encode(args),
            "base64Decode" => BuiltInBase64Decode(args),
            "urlEncode" => BuiltInUrlEncode(args),
            "urlDecode" => BuiltInUrlDecode(args),
            // Hash functions
            "md5" => BuiltInMd5(args),
            "sha256" => BuiltInSha256(args),
            "hashPassword" => BuiltInHashPassword(args),
            "verifyPassword" => BuiltInVerifyPassword(args),
            "createJwt" => BuiltInCreateJwt(args),
            "verifyJwt" => BuiltInVerifyJwt(args),
            "generateCsrfToken" => BuiltInGenerateCsrfToken(args),
            "verifyCsrfToken" => BuiltInVerifyCsrfToken(args),
            "createSecureCookie" => BuiltInCreateSecureCookie(args),
            "readSecureCookie" => BuiltInReadSecureCookie(args),
            // Path manipulation
            "pathJoin" => BuiltInPathJoin(args),
            "pathNormalize" => BuiltInPathNormalize(args),
            "pathExists" => BuiltInPathExists(args),
            "pathGetExtension" => BuiltInPathGetExtension(args),
            // Range generation
            "range" => BuiltInRange(args),
            // Error handling
            "exit" => BuiltInExit(args),
            "error" => BuiltInError(args),
            "assert" => BuiltInAssert(args),
            // Additional string utilities
            "startsWith" => BuiltInStartsWith(args),
            "endsWith" => BuiltInEndsWith(args),
            "padStart" => BuiltInPadStart(args),
            "padEnd" => BuiltInPadEnd(args),
            "repeat" => BuiltInRepeat(args),
            "all" => BuiltInAll(args),
            "getWorkflowStatus" => BuiltInGetWorkflowStatus(args),
            "getWorkflow" => BuiltInGetWorkflow(args),
            "getWorkflowSteps" => BuiltInGetWorkflowSteps(args),
            "getWorkflowEvents" => BuiltInGetWorkflowEvents(args),
            "getWorkflowMetrics" => BuiltInGetWorkflowMetrics(args),
            "listWorkflows" => BuiltInListWorkflows(args),
            "listWorkflowDeadLetters" => BuiltInListWorkflowDeadLetters(args),
            "requeueDeadLetter" => BuiltInRequeueDeadLetter(args),
            "cancelWorkflow" => BuiltInCancelWorkflow(args),
            "resumeWorkflow" => BuiltInResumeWorkflow(args),
            "retryWorkflow" => BuiltInRetryWorkflow(args),
            "approveWorkflowStep" => BuiltInApproveWorkflowStep(args),
            "signalWorkflow" => BuiltInSignalWorkflow(args),
            "runProperty" => BuiltInRunProperty(args, interpreter),
            "setDefaultAgent" => BuiltInSetDefaultAgent(args, interpreter),
            "runWorkflowInstance" => throw new Exception("runWorkflowInstance() must be called via CallBuiltInAsync"),
                _ => throw new Exception($"Unknown built-in function: {name}")
            };
        }
        finally
        {
            MaldaProfiler.Exit(profileToken);
        }
    }

    public static async Task<RuntimeValue> CallBuiltInAsync(string name, List<RuntimeValue> args, Interpreter? interpreter)
    {
        CheckWorkflowDeterminism(name, interpreter);
        var profileToken = MaldaProfiler.EnterBuiltIn(name);
        try
        {
            return name switch
            {
            "int" => BuiltInInt(args),
            "toIntOr" => BuiltInToIntOr(args),
            "toIntOrNull" => BuiltInToIntOrNull(args),
            "float" => BuiltInFloat(args),
            "string" => BuiltInString(args),
            "formatNumber" => BuiltInFormatNumber(args),
            "abs" => BuiltInAbs(args),
            "sum" => BuiltInSum(args),
            "average" => BuiltInAverage(args),
            "max" => BuiltInMax(args),
            "min" => BuiltInMin(args),
            "pow" => BuiltInPow(args),
            "sqrt" => BuiltInSqrt(args),
            // Extended math: rounding and sign
            "floor" => BuiltInFloor(args),
            "ceil" => BuiltInCeil(args),
            "round" => BuiltInRound(args),
            "trunc" => BuiltInTrunc(args),
            "sign" => BuiltInSign(args),
            // Extended math: exponential and logarithm
            "exp" => BuiltInExp(args),
            "log" => BuiltInLog(args),
            "log10" => BuiltInLog10(args),
            "log2" => BuiltInLog2(args),
            // Extended math: trigonometry
            "sin" => BuiltInSin(args),
            "cos" => BuiltInCos(args),
            "tan" => BuiltInTan(args),
            "asin" => BuiltInAsin(args),
            "acos" => BuiltInAcos(args),
            "atan" => BuiltInAtan(args),
            "atan2" => BuiltInAtan2(args),
            // Extended math: utility
            "hypot" => BuiltInHypot(args),
            "clamp" => BuiltInClamp(args),
            "degToRad" => BuiltInDegToRad(args),
            "radToDeg" => BuiltInRadToDeg(args),
            // LLM-oriented math helpers
            "rsqrt" => BuiltInRsqrt(args),
            "randn" => BuiltInRandn(args),
            "argmax" => BuiltInArgmax(args),
            "argmin" => BuiltInArgmin(args),
            "logSumExp" => BuiltInLogSumExp(args),
            "softmax" => BuiltInSoftmax(args),
            "crossEntropyFromLogits" => BuiltInCrossEntropyFromLogits(args),
            "randomChoiceWeighted" => BuiltInRandomChoiceWeighted(args),
            "seed" => BuiltInSeed(args),
            "length" => BuiltInLength(args),
            "upper" => BuiltInUpper(args),
            "lower" => BuiltInLower(args),
            "trim" => BuiltInTrim(args),
            "substring" => BuiltInSubstring(args),
            "indexOf" => BuiltInIndexOf(args),
            "replace" => BuiltInReplace(args),
            "split" => BuiltInSplit(args),
            "normalizeText" => BuiltInNormalizeText(args),
            "tokenize" => BuiltInTokenize(args),
            "tokenOverlap" => BuiltInTokenOverlap(args),
            "similarity" => BuiltInSimilarity(args),
            "extractNumbers" => BuiltInExtractNumbers(args),
            "regexMatch" => BuiltInRegexMatch(args),
            "regexReplace" => BuiltInRegexReplace(args),
            "regexFind" => BuiltInRegexFind(args),
            "getFileName" => BuiltInGetFileName(args),
            "getDirectoryName" => BuiltInGetDirectoryName(args),
            "getMaldaHome" => BuiltInGetMaldaHome(args),
            "getProgramDirectory" => BuiltInGetProgramDirectory(args, interpreter),
            "getMaldaConfig" => BuiltInGetMaldaConfig(args),
            "getAssistantMemory" => BuiltInGetAssistantMemory(args, interpreter),
            "enableAgentVerboseLogging" => BuiltInEnableAgentVerboseLogging(args),
            "setAgentVerbosePhase" => BuiltInSetAgentVerbosePhase(args),
            "setAgentStatusBanner" => BuiltInSetAgentStatusBanner(args),
            "reportRalphStatus" => BuiltInReportRalphStatus(args),
            "getSkillNames" => BuiltInGetSkillNames(args),
            "loadSkill" => BuiltInLoadSkill(args, interpreter),
            "loadSkillsFromDir" => BuiltInLoadSkillsFromDir(args, interpreter),
            "print" => BuiltInPrint(args, interpreter),
            "input" => await BuiltInInputAsync(args, interpreter),
            "sleep" => await BuiltInSleepAsync(args, interpreter),
            "domQuery" => BuiltInDomUnavailable("domQuery"),
            "domCreate" => BuiltInDomUnavailable("domCreate"),
            "domAppend" => BuiltInDomUnavailable("domAppend"),
            "domClear" => BuiltInDomUnavailable("domClear"),
            "domSetText" => BuiltInDomUnavailable("domSetText"),
            "domHtml" => BuiltInDomUnavailable("domHtml"),
            "domOn" => BuiltInDomUnavailable("domOn"),
            "getEnv" => BuiltInGetEnv(args),
            "getHostPlatform" => BuiltInGetHostPlatform(args),
            "getCommandLineArgs" => BuiltInGetCommandLineArgs(args),
            "hasEnv" => BuiltInHasEnv(args),
            "parseJSON" => BuiltInParseJSON(args),
            "parseJson" => BuiltInParseJsonTyped(args, interpreter),
            "runPrompt" => await AiPipelineHelpers.RunPromptAsync(args, interpreter),
            "loadDocuments" => BuiltInLoadDocuments(args),
            "splitDocuments" => BuiltInSplitDocuments(args),
            "formatRetrievedDocs" => BuiltInFormatRetrievedDocs(args),
            "composePipe" => BuiltInComposePipe(args, interpreter),
            "parallelRun" => await AiPipelineHelpers.ParallelRunAsync(args, interpreter),
            "mergeRetrievedDocs" => BuiltInMergeRetrievedDocs(args),
            "withExamples" => BuiltInWithExamples(args),
            "indexInto" => BuiltInIndexInto(args, interpreter),
            "toJSON" => BuiltInToJSON(args),
            "loadNativeModule" => BuiltInLoadNativeModule(args),
            "createNativeCallback" => BuiltInCreateNativeCallback(args, interpreter),
            "readFile" => BuiltInReadFile(args),
            "readTextFileLines" => BuiltInReadTextFileLines(args),
            "writeFile" => BuiltInWriteFile(args),
            "writeFileBase64" => BuiltInWriteFileBase64(args),
            "readFileBase64" => BuiltInReadFileBase64(args),
            "hasFile" => BuiltInHasFile(args),
            "deleteFile" => BuiltInDeleteFile(args),
            "hasDirectory" => BuiltInHasDirectory(args),
            "ensureDir" => BuiltInEnsureDir(args),
            "listDirectory" => BuiltInListDirectory(args),
            "hasEmbeddedFolder" => BuiltInHasEmbeddedFolder(args),
            "embeddedFolderRoot" => BuiltInEmbeddedFolderRoot(args),
            "replaceInFile" => BuiltInReplaceInFile(args),
            "editFile" => BuiltInEditFile(args),
            "createReadFileTool" => BuiltInCreateReadFileTool(args),
            "createWriteFileTool" => BuiltInCreateWriteFileTool(args),
            "createReplaceInFileTool" => BuiltInCreateReplaceInFileTool(args),
            "createListDirectoryTool" => BuiltInCreateListDirectoryTool(args),
            "createAskUserTool" => BuiltInCreateAskUserTool(args),
            "createWebSearchTool" => BuiltInCreateWebSearchTool(args),
            "createGrepTool" => BuiltInCreateGrepTool(args),
            "grep" => BuiltInGrep(args),
            "createGlobTool" => BuiltInCreateGlobTool(args),
            "glob" => BuiltInGlob(args),
            "createInsertAtLineTool" => BuiltInCreateInsertAtLineTool(args),
            "insertAtLine" => BuiltInInsertAtLine(args),
            "createEditFileTool" => BuiltInCreateEditFileTool(args),
            "getSymbols" => BuiltInGetSymbols(args),
            "createGetSymbolsTool" => BuiltInCreateGetSymbolsTool(args),
            "getParseErrors" => BuiltInGetParseErrors(args),
            "createGetParseErrorsTool" => BuiltInCreateGetParseErrorsTool(args),
            "gitStatus" => BuiltInGitStatus(args),
            "gitAdd" => BuiltInGitAdd(args),
            "gitCommit" => BuiltInGitCommit(args),
            "gitLog" => BuiltInGitLog(args),
            "gitDiff" => BuiltInGitDiff(args),
            "gitBranch" => BuiltInGitBranch(args),
            "gitCheckout" => BuiltInGitCheckout(args),
            "gitPush" => BuiltInGitPush(args),
            "gitPull" => BuiltInGitPull(args),
            "createGitStatusTool" => BuiltInCreateGitStatusTool(args),
            "createGitAddTool" => BuiltInCreateGitAddTool(args),
            "createGitCommitTool" => BuiltInCreateGitCommitTool(args),
            "createGitLogTool" => BuiltInCreateGitLogTool(args),
            "createGitDiffTool" => BuiltInCreateGitDiffTool(args),
            "createGitBranchTool" => BuiltInCreateGitBranchTool(args),
            "createGitCheckoutTool" => BuiltInCreateGitCheckoutTool(args),
            "createGitPushTool" => BuiltInCreateGitPushTool(args),
            "createGitPullTool" => BuiltInCreateGitPullTool(args),
            "runCommand" => BuiltInRunCommand(args),
            "createRunCommandTool" => BuiltInCreateRunCommandTool(args),
            "runMALDA" => BuiltInRunMALDA(args),
            "createRunMALDATool" => BuiltInCreateRunMALDATool(args),
            "compileMALDA" => BuiltInCompileMALDA(args),
            "createCompileMALDATool" => BuiltInCreateCompileMALDATool(args),
            "createMcpAgentScript" => BuiltInCreateMcpAgentScript(args),
            "createCreateMcpAgentScriptTool" => BuiltInCreateCreateMcpAgentScriptTool(args),
            "createSubmitPlanTool" => BuiltInCreateSubmitPlanTool(args),
            "executePlan" => BuiltInExecutePlan(args),
            "decomposeTask" => BuiltInDecomposeTask(args),
            "extractHTML" => BuiltInExtractHTML(args),
            "markdownToHtml" => BuiltInMarkdownToHtml(args),
            "renderTemplate" => BuiltInRenderTemplate(args),
            "componentFragment" => BuiltInComponentFragment(args),
            "componentLiveEmit" => BuiltInComponentLiveEmit(args),
            "componentStateGet" => BuiltInComponentStateGet(args),
            "componentStateSet" => BuiltInComponentStateSet(args),
            "componentStateObject" => BuiltInComponentStateObject(args),
            "componentStateClear" => BuiltInComponentStateClear(args),
            "componentStateConfigure" => BuiltInComponentStateConfigure(args),
            "onAgentProgress" => BuiltInOnAgentProgress(args, interpreter),
            "clearAgentProgress" => BuiltInClearAgentProgress(args),
            "uiRow" => BuiltInUiRow(args),
            "uiColumn" => BuiltInUiColumn(args),
            "uiStack" => BuiltInUiStack(args),
            "uiSpacer" => BuiltInUiSpacer(args),
            "uiPanel" => BuiltInUiPanel(args),
            "uiText" => BuiltInUiText(args),
            "uiHeading" => BuiltInUiHeading(args),
            "uiImage" => BuiltInUiImage(args),
            "uiIcon" => BuiltInUiIcon(args),
            "uiButton" => BuiltInUiButton(args),
            "uiTextField" => BuiltInUiTextField(args),
            "uiCheckbox" => BuiltInUiCheckbox(args),
            "uiSelect" => BuiltInUiSelect(args),
            "uiSlider" => BuiltInUiSlider(args),
            "uiDatePicker" => BuiltInUiDatePicker(args),
            "uiList" => BuiltInUiList(args),
            "uiTable" => BuiltInUiTable(args),
            "uiAlert" => BuiltInUiAlert(args),
            "uiProgress" => BuiltInUiProgress(args),
            "uiModal" => BuiltInUiModal(args),
            "uiForm" => BuiltInUiForm(args),
            "uiField" => BuiltInUiField(args),
            "uiTextArea" => BuiltInUiTextArea(args),
            "uiRadioGroup" => BuiltInUiRadioGroup(args),
            "uiSwitch" => BuiltInUiSwitch(args),
            "uiTabs" => BuiltInUiTabs(args),
            "uiAccordion" => BuiltInUiAccordion(args),
            "uiBreadcrumbs" => BuiltInUiBreadcrumbs(args),
            "uiDrawer" => BuiltInUiDrawer(args),
            "uiDataGrid" => BuiltInUiDataGrid(args),
            "uiTreeView" => BuiltInUiTreeView(args),
            "uiPaginator" => BuiltInUiPaginator(args),
            "uiEmptyState" => BuiltInUiEmptyState(args),
            "uiBadge" => BuiltInUiBadge(args),
            "uiToast" => BuiltInUiToast(args),
            "uiSkeleton" => BuiltInUiSkeleton(args),
            "uiSpinner" => BuiltInUiSpinner(args),
            "uiErrorBoundary" => BuiltInUiErrorBoundary(args),
            "uiSlot" => BuiltInUiSlot(args),
            "uiWithSlot" => BuiltInUiWithSlot(args),
            "uiWhen" => BuiltInUiWhen(args),
            "uiChoose" => BuiltInUiChoose(args),
            "uiEach" => BuiltInUiEach(args),
            "uiTemplate" => BuiltInUiTemplate(args),
            "uiPartial" => BuiltInUiPartial(args),
            "uiLayout" => BuiltInUiLayout(args),
            "uiRenderList" => BuiltInUiRenderList(args),
            "uiCrudModel" => BuiltInUiCrudModel(args),
            "uiCrudControls" => BuiltInUiCrudControls(args),
            "uiCrudSchema" => BuiltInUiCrudSchema(args),
            "uiMount" => BuiltInUiMount(args),
            "uiMountEnvelope" => BuiltInUiMountEnvelope(args),
            "uiRender" => BuiltInUiRender(args),
            "uiDispatchEvent" => BuiltInUiDispatchEvent(args),
            "uiPullEvent" => BuiltInUiPullEvent(args),
            "uiState" => BuiltInUiState(args),
            "uiSetState" => BuiltInUiSetState(args),
            "uiInvalidate" => BuiltInUiInvalidate(args),
            "uiOnInit" => BuiltInUiOnInit(args),
            "uiOnPreRender" => BuiltInUiOnPreRender(args),
            "uiOnLoad" => BuiltInUiOnLoad(args),
            "uiOnDispose" => BuiltInUiOnDispose(args),
            "uiOnMount" => BuiltInUiOnMount(args),
            "uiOnUpdate" => BuiltInUiOnUpdate(args),
            "uiOnUnmount" => BuiltInUiOnUnmount(args),
            "uiOnError" => BuiltInUiOnError(args),
            "uiConfigure" => BuiltInUiConfigure(args),
            "uiSnapshot" => BuiltInUiSnapshot(args),
            "uiResync" => BuiltInUiResync(args),
            "uiSessionId" => BuiltInUiSessionId(args),
            "uiRedirectWithSession" => BuiltInUiRedirectWithSession(args),
            "uiGenerate" => await BuiltInUiGenerateAsync(args, interpreter),
            "redirect" => BuiltInRedirect(args),
            "RedirectTo" => BuiltInRedirectTo(args),
            "generateUI" => await BuiltInGenerateUIAsync(args, interpreter),
            "loadAssembly" => BuiltInLoadAssembly(args),
            "getDotNetType" => BuiltInGetDotNetType(args),
            "dotnetNew" => BuiltInDotNetNew(args),
            "httpGet" => BuiltInHttpGet(args),
            "httpPost" => BuiltInHttpPost(args),
            "httpPut" => BuiltInHttpPut(args),
            "httpDelete" => BuiltInHttpDelete(args),
            "httpPatch" => BuiltInHttpPatch(args),
            "httpBearerToken" => BuiltInHttpBearerToken(args),
            "httpCookieToken" => BuiltInHttpCookieToken(args),
            "httpAuthToken" => BuiltInHttpAuthToken(args),
            "webSearch" => BuiltInWebSearch(args),
            "reply" => BuiltInReply(args, interpreter),
            "embedBagOfWords" => BuiltInEmbedBagOfWords(args),
            "embedCharacterNGrams" => BuiltInEmbedCharacterNGrams(args),
            "embedHash" => BuiltInEmbedHash(args),
            "embedTFIDF" => BuiltInEmbedTFIDF(args),
            "embedFromFile" => BuiltInEmbedFromFile(args),
            "embedFromFiles" => BuiltInEmbedFromFiles(args),
            // Date/Time functions
            "now" => BuiltInNow(args),
            "formatDate" => BuiltInFormatDate(args),
            "parseDate" => BuiltInParseDate(args),
            "addDays" => BuiltInAddDays(args),
            "addHours" => BuiltInAddHours(args),
            // Random functions
            "random" => BuiltInRandom(args),
            "randomInt" => BuiltInRandomInt(args),
            "randomFloat" => BuiltInRandomFloat(args),
            // Type checking functions
            "isNumber" => BuiltInIsNumber(args),
            "isString" => BuiltInIsString(args),
            "isArray" => BuiltInIsArray(args),
            "isObject" => BuiltInIsObject(args),
            "typeOf" => BuiltInTypeOf(args),
            "isTag" => BuiltInIsTag(args),
            "validate" => BuiltInValidate(args),
            // Array utilities
            "join" => BuiltInJoin(args),
            "toCsv" => BuiltInToCsv(args),
            "reverse" => BuiltInReverse(args),
            "sort" => BuiltInSort(args, interpreter),
            "includes" => BuiltInIncludes(args),
            // Encoding/Decoding
            "base64Encode" => BuiltInBase64Encode(args),
            "base64Decode" => BuiltInBase64Decode(args),
            "urlEncode" => BuiltInUrlEncode(args),
            "urlDecode" => BuiltInUrlDecode(args),
            // Hash functions
            "md5" => BuiltInMd5(args),
            "sha256" => BuiltInSha256(args),
            "hashPassword" => BuiltInHashPassword(args),
            "verifyPassword" => BuiltInVerifyPassword(args),
            "createJwt" => BuiltInCreateJwt(args),
            "verifyJwt" => BuiltInVerifyJwt(args),
            "generateCsrfToken" => BuiltInGenerateCsrfToken(args),
            "verifyCsrfToken" => BuiltInVerifyCsrfToken(args),
            "createSecureCookie" => BuiltInCreateSecureCookie(args),
            "readSecureCookie" => BuiltInReadSecureCookie(args),
            // Path manipulation
            "pathJoin" => BuiltInPathJoin(args),
            "pathNormalize" => BuiltInPathNormalize(args),
            "pathExists" => BuiltInPathExists(args),
            "pathGetExtension" => BuiltInPathGetExtension(args),
            // Range generation
            "range" => BuiltInRange(args),
            // Error handling
            "exit" => BuiltInExit(args),
            "error" => BuiltInError(args),
            "assert" => BuiltInAssert(args),
            // Additional string utilities
            "startsWith" => BuiltInStartsWith(args),
            "endsWith" => BuiltInEndsWith(args),
            "padStart" => BuiltInPadStart(args),
            "padEnd" => BuiltInPadEnd(args),
            "repeat" => BuiltInRepeat(args),
            "all" => BuiltInAll(args),
            "getWorkflowStatus" => BuiltInGetWorkflowStatus(args),
            "getWorkflow" => BuiltInGetWorkflow(args),
            "getWorkflowSteps" => BuiltInGetWorkflowSteps(args),
            "getWorkflowEvents" => BuiltInGetWorkflowEvents(args),
            "getWorkflowMetrics" => BuiltInGetWorkflowMetrics(args),
            "listWorkflows" => BuiltInListWorkflows(args),
            "listWorkflowDeadLetters" => BuiltInListWorkflowDeadLetters(args),
            "requeueDeadLetter" => BuiltInRequeueDeadLetter(args),
            "cancelWorkflow" => BuiltInCancelWorkflow(args),
            "resumeWorkflow" => BuiltInResumeWorkflow(args),
            "retryWorkflow" => BuiltInRetryWorkflow(args),
            "approveWorkflowStep" => BuiltInApproveWorkflowStep(args),
            "signalWorkflow" => BuiltInSignalWorkflow(args),
            "runProperty" => BuiltInRunProperty(args, interpreter),
            "setDefaultAgent" => BuiltInSetDefaultAgent(args, interpreter),
            "startWorkflow" => await BuiltInStartWorkflowAsync(args, interpreter),
            "runWorkflowInstance" => await BuiltInRunWorkflowInstanceAsync(args, interpreter),
                _ => throw new Exception($"Unknown built-in function: {name}")
            };
        }
        finally
        {
            MaldaProfiler.Exit(profileToken);
        }
    }
    
    private static RuntimeValue BuiltInLoadAssembly(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("loadAssembly() expects 1 string argument (path or assembly name)");

        var id = args[0].AsString();
        Assembly assembly;

        try
        {
            // Treat rooted paths or .dll-suffixed values as file paths
            if (Path.IsPathRooted(id) || id.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = Path.GetFullPath(id);
                if (!File.Exists(fullPath))
                    throw new Exception($"Assembly file not found: {fullPath}");

                assembly = Assembly.LoadFrom(fullPath);
            }
            else
            {
                // Load by simple or full assembly name
                assembly = Assembly.Load(id);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"loadAssembly() failed: {ex.Message}");
        }

        return RuntimeValue.Object(new DotNetAssemblyInstance(assembly));
    }

    private static RuntimeValue BuiltInGetDotNetType(List<RuntimeValue> args)
    {
        if (args.Count == 1 && args[0].Type == ValueType.String)
        {
            var typeName = args[0].AsString();
            var type = Type.GetType(typeName, throwOnError: false, ignoreCase: false);

            if (type == null)
            {
                // Search loaded assemblies as a fallback
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                throw new Exception($"Type '{typeName}' not found in loaded assemblies.");

            return RuntimeValue.Object(new DotNetTypeInstance(type));
        }

        if (args.Count == 2 && args[0].Type == ValueType.Object && args[1].Type == ValueType.String)
        {
            var asmObj = args[0].AsObject();
            if (asmObj is not DotNetAssemblyInstance asm)
                throw new Exception("getDotNetType() first argument must be an Assembly returned by loadAssembly()");

            var typeName = args[1].AsString();
            var type = asm.Assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type == null)
                throw new Exception($"Type '{typeName}' not found in assembly '{asm.Assembly.FullName}'.");

            return RuntimeValue.Object(new DotNetTypeInstance(type));
        }

        throw new Exception("getDotNetType() expects either (string fullTypeName) or (assembly, string typeName)");
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

    private static RuntimeValue BuiltInDotNetNew(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("dotnetNew() expects at least 1 argument: (typeOrTypeName, ...ctorArgs)");

        Type? type = null;

        // First argument: either DotNetTypeInstance or string type name
        if (args[0].Type == ValueType.Object)
        {
            var obj = args[0].AsObject();
            if (obj is not DotNetTypeInstance typeHandle)
                throw new Exception("dotnetNew() first argument must be a type handle (from getDotNetType) or a string type name");
            type = typeHandle.Type;
        }
        else if (args[0].Type == ValueType.String)
        {
            var typeName = args[0].AsString();
            type = Type.GetType(typeName, throwOnError: false, ignoreCase: false);

            if (type == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                throw new Exception($"Type '{typeName}' not found in loaded assemblies.");
        }
        else
        {
            throw new Exception("dotnetNew() first argument must be a type handle (from getDotNetType) or a string type name");
        }

        var ctorArgsRuntime = args.Skip(1).ToList();
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (ctors.Length == 0 && ctorArgsRuntime.Count == 0)
        {
            // Value type or type with implicit default ctor
            var defaultInstance = Activator.CreateInstance(type);
            if (defaultInstance == null)
                throw new Exception($"Could not create instance of '{type.FullName}'.");
            return RuntimeValue.Object(new DotNetObjectInstance(defaultInstance));
        }

        ConstructorInfo? chosen = null;
        object?[]? converted = null;

        foreach (var ctor in ctors)
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length != ctorArgsRuntime.Count)
                continue;

            try
            {
                var temp = new object?[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    temp[i] = DotNetInteropHelpers.ConvertToClr(ctorArgsRuntime[i], parameters[i].ParameterType);
                }

                chosen = ctor;
                converted = temp;
                break;
            }
            catch
            {
                continue;
            }
        }

        if (chosen == null)
        {
            throw new Exception($"No suitable constructor found for type '{type.FullName}'.");
        }

        try
        {
            var instance = chosen.Invoke(converted);
            if (instance == null)
                throw new Exception($"Constructor for type '{type.FullName}' returned null.");

            return RuntimeValue.Object(new DotNetObjectInstance(instance));
        }
        catch (TargetInvocationException tie)
        {
            throw new Exception(tie.InnerException?.Message ?? tie.Message);
        }
    }
    
    private static RuntimeValue BuiltInInt(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("int() expects 1 argument");
        var arg = args[0];
        return arg.Type switch
        {
            MaldaLang.Interpreter.ValueType.Integer => arg,
            MaldaLang.Interpreter.ValueType.Float => RuntimeValue.Integer((int)arg.AsFloat()),
            MaldaLang.Interpreter.ValueType.String => RuntimeValue.Integer(int.Parse(arg.AsString())),
            _ => throw new Exception("Cannot convert to int")
        };
    }

    private static RuntimeValue BuiltInToIntOr(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("toIntOr() expects 2 arguments: (value, fallback)");
        var fallback = CoerceRuntimeValueToInt(args[1], "toIntOr() fallback");
        if (IsNullOrEmptyRuntimeValue(args[0]))
        {
            return RuntimeValue.Integer(fallback);
        }

        return TryCoerceRuntimeValueToInt(args[0], out var value)
            ? RuntimeValue.Integer(value)
            : RuntimeValue.Integer(fallback);
    }

    private static RuntimeValue BuiltInToIntOrNull(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("toIntOrNull() expects 1 argument");
        if (IsNullOrEmptyRuntimeValue(args[0]))
        {
            return RuntimeValue.Null();
        }

        return TryCoerceRuntimeValueToInt(args[0], out var value)
            ? RuntimeValue.Integer(value)
            : RuntimeValue.Null();
    }

    private static bool IsNullOrEmptyRuntimeValue(RuntimeValue value)
    {
        return value.Type == ValueType.Null ||
               (value.Type == ValueType.String && string.IsNullOrWhiteSpace(value.AsString()));
    }

    private static int CoerceRuntimeValueToInt(RuntimeValue value, string label)
    {
        if (TryCoerceRuntimeValueToInt(value, out var parsed))
        {
            return parsed;
        }

        throw new Exception($"{label} must be an integer-compatible value");
    }

    private static bool TryCoerceRuntimeValueToInt(RuntimeValue value, out int parsed)
    {
        switch (value.Type)
        {
            case ValueType.Integer:
                parsed = value.AsInteger();
                return true;
            case ValueType.Float:
                parsed = (int)value.AsFloat();
                return true;
            case ValueType.Boolean:
                parsed = value.AsBoolean() ? 1 : 0;
                return true;
            case ValueType.String:
            {
                var text = value.AsString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    parsed = 0;
                    return false;
                }

                if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var intValue))
                {
                    parsed = intValue;
                    return true;
                }

                if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var doubleValue))
                {
                    parsed = (int)doubleValue;
                    return true;
                }

                parsed = 0;
                return false;
            }
            default:
                parsed = 0;
                return false;
        }
    }
    
    private static RuntimeValue BuiltInFloat(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("float() expects 1 argument");
        var arg = args[0];
        return arg.Type switch
        {
            MaldaLang.Interpreter.ValueType.Float => arg,
            MaldaLang.Interpreter.ValueType.Integer => RuntimeValue.Float(arg.AsInteger()),
            MaldaLang.Interpreter.ValueType.String => RuntimeValue.Float(double.Parse(arg.AsString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)),
            _ => throw new Exception("Cannot convert to float")
        };
    }
    
    private static RuntimeValue BuiltInString(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("string() expects 1 argument");
        return RuntimeValue.String(args[0].ToString());
    }
    
    private static RuntimeValue BuiltInFormatNumber(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("formatNumber() expects 2 arguments (number, decimalPlaces)");
        
        var number = args[0];
        var decimalPlaces = args[1];
        
        if (!NumericCoercion.TryAsInteger(decimalPlaces, out var places))
            throw new Exception("formatNumber() second argument (decimalPlaces) must be an integer");

        if (places < 0)
            throw new Exception("formatNumber() decimalPlaces must be non-negative");
        
        double value;
        if (number.Type == ValueType.Integer)
            value = number.AsInteger();
        else if (number.Type == ValueType.Float)
            value = number.AsFloat();
        else
            throw new Exception("formatNumber() first argument must be a number");
        
        return RuntimeValue.String(value.ToString($"F{places}", System.Globalization.CultureInfo.InvariantCulture));
    }
    
    private static RuntimeValue BuiltInPrint(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count != 1) throw new Exception("print() expects 1 argument");
        var text = args[0].ToString();
        var callback = interpreter?.GetOutputCallback();
        if (callback != null)
            callback(text);
        else
            Console.WriteLine(text);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInDomUnavailable(string builtInName)
    {
        throw new Exception($"{builtInName}() is only available in browser-hosted JavaScript runtime. Use JS mode with mlRuntime.dom.*.");
    }
    
    private static RuntimeValue BuiltInAbs(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("abs() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.Integer)
            return RuntimeValue.Integer(Math.Abs(arg.AsInteger()));
        if (arg.Type == MaldaLang.Interpreter.ValueType.Float)
            return RuntimeValue.Float(Math.Abs(arg.AsFloat()));
        throw new Exception("abs() expects a number");
    }

    private static RuntimeValue BuiltInSum(List<RuntimeValue> args)
    {
        BuiltInArity.Require("sum", args, 1, 1, "array");
        var values = GetNumericArrayArgument("sum", args, requireNonEmpty: false);
        double sum = 0;
        var allIntegers = true;

        foreach (var value in values)
        {
            if (value.Type == ValueType.Integer)
            {
                sum += value.AsInteger();
            }
            else
            {
                allIntegers = false;
                sum += value.AsFloat();
            }
        }

        return CreateNumericRuntimeValue(sum, allIntegers);
    }

    private static RuntimeValue BuiltInAverage(List<RuntimeValue> args)
    {
        BuiltInArity.Require("average", args, 1, 1, "array");
        var values = GetNumericArrayArgument("average", args, requireNonEmpty: true);
        double sum = 0;

        foreach (var value in values)
        {
            sum += value.Type == ValueType.Integer ? value.AsInteger() : value.AsFloat();
        }

        return RuntimeValue.Float(sum / values.Count);
    }
    
    private static RuntimeValue BuiltInMax(List<RuntimeValue> args)
    {
        if (args.Count == 1)
        {
            var values = GetNumericArrayArgument("max", args, requireNonEmpty: true);
            var best = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
            var allIntegers = values[0].Type == ValueType.Integer;

            for (var i = 1; i < values.Count; i++)
            {
                var current = values[i];
                if (current.Type == ValueType.Float)
                    allIntegers = false;

                var currentValue = current.Type == ValueType.Integer ? current.AsInteger() : current.AsFloat();
                if (currentValue > best)
                    best = currentValue;
            }

            return CreateNumericRuntimeValue(best, allIntegers);
        }

        if (args.Count != 2) throw new Exception("max() expects 2 arguments or 1 array");
        var a = args[0];
        var b = args[1];
        if (!IsNumericValue(a) || !IsNumericValue(b))
            throw new Exception("max() expects numbers");
        if (a.Type == MaldaLang.Interpreter.ValueType.Integer && b.Type == MaldaLang.Interpreter.ValueType.Integer)
            return RuntimeValue.Integer(Math.Max(a.AsInteger(), b.AsInteger()));
        var aVal = a.Type == MaldaLang.Interpreter.ValueType.Integer ? a.AsInteger() : a.AsFloat();
        var bVal = b.Type == MaldaLang.Interpreter.ValueType.Integer ? b.AsInteger() : b.AsFloat();
        return RuntimeValue.Float(Math.Max(aVal, bVal));
    }
    
    private static RuntimeValue BuiltInMin(List<RuntimeValue> args)
    {
        if (args.Count == 1)
        {
            var values = GetNumericArrayArgument("min", args, requireNonEmpty: true);
            var best = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
            var allIntegers = values[0].Type == ValueType.Integer;

            for (var i = 1; i < values.Count; i++)
            {
                var current = values[i];
                if (current.Type == ValueType.Float)
                    allIntegers = false;

                var currentValue = current.Type == ValueType.Integer ? current.AsInteger() : current.AsFloat();
                if (currentValue < best)
                    best = currentValue;
            }

            return CreateNumericRuntimeValue(best, allIntegers);
        }

        if (args.Count != 2) throw new Exception("min() expects 2 arguments or 1 array");
        var a = args[0];
        var b = args[1];
        if (!IsNumericValue(a) || !IsNumericValue(b))
            throw new Exception("min() expects numbers");
        if (a.Type == MaldaLang.Interpreter.ValueType.Integer && b.Type == MaldaLang.Interpreter.ValueType.Integer)
            return RuntimeValue.Integer(Math.Min(a.AsInteger(), b.AsInteger()));
        var aVal = a.Type == MaldaLang.Interpreter.ValueType.Integer ? a.AsInteger() : a.AsFloat();
        var bVal = b.Type == MaldaLang.Interpreter.ValueType.Integer ? b.AsInteger() : b.AsFloat();
        return RuntimeValue.Float(Math.Min(aVal, bVal));
    }

    private static List<RuntimeValue> GetNumericArrayArgument(string functionName, List<RuntimeValue> args, bool requireNonEmpty)
    {
        if (args[0].Type != ValueType.Array) throw new Exception($"{functionName}() expects an array argument");

        var values = args[0].AsArray();
        if (requireNonEmpty && values.Count == 0)
            throw new Exception($"{functionName}() expects a non-empty array");

        foreach (var value in values)
        {
            if (!IsNumericValue(value))
                throw new Exception($"{functionName}() array elements must be numbers");
        }

        return values;
    }

    private static bool IsNumericValue(RuntimeValue value)
    {
        return value.Type == ValueType.Integer || value.Type == ValueType.Float;
    }

    private static RuntimeValue CreateNumericRuntimeValue(double value, bool preferInteger)
    {
        if (preferInteger)
            return RuntimeValue.Integer((int)value);

        return RuntimeValue.Float(value);
    }

    private static RuntimeValue BuiltInPow(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("pow() expects 2 arguments");
        var a = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        var b = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        return RuntimeValue.Float(Math.Pow(a, b));
    }
    
    private static RuntimeValue BuiltInSqrt(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("sqrt() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Sqrt(arg));
    }

    // Extended math: rounding and sign

    private static RuntimeValue BuiltInFloor(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("floor() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Floor(arg));
    }

    private static RuntimeValue BuiltInCeil(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("ceil() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Ceiling(arg));
    }

    private static RuntimeValue BuiltInRound(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("round() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Round(arg));
    }

    private static RuntimeValue BuiltInTrunc(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("trunc() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Truncate(arg));
    }

    private static RuntimeValue BuiltInSign(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("sign() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float((double)Math.Sign(arg));
    }

    // Extended math: exponential and logarithm

    private static RuntimeValue BuiltInExp(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("exp() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Exp(arg));
    }

    private static RuntimeValue BuiltInLog(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("log() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Log(arg));
    }

    private static RuntimeValue BuiltInLog10(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("log10() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Log10(arg));
    }

    private static RuntimeValue BuiltInLog2(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("log2() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Log(arg, 2.0));
    }

    // Extended math: trigonometry (radians)

    private static RuntimeValue BuiltInSin(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("sin() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Sin(arg));
    }

    private static RuntimeValue BuiltInCos(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("cos() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Cos(arg));
    }

    private static RuntimeValue BuiltInTan(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("tan() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Tan(arg));
    }

    private static RuntimeValue BuiltInAsin(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("asin() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Asin(arg));
    }

    private static RuntimeValue BuiltInAcos(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("acos() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Acos(arg));
    }

    private static RuntimeValue BuiltInAtan(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("atan() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Atan(arg));
    }

    private static RuntimeValue BuiltInAtan2(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("atan2() expects 2 arguments");
        var y = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        var x = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        return RuntimeValue.Float(Math.Atan2(y, x));
    }

    // Extended math: utility functions

    private static RuntimeValue BuiltInHypot(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("hypot() expects 2 arguments");
        var x = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        var y = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        return RuntimeValue.Float(Math.Sqrt(x * x + y * y));
    }

    private static RuntimeValue BuiltInClamp(List<RuntimeValue> args)
    {
        if (args.Count != 3) throw new Exception("clamp() expects 3 arguments");
        var x = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        var lo = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        var hi = args[2].Type == MaldaLang.Interpreter.ValueType.Integer ? args[2].AsInteger() : args[2].AsFloat();
        if (lo > hi)
        {
            var tmp = lo;
            lo = hi;
            hi = tmp;
        }
        var result = x < lo ? lo : (x > hi ? hi : x);
        return RuntimeValue.Float(result);
    }

    private static RuntimeValue BuiltInDegToRad(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("degToRad() expects 1 argument");
        var deg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(deg * Math.PI / 180.0);
    }

    private static RuntimeValue BuiltInRadToDeg(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("radToDeg() expects 1 argument");
        var rad = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(rad * 180.0 / Math.PI);
    }

    // LLM-oriented math helpers

    private static RuntimeValue BuiltInRsqrt(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("rsqrt() expects 1 argument");
        var x = args[0].Type == ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(1.0 / Math.Sqrt(x));
    }

    private static RuntimeValue BuiltInRandn(List<RuntimeValue> args)
    {
        if (args.Count > 2) throw new Exception("randn() expects 0-2 arguments: (std?, mean?)");
        var std = 1.0;
        var mean = 0.0;
        if (args.Count >= 1)
            std = args[0].Type == ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        if (args.Count == 2)
            mean = args[1].Type == ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();

        // Box-Muller transform
        var u1 = Math.Max(_random.NextDouble(), 1e-12);
        var u2 = _random.NextDouble();
        var z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return RuntimeValue.Float(mean + z0 * std);
    }

    private static RuntimeValue BuiltInArgmax(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("argmax() expects 1 argument: (array)");
        if (args[0].Type != ValueType.Array) throw new Exception("argmax() expects an array argument");
        var values = args[0].AsArray();
        if (values.Count == 0) throw new Exception("argmax() expects a non-empty array");

        var bestIdx = 0;
        var bestVal = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
        for (var i = 1; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            if (v > bestVal)
            {
                bestVal = v;
                bestIdx = i;
            }
        }
        return RuntimeValue.Integer(bestIdx);
    }

    private static RuntimeValue BuiltInArgmin(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("argmin() expects 1 argument: (array)");
        if (args[0].Type != ValueType.Array) throw new Exception("argmin() expects an array argument");
        var values = args[0].AsArray();
        if (values.Count == 0) throw new Exception("argmin() expects a non-empty array");

        var bestIdx = 0;
        var bestVal = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
        for (var i = 1; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            if (v < bestVal)
            {
                bestVal = v;
                bestIdx = i;
            }
        }
        return RuntimeValue.Integer(bestIdx);
    }

    private static RuntimeValue BuiltInLogSumExp(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("logSumExp() expects 1 argument: (array)");
        if (args[0].Type != ValueType.Array) throw new Exception("logSumExp() expects an array argument");
        var values = args[0].AsArray();
        if (values.Count == 0) throw new Exception("logSumExp() expects a non-empty array");

        var maxVal = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
        for (var i = 1; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            if (v > maxVal) maxVal = v;
        }

        var sumExp = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            sumExp += Math.Exp(v - maxVal);
        }

        return RuntimeValue.Float(maxVal + Math.Log(sumExp));
    }

    private static RuntimeValue BuiltInSoftmax(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("softmax() expects 1-2 arguments: (array, temperature?)");
        if (args[0].Type != ValueType.Array) throw new Exception("softmax() expects an array as first argument");
        var values = args[0].AsArray();
        if (values.Count == 0) throw new Exception("softmax() expects a non-empty array");

        var temperature = 1.0;
        if (args.Count == 2)
            temperature = args[1].Type == ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        if (temperature <= 0.0) throw new Exception("softmax() temperature must be > 0");

        var first = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
        var maxVal = first / temperature;
        for (var i = 1; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            var scaled = v / temperature;
            if (scaled > maxVal) maxVal = scaled;
        }

        var exps = new double[values.Count];
        var sumExp = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            var e = Math.Exp((v / temperature) - maxVal);
            exps[i] = e;
            sumExp += e;
        }

        var probs = new List<RuntimeValue>(values.Count);
        for (var i = 0; i < exps.Length; i++)
            probs.Add(RuntimeValue.Float(exps[i] / sumExp));
        return RuntimeValue.Array(probs);
    }

    private static RuntimeValue BuiltInCrossEntropyFromLogits(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("crossEntropyFromLogits() expects 2 arguments: (logits, targetIndex)");
        if (args[0].Type != ValueType.Array) throw new Exception("crossEntropyFromLogits() expects an array as first argument");
        if (!NumericCoercion.TryAsInteger(args[1], out var targetIndex))
            throw new Exception("crossEntropyFromLogits() expects integer targetIndex");

        var logits = args[0].AsArray();
        if (logits.Count == 0) throw new Exception("crossEntropyFromLogits() expects non-empty logits");
        if (targetIndex < 0 || targetIndex >= logits.Count) throw new Exception("crossEntropyFromLogits() targetIndex out of range");

        var maxVal = logits[0].Type == ValueType.Integer ? logits[0].AsInteger() : logits[0].AsFloat();
        for (var i = 1; i < logits.Count; i++)
        {
            var v = logits[i].Type == ValueType.Integer ? logits[i].AsInteger() : logits[i].AsFloat();
            if (v > maxVal) maxVal = v;
        }

        var sumExp = 0.0;
        for (var i = 0; i < logits.Count; i++)
        {
            var v = logits[i].Type == ValueType.Integer ? logits[i].AsInteger() : logits[i].AsFloat();
            sumExp += Math.Exp(v - maxVal);
        }
        var logSumExp = maxVal + Math.Log(sumExp);
        var target = logits[targetIndex].Type == ValueType.Integer ? logits[targetIndex].AsInteger() : logits[targetIndex].AsFloat();
        return RuntimeValue.Float(logSumExp - target);
    }

    private static RuntimeValue BuiltInRandomChoiceWeighted(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("randomChoiceWeighted() expects 1 argument: (weights)");
        if (args[0].Type != ValueType.Array) throw new Exception("randomChoiceWeighted() expects an array argument");
        var weights = args[0].AsArray();
        if (weights.Count == 0) throw new Exception("randomChoiceWeighted() expects a non-empty array");

        var total = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i].Type == ValueType.Integer ? weights[i].AsInteger() : weights[i].AsFloat();
            if (w < 0) throw new Exception("randomChoiceWeighted() weights must be >= 0");
            total += w;
        }
        if (total <= 0.0) throw new Exception("randomChoiceWeighted() sum of weights must be > 0");

        var r = _random.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i].Type == ValueType.Integer ? weights[i].AsInteger() : weights[i].AsFloat();
            cumulative += w;
            if (r <= cumulative)
                return RuntimeValue.Integer(i);
        }
        return RuntimeValue.Integer(weights.Count - 1);
    }

    private static RuntimeValue BuiltInSeed(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("seed() expects 1 argument");
        if (!NumericCoercion.TryAsInteger(args[0], out var seed))
            throw new Exception("seed() expects an integer argument");
        _random = new Random(seed);
        return RuntimeValue.Null();
    }
    
    private static RuntimeValue BuiltInLength(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("length() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.String)
            return RuntimeValue.Integer(arg.AsString().Length);
        if (arg.Type == MaldaLang.Interpreter.ValueType.Array)
            return RuntimeValue.Integer(arg.AsArray().Count);
        throw new Exception("length() expects a string or array");
    }
    
    private static RuntimeValue BuiltInUpper(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("upper() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.String)
            return RuntimeValue.String(arg.AsString().ToUpper());
        throw new Exception("upper() expects a string");
    }
    
    private static RuntimeValue BuiltInLower(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("lower() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.String)
            return RuntimeValue.String(arg.AsString().ToLower());
        throw new Exception("lower() expects a string");
    }
    
    private static RuntimeValue BuiltInTrim(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("trim() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.String)
            return RuntimeValue.String(arg.AsString().Trim());
        throw new Exception("trim() expects a string");
    }
    
    private static RuntimeValue BuiltInSubstring(List<RuntimeValue> args)
    {
        if (args.Count != 3) throw new Exception("substring() expects 3 arguments");
        var str = args[0];
        if (str.Type != MaldaLang.Interpreter.ValueType.String
            || !NumericCoercion.TryAsInteger(args[1], out var startIdx)
            || !NumericCoercion.TryAsInteger(args[2], out var len))
            throw new Exception("substring() expects (string, int, int)");
        var s = str.AsString();
        if (startIdx < 0 || startIdx >= s.Length || len < 0)
            throw new Exception("Invalid substring indices");
        var endIdx = Math.Min(startIdx + len, s.Length);
        return RuntimeValue.String(s.Substring(startIdx, endIdx - startIdx));
    }
    
    private static RuntimeValue BuiltInIndexOf(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("indexOf() expects 2 arguments");
        var str = args[0];
        var searchStr = args[1];
        if (str.Type != MaldaLang.Interpreter.ValueType.String || searchStr.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("indexOf() expects (string, string)");
        var s = str.AsString();
        var search = searchStr.AsString();
        var index = s.IndexOf(search, StringComparison.Ordinal);
        return RuntimeValue.Integer(index);
    }
    
    private static RuntimeValue BuiltInReplace(List<RuntimeValue> args)
    {
        if (args.Count != 3) throw new Exception("replace() expects 3 arguments");
        var text = args[0];
        var oldText = args[1];
        var newText = args[2];
        if (text.Type != MaldaLang.Interpreter.ValueType.String || oldText.Type != MaldaLang.Interpreter.ValueType.String || newText.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("replace() expects (string, string, string)");
        var textStr = text.AsString();
        var oldTextStr = oldText.AsString();
        var newTextStr = newText.AsString();
        var result = textStr.Replace(oldTextStr, newTextStr, StringComparison.Ordinal);
        return RuntimeValue.String(result);
    }
    
    private static RuntimeValue BuiltInSplit(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3) throw new Exception("split() expects 1 to 3 arguments");
        var text = args[0];
        if (text.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("split() expects (string, string?, bool?)");
        var textStr = text.AsString();
        
        var separator = args.Count > 1 ? args[1] : null;
        var keepSeparator = args.Count > 2 && args[2].Type == ValueType.Boolean && args[2].AsBoolean();
        var result = new List<RuntimeValue>();
        
        if (separator == null || separator.Type == MaldaLang.Interpreter.ValueType.Null)
        {
            // Split by whitespace (default)
            var parts = textStr.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                result.Add(RuntimeValue.String(part));
            }
        }
        else if (separator.Type == MaldaLang.Interpreter.ValueType.String)
        {
            var separatorStr = separator.AsString();
            
            // Check if separator looks like a regex pattern (contains regex special chars)
            // Simple heuristic: if it contains common regex chars, treat as regex
            bool isRegex = separatorStr.Contains("(") || separatorStr.Contains("[") || 
                          separatorStr.Contains("*") || separatorStr.Contains("+") || 
                          separatorStr.Contains("?") || separatorStr.Contains("\\");
            
            if (isRegex)
            {
                // Split by regex pattern
                try
                {
                    var regex = new Regex(separatorStr);
                    var matches = regex.Matches(textStr);
                    var lastIndex = 0;
                    
                    foreach (Match match in matches)
                    {
                        // Add text before match
                        if (match.Index > lastIndex)
                        {
                            result.Add(RuntimeValue.String(textStr.Substring(lastIndex, match.Index - lastIndex)));
                        }
                        
                        // Add separator if keepSeparator is true
                        if (keepSeparator)
                        {
                            result.Add(RuntimeValue.String(match.Value));
                        }
                        
                        lastIndex = match.Index + match.Length;
                    }
                    
                    // Add remaining text
                    if (lastIndex < textStr.Length)
                    {
                        result.Add(RuntimeValue.String(textStr.Substring(lastIndex)));
                    }
                }
                catch (ArgumentException ex)
                {
                    throw new Exception($"Invalid regex pattern in split(): {ex.Message}");
                }
            }
            else
            {
                // Split by string separator
                var parts = textStr.Split(new[] { separatorStr }, StringSplitOptions.None);
                foreach (var part in parts)
                {
                    result.Add(RuntimeValue.String(part));
                }
            }
        }
        else
        {
            throw new Exception("split() separator must be a string or null");
        }
        
        return RuntimeValue.Array(result);
    }

    private static RuntimeValue BuiltInNormalizeText(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("normalizeText() expects 1 or 2 arguments");
        if (args[0].Type != ValueType.String)
            throw new Exception("normalizeText() expects (string, options?)");

        var options = args.Count > 1 ? args[1] : RuntimeValue.Null();
        return RuntimeValue.String(NormalizeTextValue(args[0].AsString(), options));
    }

    private static RuntimeValue BuiltInTokenize(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("tokenize() expects 1 or 2 arguments");
        if (args[0].Type != ValueType.String)
            throw new Exception("tokenize() expects (string, options?)");

        var options = args.Count > 1 ? args[1] : RuntimeValue.Null();
        var normalized = NormalizeTextValue(args[0].AsString(), options);
        var tokens = TokenizeNormalizedText(normalized);
        return RuntimeValue.Array(tokens.Select(RuntimeValue.String).ToList());
    }

    private static RuntimeValue BuiltInTokenOverlap(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3) throw new Exception("tokenOverlap() expects 2 or 3 arguments");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("tokenOverlap() expects (string, string, options?)");

        var options = args.Count > 2 ? args[2] : RuntimeValue.Null();
        var leftTokens = TokenizeNormalizedText(NormalizeTextValue(args[0].AsString(), options));
        var rightTokens = TokenizeNormalizedText(NormalizeTextValue(args[1].AsString(), options));
        var leftSet = new HashSet<string>(leftTokens, StringComparer.Ordinal);
        var rightSet = new HashSet<string>(rightTokens, StringComparer.Ordinal);
        var shared = leftSet.Intersect(rightSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var unionCount = leftSet.Union(rightSet, StringComparer.Ordinal).Count();
        var payload = new JsonObject();
        payload.Set("sharedCount", RuntimeValue.Integer(shared.Count));
        payload.Set("leftCount", RuntimeValue.Integer(leftSet.Count));
        payload.Set("rightCount", RuntimeValue.Integer(rightSet.Count));
        payload.Set("jaccard", RuntimeValue.Float(unionCount == 0 ? 1.0 : (double)shared.Count / unionCount));
        payload.Set("sharedTokens", RuntimeValue.Array(shared.Select(RuntimeValue.String).ToList()));
        return RuntimeValue.Object(payload);
    }

    private static RuntimeValue BuiltInSimilarity(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 4) throw new Exception("similarity() expects 2 to 4 arguments");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("similarity() expects (string, string, method?, options?)");

        var left = args[0].AsString();
        var right = args[1].AsString();
        var method = args.Count > 2 && args[2].Type == ValueType.String
            ? args[2].AsString().Trim().ToLowerInvariant()
            : "jaccard";
        var options = args.Count > 3 ? args[3] : RuntimeValue.Null();

        var score = method switch
        {
            "exact" => string.Equals(
                NormalizeTextValue(left, options),
                NormalizeTextValue(right, options),
                StringComparison.Ordinal) ? 1.0 : 0.0,
            "contains" => ComputeContainsSimilarity(left, right, options),
            "char-ngram" => ComputeCharacterNGramSimilarity(left, right, options),
            _ => ComputeJaccardSimilarity(left, right, options)
        };

        return RuntimeValue.Float(score);
    }

    private static RuntimeValue BuiltInExtractNumbers(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("extractNumbers() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("extractNumbers() expects a string");

        var matches = Regex.Matches(args[0].AsString(), @"-?\d+(?:[.,]\d+)?");
        var values = new List<RuntimeValue>();
        foreach (Match match in matches)
        {
            var text = match.Value.Replace(',', '.');
            if (text.Contains('.'))
            {
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    values.Add(RuntimeValue.Float(floatValue));
                }
            }
            else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                values.Add(RuntimeValue.Integer(intValue));
            }
        }

        return RuntimeValue.Array(values);
    }
    
    private static RuntimeValue BuiltInRegexMatch(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("regexMatch() expects 2 arguments");
        var text = args[0];
        var pattern = args[1];
        if (text.Type != MaldaLang.Interpreter.ValueType.String || pattern.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("regexMatch() expects (string, string)");
        var textStr = text.AsString();
        var patternStr = pattern.AsString();
        try
        {
            var regex = new Regex(patternStr);
            var isMatch = regex.IsMatch(textStr);
            return RuntimeValue.Boolean(isMatch);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Invalid regex pattern: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInRegexReplace(List<RuntimeValue> args)
    {
        if (args.Count != 3) throw new Exception("regexReplace() expects 3 arguments");
        var text = args[0];
        var pattern = args[1];
        var replacement = args[2];
        if (text.Type != MaldaLang.Interpreter.ValueType.String || pattern.Type != MaldaLang.Interpreter.ValueType.String || replacement.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("regexReplace() expects (string, string, string)");
        var textStr = text.AsString();
        var patternStr = pattern.AsString();
        var replacementStr = replacement.AsString();
        try
        {
            var regex = new Regex(patternStr);
            var result = regex.Replace(textStr, replacementStr);
            return RuntimeValue.String(result);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Invalid regex pattern: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInRegexFind(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("regexFind() expects 2 arguments");
        var text = args[0];
        var pattern = args[1];
        if (text.Type != MaldaLang.Interpreter.ValueType.String || pattern.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("regexFind() expects (string, string)");
        var textStr = text.AsString();
        var patternStr = pattern.AsString();
        try
        {
            var regex = new Regex(patternStr);
            var matches = regex.Matches(textStr);
            var matchList = new List<RuntimeValue>();
            foreach (Match match in matches)
            {
                var matchObj = new BuiltIns.JsonObject();
                matchObj.Set("text", RuntimeValue.String(match.Value));
                var groups = new List<RuntimeValue>();
                for (int i = 0; i < match.Groups.Count; i++)
                {
                    groups.Add(RuntimeValue.String(match.Groups[i].Value));
                }
                matchObj.Set("groups", RuntimeValue.Array(groups));
                matchList.Add(RuntimeValue.Object(matchObj));
            }
            return RuntimeValue.Array(matchList);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Invalid regex pattern: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGetFileName(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("getFileName() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("getFileName() expects a string argument");
        var path = args[0].AsString();
        var fileName = Path.GetFileName(path);
        return RuntimeValue.String(fileName);
    }
    
    private static RuntimeValue BuiltInGetDirectoryName(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("getDirectoryName() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("getDirectoryName() expects a string argument");
        var path = args[0].AsString();
        var dirName = Path.GetDirectoryName(path);
        return RuntimeValue.String(dirName ?? "");
    }
    
    private static RuntimeValue BuiltInGetMaldaHome(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getMaldaHome", args, 0, 0);
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return RuntimeValue.String("");
        var maldaHome = Path.Combine(userProfile, ".malda");
        return RuntimeValue.String(maldaHome);
    }

    /// <summary>
    /// Directory of the running .malda script (interpreter) or of the compiled executable
    /// (<see cref="AppContext.BaseDirectory"/>). Useful for bundling assets next to the program.
    /// </summary>
    private static RuntimeValue BuiltInGetProgramDirectory(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("getProgramDirectory", args, 0, 0);

        // Prefer a real on-disk source script (interpreter). Ignore placeholders such as
        // TranspiledBuiltinRuntime's currentFile "transpiled", which would otherwise resolve
        // to the process CWD and miss assets placed beside the .exe (e.g. BGE-M3 GGUF).
        var currentFile = interpreter?.GetCurrentFile();
        if (!string.IsNullOrWhiteSpace(currentFile))
        {
            try
            {
                var full = Path.GetFullPath(currentFile);
                if (File.Exists(full))
                {
                    var dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir))
                        return RuntimeValue.String(dir);
                }
            }
            catch
            {
                // Fall through to executable directory.
            }
        }

        try
        {
            var processPath = System.Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var exeDir = Path.GetDirectoryName(Path.GetFullPath(processPath));
                if (!string.IsNullOrEmpty(exeDir))
                    return RuntimeValue.String(exeDir);
            }
        }
        catch
        {
            // Fall through to AppContext.BaseDirectory.
        }

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            try
            {
                return RuntimeValue.String(Path.GetFullPath(baseDir));
            }
            catch
            {
                return RuntimeValue.String(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        return RuntimeValue.String(Directory.GetCurrentDirectory());
    }

    private static RuntimeValue BuiltInGetAssistantMemory(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("getAssistantMemory", args, 0, 1, "path?");
        if (interpreter == null)
            throw new MaldaLang.Interpreter.RuntimeException("getAssistantMemory() requires interpreter context");
        string? path = null;
        if (args.Count >= 1 && args[0].Type == MaldaLang.Interpreter.ValueType.String)
            path = args[0].AsString();
        var memory = GraphMemoryBootstrap.CreateAssistantMemory(interpreter, path);
        return RuntimeValue.Object(memory);
    }
    
    private static RuntimeValue? TryLoadMaldaConfigFile(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            return JsonToRuntimeValue(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static RuntimeValue BuiltInGetMaldaConfig(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getMaldaConfig", args, 0, 0);
        // 1. Explicit path (MALDA_CONFIG)
        var configOverride = System.Environment.GetEnvironmentVariable("MALDA_CONFIG");
        if (!string.IsNullOrWhiteSpace(configOverride))
        {
            var fromEnv = TryLoadMaldaConfigFile(configOverride);
            if (fromEnv != null)
                return fromEnv;
        }

        // 2. ./.malda/config.json in current directory, then parent directories (project root when cwd is a subfolder)
        var dir = System.Environment.CurrentDirectory;
        for (var depth = 0; depth < 12 && !string.IsNullOrEmpty(dir); depth++)
        {
            var localPath = Path.Combine(dir, ".malda", "config.json");
            var fromLocal = TryLoadMaldaConfigFile(localPath);
            if (fromLocal != null)
                return fromLocal;

            var parent = Directory.GetParent(dir);
            if (parent == null)
                break;
            dir = parent.FullName;
        }

        // 3. ~/.malda/config.json
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            var userPath = Path.Combine(userProfile, ".malda", "config.json");
            var fromUser = TryLoadMaldaConfigFile(userPath);
            if (fromUser != null)
                return fromUser;
        }

        return RuntimeValue.Null();
    }
    
    private static RuntimeValue BuiltInEnableAgentVerboseLogging(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("enableAgentVerboseLogging() expects 0 or 1 arguments");
        var enabled = args.Count == 0 || args[0].IsTruthy();
        ConversationInstance.EnableVerboseLogging(enabled);
        return RuntimeValue.Boolean(enabled);
    }

    private static RuntimeValue BuiltInSetAgentVerbosePhase(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("setAgentVerbosePhase() expects 1 string argument (phase label)");
        ConversationInstance.SetVerbosePhase(args[0].AsString());
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInSetAgentStatusBanner(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("setAgentStatusBanner() expects 1 string argument (status banner line)");
        ConversationInstance.SetStatusBanner(args[0].AsString());
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInReportRalphStatus(List<RuntimeValue> args)
    {
        if (args.Count < 5)
            throw new Exception("reportRalphStatus() expects at least 5 arguments (agentName, phase, iteration, maxIter, prdPercent)");
        var agentName = args[0].AsString();
        var phase = args[1].AsString();
        var iteration = args[2].Type == ValueType.Integer ? (int)args[2].AsInteger() : 0;
        var maxIter = args[3].Type == ValueType.Integer ? (int)args[3].AsInteger() : 0;
        var prdPercent = args[4].Type == ValueType.Integer ? (int)args[4].AsInteger() : 0;
        var validationOk = args.Count > 5 && args[5].Type == ValueType.Boolean && args[5].AsBoolean();
        var elapsedMs = args.Count > 6 && args[6].Type == ValueType.Integer ? args[6].AsInteger() : 0L;
        var promptTokens = args.Count > 7 && args[7].Type == ValueType.Integer ? (int)args[7].AsInteger() : 0;
        var completionTokens = args.Count > 8 && args[8].Type == ValueType.Integer ? (int)args[8].AsInteger() : 0;
        var costUsd = 0.0;
        if (args.Count > 9 && (args[9].Type == ValueType.Float || args[9].Type == ValueType.Integer))
            costUsd = args[9].Type == ValueType.Float ? args[9].AsFloat() : args[9].AsInteger();
        AgentDashboardService.Instance.ReportRalphStatus(agentName, phase, iteration, maxIter, prdPercent, validationOk, elapsedMs, promptTokens, completionTokens, costUsd);
        return RuntimeValue.Null();
    }
    
    private static RuntimeValue BuiltInGetSkillNames(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getSkillNames", args, 0, 0);
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return RuntimeValue.Array(new List<RuntimeValue>());
        var skillsPath = Path.Combine(userProfile, ".malda", "skills");
        if (!Directory.Exists(skillsPath))
            return RuntimeValue.Array(new List<RuntimeValue>());
        var files = Directory.GetFiles(skillsPath, "*.malda");
        var names = new List<RuntimeValue>();
        foreach (var f in files)
            names.Add(RuntimeValue.String(Path.GetFileNameWithoutExtension(f)));
        return RuntimeValue.Array(names);
    }
    
    private static RuntimeValue BuiltInLoadSkill(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("loadSkill", args, 1, 1, "name");
        if (interpreter == null)
            return RuntimeValue.Null();
        var name = args[0].AsString();
        if (string.IsNullOrEmpty(name))
            return RuntimeValue.Null();
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return RuntimeValue.Null();
        var path = Path.Combine(userProfile, ".malda", "skills", name + ".malda");
        if (!File.Exists(path))
            return RuntimeValue.Null();
        return interpreter.LoadSkillModule(path);
    }

    private static RuntimeValue BuiltInLoadSkillsFromDir(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("loadSkillsFromDir", args, 0, 1, "skillsPath?");
        if (interpreter == null)
            return RuntimeValue.Array(new List<RuntimeValue>());

        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var skillsPath = string.IsNullOrEmpty(userProfile)
            ? ""
            : Path.Combine(userProfile, ".malda", "skills");
        if (args.Count > 0 && args[0].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[0].AsString()))
            skillsPath = args[0].AsString();

        var results = new List<RuntimeValue>();
        if (string.IsNullOrWhiteSpace(skillsPath) || !Directory.Exists(skillsPath))
            return RuntimeValue.Array(results);

        foreach (var file in Directory.GetFiles(skillsPath, "*.malda").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            try
            {
                var skill = interpreter.LoadSkillModule(file);
                var wrapper = new JsonObject();
                wrapper.Set("name", RuntimeValue.String(name));
                if (skill.Type == ValueType.Object && skill.AsObject() is ObjectInstance skillObj)
                {
                    foreach (var key in skillObj.GetAllKeys())
                        wrapper.Set(key, skillObj.Get(key, null) ?? RuntimeValue.Null());
                }
                else if (skill.Type == ValueType.Null)
                {
                    wrapper.Set("error", RuntimeValue.String("skill file missing or empty"));
                }
                results.Add(RuntimeValue.Object(wrapper));
            }
            catch (Exception ex)
            {
                var err = new JsonObject();
                err.Set("name", RuntimeValue.String(name));
                err.Set("error", RuntimeValue.String(ex.Message));
                results.Add(RuntimeValue.Object(err));
            }
        }

        return RuntimeValue.Array(results);
    }
    
    // ========== Date/Time Functions ==========
    
    private static RuntimeValue BuiltInNow(List<RuntimeValue> args)
    {
        if (args.Count != 0) throw new Exception("now() expects 0 arguments");
        var now = DateTimeOffset.UtcNow;
        return RuntimeValue.Float(now.ToUnixTimeMilliseconds());
    }
    
    private static RuntimeValue BuiltInFormatDate(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("formatDate() expects 1-2 arguments: (timestamp, format?)");
        var timestamp = args[0];
        var format = args.Count > 1 ? args[1] : null;
        
        double timestampMs;
        if (timestamp.Type == ValueType.Integer)
            timestampMs = timestamp.AsInteger();
        else if (timestamp.Type == ValueType.Float)
            timestampMs = timestamp.AsFloat();
        else
            throw new Exception("formatDate() timestamp must be a number");
        
        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs).DateTime;
        var formatStr = format != null && format.Type == ValueType.String ? format.AsString() : "yyyy-MM-dd HH:mm:ss";
        
        return RuntimeValue.String(dateTime.ToString(formatStr));
    }
    
    private static RuntimeValue BuiltInParseDate(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("parseDate() expects 1 argument: (dateString)");
        if (args[0].Type != ValueType.String)
            throw new Exception("parseDate() expects a string argument");
        
        var dateStr = args[0].AsString();
        if (DateTimeOffset.TryParse(dateStr, out var dateTime))
        {
            return RuntimeValue.Float(dateTime.ToUnixTimeMilliseconds());
        }
        throw new Exception($"parseDate() could not parse date string: {dateStr}");
    }
    
    private static RuntimeValue BuiltInAddDays(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("addDays() expects 2 arguments: (timestamp, days)");
        var timestamp = args[0];
        var days = args[1];
        
        double timestampMs;
        if (timestamp.Type == ValueType.Integer)
            timestampMs = timestamp.AsInteger();
        else if (timestamp.Type == ValueType.Float)
            timestampMs = timestamp.AsFloat();
        else
            throw new Exception("addDays() timestamp must be a number");
        
        double daysVal;
        if (days.Type == ValueType.Integer)
            daysVal = days.AsInteger();
        else if (days.Type == ValueType.Float)
            daysVal = days.AsFloat();
        else
            throw new Exception("addDays() days must be a number");
        
        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs);
        var result = dateTime.AddDays(daysVal);
        return RuntimeValue.Float(result.ToUnixTimeMilliseconds());
    }
    
    private static RuntimeValue BuiltInAddHours(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("addHours() expects 2 arguments: (timestamp, hours)");
        var timestamp = args[0];
        var hours = args[1];
        
        double timestampMs;
        if (timestamp.Type == ValueType.Integer)
            timestampMs = timestamp.AsInteger();
        else if (timestamp.Type == ValueType.Float)
            timestampMs = timestamp.AsFloat();
        else
            throw new Exception("addHours() timestamp must be a number");
        
        double hoursVal;
        if (hours.Type == ValueType.Integer)
            hoursVal = hours.AsInteger();
        else if (hours.Type == ValueType.Float)
            hoursVal = hours.AsFloat();
        else
            throw new Exception("addHours() hours must be a number");
        
        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs);
        var result = dateTime.AddHours(hoursVal);
        return RuntimeValue.Float(result.ToUnixTimeMilliseconds());
    }
    
    // ========== Random Functions ==========
    
    private static Random _random = new Random();
    
    private static RuntimeValue BuiltInRandom(List<RuntimeValue> args)
    {
        if (args.Count != 0) throw new Exception("random() expects 0 arguments");
        return RuntimeValue.Float(_random.NextDouble());
    }
    
    private static RuntimeValue BuiltInRandomInt(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("randomInt() expects 2 arguments: (min, max)");
        if (!NumericCoercion.TryAsInteger(args[0], out var min)
            || !NumericCoercion.TryAsInteger(args[1], out var max))
            throw new Exception("randomInt() expects integer arguments");

        if (min > max)
            throw new Exception("randomInt() min must be <= max");
        
        return RuntimeValue.Integer(_random.Next(min, max + 1));
    }
    
    private static RuntimeValue BuiltInRandomFloat(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("randomFloat() expects 2 arguments: (min, max)");
        double min, max;
        
        if (args[0].Type == ValueType.Integer)
            min = args[0].AsInteger();
        else if (args[0].Type == ValueType.Float)
            min = args[0].AsFloat();
        else
            throw new Exception("randomFloat() min must be a number");
        
        if (args[1].Type == ValueType.Integer)
            max = args[1].AsInteger();
        else if (args[1].Type == ValueType.Float)
            max = args[1].AsFloat();
        else
            throw new Exception("randomFloat() max must be a number");
        
        if (min > max)
            throw new Exception("randomFloat() min must be <= max");
        
        var range = max - min;
        return RuntimeValue.Float(min + _random.NextDouble() * range);
    }
    
    // ========== Type Checking Functions ==========
    
    private static RuntimeValue BuiltInIsNumber(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("isNumber() expects 1 argument");
        var arg = args[0];
        return RuntimeValue.Boolean(arg.Type == ValueType.Integer || arg.Type == ValueType.Float);
    }
    
    private static RuntimeValue BuiltInIsString(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("isString() expects 1 argument");
        return RuntimeValue.Boolean(args[0].Type == ValueType.String);
    }
    
    private static RuntimeValue BuiltInIsArray(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("isArray() expects 1 argument");
        return RuntimeValue.Boolean(args[0].Type == ValueType.Array);
    }
    
    private static RuntimeValue BuiltInIsObject(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("isObject() expects 1 argument");
        return RuntimeValue.Boolean(args[0].Type == ValueType.Object);
    }
    
    private static RuntimeValue BuiltInTypeOf(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("typeOf() expects 1 argument");
        return RuntimeValue.String(Tier0TypeTags.GetTag(args[0]));
    }

    private static RuntimeValue BuiltInIsTag(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("isTag() expects 2 arguments: (value, tag)");
        if (args[1].Type != ValueType.String)
            throw new Exception("isTag() second argument must be a string tag");
        var actual = Tier0TypeTags.GetTag(args[0]);
        return RuntimeValue.Boolean(Tier0TypeTags.MatchesTag(actual, args[1].AsString()));
    }

    private static RuntimeValue BuiltInValidate(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("validate() expects 2 arguments: (schema, value)");

        var schema = SchemaRegistry.ResolveSchemaArgument(args[0]);
        var value = args[1];
        var result = new JsonObject();

        if (TypedPromptValidator.TryValidateReturnType(value, schema, out var error))
        {
            result.Set("ok", RuntimeValue.Boolean(true));
            result.Set("data", value);
            return RuntimeValue.Object(result);
        }

        result.Set("ok", RuntimeValue.Boolean(false));
        result.Set("error", RuntimeValue.String(error));
        return RuntimeValue.Object(result);
    }

    // ========== Array Utilities ==========
    
    private static RuntimeValue BuiltInJoin(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("join() expects 1-2 arguments: (array, separator?)");
        if (args[0].Type != ValueType.Array)
            throw new Exception("join() expects an array as first argument");
        
        var array = args[0].AsArray();
        var separator = args.Count > 1 && args[1].Type == ValueType.String ? args[1].AsString() : ",";
        
        var stringParts = new List<string>();
        foreach (var item in array)
        {
            stringParts.Add(item.ToString());
        }
        
        return RuntimeValue.String(string.Join(separator, stringParts));
    }

    private static RuntimeValue BuiltInToCsv(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3) throw new Exception("toCsv() expects 1-3 arguments: (rows, columns?, options?)");
        if (args[0].Type != ValueType.Array)
            throw new Exception("toCsv() first argument must be an array");

        var rows = args[0].AsArray();
        var columns = new List<string>();
        JsonObject? options = null;

        if (args.Count >= 2 && args[1].Type != ValueType.Null)
        {
            if (args[1].Type == ValueType.Array)
            {
                foreach (var col in args[1].AsArray())
                {
                    columns.Add(col.Type == ValueType.String ? col.AsString() : col.ToString());
                }
            }
            else if (args[1].Type == ValueType.Object)
            {
                options = CoerceRuntimeObjectToJsonObject(args[1]);
            }
            else
            {
                throw new Exception("toCsv() second argument must be columns array, options object, or null");
            }
        }

        if (args.Count == 3)
        {
            if (args[2].Type == ValueType.Object)
            {
                options = CoerceRuntimeObjectToJsonObject(args[2]);
            }
            else if (args[2].Type != ValueType.Null)
            {
                throw new Exception("toCsv() third argument must be options object or null");
            }
        }

        var delimiter = options != null ? GetStringOptionFromObject(options, "delimiter", ",") : ",";
        var newline = options != null ? GetStringOptionFromObject(options, "newline", "\n") : "\n";
        var includeHeader = options == null || GetBooleanOptionFromObject(options, "includeHeader", true);
        var quoteAll = options != null && GetBooleanOptionFromObject(options, "quoteAll", false);

        if (columns.Count == 0)
        {
            columns = InferCsvColumns(rows);
        }

        var lines = new List<string>();
        if (includeHeader && columns.Count > 0)
        {
            lines.Add(string.Join(delimiter, columns.Select(c => EscapeCsvCell(c, delimiter, quoteAll))));
        }

        foreach (var row in rows)
        {
            var cells = new List<string>();
            for (int i = 0; i < columns.Count; i++)
            {
                var value = ResolveCsvValue(row, columns[i]);
                var text = value.Type == ValueType.Null ? string.Empty : RuntimeValueToTemplateString(value);
                cells.Add(EscapeCsvCell(text, delimiter, quoteAll));
            }

            lines.Add(string.Join(delimiter, cells));
        }

        return RuntimeValue.String(string.Join(newline, lines));
    }

    private static List<string> InferCsvColumns(List<RuntimeValue> rows)
    {
        if (rows.Count == 0)
        {
            return new List<string>();
        }

        var first = rows[0];
        var firstObj = CoerceRuntimeObjectToJsonObject(first);
        if (firstObj == null)
        {
            return new List<string> { "value" };
        }

        return firstObj.GetProperties().Select(kvp => kvp.Key).ToList();
    }

    private static RuntimeValue ResolveCsvValue(RuntimeValue row, string columnName)
    {
        var obj = CoerceRuntimeObjectToJsonObject(row);
        if (obj != null)
        {
            return obj.Get(columnName, null);
        }

        return columnName == "value" ? row : RuntimeValue.Null();
    }

    private static string EscapeCsvCell(string text, string delimiter, bool quoteAll)
    {
        var raw = text ?? string.Empty;
        var escaped = raw.Replace("\"", "\"\"");
        var mustQuote = quoteAll ||
                        escaped.Contains(delimiter, StringComparison.Ordinal) ||
                        escaped.Contains("\"", StringComparison.Ordinal) ||
                        escaped.Contains("\n", StringComparison.Ordinal) ||
                        escaped.Contains("\r", StringComparison.Ordinal);
        return mustQuote ? $"\"{escaped}\"" : escaped;
    }
    
    private static RuntimeValue BuiltInReverse(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("reverse() expects 1 argument");
        if (args[0].Type != ValueType.Array)
            throw new Exception("reverse() expects an array");
        
        var array = args[0].AsArray();
        var reversed = new List<RuntimeValue>(array);
        reversed.Reverse();
        return RuntimeValue.Array(reversed);
    }
    
    private static RuntimeValue BuiltInSort(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("sort() expects 1-2 arguments: (array, compareFn?)");
        if (args[0].Type != ValueType.Array)
            throw new Exception("sort() expects an array as first argument");
        
        var array = args[0].AsArray();
        var sorted = new List<RuntimeValue>(array);
        
        // Simple numeric/string sort if no compare function
        if (args.Count == 1 || args[1].Type == ValueType.Null)
        {
            sorted.Sort((a, b) =>
            {
                // Try to compare as numbers first
                if (a.Type == ValueType.Integer && b.Type == ValueType.Integer)
                    return a.AsInteger().CompareTo(b.AsInteger());
                if ((a.Type == ValueType.Integer || a.Type == ValueType.Float) &&
                    (b.Type == ValueType.Integer || b.Type == ValueType.Float))
                {
                    var aVal = a.Type == ValueType.Integer ? a.AsInteger() : a.AsFloat();
                    var bVal = b.Type == ValueType.Integer ? b.AsInteger() : b.AsFloat();
                    return aVal.CompareTo(bVal);
                }
                // Otherwise compare as strings
                return a.ToString().CompareTo(b.ToString());
            });
        }
        else
        {
            if (args[1].Type != ValueType.Function)
                throw new Exception("sort() second argument must be a function or null");
            if (interpreter == null)
                throw new Exception("sort() with compare function is not supported in transpiled code; use interpreter mode.");
            var compareFn = args[1].AsFunction();
            sorted.Sort((a, b) =>
            {
                var result = interpreter.CallFunctionAsync(compareFn, new List<RuntimeValue> { a, b }).GetAwaiter().GetResult();
                if (result.Type == ValueType.Integer)
                    return result.AsInteger();
                if (result.Type == ValueType.Float)
                    return (int)result.AsFloat();
                if (result.Type == ValueType.Boolean)
                    return result.AsBoolean() ? 1 : -1;
                return result.IsTruthy() ? 1 : -1;
            });
        }
        
        return RuntimeValue.Array(sorted);
    }
    
    private static RuntimeValue BuiltInIncludes(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("includes() expects 2 arguments: (array, value)");
        if (args[0].Type != ValueType.Array)
            throw new Exception("includes() expects an array as first argument");
        
        var array = args[0].AsArray();
        var searchValue = args[1];
        
        foreach (var item in array)
        {
            if (item.Type == searchValue.Type && item.ToString() == searchValue.ToString())
                return RuntimeValue.Boolean(true);
        }
        
        return RuntimeValue.Boolean(false);
    }
    
    // ========== Encoding/Decoding Functions ==========
    
    private static RuntimeValue BuiltInBase64Encode(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("base64Encode() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("base64Encode() expects a string argument");
        
        var text = args[0].AsString();
        var bytes = Encoding.UTF8.GetBytes(text);
        var encoded = Convert.ToBase64String(bytes);
        return RuntimeValue.String(encoded);
    }
    
    private static RuntimeValue BuiltInBase64Decode(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("base64Decode() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("base64Decode() expects a string argument");
        
        try
        {
            var encoded = args[0].AsString();
            var bytes = Convert.FromBase64String(encoded);
            var decoded = Encoding.UTF8.GetString(bytes);
            return RuntimeValue.String(decoded);
        }
        catch (FormatException)
        {
            throw new Exception("base64Decode() invalid base64 string");
        }
    }
    
    private static RuntimeValue BuiltInUrlEncode(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("urlEncode() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("urlEncode() expects a string argument");
        
        var text = args[0].AsString();
        var encoded = Uri.EscapeDataString(text);
        return RuntimeValue.String(encoded);
    }
    
    private static RuntimeValue BuiltInUrlDecode(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("urlDecode() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("urlDecode() expects a string argument");
        
        try
        {
            var encoded = args[0].AsString();
            var decoded = Uri.UnescapeDataString(encoded);
            return RuntimeValue.String(decoded);
        }
        catch (UriFormatException)
        {
            throw new Exception("urlDecode() invalid URL encoded string");
        }
    }
    
    // ========== Hash Functions ==========
    
    private static RuntimeValue BuiltInMd5(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("md5() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("md5() expects a string argument");
        
        var text = args[0].AsString();
        var bytes = Encoding.UTF8.GetBytes(text);
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var hashBytes = md5.ComputeHash(bytes);
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return RuntimeValue.String(hash);
        }
    }
    
    private static RuntimeValue BuiltInSha256(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("sha256() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("sha256() expects a string argument");
        
        var text = args[0].AsString();
        var bytes = Encoding.UTF8.GetBytes(text);
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(bytes);
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return RuntimeValue.String(hash);
        }
    }

    private static RuntimeValue BuiltInHashPassword(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("hashPassword() expects 1 or 2 arguments: (password, iterations?)");
        if (args[0].Type != ValueType.String)
            throw new Exception("hashPassword() first argument must be a string");

        var password = args[0].AsString();
        var iterations = 100_000;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Integer)
                throw new Exception("hashPassword() second argument (iterations) must be an integer");
            iterations = args[1].AsInteger();
        }

        if (iterations < 10_000)
            throw new Exception("hashPassword() iterations must be >= 10000");

        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

        var encoded = string.Join(
            "$",
            "pbkdf2",
            "sha256",
            iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
        return RuntimeValue.String(encoded);
    }

    private static RuntimeValue BuiltInVerifyPassword(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("verifyPassword() expects 2 arguments: (password, passwordHash)");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("verifyPassword() expects (string, string)");

        var password = args[0].AsString();
        var storedHash = args[1].AsString();
        var parts = storedHash.Split('$');
        if (parts.Length != 5 || parts[0] != "pbkdf2" || parts[1] != "sha256")
            return RuntimeValue.Boolean(false);

        if (!int.TryParse(parts[2], out var iterations) || iterations < 10_000)
            return RuntimeValue.Boolean(false);

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedHash = Convert.FromBase64String(parts[4]);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        var isValid = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        return RuntimeValue.Boolean(isValid);
    }

    private static RuntimeValue BuiltInCreateJwt(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3)
            throw new Exception("createJwt() expects 2 or 3 arguments: (payload, secret, expiresInSeconds?)");
        if (args[1].Type != ValueType.String)
            throw new Exception("createJwt() second argument (secret) must be a string");

        var payloadObj = NormalizeJwtPayload(args[0]);
        var secret = args[1].AsString();
        if (string.IsNullOrWhiteSpace(secret))
            throw new Exception("createJwt() secret cannot be empty");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        payloadObj.Set("iat", RuntimeValue.Integer((int)now));

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Integer)
                throw new Exception("createJwt() third argument (expiresInSeconds) must be an integer");
            var expiresInSeconds = args[2].AsInteger();
            payloadObj.Set("exp", RuntimeValue.Integer((int)(now + expiresInSeconds)));
        }

        var header = RuntimeValue.String("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = RuntimeValue.String(RuntimeValueToJson(RuntimeValue.Object(payloadObj)));
        var encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(header.AsString()));
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload.AsString()));
        var signingInput = $"{encodedHeader}.{encodedPayload}";
        var signature = ComputeJwtSignature(signingInput, secret);

        return RuntimeValue.String($"{signingInput}.{signature}");
    }

    private static RuntimeValue BuiltInVerifyJwt(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("verifyJwt() expects 2 arguments: (token, secret)");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("verifyJwt() expects (string, string)");

        var token = args[0].AsString();
        var secret = args[1].AsString();

        if (string.IsNullOrWhiteSpace(token))
            throw new WebRuntimeException(401, "MissingToken", "Missing token.");
        if (string.IsNullOrWhiteSpace(secret))
            throw new Exception("verifyJwt() secret cannot be empty");

        var parts = token.Split('.');
        if (parts.Length != 3)
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token.");

        var signingInput = $"{parts[0]}.{parts[1]}";
        var expectedSignature = ComputeJwtSignature(signingInput, secret);
        var actualSignatureBytes = Encoding.UTF8.GetBytes(parts[2]);
        var expectedSignatureBytes = Encoding.UTF8.GetBytes(expectedSignature);
        if (!CryptographicOperations.FixedTimeEquals(actualSignatureBytes, expectedSignatureBytes))
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token signature.");

        RuntimeValue payloadValue;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var payloadDoc = JsonDocument.Parse(payloadJson);
            payloadValue = JsonToRuntimeValue(payloadDoc.RootElement);
        }
        catch
        {
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token payload.");
        }

        if (payloadValue.Type != ValueType.Object || payloadValue.AsObject() is not JsonObject payloadObject)
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token payload.");

        if (TryReadUnixTimestamp(payloadObject, "exp", out var exp))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now >= exp)
                throw new WebRuntimeException(401, "TokenExpired", "Token expired.");
        }

        return RuntimeValue.Object(payloadObject);
    }

    private static RuntimeValue BuiltInGenerateCsrfToken(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("generateCsrfToken() expects secret and optional ttlSeconds");
        if (args[0].Type != ValueType.String)
            throw new Exception("generateCsrfToken() secret must be a string");

        var ttlSeconds = 7200;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Integer)
                throw new Exception("generateCsrfToken() ttlSeconds must be an integer");
            ttlSeconds = args[1].AsInteger();
        }

        var token = WebRuntimeHelpers.GenerateCsrfToken(args[0].AsString(), ttlSeconds);
        return RuntimeValue.String(token);
    }

    private static RuntimeValue BuiltInVerifyCsrfToken(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("verifyCsrfToken() expects token and secret");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("verifyCsrfToken() expects (string, string)");

        var isValid = WebRuntimeHelpers.VerifyCsrfToken(args[0].AsString(), args[1].AsString());
        return RuntimeValue.Boolean(isValid);
    }

    private static RuntimeValue BuiltInCreateSecureCookie(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args.Count > 4)
            throw new Exception("createSecureCookie() expects name, value, secret, and optional options object");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String || args[2].Type != ValueType.String)
            throw new Exception("createSecureCookie() expects (string, string, string, object?)");

        RuntimeValue options = RuntimeValue.Null();
        if (args.Count == 4)
        {
            options = args[3];
            if (options.Type != ValueType.Null && options.Type != ValueType.Object)
                throw new Exception("createSecureCookie() options must be an object when provided");
        }

        int? maxAge = null;
        if (options.Type == ValueType.Object && options.AsObject() is JsonObject optionsObj)
        {
            var maxAgeValue = optionsObj.Get("maxAge", null);
            if (maxAgeValue.Type == ValueType.Integer)
            {
                maxAge = maxAgeValue.AsInteger();
            }
        }

        var signedValue = WebRuntimeHelpers.CreateSecureCookieValue(args[1].AsString(), args[2].AsString(), maxAge);
        var header = WebRuntimeHelpers.CreateCookieHeader(args[0].AsString(), signedValue, options, useSecureDefaults: true);
        return RuntimeValue.String(header);
    }

    private static RuntimeValue BuiltInReadSecureCookie(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("readSecureCookie() expects cookieValue and secret");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("readSecureCookie() expects (string, string)");

        if (WebRuntimeHelpers.TryReadSecureCookieValue(args[0].AsString(), args[1].AsString(), out var plainValue))
        {
            return RuntimeValue.String(plainValue);
        }

        return RuntimeValue.Null();
    }

    private static JsonObject NormalizeJwtPayload(RuntimeValue value)
    {
        if (value.Type == ValueType.Object)
        {
            var objectValue = value.AsObject();
            if (objectValue is JsonObject obj)
            {
                var jsonCopy = new JsonObject();
                foreach (var kvp in obj.GetProperties())
                {
                    jsonCopy.Set(kvp.Key, kvp.Value);
                }
                return jsonCopy;
            }

            var copy = new JsonObject();
            foreach (var key in objectValue.GetAllKeys())
            {
                copy.Set(key, objectValue.Get(key, null));
            }
            return copy;
        }

        if (value.Type == ValueType.String)
        {
            try
            {
                using var doc = JsonDocument.Parse(value.AsString());
                var parsed = JsonToRuntimeValue(doc.RootElement);
                if (parsed.Type == ValueType.Object && parsed.AsObject() is JsonObject parsedObj)
                {
                    return parsedObj;
                }
            }
            catch
            {
                // Handled below.
            }
        }

        throw new Exception("createJwt() payload must be an object or JSON object string");
    }

    private static string ComputeJwtSignature(string signingInput, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        return Base64UrlEncode(signatureBytes);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        var mod4 = padded.Length % 4;
        if (mod4 == 2) padded += "==";
        else if (mod4 == 3) padded += "=";
        else if (mod4 != 0) throw new FormatException("Invalid base64url payload.");
        return Convert.FromBase64String(padded);
    }

    private static bool TryReadUnixTimestamp(JsonObject payload, string fieldName, out long unixTimestamp)
    {
        unixTimestamp = 0;
        var value = payload.Get(fieldName, null);
        if (value.Type == ValueType.Integer)
        {
            unixTimestamp = value.AsInteger();
            return true;
        }

        if (value.Type == ValueType.Float)
        {
            unixTimestamp = (long)value.AsFloat();
            return true;
        }

        if (value.Type == ValueType.String && long.TryParse(value.AsString(), out var parsed))
        {
            unixTimestamp = parsed;
            return true;
        }

        return false;
    }
    
    // ========== Path Manipulation Functions ==========
    
    private static RuntimeValue BuiltInPathJoin(List<RuntimeValue> args)
    {
        if (args.Count < 1) throw new Exception("pathJoin() expects at least 1 argument");
        var parts = new List<string>();
        foreach (var arg in args)
        {
            if (arg.Type != ValueType.String)
                throw new Exception("pathJoin() all arguments must be strings");
            parts.Add(arg.AsString());
        }

        if (parts.Count > 0 && EmbeddedFolderStore.IsEmbedPath(parts[0]))
        {
            var rest = parts.Skip(1).ToArray();
            return RuntimeValue.String(EmbeddedFolderStore.Join(parts[0], rest));
        }

        return RuntimeValue.String(Path.Combine(parts.ToArray()));
    }

    private static RuntimeValue BuiltInHasEmbeddedFolder(List<RuntimeValue> args)
    {
        BuiltInArity.Require("hasEmbeddedFolder", args, 1, 1, "alias");
        if (args[0].Type != ValueType.String)
            throw new Exception("hasEmbeddedFolder() expects a string alias");
        return RuntimeValue.Boolean(EmbeddedFolderStore.HasAlias(args[0].AsString()));
    }

    private static RuntimeValue BuiltInEmbeddedFolderRoot(List<RuntimeValue> args)
    {
        BuiltInArity.Require("embeddedFolderRoot", args, 1, 1, "alias");
        if (args[0].Type != ValueType.String)
            throw new Exception("embeddedFolderRoot() expects a string alias");
        var alias = args[0].AsString();
        if (!EmbeddedFolderStore.HasAlias(alias))
            return RuntimeValue.Null();
        return RuntimeValue.String(EmbeddedFolderStore.MakeRoot(alias));
    }

    private static void RejectEmbedWrite(string path, string operation)
    {
        if (EmbeddedFolderStore.IsEmbedPath(path))
            throw new Exception($"{operation}() cannot write to embedded path '{path}'");
    }
    
    private static RuntimeValue BuiltInPathNormalize(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("pathNormalize() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("pathNormalize() expects a string argument");
        
        var path = args[0].AsString();
        return RuntimeValue.String(Path.GetFullPath(path));
    }
    
    private static RuntimeValue BuiltInPathExists(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("pathExists() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("pathExists() expects a string argument");
        
        var path = args[0].AsString();
        if (EmbeddedFolderStore.IsEmbedPath(path))
        {
            return RuntimeValue.Boolean(EmbeddedFolderStore.HasFile(path) || EmbeddedFolderStore.HasDirectory(path));
        }
        return RuntimeValue.Boolean(File.Exists(path) || Directory.Exists(path));
    }
    
    private static RuntimeValue BuiltInPathGetExtension(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("pathGetExtension() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("pathGetExtension() expects a string argument");
        
        var path = args[0].AsString();
        return RuntimeValue.String(Path.GetExtension(path));
    }
    
    // ========== Range Generation ==========
    
    private static RuntimeValue BuiltInRange(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3) throw new Exception("range() expects 1-3 arguments: (end) or (start, end) or (start, end, step)");
        
        int start, end, step = 1;
        
        if (args.Count == 1)
        {
            if (!NumericCoercion.TryAsInteger(args[0], out end))
                throw new Exception("range(end) expects an integer");
            start = 0;
        }
        else
        {
            if (!NumericCoercion.TryAsInteger(args[0], out start)
                || !NumericCoercion.TryAsInteger(args[1], out end))
                throw new Exception("range(start, end) expects integers");

            if (args.Count == 3)
            {
                if (!NumericCoercion.TryAsInteger(args[2], out step))
                    throw new Exception("range(start, end, step) step must be an integer");
                if (step == 0)
                    throw new Exception("range() step cannot be zero");
            }
        }
        
        var result = new List<RuntimeValue>();
        if (step > 0)
        {
            for (int i = start; i < end; i += step)
                result.Add(RuntimeValue.Integer(i));
        }
        else
        {
            for (int i = start; i > end; i += step)
                result.Add(RuntimeValue.Integer(i));
        }
        
        return RuntimeValue.Array(result);
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
    
    // ========== Additional String Utilities ==========
    
    private static RuntimeValue BuiltInStartsWith(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("startsWith() expects 2 arguments: (str, prefix)");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("startsWith() expects string arguments");
        
        var str = args[0].AsString();
        var prefix = args[1].AsString();
        return RuntimeValue.Boolean(str.StartsWith(prefix, StringComparison.Ordinal));
    }
    
    private static RuntimeValue BuiltInEndsWith(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("endsWith() expects 2 arguments: (str, suffix)");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("endsWith() expects string arguments");
        
        var str = args[0].AsString();
        var suffix = args[1].AsString();
        return RuntimeValue.Boolean(str.EndsWith(suffix, StringComparison.Ordinal));
    }
    
    private static RuntimeValue BuiltInPadStart(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3) throw new Exception("padStart() expects 2-3 arguments: (str, length, padChar?)");
        if (args[0].Type != ValueType.String || !NumericCoercion.TryAsInteger(args[1], out var length))
            throw new Exception("padStart() expects (string, integer, string?)");

        var str = args[0].AsString();
        var padChar = args.Count > 2 && args[2].Type == ValueType.String && args[2].AsString().Length > 0
            ? args[2].AsString()[0]
            : ' ';
        
        if (str.Length >= length)
            return RuntimeValue.String(str);
        
        return RuntimeValue.String(str.PadLeft(length, padChar));
    }
    
    private static RuntimeValue BuiltInPadEnd(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3) throw new Exception("padEnd() expects 2-3 arguments: (str, length, padChar?)");
        if (args[0].Type != ValueType.String || !NumericCoercion.TryAsInteger(args[1], out var length))
            throw new Exception("padEnd() expects (string, integer, string?)");

        var str = args[0].AsString();
        var padChar = args.Count > 2 && args[2].Type == ValueType.String && args[2].AsString().Length > 0
            ? args[2].AsString()[0]
            : ' ';
        
        if (str.Length >= length)
            return RuntimeValue.String(str);
        
        return RuntimeValue.String(str.PadRight(length, padChar));
    }
    
    private static RuntimeValue BuiltInRepeat(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("repeat() expects 2 arguments: (str, count)");
        if (args[0].Type != ValueType.String || !NumericCoercion.TryAsInteger(args[1], out var count))
            throw new Exception("repeat() expects (string, integer)");

        var str = args[0].AsString();
        
        if (count < 0)
            throw new Exception("repeat() count must be non-negative");
        if (count == 0)
            return RuntimeValue.String("");
        
        return RuntimeValue.String(string.Concat(Enumerable.Repeat(str, count)));
    }

    private static string NormalizeTextValue(string text, RuntimeValue options)
    {
        var lowercase = GetBooleanOption(options, "lowercase", true);
        var stripDiacritics = GetBooleanOption(options, "stripDiacritics", true);
        var collapseWhitespace = GetBooleanOption(options, "collapseWhitespace", true);
        var removePunctuation = GetBooleanOption(options, "removePunctuation", true);

        var normalized = text ?? string.Empty;
        if (lowercase)
        {
            normalized = normalized.ToLowerInvariant();
        }

        if (stripDiacritics)
        {
            normalized = RemoveDiacritics(normalized);
        }

        if (removePunctuation)
        {
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                builder.Append(char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ? ch : ' ');
            }
            normalized = builder.ToString();
        }

        if (collapseWhitespace)
        {
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        }

        return normalized;
    }

    private static List<string> TokenizeNormalizedText(string text)
    {
        return text
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static bool GetBooleanOption(RuntimeValue options, string key, bool fallback)
    {
        if (options.Type != ValueType.Object || options.AsObject() is not JsonObject jsonObject)
        {
            return fallback;
        }

        var value = jsonObject.Get(key, null);
        return value.Type == ValueType.Boolean ? value.AsBoolean() : fallback;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static double ComputeJaccardSimilarity(string left, string right, RuntimeValue options)
    {
        var leftSet = new HashSet<string>(TokenizeNormalizedText(NormalizeTextValue(left, options)), StringComparer.Ordinal);
        var rightSet = new HashSet<string>(TokenizeNormalizedText(NormalizeTextValue(right, options)), StringComparer.Ordinal);
        if (leftSet.Count == 0 && rightSet.Count == 0)
        {
            return 1.0;
        }

        var intersection = leftSet.Intersect(rightSet, StringComparer.Ordinal).Count();
        var union = leftSet.Union(rightSet, StringComparer.Ordinal).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static double ComputeContainsSimilarity(string left, string right, RuntimeValue options)
    {
        var normalizedLeft = NormalizeTextValue(left, options);
        var normalizedRight = NormalizeTextValue(right, options);
        if (normalizedLeft.Length == 0 && normalizedRight.Length == 0)
        {
            return 1.0;
        }

        if (normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) ||
            normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))
        {
            return 1.0;
        }

        return ComputeJaccardSimilarity(left, right, options);
    }

    private static double ComputeCharacterNGramSimilarity(string left, string right, RuntimeValue options)
    {
        var normalizedLeft = NormalizeTextValue(left, options);
        var normalizedRight = NormalizeTextValue(right, options);
        var leftNgrams = BuildCharacterNgrams(normalizedLeft, 3);
        var rightNgrams = BuildCharacterNgrams(normalizedRight, 3);
        if (leftNgrams.Count == 0 && rightNgrams.Count == 0)
        {
            return 1.0;
        }

        var shared = leftNgrams.Intersect(rightNgrams, StringComparer.Ordinal).Count();
        var total = leftNgrams.Count + rightNgrams.Count;
        return total == 0 ? 0.0 : (2.0 * shared) / total;
    }

    private static HashSet<string> BuildCharacterNgrams(string text, int size)
    {
        var compact = text.Replace(" ", "", StringComparison.Ordinal);
        var ngrams = new HashSet<string>(StringComparer.Ordinal);
        if (compact.Length == 0)
        {
            return ngrams;
        }

        if (compact.Length <= size)
        {
            ngrams.Add(compact);
            return ngrams;
        }

        for (var i = 0; i <= compact.Length - size; i++)
        {
            ngrams.Add(compact.Substring(i, size));
        }

        return ngrams;
    }
    
    private static async Task<RuntimeValue> BuiltInSleepAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        if (args.Count != 1)
            throw new Exception("sleep() expects 1 argument: (milliseconds)");
        
        if (!NumericCoercion.TryAsInteger(args[0], out var milliseconds))
            throw new Exception("sleep() milliseconds must be an integer");

        if (milliseconds < 0)
            throw new Exception("sleep() milliseconds must be non-negative");
        
        // Trigger output update before sleeping (similar to input)
        interpreter?.TriggerOutputUpdate();

        WithinBoundsContext.EnsureWithinBound();
        await Task.Delay(milliseconds);
        WithinBoundsContext.EnsureWithinBound();
        return RuntimeValue.Null();
    }
    
    private static async Task<RuntimeValue> BuiltInInputAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        BuiltInArity.Require("input", args, 0, 1, "prompt?");
        string prompt = "";
        if (args.Count > 0)
        {
            if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
                throw new Exception("input() prompt must be a string");
            prompt = args[0].AsString();
        }
        
        // If we have an input provider (web environment), use it
        var inputProvider = interpreter.GetInputProvider();
        
        if (inputProvider != null)
        {
            // Try to get input synchronously from queue first
            var hasQueued = inputProvider.HasQueuedInput();
            
            if (hasQueued)
            {
                var queuedInput = inputProvider.GetQueuedInput();
                return RuntimeValue.String(queuedInput);
            }
            // No queued input available - await it directly
            var input = await inputProvider.GetInputAsync(prompt);
            return RuntimeValue.String(input ?? "");
        }
        
        // Fallback for console environment
        Console.Write(prompt);
        var consoleInput = Console.ReadLine() ?? "";
        return RuntimeValue.String(consoleInput);
    }
    
    private static string? ResolveEnvironmentVariable(string varName)
    {
        // Try Process environment first (most specific)
        var value = System.Environment.GetEnvironmentVariable(varName);

        // If not found, try User environment (Windows-specific)
        if (value == null && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
            value = System.Environment.GetEnvironmentVariable(varName, System.EnvironmentVariableTarget.User);

        // If still not found, try Machine environment (Windows-specific)
        if (value == null && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
            value = System.Environment.GetEnvironmentVariable(varName, System.EnvironmentVariableTarget.Machine);

        return value;
    }

    /// <summary>Clears the process-lifetime <c>getEnv</c> cache so tests can change environment variables between runs.</summary>
    public static void ClearGetEnvCacheForTesting() => GetEnvCache.Clear();

    private static RuntimeValue BuiltInGetEnv(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("getEnv() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("getEnv() expects a string argument");

        var varName = args[0].AsString();
        var value = GetEnvCache.GetOrAdd(varName, static n => ResolveEnvironmentVariable(n));
        return value == null ? RuntimeValue.Null() : RuntimeValue.String(value);
    }

    private static RuntimeValue BuiltInGetHostPlatform(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("getHostPlatform() expects 0 arguments");

        var obj = new JsonObject();
        obj.Set("os", RuntimeValue.String(AgentPlatformContext.OsFamily));
        obj.Set("description", RuntimeValue.String(System.Runtime.InteropServices.RuntimeInformation.OSDescription));
        obj.Set("arch", RuntimeValue.String(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()));
        obj.Set("pathSeparator", RuntimeValue.String(Path.DirectorySeparatorChar.ToString()));
        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue BuiltInGetCommandLineArgs(List<RuntimeValue> args)
    {
        if (args.Count != 0) throw new Exception("getCommandLineArgs() expects 0 arguments");

        return RuntimeValue.Array(
            System.Environment.GetCommandLineArgs()
                .Skip(1)
                .Select(RuntimeValue.String)
                .ToList()
        );
    }
    
    private static RuntimeValue BuiltInHasEnv(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("hasEnv() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("hasEnv() expects a string argument");
        
        var varName = args[0].AsString();
        
        // Try Process environment first (most specific)
        var value = System.Environment.GetEnvironmentVariable(varName);
        
        // If not found, try User environment (Windows-specific)
        if (value == null && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            value = System.Environment.GetEnvironmentVariable(varName, EnvironmentVariableTarget.User);
        }
        
        // If still not found, try Machine environment (Windows-specific)
        if (value == null && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            value = System.Environment.GetEnvironmentVariable(varName, EnvironmentVariableTarget.Machine);
        }
        
        return RuntimeValue.Boolean(value != null);
    }
    
    private static RuntimeValue BuiltInParseJSON(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("parseJSON() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("parseJSON() expects a string argument");
        
        try
        {
            var jsonString = args[0].AsString();
            using var doc = JsonDocument.Parse(jsonString);
            return JsonToRuntimeValue(doc.RootElement);
        }
        catch (JsonException)
        {
            throw new Exception("Invalid JSON string");
        }
    }
    
    private static RuntimeValue JsonToRuntimeValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var jsonObj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    jsonObj.Set(prop.Name, JsonToRuntimeValue(prop.Value));
                }
                return RuntimeValue.Object(jsonObj);
            
            case JsonValueKind.Array:
                var arr = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(JsonToRuntimeValue(item));
                }
                return RuntimeValue.Array(arr);
            
            case JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return RuntimeValue.Integer(intVal);
                return RuntimeValue.Float(element.GetDouble());
            
            case JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            
            case JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            
            case JsonValueKind.Null:
                return RuntimeValue.Null();
            
            default:
                return RuntimeValue.Null();
        }
    }
    
    private static RuntimeValue BuiltInToJSON(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("toJSON() expects 1 argument");
        
        var json = RuntimeValueToJson(args[0]);
        return RuntimeValue.String(json);
    }

    /// <summary>
    /// Serializes a RuntimeValue to a JSON string. Used by the transpiler to embed prompt return-type schemas in generated code.
    /// </summary>
    public static string SerializeToJson(RuntimeValue value)
    {
        return RuntimeValueToJson(value);
    }

    private static string RuntimeValueToJson(RuntimeValue value)
    {
        switch (value.Type)
        {
            case MaldaLang.Interpreter.ValueType.String:
                return JsonSerializer.Serialize(value.AsString());
            
            case MaldaLang.Interpreter.ValueType.Integer:
                return value.AsInteger().ToString();
            
            case MaldaLang.Interpreter.ValueType.Float:
                {
                    var f = value.AsFloat();
                    // JSON has no NaN/Infinity; unquoted tokens break browser JSON.parse (e.g. SSE live handlers).
                    if (double.IsNaN(f) || double.IsInfinity(f))
                        return "null";
                    return f.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                }
            
            case MaldaLang.Interpreter.ValueType.Boolean:
                return value.AsBoolean().ToString().ToLower();
            
            case MaldaLang.Interpreter.ValueType.Null:
                return "null";
            
            case MaldaLang.Interpreter.ValueType.Array:
                var arr = value.AsArray();
                var items = arr.Select(RuntimeValueToJson);
                return "[" + string.Join(",", items) + "]";
            
            case MaldaLang.Interpreter.ValueType.Object:
                var obj = value.AsObject();
                if (obj is JsonObject jsonObj)
                {
                    var props = new List<string>();
                    foreach (var kvp in jsonObj.GetProperties())
                    {
                        var key = JsonSerializer.Serialize(kvp.Key);
                        var val = RuntimeValueToJson(kvp.Value);
                        props.Add($"{key}:{val}");
                    }
                    return "{" + string.Join(",", props) + "}";
                }
                if (obj is DictionaryInstance dictObj)
                {
                    var props = new List<string>();
                    foreach (var kvp in dictObj.GetEntries())
                    {
                        var key = JsonSerializer.Serialize(kvp.Key);
                        var val = RuntimeValueToJson(kvp.Value);
                        props.Add($"{key}:{val}");
                    }
                    return "{" + string.Join(",", props) + "}";
                }
                // For regular ObjectInstance, return empty object for now
                return "{}";
            
            default:
                return "\"<" + value.Type + ">\"";
        }
    }
    
    private static RuntimeValue BuiltInLoadNativeModule(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("loadNativeModule() expects exactly 1 string argument");

        var module = NativeModuleRegistry.LoadModule(args[0].AsString());
        return RuntimeValue.Object(new DotNetObjectInstance(module));
    }

    private static RuntimeValue BuiltInCreateNativeCallback(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count != 1 || args[0].Type != ValueType.Function)
            throw new Exception("createNativeCallback() expects exactly 1 function argument");
        if (interpreter == null)
            throw new Exception("createNativeCallback() is not available in this runtime context");

        return RuntimeValue.Object(new DotNetObjectInstance(new NativeCallbackBridge(args[0].AsFunction(), interpreter)));
    }

    private static RuntimeValue BuiltInReadFile(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3) throw new Exception("readFile() expects 1 to 3 arguments");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("readFile() expects (string, int?, int?)");
        
        try
        {
            var filePath = args[0].AsString();
            string content;
            if (EmbeddedFolderStore.IsEmbedPath(filePath))
            {
                var embedded = EmbeddedFolderStore.ReadText(filePath);
                if (embedded == null)
                    return RuntimeValue.Null();
                content = embedded;
            }
            else
            {
                if (!File.Exists(filePath))
                    return RuntimeValue.Null();
                content = File.ReadAllText(filePath);
            }
            
            // If line range is specified, extract only those lines
            if (args.Count >= 2)
            {
                var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                
                int startLine;
                int endLine;
                
                if (args.Count == 2)
                {
                    // Only one line argument provided
                    if (args[1].Type == MaldaLang.Interpreter.ValueType.Integer)
                    {
                        var lineArg = args[1].AsInteger();
                        if (lineArg < 0)
                        {
                            // Negative value means "read last N lines" (e.g., -30 = last 30 lines)
                            endLine = lines.Length;
                            startLine = Math.Max(1, lines.Length + lineArg + 1);
                        }
                        else
                        {
                            // Positive value means "read from this line to end"
                            startLine = Math.Max(1, lineArg);
                            endLine = lines.Length;
                        }
                    }
                    else
                    {
                        // Non-integer, default to reading entire file
                        startLine = 1;
                        endLine = lines.Length;
                    }
                }
                else
                {
                    // Two line arguments provided (startLine and endLine)
                    startLine = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : 1;
                    endLine = args[2].Type == MaldaLang.Interpreter.ValueType.Integer 
                        ? args[2].AsInteger() 
                        : -1; // -1 means to end of file
                    
                    if (startLine < 1)
                        startLine = 1;
                    
                    // Handle negative endLine values: negative values count from the end
                    // -1 means to end of file, -30 means 30 lines from the end
                    if (endLine < 0)
                    {
                        // Convert negative index to positive (e.g., -30 in 100-line file = line 71)
                        endLine = lines.Length + endLine + 1;
                        // If negative offset was too large, clamp to startLine (can't end before start)
                        if (endLine < startLine)
                            endLine = startLine;
                        // If still invalid (shouldn't happen, but safety check), default to end of file
                        if (endLine < 1)
                            endLine = lines.Length;
                    }
                    else if (endLine == 0 || endLine > lines.Length)
                    {
                        // Zero or beyond file length, default to end of file
                        endLine = lines.Length;
                    }
                }
                
                if (startLine > lines.Length)
                    return RuntimeValue.String(""); // Empty if start is beyond file
                
                // Extract lines (1-indexed, so subtract 1 for array index)
                var selectedLines = new List<string>();
                for (int i = startLine - 1; i < endLine && i < lines.Length; i++)
                {
                    selectedLines.Add(lines[i]);
                }
                
                return RuntimeValue.String(string.Join("\n", selectedLines));
            }
            
            return RuntimeValue.String(content);
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }

    /// <summary>
    /// Reads all lines from a text file as a MALDA array of strings (UTF-8). Faster than readFile()+split() for large files.
    /// </summary>
    private static RuntimeValue BuiltInReadTextFileLines(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("readTextFileLines() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("readTextFileLines() expects a string path");

        try
        {
            var filePath = args[0].AsString();
            if (!File.Exists(filePath))
                return RuntimeValue.Null();

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var list = new List<RuntimeValue>(lines.Length);
            foreach (var line in lines)
                list.Add(RuntimeValue.String(line));

            return RuntimeValue.Array(list);
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }
    
    private static string CoerceWriteFilePath(RuntimeValue value)
    {
        return value.Type == ValueType.String ? value.AsString() : value.ToString();
    }

    private static string CoerceWriteFileContent(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.String => value.AsString(),
            ValueType.Object or ValueType.Array => CallBuiltIn("toJSON", new List<RuntimeValue> { value }, null).AsString(),
            _ => value.ToString()
        };
    }

    private static RuntimeValue BuiltInWriteFile(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("writeFile() expects 2 arguments");
        var filePath = CoerceWriteFilePath(args[0]);
        RejectEmbedWrite(filePath, "writeFile");
        
        try
        {
            var content = CoerceWriteFileContent(args[1]);
            
            string? beforeContent = null;
            try
            {
                if (File.Exists(filePath))
                {
                    beforeContent = File.ReadAllText(filePath);
                }
            }
            catch
            {
                // Ignore read failures for tracing; file write may still succeed.
            }
            
            File.WriteAllText(filePath, content);
            
            // Trace file edit (best-effort)
            try
            {
                string? afterContent = null;
                try
                {
                    afterContent = File.ReadAllText(filePath);
                }
                catch
                {
                }
                
                TraceManager.Record(
                    TraceEventType.FileEdit,
                    new
                    {
                        path = filePath,
                        operation = "overwrite",
                        beforeContent,
                        afterContent,
                        toolName = "writeFile",
                        gitStatusBefore = (object?)null,
                        gitStatusAfter = (object?)null
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Boolean(true);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }
    
    private static RuntimeValue BuiltInHasFile(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("hasFile() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("hasFile() expects a string argument");
        
        try
        {
            var filePath = args[0].AsString();
            if (EmbeddedFolderStore.IsEmbedPath(filePath))
                return RuntimeValue.Boolean(EmbeddedFolderStore.HasFile(filePath));
            return RuntimeValue.Boolean(File.Exists(filePath));
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }

    private static RuntimeValue BuiltInDeleteFile(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("deleteFile() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("deleteFile() expects a string argument");
        try
        {
            var filePath = args[0].AsString();
            RejectEmbedWrite(filePath, "deleteFile");
            if (string.IsNullOrEmpty(filePath)) return RuntimeValue.Boolean(false);
            if (!File.Exists(filePath)) return RuntimeValue.Boolean(true);
            File.Delete(filePath);
            return RuntimeValue.Boolean(true);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }

    private static RuntimeValue BuiltInWriteFileBase64(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("writeFileBase64() expects 2 arguments");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("writeFileBase64() expects (path, base64Content)");
        try
        {
            var filePath = args[0].AsString();
            RejectEmbedWrite(filePath, "writeFileBase64");
            var base64 = args[1].AsString();
            var bytes = Convert.FromBase64String(base64 ?? "");
            File.WriteAllBytes(filePath, bytes);
            return RuntimeValue.Boolean(true);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }

    private static RuntimeValue BuiltInReadFileBase64(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("readFileBase64() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("readFileBase64() expects a string argument");
        try
        {
            var filePath = args[0].AsString();
            if (EmbeddedFolderStore.IsEmbedPath(filePath))
            {
                var embedded = EmbeddedFolderStore.ReadBytes(filePath);
                if (embedded == null) return RuntimeValue.Null();
                return RuntimeValue.String(Convert.ToBase64String(embedded));
            }
            if (!File.Exists(filePath)) return RuntimeValue.Null();
            var bytes = File.ReadAllBytes(filePath);
            return RuntimeValue.String(Convert.ToBase64String(bytes));
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }
    
    private static RuntimeValue BuiltInHasDirectory(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("hasDirectory() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("hasDirectory() expects a string argument");
        
        try
        {
            var dirPath = args[0].AsString();
            if (EmbeddedFolderStore.IsEmbedPath(dirPath))
                return RuntimeValue.Boolean(EmbeddedFolderStore.HasDirectory(dirPath));
            return RuntimeValue.Boolean(Directory.Exists(dirPath));
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }
    
    private static RuntimeValue BuiltInEnsureDir(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("ensureDir() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("ensureDir() expects a string argument");
        var dirPath = args[0].AsString();
        RejectEmbedWrite(dirPath, "ensureDir");
        if (string.IsNullOrEmpty(dirPath)) return RuntimeValue.Null();
        try
        {
            Directory.CreateDirectory(dirPath);
            return RuntimeValue.Null();
        }
        catch (Exception ex)
        {
            throw new Exception($"ensureDir() failed: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInListDirectory(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("listDirectory() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("listDirectory() expects a string argument");
        
        try
        {
            var dirPath = args[0].AsString();

            if (EmbeddedFolderStore.IsEmbedPath(dirPath))
            {
                var embedItems = new List<RuntimeValue>();
                foreach (var entry in EmbeddedFolderStore.List(dirPath))
                {
                    var itemObj = new JsonObject();
                    itemObj.Set("name", RuntimeValue.String(entry.Name));
                    itemObj.Set("type", RuntimeValue.String(entry.IsDirectory ? "directory" : "file"));
                    itemObj.Set("path", RuntimeValue.String(entry.Path));
                    embedItems.Add(RuntimeValue.Object(itemObj));
                }
                return RuntimeValue.Array(embedItems);
            }
            
            // Handle empty string - treat as current directory
            if (string.IsNullOrWhiteSpace(dirPath))
                dirPath = ".";
            
            // Resolve the directory path to an absolute path first
            // This handles relative paths like "." correctly
            var absoluteDirPath = Path.GetFullPath(dirPath);
            
            // Check if the resolved absolute path exists and is actually a directory
            if (!Directory.Exists(absoluteDirPath))
                return RuntimeValue.Array(new List<RuntimeValue>());
            
            var items = new List<RuntimeValue>();
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(absoluteDirPath))
                {
                    try
                    {
                        var name = Path.GetFileName(entry);
                        
                        // Skip if name is null (shouldn't happen, but be safe)
                        if (string.IsNullOrEmpty(name))
                            continue;
                        
                        // Ensure we always return an absolute path
                        // Directory.GetFileSystemEntries() should return full paths when given an absolute path,
                        // but we normalize it to ensure it's a proper absolute path
                        string fullPath;
                        if (Path.IsPathRooted(entry))
                        {
                            // Entry is already rooted (absolute), just normalize it
                            fullPath = Path.GetFullPath(entry);
                        }
                        else
                        {
                            // Entry is relative, combine with absolute directory path
                            fullPath = Path.GetFullPath(Path.Combine(absoluteDirPath, entry));
                        }
                        
                        // Check if it's a directory or file
                        var isDirectory = Directory.Exists(fullPath);
                        
                        var itemObj = new JsonObject();
                        itemObj.Set("name", RuntimeValue.String(name));
                        itemObj.Set("type", RuntimeValue.String(isDirectory ? "directory" : "file"));
                        itemObj.Set("path", RuntimeValue.String(fullPath));
                        items.Add(RuntimeValue.Object(itemObj));
                    }
                    catch
                    {
                        // Skip individual entries that cause errors (e.g., permission issues)
                        continue;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Directory exists but we don't have permission to read it
                throw new Exception($"listDirectory() failed: Access denied to directory '{absoluteDirPath}'");
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted between check and enumeration
                return RuntimeValue.Array(new List<RuntimeValue>());
            }
            
            return RuntimeValue.Array(items);
        }
        catch (Exception ex)
        {
            // Re-throw with more context for debugging
            throw new Exception($"listDirectory() failed: {ex.Message}", ex);
        }
    }

    private static RuntimeValue BuiltInReplaceInFile(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args.Count > 4) 
            throw new Exception("replaceInFile() expects 3 or 4 arguments");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String || args[1].Type != MaldaLang.Interpreter.ValueType.String || args[2].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("replaceInFile() expects (string, string, string, int?)");

        var filePath = args[0].AsString();
        RejectEmbedWrite(filePath, "replaceInFile");
        
        try
        {
            var oldText = args[1].AsString();
            var newText = args[2].AsString();
            var contextLines = args.Count == 4 && args[3].Type == MaldaLang.Interpreter.ValueType.Integer 
                ? args[3].AsInteger() 
                : 3;
            
            string? beforeContent = null;
            try
            {
                if (File.Exists(filePath))
                {
                    beforeContent = File.ReadAllText(filePath);
                }
            }
            catch
            {
            }
            
            var success = FileOperations.ReplaceInFile(filePath, oldText, newText, contextLines);
            
            // Trace file edit (best-effort)
            if (success)
            {
                try
                {
                    string? afterContent = null;
                    try
                    {
                        afterContent = File.ReadAllText(filePath);
                    }
                    catch
                    {
                    }
                    
                    TraceManager.Record(
                        TraceEventType.FileEdit,
                        new
                        {
                            path = filePath,
                            operation = "replace_range",
                            beforeContent,
                            afterContent,
                            toolName = "replaceInFile",
                            gitStatusBefore = (object?)null,
                            gitStatusAfter = (object?)null
                        });
                }
                catch
                {
                }
            }
            
            return RuntimeValue.Boolean(success);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }
    }
    
    private static RuntimeValue BuiltInEditFile(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("editFile() expects 2 arguments");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String || args[1].Type != MaldaLang.Interpreter.ValueType.Array)
            throw new Exception("editFile() expects (string, array)");
        
        try
        {
            var filePath = args[0].AsString();
            var edits = args[1].AsArray();
            
            var editList = new List<FileOperations.FileEdit>();
            foreach (var editValue in edits)
            {
                if (editValue.Type != MaldaLang.Interpreter.ValueType.Object)
                    continue;
                
                var editObj = editValue.AsObject();
                RuntimeValue? oldTextVal = null;
                RuntimeValue? newTextVal = null;
                RuntimeValue? contextLinesVal = null;
                
                try { oldTextVal = editObj.Get("oldText", null); } catch { }
                try { newTextVal = editObj.Get("newText", null); } catch { }
                try { contextLinesVal = editObj.Get("contextLines", null); } catch { }
                
                var oldText = oldTextVal?.AsString() ?? "";
                var newText = newTextVal?.AsString() ?? "";
                var contextLines = contextLinesVal?.AsInteger() ?? 3;
                
                editList.Add(new FileOperations.FileEdit
                {
                    OldText = oldText,
                    NewText = newText,
                    ContextLines = contextLines
                });
            }
            
            string? beforeContent = null;
            try
            {
                if (File.Exists(filePath))
                {
                    beforeContent = File.ReadAllText(filePath);
                }
            }
            catch
            {
            }
            
            var result = FileOperations.EditFile(filePath, editList);
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(result.Success));
            resultObj.Set("applied", RuntimeValue.Integer(result.Applied));
            resultObj.Set("totalEdits", RuntimeValue.Integer(result.TotalEdits));
            if (result.FailedEditIndex > 0)
                resultObj.Set("failedEdit", RuntimeValue.Integer(result.FailedEditIndex));
            if (!string.IsNullOrEmpty(result.Error))
                resultObj.Set("error", RuntimeValue.String(result.Error));
            
            // Trace file edit (best-effort)
            if (result.Success)
            {
                try
                {
                    string? afterContent = null;
                    try
                    {
                        afterContent = File.ReadAllText(filePath);
                    }
                    catch
                    {
                    }
                    
                    TraceManager.Record(
                        TraceEventType.FileEdit,
                        new
                        {
                            path = filePath,
                            operation = "edit",
                            beforeContent,
                            afterContent,
                            toolName = "editFile",
                            gitStatusBefore = (object?)null,
                            gitStatusAfter = (object?)null
                        });
                }
                catch
                {
                }
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch
        {
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(false));
            resultObj.Set("applied", RuntimeValue.Integer(0));
            return RuntimeValue.Object(resultObj);
        }
    }
    
    private static RuntimeValue BuiltInCreateReadFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createReadFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateReadFileTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateWriteFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createWriteFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateWriteFileTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateReplaceInFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createReplaceInFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateReplaceInFileTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateListDirectoryTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createListDirectoryTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateListDirectoryTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateAskUserTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createAskUserTool", args, 0, 0);
        // ask_user tool doesn't need a working directory parameter
        return BuiltInTools.CreateAskUserTool();
    }
    
    private static RuntimeValue BuiltInCreateWebSearchTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createWebSearchTool", args, 0, 0);
        return BuiltInTools.CreateWebSearchTool();
    }
    
    private static RuntimeValue BuiltInCreateGrepTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGrepTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateGrepTool(workingDir);
    }

    private static RuntimeValue BuiltInCreateGlobTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGlobTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateGlobTool(workingDir);
    }

    private static RuntimeValue BuiltInGlob(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("glob() expects at least 1 argument: (pattern, dirPath?, ...)");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("glob() expects (string pattern, ...)");

        var pattern = args[0].AsString();
        var dirPath = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.String
            ? args[1].AsString()
            : ".";
        var maxResults = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.Integer
            ? args[2].AsInteger()
            : GlobHelper.DefaultMaxResults;
        var includeDirectories = args.Count > 3 && args[3].Type == MaldaLang.Interpreter.ValueType.Boolean
            && args[3].AsBoolean();
        var excludeDirs = args.Count > 4 && args[4].Type == MaldaLang.Interpreter.ValueType.String
            ? args[4].AsString()
            : "";
        var workingDirectory = args.Count > 5 && args[5].Type == MaldaLang.Interpreter.ValueType.String
            ? args[5].AsString()
            : "";

        try
        {
            if (string.IsNullOrWhiteSpace(dirPath))
                dirPath = ".";

            string searchRoot;
            if (!string.IsNullOrEmpty(workingDirectory))
                searchRoot = Path.GetFullPath(Path.Combine(Path.GetFullPath(workingDirectory), dirPath));
            else
                searchRoot = Path.GetFullPath(dirPath);

            var matchResult = GlobHelper.Match(
                searchRoot,
                pattern,
                maxResults,
                includeDirectories,
                excludeDirs,
                string.IsNullOrEmpty(workingDirectory) ? null : Path.GetFullPath(workingDirectory));

            var items = new List<RuntimeValue>();
            foreach (var item in matchResult.Items)
            {
                var itemObj = new JsonObject();
                itemObj.Set("name", RuntimeValue.String(item.Name));
                itemObj.Set("type", RuntimeValue.String(item.Type));
                itemObj.Set("path", RuntimeValue.String(item.Path));
                items.Add(RuntimeValue.Object(itemObj));
            }

            var result = new JsonObject();
            result.Set("items", RuntimeValue.Array(items));
            result.Set("count", RuntimeValue.Integer(matchResult.Count));
            result.Set("truncated", RuntimeValue.Boolean(matchResult.Truncated));
            return RuntimeValue.Object(result);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error in glob: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGrep(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("grep() expects at least 2 arguments: (pattern, filePath, ...)");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String || args[1].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("grep() expects (string pattern, string filePath, ...)");
        
        var pattern = args[0].AsString();
        var filePath = args[1].AsString();
        
        // Extract optional parameters
        var useRegex = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[2].AsBoolean() : false;
        var caseInsensitive = args.Count > 3 && args[3].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[3].AsBoolean() : false;
        var includeLineNumbers = args.Count > 4 && args[4].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[4].AsBoolean() : true;
        var contextLines = args.Count > 5 && args[5].Type == MaldaLang.Interpreter.ValueType.Integer ? args[5].AsInteger() : 3;
        var countOnly = args.Count > 6 && args[6].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[6].AsBoolean() : false;
        var recursive = args.Count > 7 && args[7].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[7].AsBoolean() : true;
        var workingDirectory = args.Count > 8 && args[8].Type == MaldaLang.Interpreter.ValueType.String ? args[8].AsString() : "";
        
        try
        {
            var matches = new List<RuntimeValue>();
            var filesSearched = 0;
            var totalMatches = 0;

            // Prepare regex or string matching
            System.Text.RegularExpressions.Regex? regex = null;
            string? searchPattern = null;
            
            if (useRegex)
            {
                try
                {
                    var options = RegexOptions.None;
                    if (caseInsensitive)
                    {
                        options |= RegexOptions.IgnoreCase;
                    }
                    regex = new Regex(pattern, options);
                }
                catch (Exception ex)
                {
                    return RuntimeValue.String($"Error: Invalid regex pattern: {ex.Message}");
                }
            }
            else
            {
                searchPattern = caseInsensitive ? pattern.ToLowerInvariant() : pattern;
            }

            if (EmbeddedFolderStore.IsEmbedPath(filePath))
            {
                var embedFiles = EmbeddedFolderStore.EnumerateFiles(filePath, recursive);
                if (embedFiles.Count == 0 &&
                    !EmbeddedFolderStore.HasFile(filePath) &&
                    !EmbeddedFolderStore.HasDirectory(filePath))
                {
                    return RuntimeValue.String($"Error: Path '{filePath}' does not exist");
                }

                foreach (var file in embedFiles)
                {
                    filesSearched++;
                    var text = EmbeddedFolderStore.ReadText(file);
                    if (text == null)
                    {
                        continue;
                    }

                    var pathForResult = file;
                    if (!string.IsNullOrEmpty(workingDirectory) &&
                        EmbeddedFolderStore.IsEmbedPath(workingDirectory) &&
                        file.StartsWith(workingDirectory.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        pathForResult = file.Substring(workingDirectory.TrimEnd('/').Length + 1);
                    }

                    var fileMatches = SearchLines(
                        text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None),
                        pathForResult,
                        pattern,
                        regex,
                        searchPattern,
                        useRegex,
                        caseInsensitive,
                        includeLineNumbers,
                        contextLines);

                    if (countOnly)
                    {
                        totalMatches += fileMatches.Count;
                    }
                    else
                    {
                        matches.AddRange(fileMatches);
                    }
                }

                if (countOnly)
                {
                    var embedResult = new JsonObject();
                    embedResult.Set("count", RuntimeValue.Integer(totalMatches));
                    embedResult.Set("filesSearched", RuntimeValue.Integer(filesSearched));
                    return RuntimeValue.Object(embedResult);
                }

                return RuntimeValue.Array(matches);
            }
            
            // Determine if filePath is a file or directory
            var isDirectory = Directory.Exists(filePath);
            var isFile = File.Exists(filePath);
            
            if (!isFile && !isDirectory)
            {
                return RuntimeValue.String($"Error: Path '{filePath}' does not exist");
            }
            
            // Get list of files to search
            var filesToSearch = new List<string>();
            
            if (isFile)
            {
                filesToSearch.Add(filePath);
            }
            else if (isDirectory)
            {
                if (recursive)
                {
                    // Recursively get all files
                    try
                    {
                        filesToSearch.AddRange(Directory.GetFiles(filePath, "*", SearchOption.AllDirectories));
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error accessing directory: {ex.Message}");
                    }
                }
                else
                {
                    // Only files in the directory itself
                    try
                    {
                        filesToSearch.AddRange(Directory.GetFiles(filePath, "*", SearchOption.TopDirectoryOnly));
                    }
                    catch (Exception ex)
                    {
                        return RuntimeValue.String($"Error accessing directory: {ex.Message}");
                    }
                }
            }
            
            // Search each file
            foreach (var file in filesToSearch)
            {
                try
                {
                    filesSearched++;
                    var fileMatches = SearchFile(file, pattern, regex, searchPattern, useRegex, caseInsensitive, includeLineNumbers, contextLines, workingDirectory);
                    
                    if (countOnly)
                    {
                        totalMatches += fileMatches.Count;
                    }
                    else
                    {
                        matches.AddRange(fileMatches);
                    }
                }
                catch (Exception ex)
                when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    // Skip files we can't read (permissions, locked, etc.)
                    continue;
                }
            }
            
            if (countOnly)
            {
                var result = new JsonObject();
                result.Set("count", RuntimeValue.Integer(totalMatches));
                result.Set("filesSearched", RuntimeValue.Integer(filesSearched));
                return RuntimeValue.Object(result);
            }
            else
            {
                return RuntimeValue.Array(matches);
            }
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error in grep: {ex.Message}");
        }
    }
    
    private static List<RuntimeValue> SearchFile(
        string filePath,
        string originalPattern,
        Regex? regex,
        string? searchPattern,
        bool useRegex,
        bool caseInsensitive,
        bool includeLineNumbers,
        int contextLines,
        string workingDirectory = "")
    {
        try
        {
            var lines = File.ReadAllLines(filePath);
            var absolutePath = Path.GetFullPath(filePath);
            var pathForResult = absolutePath;
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                try
                {
                    var baseDir = Path.GetFullPath(workingDirectory);
                    pathForResult = Path.GetRelativePath(baseDir, absolutePath);
                }
                catch
                {
                    /* keep absolute path if GetRelativePath fails */
                }
            }

            return SearchLines(
                lines,
                pathForResult,
                originalPattern,
                regex,
                searchPattern,
                useRegex,
                caseInsensitive,
                includeLineNumbers,
                contextLines);
        }
        catch (Exception)
        {
            return new List<RuntimeValue>();
        }
    }

    private static List<RuntimeValue> SearchLines(
        string[] lines,
        string pathForResult,
        string originalPattern,
        Regex? regex,
        string? searchPattern,
        bool useRegex,
        bool caseInsensitive,
        bool includeLineNumbers,
        int contextLines)
    {
        var matches = new List<RuntimeValue>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1; // 1-indexed
            bool isMatch = false;

            if (useRegex && regex != null)
            {
                isMatch = regex.IsMatch(line);
            }
            else if (searchPattern != null)
            {
                var lineToSearch = caseInsensitive ? line.ToLowerInvariant() : line;
                isMatch = lineToSearch.Contains(searchPattern);
            }

            if (!isMatch)
            {
                continue;
            }

            var matchObj = new JsonObject();
            matchObj.Set("filePath", RuntimeValue.String(pathForResult));

            if (includeLineNumbers)
            {
                matchObj.Set("lineNumber", RuntimeValue.Integer(lineNumber));
            }

            matchObj.Set("content", RuntimeValue.String(line));

            if (contextLines > 0)
            {
                var contextBefore = new List<RuntimeValue>();
                var contextAfter = new List<RuntimeValue>();

                for (int j = Math.Max(0, i - contextLines); j < i; j++)
                {
                    contextBefore.Add(RuntimeValue.String(lines[j]));
                }

                for (int j = i + 1; j < Math.Min(lines.Length, i + 1 + contextLines); j++)
                {
                    contextAfter.Add(RuntimeValue.String(lines[j]));
                }

                if (contextBefore.Count > 0)
                {
                    matchObj.Set("contextBefore", RuntimeValue.Array(contextBefore));
                }

                if (contextAfter.Count > 0)
                {
                    matchObj.Set("contextAfter", RuntimeValue.Array(contextAfter));
                }
            }

            matches.Add(RuntimeValue.Object(matchObj));
        }

        return matches;
    }
    
    private static RuntimeValue BuiltInCreateInsertAtLineTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createInsertAtLineTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateInsertAtLineTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateEditFileTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createEditFileTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateEditFileTool(workingDir);
    }
    
    private static RuntimeValue BuiltInInsertAtLine(List<RuntimeValue> args)
    {
        if (args.Count < 3) throw new Exception("insertAtLine() expects at least 3 arguments: (filePath, lineNumber, content, ...)");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String || args[1].Type != MaldaLang.Interpreter.ValueType.Integer || args[2].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("insertAtLine() expects (string filePath, integer lineNumber, string content, ...)");
        
        var filePath = args[0].AsString();
        var lineNumber = args[1].AsInteger();
        var content = args[2].AsString();
        var insertAfter = args.Count > 3 && args[3].Type == MaldaLang.Interpreter.ValueType.Boolean ? args[3].AsBoolean() : false;
        
        try
        {
            if (!File.Exists(filePath))
            {
                return RuntimeValue.Boolean(false);
            }
            
            // Read file content to detect line endings
            var fileContent = File.ReadAllText(filePath);
            string lineEnding;
            
            // Detect line ending (CRLF or LF)
            if (fileContent.Contains("\r\n"))
            {
                lineEnding = "\r\n";
            }
            else if (fileContent.Contains("\n"))
            {
                lineEnding = "\n";
            }
            else
            {
                // No line breaks found, default to system line ending
                lineEnding = System.Environment.NewLine;
            }
            
            // Split into lines, preserving empty lines
            var lines = new List<string>();
            if (fileContent.Length > 0)
            {
                // Handle different line ending styles
                var normalizedContent = fileContent.Replace("\r\n", "\n").Replace("\r", "\n");
                var splitLines = normalizedContent.Split('\n');
                
                // If file ends with newline, preserve it
                bool endsWithNewline = fileContent.EndsWith("\r\n") || fileContent.EndsWith("\n");
                
                foreach (var line in splitLines)
                {
                    lines.Add(line);
                }
                
                // Remove last empty line if file didn't end with newline
                if (!endsWithNewline && lines.Count > 0 && lines[lines.Count - 1] == "")
                {
                    lines.RemoveAt(lines.Count - 1);
                }
            }
            
            // Handle edge cases for line number
            int insertIndex;
            if (lineNumber <= 0)
            {
                // Line 0 or negative: insert at start
                insertIndex = 0;
            }
            else if (lineNumber > lines.Count)
            {
                // Line beyond file length: append at end
                insertIndex = lines.Count;
            }
            else
            {
                // Normal case: convert 1-indexed to 0-indexed
                insertIndex = lineNumber - 1;
                
                // Apply insertAfter logic
                if (insertAfter)
                {
                    insertIndex = lineNumber; // Insert after the line (before line N+1)
                }
            }
            
            // Split content by newlines to handle multi-line content
            var contentToInsert = new List<string>();
            if (!string.IsNullOrEmpty(content))
            {
                var normalizedContent = content.Replace("\r\n", "\n").Replace("\r", "\n");
                var contentLines = normalizedContent.Split('\n');
                foreach (var line in contentLines)
                {
                    contentToInsert.Add(line);
                }
            }
            else
            {
                // Empty content - insert empty line
                contentToInsert.Add("");
            }
            
            // Insert all lines
            lines.InsertRange(insertIndex, contentToInsert);
            
            // Reconstruct file content with original line endings
            var newContent = string.Join(lineEnding, lines);
            
            // Write back to file
            File.WriteAllText(filePath, newContent);
            
            // Trace file edit (best-effort)
            try
            {
                string? afterContent = null;
                try
                {
                    afterContent = File.ReadAllText(filePath);
                }
                catch
                {
                }
                
                TraceManager.Record(
                    TraceEventType.FileEdit,
                    new
                    {
                        path = filePath,
                        operation = "insert",
                        beforeContent = fileContent,
                        afterContent,
                        toolName = "insertAtLine",
                        gitStatusBefore = (object?)null,
                        gitStatusAfter = (object?)null
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Boolean(true);
        }
        catch (Exception ex)
        {
            // Return false on any error
            return RuntimeValue.Boolean(false);
        }
    }
    
    // Git functions
    private static RuntimeValue BuiltInGitStatus(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitStatus", args, 0, 1, "repoPath?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        
        try
        {
            var result = ExecuteGitCommand(repoPath, "status", "--porcelain");
            if (result == null)
                return RuntimeValue.String("Error: Not a git repository or git command failed");
            
            // Parse porcelain output
            var statusObj = new JsonObject();
            var modified = new List<RuntimeValue>();
            var staged = new List<RuntimeValue>();
            var untracked = new List<RuntimeValue>();
            
            foreach (var line in result.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var status = line.Substring(0, 2);
                var file = line.Substring(3).Trim();
                
                if (status[0] == '?' || status[1] == '?')
                    untracked.Add(RuntimeValue.String(file));
                else if (status[0] != ' ' && status[0] != '?')
                    staged.Add(RuntimeValue.String(file));
                else if (status[1] != ' ' && status[1] != '?')
                    modified.Add(RuntimeValue.String(file));
            }
            
            statusObj.Set("modified", RuntimeValue.Array(modified));
            statusObj.Set("staged", RuntimeValue.Array(staged));
            statusObj.Set("untracked", RuntimeValue.Array(untracked));
            
            return RuntimeValue.Object(statusObj);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitAdd(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("gitAdd() expects 2 arguments: (repoPath, files)");
        var repoPath = Path.GetFullPath(args[0].AsString());
        var files = args[1].AsString();
        
        try
        {
            var (gitCwd, normalizedFiles) = NormalizeGitAddPaths(repoPath, files);
            var (success, _, error) = TryExecuteGitCommand(gitCwd, "add", normalizedFiles);
            if (!success)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? "" : ": " + error.Trim();
                return RuntimeValue.String($"Error: Failed to add files{detail}");
            }
            return RuntimeValue.String("Success: Files added to staging");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitCommit(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("gitCommit() expects 2 arguments: (repoPath, message)");
        var repoPath = args[0].AsString();
        var message = args[1].AsString();
        
        try
        {
            var result = ExecuteGitCommand(repoPath, "commit", "-m", message);
            if (result == null)
                return RuntimeValue.String("Error: Failed to create commit");
            return RuntimeValue.String("Success: Commit created");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitLog(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitLog", args, 0, 2, "repoPath?, count?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        var count = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.Integer 
            ? args[1].AsInteger() 
            : 10;
        
        try
        {
            var result = ExecuteGitCommand(repoPath, "log", $"-{count}", "--pretty=format:%H|%an|%ae|%ad|%s", "--date=iso");
            if (result == null)
                return RuntimeValue.String("Error: Failed to get git log");
            
            var commits = new List<RuntimeValue>();
            foreach (var line in result.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var parts = line.Split('|');
                if (parts.Length >= 5)
                {
                    var commitObj = new JsonObject();
                    commitObj.Set("hash", RuntimeValue.String(parts[0]));
                    commitObj.Set("author", RuntimeValue.String(parts[1]));
                    commitObj.Set("email", RuntimeValue.String(parts[2]));
                    commitObj.Set("date", RuntimeValue.String(parts[3]));
                    commitObj.Set("message", RuntimeValue.String(parts[4]));
                    commits.Add(RuntimeValue.Object(commitObj));
                }
            }
            
            return RuntimeValue.Array(commits);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitDiff(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitDiff", args, 0, 3, "repoPath?, filePath?, staged?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        var filePath = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[1].AsString() 
            : null;
        var staged = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.Boolean 
            ? args[2].AsBoolean() 
            : false;
        
        try
        {
            var gitArgs = new List<string> { staged ? "--cached" : "" };
            if (filePath != null)
                gitArgs.Add(filePath);
            
            var result = ExecuteGitCommand(repoPath, "diff", gitArgs.Where(a => !string.IsNullOrEmpty(a)).ToArray());
            if (result == null)
                return RuntimeValue.String("No changes");
            return RuntimeValue.String(result);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitBranch(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("gitBranch() expects at least 2 arguments: (repoPath, action, ...)");
        var repoPath = args[0].AsString();
        var action = args[1].AsString();
        
        try
        {
            if (action == "list")
            {
                var result = ExecuteGitCommand(repoPath, "branch", "-a");
                if (result == null)
                    return RuntimeValue.String("Error: Failed to list branches");
                
                var branches = new List<RuntimeValue>();
                foreach (var line in result.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var branchName = line.Trim().TrimStart('*', ' ');
                    branches.Add(RuntimeValue.String(branchName));
                }
                return RuntimeValue.Array(branches);
            }
            else if (action == "create")
            {
                if (args.Count < 3) throw new Exception("gitBranch() 'create' action requires branchName");
                var branchName = args[2].AsString();
                var result = ExecuteGitCommand(repoPath, "branch", branchName);
                if (result == null)
                    return RuntimeValue.String("Error: Failed to create branch");
                return RuntimeValue.String($"Success: Branch '{branchName}' created");
            }
            else
            {
                return RuntimeValue.String($"Error: Unknown action '{action}'. Use 'list' or 'create'");
            }
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitCheckout(List<RuntimeValue> args)
    {
        if (args.Count < 2) throw new Exception("gitCheckout() expects at least 2 arguments: (repoPath, branchName, ...)");
        var repoPath = args[0].AsString();
        var branchName = args[1].AsString();
        var create = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.Boolean 
            ? args[2].AsBoolean() 
            : false;
        
        try
        {
            var gitArgs = create ? new[] { "-b", branchName } : new[] { branchName };
            var result = ExecuteGitCommand(repoPath, "checkout", gitArgs);
            if (result == null)
                return RuntimeValue.String("Error: Failed to checkout branch");
            return RuntimeValue.String($"Success: Switched to branch '{branchName}'");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitPush(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitPush", args, 0, 3, "repoPath?, remote?, branch?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        var remote = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[1].AsString() 
            : "origin";
        var branch = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[2].AsString() 
            : null;
        
        try
        {
            var gitArgs = branch != null ? new[] { remote, branch } : new[] { remote };
            var result = ExecuteGitCommand(repoPath, "push", gitArgs);
            if (result == null)
                return RuntimeValue.String("Error: Failed to push");
            return RuntimeValue.String("Success: Pushed to remote");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInGitPull(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gitPull", args, 0, 3, "repoPath?, remote?, branch?");
        var repoPath = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : Directory.GetCurrentDirectory();
        var remote = args.Count > 1 && args[1].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[1].AsString() 
            : "origin";
        var branch = args.Count > 2 && args[2].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[2].AsString() 
            : null;
        
        try
        {
            var gitArgs = branch != null ? new[] { remote, branch } : new[] { remote };
            var result = ExecuteGitCommand(repoPath, "pull", gitArgs);
            if (result == null)
                return RuntimeValue.String("Error: Failed to pull");
            return RuntimeValue.String("Success: Pulled from remote");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error: {ex.Message}");
        }
    }
    
    private static string? ExecuteGitCommand(string repoPath, string command, params string[] args)
    {
        var (success, output, _) = TryExecuteGitCommand(repoPath, command, args);
        return success ? output : null;
    }

    private static (bool Success, string Output, string Error) TryExecuteGitCommand(string repoPath, string command, params string[] args)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"{command} {string.Join(" ", args.Select(a => $"\"{a}\""))}",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
                return (false, "", "Failed to start git process");
            
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode != 0)
                return (false, output, error);
            
            return (true, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static string? GetGitRoot(string startPath)
    {
        var (success, output, _) = TryExecuteGitCommand(startPath, "rev-parse", "--show-toplevel");
        return success ? output.Trim() : null;
    }

    private static (string GitCwd, string Files) NormalizeGitAddPaths(string repoPath, string files)
    {
        repoPath = Path.GetFullPath(repoPath);

        if (string.IsNullOrWhiteSpace(files) || files.Trim() == ".")
            return (repoPath, ".");

        var gitRoot = GetGitRoot(repoPath);
        gitRoot = gitRoot != null ? Path.GetFullPath(gitRoot) : null;

        var resolved = new List<string>();
        foreach (var part in files.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            resolved.Add(ResolveGitAddPathForRepoPath(repoPath, gitRoot, part));
        }

        if (resolved.Count > 0 && resolved.All(static p => p == "."))
            return (repoPath, ".");

        // Always run git add from repoPath with paths relative to that directory.
        // Running from git root with repo-root pathspecs breaks when repoPath is nested
        // (e.g. Ralph worktree project dir); git would look for doubled paths like
        // Examples/RalphWiggum/snake-demo/Examples/RalphWiggum/...
        return (repoPath, string.Join(" ", resolved));
    }

    private static string ResolveGitAddPathForRepoPath(string repoPath, string? gitRoot, string path)
    {
        path = path.Replace('\\', '/').Trim().Trim('"', '\'');

        if (path == "." || path == "")
            return ".";

        var directFull = Path.GetFullPath(Path.Combine(repoPath, path.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(directFull) || Directory.Exists(directFull))
            return ToGitForwardSlashPath(Path.GetRelativePath(repoPath, directFull));

        if (Path.IsPathRooted(path))
        {
            var absFull = Path.GetFullPath(path);
            if (File.Exists(absFull) || Directory.Exists(absFull))
                return ToGitForwardSlashPath(Path.GetRelativePath(repoPath, absFull));
        }

        if (gitRoot != null)
        {
            var fromGitRoot = Path.GetFullPath(Path.Combine(gitRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(fromGitRoot) || Directory.Exists(fromGitRoot))
                return ToGitForwardSlashPath(Path.GetRelativePath(repoPath, fromGitRoot));

            var repoRelFromRoot = ToGitForwardSlashPath(Path.GetRelativePath(gitRoot, repoPath));
            if (path == repoRelFromRoot || path == repoRelFromRoot + "/"
                || path.StartsWith(repoRelFromRoot + "/", StringComparison.Ordinal))
            {
                var suffix = path.Length > repoRelFromRoot.Length
                    ? path[(repoRelFromRoot.Length + 1)..]
                    : "";
                if (string.IsNullOrEmpty(suffix))
                    return ".";
                var suffixFull = Path.GetFullPath(Path.Combine(repoPath, suffix.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(suffixFull) || Directory.Exists(suffixFull))
                    return ToGitForwardSlashPath(Path.GetRelativePath(repoPath, suffixFull));
            }
        }

        var basename = Path.GetFileName(path.TrimEnd('/'));
        if (!string.IsNullOrEmpty(basename))
        {
            var inRepoPath = Path.Combine(repoPath, basename);
            if (File.Exists(inRepoPath) || Directory.Exists(inRepoPath))
                return ToGitForwardSlashPath(basename);
        }

        return path;
    }

    private static string ToGitForwardSlashPath(string path) => path.Replace('\\', '/');
    
    // Git tool creation functions
    private static RuntimeValue BuiltInCreateGitStatusTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitStatusTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitStatusTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitAddTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitAddTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitAddTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitCommitTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitCommitTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitCommitTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitLogTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitLogTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitLogTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitDiffTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitDiffTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitDiffTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitBranchTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitBranchTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitBranchTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitCheckoutTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitCheckoutTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitCheckoutTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitPushTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitPushTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitPushTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGitPullTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGitPullTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGitPullTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateRunCommandTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createRunCommandTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateRunCommandTool(workingDir);
    }
    
    private static Dictionary<string, string>? ExtractCommandEnvironment(RuntimeValue envValue)
    {
        if (envValue.Type != ValueType.Object)
            throw new Exception("runCommand() environment must be an object");

        if (envValue.AsObject() is not JsonObject jsonObject)
            throw new Exception("runCommand() environment must be a plain object");

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in jsonObject.GetProperties())
        {
            if (value.Type == ValueType.Null)
                continue;
            environment[key] = value.Type == ValueType.String ? value.AsString() : value.ToString() ?? "";
        }

        return environment.Count == 0 ? null : environment;
    }

    private static void ApplyCommandEnvironment(System.Diagnostics.ProcessStartInfo startInfo, Dictionary<string, string>? extraEnvironment)
    {
        if (extraEnvironment == null)
            return;

        foreach (var (key, value) in extraEnvironment)
            startInfo.Environment[key] = value;
    }

    // Command execution function
    private static RuntimeValue BuiltInRunCommand(List<RuntimeValue> args)
    {
        if (args.Count == 0 || args[0].Type != ValueType.String)
            throw new Exception("runCommand() expects at least 1 argument: command (string)");

        Dictionary<string, string>? extraEnvironment = null;
        if (args.Count > 1 && args[^1].Type == ValueType.Object)
        {
            extraEnvironment = ExtractCommandEnvironment(args[^1]);
            args = args.Take(args.Count - 1).ToList();
        }
        
        var command = args[0].AsString();
        if (string.IsNullOrWhiteSpace(command))
            throw new Exception("runCommand() command cannot be empty");
        
        // Safety check: blocked commands and shell wrappers
        var commandName = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        if (CommandApprovalPolicy.DeniedAlwaysCommands.Contains(commandName))
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String($"Error: Command '{commandName}' is not allowed for security reasons"));
            return RuntimeValue.Object(errorObj);
        }

        if (CommandApprovalPolicy.ShellWrapperCommands.Contains(commandName) && !CommandExecutionContext.IsUserApproved)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String(
                $"Error: Command '{commandName}' requires user approval. The run_command tool will prompt when an interactive UI is available."));
            return RuntimeValue.Object(errorObj);
        }
        
        // Safety check: Validate command path if it's an absolute path
        if (Path.IsPathRooted(command))
        {
            try
            {
                var normalizedCommandPath = Path.GetFullPath(command);
                
                // Check for path traversal attempts
                if (command.Contains("..") || command.Contains("~"))
                {
                    var errorObj = new JsonObject();
                    errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                    errorObj.Set("stdout", RuntimeValue.String(""));
                    errorObj.Set("stderr", RuntimeValue.String($"Error: Command path contains suspicious characters (path traversal attempt)"));
                    return RuntimeValue.Object(errorObj);
                }
                
                // Check if the file exists (for absolute paths)
                if (!File.Exists(normalizedCommandPath))
                {
                    var errorObj = new JsonObject();
                    errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                    errorObj.Set("stdout", RuntimeValue.String(""));
                    errorObj.Set("stderr", RuntimeValue.String($"Error: Command executable not found: '{normalizedCommandPath}'"));
                    return RuntimeValue.Object(errorObj);
                }
            }
            catch (Exception ex)
            {
                var errorObj = new JsonObject();
                errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                errorObj.Set("stdout", RuntimeValue.String(""));
                errorObj.Set("stderr", RuntimeValue.String($"Error: Invalid command path: {ex.Message}"));
                return RuntimeValue.Object(errorObj);
            }
        }
        
        // Parse optional arguments
        List<string>? commandArgs = null;
        string? workingDirectory = null;
        int? timeoutMs = null;
        var detached = false;
        
        if (args.Count > 1 && args[1].Type == ValueType.Array)
        {
            var argsArray = args[1].AsArray();
            commandArgs = new List<string>();
            foreach (var arg in argsArray)
            {
                if (arg.Type == ValueType.String)
                    commandArgs.Add(arg.AsString());
            }
        }
        
        if (args.Count > 2 && args[2].Type == ValueType.String)
        {
            workingDirectory = args[2].AsString();
        }
        
        if (args.Count > 3 && args[3].Type == ValueType.Integer)
        {
            timeoutMs = (int)args[3].AsInteger();
            if (timeoutMs <= 0)
                timeoutMs = null;
        }
        else if (args.Count > 3 && args[3].Type == ValueType.Boolean)
        {
            detached = args[3].AsBoolean();
        }

        if (args.Count > 4 && args[4].Type == ValueType.Boolean)
        {
            detached = args[4].AsBoolean();
        }

        timeoutMs = RunCommandShellHelper.ResolveTimeoutMs(command, timeoutMs);
        
        var shellError = RunCommandShellHelper.ValidateAndNormalize(command, commandArgs);
        if (shellError != null)
            return shellError;
        
        // Safety check: Maximum timeout limit (1 hour = 3600000ms)
        const int maxTimeoutMs = 3600000;
        if (timeoutMs.HasValue && timeoutMs.Value > maxTimeoutMs)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String($"Error: Timeout cannot exceed {maxTimeoutMs}ms (1 hour)"));
            return RuntimeValue.Object(errorObj);
        }

        if (detached && timeoutMs.HasValue)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String("Error: timeout is not supported in detached mode"));
            return RuntimeValue.Object(errorObj);
        }
        
        // Safety check: Validate and normalize working directory
        string finalWorkingDirectory;
        try
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                // Resolve to absolute path to prevent path traversal
                finalWorkingDirectory = Path.GetFullPath(workingDirectory);
                
                // Verify the directory exists
                if (!Directory.Exists(finalWorkingDirectory))
                {
                    var errorObj = new JsonObject();
                    errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                    errorObj.Set("stdout", RuntimeValue.String(""));
                    errorObj.Set("stderr", RuntimeValue.String($"Error: Working directory does not exist: '{finalWorkingDirectory}'"));
                    return RuntimeValue.Object(errorObj);
                }
            }
            else
            {
                finalWorkingDirectory = System.Environment.CurrentDirectory;
            }
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String($"Error: Invalid working directory: {ex.Message}"));
            return RuntimeValue.Object(errorObj);
        }
        
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var pseudoResult = RunCommandPseudo.TryExecute(command, commandArgs, finalWorkingDirectory);
            if (pseudoResult != null)
            {
                stopwatch.Stop();
                return pseudoResult;
            }
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                WorkingDirectory = finalWorkingDirectory,
                RedirectStandardOutput = !detached,
                RedirectStandardError = !detached,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (commandArgs != null)
            {
                foreach (var arg in commandArgs)
                    startInfo.ArgumentList.Add(arg);
            }

            ApplyCommandEnvironment(startInfo, extraEnvironment);
            
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                var errorObj = new JsonObject();
                errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                errorObj.Set("stdout", RuntimeValue.String(""));
                errorObj.Set("stderr", RuntimeValue.String($"Error: Failed to start process '{command}'"));
                
                // Trace failed process start
                try
                {
                    TraceManager.Record(
                        TraceEventType.RunCommand,
                        new
                        {
                            command,
                            arguments = commandArgs,
                            workingDirectory = finalWorkingDirectory,
                            exitCode = -1,
                            stdout = "",
                            stderr = $"Error: Failed to start process '{command}'",
                            durationMs = (int?)stopwatch.ElapsedMilliseconds
                        });
                }
                catch
                {
                    // Tracing must never interfere with normal execution
                }
                
                return RuntimeValue.Object(errorObj);
            }

            if (detached)
            {
                stopwatch.Stop();
                var detachedObj = new JsonObject();
                detachedObj.Set("exitCode", RuntimeValue.Integer(0));
                detachedObj.Set("stdout", RuntimeValue.String(""));
                detachedObj.Set("stderr", RuntimeValue.String(""));
                detachedObj.Set("detached", RuntimeValue.Boolean(true));
                detachedObj.Set("pid", RuntimeValue.Integer(process.Id));

                try
                {
                    TraceManager.Record(
                        TraceEventType.RunCommand,
                        new
                        {
                            command,
                            arguments = commandArgs,
                            workingDirectory = finalWorkingDirectory,
                            exitCode = 0,
                            stdout = "",
                            stderr = "",
                            durationMs = (int?)stopwatch.ElapsedMilliseconds,
                            detached = true,
                            pid = process.Id
                        });
                }
                catch
                {
                    // Tracing must never interfere with normal execution
                }

                return RuntimeValue.Object(detachedObj);
            }
            
            // Read output asynchronously to avoid deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            
            // Wait for process with optional timeout
            bool exited;
            if (timeoutMs.HasValue)
            {
                exited = process.WaitForExit(timeoutMs.Value);
                if (!exited)
                {
                    stopwatch.Stop();
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                    
                    var timeoutObj = new JsonObject();
                    timeoutObj.Set("exitCode", RuntimeValue.Integer(-1));
                    timeoutObj.Set("stdout", RuntimeValue.String(""));
                    timeoutObj.Set("stderr", RuntimeValue.String($"Error: Command timed out after {timeoutMs}ms"));
                    
                    // Trace timeout
                    try
                    {
                        TraceManager.Record(
                            TraceEventType.RunCommand,
                            new
                            {
                                command,
                                arguments = commandArgs,
                                workingDirectory = finalWorkingDirectory,
                                exitCode = -1,
                                stdout = "",
                                stderr = $"Error: Command timed out after {timeoutMs}ms",
                                durationMs = (int?)stopwatch.ElapsedMilliseconds
                            });
                    }
                    catch
                    {
                        // Tracing must never interfere with normal execution
                    }
                    
                    return RuntimeValue.Object(timeoutObj);
                }
            }
            else
            {
                process.WaitForExit();
                exited = true;
            }
            
            // Wait for output reading to complete
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            stopwatch.Stop();
            
            var resultObj = new JsonObject();
            resultObj.Set("exitCode", RuntimeValue.Integer(process.ExitCode));
            resultObj.Set("stdout", RuntimeValue.String(stdout ?? ""));
            resultObj.Set("stderr", RuntimeValue.String(stderr ?? ""));
            
            // Trace successful command execution
            try
            {
                TraceManager.Record(
                    TraceEventType.RunCommand,
                    new
                    {
                        command,
                        arguments = commandArgs,
                        workingDirectory = finalWorkingDirectory,
                        exitCode = process.ExitCode,
                        stdout = stdout ?? "",
                        stderr = stderr ?? "",
                        durationMs = (int?)stopwatch.ElapsedMilliseconds
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String($"Error executing command: {ex.Message}"));
            
            // Trace execution error
            try
            {
                TraceManager.Record(
                    TraceEventType.RunCommand,
                    new
                    {
                        command,
                        arguments = commandArgs,
                        workingDirectory = finalWorkingDirectory,
                        exitCode = -1,
                        stdout = "",
                        stderr = $"Error executing command: {ex.Message}",
                        durationMs = (int?)null
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Object(errorObj);
        }
    }
    
    private static RuntimeValue BuiltInRunMALDA(List<RuntimeValue> args)
    {
        if (args.Count == 0 || args[0].Type != ValueType.String)
            throw new Exception("runMALDA() expects at least 1 argument: sourceOrFilePath (string)");
        
        var sourceOrFilePath = args[0].AsString();
        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
            throw new Exception("runMALDA() sourceOrFilePath cannot be empty");
        
        // Extract optional input parameter
        string? input = null;
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            input = args[1].AsString();
        }
        
        // Detect if argument is a file path
        bool isFilePath = false;
        string source = sourceOrFilePath;
        
        // Check if it looks like a file path
        if (sourceOrFilePath.Contains(Path.DirectorySeparatorChar) || 
            sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
            sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
        {
            // Try to resolve as file path
            try
            {
                string filePath;
                if (Path.IsPathRooted(sourceOrFilePath))
                {
                    filePath = Path.GetFullPath(sourceOrFilePath);
                }
                else
                {
                    filePath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                }
                
                // Check for path traversal attempts
                if (sourceOrFilePath.Contains("..") || sourceOrFilePath.Contains("~"))
                {
                    var errorObj = new JsonObject();
                    errorObj.Set("success", RuntimeValue.Boolean(false));
                    errorObj.Set("output", RuntimeValue.String(""));
                    errorObj.Set("error", RuntimeValue.String($"Error: Path contains suspicious characters (path traversal attempt)"));
                    return RuntimeValue.Object(errorObj);
                }
                
                if (File.Exists(filePath))
                {
                    isFilePath = true;
                    source = File.ReadAllText(filePath);
                }
            }
            catch (Exception ex)
            {
                var errorObj = new JsonObject();
                errorObj.Set("success", RuntimeValue.Boolean(false));
                errorObj.Set("output", RuntimeValue.String(""));
                errorObj.Set("error", RuntimeValue.String($"Error reading file: {ex.Message}"));
                return RuntimeValue.Object(errorObj);
            }
        }
        
        // Capture output
        var output = new StringBuilder();
        TextWriter? originalOut = null;
        TextReader? originalIn = null;
        StringWriter? outputWriter = null;
        StringReader? inputReader = null;
        
        try
        {
            // Capture stdout
            originalOut = Console.Out;
            outputWriter = new StringWriter(output);
            Console.SetOut(outputWriter);
            
            // Set up stdin if input provided
            if (!string.IsNullOrEmpty(input))
            {
                originalIn = Console.In;
                inputReader = new StringReader(input);
                Console.SetIn(inputReader);
            }
            
            // Parse and execute
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            
            var parser = new Parser(tokens);
            var statements = parser.Parse();
            
            // Check for parse errors
            if (parser.Errors.Count > 0)
            {
                var firstError = parser.Errors[0];
                var errorObj = new JsonObject();
                errorObj.Set("success", RuntimeValue.Boolean(false));
                errorObj.Set("output", RuntimeValue.String(output.ToString()));
                errorObj.Set("error", RuntimeValue.String(firstError.Message));
                return RuntimeValue.Object(errorObj);
            }
            
            // Create interpreter and execute
            var interpreter = new Interpreter();
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
            
            // Success
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(true));
            resultObj.Set("output", RuntimeValue.String(output.ToString()));
            resultObj.Set("error", RuntimeValue.String(""));
            
            // Trace successful runMALDA execution
            try
            {
                TraceManager.Record(
                    TraceEventType.RunMalda,
                    new
                    {
                        sourceOrFilePath,
                        input,
                        success = true,
                        output = output.ToString(),
                        error = (string?)null,
                        runtimeError = (string?)null
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch (MaldaLang.Parser.ParseException ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("output", RuntimeValue.String(output.ToString()));
            errorObj.Set("error", RuntimeValue.String(ex.Message));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.RunMalda,
                    new
                    {
                        sourceOrFilePath,
                        input,
                        success = false,
                        output = output.ToString(),
                        error = ex.Message,
                        runtimeError = (string?)null
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
        catch (RuntimeException ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("output", RuntimeValue.String(output.ToString()));
            errorObj.Set("error", RuntimeValue.String(""));
            errorObj.Set("runtimeError", RuntimeValue.String(ex.Message));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.RunMalda,
                    new
                    {
                        sourceOrFilePath,
                        input,
                        success = false,
                        output = output.ToString(),
                        error = (string?)null,
                        runtimeError = ex.Message
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("output", RuntimeValue.String(output.ToString()));
            errorObj.Set("error", RuntimeValue.String(""));
            errorObj.Set("runtimeError", RuntimeValue.String($"Error: {ex.Message}"));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.RunMalda,
                    new
                    {
                        sourceOrFilePath,
                        input,
                        success = false,
                        output = output.ToString(),
                        error = (string?)null,
                        runtimeError = $"Error: {ex.Message}"
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
        finally
        {
            // Restore original streams
            if (originalOut != null)
                Console.SetOut(originalOut);
            if (originalIn != null)
                Console.SetIn(originalIn);
            outputWriter?.Dispose();
            inputReader?.Dispose();
        }
    }
    
    private static RuntimeValue BuiltInCreateRunMALDATool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createRunMALDATool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateRunMALDATool(workingDir);
    }
    
    private static RuntimeValue BuiltInCompileMALDA(List<RuntimeValue> args)
    {
        BuiltInArity.Require("compileMALDA", args, 1, 4, "sourcePath, outputPath?, mode?, embedFolder?");
        if (args[0].Type != ValueType.String)
            throw new Exception("compileMALDA() sourcePath must be a string");
        
        var sourcePath = args[0].AsString();
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new Exception("compileMALDA() sourcePath cannot be empty");
        
        // Extract optional outputPath parameter
        string? outputPath = null;
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            outputPath = args[1].AsString();
        }
        else if (args.Count > 1 && args[1].Type != ValueType.Null)
        {
            throw new Exception("compileMALDA() outputPath must be a string when provided");
        }
        
        // Extract optional mode parameter
        // Use reflection to avoid circular dependency with MaldaLang.Compiler
        int modeValue = 0; // 0 = Interpreter, 1 = TranspileToCSharp
        if (args.Count > 2 && args[2].Type == ValueType.String)
        {
            var modeStr = args[2].AsString().ToLower();
            if (modeStr == "transpile" || modeStr == "transpiletocsharp")
            {
                modeValue = 1; // TranspileToCSharp
            }
            else if (modeStr != "interpreter")
            {
                throw new Exception($"compileMALDA() mode must be 'interpreter' or 'transpile', got '{modeStr}'");
            }
        }
        else if (args.Count > 2 && args[2].Type != ValueType.Null)
        {
            throw new Exception("compileMALDA() mode must be a string when provided");
        }

        // Optional embed folder: "path" or "path=alias" (same as CLI --embed-folder)
        string[]? embedFolderArgs = null;
        if (args.Count > 3)
        {
            if (args[3].Type != ValueType.String)
                throw new Exception("compileMALDA() embedFolder must be a string (path or path=alias)");
            var embedFolder = args[3].AsString();
            if (!string.IsNullOrWhiteSpace(embedFolder))
                embedFolderArgs = new[] { embedFolder };
        }
        
        // Resolve file path
        string filePath;
        if (Path.IsPathRooted(sourcePath))
        {
            filePath = Path.GetFullPath(sourcePath);
        }
        else
        {
            filePath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourcePath));
        }
        
        // Check for path traversal attempts
        if (sourcePath.Contains("..") || sourcePath.Contains("~"))
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.Null());
            errorObj.Set("error", RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)"));
            var errorsArray = new List<RuntimeValue>();
            errorObj.Set("errors", RuntimeValue.Array(errorsArray));
            return RuntimeValue.Object(errorObj);
        }
        
        // Check if file exists
        if (!File.Exists(filePath))
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.Null());
            errorObj.Set("error", RuntimeValue.String($"Error: Source file not found: {filePath}"));
            var errorsArray = new List<RuntimeValue>();
            errorObj.Set("errors", RuntimeValue.Array(errorsArray));
            return RuntimeValue.Object(errorObj);
        }
        
        // Determine output path
        string finalOutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            finalOutputPath = Path.ChangeExtension(filePath, ".exe");
        }
        else
        {
            if (Path.IsPathRooted(outputPath))
            {
                finalOutputPath = Path.GetFullPath(outputPath);
            }
            else
            {
                finalOutputPath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, outputPath));
            }
        }
        
        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(finalOutputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }
        
        try
        {
            // Call the compiler using reflection to avoid circular dependency
            var compilerAssembly = System.Reflection.Assembly.LoadFrom(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MaldaLang.Compiler.dll"));
            var compilerType = compilerAssembly.GetType("MaldaLang.Compiler.Compiler");
            var compilationModeType = compilerAssembly.GetType("MaldaLang.Compiler.CompilationMode");
            
            if (compilerType == null || compilationModeType == null)
            {
                throw new Exception("Compiler assembly not found. Please ensure MaldaLang.Compiler.dll is available.");
            }
            
            var compiler = Activator.CreateInstance(compilerType);
            var modeEnum = Enum.ToObject(compilationModeType, modeValue);

            // Prefer the overload that accepts --embed-folder args so portable ASK builds
            // can pack a directory without shelling out to the CLI.
            var compileMethod = compilerType.GetMethods()
                .FirstOrDefault(m =>
                {
                    if (m.Name != "Compile")
                        return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 9
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(string)
                        && parameters[2].ParameterType == compilationModeType
                        && parameters[8].ParameterType == typeof(string[]);
                });

            if (compileMethod == null)
            {
                compileMethod = compilerType.GetMethod("Compile", new[]
                {
                    typeof(string),
                    typeof(string),
                    compilationModeType,
                    typeof(bool)
                });
            }

            if (compileMethod == null)
            {
                throw new Exception("Compiler.Compile method not found.");
            }

            object? result;
            if (compileMethod.GetParameters().Length == 9)
            {
                result = compileMethod.Invoke(compiler, new object?[]
                {
                    filePath,
                    finalOutputPath,
                    modeEnum,
                    false, // includeLLamaSharp
                    false, // includeUiHost
                    null,  // profilingOptions
                    1,     // typedTranspileLevel
                    false, // includeOptionalPacks
                    embedFolderArgs
                });
            }
            else
            {
                if (embedFolderArgs != null && embedFolderArgs.Length > 0)
                {
                    throw new Exception("compileMALDA() embedFolder requires a compiler that supports --embed-folder.");
                }
                result = compileMethod.Invoke(compiler, new object[] { filePath, finalOutputPath, modeEnum, false });
            }
            
            // Access result properties using reflection
            var resultType = result?.GetType();
            var successProp = resultType?.GetProperty("Success");
            var outputPathProp = resultType?.GetProperty("OutputPath");
            var errorMessageProp = resultType?.GetProperty("ErrorMessage");
            
            var success = (bool)(successProp?.GetValue(result) ?? false);
            var outputPathValue = outputPathProp?.GetValue(result) as string;
            var errorMessage = errorMessageProp?.GetValue(result) as string;
            
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(success));
            resultObj.Set("outputPath", success && outputPathValue != null 
                ? RuntimeValue.String(outputPathValue) 
                : RuntimeValue.Null());
            
            // Build errors array
            var errorsArray = new List<RuntimeValue>();
            if (!success && !string.IsNullOrEmpty(errorMessage))
            {
                // Try to parse errors from the compiler
                // The compiler may return parse errors or compilation errors
                // We'll create a structured error entry
                var errorEntry = new JsonObject();
                errorEntry.Set("message", RuntimeValue.String(errorMessage));
                errorEntry.Set("line", RuntimeValue.Integer(0));
                errorEntry.Set("column", RuntimeValue.Integer(0));
                errorsArray.Add(RuntimeValue.Object(errorEntry));
                
                resultObj.Set("error", RuntimeValue.String(errorMessage));
            }
            else
            {
                resultObj.Set("error", RuntimeValue.String(""));
            }
            
            resultObj.Set("errors", RuntimeValue.Array(errorsArray));
            
            // Trace compileMALDA result
            try
            {
                TraceManager.Record(
                    TraceEventType.CompileMalda,
                    new
                    {
                        sourcePath,
                        outputPath = outputPathValue,
                        mode = modeValue == 1 ? "transpile" : "interpreter",
                        success,
                        error = success ? "" : errorMessage,
                        errors = errorsArray.Select<RuntimeValue, object>(rv =>
                        {
                            if (rv.Type == ValueType.Object && rv.AsObject() is JsonObject obj)
                            {
                                var msgVal = obj.Get("message");
                                var lineVal = obj.Get("line");
                                var columnVal = obj.Get("column");

                                var msg = msgVal.Type == ValueType.String ? msgVal.AsString() : "";
                                var line = lineVal.Type == ValueType.Integer ? lineVal.AsInteger() : 0;
                                var column = columnVal.Type == ValueType.Integer ? columnVal.AsInteger() : 0;

                                return new
                                {
                                    message = msg,
                                    file = (string?)null,
                                    line,
                                    column
                                };
                            }
                            return new
                            {
                                message = (string?)null,
                                file = (string?)null,
                                line = (long)0,
                                column = (long)0
                            } as object;
                        }).ToList()
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch (ParseException ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.Null());
            errorObj.Set("error", RuntimeValue.String($"Syntax error: {ex.Message}"));
            
            var errorsArray = new List<RuntimeValue>();
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String(ex.Message));
            errorEntry.Set("line", RuntimeValue.Integer(ex.Line));
            errorEntry.Set("column", RuntimeValue.Integer(ex.Column));
            errorsArray.Add(RuntimeValue.Object(errorEntry));
            errorObj.Set("errors", RuntimeValue.Array(errorsArray));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.CompileMalda,
                    new
                    {
                        sourcePath,
                        outputPath,
                        mode = modeValue == 1 ? "transpile" : "interpreter",
                        success = false,
                        error = $"Syntax error: {ex.Message}",
                        errors = new[]
                        {
                            new
                            {
                                message = ex.Message,
                                file = (string?)null,
                                line = (long)ex.Line,
                                column = (long)ex.Column
                            }
                        }
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.Null());
            errorObj.Set("error", RuntimeValue.String($"Compilation error: {ex.Message}"));
            
            var errorsArray = new List<RuntimeValue>();
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String(ex.Message));
            errorEntry.Set("line", RuntimeValue.Integer(0));
            errorEntry.Set("column", RuntimeValue.Integer(0));
            errorsArray.Add(RuntimeValue.Object(errorEntry));
            errorObj.Set("errors", RuntimeValue.Array(errorsArray));
            
            try
            {
                TraceManager.Record(
                    TraceEventType.CompileMalda,
                    new
                    {
                        sourcePath,
                        outputPath,
                        mode = modeValue == 1 ? "transpile" : "interpreter",
                        success = false,
                        error = $"Compilation error: {ex.Message}",
                        errors = new[]
                        {
                            new
                            {
                                message = ex.Message,
                                file = (string?)null,
                                line = (long)0,
                                column = (long)0
                            }
                        }
                    });
            }
            catch
            {
            }
            
            return RuntimeValue.Object(errorObj);
        }
    }
    
    private static string BuildFunctionSignature(string name, List<string> parameters, List<string?>? parameterTypeHints, string? returnType)
    {
        var paramParts = new List<string>();
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (parameterTypeHints != null && i < parameterTypeHints.Count && parameterTypeHints[i] != null)
                paramParts.Add($"{p}: {parameterTypeHints[i]}");
            else
                paramParts.Add(p);
        }
        var sig = $"function {name}({string.Join(", ", paramParts)})";
        if (returnType != null)
            sig += $" -> {returnType}";
        return sig;
    }

    private static RuntimeValue BuiltInGetSymbols(List<RuntimeValue> args)
    {
        if (args.Count == 0 || args[0].Type != ValueType.String)
            throw new Exception("getSymbols() expects 1 argument: sourceOrFilePath (string)");
        
        var sourceOrFilePath = args[0].AsString();
        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
            throw new Exception("getSymbols() sourceOrFilePath cannot be empty");
        
        string source = sourceOrFilePath;
        string? sourceFileName = null;
        
        // Detect if argument is a file path (mirror BuiltInRunMALDA)
        if (sourceOrFilePath.Contains(Path.DirectorySeparatorChar) ||
            sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
            sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string filePath = Path.IsPathRooted(sourceOrFilePath)
                    ? Path.GetFullPath(sourceOrFilePath)
                    : Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                
                if (sourceOrFilePath.Contains("..") || sourceOrFilePath.Contains("~"))
                {
                    var errorResultObj = new JsonObject();
                    errorResultObj.Set("classes", RuntimeValue.Array(new List<RuntimeValue>()));
                    errorResultObj.Set("functions", RuntimeValue.Array(new List<RuntimeValue>()));
                    errorResultObj.Set("actors", RuntimeValue.Array(new List<RuntimeValue>()));
                    errorResultObj.Set("prompts", RuntimeValue.Array(new List<RuntimeValue>()));
                    errorResultObj.Set("imports", RuntimeValue.Array(new List<RuntimeValue>()));
                    var errList = new List<RuntimeValue>();
                    var errEntry = new JsonObject();
                    errEntry.Set("message", RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)"));
                    errEntry.Set("line", RuntimeValue.Integer(0));
                    errEntry.Set("column", RuntimeValue.Integer(0));
                    errList.Add(RuntimeValue.Object(errEntry));
                    errorResultObj.Set("parseErrors", RuntimeValue.Array(errList));
                    return RuntimeValue.Object(errorResultObj);
                }
                
                if (File.Exists(filePath))
                {
                    source = File.ReadAllText(filePath);
                    sourceFileName = filePath;
                }
            }
            catch (Exception ex)
            {
                var errorResultObj = new JsonObject();
                errorResultObj.Set("classes", RuntimeValue.Array(new List<RuntimeValue>()));
                errorResultObj.Set("functions", RuntimeValue.Array(new List<RuntimeValue>()));
                errorResultObj.Set("actors", RuntimeValue.Array(new List<RuntimeValue>()));
                errorResultObj.Set("prompts", RuntimeValue.Array(new List<RuntimeValue>()));
                errorResultObj.Set("imports", RuntimeValue.Array(new List<RuntimeValue>()));
                var errList = new List<RuntimeValue>();
                var errEntry = new JsonObject();
                errEntry.Set("message", RuntimeValue.String($"Error reading file: {ex.Message}"));
                errEntry.Set("line", RuntimeValue.Integer(0));
                errEntry.Set("column", RuntimeValue.Integer(0));
                errList.Add(RuntimeValue.Object(errEntry));
                errorResultObj.Set("parseErrors", RuntimeValue.Array(errList));
                return RuntimeValue.Object(errorResultObj);
            }
        }
        
        var classes = new List<RuntimeValue>();
        var functions = new List<RuntimeValue>();
        var actors = new List<RuntimeValue>();
        var prompts = new List<RuntimeValue>();
        var parseErrors = new List<RuntimeValue>();
        var importsList = new List<RuntimeValue>();
        
        try
        {
            var lexer = new Lexer(source, sourceFileName);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
            var statements = parser.Parse();
            
            // Collect parse errors
            foreach (var error in parser.Errors)
            {
                var errorEntry = new JsonObject();
                errorEntry.Set("message", RuntimeValue.String(error.Message));
                errorEntry.Set("line", RuntimeValue.Integer(error.Line));
                errorEntry.Set("column", RuntimeValue.Integer(error.Column));
                parseErrors.Add(RuntimeValue.Object(errorEntry));
            }
            
            // Extract symbols from AST
            foreach (var stmt in statements)
            {
                if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl)
                {
                    var classObj = new JsonObject();
                    classObj.Set("name", RuntimeValue.String(classDecl.Name));
                    classObj.Set("superclass", classDecl.Superclass != null 
                        ? RuntimeValue.String(classDecl.Superclass) 
                        : RuntimeValue.Null());
                    classObj.Set("line", RuntimeValue.Integer(classDecl.Line));
                    classObj.Set("column", RuntimeValue.Integer(classDecl.Column));
                    
                    // Extract members
                    var members = new List<RuntimeValue>();
                    foreach (var member in classDecl.Members)
                    {
                        var memberObj = new JsonObject();
                        memberObj.Set("name", RuntimeValue.String(member.Name));
                        memberObj.Set("type", RuntimeValue.String(member.Type.ToString().ToLower()));
                        memberObj.Set("access", RuntimeValue.String(member.Access.ToString().ToLower()));
                        memberObj.Set("isStatic", RuntimeValue.Boolean(member.IsStatic));
                        
                        // Get line and column from the member
                        // For methods/constructors, get from FunctionDeclaration
                        int memberLine = classDecl.Line;
                        int memberColumn = classDecl.Column;
                        string signature = "";
                        
                        if (member.Type == MemberType.Method || member.Type == MemberType.Constructor)
                        {
                            if (member.Value is MaldaLang.Parser.AST.Declarations.FunctionDeclaration memberFuncDecl)
                            {
                                memberLine = memberFuncDecl.Line;
                                memberColumn = memberFuncDecl.Column;
                                signature = BuildFunctionSignature(member.Name, memberFuncDecl.Parameters, memberFuncDecl.ParameterTypeHints, memberFuncDecl.ReturnType);
                                var paramTypesList = new List<RuntimeValue>();
                                for (int i = 0; i < memberFuncDecl.Parameters.Count; i++)
                                {
                                    string? hint = (memberFuncDecl.ParameterTypeHints != null && i < memberFuncDecl.ParameterTypeHints.Count)
                                        ? memberFuncDecl.ParameterTypeHints[i]
                                        : null;
                                    paramTypesList.Add(hint != null ? RuntimeValue.String(hint) : RuntimeValue.Null());
                                }
                                memberObj.Set("parameterTypes", RuntimeValue.Array(paramTypesList));
                                memberObj.Set("returnType", memberFuncDecl.ReturnType != null ? RuntimeValue.String(memberFuncDecl.ReturnType) : RuntimeValue.Null());
                            }
                        }
                        
                        memberObj.Set("line", RuntimeValue.Integer(memberLine));
                        memberObj.Set("column", RuntimeValue.Integer(memberColumn));
                        memberObj.Set("signature", RuntimeValue.String(signature));
                        members.Add(RuntimeValue.Object(memberObj));
                    }
                    
                    classObj.Set("members", RuntimeValue.Array(members));
                    classes.Add(RuntimeValue.Object(classObj));
                }
                else if (stmt is MaldaLang.Parser.AST.Declarations.FunctionDeclaration funcDecl)
                {
                    var funcObj = new JsonObject();
                    funcObj.Set("name", RuntimeValue.String(funcDecl.Name));
                    funcObj.Set("line", RuntimeValue.Integer(funcDecl.Line));
                    funcObj.Set("column", RuntimeValue.Integer(funcDecl.Column));
                    
                    var paramsArray = new List<RuntimeValue>();
                    foreach (var param in funcDecl.Parameters)
                    {
                        paramsArray.Add(RuntimeValue.String(param));
                    }
                    funcObj.Set("parameters", RuntimeValue.Array(paramsArray));
                    
                    var paramTypesArray = new List<RuntimeValue>();
                    for (int i = 0; i < funcDecl.Parameters.Count; i++)
                    {
                        string? hint = (funcDecl.ParameterTypeHints != null && i < funcDecl.ParameterTypeHints.Count)
                            ? funcDecl.ParameterTypeHints[i]
                            : null;
                        paramTypesArray.Add(hint != null ? RuntimeValue.String(hint) : RuntimeValue.Null());
                    }
                    funcObj.Set("parameterTypes", RuntimeValue.Array(paramTypesArray));
                    funcObj.Set("returnType", funcDecl.ReturnType != null ? RuntimeValue.String(funcDecl.ReturnType) : RuntimeValue.Null());
                    
                    var sig = BuildFunctionSignature(funcDecl.Name, funcDecl.Parameters, funcDecl.ParameterTypeHints, funcDecl.ReturnType);
                    funcObj.Set("signature", RuntimeValue.String(sig));
                    functions.Add(RuntimeValue.Object(funcObj));
                }
                else if (stmt is MaldaLang.Parser.AST.Declarations.ActorDeclaration actorDecl)
                {
                    var actorObj = new JsonObject();
                    actorObj.Set("name", RuntimeValue.String(actorDecl.Name));
                    actorObj.Set("line", RuntimeValue.Integer(actorDecl.Line));
                    actorObj.Set("column", RuntimeValue.Integer(actorDecl.Column));
                    
                    // Extract members (same structure as class members)
                    var members = new List<RuntimeValue>();
                    foreach (var member in actorDecl.Members)
                    {
                        var memberObj = new JsonObject();
                        memberObj.Set("name", RuntimeValue.String(member.Name));
                        memberObj.Set("type", RuntimeValue.String(member.Type.ToString().ToLower()));
                        memberObj.Set("access", RuntimeValue.String(member.Access.ToString().ToLower()));
                        memberObj.Set("isStatic", RuntimeValue.Boolean(member.IsStatic));
                        
                        // Get line and column from the member
                        int memberLine = actorDecl.Line;
                        int memberColumn = actorDecl.Column;
                        string signature = "";
                        
                        if (member.Type == MemberType.Method || member.Type == MemberType.Constructor)
                        {
                            if (member.Value is MaldaLang.Parser.AST.Declarations.FunctionDeclaration actorMemberFuncDecl)
                            {
                                memberLine = actorMemberFuncDecl.Line;
                                memberColumn = actorMemberFuncDecl.Column;
                                signature = BuildFunctionSignature(member.Name, actorMemberFuncDecl.Parameters, actorMemberFuncDecl.ParameterTypeHints, actorMemberFuncDecl.ReturnType);
                                var paramTypesList = new List<RuntimeValue>();
                                for (int i = 0; i < actorMemberFuncDecl.Parameters.Count; i++)
                                {
                                    string? hint = (actorMemberFuncDecl.ParameterTypeHints != null && i < actorMemberFuncDecl.ParameterTypeHints.Count)
                                        ? actorMemberFuncDecl.ParameterTypeHints[i]
                                        : null;
                                    paramTypesList.Add(hint != null ? RuntimeValue.String(hint) : RuntimeValue.Null());
                                }
                                memberObj.Set("parameterTypes", RuntimeValue.Array(paramTypesList));
                                memberObj.Set("returnType", actorMemberFuncDecl.ReturnType != null ? RuntimeValue.String(actorMemberFuncDecl.ReturnType) : RuntimeValue.Null());
                            }
                        }
                        
                        memberObj.Set("line", RuntimeValue.Integer(memberLine));
                        memberObj.Set("column", RuntimeValue.Integer(memberColumn));
                        memberObj.Set("signature", RuntimeValue.String(signature));
                        members.Add(RuntimeValue.Object(memberObj));
                    }
                    
                    actorObj.Set("members", RuntimeValue.Array(members));
                    actors.Add(RuntimeValue.Object(actorObj));
                }
                else if (stmt is MaldaLang.Parser.AST.Declarations.ChainDeclaration chainDecl)
                {
                    var funcObj = new JsonObject();
                    funcObj.Set("name", RuntimeValue.String(chainDecl.Name));
                    funcObj.Set("line", RuntimeValue.Integer(chainDecl.Line));
                    funcObj.Set("column", RuntimeValue.Integer(chainDecl.Column));

                    var paramsArray = new List<RuntimeValue>();
                    foreach (var param in chainDecl.Parameters)
                        paramsArray.Add(RuntimeValue.String(param));
                    funcObj.Set("parameters", RuntimeValue.Array(paramsArray));
                    funcObj.Set("returnType", chainDecl.ReturnType != null ? RuntimeValue.String(chainDecl.ReturnType) : RuntimeValue.Null());

                    var sig = $"chain {chainDecl.Name}({string.Join(", ", chainDecl.Parameters)})";
                    if (chainDecl.ReturnType != null)
                        sig += $" -> {chainDecl.ReturnType}";
                    funcObj.Set("signature", RuntimeValue.String(sig));
                    functions.Add(RuntimeValue.Object(funcObj));
                }
                else if (stmt is MaldaLang.Parser.AST.Declarations.PromptDeclaration promptDecl)
                {
                    var promptObj = new JsonObject();
                    promptObj.Set("name", RuntimeValue.String(promptDecl.Name));
                    promptObj.Set("line", RuntimeValue.Integer(promptDecl.Line));
                    promptObj.Set("column", RuntimeValue.Integer(promptDecl.Column));
                    var paramsArray = new List<RuntimeValue>();
                    foreach (var param in promptDecl.Parameters)
                        paramsArray.Add(RuntimeValue.String(param));
                    promptObj.Set("parameters", RuntimeValue.Array(paramsArray));
                    promptObj.Set("returnType", promptDecl.ReturnType != null ? RuntimeValue.String(promptDecl.ReturnType) : RuntimeValue.Null());
                    var sig = $"prompt {promptDecl.Name}({string.Join(", ", promptDecl.Parameters)})";
                    if (promptDecl.ReturnType != null)
                        sig += $" -> {promptDecl.ReturnType}";
                    promptObj.Set("signature", RuntimeValue.String(sig));
                    prompts.Add(RuntimeValue.Object(promptObj));
                }
            }

            var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, sourceFileName);
            var knownFunctionNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fn in functions)
            {
                var n = fn.AsObject().Get("name", null)?.AsString();
                if (n != null)
                    knownFunctionNames.Add(n);
            }

            foreach (var imp in imported.Imports)
            {
                var impObj = new JsonObject();
                impObj.Set("file", imp.IsFileImport ? RuntimeValue.String(imp.ModuleKey) : RuntimeValue.Null());
                impObj.Set("resolvedPath", imp.ResolvedPath != null ? RuntimeValue.String(imp.ResolvedPath) : RuntimeValue.Null());
                impObj.Set("package", imp.PackageName != null ? RuntimeValue.String(imp.PackageName) : RuntimeValue.Null());
                impObj.Set("alias", imp.Alias != null ? RuntimeValue.String(imp.Alias) : RuntimeValue.Null());
                importsList.Add(RuntimeValue.Object(impObj));
            }

            foreach (var funcDecl in imported.Functions)
            {
                if (knownFunctionNames.Contains(funcDecl.Name))
                    continue;
                knownFunctionNames.Add(funcDecl.Name);

                var funcObj = new JsonObject();
                funcObj.Set("name", RuntimeValue.String(funcDecl.Name));
                funcObj.Set("line", RuntimeValue.Integer(funcDecl.Line));
                funcObj.Set("column", RuntimeValue.Integer(funcDecl.Column));
                funcObj.Set("fromModule", RuntimeValue.String(Path.GetFileName(funcDecl.SourceFile ?? "imported")));

                var paramsArray = new List<RuntimeValue>();
                foreach (var param in funcDecl.Parameters)
                    paramsArray.Add(RuntimeValue.String(param));
                funcObj.Set("parameters", RuntimeValue.Array(paramsArray));

                var sig = BuildFunctionSignature(funcDecl.Name, funcDecl.Parameters, funcDecl.ParameterTypeHints, funcDecl.ReturnType);
                funcObj.Set("signature", RuntimeValue.String(sig));
                functions.Add(RuntimeValue.Object(funcObj));
            }

            var knownClassNames = new HashSet<string>(classes.Select(c => c.AsObject().Get("name", null)?.AsString() ?? ""), StringComparer.Ordinal);
            foreach (var classDecl in imported.Classes)
            {
                if (knownClassNames.Contains(classDecl.Name))
                    continue;
                knownClassNames.Add(classDecl.Name);

                var classObj = new JsonObject();
                classObj.Set("name", RuntimeValue.String(classDecl.Name));
                classObj.Set("fromModule", RuntimeValue.String(Path.GetFileName(classDecl.SourceFile ?? "imported")));
                classObj.Set("line", RuntimeValue.Integer(classDecl.Line));
                classObj.Set("column", RuntimeValue.Integer(classDecl.Column));
                classes.Add(RuntimeValue.Object(classObj));
            }
        }
        catch (MaldaLang.Parser.ParseException ex)
        {
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String(ex.Message));
            errorEntry.Set("line", RuntimeValue.Integer(ex.Line));
            errorEntry.Set("column", RuntimeValue.Integer(ex.Column));
            parseErrors.Add(RuntimeValue.Object(errorEntry));
        }
        catch (Exception ex)
        {
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String($"Error parsing code: {ex.Message}"));
            errorEntry.Set("line", RuntimeValue.Integer(0));
            errorEntry.Set("column", RuntimeValue.Integer(0));
            parseErrors.Add(RuntimeValue.Object(errorEntry));
        }
        
        // Build result object
        var resultObj = new JsonObject();
        resultObj.Set("classes", RuntimeValue.Array(classes));
        resultObj.Set("functions", RuntimeValue.Array(functions));
        resultObj.Set("actors", RuntimeValue.Array(actors));
        resultObj.Set("prompts", RuntimeValue.Array(prompts));
        resultObj.Set("parseErrors", RuntimeValue.Array(parseErrors));
        resultObj.Set("imports", RuntimeValue.Array(importsList));
        
        return RuntimeValue.Object(resultObj);
    }
    
    private static RuntimeValue BuiltInCreateCompileMALDATool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createCompileMALDATool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateCompileMALDATool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGetSymbolsTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGetSymbolsTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGetSymbolsTool(workingDir);
    }
    
    private static RuntimeValue BuiltInGetParseErrors(List<RuntimeValue> args)
    {
        if (args.Count == 0 || args[0].Type != ValueType.String)
            throw new Exception("getParseErrors() expects 1 argument: sourceOrFilePath (string)");
        
        var sourceOrFilePath = args[0].AsString();
        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
            throw new Exception("getParseErrors() sourceOrFilePath cannot be empty");
        
        string source = sourceOrFilePath;
        
        if (sourceOrFilePath.Contains(Path.DirectorySeparatorChar) ||
            sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
            sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string filePath = Path.IsPathRooted(sourceOrFilePath)
                    ? Path.GetFullPath(sourceOrFilePath)
                    : Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                
                if (sourceOrFilePath.Contains("..") || sourceOrFilePath.Contains("~"))
                {
                    var errorResultObj = new JsonObject();
                    var errList = new List<RuntimeValue>();
                    var errEntry = new JsonObject();
                    errEntry.Set("message", RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)"));
                    errEntry.Set("line", RuntimeValue.Integer(0));
                    errEntry.Set("column", RuntimeValue.Integer(0));
                    errList.Add(RuntimeValue.Object(errEntry));
                    errorResultObj.Set("parseErrors", RuntimeValue.Array(errList));
                    return RuntimeValue.Object(errorResultObj);
                }
                
                if (File.Exists(filePath))
                    source = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                var errorResultObj = new JsonObject();
                var errList = new List<RuntimeValue>();
                var errEntry = new JsonObject();
                errEntry.Set("message", RuntimeValue.String($"Error reading file: {ex.Message}"));
                errEntry.Set("line", RuntimeValue.Integer(0));
                errEntry.Set("column", RuntimeValue.Integer(0));
                errList.Add(RuntimeValue.Object(errEntry));
                errorResultObj.Set("parseErrors", RuntimeValue.Array(errList));
                return RuntimeValue.Object(errorResultObj);
            }
        }
        
        var parseErrors = new List<RuntimeValue>();
        
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens);
            parser.Parse();
            
            foreach (var error in parser.Errors)
            {
                var errorEntry = new JsonObject();
                errorEntry.Set("message", RuntimeValue.String(error.Message));
                errorEntry.Set("line", RuntimeValue.Integer(error.Line));
                errorEntry.Set("column", RuntimeValue.Integer(error.Column));
                parseErrors.Add(RuntimeValue.Object(errorEntry));
            }
        }
        catch (MaldaLang.Parser.ParseException ex)
        {
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String(ex.Message));
            errorEntry.Set("line", RuntimeValue.Integer(ex.Line));
            errorEntry.Set("column", RuntimeValue.Integer(ex.Column));
            parseErrors.Add(RuntimeValue.Object(errorEntry));
        }
        catch (Exception ex)
        {
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String($"Error parsing code: {ex.Message}"));
            errorEntry.Set("line", RuntimeValue.Integer(0));
            errorEntry.Set("column", RuntimeValue.Integer(0));
            parseErrors.Add(RuntimeValue.Object(errorEntry));
        }
        
        var resultObj = new JsonObject();
        resultObj.Set("parseErrors", RuntimeValue.Array(parseErrors));
        return RuntimeValue.Object(resultObj);
    }
    
    private static RuntimeValue BuiltInCreateGetParseErrorsTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGetParseErrorsTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGetParseErrorsTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateMcpAgentScript(List<RuntimeValue> args)
    {
        if (args.Count < 5)
            throw new Exception("createMcpAgentScript() expects at least 5 arguments: (agentName, agentRole, agentInstructions, tools, outputPath, model?)");
        
        // Validate required parameters
        if (args[0].Type != ValueType.String)
            throw new Exception("createMcpAgentScript() agentName must be a string");
        if (args[1].Type != ValueType.String)
            throw new Exception("createMcpAgentScript() agentRole must be a string");
        if (args[2].Type != ValueType.String)
            throw new Exception("createMcpAgentScript() agentInstructions must be a string");
        if (args[3].Type != ValueType.Array)
            throw new Exception("createMcpAgentScript() tools must be an array");
        if (args[4].Type != ValueType.String)
            throw new Exception("createMcpAgentScript() outputPath must be a string");
        
        var agentName = args[0].AsString();
        var agentRole = args[1].AsString();
        var agentInstructions = args[2].AsString();
        var toolsArray = args[3].AsArray();
        var outputPath = args[4].AsString();
        var model = args.Count > 5 && args[5].Type == ValueType.String ? args[5].AsString() : "openai/gpt-4";
        
        // Validate agentName is not empty
        if (string.IsNullOrWhiteSpace(agentName))
            throw new Exception("createMcpAgentScript() agentName cannot be empty");
        
        // Validate outputPath is not empty
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new Exception("createMcpAgentScript() outputPath cannot be empty");
        
        // Validate tools array
        if (toolsArray == null || toolsArray.Count == 0)
            throw new Exception("createMcpAgentScript() tools array cannot be empty");
        
        // Validate each tool in the array
        var toolNames = new List<string>();
        foreach (var toolValue in toolsArray)
        {
            if (toolValue.Type != ValueType.Object)
                throw new Exception("createMcpAgentScript() each tool in tools array must be an object");
            
            var toolObj = toolValue.AsObject();
            var nameValue = toolObj.Get("name");
            var descValue = toolObj.Get("description");
            
            if (nameValue == null || nameValue.Type != ValueType.String || string.IsNullOrWhiteSpace(nameValue.AsString()))
                throw new Exception("createMcpAgentScript() each tool must have a non-empty 'name' string property");
            
            if (descValue == null || descValue.Type != ValueType.String || string.IsNullOrWhiteSpace(descValue.AsString()))
                throw new Exception("createMcpAgentScript() each tool must have a non-empty 'description' string property");
            
            var toolName = nameValue.AsString();
            if (toolNames.Contains(toolName))
                throw new Exception($"createMcpAgentScript() duplicate tool name: {toolName}");
            
            toolNames.Add(toolName);
        }
        
        // Check for path traversal attempts (similar to runMALDA)
        if (outputPath.Contains("..") || outputPath.Contains("~"))
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.String(""));
            errorObj.Set("scriptContent", RuntimeValue.String(""));
            errorObj.Set("error", RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)"));
            return RuntimeValue.Object(errorObj);
        }
        
        try
        {
            // Resolve output path
            string fullPath;
            if (Path.IsPathRooted(outputPath))
            {
                fullPath = Path.GetFullPath(outputPath);
            }
            else
            {
                fullPath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, outputPath));
            }
            
            // Create directory if needed
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            // Generate the script
            var script = GenerateMcpAgentScript(agentName, agentRole, agentInstructions, toolsArray, model);
            
            // Write to file
            File.WriteAllText(fullPath, script);
            
            // Return success result
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(true));
            resultObj.Set("outputPath", RuntimeValue.String(fullPath));
            resultObj.Set("scriptContent", RuntimeValue.String(script));
            resultObj.Set("error", RuntimeValue.String(""));
            
            return RuntimeValue.Object(resultObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.String(""));
            errorObj.Set("scriptContent", RuntimeValue.String(""));
            errorObj.Set("error", RuntimeValue.String($"Error: {ex.Message}"));
            
            return RuntimeValue.Object(errorObj);
        }
    }
    
    private static string GenerateMcpAgentScript(string agentName, string agentRole, 
        string agentInstructions, List<RuntimeValue> tools, string model)
    {
        var sb = new StringBuilder();
        
        // Header comment
        sb.AppendLine($"// MCP Server for {agentName} Agent");
        sb.AppendLine("// Auto-generated script");
        sb.AppendLine();
        
        // Client setup
        sb.AppendLine($"var client = new OpenRouterClient(\"{EscapeString(model)}\");");
        sb.AppendLine();
        
        // Agent variable name (lowercase, sanitized)
        var agentVarName = SanitizeVariableName(agentName.ToLower());
        
        // Agent creation
        sb.AppendLine($"// Create the {agentName} agent");
        sb.AppendLine($"var {agentVarName} = new Agent(");
        sb.AppendLine($"    \"{EscapeString(agentName)}\",");
        sb.AppendLine($"    \"{EscapeString(agentRole)}\",");
        sb.AppendLine($"    \"{EscapeString(agentInstructions)}\",");
        sb.AppendLine("    client");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // Generate MCP tools
        sb.AppendLine("// Expose agent capabilities as MCP tools");
        var toolNameList = new List<string>();
        
        foreach (var toolValue in tools)
        {
            var toolObj = toolValue.AsObject();
            var nameValue = toolObj.Get("name");
            var descValue = toolObj.Get("description");
            var toolSchemaValue = toolObj.Get("schema");
            
            // These should already be validated, but check for safety
            if (nameValue == null || nameValue.Type != ValueType.String)
                continue; // Skip invalid tools (shouldn't happen after validation)
            
            var toolName = nameValue.AsString();
            var toolDesc = descValue != null && descValue.Type == ValueType.String 
                ? descValue.AsString() 
                : "";
            
            toolNameList.Add(toolName);
            
            sb.AppendLine();
            if (toolSchemaValue != null && toolSchemaValue.Type == ValueType.String && !string.IsNullOrWhiteSpace(toolSchemaValue.AsString()))
            {
                // Tool with custom schema
                var schemaStr = toolSchemaValue.AsString();
                sb.AppendLine($"@MCPTool(\"{EscapeString(toolName)}\", \"{EscapeString(toolDesc)}\", {schemaStr})");
            }
            else
            {
                // Tool without custom schema (auto-generated)
                sb.AppendLine($"@MCPTool(\"{EscapeString(toolName)}\", \"{EscapeString(toolDesc)}\")");
            }
            
            // Generate function - for now, all tools take a prompt parameter
            // This could be enhanced in the future to support more complex signatures
            var functionName = SanitizeFunctionName(toolName);
            sb.AppendLine($"function {functionName}(prompt) {{");
            sb.AppendLine($"    return {agentVarName}.think(prompt).content;");
            sb.AppendLine("}");
        }
        
        sb.AppendLine();
        sb.AppendLine("// Start the MCP server");
        sb.AppendLine("var server = new MCPServer();");
        sb.AppendLine("server.start();");
        sb.AppendLine();
        sb.AppendLine($"print(\"{EscapeString(agentName)} MCP Server started\");");
        sb.AppendLine($"print(\"Available tools: {string.Join(", ", toolNameList.Select(EscapeString))}\");");
        sb.AppendLine();
        sb.AppendLine("// Keep server running");
        sb.AppendLine("while (server.isRunning) {");
        sb.AppendLine("    sleep(1000);");
        sb.AppendLine("}");
        
        return sb.ToString();
    }
    
    private static string EscapeString(string str)
    {
        if (string.IsNullOrEmpty(str))
            return "";
        
        return str.Replace("\\", "\\\\")
                  .Replace("\"", "\\\"")
                  .Replace("\n", "\\n")
                  .Replace("\r", "\\r")
                  .Replace("\t", "\\t");
    }
    
    private static string SanitizeVariableName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "agent";
        
        // Remove invalid characters and ensure it starts with a letter
        var sanitized = new StringBuilder();
        var firstChar = true;
        
        foreach (var c in name)
        {
            if (char.IsLetter(c) || (!firstChar && (char.IsDigit(c) || c == '_')))
            {
                sanitized.Append(c);
                firstChar = false;
            }
            else if (!firstChar)
            {
                sanitized.Append('_');
            }
        }
        
        var result = sanitized.ToString();
        if (string.IsNullOrEmpty(result) || char.IsDigit(result[0]))
            return "agent_" + result;
        
        return result;
    }
    
    private static string SanitizeFunctionName(string name)
    {
        // Function names follow same rules as variable names
        return SanitizeVariableName(name);
    }
    
    private static RuntimeValue BuiltInCreateCreateMcpAgentScriptTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createCreateMcpAgentScriptTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateCreateMcpAgentScriptTool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateSubmitPlanTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createSubmitPlanTool", args, 0, 0);
        return BuiltInTools.CreateSubmitPlanTool();
    }
    
    private static RuntimeValue BuiltInExecutePlan(List<RuntimeValue> args)
    {
        BuiltInArity.Require("executePlan", args, 2, BuiltInArity.Unbounded, "plan, agent");
        var planVal = args[0];
        var agentVal = args[1];
        if (agentVal.Type != ValueType.Object)
            throw new Exception("executePlan second argument must be an Agent instance");
        var agentObj = agentVal.AsObject();
        if (agentObj is not AgentInstance agentInstance)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("executePlan second argument must be an Agent instance"));
            return RuntimeValue.Object(err);
        }
        // Accept already-normalized plan: any object with non-empty "steps" array is used as-is to avoid re-validation.
        RuntimeValue validation;
        if (planVal.Type == ValueType.Object)
        {
            var pObj = planVal.AsObject();
            var stepsVal = pObj.Get("steps", null);
            var hasStepsArray = stepsVal != null && stepsVal.Type == ValueType.Array && stepsVal.AsArray().Count > 0;
            if (hasStepsArray)
            {
                validation = planVal;
            }
            else
            {
                validation = ValidateAndNormalizePlan(planVal);
            }
        }
        else
        {
            validation = ValidateAndNormalizePlan(planVal);
        }
        if (validation.Type != ValueType.Object)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Invalid plan"));
            return RuntimeValue.Object(err);
        }
        var vObj = validation.AsObject();
        var errVal = vObj.Get("error", null);
        if (errVal != null && errVal.Type == ValueType.String)
        {
            var err = new JsonObject();
            err.Set("error", errVal);
            return RuntimeValue.Object(err);
        }
        var validatedPlanIdVal = vObj.Get("planId", null);
        string planId = validatedPlanIdVal != null && validatedPlanIdVal.Type == ValueType.String ? validatedPlanIdVal.AsString() : "";
        var orderedSteps = TopoSortSteps(validation);
        if (orderedSteps == null)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Plan has a cycle in step dependencies"));
            return RuntimeValue.Object(err);
        }
        var completed = new List<RuntimeValue>();
        var failed = new List<RuntimeValue>();
        var results = new List<RuntimeValue>();
        foreach (var step in orderedSteps)
        {
            var so = step.AsObject();
            var stepIdVal = so.Get("id", null);
            var descVal = so.Get("description", null);
            string stepId = stepIdVal != null && stepIdVal.Type == ValueType.String ? stepIdVal.AsString() : "";
            string description = descVal != null && descVal.Type == ValueType.String ? descVal.AsString() : "";
            var stepResult = new JsonObject();
            stepResult.Set("stepId", RuntimeValue.String(stepId));
            try
            {
                var thinkResult = agentInstance.Think(RuntimeValue.String(description));
                string output = thinkResult.Type == ValueType.String ? thinkResult.AsString() : (thinkResult.ToString() ?? "");
                if (thinkResult.Type == ValueType.Object)
                {
                    var contentVal = thinkResult.AsObject().Get("content", null);
                    if (contentVal != null && contentVal.Type == ValueType.String)
                        output = contentVal.AsString();
                }
                stepResult.Set("success", RuntimeValue.Boolean(true));
                stepResult.Set("output", RuntimeValue.String(output));
                completed.Add(RuntimeValue.String(stepId));
            }
            catch (Exception ex)
            {
                stepResult.Set("success", RuntimeValue.Boolean(false));
                stepResult.Set("error", RuntimeValue.String(ex.Message));
                failed.Add(RuntimeValue.String(stepId));
            }
            results.Add(RuntimeValue.Object(stepResult));
        }
        var outResult = new JsonObject();
        outResult.Set("planId", RuntimeValue.String(planId));
        outResult.Set("completed", RuntimeValue.Array(completed));
        outResult.Set("failed", RuntimeValue.Array(failed));
        outResult.Set("results", RuntimeValue.Array(results));
        return RuntimeValue.Object(outResult);
    }
    
    private static RuntimeValue BuiltInSetDefaultAgent(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count != 1)
            throw new Exception("setDefaultAgent(agent) expects exactly 1 argument: agent instance");
        
        if (args[0].Type != ValueType.Object)
            throw new Exception("setDefaultAgent() expects an Agent instance");
        
        var agentObj = args[0].AsObject();
        if (agentObj is not AgentInstance agent)
            throw new Exception("setDefaultAgent() expects an Agent instance");
        
        var effectiveInterpreter = interpreter ?? TranspiledBuiltinRuntime.GetOrCreateInterpreter();
        effectiveInterpreter._defaultAgent = agent;
        return RuntimeValue.Null();
    }
    
    private static RuntimeValue BuiltInDecomposeTask(List<RuntimeValue> args)
    {
        BuiltInArity.Require("decomposeTask", args, 1, BuiltInArity.Unbounded, "instruction, client?");
        if (args[0].Type != ValueType.String)
            throw new Exception("decomposeTask() instruction must be a string");
        var instruction = args[0].AsString();
        LLMClientInstance? client = null;
        if (args.Count > 1 && args[1].Type == ValueType.Object)
        {
            var obj = args[1].AsObject();
            if (obj is LLMClientInstance llmClient)
                client = llmClient;
        }
        const string systemPrompt = "You are a task decomposer. Given a task description, respond with ONLY a valid JSON object and no other text. The JSON must have this exact shape: {\"steps\": [{\"id\": \"1\", \"description\": \"...\", \"dependsOn\": []}, ...]}. Each step must have \"id\" (unique string) and \"description\" (string). Optional \"dependsOn\" is an array of step ids that must complete before this step. No cycles. No markdown, no explanation.";
        var conv = new ConversationInstance();
        if (client != null)
            conv.Initialize(client, null, null, systemPrompt, null);
        else
            conv.Initialize(null, DefaultLocalLlm.GetDefaultLocalClient(), null, systemPrompt, null);
        conv.AddUserMessage(instruction);
        RuntimeValue response;
        try
        {
            response = conv.Send();
        }
        catch (Exception ex)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String($"LLM call failed: {ex.Message}"));
            return RuntimeValue.Object(err);
        }
        if (response.Type != ValueType.Object)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Unexpected response from LLM"));
            return RuntimeValue.Object(err);
        }
        var contentVal = response.AsObject().Get("content", null);
        if (contentVal == null || contentVal.Type != ValueType.String)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("No content in LLM response"));
            return RuntimeValue.Object(err);
        }
        var content = contentVal.AsString().Trim();
        var jsonStr = content;
        if (content.Contains("```"))
        {
            var start = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                start = content.IndexOf('\n', start) + 1;
                var end = content.IndexOf("```", start, StringComparison.Ordinal);
                if (end > start)
                    jsonStr = content.Substring(start, end - start).Trim();
            }
        }
        if (jsonStr.IndexOf('{') >= 0 && jsonStr.IndexOf('}') >= 0)
        {
            var first = jsonStr.IndexOf('{');
            var last = jsonStr.LastIndexOf('}');
            if (last > first)
                jsonStr = jsonStr.Substring(first, last - first + 1);
        }
        RuntimeValue parsed;
        try
        {
            parsed = BuiltInParseJSON(new List<RuntimeValue> { RuntimeValue.String(jsonStr) });
        }
        catch
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("LLM response was not valid JSON. Ensure the model returns only a JSON object with a 'steps' array."));
            return RuntimeValue.Object(err);
        }
        var validation = ValidateAndNormalizePlan(parsed);
        if (validation.Type != ValueType.Object)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Invalid plan structure from LLM"));
            return RuntimeValue.Object(err);
        }
        var vObj = validation.AsObject();
        var errVal = vObj.Get("error", null);
        if (errVal != null && errVal.Type == ValueType.String)
            return validation;
        return validation;
    }
    
    private static readonly MarkdownPipeline MarkdownToHtmlPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static RuntimeValue BuiltInMarkdownToHtml(List<RuntimeValue> args)
    {
        BuiltInArity.Require("markdownToHtml", args, 1, 1, "markdown");
        if (args[0].Type != ValueType.String)
            throw new Exception("markdownToHtml() expects 1 string argument: (markdown)");

        var markdown = args[0].AsString();
        if (string.IsNullOrEmpty(markdown))
            return RuntimeValue.String("");

        var html = Markdown.ToHtml(markdown, MarkdownToHtmlPipeline);
        return RuntimeValue.String(html);
    }

    private static RuntimeValue BuiltInExtractHTML(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("extractHTML() expects 1 string argument");

        var markdown = args[0].AsString();

        // Extract HTML from markdown code blocks
        var htmlPattern = @"```html\s*(.*?)\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(
            markdown,
            htmlPattern,
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        if (match.Success)
            return RuntimeValue.String(match.Groups[1].Value.Trim());

        // If no code block, check if it's already HTML
        if (markdown.Contains("<html") || markdown.Contains("<!DOCTYPE"))
            return RuntimeValue.String(markdown);
        
        // Return as-is if no HTML found
        return RuntimeValue.String(markdown);
    }
    
    private static RuntimeValue BuiltInRedirect(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception("redirect() expects location string and optional status integer");

        int? statusCode = null;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Integer)
                throw new Exception("redirect() status must be an integer when provided");
            statusCode = args[1].AsInteger();
        }

        return RuntimeValue.Object(WebRuntimeHelpers.CreateRedirectResponse(args[0].AsString(), statusCode));
    }

    private static RuntimeValue BuiltInRedirectTo(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("RedirectTo() expects 1 string argument: (location)");

        return BuiltInRedirect(new List<RuntimeValue> { args[0] });
    }

    private static RuntimeValue BuiltInRenderTemplate(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception("renderTemplate() expects template string and optional object model");

        var template = args[0].AsString();
        if (args.Count == 1)
            return RuntimeValue.String(template);

        var modelObj = CoerceRuntimeObjectToJsonObject(args[1]);
        if (modelObj == null)
            return RuntimeValue.String(template);

        return RuntimeValue.String(RenderTemplateTokenString(template, modelObj));
    }

    private static RuntimeValue BuiltInComponentFragment(List<RuntimeValue> args)
    {
        if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("componentFragment() expects targetId and html string");

        var targetId = args[0].AsString();
        var html = args[1].AsString();

        var response = new JsonObject();
        response.Set("status", RuntimeValue.Integer(200));

        var headers = new JsonObject();
        headers.Set("X-Malda-Fragment", RuntimeValue.String("true"));
        headers.Set("X-Malda-Fragment-Target", RuntimeValue.String(targetId));
        response.Set("headers", RuntimeValue.Object(headers));
        response.Set("body", RuntimeValue.String(html));

        return RuntimeValue.Object(response);
    }

    private static RuntimeValue BuiltInComponentLiveEmit(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("componentLiveEmit() expects channel, payload, optional eventType");

        var channel = args[0].AsString();
        var payload = args[1];
        var eventType = args.Count == 3 && args[2].Type == ValueType.String ? args[2].AsString() : "update";

        var msg = new JsonObject();
        msg.Set("type", RuntimeValue.String("componentLiveEvent"));
        msg.Set("event", RuntimeValue.String(eventType));
        msg.Set("channel", RuntimeValue.String(channel));
        msg.Set("payload", payload);

        var json = RuntimeValueToJson(RuntimeValue.Object(msg));
        HttpServerInstance.BroadcastSSEMessage(json, channel);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInOnAgentProgress(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("onAgentProgress", args, 1, 1, "handlerOrChannel");
        if (args[0].Type == ValueType.String)
        {
            // Transpile-friendly: emit progress via componentLiveEmit(channel, …).
            ConversationInstance.SetAgentProgressLiveChannel(args[0].AsString());
            return RuntimeValue.Null();
        }

        if (args[0].Type != ValueType.Function)
            throw new Exception("onAgentProgress() expects a function handler(event) or a live channel string");
        if (interpreter == null)
            throw new Exception("onAgentProgress(handler) requires an interpreter; use onAgentProgress(\"channel\") in transpile");

        var handler = args[0].AsFunction();
        ConversationInstance.SetAgentProgressHandler(evt =>
        {
            try
            {
                interpreter.CallFunctionAsync(handler, new List<RuntimeValue> { evt })
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Never break the agent loop from a progress handler.
            }
        });
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInClearAgentProgress(List<RuntimeValue> args)
    {
        BuiltInArity.Require("clearAgentProgress", args, 0, 0);
        ConversationInstance.ClearAgentProgressHandler();
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInComponentStateGet(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 4 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("componentStateGet() expects componentId, key, optional defaultValue, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        var key = args[1].AsString();
        var value = HttpServerInstance.GetComponentState(componentId, key);
        if (value.Type == ValueType.Null && args.Count >= 3)
            return args[2];
        return value;
    }

    private static RuntimeValue BuiltInComponentStateSet(List<RuntimeValue> args)
    {
        if ((args.Count != 3 && args.Count != 4) || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("componentStateSet() expects componentId, key, value, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        var key = args[1].AsString();
        var value = args[2];
        HttpServerInstance.SetComponentState(componentId, key, value);
        return value;
    }

    private static RuntimeValue BuiltInComponentStateObject(List<RuntimeValue> args)
    {
        if ((args.Count != 1 && args.Count != 2) || args[0].Type != ValueType.String)
            throw new Exception("componentStateObject() expects componentId and optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 2 ? args[1] : RuntimeValue.Null());
        return HttpServerInstance.GetComponentStateObject(componentId);
    }

    private static RuntimeValue BuiltInComponentStateClear(List<RuntimeValue> args)
    {
        if (args.Count > 2)
            throw new Exception("componentStateClear() expects zero args (all), one componentId, or componentId + scope");

        if (args.Count == 0)
        {
            HttpServerInstance.ClearAllComponentState();
            return RuntimeValue.Null();
        }

        if (args[0].Type != ValueType.String)
            throw new Exception("componentStateClear(componentId) expects a string");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 2 ? args[1] : RuntimeValue.Null());
        HttpServerInstance.ClearComponentState(componentId);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInComponentStateConfigure(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.Integer || args[1].Type != ValueType.Integer || (args.Count == 3 && args[2].Type != ValueType.Integer))
            throw new Exception("componentStateConfigure() expects maxComponents, maxKeysPerComponent, optional ttlMs");

        var maxComponents = args[0].AsInteger();
        var maxKeysPerComponent = args[1].AsInteger();
        var ttlMs = args.Count == 3 ? args[2].AsInteger() : 1800000;
        HttpServerInstance.ConfigureComponentStatePolicy(maxComponents, maxKeysPerComponent, ttlMs);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiRow(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("row", args);
    private static RuntimeValue BuiltInUiColumn(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("column", args);
    private static RuntimeValue BuiltInUiStack(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("stack", args);
    private static RuntimeValue BuiltInUiSpacer(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("spacer", args);
    private static RuntimeValue BuiltInUiPanel(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("panel", args);
    private static RuntimeValue BuiltInUiText(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("text", args);
    private static RuntimeValue BuiltInUiHeading(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("heading", args);
    private static RuntimeValue BuiltInUiImage(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("image", args);
    private static RuntimeValue BuiltInUiIcon(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("icon", args);
    private static RuntimeValue BuiltInUiButton(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("button", args);
    private static RuntimeValue BuiltInUiTextField(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("textField", args);
    private static RuntimeValue BuiltInUiCheckbox(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("checkbox", args);
    private static RuntimeValue BuiltInUiSelect(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("select", args);
    private static RuntimeValue BuiltInUiSlider(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("slider", args);
    private static RuntimeValue BuiltInUiDatePicker(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("datePicker", args);
    private static RuntimeValue BuiltInUiList(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("list", args);
    private static RuntimeValue BuiltInUiTable(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("table", args);
    private static RuntimeValue BuiltInUiAlert(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("alert", args);
    private static RuntimeValue BuiltInUiProgress(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("progress", args);
    private static RuntimeValue BuiltInUiModal(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("modal", args);
    private static RuntimeValue BuiltInUiForm(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("form", args);
    private static RuntimeValue BuiltInUiField(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("field", args);
    private static RuntimeValue BuiltInUiTextArea(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("textArea", args);
    private static RuntimeValue BuiltInUiRadioGroup(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("radioGroup", args);
    private static RuntimeValue BuiltInUiSwitch(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("switch", args);
    private static RuntimeValue BuiltInUiTabs(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("tabs", args);
    private static RuntimeValue BuiltInUiAccordion(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("accordion", args);
    private static RuntimeValue BuiltInUiBreadcrumbs(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("breadcrumbs", args);
    private static RuntimeValue BuiltInUiDrawer(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("drawer", args);
    private static RuntimeValue BuiltInUiDataGrid(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("dataGrid", args);
    private static RuntimeValue BuiltInUiTreeView(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("treeView", args);
    private static RuntimeValue BuiltInUiPaginator(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("paginator", args);
    private static RuntimeValue BuiltInUiEmptyState(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("emptyState", args);
    private static RuntimeValue BuiltInUiBadge(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("badge", args);
    private static RuntimeValue BuiltInUiToast(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("toast", args);
    private static RuntimeValue BuiltInUiSkeleton(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("skeleton", args);
    private static RuntimeValue BuiltInUiSpinner(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("spinner", args);
    private static RuntimeValue BuiltInUiErrorBoundary(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("errorBoundary", args);
    private static RuntimeValue BuiltInUiSlot(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("slot", args);

    private static RuntimeValue BuiltInUiTemplate(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("uiTemplate() expects source string, optional model object, optional options object");

        var model = args.Count >= 2 ? CoerceRuntimeObjectToJsonObject(args[1]) : null;
        var useCache = true;
        var compatRaw = false;
        if (args.Count == 3)
        {
            var options = CoerceRuntimeObjectToJsonObject(args[2]);
            if (options == null)
                throw new Exception("uiTemplate() options must be an object when provided");
            useCache = GetBooleanOptionFromObject(options, "cache", true);
            compatRaw = GetBooleanOptionFromObject(options, "compatRaw", false);
        }

        var template = ResolveUiTemplateSource(args[0].AsString(), useCache);
        return RuntimeValue.String(RenderUiTemplateString(template, model, compatRaw));
    }

    private static RuntimeValue BuiltInUiPartial(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception("uiPartial() expects source string and optional model object");

        var model = args.Count == 2 ? CoerceRuntimeObjectToJsonObject(args[1]) : null;
        var template = ResolveUiTemplateSource(args[0].AsString(), true);
        return RuntimeValue.String(RenderUiTemplateString(template, model, false));
    }

    private static RuntimeValue BuiltInUiLayout(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("uiLayout() expects source string, slots object, optional model object");

        var slots = CoerceRuntimeObjectToJsonObject(args[1]);
        if (slots == null)
            throw new Exception("uiLayout() slots must be an object");

        var model = args.Count == 3 ? CoerceRuntimeObjectToJsonObject(args[2]) : null;
        var mergedModel = new JsonObject();
        if (model != null)
        {
            foreach (var kvp in model.GetProperties())
            {
                mergedModel.Set(kvp.Key, kvp.Value);
            }
        }
        var rawSlotKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in slots.GetProperties())
        {
            var slotKey = "slot:" + kvp.Key;
            mergedModel.Set(slotKey, kvp.Value);
            rawSlotKeys.Add(slotKey);
        }

        var template = ResolveUiTemplateSource(args[0].AsString(), true);
        return RuntimeValue.String(RenderUiTemplateString(template, mergedModel, false, rawSlotKeys));
    }

    private static RuntimeValue BuiltInUiRenderList(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.Array || args[1].Type != ValueType.String)
            throw new Exception("uiRenderList() expects items array, source string, optional itemName");

        var itemName = args.Count == 3 && args[2].Type == ValueType.String ? args[2].AsString() : "item";
        if (string.IsNullOrWhiteSpace(itemName))
            itemName = "item";

        var template = ResolveUiTemplateSource(args[1].AsString(), true);
        var items = args[0].AsArray();
        var parts = new List<string>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var model = new JsonObject();
            model.Set("index", RuntimeValue.Integer(i));
            model.Set(itemName, item);
            var itemObject = CoerceRuntimeObjectToJsonObject(item);
            if (itemObject != null)
            {
                foreach (var kvp in itemObject.GetProperties())
                {
                    model.Set(kvp.Key, kvp.Value);
                }
            }

            parts.Add(RenderUiTemplateString(template, model, false));
        }

        return RuntimeValue.String(string.Join(string.Empty, parts));
    }

    private static RuntimeValue BuiltInUiCrudModel(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 4)
            throw new Exception("uiCrudModel() expects schema object, optional sessionId, optional queryParams object, optional lookups object");
        if (args[0].Type != ValueType.Object)
            throw new Exception("uiCrudModel() schema must be an object");

        var schema = CoerceRuntimeObjectToJsonObject(args[0]);
        if (schema == null)
            throw new Exception("uiCrudModel() schema must be an object");

        var sessionIdArg = args.Count >= 2 && args[1].Type == ValueType.String ? args[1].AsString() : null;
        var queryParams = args.Count >= 3 ? CoerceRuntimeObjectToJsonObject(args[2]) : null;
        var lookups = args.Count == 4 ? CoerceRuntimeObjectToJsonObject(args[3]) : null;
        var model = BuildUiCrudModelFromSchema(schema, sessionIdArg, queryParams, lookups, null);
        return RuntimeValue.Object(model);
    }

    private static RuntimeValue BuiltInUiCrudControls(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 5)
            throw new Exception("uiCrudControls() expects schema object, optional sessionId, optional queryParams object, optional lookups object, optional options object");
        if (args[0].Type != ValueType.Object)
            throw new Exception("uiCrudControls() schema must be an object");

        var schema = CoerceRuntimeObjectToJsonObject(args[0]);
        if (schema == null)
            throw new Exception("uiCrudControls() schema must be an object");

        var sessionIdArg = args.Count >= 2 && args[1].Type == ValueType.String ? args[1].AsString() : null;
        var queryParams = args.Count >= 3 ? CoerceRuntimeObjectToJsonObject(args[2]) : null;
        var lookups = args.Count >= 4 ? CoerceRuntimeObjectToJsonObject(args[3]) : null;
        var options = args.Count == 5 ? CoerceRuntimeObjectToJsonObject(args[4]) : null;
        if (args.Count == 5 && options == null)
            throw new Exception("uiCrudControls() options must be an object when provided");

        var model = BuildUiCrudModelFromSchema(schema, sessionIdArg, queryParams, lookups, options);

        var useCache = options == null ? true : GetBooleanOptionFromObject(options, "cache", true);
        var compatRaw = options != null && GetBooleanOptionFromObject(options, "compatRaw", false);
        var templatePath = ResolveCrudControlsTemplatePath(schema, options);
        var template = ResolveUiTemplateSource(templatePath, useCache);
        return RuntimeValue.String(RenderUiTemplateString(template, model, compatRaw));
    }

    private static RuntimeValue BuiltInUiCrudSchema(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("uiCrudSchema() expects schema object and optional defaults object");
        if (args[0].Type != ValueType.Object)
            throw new Exception("uiCrudSchema() schema must be an object");

        var schema = CoerceRuntimeObjectToJsonObject(args[0]);
        if (schema == null)
            throw new Exception("uiCrudSchema() schema must be an object");

        JsonObject? defaults = null;
        if (args.Count == 2)
        {
            defaults = CoerceRuntimeObjectToJsonObject(args[1]);
            if (defaults == null)
                throw new Exception("uiCrudSchema() defaults must be an object when provided");
        }

        var normalized = new JsonObject();
        foreach (var kvp in schema.GetProperties())
        {
            normalized.Set(kvp.Key, kvp.Value);
        }

        var templateBasePath = GetStringValueFromObject(
            normalized,
            "templateBasePath",
            defaults == null
                ? Path.Combine("Examples", "Web", "templates", "crm")
                : GetStringValueFromObject(defaults, "templateBasePath", Path.Combine("Examples", "Web", "templates", "crm")));
        normalized.Set("templateBasePath", RuntimeValue.String(templateBasePath));

        var controlsDefault = defaults == null
            ? Path.Combine(templateBasePath, "entity_controls.html")
            : GetStringValueFromObject(defaults, "controlsTemplatePath", Path.Combine(templateBasePath, "entity_controls.html"));
        var controlsTemplatePath = GetStringValueFromObject(normalized, "controlsTemplatePath", controlsDefault);
        normalized.Set("controlsTemplatePath", RuntimeValue.String(controlsTemplatePath));

        var filterDefs = normalized.Get("filterDefs", null);
        if (filterDefs.Type != ValueType.Array)
        {
            normalized.Set("filterDefs", RuntimeValue.Array(new List<RuntimeValue>()));
        }

        var dialogLookupOptions = normalized.Get("dialogLookupOptions", null);
        if (dialogLookupOptions.Type != ValueType.Array)
        {
            normalized.Set("dialogLookupOptions", RuntimeValue.Array(new List<RuntimeValue>()));
        }

        var singular = GetStringValueFromObject(normalized, "entitySingularLower", "item");
        var addLabelDefault = defaults == null
            ? "Add " + singular
            : GetStringValueFromObject(defaults, "openAddLabel", "Add " + singular);
        var editLabelDefault = defaults == null
            ? "Edit selected " + singular
            : GetStringValueFromObject(defaults, "openEditLabel", "Edit selected " + singular);

        normalized.Set("openAddLabel", RuntimeValue.String(GetStringValueFromObject(normalized, "openAddLabel", addLabelDefault)));
        normalized.Set("openEditLabel", RuntimeValue.String(GetStringValueFromObject(normalized, "openEditLabel", editLabelDefault)));
        return RuntimeValue.Object(normalized);
    }

    private static JsonObject BuildUiCrudModelFromSchema(
        JsonObject schema,
        string? sessionIdArg,
        JsonObject? queryParams,
        JsonObject? lookups,
        JsonObject? options)
    {
        var sessionId = string.IsNullOrWhiteSpace(sessionIdArg)
            ? GetStringValueFromObject(schema, "sessionDefault", "default")
            : sessionIdArg!;
        var effectiveQuery = queryParams ?? new JsonObject();
        var effectiveLookups = lookups ?? new JsonObject();
        var templateBasePath = ResolveCrudTemplateBasePath(schema, options);

        var model = new JsonObject();
        model.Set("sessionId", RuntimeValue.String(sessionId));
        model.Set("entityPluralLower", RuntimeValue.String(GetStringValueFromObject(schema, "entityPluralLower", string.Empty)));
        model.Set("entitySingularLower", RuntimeValue.String(GetStringValueFromObject(schema, "entitySingularLower", string.Empty)));
        model.Set("listAction", RuntimeValue.String(GetStringValueFromObject(schema, "listAction", string.Empty)));
        model.Set("filterGridColumns", RuntimeValue.String(GetStringValueFromObject(schema, "filterGridColumns", string.Empty)));
        model.Set("openAddButtonId", RuntimeValue.String(GetStringValueFromObject(schema, "openAddButtonId", string.Empty)));
        model.Set("openEditButtonId", RuntimeValue.String(GetStringValueFromObject(schema, "openEditButtonId", string.Empty)));
        model.Set("openAddLabel", RuntimeValue.String(GetStringValueFromObject(schema, "openAddLabel", string.Empty)));
        model.Set("openEditLabel", RuntimeValue.String(GetStringValueFromObject(schema, "openEditLabel", string.Empty)));
        model.Set("filters", RuntimeValue.Array(BuildCrudFilters(schema, effectiveQuery)));

        var dialogModel = new JsonObject();
        dialogModel.Set("sessionId", RuntimeValue.String(sessionId));
        var lookupBindings = BuildCrudLookupBindings(schema, effectiveLookups, templateBasePath);
        foreach (var kvp in lookupBindings.GetProperties())
        {
            dialogModel.Set(kvp.Key, kvp.Value);
        }

        model.Set("addDialogHtml", RuntimeValue.String(RenderCrudTemplate(schema, "addDialogTemplate", dialogModel, templateBasePath)));
        model.Set("editDialogHtml", RuntimeValue.String(RenderCrudTemplate(schema, "editDialogTemplate", dialogModel, templateBasePath)));
        model.Set("dialogScript", RuntimeValue.String(RenderCrudTemplate(schema, "dialogScriptTemplate", null, templateBasePath)));
        return model;
    }

    private static List<RuntimeValue> BuildCrudFilters(JsonObject schema, JsonObject queryParams)
    {
        var filters = new List<RuntimeValue>();
        var defsValue = schema.Get("filterDefs", null);
        if (defsValue.Type != ValueType.Array)
        {
            return filters;
        }

        foreach (var defValue in defsValue.AsArray())
        {
            var def = CoerceRuntimeObjectToJsonObject(defValue);
            if (def == null)
            {
                continue;
            }

            var kind = GetStringValueFromObject(def, "kind", string.Empty);
            var name = GetStringValueFromObject(def, "name", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var defaultValue = GetStringValueFromObject(def, "defaultValue", string.Empty);
            var currentValue = GetQueryParamString(queryParams, name, defaultValue);

            if (string.Equals(kind, "input", StringComparison.Ordinal))
            {
                var inputFilter = new JsonObject();
                inputFilter.Set("isInput", RuntimeValue.Boolean(true));
                inputFilter.Set("isSelect", RuntimeValue.Boolean(false));
                inputFilter.Set("name", RuntimeValue.String(name));
                inputFilter.Set("placeholder", RuntimeValue.String(GetStringValueFromObject(def, "placeholder", string.Empty)));
                inputFilter.Set("value", RuntimeValue.String(currentValue));
                filters.Add(RuntimeValue.Object(inputFilter));
                continue;
            }

            var options = new List<RuntimeValue>();
            var rawOptions = def.Get("options", null);
            if (rawOptions.Type == ValueType.Array)
            {
                foreach (var optionValue in rawOptions.AsArray())
                {
                    var optionObj = CoerceRuntimeObjectToJsonObject(optionValue);
                    if (optionObj == null)
                    {
                        continue;
                    }

                    var optionKey = GetStringValueFromObject(optionObj, "value", string.Empty);
                    var optionModel = new JsonObject();
                    optionModel.Set("value", RuntimeValue.String(optionKey));
                    optionModel.Set("label", RuntimeValue.String(GetStringValueFromObject(optionObj, "label", string.Empty)));
                    optionModel.Set("selectedAttr", RuntimeValue.String(string.Equals(currentValue, optionKey, StringComparison.Ordinal) ? " selected" : string.Empty));
                    options.Add(RuntimeValue.Object(optionModel));
                }
            }

            var selectFilter = new JsonObject();
            selectFilter.Set("isInput", RuntimeValue.Boolean(false));
            selectFilter.Set("isSelect", RuntimeValue.Boolean(true));
            selectFilter.Set("name", RuntimeValue.String(name));
            selectFilter.Set("options", RuntimeValue.Array(options));
            filters.Add(RuntimeValue.Object(selectFilter));
        }

        return filters;
    }

    private static JsonObject BuildCrudLookupBindings(JsonObject schema, JsonObject lookups, string templateBasePath)
    {
        var rendered = new JsonObject();
        var bindingsValue = schema.Get("dialogLookupOptions", null);
        if (bindingsValue.Type != ValueType.Array)
        {
            return rendered;
        }

        foreach (var bindingValue in bindingsValue.AsArray())
        {
            var binding = CoerceRuntimeObjectToJsonObject(bindingValue);
            if (binding == null)
            {
                continue;
            }

            var key = GetStringValueFromObject(binding, "key", string.Empty);
            var source = GetStringValueFromObject(binding, "source", string.Empty);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var rowsValue = lookups.Get(source, null);
            var rows = rowsValue.Type == ValueType.Array ? rowsValue.AsArray() : new List<RuntimeValue>();

            var renderer = GetStringValueFromObject(binding, "renderer", string.Empty);
            var itemName = GetStringValueFromObject(binding, "itemName", "item");
            var templatePath = GetStringValueFromObject(binding, "templatePath", string.Empty);
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                if (string.Equals(renderer, "customerOptions", StringComparison.Ordinal))
                {
                    templatePath = "customer_option.html";
                    if (string.IsNullOrWhiteSpace(itemName) || string.Equals(itemName, "item", StringComparison.Ordinal))
                    {
                        itemName = "customer";
                    }
                }
                else if (string.Equals(renderer, "agentOptions", StringComparison.Ordinal))
                {
                    templatePath = "agent_option.html";
                    if (string.IsNullOrWhiteSpace(itemName) || string.Equals(itemName, "item", StringComparison.Ordinal))
                    {
                        itemName = "agent";
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(templatePath))
            {
                continue;
            }

            var resolvedTemplatePath = ResolveCrudTemplatePath(templatePath, templateBasePath);
            var renderedValue = BuiltInUiRenderList(new List<RuntimeValue>
            {
                RuntimeValue.Array(rows),
                RuntimeValue.String(resolvedTemplatePath),
                RuntimeValue.String(string.IsNullOrWhiteSpace(itemName) ? "item" : itemName)
            });
            rendered.Set(key, renderedValue.Type == ValueType.String ? renderedValue : RuntimeValue.String(string.Empty));
        }

        return rendered;
    }

    private static string RenderCrudTemplate(JsonObject schema, string schemaKey, JsonObject? model, string templateBasePath)
    {
        var templateName = GetStringValueFromObject(schema, schemaKey, string.Empty);
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return string.Empty;
        }

        var templatePath = ResolveCrudTemplatePath(templateName, templateBasePath);
        var template = ResolveUiTemplateSource(templatePath, true);
        return RenderUiTemplateString(template, model, false);
    }

    private static string ResolveCrudControlsTemplatePath(JsonObject schema, JsonObject? options)
    {
        if (options != null)
        {
            var optionsTemplatePath = GetStringValueFromObject(options, "templatePath", string.Empty);
            if (!string.IsNullOrWhiteSpace(optionsTemplatePath))
            {
                return optionsTemplatePath;
            }
        }

        var schemaTemplatePath = GetStringValueFromObject(schema, "controlsTemplatePath", string.Empty);
        if (!string.IsNullOrWhiteSpace(schemaTemplatePath))
        {
            return schemaTemplatePath;
        }

        schemaTemplatePath = GetStringValueFromObject(schema, "controlsTemplate", string.Empty);
        if (!string.IsNullOrWhiteSpace(schemaTemplatePath))
        {
            return schemaTemplatePath;
        }

        return Path.Combine("Examples", "Web", "templates", "crm", "entity_controls.html");
    }

    private static string ResolveCrudTemplateBasePath(JsonObject schema, JsonObject? options)
    {
        if (options != null)
        {
            var optionsBasePath = GetStringValueFromObject(options, "templateBasePath", string.Empty);
            if (!string.IsNullOrWhiteSpace(optionsBasePath))
            {
                return optionsBasePath;
            }
        }

        var schemaBasePath = GetStringValueFromObject(schema, "templateBasePath", string.Empty);
        if (!string.IsNullOrWhiteSpace(schemaBasePath))
        {
            return schemaBasePath;
        }

        return Path.Combine("Examples", "Web", "templates", "crm");
    }

    private static string ResolveCrudTemplatePath(string templatePathOrName, string templateBasePath)
    {
        if (string.IsNullOrWhiteSpace(templatePathOrName))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(templatePathOrName))
        {
            return templatePathOrName;
        }

        if (string.IsNullOrWhiteSpace(templateBasePath))
        {
            return templatePathOrName;
        }

        return Path.Combine(templateBasePath, templatePathOrName);
    }

    private static string GetQueryParamString(JsonObject queryParams, string key, string defaultValue)
    {
        var value = queryParams.Get(key, null);
        if (value.Type == ValueType.Null)
        {
            return defaultValue;
        }

        var text = value.Type == ValueType.String ? value.AsString() : RuntimeValueToTemplateString(value);
        return string.IsNullOrEmpty(text) ? defaultValue : text;
    }

    private static string GetStringValueFromObject(JsonObject obj, string key, string defaultValue)
    {
        var value = obj.Get(key, null);
        if (value.Type == ValueType.Null)
        {
            return defaultValue;
        }

        if (value.Type == ValueType.String)
        {
            var text = value.AsString();
            return string.IsNullOrEmpty(text) ? defaultValue : text;
        }

        return RuntimeValueToTemplateString(value);
    }

    private static RuntimeValue BuiltInUiWithSlot(List<RuntimeValue> args)
    {
        if (args.Count != 3 || args[0].Type != ValueType.Object || args[1].Type != ValueType.String)
            throw new Exception("uiWithSlot() expects rootNode, slotName, slotContent");

        var root = args[0].AsObject() as JsonObject;
        if (root == null)
            throw new Exception("uiWithSlot() rootNode must be a UI node object");

        var slotName = args[1].AsString();
        var slotContent = args[2];
        var replaced = ReplaceSlot(root, slotName, slotContent);
        return RuntimeValue.Object(replaced);
    }

    private static RuntimeValue BuiltInUiWhen(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.Boolean)
            throw new Exception("uiWhen() expects condition, thenNode, optional elseNode");

        if (args[0].AsBoolean())
            return args[1];

        return args.Count == 3 ? args[2] : RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiChoose(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[1].Type != ValueType.Object)
            throw new Exception("uiChoose() expects key, casesObject, optional defaultNode");

        var key = args[0].ToString();
        var casesObject = args[1].AsObject() as JsonObject;
        if (casesObject == null)
            return args.Count == 3 ? args[2] : RuntimeValue.Null();

        var selected = casesObject.Get(key, null);
        if (selected.Type != ValueType.Null)
            return selected;

        return args.Count == 3 ? args[2] : RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiEach(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.Array)
            throw new Exception("uiEach() expects items array and optional propName (default: value)");

        var propName = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "value";
        var children = new List<RuntimeValue>();
        foreach (var item in args[0].AsArray())
        {
            var props = new JsonObject();
            props.Set(propName, item);
            var node = new JsonObject();
            node.Set("type", RuntimeValue.String("text"));
            node.Set("props", RuntimeValue.Object(props));
            node.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));
            children.Add(RuntimeValue.Object(node));
        }

        return RuntimeValue.Array(children);
    }

    private static JsonObject ReplaceSlot(JsonObject root, string slotName, RuntimeValue slotContent)
    {
        var type = root.Get("type", null);
        if (type.Type == ValueType.String && type.AsString() == "slot")
        {
            var propsValue = root.Get("props", null);
            if (propsValue.Type == ValueType.Object && propsValue.AsObject() is JsonObject slotProps)
            {
                var nameValue = slotProps.Get("name", null);
                if (nameValue.Type == ValueType.String && nameValue.AsString() == slotName)
                {
                    if (slotContent.Type == ValueType.Object && slotContent.AsObject() is JsonObject replacementNode)
                    {
                        return replacementNode;
                    }

                    var textNode = new JsonObject();
                    textNode.Set("type", RuntimeValue.String("text"));
                    var props = new JsonObject();
                    props.Set("value", RuntimeValue.String(slotContent.ToString()));
                    textNode.Set("props", RuntimeValue.Object(props));
                    textNode.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));
                    return textNode;
                }
            }
        }

        var cloned = new JsonObject();
        foreach (var kvp in root.GetProperties())
        {
            if (kvp.Key != "children")
            {
                cloned.Set(kvp.Key, kvp.Value);
                continue;
            }

            if (kvp.Value.Type != ValueType.Array)
            {
                cloned.Set(kvp.Key, kvp.Value);
                continue;
            }

            var nextChildren = new List<RuntimeValue>();
            foreach (var child in kvp.Value.AsArray())
            {
                if (child.Type == ValueType.Object && child.AsObject() is JsonObject childObj)
                {
                    nextChildren.Add(RuntimeValue.Object(ReplaceSlot(childObj, slotName, slotContent)));
                }
                else
                {
                    nextChildren.Add(child);
                }
            }

            cloned.Set("children", RuntimeValue.Array(nextChildren));
        }

        return cloned;
    }

    private static RuntimeValue BuiltInUiMount(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("uiMount() expects rootNode and optional sessionId");

        var root = UiNode.FromRuntimeValue(args[0]);
        var sessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        return session.Mount(root);
    }

    private static RuntimeValue BuiltInUiMountEnvelope(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new Exception("uiMountEnvelope() expects rootNode, optional sessionId, optional options");

        var root = UiNode.FromRuntimeValue(args[0]);
        var sessionId = args.Count >= 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";

        JsonObject? options = null;
        if (args.Count == 3)
        {
            options = CoerceRuntimeObjectToJsonObject(args[2]);
            if (options == null)
                throw new Exception("uiMountEnvelope() options must be an object when provided");
        }

        var maxPatchCount = options == null ? 1024 : GetIntegerOptionFromObject(options, "maxPatchCount", 1024);
        var maxEventQueueDepth = options == null ? 1024 : GetIntegerOptionFromObject(options, "maxEventQueueDepth", 1024);
        var sessionTtlMs = options == null ? 1800000 : GetIntegerOptionFromObject(options, "sessionTtlMs", 1800000);

        UiSessionRegistry.ConfigureTtl(TimeSpan.FromMilliseconds(Math.Max(60000, sessionTtlMs)));
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        session.MaxPatchCountPerEnvelope = Math.Max(1, maxPatchCount);
        session.ConfigureQueueDepth(maxEventQueueDepth);

        var mount = session.Mount(root);
        var envelope = new JsonObject();
        envelope.Set("status", RuntimeValue.Integer(200));
        envelope.Set("sessionId", RuntimeValue.String(sessionId));
        envelope.Set("mount", mount);
        envelope.Set("snapshot", session.SnapshotAsRuntimeValue());
        envelope.Set("resync", session.BuildResyncEnvelope());
        return RuntimeValue.Object(envelope);
    }

    private static RuntimeValue BuiltInUiRender(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("uiRender() expects rootNode and optional sessionId");

        var nextTree = UiNode.FromRuntimeValue(args[0]);
        var sessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        return session.Render(nextTree);
    }

    private static RuntimeValue BuiltInUiDispatchEvent(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3 || args[0].Type != ValueType.Object)
            throw new Exception("uiDispatchEvent() expects event object, optional sessionId, optional sequence");

        var sessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        var evtObject = args[0].AsObject() as JsonObject;
        if (evtObject == null)
            throw new Exception("uiDispatchEvent() event must be a json object");

        var eventType = evtObject.Get("type", null).Type == ValueType.String ? evtObject.Get("type", null).AsString() : "event";
        var targetPath = evtObject.Get("targetPath", null).Type == ValueType.String ? evtObject.Get("targetPath", null).AsString() : "/";
        var payload = evtObject.Get("payload", null);
        var sequence = args.Count == 3 && args[2].Type == ValueType.Integer ? args[2].AsInteger() : 0;
        if (sequence > 0 && !session.TryAcceptInboundSequence(sequence, out var reason))
        {
            return session.BuildNack(sequence, "InvalidSequence", reason ?? "invalid sequence");
        }

        session.EnqueueEvent(new UiEvent(eventType, targetPath, payload.Type == ValueType.Null ? RuntimeValue.Null() : payload));
        if (string.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase))
        {
            var errorComponentId = ResolveUiErrorComponentId(evtObject, payload, targetPath);
            if (!string.IsNullOrWhiteSpace(errorComponentId))
            {
                session.EmitLifecycleHook("onError", errorComponentId, payload);
            }
        }

        return sequence > 0 ? session.BuildAck(sequence) : RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiPullEvent(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("uiPullEvent() expects optional sessionId");

        var sessionId = args.Count == 1 && args[0].Type == ValueType.String ? args[0].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        if (session.TryDequeueEvent(out var uiEvent) && uiEvent != null)
        {
            return uiEvent.ToRuntimeValue();
        }

        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiState(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args.Count > 4 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("uiState() expects componentId, key, defaultValue, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        var key = args[1].AsString();
        var existing = HttpServerInstance.GetComponentState(componentId, key);
        if (existing.Type != ValueType.Null)
        {
            return existing;
        }

        HttpServerInstance.SetComponentState(componentId, key, args[2]);
        return args[2];
    }

    private static RuntimeValue BuiltInUiSetState(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args.Count > 4 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("uiSetState() expects componentId, key, value, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        HttpServerInstance.SetComponentState(componentId, args[1].AsString(), args[2]);
        return args[2];
    }

    private static RuntimeValue BuiltInUiInvalidate(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception("uiInvalidate() expects channel and optional payload");

        var channel = args[0].AsString();
        var payload = args.Count == 2 ? args[1] : RuntimeValue.Null();
        var message = new JsonObject();
        message.Set("type", RuntimeValue.String("invalidate"));
        message.Set("channel", RuntimeValue.String(channel));
        message.Set("payload", payload);
        HttpServerInstance.BroadcastSSEMessage(RuntimeValueToJson(RuntimeValue.Object(message)), channel);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiSessionId(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("uiSessionId() expects source and optional defaultSessionId");

        var defaultSessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var sessionId = ResolveSessionId(args[0]);
        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = defaultSessionId;
        return RuntimeValue.String(sessionId);
    }

    private static RuntimeValue BuiltInUiRedirectWithSession(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("uiRedirectWithSession() expects path, source, optional defaultSessionId");

        var path = args[0].AsString();
        var defaultSessionId = args.Count == 3 && args[2].Type == ValueType.String ? args[2].AsString() : "default";
        var sessionId = ResolveSessionId(args[1]);
        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = defaultSessionId;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var separator = path.Contains("?", StringComparison.Ordinal) ? "&" : "?";
            path = path + separator + "session=" + sessionId;
        }

        return BuiltInRedirectTo(new List<RuntimeValue> { RuntimeValue.String(path) });
    }

    private static RuntimeValue BuiltInUiGenerate(List<RuntimeValue> args, Interpreter? interpreter)
    {
        return BuiltInUiGenerateAsync(args, interpreter).GetAwaiter().GetResult();
    }

    private static async Task<RuntimeValue> BuiltInUiGenerateAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("uiGenerate() expects description and optional agent/cache arguments");

        var description = args[0].AsString();
        HTMLCacheInstance? cache = null;
        AgentInstance? agent = null;

        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.Object)
                throw new Exception("uiGenerate() optional argument #2 must be an Agent or HTMLCache object");

            var objectArg = args[1].AsObject();
            if (objectArg is AgentInstance agentInstance)
                agent = agentInstance;
            else if (objectArg is HTMLCacheInstance cacheInstance)
                cache = cacheInstance;
            else
                throw new Exception("uiGenerate() optional argument #2 must be an Agent or HTMLCache object");
        }

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Object)
                throw new Exception("uiGenerate() optional argument #3 must be an Agent or HTMLCache object");

            var objectArg = args[2].AsObject();
            if (objectArg is AgentInstance agentInstance)
            {
                if (agent != null)
                    throw new Exception("uiGenerate() received multiple Agent arguments");
                agent = agentInstance;
            }
            else if (objectArg is HTMLCacheInstance cacheInstance)
            {
                if (cache != null)
                    throw new Exception("uiGenerate() received multiple HTMLCache arguments");
                cache = cacheInstance;
            }
            else
            {
                throw new Exception("uiGenerate() optional argument #3 must be an Agent or HTMLCache object");
            }
        }

        var cacheKey = "ui:" + description;
        if (cache != null)
        {
            var cached = cache.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String(cacheKey) });
            if (cached.Type == ValueType.String)
            {
                var cachedTree = BuiltInParseJSON(new List<RuntimeValue> { cached });
                UiNode.FromRuntimeValue(cachedTree);
                return cachedTree;
            }
        }

        if (agent == null)
        {
            var defaultLlama = DefaultLocalLlm.GetDefaultLocalClient();
            agent = new AgentInstance();
            agent.Initialize(
                "UiTreeGenerator",
                "UI tree designer",
                "You output valid MALDA server UI trees as JSON only.",
                null,
                defaultLlama,
                null,
                null
            );
        }

        var prompt = BuildUiGeneratePrompt(description);
        var response = await Task.Run(() => agent.Think(RuntimeValue.String(prompt)));
        var responseText = ExtractAgentContentAsString(response);
        var jsonPayload = ExtractJsonObjectPayload(responseText);
        RuntimeValue parsedTree;
        try
        {
            parsedTree = BuiltInParseJSON(new List<RuntimeValue> { RuntimeValue.String(jsonPayload) });
        }
        catch (Exception ex)
        {
            throw new Exception("ui.generate could not parse agent response as JSON: " + ex.Message);
        }

        UiNode.FromRuntimeValue(parsedTree);

        if (cache != null)
        {
            var serialized = BuiltInToJSON(new List<RuntimeValue> { parsedTree });
            if (serialized.Type == ValueType.String)
            {
                cache.CallMethod("set", new List<RuntimeValue>
                {
                    RuntimeValue.String(cacheKey),
                    serialized
                });
            }
        }

        return parsedTree;
    }

    private static string BuildUiGeneratePrompt(string description)
    {
        return string.Join(
            "\n",
            "Generate a MALDA server UI tree as JSON.",
            "Output requirements:",
            "- Return ONLY one JSON object.",
            "- Root and every child must be: {\"type\": string, \"props\": object, \"children\": array}.",
            "- Optional \"key\" is allowed.",
            "- Do not include markdown, comments, or explanations.",
            "- Use only these control types:",
            "  row,column,stack,spacer,panel,drawer,tabs,accordion,breadcrumbs,text,heading,image,icon,button,textField,textArea,checkbox,switch,select,radioGroup,slider,form,field,list,table,dataGrid,treeView,paginator,emptyState,badge,alert,progress,modal,toast,skeleton,spinner,errorBoundary,slot,when,choose,each",
            "- Prefer event names supported by MALDA: onClick,onChange,onInput,onSubmit,onClose,onFocus,onBlur,onRowClick,onSelectionChange,onSort,onFilter,onPageChange,onViewportChange,onNodeSelect,onNodeToggle,onNodeExpand,onNodeCollapse,onNodeActivate,onLoadChildren,onDragStart,onDragOver,onDrop,onDragEnd.",
            "",
            "Requested UI:",
            description
        );
    }

    private static string ExtractAgentContentAsString(RuntimeValue response)
    {
        if (response.Type == ValueType.String)
        {
            return response.AsString();
        }

        if (response.Type == ValueType.Object)
        {
            var responseObject = response.AsObject();
            if (responseObject is JsonObject jsonObj)
            {
                var content = jsonObj.Get("content", null);
                if (content.Type == ValueType.String)
                {
                    return content.AsString();
                }
            }
            else
            {
                try
                {
                    var content = responseObject.Get("content", null);
                    if (content.Type == ValueType.String)
                    {
                        return content.AsString();
                    }
                }
                catch
                {
                    // Fall back to string conversion below.
                }
            }
        }

        return response.ToString();
    }

    private static string ExtractJsonObjectPayload(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new Exception("ui.generate received an empty agent response");
        }

        var codeFencePattern = @"```json\s*(.*?)\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            codeFencePattern,
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return trimmed;
        }

        throw new Exception("ui.generate expected a JSON object response");
    }

    private static string ResolveSessionId(RuntimeValue source)
    {
        if (source.Type == ValueType.String)
        {
            return source.AsString();
        }

        var sourceObj = CoerceRuntimeObjectToJsonObject(source);
        if (sourceObj == null)
        {
            return string.Empty;
        }

        var directSession = GetStringValueFromObject(sourceObj, "session", "");
        if (!string.IsNullOrWhiteSpace(directSession))
        {
            return directSession;
        }

        var queryValue = sourceObj.Get("query", null);
        if (queryValue.Type == ValueType.Object && queryValue.AsObject() is JsonObject queryObject)
        {
            var querySession = GetStringValueFromObject(queryObject, "session", "");
            if (!string.IsNullOrWhiteSpace(querySession))
            {
                return querySession;
            }
        }

        var bodyValue = sourceObj.Get("body", null);
        if (bodyValue.Type == ValueType.Object && bodyValue.AsObject() is JsonObject bodyObject)
        {
            var bodySession = GetStringValueFromObject(bodyObject, "session", "");
            if (!string.IsNullOrWhiteSpace(bodySession))
            {
                return bodySession;
            }
        }

        return string.Empty;
    }

    private static RuntimeValue BuiltInUiOnInit(List<RuntimeValue> args) => RegisterUiHook(args, "onInit");
    private static RuntimeValue BuiltInUiOnPreRender(List<RuntimeValue> args) => RegisterUiHook(args, "onPreRender");
    private static RuntimeValue BuiltInUiOnLoad(List<RuntimeValue> args) => RegisterUiHook(args, "onLoad");
    private static RuntimeValue BuiltInUiOnDispose(List<RuntimeValue> args) => RegisterUiHook(args, "onDispose");
    private static RuntimeValue BuiltInUiOnMount(List<RuntimeValue> args) => RegisterUiHook(args, "onMount");
    private static RuntimeValue BuiltInUiOnUpdate(List<RuntimeValue> args) => RegisterUiHook(args, "onUpdate");
    private static RuntimeValue BuiltInUiOnUnmount(List<RuntimeValue> args) => RegisterUiHook(args, "onUnmount");
    private static RuntimeValue BuiltInUiOnError(List<RuntimeValue> args) => RegisterUiHook(args, "onError");

    private static RuntimeValue RegisterUiHook(List<RuntimeValue> args, string hookName)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception($"{hookName}() expects componentId and optional sessionId");

        var componentId = args[0].AsString();
        var sessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        session.RegisterLifecycleHook(hookName, componentId);
        return RuntimeValue.Null();
    }

    private static string? ResolveUiErrorComponentId(JsonObject eventObject, RuntimeValue payload, string targetPath)
    {
        if (TryGetStringProperty(eventObject, "componentId", out var eventComponentId))
        {
            return eventComponentId;
        }

        if (payload.Type == ValueType.Object &&
            payload.AsObject() is JsonObject payloadObject &&
            TryGetStringProperty(payloadObject, "componentId", out var payloadComponentId))
        {
            return payloadComponentId;
        }

        if (string.IsNullOrWhiteSpace(targetPath) || targetPath == "/")
        {
            return null;
        }

        var trimmedPath = targetPath.Trim('/');
        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            return null;
        }

        var separatorIndex = trimmedPath.LastIndexOf('/');
        return separatorIndex >= 0 ? trimmedPath[(separatorIndex + 1)..] : trimmedPath;
    }

    private static bool TryGetStringProperty(JsonObject obj, string propertyName, out string value)
    {
        value = string.Empty;
        var property = obj.Get(propertyName, null);
        if (property.Type != ValueType.String)
        {
            return false;
        }

        value = property.AsString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static RuntimeValue BuiltInUiConfigure(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.Integer)
            throw new Exception("uiConfigure() expects settingName and integer value");

        var setting = args[0].AsString();
        var value = args[1].AsInteger();

        if (setting == "sessionTtlMs")
        {
            UiSessionRegistry.ConfigureTtl(TimeSpan.FromMilliseconds(Math.Max(60000, value)));
            return RuntimeValue.Null();
        }

        var sessionId = args.Count == 3 && args[2].Type == ValueType.String ? args[2].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        switch (setting)
        {
            case "maxPatchCount":
                session.MaxPatchCountPerEnvelope = Math.Max(1, value);
                return RuntimeValue.Null();
            case "maxPayloadBytes":
                session.MaxPayloadSizeBytes = Math.Max(1024, value);
                return RuntimeValue.Null();
            case "maxEventQueueDepth":
                session.ConfigureQueueDepth(value);
                return RuntimeValue.Null();
            default:
                throw new Exception($"Unknown uiConfigure setting: {setting}");
        }
    }

    private static RuntimeValue BuiltInUiSnapshot(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("uiSnapshot() expects optional sessionId");

        var sessionId = args.Count == 1 && args[0].Type == ValueType.String ? args[0].AsString() : "default";
        return UiSessionRegistry.GetOrCreate(sessionId).SnapshotAsRuntimeValue();
    }

    private static RuntimeValue BuiltInUiResync(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("uiResync() expects optional sessionId");

        var sessionId = args.Count == 1 && args[0].Type == ValueType.String ? args[0].AsString() : "default";
        return UiSessionRegistry.GetOrCreate(sessionId).BuildResyncEnvelope();
    }

    private static string ResolveUiTemplateSource(string sourceOrTemplate, bool useCache)
    {
        if (string.IsNullOrEmpty(sourceOrTemplate))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.IsPathRooted(sourceOrTemplate)
                ? Path.GetFullPath(sourceOrTemplate)
                : Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrTemplate));
            if (!File.Exists(fullPath))
            {
                return sourceOrTemplate;
            }

            if (!useCache)
            {
                return File.ReadAllText(fullPath);
            }

            lock (UiTemplateCacheLock)
            {
                if (UiTemplateCache.TryGetValue(fullPath, out var cached))
                {
                    return cached;
                }

                var content = File.ReadAllText(fullPath);
                UiTemplateCache[fullPath] = content;
                return content;
            }
        }
        catch
        {
            return sourceOrTemplate;
        }
    }

    private static JsonObject? CoerceRuntimeObjectToJsonObject(RuntimeValue value)
    {
        if (value.Type != ValueType.Object)
        {
            return null;
        }

        var obj = value.AsObject();
        if (obj is JsonObject jsonObject)
        {
            return jsonObject;
        }

        if (obj is DictionaryInstance dict)
        {
            var mapped = new JsonObject();
            foreach (var kvp in dict.GetEntries())
            {
                mapped.Set(kvp.Key, kvp.Value);
            }

            return mapped;
        }

        return null;
    }

    private static bool GetBooleanOptionFromObject(JsonObject options, string key, bool defaultValue)
    {
        var optionValue = options.Get(key, null);
        if (optionValue == null || optionValue.Type == ValueType.Null)
        {
            return defaultValue;
        }

        return optionValue.Type switch
        {
            ValueType.Boolean => optionValue.AsBoolean(),
            ValueType.Integer => optionValue.AsInteger() != 0,
            ValueType.String when bool.TryParse(optionValue.AsString(), out var parsed) => parsed,
            ValueType.String when int.TryParse(optionValue.AsString(), out var parsedInt) => parsedInt != 0,
            _ => defaultValue
        };
    }

    private static string GetStringOptionFromObject(JsonObject options, string key, string defaultValue)
    {
        var optionValue = options.Get(key, null);
        if (optionValue == null || optionValue.Type == ValueType.Null)
        {
            return defaultValue;
        }

        var text = optionValue.Type == ValueType.String ? optionValue.AsString() : optionValue.ToString();
        return string.IsNullOrEmpty(text) ? defaultValue : text;
    }

    private static int GetIntegerOptionFromObject(JsonObject options, string key, int defaultValue)
    {
        var optionValue = options.Get(key, null);
        if (optionValue == null || optionValue.Type == ValueType.Null)
        {
            return defaultValue;
        }

        return optionValue.Type switch
        {
            ValueType.Integer => optionValue.AsInteger(),
            ValueType.Boolean => optionValue.AsBoolean() ? 1 : 0,
            ValueType.String when int.TryParse(optionValue.AsString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static string RuntimeValueToTemplateString(RuntimeValue value)
    {
        return value.Type == ValueType.String ? value.AsString() : RuntimeValueToJson(value);
    }

    private static string RenderTemplateTokenString(string template, JsonObject? modelObj)
    {
        if (modelObj == null)
        {
            return template;
        }

        var rendered = template;
        foreach (var kvp in modelObj.GetProperties())
        {
            var token = "{{" + kvp.Key + "}}";
            rendered = rendered.Replace(token, RuntimeValueToTemplateString(kvp.Value), StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string RenderUiTemplateString(string template, JsonObject? modelObj, bool compatRaw, HashSet<string>? rawKeys = null)
    {
        return UiTemplateEngine.Render(template, modelObj, new UiTemplateRenderOptions
        {
            EscapeByDefault = true,
            CompatRaw = compatRaw,
            RawKeys = rawKeys,
            StringifyValue = RuntimeValueToTemplateString
        });
    }

    private static string ComposeScopedComponentId(string componentId, RuntimeValue scopeValue)
    {
        if (scopeValue.Type != ValueType.String)
        {
            return componentId;
        }

        var scope = scopeValue.AsString();
        if (string.IsNullOrWhiteSpace(scope))
        {
            return componentId;
        }

        return scope + "::" + componentId;
    }
    
    private static async Task<RuntimeValue> BuiltInGenerateUIAsync(
        List<RuntimeValue> args, 
        Interpreter interpreter)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("generateUI() expects at least 1 string argument");
        
        var description = args[0].AsString();
        HTMLCacheInstance? cache = null;
        AgentInstance? agent = null;
        
        // Parse optional arguments
        if (args.Count >= 2 && args[1].Type == ValueType.Object)
        {
            var cacheObj = args[1].AsObject();
            if (cacheObj is HTMLCacheInstance cacheInst)
                cache = cacheInst;
        }
        
        if (args.Count >= 3 && args[2].Type == ValueType.Object)
        {
            var agentObj = args[2].AsObject();
            if (agentObj is AgentInstance agentInst)
                agent = agentInst;
        }
        
        // Check cache first
        if (cache != null)
        {
            var cached = cache.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String(description) });
            if (cached.Type != ValueType.Null)
                return cached;
        }
        
        // Generate with agent (or create default)
        if (agent == null)
        {
            var defaultLlama = DefaultLocalLlm.GetDefaultLocalClient();
            agent = new AgentInstance();
            agent.Initialize(
                "UIGenerator",
                "UI designer",
                "You create beautiful, functional HTML forms. Always include proper form submission to /submit endpoint with POST method. Use modern CSS styling.",
                null,
                defaultLlama,
                null,
                null
            );
        }
        
        var response = agent.Think(RuntimeValue.String($"Create an HTML form: {description}"));
        
        // Extract content from response
        RuntimeValue contentValue;
        if (response.Type == ValueType.Object)
        {
            var responseObj = response.AsObject();
            if (responseObj is JsonObject jsonObj)
            {
                contentValue = jsonObj.Get("content", null) ?? RuntimeValue.String("");
            }
            else
            {
                // Try to get content property
                try
                {
                    contentValue = responseObj.Get("content", null) ?? RuntimeValue.String("");
                }
                catch
                {
                    contentValue = RuntimeValue.String("");
                }
            }
        }
        else
        {
            contentValue = RuntimeValue.String("");
        }
        
        var html = BuiltInExtractHTML(new List<RuntimeValue> { contentValue });
        
        // Cache if cache provided
        if (cache != null && html.Type == ValueType.String)
            cache.CallMethod("set", new List<RuntimeValue> { RuntimeValue.String(description), html });
        
        return html;
    }
    
    private static RuntimeValue BuiltInHttpGet(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpGet() expects at least 1 string argument: (url, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("get", args);
    }
    
    private static RuntimeValue BuiltInHttpPost(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpPost() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("post", args);
    }
    
    private static RuntimeValue BuiltInHttpPut(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpPut() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("put", args);
    }
    
    private static RuntimeValue BuiltInHttpDelete(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpDelete() expects at least 1 string argument: (url, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("delete", args);
    }
    
    private static RuntimeValue BuiltInHttpPatch(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpPatch() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("patch", args);
    }

    private static RuntimeValue BuiltInHttpBearerToken(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("httpBearerToken() expects 1 argument: (request)");

        if (!TryGetObjectPropertyValue(args[0], "headers", out var headersValue))
            return RuntimeValue.String("");

        var authHeader = GetCaseInsensitiveStringValue(headersValue, "Authorization");
        if (string.IsNullOrWhiteSpace(authHeader))
            return RuntimeValue.String("");

        var pieces = authHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length < 2 || !string.Equals(pieces[0], "bearer", StringComparison.OrdinalIgnoreCase))
            return RuntimeValue.String("");

        return RuntimeValue.String(pieces[1]);
    }

    private static RuntimeValue BuiltInHttpCookieToken(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[1].Type != ValueType.String)
            throw new Exception("httpCookieToken() expects request, cookieName, optional cookieSecret");

        var cookieName = args[1].AsString();
        if (string.IsNullOrWhiteSpace(cookieName))
            return RuntimeValue.String("");

        if (!TryGetObjectPropertyValue(args[0], "cookies", out var cookiesValue))
            return RuntimeValue.String("");

        var rawCookie = GetCaseInsensitiveStringValue(cookiesValue, cookieName);
        if (string.IsNullOrWhiteSpace(rawCookie))
            return RuntimeValue.String("");

        var decoded = BuiltInUrlDecode(new List<RuntimeValue> { RuntimeValue.String(rawCookie) });
        var decodedText = decoded.Type == ValueType.String ? decoded.AsString() : rawCookie;
        if (args.Count == 3 && args[2].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[2].AsString()))
        {
            var secureValue = BuiltInReadSecureCookie(new List<RuntimeValue>
            {
                RuntimeValue.String(decodedText),
                args[2]
            });

            if (secureValue.Type == ValueType.Null)
                return RuntimeValue.String("");

            return RuntimeValue.String(secureValue.ToString());
        }

        return RuntimeValue.String(decodedText);
    }

    private static RuntimeValue BuiltInHttpAuthToken(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("httpAuthToken() expects request and optional options object");

        var options = args.Count == 2 ? CoerceRuntimeObjectToJsonObject(args[1]) : null;
        var allowBearer = options == null || GetBooleanOptionFromObject(options, "allowBearer", true);
        var allowCookie = options == null || GetBooleanOptionFromObject(options, "allowCookie", true);
        var allowBody = options == null || GetBooleanOptionFromObject(options, "allowBody", true);
        var cookieName = options == null ? string.Empty : GetStringOptionFromObject(options, "cookieName", string.Empty);
        var cookieSecret = options == null ? string.Empty : GetStringOptionFromObject(options, "cookieSecret", string.Empty);
        var bodyKey = options == null ? "auth" : GetStringOptionFromObject(options, "bodyKey", "auth");

        if (allowBearer)
        {
            var token = BuiltInHttpBearerToken(new List<RuntimeValue> { args[0] });
            if (token.Type == ValueType.String && !string.IsNullOrWhiteSpace(token.AsString()))
                return token;
        }

        if (allowCookie && !string.IsNullOrWhiteSpace(cookieName))
        {
            var cookieArgs = new List<RuntimeValue> { args[0], RuntimeValue.String(cookieName) };
            if (!string.IsNullOrWhiteSpace(cookieSecret))
                cookieArgs.Add(RuntimeValue.String(cookieSecret));
            var token = BuiltInHttpCookieToken(cookieArgs);
            if (token.Type == ValueType.String && !string.IsNullOrWhiteSpace(token.AsString()))
                return token;
        }

        if (allowBody)
        {
            if (TryGetObjectPropertyValue(args[0], "body", out var bodyValue))
            {
                var bodyToken = GetCaseInsensitiveStringValue(bodyValue, bodyKey);
                if (!string.IsNullOrWhiteSpace(bodyToken))
                    return RuntimeValue.String(bodyToken);
            }            
        }

        return RuntimeValue.String("");
    }

    private static bool TryGetObjectPropertyValue(RuntimeValue source, string key, out RuntimeValue value)
    {
        value = RuntimeValue.Null();
        if (source.Type != ValueType.Object)
            return false;

        var obj = source.AsObject();
        if (obj is JsonObject jsonObj)
        {
            var direct = jsonObj.Get(key, null);
            if (direct.Type != ValueType.Null)
            {
                value = direct;
                return true;
            }

            foreach (var kvp in jsonObj.GetProperties())
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        if (obj is DictionaryInstance dict)
        {
            if (dict.TryGetEntry(key, out var direct))
            {
                value = direct;
                return true;
            }

            foreach (var kvp in dict.GetEntries())
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        try
        {
            var direct = obj.Get(key, null);
            if (direct.Type != ValueType.Null)
            {
                value = direct;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string GetCaseInsensitiveStringValue(RuntimeValue source, string key)
    {
        if (!TryGetObjectPropertyValue(source, key, out var value) || value.Type == ValueType.Null)
            return string.Empty;

        return value.Type == ValueType.String ? value.AsString() : value.ToString();
    }
    
    private static RuntimeValue BuiltInWebSearch(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("webSearch() expects at least 1 string argument: (query, apiKey?)");
        
        var query = args[0].AsString();
        string? apiKey = null;
        if (args.Count > 1 && args[1].Type == ValueType.String)
            apiKey = args[1].AsString();
        if (string.IsNullOrEmpty(apiKey))
            apiKey = System.Environment.GetEnvironmentVariable("BRAVE_SEARCH_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            var configVal = BuiltInGetMaldaConfig(new List<RuntimeValue>());
            if (configVal.Type == ValueType.Object)
            {
                var cfg = configVal.AsObject();
                try
                {
                    var tools = cfg.Get("tools", null);
                    if (tools?.Type == ValueType.Object)
                    {
                        var web = tools.AsObject().Get("web", null);
                        if (web?.Type == ValueType.Object)
                        {
                            var search = web.AsObject().Get("search", null);
                            if (search?.Type == ValueType.Object)
                            {
                                var keyVal = search.AsObject().Get("apiKey", null);
                                if (keyVal?.Type == ValueType.String && !string.IsNullOrWhiteSpace(keyVal.AsString()))
                                    apiKey = keyVal.AsString();
                            }
                        }
                    }
                }
                catch { }
            }
        }
        if (string.IsNullOrEmpty(apiKey))
            throw new Exception("webSearch() requires apiKey argument, BRAVE_SEARCH_API_KEY, or tools.web.search.apiKey in ~/.malda/config.json");
        
        var url = "https://api.search.brave.com/res/v1/web/search?q=" + Uri.EscapeDataString(query);
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Subscription-Token", apiKey);
            var response = httpClient.Send(request);
            var responseContent = response.Content.ReadAsStringAsync().Result;
            
            if (!response.IsSuccessStatusCode)
            {
                var errorObj = new JsonObject();
                errorObj.Set("ok", RuntimeValue.Boolean(false));
                errorObj.Set("error", RuntimeValue.String($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"));
                return RuntimeValue.Object(errorObj);
            }
            
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            
            var resultObj = new JsonObject();
            resultObj.Set("ok", RuntimeValue.Boolean(true));
            
            var resultsList = new List<RuntimeValue>();
            if (root.TryGetProperty("web", out var webEl) &&
                webEl.ValueKind == JsonValueKind.Object &&
                webEl.TryGetProperty("results", out var resultsEl) &&
                resultsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsEl.EnumerateArray())
                {
                    var itemObj = new JsonObject();
                    itemObj.Set("title", RuntimeValue.String(item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""));
                    itemObj.Set("url", RuntimeValue.String(item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : ""));
                    itemObj.Set("description", RuntimeValue.String(item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""));
                    resultsList.Add(RuntimeValue.Object(itemObj));
                }
            }
            resultObj.Set("results", RuntimeValue.Array(resultsList));
            
            var moreAvailable = false;
            if (root.TryGetProperty("query", out var queryEl) &&
                queryEl.ValueKind == JsonValueKind.Object &&
                queryEl.TryGetProperty("more_results_available", out var moreEl))
                moreAvailable = moreEl.ValueKind == JsonValueKind.True;
            resultObj.Set("moreResultsAvailable", RuntimeValue.Boolean(moreAvailable));
            
            return RuntimeValue.Object(resultObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("ok", RuntimeValue.Boolean(false));
            errorObj.Set("error", RuntimeValue.String(ex.Message));
            return RuntimeValue.Object(errorObj);
        }
    }
    
    // Spectre.Console functions
    private static RuntimeValue BuiltInSpectreConsoleMarkup(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("markup() expects 1 string argument");

        WriteSpectreMarkup(args[0].AsString(), appendNewLine: false);
        return RuntimeValue.Null();
    }

    /// <summary>
    /// Renders Spectre markup when valid; otherwise writes the text literally.
    /// Dynamic paths/errors often contain <c>[...]</c> that must not abort the program.
    /// </summary>
    public static void WriteSpectreMarkup(string text, bool appendNewLine)
    {
        if (IsParseableSpectreMarkup(text))
        {
            if (appendNewLine)
                AnsiConsole.MarkupLine(text);
            else
                AnsiConsole.Markup(text);
            return;
        }

        if (appendNewLine)
            AnsiConsole.WriteLine(text);
        else
            AnsiConsole.Write(text);
    }
    
    public static RuntimeValue BuiltInSpectreConsoleTable(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("table() expects at least 1 argument: (data, title?, columns?)");
        
        var table = new Table();
        
        // Parse data (array of objects or array of arrays)
        if (args[0].Type != ValueType.Array)
            throw new Exception("table() first argument must be an array");
        
        var dataArray = args[0].AsArray();
        
        // Optional title
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            table.Title = new TableTitle(args[1].AsString());
        }
        
        // Optional columns (array of strings)
        List<string>? columnNames = null;
        if (args.Count > 2 && args[2].Type == ValueType.Array)
        {
            var colsArray = args[2].AsArray();
            columnNames = new List<string>();
            foreach (var col in colsArray)
            {
                if (col.Type == ValueType.String)
                    columnNames.Add(col.AsString());
            }
        }
        
        // Determine structure from first row
        if (dataArray.Count == 0)
        {
            AnsiConsole.Write(table);
            return RuntimeValue.Null();
        }
        
        var firstRow = dataArray[0];
        if (firstRow.Type == ValueType.Object)
        {
            var firstRowObj = firstRow.AsObject();
            if (firstRowObj is JsonObject firstJsonObj)
            {
                // Object array - use object keys as columns
                if (columnNames == null)
                {
                    columnNames = new List<string>();
                    foreach (var key in firstJsonObj.GetAllKeys())
                    {
                        columnNames.Add(key);
                        table.AddColumn(key);
                    }
                }
                else
                {
                    foreach (var col in columnNames)
                    {
                        table.AddColumn(col);
                    }
                }
                
                // Add rows
                foreach (var row in dataArray)
                {
                    if (row.Type == ValueType.Object && row.AsObject() is JsonObject rowObj)
                    {
                        var rowValues = new List<string>();
                        foreach (var col in columnNames)
                        {
                            var value = rowObj.Get(col, null);
                            rowValues.Add(value?.ToString() ?? "");
                        }
                        table.AddRow(rowValues.ToArray());
                    }
                }
            }
        }
        else if (firstRow.Type == ValueType.Array)
        {
            // Array of arrays
            var firstArray = firstRow.AsArray();
            if (columnNames == null)
            {
                // Use first row as headers
                var headers = new List<string>();
                foreach (var item in firstArray)
                {
                    headers.Add(item.ToString());
                }
                foreach (var header in headers)
                {
                    table.AddColumn(header);
                }
                
                // Add remaining rows
                for (int i = 1; i < dataArray.Count; i++)
                {
                    var row = dataArray[i];
                    if (row.Type == ValueType.Array)
                    {
                        var rowArray = row.AsArray();
                        var rowValues = new List<string>();
                        foreach (var item in rowArray)
                        {
                            rowValues.Add(item.ToString());
                        }
                        table.AddRow(rowValues.ToArray());
                    }
                }
            }
            else
            {
                // Use provided column names
                foreach (var col in columnNames)
                {
                    table.AddColumn(col);
                }
                
                // Add all rows
                foreach (var row in dataArray)
                {
                    if (row.Type == ValueType.Array)
                    {
                        var rowArray = row.AsArray();
                        var rowValues = new List<string>();
                        foreach (var item in rowArray)
                        {
                            rowValues.Add(item.ToString());
                        }
                        table.AddRow(rowValues.ToArray());
                    }
                }
            }
        }
        
        AnsiConsole.Write(table);
        return RuntimeValue.Null();
    }
    
    private static string EscapeSpectrePlainText(string text)
    {
        return text.Replace("[", "[[", StringComparison.Ordinal)
            .Replace("]", "]]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Spectre throws on malformed markup, and panels have always accepted arbitrary text
    /// (JSON, code, error dumps). Content that does not parse is rendered literally rather
    /// than aborting the program.
    /// </summary>
    private static bool IsParseableSpectreMarkup(string text)
    {
        try
        {
            _ = new Markup(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
    
    public static RuntimeValue BuiltInSpectreConsolePanel(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("panel() expects at least 1 string argument: (content, title?, borderStyle?)");
        
        var content = args[0].AsString();
        IRenderable body = IsParseableSpectreMarkup(content)
            ? new Markup(content)
            : new Text(content);
        var panel = new Panel(body);
        
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            var header = args[1].AsString();
            panel.Header = new PanelHeader(IsParseableSpectreMarkup(header)
                ? header
                : EscapeSpectrePlainText(header));
        }
        
        if (args.Count > 2 && args[2].Type == ValueType.String)
        {
            var borderStyle = args[2].AsString().ToLower();
            panel.Border = borderStyle switch
            {
                "rounded" => BoxBorder.Rounded,
                "double" => BoxBorder.Double,
                "heavy" => BoxBorder.Heavy,
                _ => BoxBorder.Square
            };
        }
        
        AnsiConsole.Write(panel);
        return RuntimeValue.Null();
    }
    
    public static RuntimeValue BuiltInSpectreConsoleTree(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("tree() expects at least 1 string argument: (label, items?)");
        
        var label = args[0].AsString();
        var tree = new Tree(label);
        
        if (args.Count > 1 && args[1].Type == ValueType.Array)
        {
            var itemsArray = args[1].AsArray();
            foreach (var item in itemsArray)
            {
                if (item.Type == ValueType.String)
                {
                    tree.AddNode(item.AsString());
                }
                else if (item.Type == ValueType.Object && item.AsObject() is JsonObject itemObj)
                {
                    // Object with label and children
                    var itemLabelValue = itemObj.Get("label", null);
                    var itemLabel = itemLabelValue != null && itemLabelValue.Type == ValueType.String 
                        ? itemLabelValue.AsString() 
                        : "";
                    var node = tree.AddNode(itemLabel);
                    
                    var children = itemObj.Get("children", null);
                    if (children != null && children.Type == ValueType.Array)
                    {
                        var childrenArray = children.AsArray();
                        foreach (var child in childrenArray)
                        {
                            if (child.Type == ValueType.String)
                            {
                                node.AddNode(child.AsString());
                            }
                        }
                    }
                }
            }
        }
        
        AnsiConsole.Write(tree);
        return RuntimeValue.Null();
    }
    
    public static async Task<RuntimeValue> BuiltInSpectreConsoleStatusAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("status() expects at least 1 string argument: (message, action?)");
        
        var message = args[0].AsString();
        
        if (args.Count > 1 && args[1].Type == ValueType.Function)
        {
            // action is a function/callback
            await AnsiConsole.Status()
                .StartAsync(message, async ctx =>
                {
                    // Call the action function
                    var funcValue = args[1].AsFunction();
                    await interpreter.CallFunctionAsync(funcValue, new List<RuntimeValue>());
                });
        }
        else
        {
            AnsiConsole.Status().Start(message, ctx => { });
        }
        
        return RuntimeValue.Null();
    }
    
    public static async Task<RuntimeValue> BuiltInSpectreConsolePromptAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1 || args[0].Type != ValueType.Object)
            throw new Exception("prompt() expects at least 1 object argument: (type, message, defaultValue?)");
        
        var promptObj = args[0].AsObject();
        if (promptObj is not JsonObject promptJson)
            throw new Exception("prompt() first argument must be an object");
        
        var typeValue = promptJson.Get("type", null);
        var type = typeValue != null && typeValue.Type == ValueType.String 
            ? typeValue.AsString().ToLower() 
            : "text";
        var messageValue = promptJson.Get("message", null);
        var message = messageValue != null && messageValue.Type == ValueType.String 
            ? messageValue.AsString() 
            : "";
        var defaultValue = promptJson.Get("defaultValue", null);
        
        string result;
        
        switch (type)
        {
            case "text":
            case "input":
                if (defaultValue != null && defaultValue.Type == ValueType.String)
                {
                    result = AnsiConsole.Prompt(new TextPrompt<string>(message).DefaultValue(defaultValue.AsString()));
                }
                else
                {
                    result = AnsiConsole.Prompt(new TextPrompt<string>(message));
                }
                break;
                
            case "confirm":
            case "yesno":
                var defaultBool = defaultValue != null && defaultValue.Type == ValueType.Boolean && defaultValue.AsBoolean();
                var confirmed = AnsiConsole.Confirm(message, defaultBool);
                return RuntimeValue.Boolean(confirmed);
                
            case "selection":
            case "choice":
                var choicesValue = promptJson.Get("choices", null);
                if (choicesValue == null || choicesValue.Type != ValueType.Array)
                    throw new Exception("prompt() with type 'selection' requires 'choices' array");
                var choices = choicesValue;
                
                var choicesArray = choices.AsArray();
                var choiceList = new List<string>();
                foreach (var choice in choicesArray)
                {
                    if (choice.Type == ValueType.String)
                        choiceList.Add(choice.AsString());
                }
                
                var selected = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .Title(message)
                    .AddChoices(choiceList));
                return RuntimeValue.String(selected);
                
            default:
                throw new Exception($"Unknown prompt type: {type}. Supported: text, confirm, selection");
        }
        
        return RuntimeValue.String(result);
    }
    
    public static async Task<RuntimeValue> BuiltInSpectreConsoleProgressAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1)
            throw new Exception("progress() expects at least 1 argument");
        
        // Check if the first argument is a function (new callback syntax)
        if (args[0].Type == ValueType.Function)
        {
            if (interpreter == null)
                throw new Exception("progress() with callback function requires an interpreter.");
            
            var callbackFunc = args[0].AsFunction();
            
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var taskDict = new Dictionary<string, ProgressTask>();
                    var progressCtx = new ProgressContextWrapper(ctx, taskDict);
                    var progressCtxValue = RuntimeValue.Object(progressCtx);
                    
                    await interpreter.CallFunctionAsync(callbackFunc, new List<RuntimeValue> { progressCtxValue });
                });
            
            return RuntimeValue.Null();
        }
        
        // Old syntax: object with tasks array and optional action callback
        if (args[0].Type != ValueType.Object)
            throw new Exception("progress() expects either a function callback or an object with 'tasks' array");
        
        var progressObj = args[0].AsObject();
        if (progressObj is not JsonObject progressJson)
            throw new Exception("progress() first argument must be an object");
        
        var tasks = progressJson.Get("tasks", null);
        if (tasks == null || tasks.Type != ValueType.Array)
            throw new Exception("progress() requires 'tasks' array");
        
        var tasksArray = tasks.AsArray();
        var action = progressJson.Get("action", null);
        
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var progressTasks = new List<ProgressTask>();
                
                // Create progress tasks
                foreach (var task in tasksArray)
                {
                    if (task.Type == ValueType.Object && task.AsObject() is JsonObject taskObj)
                    {
                        var taskNameValue = taskObj.Get("name", null);
                        var taskName = taskNameValue != null && taskNameValue.Type == ValueType.String 
                            ? taskNameValue.AsString() 
                            : "";
                        var maxValueValue = taskObj.Get("maxValue", null);
                        var maxValue = maxValueValue != null && maxValueValue.Type == ValueType.Integer 
                            ? maxValueValue.AsInteger() 
                            : (maxValueValue != null && maxValueValue.Type == ValueType.Float 
                                ? (int)maxValueValue.AsFloat() 
                                : 100);
                        var progressTask = ctx.AddTask(taskName, maxValue: maxValue);
                        progressTasks.Add(progressTask);
                    }
                }
                
                // Execute action if provided
                if (action != null && action.Type == ValueType.Function)
                {
                    if (interpreter == null)
                        throw new Exception("progress() with action callback requires an interpreter. In transpiled code, callbacks are not yet supported.");
                    
                    var funcValue = action.AsFunction();
                    
                    // Create a dictionary mapping task names to ProgressTask instances
                    var taskDict = new Dictionary<string, ProgressTask>();
                    for (int i = 0; i < progressTasks.Count; i++)
                    {
                        var taskName = $"Task{i}";
                        if (i < tasksArray.Count && tasksArray[i].Type == ValueType.Object && tasksArray[i].AsObject() is JsonObject t)
                        {
                            var nameValue = t.Get("name", null);
                            if (nameValue != null && nameValue.Type == ValueType.String)
                            {
                                taskName = nameValue.AsString();
                            }
                        }
                        taskDict[taskName] = progressTasks[i];
                    }
                    
                    // Create a wrapper object that allows reading and updating progress
                    var progressWrapper = new ProgressWrapperObjectInstance(taskDict);
                    var progressWrapperValue = RuntimeValue.Object(progressWrapper);
                    
                    await interpreter.CallFunctionAsync(funcValue, new List<RuntimeValue> { progressWrapperValue });
                }
            });
        
        return RuntimeValue.Null();
    }
    
    // ============================================
    // Text Embedding Functions
    // ============================================
    
    // Helper: Read text from string or file path
    private static string ReadTextOrFile(RuntimeValue textOrPath)
    {
        if (textOrPath.Type == ValueType.Array)
        {
            // If array, concatenate all strings
            var array = textOrPath.AsArray();
            var sb = new StringBuilder();
            foreach (var item in array)
            {
                if (item.Type == ValueType.String)
                {
                    var text = ReadTextOrFile(item);
                    sb.Append(text);
                    sb.Append(" ");
                }
            }
            return sb.ToString().Trim();
        }
        
        if (textOrPath.Type != ValueType.String)
            throw new Exception("embed functions expect string or array of strings");
        
        var str = textOrPath.AsString();
        
        // Check if it looks like a file path
        if (str.Contains(Path.DirectorySeparatorChar) || str.Contains(Path.AltDirectorySeparatorChar) ||
            str.Contains("/") || str.Contains("\\") ||
            str.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            // Try to read as file
            if (File.Exists(str))
            {
                try
                {
                    return File.ReadAllText(str);
                }
                catch
                {
                    // If file read fails, treat as text
                    return str;
                }
            }
        }
        
        return str;
    }
    
    // Helper: Tokenize text into words
    private static List<string> TokenizeText(string text)
    {
        var words = new List<string>();
        var currentWord = new StringBuilder();
        
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
            {
                currentWord.Append(char.ToLowerInvariant(c));
            }
            else
            {
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
        }
        
        if (currentWord.Length > 0)
        {
            words.Add(currentWord.ToString());
        }
        
        return words;
    }
    
    // Helper: Generate character n-grams
    private static List<string> GenerateNGrams(string text, int n)
    {
        var ngrams = new List<string>();
        if (string.IsNullOrEmpty(text) || n <= 0)
            return ngrams;
        
        text = text.ToLowerInvariant();
        for (int i = 0; i <= text.Length - n; i++)
        {
            ngrams.Add(text.Substring(i, n));
        }
        
        return ngrams;
    }
    
    // Helper: FNV-1a hash function for feature hashing
    private static uint FNV1aHash(string input)
    {
        const uint FNV_OFFSET_BASIS = 2166136261u;
        const uint FNV_PRIME = 16777619u;
        
        uint hash = FNV_OFFSET_BASIS;
        foreach (byte b in Encoding.UTF8.GetBytes(input))
        {
            hash ^= b;
            hash *= FNV_PRIME;
        }
        
        return hash;
    }
    
    // Helper: Hash feature to dimension index
    private static int HashFeature(string feature, int dimension)
    {
        var hash = FNV1aHash(feature);
        return (int)(hash % (uint)dimension);
    }
    
    // Helper: L2 normalize vector
    private static List<double> NormalizeVector(List<double> vector)
    {
        double norm = 0.0;
        foreach (var val in vector)
        {
            norm += val * val;
        }
        
        norm = Math.Sqrt(norm);
        
        if (norm == 0.0)
            return vector; // Zero vector stays zero
        
        var normalized = new List<double>();
        foreach (var val in vector)
        {
            normalized.Add(val / norm);
        }
        
        return normalized;
    }
    
    // embedBagOfWords: Bag-of-words with word frequencies
    private static RuntimeValue BuiltInEmbedBagOfWords(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("embedBagOfWords() expects at least 1 argument (text, dimension?, vocabulary?)");
        
        var text = ReadTextOrFile(args[0]);
        int dimension = 1000;
        List<string>? vocabulary = null;
        
        if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            dimension = args[1].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedBagOfWords() dimension must be greater than 0");
        }
        
        if (args.Count >= 3 && args[2].Type == ValueType.Array)
        {
            vocabulary = new List<string>();
            foreach (var item in args[2].AsArray())
            {
                if (item.Type == ValueType.String)
                    vocabulary.Add(item.AsString().ToLowerInvariant());
            }
        }
        
        var words = TokenizeText(text);
        var vector = new List<double>(new double[dimension]);
        
        if (vocabulary != null)
        {
            // Use exact vocabulary matching
            var vocabDict = new Dictionary<string, int>();
            for (int i = 0; i < vocabulary.Count && i < dimension; i++)
            {
                vocabDict[vocabulary[i]] = i;
            }
            
            foreach (var word in words)
            {
                if (vocabDict.TryGetValue(word, out int index))
                {
                    vector[index] += 1.0;
                }
            }
        }
        else
        {
            // Use feature hashing
            foreach (var word in words)
            {
                int index = HashFeature(word, dimension);
                vector[index] += 1.0;
            }
        }
        
        // Normalize
        vector = NormalizeVector(vector);
        
        var result = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            result.Add(RuntimeValue.Float(val));
        }
        
        return RuntimeValue.Array(result);
    }
    
    // embedCharacterNGrams: Character n-gram embeddings
    private static RuntimeValue BuiltInEmbedCharacterNGrams(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("embedCharacterNGrams() expects at least 1 argument (text, n?, dimension?)");
        
        var text = ReadTextOrFile(args[0]);
        int n = 3;
        int dimension = 1000;
        
        if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            n = args[1].AsInteger();
            if (n <= 0)
                throw new Exception("embedCharacterNGrams() n must be greater than 0");
        }
        
        if (args.Count >= 3 && args[2].Type == ValueType.Integer)
        {
            dimension = args[2].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedCharacterNGrams() dimension must be greater than 0");
        }
        
        var ngrams = GenerateNGrams(text, n);
        var vector = new List<double>(new double[dimension]);
        
        foreach (var ngram in ngrams)
        {
            int index = HashFeature(ngram, dimension);
            vector[index] += 1.0;
        }
        
        // Normalize
        vector = NormalizeVector(vector);
        
        var result = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            result.Add(RuntimeValue.Float(val));
        }
        
        return RuntimeValue.Array(result);
    }
    
    // embedHash: Hash-based feature hashing
    private static RuntimeValue BuiltInEmbedHash(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("embedHash() expects at least 1 argument (text, dimension?)");
        
        var text = ReadTextOrFile(args[0]);
        int dimension = 128;
        
        if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            dimension = args[1].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedHash() dimension must be greater than 0");
        }
        
        var words = TokenizeText(text);
        var vector = new List<double>(new double[dimension]);
        
        foreach (var word in words)
        {
            int index = HashFeature(word, dimension);
            vector[index] += 1.0;
        }
        
        // Normalize
        vector = NormalizeVector(vector);
        
        var result = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            result.Add(RuntimeValue.Float(val));
        }
        
        return RuntimeValue.Array(result);
    }
    
    // embedTFIDF: TF-IDF embeddings
    private static RuntimeValue BuiltInEmbedTFIDF(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("embedTFIDF() expects at least 1 argument (text, corpus?, dimension?)");
        
        var text = ReadTextOrFile(args[0]);
        int dimension = 1000;
        List<string>? corpus = null;
        
        if (args.Count >= 2 && args[1].Type == ValueType.Array)
        {
            corpus = new List<string>();
            foreach (var item in args[1].AsArray())
            {
                corpus.Add(ReadTextOrFile(item));
            }
        }
        else if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            // Second argument is dimension, not corpus
            dimension = args[1].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedTFIDF() dimension must be greater than 0");
        }
        
        if (args.Count >= 3 && args[2].Type == ValueType.Integer)
        {
            dimension = args[2].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedTFIDF() dimension must be greater than 0");
        }
        
        var words = TokenizeText(text);
        var vector = new List<double>(new double[dimension]);
        
        // Calculate term frequencies (TF)
        var wordCounts = new Dictionary<string, int>();
        foreach (var word in words)
        {
            wordCounts[word] = wordCounts.GetValueOrDefault(word, 0) + 1;
        }
        
        int totalWords = words.Count;
        if (totalWords == 0)
        {
            // Return zero vector
            var result = new List<RuntimeValue>();
            for (int i = 0; i < dimension; i++)
            {
                result.Add(RuntimeValue.Float(0.0));
            }
            return RuntimeValue.Array(result);
        }
        
        // Calculate IDF if corpus provided
        var idf = new Dictionary<string, double>();
        if (corpus != null && corpus.Count > 0)
        {
            var docWordSets = new List<HashSet<string>>();
            foreach (var doc in corpus)
            {
                var docWords = new HashSet<string>(TokenizeText(doc));
                docWordSets.Add(docWords);
            }
            
            int totalDocs = corpus.Count;
            foreach (var word in wordCounts.Keys)
            {
                int docsWithWord = 0;
                foreach (var docWords in docWordSets)
                {
                    if (docWords.Contains(word))
                        docsWithWord++;
                }
                
                if (docsWithWord > 0)
                {
                    idf[word] = Math.Log((double)totalDocs / docsWithWord) + 1.0;
                }
                else
                {
                    idf[word] = 1.0;
                }
            }
        }
        
        // Calculate TF-IDF
        foreach (var kvp in wordCounts)
        {
            var word = kvp.Key;
            var tf = (double)kvp.Value / totalWords;
            var idfValue = idf.GetValueOrDefault(word, 1.0);
            var tfidf = tf * idfValue;
            
            int index = HashFeature(word, dimension);
            vector[index] += tfidf;
        }
        
        // Normalize
        vector = NormalizeVector(vector);
        
        var result2 = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            result2.Add(RuntimeValue.Float(val));
        }
        
        return RuntimeValue.Array(result2);
    }
    
    // embedFromFile: Convenience function to read file and embed
    private static RuntimeValue BuiltInEmbedFromFile(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("embedFromFile() expects at least 2 arguments (filePath, method, ...)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("embedFromFile() first argument must be a string (file path)");
        
        if (args[1].Type != ValueType.String)
            throw new Exception("embedFromFile() second argument must be a string (method name)");
        
        var filePath = args[0].AsString();
        var method = args[1].AsString().ToLowerInvariant();
        
        if (!File.Exists(filePath))
            throw new Exception($"embedFromFile() file not found: {filePath}");
        
        var fileContent = File.ReadAllText(filePath);
        var newArgs = new List<RuntimeValue> { RuntimeValue.String(fileContent) };
        
        // Add remaining arguments
        for (int i = 2; i < args.Count; i++)
        {
            newArgs.Add(args[i]);
        }
        
        return method switch
        {
            "bagofwords" or "bag_of_words" => BuiltInEmbedBagOfWords(newArgs),
            "characterngrams" or "character_ngrams" => BuiltInEmbedCharacterNGrams(newArgs),
            "hash" => BuiltInEmbedHash(newArgs),
            "tfidf" => BuiltInEmbedTFIDF(newArgs),
            _ => throw new Exception($"embedFromFile() unknown method: {method}. Supported: bagOfWords, characterNGrams, hash, tfidf")
        };
    }
    
    // embedFromFiles: Embed multiple files
    private static RuntimeValue BuiltInEmbedFromFiles(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("embedFromFiles() expects at least 2 arguments (filePaths, method, ...)");
        
        if (args[0].Type != ValueType.Array)
            throw new Exception("embedFromFiles() first argument must be an array of file paths");
        
        if (args[1].Type != ValueType.String)
            throw new Exception("embedFromFiles() second argument must be a string (method name)");
        
        var filePaths = args[0].AsArray();
        var method = args[1].AsString().ToLowerInvariant();
        
        var results = new List<RuntimeValue>();
        
        foreach (var filePathValue in filePaths)
        {
            if (filePathValue.Type != ValueType.String)
                continue;
            
            var filePath = filePathValue.AsString();
            if (!File.Exists(filePath))
                continue;
            
            var newArgs = new List<RuntimeValue> { filePathValue, args[1] };
            for (int i = 2; i < args.Count; i++)
            {
                newArgs.Add(args[i]);
            }
            
            try
            {
                var embedding = BuiltInEmbedFromFile(newArgs);
                results.Add(embedding);
            }
            catch
            {
                // Skip files that can't be read
            }
        }
        
        return RuntimeValue.Array(results);
    }
    
    // --- Structured task planning: plan validation and topo-sort ---

    /// <summary>
    /// Validates a plan (object with "steps" or array of steps) and returns normalized plan or error object.
    /// Success: returns object with steps, planId (guid), optional taskSummary.
    /// Failure: returns object with "error" key (string message).
    /// </summary>
    public static RuntimeValue ValidateAndNormalizePlan(RuntimeValue planOrSteps)
    {
        List<RuntimeValue> steps;
        string? taskSummary = null;
        if (planOrSteps.Type == ValueType.Array)
        {
            steps = planOrSteps.AsArray();
        }
        else if (planOrSteps.Type == ValueType.Object)
        {
            var obj = planOrSteps.AsObject();
            var stepsVal = obj.Get("steps", null);
            if (stepsVal == null || stepsVal.Type != ValueType.Array)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String("Plan must have a 'steps' array"));
                return RuntimeValue.Object(err);
            }
            steps = stepsVal.AsArray();
            var summaryVal = obj.Get("taskSummary", null);
            if (summaryVal != null && summaryVal.Type == ValueType.String)
                taskSummary = summaryVal.AsString();
        }
        else
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Plan must be an object with 'steps' or an array of steps"));
            return RuntimeValue.Object(err);
        }

        if (steps == null || steps.Count == 0)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Plan must have at least one step"));
            return RuntimeValue.Object(err);
        }

        var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var normalizedSteps = new List<RuntimeValue>();
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.Type != ValueType.Object)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step at index {i} must be an object with 'id' and 'description'"));
                return RuntimeValue.Object(err);
            }
            var so = s.AsObject();
            var idVal = so.Get("id", null);
            var descVal = so.Get("description", null);
            if (idVal == null || idVal.Type != ValueType.String)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step at index {i} must have a string 'id'"));
                return RuntimeValue.Object(err);
            }
            var id = idVal.AsString();
            if (string.IsNullOrWhiteSpace(id))
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step at index {i} has empty 'id'"));
                return RuntimeValue.Object(err);
            }
            if (idToIndex.ContainsKey(id))
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Duplicate step id: '{id}'"));
                return RuntimeValue.Object(err);
            }
            idToIndex[id] = i;
            if (descVal == null || descVal.Type != ValueType.String)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step '{id}' must have a string 'description'"));
                return RuntimeValue.Object(err);
            }
            var dependsOnVal = so.Get("dependsOn", null);
            // Missing dependsOn (Null) is optional; if present it must be an array
            if (dependsOnVal.Type != ValueType.Null && dependsOnVal.Type != ValueType.Array)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step '{id}': 'dependsOn' must be an array of step ids"));
                return RuntimeValue.Object(err);
            }
            normalizedSteps.Add(s);
        }

        // Second pass: validate dependsOn references (all ids are now in idToIndex)
        for (int i = 0; i < normalizedSteps.Count; i++)
        {
            var so = normalizedSteps[i].AsObject();
            var id = so.Get("id", null).AsString();
            var dependsOnVal = so.Get("dependsOn", null);
            if (dependsOnVal.Type != ValueType.Null && dependsOnVal.Type == ValueType.Array)
            {
                foreach (var dep in dependsOnVal.AsArray())
                {
                    if (dep.Type != ValueType.String)
                        continue;
                    var depId = dep.AsString();
                    if (!idToIndex.ContainsKey(depId))
                    {
                        var err = new JsonObject();
                        err.Set("error", RuntimeValue.String($"Step '{id}' depends on unknown step id '{depId}'"));
                        return RuntimeValue.Object(err);
                    }
                }
            }
        }

        // Cycle check via Kahn's algorithm: inDegree[i] = number of steps that step i depends on
        var idByIndex = new string[steps.Count];
        foreach (var kv in idToIndex) idByIndex[kv.Value] = kv.Key;
        var inDegree = new int[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            var depVal = normalizedSteps[i].AsObject().Get("dependsOn", null);
            inDegree[i] = (depVal != null && depVal.Type == ValueType.Array) ? depVal.AsArray().Count : 0;
        }
        var queue = new Queue<int>();
        for (int i = 0; i < steps.Count; i++)
            if (inDegree[i] == 0) queue.Enqueue(i);
        int count = 0;
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            count++;
            var uid = idByIndex[u];
            for (int v = 0; v < steps.Count; v++)
            {
                if (v == u) continue;
                var depValV = normalizedSteps[v].AsObject().Get("dependsOn", null);
                if (depValV == null || depValV.Type != ValueType.Array) continue;
                bool dependsOnU = false;
                foreach (var d in depValV.AsArray())
                {
                    if (d.Type == ValueType.String && d.AsString() == uid) { dependsOnU = true; break; }
                }
                if (dependsOnU)
                {
                    inDegree[v]--;
                    if (inDegree[v] == 0) queue.Enqueue(v);
                }
            }
        }
        if (count != steps.Count)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Plan has a cycle in step dependencies"));
            return RuntimeValue.Object(err);
        }

        var planId = Guid.NewGuid().ToString();
        var outPlan = new JsonObject();
        outPlan.Set("steps", RuntimeValue.Array(normalizedSteps));
        outPlan.Set("planId", RuntimeValue.String(planId));
        if (!string.IsNullOrEmpty(taskSummary))
            outPlan.Set("taskSummary", RuntimeValue.String(taskSummary));
        return RuntimeValue.Object(outPlan);
    }

    /// <summary>
    /// Returns steps in topological order (dependency order). Returns null if cycle.
    /// </summary>
    public static List<RuntimeValue>? TopoSortSteps(RuntimeValue plan)
    {
        if (plan.Type != ValueType.Object) return null;
        var stepsVal = plan.AsObject().Get("steps", null);
        if (stepsVal == null || stepsVal.Type != ValueType.Array) return null;
        var steps = stepsVal.AsArray();
        if (steps.Count == 0) return new List<RuntimeValue>();
        var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.Type != ValueType.Object) return null;
            var id = s.AsObject().Get("id", null);
            if (id == null || id.Type != ValueType.String) return null;
            idToIndex[id.AsString()] = i;
        }
        var inDegree = new int[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            var depVal = steps[i].AsObject().Get("dependsOn", null);
            inDegree[i] = (depVal != null && depVal.Type == ValueType.Array) ? depVal.AsArray().Count : 0;
        }
        var queue = new Queue<int>();
        for (int i = 0; i < steps.Count; i++)
            if (inDegree[i] == 0) queue.Enqueue(i);
        var result = new List<RuntimeValue>();
        var idByIndex = new string[steps.Count];
        for (int i = 0; i < steps.Count; i++)
            idByIndex[i] = steps[i].AsObject().Get("id", null).AsString();
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            result.Add(steps[u]);
            var uid = idByIndex[u];
            for (int v = 0; v < steps.Count; v++)
            {
                if (v == u) continue;
                var depVal = steps[v].AsObject().Get("dependsOn", null);
                if (depVal == null || depVal.Type != ValueType.Array) continue;
                bool dependsOnU = false;
                foreach (var d in depVal.AsArray())
                {
                    if (d.Type == ValueType.String && d.AsString() == uid) { dependsOnU = true; break; }
                }
                if (dependsOnU)
                {
                    inDegree[v]--;
                    if (inDegree[v] == 0) queue.Enqueue(v);
                }
            }
        }
        if (result.Count != steps.Count) return null;
        return result;
    }

    private static RuntimeValue BuiltInParseJsonTyped(List<RuntimeValue> args, Interpreter? interpreter) =>
        AiPipelineHelpers.ParseJsonTyped(args, interpreter);

    private static RuntimeValue BuiltInLoadDocuments(List<RuntimeValue> args) =>
        AiPipelineHelpers.LoadDocuments(args);

    private static RuntimeValue BuiltInSplitDocuments(List<RuntimeValue> args) =>
        AiPipelineHelpers.SplitDocuments(args);

    private static RuntimeValue BuiltInFormatRetrievedDocs(List<RuntimeValue> args) =>
        AiPipelineHelpers.FormatRetrievedDocs(args);

    private static RuntimeValue BuiltInWithExamples(List<RuntimeValue> args) =>
        AiPipelineHelpers.WithExamples(args);

    private static RuntimeValue BuiltInIndexInto(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (interpreter == null)
            throw new Exception("indexInto() requires an active interpreter context.");
        return AiPipelineHelpers.IndexInto(args, interpreter);
    }

    private static RuntimeValue BuiltInComposePipe(List<RuntimeValue> args, Interpreter? interpreter) =>
        AiPipelineHelpers.ComposePipe(args, interpreter);

    private static RuntimeValue BuiltInMergeRetrievedDocs(List<RuntimeValue> args) =>
        AiPipelineHelpers.MergeRetrievedDocs(args);
}

public class InputRequiredException : Exception
{
    public string Prompt { get; }
    
    public InputRequiredException(string prompt) : base("Input required")
    {
        Prompt = prompt;
    }
}
