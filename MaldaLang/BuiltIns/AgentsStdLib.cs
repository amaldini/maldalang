// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Linq;
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

    public const string KindCodingAgent = "CodingAgent";
    public const string KindGitAgent = "GitAgent";
    public const string KindHumanAgent = "HumanAgent";
    public const string KindDevAgent = "DevAgent";
    public const string KindMaldaCodingAgent = "MALDACodingAgent";

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    {
        DefaultKind,
        KindCodingAgent,
        KindGitAgent,
        KindHumanAgent,
        KindDevAgent,
        KindMaldaCodingAgent
    };

    private static readonly string AllowedKindsList =
        "Agent, CodingAgent, GitAgent, HumanAgent, DevAgent, or MALDACodingAgent";

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
                $"define() kind '{kind}' is not supported; use {AllowedKindsList}");
        }

        var workingDirectory = OptionalSpecString(spec, "workingDirectory");
        var includeSymbols = OptionalSpecBool(spec, "includeSymbols");
        var readOnly = OptionalSpecBool(spec, "readOnly");
        var prdAuthorOnly = OptionalSpecBool(spec, "prdAuthorOnly");

        if (kind == DefaultKind && workingDirectory != null)
        {
            throw new RuntimeException(
                "define() spec.workingDirectory applies to CodingAgent, GitAgent, HumanAgent, DevAgent, and MALDACodingAgent");
        }

        if (kind != KindDevAgent)
        {
            if (includeSymbols.HasValue || readOnly.HasValue || prdAuthorOnly.HasValue)
            {
                throw new RuntimeException(
                    "define() spec.includeSymbols / readOnly / prdAuthorOnly apply only to kind DevAgent");
            }
        }

        ResolveClient(clientVal, out var llm, out var llama, out var bridge);

        var agent = CreateFromKind(
            kind,
            name,
            role,
            instructions,
            llm,
            llama,
            bridge,
            workingDirectory,
            includeSymbols ?? false,
            readOnly ?? false,
            prdAuthorOnly ?? false,
            interpreter?.GetInputProvider());
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

    internal static RuntimeValue Handoff(AgentTeamInstance team, List<RuntimeValue> args, Interpreter? interpreter) =>
        Hop("handoff", RelHandoff, team, args, interpreter, verdictKey: null);

    internal static RuntimeValue Review(AgentTeamInstance team, List<RuntimeValue> args, Interpreter? interpreter) =>
        Hop("review", RelReview, team, args, interpreter, verdictKey: "approved");

    internal static RuntimeValue Reject(AgentTeamInstance team, List<RuntimeValue> args, Interpreter? interpreter) =>
        Hop("reject", RelReject, team, args, interpreter, verdictKey: "rejected");

    internal static RuntimeValue Consult(AgentTeamInstance team, List<RuntimeValue> args, Interpreter? interpreter) =>
        Hop("consult", RelConsult, team, args, interpreter, verdictKey: null);

    private static RuntimeValue Hop(
        string method,
        string requiredRel,
        AgentTeamInstance team,
        List<RuntimeValue> args,
        Interpreter? interpreter,
        string? verdictKey)
    {
        BuiltInArity.Require(method, args, 3, 4, "from, to, payload, think?");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new RuntimeException($"{method}() expects (from, to, payload, think?) with string agent names");

        var from = args[0].AsString();
        var to = args[1].AsString();
        var payload = args[2];
        var option = args.Count >= 4 ? args[3] : RuntimeValue.Null();
        var think = WantsThink(option, method);
        var verdict = ReadOptionVerdict(option, verdictKey);
        var label = char.ToUpperInvariant(method[0]) + method[1..];

        if (!team.TryGetRelation(from, to, requiredRel, out var relation))
            return HopError($"No {requiredRel} relation from '{from}' to '{to}'");

        RuntimeValue validated = payload;
        if (!string.IsNullOrEmpty(relation.Contract))
        {
            var check = BuiltInFunctions.CallBuiltIn(
                "validate",
                new List<RuntimeValue> { RuntimeValue.String(relation.Contract), payload },
                interpreter);
            if (check.Type != ValueType.Object)
                return HopError("validate() returned a non-object");

            var obj = check.AsObject();
            var ok = obj.Get("ok", null);
            if (ok.Type != ValueType.Boolean || !ok.AsBoolean())
            {
                var err = obj.Get("error", null);
                var message = err.Type == ValueType.String ? err.AsString() : "payload failed contract";
                return HopError($"{label} {from}->{to} failed {relation.Contract}: {message}");
            }

            var data = obj.Get("data", null);
            if (data.Type != ValueType.Null)
                validated = data;
        }

        RuntimeValue? response = null;
        if (think)
        {
            if (!team.TryGetAgent(to, out var target))
                return HopError($"team has no agent named '{to}'");

            try
            {
                response = target.Think(ToThinkArg(validated, interpreter));
                if (verdict == null)
                    verdict = ReadResponseVerdict(response, verdictKey);
            }
            catch (Exception ex)
            {
                return HopError($"{label} {from}->{to} think() failed: {ex.Message}");
            }
        }

        return HopOk(validated, response, verdictKey, verdict ?? (verdictKey != null ? true : null));
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

    /// <summary>
    /// Team plans must name a known member on every step. A <c>dependsOn</c> hop
    /// between different roles must match a declared relation. Same-role
    /// continuation does not need a self-edge.
    /// </summary>
    internal static string? ValidateTeamPlan(AgentTeamInstance team, IReadOnlyList<RuntimeValue> steps)
    {
        var byId = new Dictionary<string, ObjectInstance>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (step.Type != ValueType.Object)
                continue;
            var so = step.AsObject();
            var idVal = so.Get("id", null);
            var id = idVal.Type == ValueType.String ? idVal.AsString() : "";
            var role = ReadStepRole(so);
            if (role == null)
                return $"step '{id}' is missing role";
            if (!team.TryGetAgent(role, out _))
                return $"step '{id}' has unknown role '{role}'";
            if (id.Length > 0)
                byId[id] = so;
        }

        foreach (var step in steps)
        {
            if (step.Type != ValueType.Object)
                continue;
            var so = step.AsObject();
            var idVal = so.Get("id", null);
            var id = idVal.Type == ValueType.String ? idVal.AsString() : "";
            var role = ReadStepRole(so);
            if (role == null)
                continue;
            var deps = so.Get("dependsOn", null);
            if (deps.Type != ValueType.Array)
                continue;
            foreach (var dep in deps.AsArray())
            {
                if (dep.Type != ValueType.String)
                    continue;
                if (!byId.TryGetValue(dep.AsString(), out var pred))
                    continue;
                var predRole = ReadStepRole(pred);
                if (predRole == null || string.Equals(predRole, role, StringComparison.Ordinal))
                    continue;
                if (!team.TryGetRelation(predRole, role, out _))
                {
                    return $"step '{id}' has no relation from '{predRole}' to '{role}'";
                }
            }
        }

        return null;
    }

    internal static string BuildDecomposeSystemPrompt(AgentTeamInstance? team)
    {
        if (team == null)
        {
            return "You are a task decomposer. Given a task description, respond with ONLY a valid JSON object and no other text. The JSON must have this exact shape: {\"steps\": [{\"id\": \"1\", \"description\": \"...\", \"dependsOn\": []}, ...]}. Each step must have \"id\" (unique string) and \"description\" (string). Optional \"dependsOn\" is an array of step ids that must complete before this step. No cycles. No markdown, no explanation.";
        }

        var members = new List<string>();
        foreach (var name in team.Members.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            var agent = team.Members[name];
            members.Add($"- {name} (kind {agent.Kind}, role {agent.Role}): {agent.Instructions}");
        }

        var rels = new List<string>();
        foreach (var relation in team.Relations)
        {
            var contract = string.IsNullOrEmpty(relation.Contract)
                ? ""
                : $" contract {relation.Contract}";
            rels.Add($"- {relation.From} -{relation.Rel}->{relation.To}{contract}");
        }

        var roster = members.Count == 0 ? "(none)" : string.Join("\n", members);
        var graph = rels.Count == 0 ? "(none)" : string.Join("\n", rels);
        var names = string.Join(", ", team.Members.Keys.OrderBy(n => n, StringComparer.Ordinal));
        return
            "You are a task decomposer for a multi-agent team. Respond with ONLY a valid JSON object and no other text. " +
            "The JSON must have this exact shape: {\"steps\": [{\"id\": \"1\", \"description\": \"...\", \"role\": \"MemberName\", \"dependsOn\": []}, ...]}. " +
            "Each step must have \"id\" (unique string), \"description\" (string), and \"role\" set to exactly one team member name. " +
            "Optional \"dependsOn\" is an array of step ids. When a step depends on another step with a different role, that hop must match a declared relation. " +
            "Same-role continuation does not need a self-edge. A reject hop runs only when the predecessor is not approved; other hops run only when the predecessor is approved (default approved). " +
            "No cycles. No markdown, no explanation.\n" +
            $"Member names: {names}\nMembers:\n{roster}\nRelations:\n{graph}";
    }

    internal static string BuildStepThinkPrompt(
        string description,
        ObjectInstance step,
        IReadOnlyDictionary<string, string> priorOutputs)
    {
        var deps = step.Get("dependsOn", null);
        if (deps.Type != ValueType.Array || deps.AsArray().Count == 0)
            return description;

        var parts = new List<string> { description };
        foreach (var dep in deps.AsArray())
        {
            if (dep.Type != ValueType.String)
                continue;
            var id = dep.AsString();
            priorOutputs.TryGetValue(id, out var output);
            parts.Add($"Prior step {id}:\n{output ?? ""}");
        }

        return parts.Count == 1 ? description : string.Join("\n\n", parts);
    }

    internal static string? ReadIncomingRel(
        AgentTeamInstance team,
        ObjectInstance step,
        IReadOnlyDictionary<string, ObjectInstance> byId)
    {
        var role = ReadStepRole(step);
        if (role == null)
            return null;
        var deps = step.Get("dependsOn", null);
        if (deps.Type != ValueType.Array)
            return null;
        foreach (var dep in deps.AsArray())
        {
            if (dep.Type != ValueType.String)
                continue;
            if (!byId.TryGetValue(dep.AsString(), out var pred))
                continue;
            var predRole = ReadStepRole(pred);
            if (predRole == null || string.Equals(predRole, role, StringComparison.Ordinal))
                continue;
            if (team.TryGetRelation(predRole, role, out var relation))
                return relation.Rel;
        }

        return null;
    }

    internal static bool WantsPlanThink(ObjectInstance plan)
    {
        var think = plan.Get("think", null);
        return think.Type != ValueType.Boolean || think.AsBoolean();
    }

    internal static bool ResolveStepApproved(ObjectInstance step, string output)
    {
        var fixture = step.Get("approved", null);
        if (fixture.Type == ValueType.Boolean)
            return fixture.AsBoolean();
        var rejected = step.Get("rejected", null);
        if (rejected.Type == ValueType.Boolean)
            return !rejected.AsBoolean();
        var fromOutput = ReadJsonBool(output, "approved");
        if (fromOutput != null)
            return fromOutput.Value;
        var rejectedOut = ReadJsonBool(output, "rejected");
        if (rejectedOut != null)
            return !rejectedOut.Value;
        return true;
    }

    internal static string? SkipReasonForStep(
        AgentTeamInstance team,
        ObjectInstance step,
        IReadOnlyDictionary<string, ObjectInstance> byId,
        IReadOnlyDictionary<string, bool> approvedById,
        IReadOnlySet<string> skipped,
        IReadOnlySet<string> failed)
    {
        var role = ReadStepRole(step);
        if (role == null)
            return null;
        var deps = step.Get("dependsOn", null);
        if (deps.Type != ValueType.Array)
            return null;
        foreach (var dep in deps.AsArray())
        {
            if (dep.Type != ValueType.String)
                continue;
            var id = dep.AsString();
            if (skipped.Contains(id))
                return $"dependency '{id}' skipped";
            if (failed.Contains(id))
                return $"dependency '{id}' failed";
            if (!byId.TryGetValue(id, out var pred))
                continue;
            var predRole = ReadStepRole(pred);
            if (predRole == null || string.Equals(predRole, role, StringComparison.Ordinal))
                continue;
            if (!team.TryGetRelation(predRole, role, out var relation))
                continue;
            var predApproved = !approvedById.TryGetValue(id, out var ok) || ok;
            if (string.Equals(relation.Rel, RelReject, StringComparison.Ordinal))
            {
                if (predApproved)
                    return $"reject hop from '{id}' skipped because it was approved";
            }
            else if (!predApproved)
            {
                return $"hop from '{id}' skipped because it was not approved";
            }
        }

        return null;
    }

    private static bool WantsThink(RuntimeValue option, string method)
    {
        if (option.Type == ValueType.Null)
            return false;
        if (option.Type == ValueType.Boolean)
            return option.AsBoolean();
        if (option.Type == ValueType.Object)
        {
            var obj = option.AsObject();
            var think = obj.Get("think", null);
            if (think.Type == ValueType.Boolean)
                return think.AsBoolean();
            var run = obj.Get("run", null);
            if (run.Type == ValueType.Boolean)
                return run.AsBoolean();
            return false;
        }

        throw new RuntimeException($"{method}() fourth argument must be a boolean or {{ think: bool }}");
    }

    private static bool? ReadOptionVerdict(RuntimeValue option, string? verdictKey)
    {
        if (verdictKey == null || option.Type != ValueType.Object)
            return null;
        var value = option.AsObject().Get(verdictKey, null);
        return value.Type == ValueType.Boolean ? value.AsBoolean() : null;
    }

    private static bool? ReadResponseVerdict(RuntimeValue? response, string? verdictKey)
    {
        if (verdictKey == null || response == null)
            return null;
        if (response.Type == ValueType.Object)
        {
            var value = response.AsObject().Get(verdictKey, null);
            if (value.Type == ValueType.Boolean)
                return value.AsBoolean();
            var content = response.AsObject().Get("content", null);
            if (content.Type == ValueType.String)
                return ReadJsonBool(content.AsString(), verdictKey);
        }

        if (response.Type == ValueType.String)
            return ReadJsonBool(response.AsString(), verdictKey);
        return null;
    }

    private static bool? ReadJsonBool(string text, string key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty(key, out var prop))
                return null;
            return prop.ValueKind switch
            {
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                _ => null
            };
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static RuntimeValue ToThinkArg(RuntimeValue payload, Interpreter? interpreter)
    {
        if (payload.Type == ValueType.String)
            return payload;
        if (payload.Type == ValueType.Object && payload.AsObject() is PromptInstance)
            return payload;

        var json = BuiltInFunctions.CallBuiltIn(
            "toJSON",
            new List<RuntimeValue> { payload },
            interpreter);
        if (json.Type == ValueType.String)
            return json;
        return RuntimeValue.String(payload.ToString() ?? "");
    }

    private static RuntimeValue HopOk(
        RuntimeValue payload,
        RuntimeValue? response,
        string? verdictKey,
        bool? verdict)
    {
        var obj = new JsonObject();
        obj.Set("ok", RuntimeValue.Boolean(true));
        obj.Set("data", payload);
        if (verdictKey != null && verdict != null)
            obj.Set(verdictKey, RuntimeValue.Boolean(verdict.Value));
        if (response != null)
            obj.Set("response", response);
        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue HopError(string message)
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

    private static bool? OptionalSpecBool(ObjectInstance spec, string key)
    {
        var value = spec.Get(key, null);
        if (value.Type == ValueType.Null)
            return null;
        if (value.Type != ValueType.Boolean)
            throw new RuntimeException($"define() spec.{key} must be a boolean");
        return value.AsBoolean();
    }

    /// <summary>
    /// Builds a specialized instance without calling constructors that auto-download
    /// <see cref="DefaultLocalLlm"/>. The three-client overloads register kind tools
    /// even when every client is null.
    /// </summary>
    private static AgentInstance CreateFromKind(
        string kind,
        string name,
        string role,
        string instructions,
        LLMClientInstance? llm,
        LlamaCppClientInstance? llama,
        LLMClientBridge.LLMClientBridgeInstance? bridge,
        string? workingDirectory,
        bool includeSymbols,
        bool readOnly,
        bool prdAuthorOnly,
        IInputProvider? inputProvider)
    {
        var workdir = workingDirectory ?? ".";
        return kind switch
        {
            DefaultKind => CreateBaseAgent(name, role, instructions, llm, llama, bridge, inputProvider),
            KindCodingAgent => new CodingAgentInstance(
                name, role, instructions, llm, llama, bridge, workdir, inputProvider),
            KindGitAgent => new GitAgentInstance(
                name, role, instructions, llm, llama, bridge, workdir, inputProvider),
            KindHumanAgent => new HumanAgentInstance(
                name, role, instructions, llm, llama, bridge, workdir, inputProvider),
            KindDevAgent => new DevAgentInstance(
                name, role, instructions, llm, llama, bridge, workdir, includeSymbols,
                inputProvider, readOnly, prdAuthorOnly),
            KindMaldaCodingAgent => new MALDACodingAgentInstance(
                name, role, instructions, llm, llama, bridge, workdir, inputProvider),
            _ => throw new RuntimeException(
                $"define() kind '{kind}' is not supported; use {AllowedKindsList}")
        };
    }

    private static AgentInstance CreateBaseAgent(
        string name,
        string role,
        string instructions,
        LLMClientInstance? llm,
        LlamaCppClientInstance? llama,
        LLMClientBridge.LLMClientBridgeInstance? bridge,
        IInputProvider? inputProvider)
    {
        var agent = new AgentInstance();
        agent.Initialize(name, role, instructions, llm, llama, bridge, inputProvider);
        return agent;
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
