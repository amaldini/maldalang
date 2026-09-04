// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Concurrent;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// One step in a stored structured plan.
/// </summary>
public sealed class StoredPlanStep
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string>? DependsOn { get; set; }
    public string Status { get; set; } = AgentPlanStore.StatusPending;
    public string? Note { get; set; }

    public StoredPlanStep Clone() => new()
    {
        Id = Id,
        Description = Description,
        DependsOn = DependsOn == null ? null : new List<string>(DependsOn),
        Status = Status,
        Note = Note
    };
}

/// <summary>
/// In-memory snapshot of a plan submitted via <c>submit_plan</c>.
/// </summary>
public sealed class StoredPlan
{
    public string PlanId { get; set; } = "";
    public string? TaskSummary { get; set; }
    public List<StoredPlanStep> Steps { get; set; } = new();

    public StoredPlan Clone() => new()
    {
        PlanId = PlanId,
        TaskSummary = TaskSummary,
        Steps = Steps.Select(s => s.Clone()).ToList()
    };
}

/// <summary>
/// Process-local store for structured task plans. Thread-safe: <see cref="TryGet"/>
/// and <see cref="Put"/> clone snapshots so callers can mutate without racing.
/// </summary>
public static class AgentPlanStore
{
    public const string StatusPending = "pending";
    public const string StatusInProgress = "in_progress";
    public const string StatusDone = "done";
    public const string StatusBlocked = "blocked";

    private static readonly ConcurrentDictionary<string, StoredPlan> Plans =
        new(StringComparer.Ordinal);

    public static bool IsValidStatus(string? status) =>
        status is StatusPending or StatusInProgress or StatusDone or StatusBlocked;

    public static bool TryGet(string planId, out StoredPlan? plan)
    {
        if (!string.IsNullOrEmpty(planId) && Plans.TryGetValue(planId, out var existing))
        {
            plan = existing.Clone();
            return true;
        }

        plan = null;
        return false;
    }

    public static void Put(StoredPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.PlanId))
            throw new ArgumentException("Plan must have a planId.", nameof(plan));
        Plans[plan.PlanId] = plan.Clone();
    }

    public static void Clear() => Plans.Clear();

    public static StoredPlan StoreValidated(string planId, string? taskSummary, IReadOnlyList<RuntimeValue> steps)
    {
        var stored = new StoredPlan
        {
            PlanId = planId,
            TaskSummary = taskSummary,
            Steps = StepsFromRuntime(steps, previousById: null)
        };
        Put(stored);
        return stored;
    }

    public static List<StoredPlanStep> StepsFromRuntime(
        IReadOnlyList<RuntimeValue> steps,
        IReadOnlyDictionary<string, StoredPlanStep>? previousById)
    {
        var result = new List<StoredPlanStep>();
        foreach (var stepVal in steps)
        {
            if (stepVal.Type != ValueType.Object)
                continue;
            var so = stepVal.AsObject();
            var idVal = so.Get("id", null);
            if (idVal == null || idVal.Type != ValueType.String)
                continue;
            var id = idVal.AsString();
            var descVal = so.Get("description", null);
            var description = descVal != null && descVal.Type == ValueType.String
                ? descVal.AsString()
                : "";
            List<string>? dependsOn = null;
            var depVal = so.Get("dependsOn", null);
            if (depVal != null && depVal.Type == ValueType.Array)
            {
                dependsOn = new List<string>();
                foreach (var dep in depVal.AsArray())
                {
                    if (dep.Type == ValueType.String)
                        dependsOn.Add(dep.AsString());
                }
            }

            var status = StatusPending;
            string? note = null;
            if (previousById != null && previousById.TryGetValue(id, out var previous))
            {
                status = previous.Status;
                note = previous.Note;
            }

            result.Add(new StoredPlanStep
            {
                Id = id,
                Description = description,
                DependsOn = dependsOn,
                Status = status,
                Note = note
            });
        }

        return result;
    }

    public static RuntimeValue StepsToRuntime(IEnumerable<StoredPlanStep> steps)
    {
        var arr = new List<RuntimeValue>();
        foreach (var step in steps)
            arr.Add(StepToRuntime(step));
        return RuntimeValue.Array(arr);
    }

    public static RuntimeValue StepToRuntime(StoredPlanStep step)
    {
        var obj = new JsonObject();
        obj.Set("id", RuntimeValue.String(step.Id));
        obj.Set("description", RuntimeValue.String(step.Description));
        if (step.DependsOn != null)
        {
            var deps = new List<RuntimeValue>(step.DependsOn.Count);
            foreach (var dep in step.DependsOn)
                deps.Add(RuntimeValue.String(dep));
            obj.Set("dependsOn", RuntimeValue.Array(deps));
        }
        obj.Set("status", RuntimeValue.String(step.Status));
        if (step.Note != null)
            obj.Set("note", RuntimeValue.String(step.Note));
        return RuntimeValue.Object(obj);
    }
}
