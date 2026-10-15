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

    /// <summary>
    /// WF1001/WF1002 when a deny-listed built-in runs in a deterministic workflow section,
    /// including nested helpers (L4). Step / onReject frames skip the check.
    /// </summary>
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
}
