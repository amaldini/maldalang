// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Reflection;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class AgentsTeamTests : TestBase
{
    [Fact]
    public void Define_CreatesAgentFromSpec_WithoutClient()
    {
        var source = """
            var writer = agents.define({
                name: "Writer",
                role: "programmer",
                instructions: "Write small diffs."
            });
            io.print(writer.name);
            io.print(writer.role);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Writer", lines[0]);
        Assert.Equal("programmer", lines[1]);
    }

    [Fact]
    public void Define_IsNotAFlatAlias()
    {
        var source = """
            var threw = false;
            try {
                define({ name: "X", role: "r", instructions: "i" });
            } catch (e) {
                threw = true;
            }
            io.print(threw);
            io.print(agents != null);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void Define_RejectsMissingName()
    {
        var source = """
            var threw = false;
            try {
                agents.define({ role: "r", instructions: "i" });
            } catch (e) {
                threw = true;
            }
            io.print(threw);
            """;
        Assert.Equal("true", RunProgram(source).Trim());
    }

    [Fact]
    public void Team_BindsGraphAndListsMembers()
    {
        var source = """
            schema DraftCode { path: string; summary: string; }

            var specs = [
                { name: "Writer", role: "programmer", instructions: "Write diffs." },
                { name: "Reviewer", role: "reviewer", instructions: "Find bugs." }
            ];
            var topology = graph directed {
                nodes: ["Writer", "Reviewer"],
                edges: [
                    { from: "Writer", to: "Reviewer", rel: "handoff", contract: "DraftCode" }
                ]
            };
            var team = agents.team(specs, topology);
            io.print(team.members().length);
            io.print(team.get("Writer").name);
            io.print(team.relations()[0].from);
            io.print(team.relations()[0].to);
            io.print(team.relations()[0].rel);
            io.print(team.relations()[0].contract);
            io.print(team.graph.nodeCount());
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("2", lines[0]);
        Assert.Equal("Writer", lines[1]);
        Assert.Equal("Writer", lines[2]);
        Assert.Equal("Reviewer", lines[3]);
        Assert.Equal("handoff", lines[4]);
        Assert.Equal("DraftCode", lines[5]);
        Assert.Equal("2", lines[6]);
    }

    [Fact]
    public void Team_RejectsUnknownGraphNode()
    {
        var source = """
            var threw = false;
            try {
                agents.team(
                    [{ name: "Writer", role: "programmer", instructions: "Write." }],
                    graph directed {
                        nodes: ["Writer", "Reviewer"],
                        edges: []
                    }
                );
            } catch (e) {
                threw = true;
            }
            io.print(threw);
            """;
        Assert.Equal("true", RunProgram(source).Trim());
    }

    [Fact]
    public void Team_RejectsUnknownRel()
    {
        var source = """
            var threw = false;
            try {
                agents.team(
                    [
                        { name: "A", role: "a", instructions: "a" },
                        { name: "B", role: "b", instructions: "b" }
                    ],
                    graph directed {
                        nodes: ["A", "B"],
                        edges: [{ from: "A", to: "B", rel: "teleport" }]
                    }
                );
            } catch (e) {
                threw = true;
            }
            io.print(threw);
            """;
        Assert.Equal("true", RunProgram(source).Trim());
    }

    [Fact]
    public void Handoff_ValidatesContract()
    {
        var source = """
            schema DraftCode { path: string; summary: string; }

            var team = agents.team(
                [
                    { name: "Writer", role: "programmer", instructions: "Write." },
                    { name: "Reviewer", role: "reviewer", instructions: "Review." }
                ],
                graph directed {
                    nodes: ["Writer", "Reviewer"],
                    edges: [
                        { from: "Writer", to: "Reviewer", rel: "handoff", contract: "DraftCode" }
                    ]
                }
            );

            var ok = team.handoff("Writer", "Reviewer", { path: "a.malda", summary: "add comments" });
            io.print(ok.ok);
            io.print(ok.data.path);

            var bad = team.handoff("Writer", "Reviewer", { path: 1, summary: "nope" });
            io.print(bad.ok);

            var missing = team.handoff("Reviewer", "Writer", { path: "a.malda", summary: "back" });
            io.print(missing.ok);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("a.malda", lines[1]);
        Assert.Equal("false", lines[2]);
        Assert.Equal("false", lines[3]);
    }

    [Fact]
    public void ExecutePlan_RoutesStepsByRole()
    {
        var specs = new List<RuntimeValue>
        {
            Spec("Writer", "programmer", "Write."),
            Spec("Reviewer", "reviewer", "Review.")
        };
        var graph = new GraphInstance(true);
        graph.CallMethod("addNode", new List<RuntimeValue> { RuntimeValue.String("Writer") }, null!);
        graph.CallMethod("addNode", new List<RuntimeValue> { RuntimeValue.String("Reviewer") }, null!);
        var props = new DictionaryInstance();
        props.SetEntry("rel", RuntimeValue.String("handoff"));
        graph.CallMethod("addEdge", new List<RuntimeValue>
        {
            RuntimeValue.String("Writer"),
            RuntimeValue.String("Reviewer"),
            RuntimeValue.Null(),
            RuntimeValue.Object(props)
        }, null!);

        var teamVal = AgentsStdLib.Team(
            new List<RuntimeValue> { RuntimeValue.Array(specs), RuntimeValue.Object(graph) },
            null);
        var team = Assert.IsType<AgentTeamInstance>(teamVal.AsObject());
        NullConversation(team.Members["Writer"]);
        NullConversation(team.Members["Reviewer"]);

        var plan = new JsonObject();
        plan.Set("steps", RuntimeValue.Array(new List<RuntimeValue>
        {
            Step("write", "Write comments", "Writer"),
            Step("review", "Review the draft", "Reviewer", new[] { "write" })
        }));

        var result = BuiltInFunctions.CallBuiltIn(
            "executePlan",
            new List<RuntimeValue> { RuntimeValue.Object(plan), teamVal },
            null);
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        var err = obj.Get("error", null);
        if (err.Type == ValueType.String)
            Assert.Fail("executePlan failed: " + err.AsString());

        Assert.Equal(2, obj.Get("completed", null).AsArray().Count);
        var results = obj.Get("results", null).AsArray();
        Assert.Equal("Writer", results[0].AsObject().Get("role", null).AsString());
        Assert.Equal("Reviewer", results[1].AsObject().Get("role", null).AsString());
    }

    [Fact]
    public void ExecutePlan_UnknownRole_ReturnsError()
    {
        var specs = new List<RuntimeValue> { Spec("Writer", "programmer", "Write.") };
        var graph = new GraphInstance(true);
        graph.CallMethod("addNode", new List<RuntimeValue> { RuntimeValue.String("Writer") }, null!);
        var teamVal = AgentsStdLib.Team(
            new List<RuntimeValue> { RuntimeValue.Array(specs), RuntimeValue.Object(graph) },
            null);
        NullConversation(((AgentTeamInstance)teamVal.AsObject()).Members["Writer"]);

        var plan = new JsonObject();
        plan.Set("steps", RuntimeValue.Array(new List<RuntimeValue>
        {
            Step("write", "Write comments", "Missing")
        }));

        var result = BuiltInFunctions.CallBuiltIn(
            "executePlan",
            new List<RuntimeValue> { RuntimeValue.Object(plan), teamVal },
            null);
        var err = result.AsObject().Get("error", null);
        Assert.Equal(ValueType.String, err.Type);
        Assert.Contains("unknown role", err.AsString());
    }

    [Fact]
    public void ExecutePlan_MissingRoleOnTeam_ReturnsError()
    {
        var specs = new List<RuntimeValue> { Spec("Writer", "programmer", "Write.") };
        var graph = new GraphInstance(true);
        graph.CallMethod("addNode", new List<RuntimeValue> { RuntimeValue.String("Writer") }, null!);
        var teamVal = AgentsStdLib.Team(
            new List<RuntimeValue> { RuntimeValue.Array(specs), RuntimeValue.Object(graph) },
            null);

        var plan = new JsonObject();
        var step = new JsonObject();
        step.Set("id", RuntimeValue.String("write"));
        step.Set("description", RuntimeValue.String("Write"));
        plan.Set("steps", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(step) }));

        var result = BuiltInFunctions.CallBuiltIn(
            "executePlan",
            new List<RuntimeValue> { RuntimeValue.Object(plan), teamVal },
            null);
        var err = result.AsObject().Get("error", null);
        Assert.Equal(ValueType.String, err.Type);
        Assert.Contains("missing role", err.AsString());
    }

    private static RuntimeValue Spec(string name, string role, string instructions)
    {
        var spec = new JsonObject();
        spec.Set("name", RuntimeValue.String(name));
        spec.Set("role", RuntimeValue.String(role));
        spec.Set("instructions", RuntimeValue.String(instructions));
        return RuntimeValue.Object(spec);
    }

    private static RuntimeValue Step(string id, string description, string role, string[]? dependsOn = null)
    {
        var step = new JsonObject();
        step.Set("id", RuntimeValue.String(id));
        step.Set("description", RuntimeValue.String(description));
        step.Set("role", RuntimeValue.String(role));
        if (dependsOn != null)
        {
            var deps = dependsOn.Select(RuntimeValue.String).Cast<RuntimeValue>().ToList();
            step.Set("dependsOn", RuntimeValue.Array(deps));
        }
        return RuntimeValue.Object(step);
    }

    private static void NullConversation(AgentInstance agent)
    {
        var convField = typeof(AgentInstance).GetField("_conversation", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(convField);
        convField!.SetValue(agent, null);
    }
}
