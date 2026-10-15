// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLang.Runtime.Tracing;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class ConversationInstance
{
    private RuntimeValue? ExecutePlanTool(string canonicalName, ToolInstance tool, RuntimeValue arguments, ObjectInstance argsObj, string? resolvedFilePath, string? resolvedDirPath)
    {
        switch (canonicalName)
        {
            case "submit_plan":
                try
                {
                    var planVal = argsObj.Get("plan", null);
                    var stepsVal = argsObj.Get("steps", null);
                    RuntimeValue planOrSteps;
                    if (planVal != null && (planVal.Type == ValueType.Object || planVal.Type == ValueType.Array))
                        planOrSteps = planVal;
                    else if (stepsVal != null && stepsVal.Type == ValueType.Array)
                        planOrSteps = stepsVal;
                    else
                    {
                        var outErr = new JsonObject();
                        outErr.Set("accepted", RuntimeValue.Boolean(false));
                        outErr.Set("error", RuntimeValue.String("submit_plan requires 'plan' (object with steps) or 'steps' (array)"));
                        return RuntimeValue.Object(outErr);
                    }
                    var validation = BuiltInFunctions.ValidateAndNormalizePlan(planOrSteps);
                    if (validation.Type != ValueType.Object)
                    {
                        var outErr = new JsonObject();
                        outErr.Set("accepted", RuntimeValue.Boolean(false));
                        outErr.Set("error", RuntimeValue.String("Invalid plan"));
                        return RuntimeValue.Object(outErr);
                    }
                    var vObj = validation.AsObject();
                    var errVal = vObj.Get("error", null);
                    if (errVal != null && errVal.Type == ValueType.String)
                    {
                        var outErr = new JsonObject();
                        outErr.Set("accepted", RuntimeValue.Boolean(false));
                        outErr.Set("error", errVal);
                        return RuntimeValue.Object(outErr);
                    }
                    var planIdVal = vObj.Get("planId", null);
                    var stepsArr = vObj.Get("steps", null);
                    int stepCount = stepsArr != null && stepsArr.Type == ValueType.Array ? stepsArr.AsArray().Count : 0;
                    var planId = planIdVal != null && planIdVal.Type == ValueType.String
                        ? planIdVal.AsString()
                        : "";
                    if (!string.IsNullOrEmpty(planId) && stepsArr != null && stepsArr.Type == ValueType.Array)
                    {
                        string? taskSummary = null;
                        var summaryVal = vObj.Get("taskSummary", null);
                        if (summaryVal != null && summaryVal.Type == ValueType.String)
                            taskSummary = summaryVal.AsString();
                        else
                        {
                            var argSummary = argsObj.Get("taskSummary", null);
                            if (argSummary != null && argSummary.Type == ValueType.String)
                                taskSummary = argSummary.AsString();
                        }
                        AgentPlanStore.StoreValidated(planId, taskSummary, stepsArr.AsArray());
                    }
                    var outOk = new JsonObject();
                    outOk.Set("accepted", RuntimeValue.Boolean(true));
                    outOk.Set("planId", planIdVal ?? RuntimeValue.String(""));
                    outOk.Set("stepCount", RuntimeValue.Integer(stepCount));
                    return RuntimeValue.Object(outOk);
                }
                catch (Exception ex)
                {
                    var outErr = new JsonObject();
                    outErr.Set("accepted", RuntimeValue.Boolean(false));
                    outErr.Set("error", RuntimeValue.String(ex.Message));
                    return RuntimeValue.Object(outErr);
                }

            case "update_plan":
                try
                {
                    return ExecuteUpdatePlan(argsObj);
                }
                catch (Exception ex)
                {
                    var outErr = new JsonObject();
                    outErr.Set("accepted", RuntimeValue.Boolean(false));
                    outErr.Set("error", RuntimeValue.String(ex.Message));
                    return RuntimeValue.Object(outErr);
                }

            case "mark_step":
                try
                {
                    return ExecuteMarkStep(argsObj);
                }
                catch (Exception ex)
                {
                    var outErr = new JsonObject();
                    outErr.Set("accepted", RuntimeValue.Boolean(false));
                    outErr.Set("error", RuntimeValue.String(ex.Message));
                    return RuntimeValue.Object(outErr);
                }

            default:
                return null;
        }
    }

    private static RuntimeValue PlanToolError(string message)
    {
        var outErr = new JsonObject();
        outErr.Set("accepted", RuntimeValue.Boolean(false));
        outErr.Set("error", RuntimeValue.String(message));
        return RuntimeValue.Object(outErr);
    }

    private static string? ReadStringArg(ObjectInstance argsObj, string name)
    {
        var val = argsObj.Get(name, null);
        if (val != null && val.Type == ValueType.String)
        {
            var s = val.AsString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    private static RuntimeValue ExecuteUpdatePlan(ObjectInstance argsObj)
    {
        var planId = ReadStringArg(argsObj, "planId");
        if (planId == null)
            return PlanToolError("update_plan requires 'planId'");
        if (!AgentPlanStore.TryGet(planId, out var stored) || stored == null)
            return PlanToolError($"Unknown planId '{planId}'");

        var stepsVal = argsObj.Get("steps", null);
        if (stepsVal != null && stepsVal.Type == ValueType.Array)
        {
            var validation = BuiltInFunctions.ValidateAndNormalizePlan(stepsVal);
            if (validation.Type != ValueType.Object)
                return PlanToolError("Invalid steps");
            var vObj = validation.AsObject();
            var errVal = vObj.Get("error", null);
            if (errVal != null && errVal.Type == ValueType.String)
            {
                var outErr = new JsonObject();
                outErr.Set("accepted", RuntimeValue.Boolean(false));
                outErr.Set("error", errVal);
                return RuntimeValue.Object(outErr);
            }
            var normalized = vObj.Get("steps", null);
            if (normalized == null || normalized.Type != ValueType.Array)
                return PlanToolError("Invalid steps");
            var previousById = stored.Steps.ToDictionary(s => s.Id, StringComparer.Ordinal);
            stored.Steps = AgentPlanStore.StepsFromRuntime(normalized.AsArray(), previousById);
        }

        var summaryVal = argsObj.Get("taskSummary", null);
        if (summaryVal != null && summaryVal.Type == ValueType.String)
            stored.TaskSummary = summaryVal.AsString();

        AgentPlanStore.Put(stored);
        var outOk = new JsonObject();
        outOk.Set("accepted", RuntimeValue.Boolean(true));
        outOk.Set("planId", RuntimeValue.String(stored.PlanId));
        outOk.Set("stepCount", RuntimeValue.Integer(stored.Steps.Count));
        outOk.Set("steps", AgentPlanStore.StepsToRuntime(stored.Steps));
        return RuntimeValue.Object(outOk);
    }

    private static RuntimeValue ExecuteMarkStep(ObjectInstance argsObj)
    {
        var planId = ReadStringArg(argsObj, "planId");
        var stepId = ReadStringArg(argsObj, "id");
        var status = ReadStringArg(argsObj, "status");
        if (planId == null || stepId == null || status == null)
            return PlanToolError("mark_step requires 'planId', 'id', and 'status'");
        if (!AgentPlanStore.IsValidStatus(status))
            return PlanToolError("status must be pending, in_progress, done, or blocked");
        if (!AgentPlanStore.TryGet(planId, out var stored) || stored == null)
            return PlanToolError($"Unknown planId '{planId}'");

        var step = stored.Steps.FirstOrDefault(s => string.Equals(s.Id, stepId, StringComparison.Ordinal));
        if (step == null)
            return PlanToolError($"Unknown step id '{stepId}'");

        step.Status = status;
        var noteVal = argsObj.Get("note", null);
        if (noteVal != null && noteVal.Type == ValueType.String)
            step.Note = noteVal.AsString();

        AgentPlanStore.Put(stored);
        var outOk = new JsonObject();
        outOk.Set("accepted", RuntimeValue.Boolean(true));
        outOk.Set("planId", RuntimeValue.String(stored.PlanId));
        outOk.Set("id", RuntimeValue.String(step.Id));
        outOk.Set("status", RuntimeValue.String(step.Status));
        return RuntimeValue.Object(outOk);
    }
}
