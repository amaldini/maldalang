// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using System.Collections.Generic;
using System.Reflection;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class StructuredTaskPlanningTests : TestBase
{
    private static RuntimeValue PlanFromSteps(List<RuntimeValue> steps)
    {
        var plan = new JsonObject();
        plan.Set("steps", RuntimeValue.Array(steps));
        return RuntimeValue.Object(plan);
    }

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

    [Fact]
    public void ValidateAndNormalizePlan_ValidPlan_ReturnsNormalizedPlan()
    {
        var steps = new List<RuntimeValue>
        {
            Step("1", "First step"),
            Step("2", "Second step", new[] { "1" })
        };
        var plan = PlanFromSteps(steps);
        var result = BuiltInFunctions.ValidateAndNormalizePlan(plan);
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        var errVal = obj.Get("error", null);
        Assert.True(errVal.Type != ValueType.String, errVal.Type == ValueType.String ? "Expected success, got error: " + errVal.AsString() : "");
        var stepsVal = obj.Get("steps", null);
        Assert.NotNull(stepsVal);
        Assert.Equal(ValueType.Array, stepsVal.Type);
        Assert.Equal(2, stepsVal.AsArray().Count);
        var planIdVal = obj.Get("planId", null);
        Assert.NotNull(planIdVal);
        Assert.Equal(ValueType.String, planIdVal.Type);
        Assert.False(string.IsNullOrEmpty(planIdVal.AsString()));
    }

    [Fact]
    public void ValidateAndNormalizePlan_ValidPlanAsArray_ReturnsNormalizedPlan()
    {
        var steps = new List<RuntimeValue>
        {
            Step("a", "Step A"),
            Step("b", "Step B")
        };
        var result = BuiltInFunctions.ValidateAndNormalizePlan(RuntimeValue.Array(steps));
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        var errVal = obj.Get("error", null);
        if (errVal != null && errVal.Type == ValueType.String)
        {
            Assert.Fail("Expected success, got error: " + errVal.AsString());
            return;
        }
        var stepsVal = obj.Get("steps", null);
        Assert.NotNull(stepsVal);
        Assert.Equal(2, stepsVal.AsArray().Count);
    }

    [Fact]
    public void ValidateAndNormalizePlan_EmptySteps_ReturnsError()
    {
        var plan = PlanFromSteps(new List<RuntimeValue>());
        var result = BuiltInFunctions.ValidateAndNormalizePlan(plan);
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        var errVal = obj.Get("error", null);
        Assert.NotNull(errVal);
        Assert.Equal(ValueType.String, errVal.Type);
        Assert.Contains("at least one step", errVal.AsString());
    }

    [Fact]
    public void ValidateAndNormalizePlan_DuplicateId_ReturnsError()
    {
        var steps = new List<RuntimeValue>
        {
            Step("1", "First"),
            Step("1", "Duplicate")
        };
        var plan = PlanFromSteps(steps);
        var result = BuiltInFunctions.ValidateAndNormalizePlan(plan);
        var obj = result.AsObject();
        var errVal = obj.Get("error", null);
        Assert.NotNull(errVal);
        Assert.Equal(ValueType.String, errVal.Type);
        Assert.Contains("Duplicate", errVal.AsString());
    }

    [Fact]
    public void ValidateAndNormalizePlan_InvalidDependsOn_ReturnsError()
    {
        var steps = new List<RuntimeValue>
        {
            Step("1", "First"),
            Step("2", "Second", new[] { "99" })
        };
        var plan = PlanFromSteps(steps);
        var result = BuiltInFunctions.ValidateAndNormalizePlan(plan);
        var obj = result.AsObject();
        var errVal = obj.Get("error", null);
        Assert.NotNull(errVal);
        Assert.Equal(ValueType.String, errVal.Type);
        Assert.Contains("99", errVal.AsString());
    }

    [Fact]
    public void ValidateAndNormalizePlan_Cycle_ReturnsError()
    {
        var steps = new List<RuntimeValue>
        {
            Step("1", "First", new[] { "3" }),
            Step("2", "Second", new[] { "1" }),
            Step("3", "Third", new[] { "2" })
        };
        var plan = PlanFromSteps(steps);
        var result = BuiltInFunctions.ValidateAndNormalizePlan(plan);
        var obj = result.AsObject();
        var errVal = obj.Get("error", null);
        Assert.NotNull(errVal);
        Assert.Equal(ValueType.String, errVal.Type);
        Assert.Contains("cycle", errVal.AsString());
    }

    [Fact]
    public void TopoSortSteps_ValidPlan_ReturnsOrderedSteps()
    {
        var steps = new List<RuntimeValue>
        {
            Step("2", "Second", new[] { "1" }),
            Step("1", "First"),
            Step("3", "Third", new[] { "2" })
        };
        var plan = PlanFromSteps(steps);
        var validated = BuiltInFunctions.ValidateAndNormalizePlan(plan);
        var ordered = BuiltInFunctions.TopoSortSteps(validated);
        Assert.NotNull(ordered);
        Assert.Equal(3, ordered.Count);
        Assert.Equal("1", ordered[0].AsObject().Get("id", null).AsString());
        Assert.Equal("2", ordered[1].AsObject().Get("id", null).AsString());
        Assert.Equal("3", ordered[2].AsObject().Get("id", null).AsString());
    }

    [Fact]
    public void TopoSortSteps_NoDeps_ReturnsSameOrder()
    {
        var steps = new List<RuntimeValue>
        {
            Step("a", "A"),
            Step("b", "B"),
            Step("c", "C")
        };
        var plan = PlanFromSteps(steps);
        var validated = BuiltInFunctions.ValidateAndNormalizePlan(plan);
        var ordered = BuiltInFunctions.TopoSortSteps(validated);
        Assert.NotNull(ordered);
        Assert.Equal(3, ordered.Count);
    }

    [Fact]
    public void CreateSubmitPlanTool_ReturnsTool()
    {
        var toolVal = BuiltInTools.CreateSubmitPlanTool();
        Assert.Equal(ValueType.Object, toolVal.Type);
        var tool = toolVal.AsObject() as ToolInstance;
        Assert.NotNull(tool);
        Assert.Equal("submit_plan", tool!.Name);
    }

    [Fact]
    public void SubmitPlanTool_ValidPlan_ReturnsAccepted()
    {
        var toolVal = BuiltInTools.CreateSubmitPlanTool();
        var tool = (ToolInstance)toolVal.AsObject();
        var args = new JsonObject();
        var steps = new List<RuntimeValue>
        {
            Step("1", "Do something")
        };
        args.Set("steps", RuntimeValue.Array(steps));
        var conversation = new ConversationInstance();
        var method = typeof(ConversationInstance).GetMethod("ExecuteToolOperation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var result = (RuntimeValue)method!.Invoke(conversation, new object[] { tool, RuntimeValue.Object(args) })!;
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        var accepted = obj.Get("accepted", null);
        Assert.NotNull(accepted);
        Assert.Equal(ValueType.Boolean, accepted.Type);
        Assert.True(accepted.AsBoolean());
        var stepCount = obj.Get("stepCount", null);
        Assert.NotNull(stepCount);
        Assert.Equal(1, stepCount.AsInteger());
    }

    [Fact]
    public void SubmitPlanTool_InvalidPlan_ReturnsNotAccepted()
    {
        var toolVal = BuiltInTools.CreateSubmitPlanTool();
        var tool = (ToolInstance)toolVal.AsObject();
        var args = new JsonObject();
        args.Set("steps", RuntimeValue.Array(new List<RuntimeValue>()));
        var conversation = new ConversationInstance();
        var method = typeof(ConversationInstance).GetMethod("ExecuteToolOperation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var result = (RuntimeValue)method!.Invoke(conversation, new object[] { tool, RuntimeValue.Object(args) })!;
        var obj = result.AsObject();
        var accepted = obj.Get("accepted", null);
        Assert.NotNull(accepted);
        Assert.False(accepted.AsBoolean());
        var errVal = obj.Get("error", null);
        Assert.NotNull(errVal);
    }

    [Fact]
    public void ExecutePlan_ValidPlan_RunsStepsInOrder()
    {
        var steps = new List<RuntimeValue>
        {
            Step("1", "Step one"),
            Step("2", "Step two", new[] { "1" })
        };
        var plan = PlanFromSteps(steps);
        var agent = new AgentInstance();
        agent.Initialize("Test", "test", "Test agent", (LLMClientInstance?)null, null);
        // Clear conversation so Think() returns Null immediately without calling Send();
        // this keeps the test deterministic and avoids dependency on TraceManager/dashboard/Conversation.Send.
        var convField = typeof(AgentInstance).GetField("_conversation", BindingFlags.NonPublic | BindingFlags.Instance);
        if (convField != null)
            convField.SetValue(agent, null);
        // Pass raw plan so executePlan validates and normalizes it internally.
        var args = new List<RuntimeValue> { plan, RuntimeValue.Object(agent) };
#pragma warning disable CS8625
        var result = BuiltInFunctions.CallBuiltIn("executePlan", args, null);
#pragma warning restore CS8625
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        var errVal = obj.Get("error", null);
        if (errVal != null && errVal.Type == ValueType.String)
        {
            Assert.Fail("executePlan failed: " + errVal.AsString());
            return;
        }
        var completed = obj.Get("completed", null);
        Assert.NotNull(completed);
        Assert.Equal(ValueType.Array, completed.Type);
        Assert.Equal(2, completed.AsArray().Count);
        var results = obj.Get("results", null);
        Assert.NotNull(results);
        Assert.Equal(2, results.AsArray().Count);
    }

    [Fact]
    public void ExecutePlan_InvalidAgent_ReturnsError()
    {
        var steps = new List<RuntimeValue> { Step("1", "Only step") };
        var plan = PlanFromSteps(steps);
        var validated = BuiltInFunctions.ValidateAndNormalizePlan(plan);
        var notAnAgent = new JsonObject();
        notAnAgent.Set("name", RuntimeValue.String("not an agent"));
        var args = new List<RuntimeValue> { validated, RuntimeValue.Object(notAnAgent) };
#pragma warning disable CS8625
        var result = BuiltInFunctions.CallBuiltIn("executePlan", args, null);
#pragma warning restore CS8625
        var obj = result.AsObject();
        var errVal = obj.Get("error", null);
        Assert.NotNull(errVal);
        Assert.Equal(ValueType.String, errVal.Type);
    }

    [Fact]
    public void DecomposeTask_NoArguments_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            BuiltInFunctions.CallBuiltIn("decomposeTask", new List<RuntimeValue>(), null));
        Assert.Contains("decomposeTask", ex.Message);
        Assert.Contains("instruction", ex.Message);
    }

    [Fact]
    public void DecomposeTask_FirstArgNotString_Throws()
    {
        var ex = Assert.Throws<Exception>(() =>
            BuiltInFunctions.CallBuiltIn("decomposeTask",
                new List<RuntimeValue> { RuntimeValue.Integer(42) }, null));
        Assert.Contains("decomposeTask", ex.Message);
        Assert.Contains("instruction", ex.Message);
    }
}
