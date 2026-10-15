// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

public static partial class BuiltInTools
{
    public static RuntimeValue CreateSubmitPlanTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        var properties = new JsonObject();
        var planProp = new JsonObject();
        planProp.Set("type", RuntimeValue.String("object"));
        planProp.Set("description", RuntimeValue.String("Plan object with 'steps' array (each step: { id: string, description: string, dependsOn?: string[] }) or pass 'steps' directly as array. Optional 'taskSummary' (string)."));
        properties.Set("plan", RuntimeValue.Object(planProp));
        var stepsProp = new JsonObject();
        stepsProp.Set("type", RuntimeValue.String("array"));
        stepsProp.Set("description", RuntimeValue.String("Alternative to 'plan': array of steps [{ id, description, dependsOn? }, ...]."));
        properties.Set("steps", RuntimeValue.Object(stepsProp));
        var taskSummaryProp = new JsonObject();
        taskSummaryProp.Set("type", RuntimeValue.String("string"));
        taskSummaryProp.Set("description", RuntimeValue.String("Optional: short summary of the task."));
        properties.Set("taskSummary", RuntimeValue.Object(taskSummaryProp));
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue>()));
        tool.Initialize(
            "submit_plan",
            "Submit a structured plan before or during execution. Use when you have broken down the task into steps. Pass either a 'plan' object with 'steps' (and optional 'taskSummary') or a 'steps' array. Each step must have 'id' (string) and 'description' (string); optional 'dependsOn' (array of step ids). Returns { accepted: true, planId, stepCount } or { accepted: false, error }.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateUpdatePlanTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        var properties = new JsonObject();
        var planIdProp = new JsonObject();
        planIdProp.Set("type", RuntimeValue.String("string"));
        planIdProp.Set("description", RuntimeValue.String("Id of the stored plan to update (from submit_plan)."));
        properties.Set("planId", RuntimeValue.Object(planIdProp));
        var stepsProp = new JsonObject();
        stepsProp.Set("type", RuntimeValue.String("array"));
        stepsProp.Set("description", RuntimeValue.String("Optional replacement steps [{ id, description, dependsOn? }, ...]. Re-validated; surviving ids keep their status; new ids start pending; omitted ids are dropped."));
        properties.Set("steps", RuntimeValue.Object(stepsProp));
        var taskSummaryProp = new JsonObject();
        taskSummaryProp.Set("type", RuntimeValue.String("string"));
        taskSummaryProp.Set("description", RuntimeValue.String("Optional: replace the stored task summary."));
        properties.Set("taskSummary", RuntimeValue.Object(taskSummaryProp));
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("planId") }));
        tool.Initialize(
            "update_plan",
            "Update a stored structured plan. Requires planId. Optional steps are re-validated (surviving step ids keep their status; new ids are pending). Optional taskSummary replaces the stored summary. Returns { accepted: true, planId, stepCount, steps } or { accepted: false, error }. Not parallel-safe.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );
        return RuntimeValue.Object(tool);
    }

    public static RuntimeValue CreateMarkStepTool()
    {
        var tool = new ToolInstance();
        var parameters = new JsonObject();
        var properties = new JsonObject();
        var planIdProp = new JsonObject();
        planIdProp.Set("type", RuntimeValue.String("string"));
        planIdProp.Set("description", RuntimeValue.String("Id of the stored plan (from submit_plan)."));
        properties.Set("planId", RuntimeValue.Object(planIdProp));
        var idProp = new JsonObject();
        idProp.Set("type", RuntimeValue.String("string"));
        idProp.Set("description", RuntimeValue.String("Step id to update."));
        properties.Set("id", RuntimeValue.Object(idProp));
        var statusProp = new JsonObject();
        statusProp.Set("type", RuntimeValue.String("string"));
        statusProp.Set("description", RuntimeValue.String("New step status: pending, in_progress, done, or blocked."));
        statusProp.Set("enum", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("pending"),
            RuntimeValue.String("in_progress"),
            RuntimeValue.String("done"),
            RuntimeValue.String("blocked")
        }));
        properties.Set("status", RuntimeValue.Object(statusProp));
        var noteProp = new JsonObject();
        noteProp.Set("type", RuntimeValue.String("string"));
        noteProp.Set("description", RuntimeValue.String("Optional note stored on the step."));
        properties.Set("note", RuntimeValue.Object(noteProp));
        parameters.Set("type", RuntimeValue.String("object"));
        parameters.Set("properties", RuntimeValue.Object(properties));
        parameters.Set("required", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("planId"),
            RuntimeValue.String("id"),
            RuntimeValue.String("status")
        }));
        tool.Initialize(
            "mark_step",
            "Set the status of a stored plan step. Requires planId, id, and status (pending | in_progress | done | blocked). Optional note is stored on the step. Returns { accepted: true, planId, id, status } or { accepted: false, error }. Not parallel-safe.",
            RuntimeValue.Object(parameters),
            null,
            ""
        );
        return RuntimeValue.Object(tool);
    }
}
