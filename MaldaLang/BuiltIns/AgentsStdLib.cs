// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Declarative multi-agent teams: <c>agents.define</c> / <c>agents.team</c>.
/// No flat alias and no new keyword. Construction does not auto-download a local model.
/// </summary>
public static class AgentsStdLib
{
    public const string DefaultKind = "Agent";
    public const string RelHandoff = "handoff";
    public const string RelDelegate = "delegate";
    public const string RelReview = "review";
    public const string RelConsult = "consult";
    public const string RelReject = "reject";

    private static readonly HashSet<string> AllowedRels = new(StringComparer.Ordinal)
    {
        RelHandoff, RelDelegate, RelReview, RelConsult, RelReject
    };

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    {
        DefaultKind
    };

    public static RuntimeValue Define(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("define", args, 1, 2, "spec, client?");
        var client = args.Count >= 2 ? args[1] : RuntimeValue.Null();
        return DefineFromSpec(args[0], client, interpreter);
    }

    public static RuntimeValue Team(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("team", args, 2, 3, "specs, topology, client?");
        if (args[0].Type != ValueType.Array)
            throw new RuntimeException("team() first argument must be an array of agent specs");
        if (args[1].Type != ValueType.Object || args[1].AsObject() is not GraphInstance graph)
            throw new RuntimeException("team() second argument must be a graph (graph directed { ... })");
        if (!graph.IsDirected)
            throw new RuntimeException("team() topology must be a directed graph");

        var client = args.Count >= 3 ? args[2] : RuntimeValue.Null();

        var specs = args[0].AsArray();
        if (specs.Count == 0)
            throw new RuntimeException("team() expects at least one agent spec");

        var members = new Dictionary<string, AgentInstance>(StringComparer.Ordinal);
        foreach (var specVal in specs)
        {
            var agent = (AgentInstance)DefineFromSpec(specVal, client, interpreter).AsObject();
            if (members.ContainsKey(agent.Name))
                throw new RuntimeException($"team() duplicate agent name '{agent.Name}'");
            members[agent.Name] = agent;
        }

        foreach (var nodeId in graph.Nodes.Keys)
        {
            if (!members.ContainsKey(nodeId))
                throw new RuntimeException($"team() graph node '{nodeId}' has no matching spec name");
        }

        var relations = new List<AgentRelation>();
        foreach (var node in graph.Nodes.Values)
        {
            foreach (var edge in node.Edges)
            {
                if (!members.ContainsKey(edge.SourceId))
                    throw new RuntimeException($"team() edge from unknown agent '{edge.SourceId}'");
                if (!members.ContainsKey(edge.TargetId))
                    throw new RuntimeException($"team() edge to unknown agent '{edge.TargetId}'");

                var rel = ReadEdgeString(edge, "rel") ?? RelHandoff;
                if (!AllowedRels.Contains(rel))
                {
                    throw new RuntimeException(
                        $"team() unknown rel '{rel}' on {edge.SourceId}->{edge.TargetId}; " +
                        "use handoff, delegate, review, consult, or reject");
                }

                var contract = ReadEdgeString(edge, "contract");
                relations.Add(new AgentRelation(edge.SourceId, edge.TargetId, rel, contract));
            }
        }

        foreach (var relation in relations)
        {
            if (relation.Rel is RelDelegate or RelConsult)
            {
                var from = members[relation.From];
                var to = members[relation.To];
                var description = BuildDelegateDescription(to, relation);
                from.AddSubAgent(to, description);
            }
        }

        return RuntimeValue.Object(new AgentTeamInstance(members, graph, relations));
    }

    public static RuntimeValue DefineFromSpec(RuntimeValue specVal, RuntimeValue clientVal, Interpreter? interpreter)
    {
        if (specVal.Type != ValueType.Object)
            throw new RuntimeException("define() spec must be an object");

        var spec = specVal.AsObject();
        if (SchemaRegistry.IsRegistered("AgentSpec"))
        {
            var check = BuiltInFunctions.CallBuiltIn(
                "validate",
                new List<RuntimeValue> { RuntimeValue.String("AgentSpec"), specVal },
                interpreter);
            if (check.Type == ValueType.Object)
            {
                var ok = check.AsObject().Get("ok", null);
                if (ok.Type == ValueType.Boolean && !ok.AsBoolean())
                {
                    var err = check.AsObject().Get("error", null);
                    var message = err.Type == ValueType.String ? err.AsString() : "AgentSpec validation failed";
                    throw new RuntimeException("define() spec failed AgentSpec: " + message);
                }
            }
        }

        var name = RequireSpecString(spec, "name");
        var role = RequireSpecString(spec, "role");
        var instructions = RequireSpecString(spec, "instructions");
        var kind = OptionalSpecString(spec, "kind") ?? DefaultKind;
        if (!AllowedKinds.Contains(kind))
        {
            throw new RuntimeException(
                $"define() kind '{kind}' is not supported; use '{DefaultKind}' " +
                "(specialized classes stay new CodingAgent / new DevAgent / …)");
        }

        ResolveClient(clientVal, out var llm, out var llama, out var bridge);

        var agent = new AgentInstance();
        agent.Initialize(name, role, instructions, llm, llama, bridge, interpreter?.GetInputProvider());
        if (interpreter != null)
            agent.SetInterpreter(interpreter);

        var tools = spec.Get("tools", null);
        if (tools.Type == ValueType.Array)
        {
            foreach (var toolName in tools.AsArray())
            {
                if (toolName.Type != ValueType.String)
                    throw new RuntimeException("define() tools entries must be strings");
                agent.AddToolByName(toolName.AsString());
            }
        }
        else if (tools.Type != ValueType.Null)
        {
            throw new RuntimeException("define() tools must be an array of tool names");
        }

        var memoryScope = OptionalSpecString(spec, "memoryScope");
        if (memoryScope != null)
            agent.CallMethod("setMemoryScope", new List<RuntimeValue> { RuntimeValue.String(memoryScope) });

        return RuntimeValue.Object(agent);
    }

