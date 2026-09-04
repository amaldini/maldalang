// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class PlanProgressToolTests : TestBase
{
    private static RuntimeValue Step(string id, string description, string[]? dependsOn = null)
    {
        var step = new JsonObject();
        step.Set("id", RuntimeValue.String(id));
        step.Set("description", RuntimeValue.String(description));
        if (dependsOn != null)
        {
            var deps = new List<RuntimeValue>();
            foreach (var d in dependsOn)
                deps.Add(RuntimeValue.String(d));
            step.Set("dependsOn", RuntimeValue.Array(deps));
        }
        return RuntimeValue.Object(step);
    }

    private static RuntimeValue Submit(params RuntimeValue[] steps)
    {
        var tool = (ToolInstance)BuiltInTools.CreateSubmitPlanTool().AsObject();
        var args = new JsonObject();
        args.Set("steps", RuntimeValue.Array(new List<RuntimeValue>(steps)));
        return tool.Execute(RuntimeValue.Object(args));
    }

    [Fact]
    public void SubmitThenMarkStepDone_StoreShowsDone()
    {
        var submitted = Submit(Step("1", "Do something"));
        Assert.True(submitted.AsObject().Get("accepted", null).AsBoolean());
        var planId = submitted.AsObject().Get("planId", null).AsString();

        var markTool = (ToolInstance)BuiltInTools.CreateMarkStepTool().AsObject();
        var markArgs = new JsonObject();
        markArgs.Set("planId", RuntimeValue.String(planId));
        markArgs.Set("id", RuntimeValue.String("1"));
        markArgs.Set("status", RuntimeValue.String("done"));
        markArgs.Set("note", RuntimeValue.String("finished"));
        var marked = markTool.Execute(RuntimeValue.Object(markArgs));

        Assert.Equal(ValueType.Object, marked.Type);
        var obj = marked.AsObject();
        Assert.True(obj.Get("accepted", null).AsBoolean());
        Assert.Equal(planId, obj.Get("planId", null).AsString());
        Assert.Equal("1", obj.Get("id", null).AsString());
        Assert.Equal("done", obj.Get("status", null).AsString());

        Assert.True(AgentPlanStore.TryGet(planId, out var stored));
        Assert.NotNull(stored);
        Assert.Single(stored!.Steps);
        Assert.Equal("done", stored.Steps[0].Status);
        Assert.Equal("finished", stored.Steps[0].Note);
    }

    [Fact]
    public void MarkUnknownPlanId_ReturnsNotAccepted()
    {
        var markTool = (ToolInstance)BuiltInTools.CreateMarkStepTool().AsObject();
        var args = new JsonObject();
        args.Set("planId", RuntimeValue.String("missing-plan"));
        args.Set("id", RuntimeValue.String("1"));
        args.Set("status", RuntimeValue.String("done"));
        var result = markTool.Execute(RuntimeValue.Object(args));
        Assert.False(result.AsObject().Get("accepted", null).AsBoolean());
        Assert.Contains("Unknown planId", result.AsObject().Get("error", null).AsString());
    }

    [Fact]
    public void MarkUnknownStep_ReturnsNotAccepted()
    {
        var submitted = Submit(Step("1", "Only step"));
        var planId = submitted.AsObject().Get("planId", null).AsString();
        var markTool = (ToolInstance)BuiltInTools.CreateMarkStepTool().AsObject();
        var args = new JsonObject();
        args.Set("planId", RuntimeValue.String(planId));
        args.Set("id", RuntimeValue.String("99"));
        args.Set("status", RuntimeValue.String("done"));
        var result = markTool.Execute(RuntimeValue.Object(args));
        Assert.False(result.AsObject().Get("accepted", null).AsBoolean());
        Assert.Contains("Unknown step", result.AsObject().Get("error", null).AsString());
    }

    [Fact]
    public void UpdatePlan_ReplacesSteps_KeepsStatusOnSurvivingIds()
    {
        var submitted = Submit(Step("1", "First"), Step("2", "Second"));
        var planId = submitted.AsObject().Get("planId", null).AsString();

        var markTool = (ToolInstance)BuiltInTools.CreateMarkStepTool().AsObject();
        var markArgs = new JsonObject();
        markArgs.Set("planId", RuntimeValue.String(planId));
        markArgs.Set("id", RuntimeValue.String("1"));
        markArgs.Set("status", RuntimeValue.String("done"));
        Assert.True(markTool.Execute(RuntimeValue.Object(markArgs)).AsObject().Get("accepted", null).AsBoolean());

        var updateTool = (ToolInstance)BuiltInTools.CreateUpdatePlanTool().AsObject();
        var updateArgs = new JsonObject();
        updateArgs.Set("planId", RuntimeValue.String(planId));
        updateArgs.Set("taskSummary", RuntimeValue.String("revised"));
        updateArgs.Set("steps", RuntimeValue.Array(new List<RuntimeValue>
        {
            Step("1", "First updated"),
            Step("3", "Third")
        }));
        var updated = updateTool.Execute(RuntimeValue.Object(updateArgs));
        var obj = updated.AsObject();
        Assert.True(obj.Get("accepted", null).AsBoolean());
        Assert.Equal(planId, obj.Get("planId", null).AsString());
        Assert.Equal(2, obj.Get("stepCount", null).AsInteger());

        Assert.True(AgentPlanStore.TryGet(planId, out var stored));
        Assert.NotNull(stored);
        Assert.Equal("revised", stored!.TaskSummary);
        Assert.Equal(2, stored.Steps.Count);
        Assert.Equal("1", stored.Steps[0].Id);
        Assert.Equal("First updated", stored.Steps[0].Description);
        Assert.Equal("done", stored.Steps[0].Status);
        Assert.Equal("3", stored.Steps[1].Id);
        Assert.Equal("pending", stored.Steps[1].Status);
    }

    [Fact]
    public void MarkStep_InvalidStatus_Rejected()
    {
        var submitted = Submit(Step("1", "Only"));
        var planId = submitted.AsObject().Get("planId", null).AsString();
        var markTool = (ToolInstance)BuiltInTools.CreateMarkStepTool().AsObject();
        var args = new JsonObject();
        args.Set("planId", RuntimeValue.String(planId));
        args.Set("id", RuntimeValue.String("1"));
        args.Set("status", RuntimeValue.String("finished"));
        var result = markTool.Execute(RuntimeValue.Object(args));
        Assert.False(result.AsObject().Get("accepted", null).AsBoolean());
        Assert.Contains("status", result.AsObject().Get("error", null).AsString());

        Assert.True(AgentPlanStore.TryGet(planId, out var stored));
        Assert.Equal("pending", stored!.Steps[0].Status);
    }

    [Fact]
    public void UpdatePlan_UnknownPlanId_ReturnsNotAccepted()
    {
        var updateTool = (ToolInstance)BuiltInTools.CreateUpdatePlanTool().AsObject();
        var args = new JsonObject();
        args.Set("planId", RuntimeValue.String("no-such-plan"));
        var result = updateTool.Execute(RuntimeValue.Object(args));
        Assert.False(result.AsObject().Get("accepted", null).AsBoolean());
        Assert.Contains("Unknown planId", result.AsObject().Get("error", null).AsString());
    }

    [Fact]
    public void UpdatePlan_InvalidSteps_ReturnsNotAccepted()
    {
        var submitted = Submit(Step("1", "Only"));
        var planId = submitted.AsObject().Get("planId", null).AsString();
        var updateTool = (ToolInstance)BuiltInTools.CreateUpdatePlanTool().AsObject();
        var args = new JsonObject();
        args.Set("planId", RuntimeValue.String(planId));
        args.Set("steps", RuntimeValue.Array(new List<RuntimeValue>()));
        var result = updateTool.Execute(RuntimeValue.Object(args));
        Assert.False(result.AsObject().Get("accepted", null).AsBoolean());
        Assert.True(AgentPlanStore.TryGet(planId, out var stored));
        Assert.Single(stored!.Steps);
    }

    [Fact]
    public void ToolExecute_DoesNotReturnStub()
    {
        var submit = (ToolInstance)BuiltInTools.CreateSubmitPlanTool().AsObject();
        var update = (ToolInstance)BuiltInTools.CreateUpdatePlanTool().AsObject();
        var mark = (ToolInstance)BuiltInTools.CreateMarkStepTool().AsObject();
        foreach (var tool in new[] { submit, update, mark })
        {
            var result = tool.Execute(RuntimeValue.Object(new JsonObject()));
            Assert.DoesNotContain("Tool execution validated", result.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SubmitMarkUpdate_InterpretAndTranspile_SameAcceptedFlags()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var submit = createSubmitPlanTool();
            var mark = createMarkStepTool();
            var update = createUpdatePlanTool();
            var plan = submit.execute({
                "steps": [
                    { "id": "s1", "description": "one" },
                    { "id": "s2", "description": "two" }
                ]
            });
            print("submit=" + string(plan.accepted));
            var marked = mark.execute({ "planId": plan.planId, "id": "s1", "status": "done" });
            print("mark=" + string(marked.accepted));
            var updated = update.execute({
                "planId": plan.planId,
                "steps": [
                    { "id": "s1", "description": "one updated" },
                    { "id": "s3", "description": "three" }
                ]
            });
            print("update=" + string(updated.accepted));
            print("kept=" + string(updated.steps[0].status));
            print("new=" + string(updated.steps[1].status));
            """,
            "submit-mark-update-plan");
    }
}
