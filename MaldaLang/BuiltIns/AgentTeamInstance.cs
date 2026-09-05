// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// A named set of agents plus the legal relations between them.
/// </summary>
public sealed class AgentTeamInstance : ObjectInstance
{
    private readonly Dictionary<string, AgentInstance> _members;
    private readonly GraphInstance _graph;
    private readonly List<AgentRelation> _relations;

    public AgentTeamInstance(
        Dictionary<string, AgentInstance> members,
        GraphInstance graph,
        List<AgentRelation> relations)
        : base(null)
    {
        _members = members;
        _graph = graph;
        _relations = relations;
    }

    public IReadOnlyDictionary<string, AgentInstance> Members => _members;
    public IReadOnlyList<AgentRelation> Relations => _relations;
    public GraphInstance Graph => _graph;

    public bool TryGetAgent(string name, out AgentInstance agent) =>
        _members.TryGetValue(name, out agent!);

    public bool TryGetRelation(string from, string to, out AgentRelation relation)
    {
        foreach (var candidate in _relations)
        {
            if (string.Equals(candidate.From, from, StringComparison.Ordinal)
                && string.Equals(candidate.To, to, StringComparison.Ordinal))
            {
                relation = candidate;
                return true;
            }
        }

        relation = default;
        return false;
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name is "get" or "members" or "relations" or "handoff" or "run")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }

        if (name == "graph")
            return RuntimeValue.Object(_graph);

        throw new Exception($"Undefined property '{name}' on AgentTeam.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter)
    {
        return methodName switch
        {
            "get" => GetMember(args),
            "members" => ListMembers(args),
            "relations" => ListRelations(args),
            "handoff" => AgentsStdLib.Handoff(this, args, interpreter),
            "run" => Run(args, interpreter),
            _ => throw new Exception($"Unknown AgentTeam method: {methodName}")
        };
    }

    private RuntimeValue GetMember(List<RuntimeValue> args)
    {
        BuiltInArity.Require("get", args, 1, 1, "name");
        if (args[0].Type != ValueType.String)
            throw new RuntimeException("get() expects a string agent name");
        var name = args[0].AsString();
        if (!_members.TryGetValue(name, out var agent))
            throw new RuntimeException($"team has no agent named '{name}'");
        return RuntimeValue.Object(agent);
    }

    private RuntimeValue ListMembers(List<RuntimeValue> args)
    {
        BuiltInArity.Require("members", args, 0, 0);
        var names = _members.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(RuntimeValue.String)
            .ToList();
        return RuntimeValue.Array(names);
    }

    private RuntimeValue ListRelations(List<RuntimeValue> args)
    {
        BuiltInArity.Require("relations", args, 0, 0);
        var items = new List<RuntimeValue>();
        foreach (var relation in _relations)
        {
            var obj = new JsonObject();
            obj.Set("from", RuntimeValue.String(relation.From));
            obj.Set("to", RuntimeValue.String(relation.To));
            obj.Set("rel", RuntimeValue.String(relation.Rel));
            obj.Set("contract", relation.Contract == null
                ? RuntimeValue.Null()
                : RuntimeValue.String(relation.Contract));
            items.Add(RuntimeValue.Object(obj));
        }

        return RuntimeValue.Array(items);
    }

    private RuntimeValue Run(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("run", args, 1, 1, "plan");
        if (args[0].Type != ValueType.Object)
        {
            throw new RuntimeException(
                "run() expects a plan object with steps; use executePlan(plan, team) or pass { steps: [...] }");
        }

        return BuiltInFunctions.CallBuiltIn(
            "executePlan",
            new List<RuntimeValue> { args[0], RuntimeValue.Object(this) },
            interpreter);
    }
}