    public static bool TryGetTeam(RuntimeValue value, out AgentTeamInstance team)
    {
        team = null!;
        if (value.Type != ValueType.Object)
            return false;
        if (value.AsObject() is not AgentTeamInstance found)
            return false;
        team = found;
        return true;
    }

    internal static RuntimeValue Handoff(AgentTeamInstance team, List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("handoff", args, 3, 3, "from, to, payload");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new RuntimeException("handoff() expects (from, to, payload) with string agent names");

        var from = args[0].AsString();
        var to = args[1].AsString();
        var payload = args[2];

        if (!team.TryGetRelation(from, to, out var relation))
            return HandoffError($"No relation from '{from}' to '{to}'");

        if (string.IsNullOrEmpty(relation.Contract))
            return HandoffOk(payload);

        var check = BuiltInFunctions.CallBuiltIn(
            "validate",
            new List<RuntimeValue> { RuntimeValue.String(relation.Contract), payload },
            interpreter);
        if (check.Type != ValueType.Object)
            return HandoffError("validate() returned a non-object");

        var obj = check.AsObject();
        var ok = obj.Get("ok", null);
        if (ok.Type == ValueType.Boolean && ok.AsBoolean())
            return check;

        var err = obj.Get("error", null);
        var message = err.Type == ValueType.String ? err.AsString() : "payload failed contract";
        return HandoffError($"Handoff {from}->{to} failed {relation.Contract}: {message}");
    }

    internal static string? ReadStepRole(ObjectInstance step)
    {
        var role = step.Get("role", null);
        if (role.Type == ValueType.String && role.AsString().Length > 0)
            return role.AsString();
        var agent = step.Get("agent", null);
        if (agent.Type == ValueType.String && agent.AsString().Length > 0)
            return agent.AsString();
        return null;
    }

    private static RuntimeValue HandoffOk(RuntimeValue payload)
    {
        var obj = new JsonObject();
        obj.Set("ok", RuntimeValue.Boolean(true));
        obj.Set("data", payload);
        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue HandoffError(string message)
    {
        var obj = new JsonObject();
        obj.Set("ok", RuntimeValue.Boolean(false));
        obj.Set("error", RuntimeValue.String(message));
        return RuntimeValue.Object(obj);
    }

    private static string BuildDelegateDescription(AgentInstance target, AgentRelation relation)
    {
        var contract = string.IsNullOrEmpty(relation.Contract)
            ? "Pass the task as the prompt."
            : $"Payload should match schema {relation.Contract}.";
        return $"{target.Role}. {relation.Rel} to {target.Name}. {contract}";
    }

    private static string? ReadEdgeString(GraphEdge edge, string key)
    {
        if (edge.Properties == null)
            return null;
        if (!edge.Properties.TryGetEntry(key, out var value))
            return null;
        if (value.Type != ValueType.String)
            return null;
        var text = value.AsString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static string RequireSpecString(ObjectInstance spec, string key)
    {
        var value = spec.Get(key, null);
        if (value.Type != ValueType.String || string.IsNullOrWhiteSpace(value.AsString()))
            throw new RuntimeException($"define() spec.{key} must be a non-empty string");
        return value.AsString();
    }

    private static string? OptionalSpecString(ObjectInstance spec, string key)
    {
        var value = spec.Get(key, null);
        if (value.Type == ValueType.Null)
            return null;
        if (value.Type != ValueType.String)
            throw new RuntimeException($"define() spec.{key} must be a string");
        var text = value.AsString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static void ResolveClient(
        RuntimeValue clientVal,
        out LLMClientInstance? llm,
        out LlamaCppClientInstance? llama,
        out LLMClientBridge.LLMClientBridgeInstance? bridge)
    {
        llm = null;
        llama = null;
        bridge = null;
        if (clientVal.Type == ValueType.Null)
            return;
        if (clientVal.Type != ValueType.Object)
            throw new RuntimeException("define() client must be an LLMClient, LlamaCppClient, or LLMClientBridge");

        var obj = clientVal.AsObject();
        if (obj is LLMClientInstance llmClient)
            llm = llmClient;
        else if (obj is LlamaCppClientInstance llamaClient)
            llama = llamaClient;
        else if (obj is LLMClientBridge.LLMClientBridgeInstance bridgeClient)
            bridge = bridgeClient;
        else
            throw new RuntimeException("define() client must be an LLMClient, LlamaCppClient, or LLMClientBridge");
    }
}

public readonly record struct AgentRelation(string From, string To, string Rel, string? Contract);
