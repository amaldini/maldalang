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
    private static RuntimeValue BuiltInEnableAgentVerboseLogging(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("enableAgentVerboseLogging() expects 0 or 1 arguments");
        var enabled = args.Count == 0 || args[0].IsTruthy();
        ConversationInstance.EnableVerboseLogging(enabled);
        return RuntimeValue.Boolean(enabled);
    }

    private static RuntimeValue BuiltInSetAgentVerbosePhase(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("setAgentVerbosePhase() expects 1 string argument (phase label)");
        ConversationInstance.SetVerbosePhase(args[0].AsString());
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInSetAgentStatusBanner(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("setAgentStatusBanner() expects 1 string argument (status banner line)");
        ConversationInstance.SetStatusBanner(args[0].AsString());
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInReportRalphStatus(List<RuntimeValue> args)
    {
        if (args.Count < 5)
            throw new Exception("reportRalphStatus() expects at least 5 arguments (agentName, phase, iteration, maxIter, prdPercent)");
        var agentName = args[0].AsString();
        var phase = args[1].AsString();
        var iteration = args[2].Type == ValueType.Integer ? (int)args[2].AsInteger() : 0;
        var maxIter = args[3].Type == ValueType.Integer ? (int)args[3].AsInteger() : 0;
        var prdPercent = args[4].Type == ValueType.Integer ? (int)args[4].AsInteger() : 0;
        var validationOk = args.Count > 5 && args[5].Type == ValueType.Boolean && args[5].AsBoolean();
        var elapsedMs = args.Count > 6 && args[6].Type == ValueType.Integer ? args[6].AsInteger() : 0L;
        var promptTokens = args.Count > 7 && args[7].Type == ValueType.Integer ? (int)args[7].AsInteger() : 0;
        var completionTokens = args.Count > 8 && args[8].Type == ValueType.Integer ? (int)args[8].AsInteger() : 0;
        var costUsd = 0.0;
        if (args.Count > 9 && (args[9].Type == ValueType.Float || args[9].Type == ValueType.Integer))
            costUsd = args[9].Type == ValueType.Float ? args[9].AsFloat() : args[9].AsInteger();
        AgentDashboardService.Instance.ReportRalphStatus(agentName, phase, iteration, maxIter, prdPercent, validationOk, elapsedMs, promptTokens, completionTokens, costUsd);
        return RuntimeValue.Null();
    }
    
    private static RuntimeValue BuiltInGetSkillNames(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getSkillNames", args, 0, 0);
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return RuntimeValue.Array(new List<RuntimeValue>());
        var skillsPath = Path.Combine(userProfile, ".malda", "skills");
        if (!Directory.Exists(skillsPath))
            return RuntimeValue.Array(new List<RuntimeValue>());
        var files = Directory.GetFiles(skillsPath, "*.malda");
        var names = new List<RuntimeValue>();
        foreach (var f in files)
            names.Add(RuntimeValue.String(Path.GetFileNameWithoutExtension(f)));
        return RuntimeValue.Array(names);
    }
    
    private static RuntimeValue BuiltInLoadSkill(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("loadSkill", args, 1, 1, "name");
        if (interpreter == null)
            return RuntimeValue.Null();
        var name = args[0].AsString();
        if (string.IsNullOrEmpty(name))
            return RuntimeValue.Null();
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return RuntimeValue.Null();
        var path = Path.Combine(userProfile, ".malda", "skills", name + ".malda");
        if (!File.Exists(path))
            return RuntimeValue.Null();
        return interpreter.LoadSkillModule(path);
    }

    private static RuntimeValue BuiltInLoadSkillsFromDir(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("loadSkillsFromDir", args, 0, 1, "skillsPath?");
        if (interpreter == null)
            return RuntimeValue.Array(new List<RuntimeValue>());

        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var skillsPath = string.IsNullOrEmpty(userProfile)
            ? ""
            : Path.Combine(userProfile, ".malda", "skills");
        if (args.Count > 0 && args[0].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[0].AsString()))
            skillsPath = args[0].AsString();

        var results = new List<RuntimeValue>();
        if (string.IsNullOrWhiteSpace(skillsPath) || !Directory.Exists(skillsPath))
            return RuntimeValue.Array(results);

        foreach (var file in Directory.GetFiles(skillsPath, "*.malda").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            try
            {
                var skill = interpreter.LoadSkillModule(file);
                var wrapper = new JsonObject();
                wrapper.Set("name", RuntimeValue.String(name));
                if (skill.Type == ValueType.Object && skill.AsObject() is ObjectInstance skillObj)
                {
                    foreach (var key in skillObj.GetAllKeys())
                        wrapper.Set(key, skillObj.Get(key, null) ?? RuntimeValue.Null());
                }
                else if (skill.Type == ValueType.Null)
                {
                    wrapper.Set("error", RuntimeValue.String("skill file missing or empty"));
                }
                results.Add(RuntimeValue.Object(wrapper));
            }
            catch (Exception ex)
            {
                var err = new JsonObject();
                err.Set("name", RuntimeValue.String(name));
                err.Set("error", RuntimeValue.String(ex.Message));
                results.Add(RuntimeValue.Object(err));
            }
        }

        return RuntimeValue.Array(results);
    }
    
    private static RuntimeValue BuiltInCreateSubmitPlanTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createSubmitPlanTool", args, 0, 0);
        return BuiltInTools.CreateSubmitPlanTool();
    }

    private static RuntimeValue BuiltInCreateUpdatePlanTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createUpdatePlanTool", args, 0, 0);
        return BuiltInTools.CreateUpdatePlanTool();
    }

    private static RuntimeValue BuiltInCreateMarkStepTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createMarkStepTool", args, 0, 0);
        return BuiltInTools.CreateMarkStepTool();
    }
    
    private static RuntimeValue BuiltInRunProgram(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("runProgram", args, 1, 1, "program");
        return ProgramRunner.Run(args[0], interpreter);
    }

    private static RuntimeValue BuiltInExecutePlan(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("executePlan", args, 2, BuiltInArity.Unbounded, "plan, agent");
        var planVal = args[0];
        var agentVal = args[1];
        if (agentVal.Type != ValueType.Object)
            throw new Exception("executePlan second argument must be an Agent or AgentTeam instance");
        var agentObj = agentVal.AsObject();
        AgentTeamInstance? team = agentObj as AgentTeamInstance;
        AgentInstance? agentInstance = agentObj as AgentInstance;
        if (team == null && agentInstance == null)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("executePlan second argument must be an Agent or AgentTeam instance"));
            return RuntimeValue.Object(err);
        }
        // Accept already-normalized plan: any object with non-empty "steps" array is used as-is to avoid re-validation.
        RuntimeValue validation;
        if (planVal.Type == ValueType.Object)
        {
            var pObj = planVal.AsObject();
            var stepsVal = pObj.Get("steps", null);
            var hasStepsArray = stepsVal != null && stepsVal.Type == ValueType.Array && stepsVal.AsArray().Count > 0;
            if (hasStepsArray)
            {
                validation = planVal;
            }
            else
            {
                validation = ValidateAndNormalizePlan(planVal);
            }
        }
        else
        {
            validation = ValidateAndNormalizePlan(planVal);
        }
        if (validation.Type != ValueType.Object)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Invalid plan"));
            return RuntimeValue.Object(err);
        }
        var vObj = validation.AsObject();
        var errVal = vObj.Get("error", null);
        if (errVal != null && errVal.Type == ValueType.String)
        {
            var err = new JsonObject();
            err.Set("error", errVal);
            return RuntimeValue.Object(err);
        }
        var validatedPlanIdVal = vObj.Get("planId", null);
        string planId = validatedPlanIdVal != null && validatedPlanIdVal.Type == ValueType.String ? validatedPlanIdVal.AsString() : "";
        var orderedSteps = TopoSortSteps(validation);
        if (orderedSteps == null)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Plan has a cycle in step dependencies"));
            return RuntimeValue.Object(err);
        }
        if (team != null)
        {
            var hopError = AgentsStdLib.ValidateTeamPlan(team, orderedSteps);
            if (hopError != null)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String("executePlan " + hopError));
                return RuntimeValue.Object(err);
            }
        }

        var outTypeError = AgentsStdLib.ValidatePlanOutputTypes(orderedSteps, interpreter);
        if (outTypeError != null)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("executePlan " + outTypeError));
            return RuntimeValue.Object(err);
        }

        var completed = new List<RuntimeValue>();
        var failed = new List<RuntimeValue>();
        var skipped = new List<RuntimeValue>();
        var results = new List<RuntimeValue>();
        var priorOutputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var stepsById = new Dictionary<string, ObjectInstance>(StringComparer.Ordinal);
        var approvedById = new Dictionary<string, bool>(StringComparer.Ordinal);
        var skippedIds = new HashSet<string>(StringComparer.Ordinal);
        var failedIds = new HashSet<string>(StringComparer.Ordinal);
        var wantsThink = AgentsStdLib.WantsPlanThink(vObj);
        foreach (var step in orderedSteps)
        {
            if (step.Type != ValueType.Object)
                continue;
            var idVal = step.AsObject().Get("id", null);
            if (idVal.Type == ValueType.String && idVal.AsString().Length > 0)
                stepsById[idVal.AsString()] = step.AsObject();
        }
        foreach (var step in orderedSteps)
        {
            var so = step.AsObject();
            var stepIdVal = so.Get("id", null);
            var descVal = so.Get("description", null);
            string stepId = stepIdVal != null && stepIdVal.Type == ValueType.String ? stepIdVal.AsString() : "";
            string description = descVal != null && descVal.Type == ValueType.String ? descVal.AsString() : "";
            var outType = AgentsStdLib.ReadStepOutType(so);
            var thinkPrompt = AgentsStdLib.ApplyStepOutputAppendix(
                AgentsStdLib.BuildStepThinkPrompt(description, so, priorOutputs),
                outType,
                interpreter);
            var stepResult = new JsonObject();
            stepResult.Set("stepId", RuntimeValue.String(stepId));
            stepResult.Set("prompt", RuntimeValue.String(thinkPrompt));
            if (!string.IsNullOrEmpty(outType))
                stepResult.Set("out", RuntimeValue.String(outType));
            AgentInstance stepAgent;
            if (team != null)
            {
                var role = AgentsStdLib.ReadStepRole(so)!;
                if (!team.TryGetAgent(role, out stepAgent))
                {
                    var err = new JsonObject();
                    err.Set("error", RuntimeValue.String($"executePlan step '{stepId}' has unknown role '{role}'"));
                    return RuntimeValue.Object(err);
                }

                stepResult.Set("role", RuntimeValue.String(role));
                var incomingRel = AgentsStdLib.ReadIncomingRel(team, so, stepsById);
                if (incomingRel != null)
                    stepResult.Set("rel", RuntimeValue.String(incomingRel));

                var skipReason = AgentsStdLib.SkipReasonForStep(
                    team, so, stepsById, approvedById, skippedIds, failedIds);
                if (skipReason != null)
                {
                    stepResult.Set("skipped", RuntimeValue.Boolean(true));
                    stepResult.Set("reason", RuntimeValue.String(skipReason));
                    if (stepId.Length > 0)
                    {
                        skipped.Add(RuntimeValue.String(stepId));
                        skippedIds.Add(stepId);
                    }
                    results.Add(RuntimeValue.Object(stepResult));
                    continue;
                }
            }
            else
            {
                stepAgent = agentInstance!;
            }

            try
            {
                var resolved = AgentsStdLib.ResolveStepOutput(
                    so, stepAgent, thinkPrompt, wantsThink, interpreter);
                if (!resolved.Success)
                {
                    stepResult.Set("success", RuntimeValue.Boolean(false));
                    stepResult.Set("error", RuntimeValue.String(resolved.Error ?? "step output failed"));
                    if (stepId.Length > 0)
                        failedIds.Add(stepId);
                    failed.Add(RuntimeValue.String(stepId));
                    results.Add(RuntimeValue.Object(stepResult));
                    continue;
                }

                var output = resolved.Output;
                if (resolved.Data.Type != ValueType.Null)
                    stepResult.Set("data", resolved.Data);
                var approved = AgentsStdLib.ResolveStepApproved(so, output);
                stepResult.Set("success", RuntimeValue.Boolean(true));
                stepResult.Set("output", RuntimeValue.String(output));
                stepResult.Set("approved", RuntimeValue.Boolean(approved));
                if (stepId.Length > 0)
                {
                    priorOutputs[stepId] = output;
                    approvedById[stepId] = approved;
                }
                completed.Add(RuntimeValue.String(stepId));
            }
            catch (Exception ex)
            {
                stepResult.Set("success", RuntimeValue.Boolean(false));
                stepResult.Set("error", RuntimeValue.String(ex.Message));
                if (stepId.Length > 0)
                    failedIds.Add(stepId);
                failed.Add(RuntimeValue.String(stepId));
            }
            results.Add(RuntimeValue.Object(stepResult));
        }
        var outResult = new JsonObject();
        outResult.Set("planId", RuntimeValue.String(planId));
        outResult.Set("completed", RuntimeValue.Array(completed));
        outResult.Set("failed", RuntimeValue.Array(failed));
        outResult.Set("skipped", RuntimeValue.Array(skipped));
        outResult.Set("results", RuntimeValue.Array(results));
        return RuntimeValue.Object(outResult);
    }
    
    private static RuntimeValue BuiltInSetDefaultAgent(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count != 1)
            throw new Exception("setDefaultAgent(agent) expects exactly 1 argument: agent instance");
        
        if (args[0].Type != ValueType.Object)
            throw new Exception("setDefaultAgent() expects an Agent instance");
        
        var agentObj = args[0].AsObject();
        if (agentObj is not AgentInstance agent)
            throw new Exception("setDefaultAgent() expects an Agent instance");
        
        var effectiveInterpreter = interpreter ?? TranspiledBuiltinRuntime.GetOrCreateInterpreter();
        effectiveInterpreter._defaultAgent = agent;
        return RuntimeValue.Null();
    }
    
    private static RuntimeValue BuiltInDecomposeTask(List<RuntimeValue> args)
    {
        BuiltInArity.Require("decomposeTask", args, 1, 3, "instruction, client?, team?");
        if (args[0].Type != ValueType.String)
            throw new Exception("decomposeTask() instruction must be a string");
        var instruction = args[0].AsString();
        LLMClientInstance? client = null;
        AgentTeamInstance? team = null;
        if (args.Count >= 2 && args[1].Type == ValueType.Object)
        {
            var obj = args[1].AsObject();
            if (obj is AgentTeamInstance teamArg)
                team = teamArg;
            else if (obj is LLMClientInstance llmClient)
                client = llmClient;
            else
                throw new Exception("decomposeTask() second argument must be an LLMClient or AgentTeam");
        }
        else if (args.Count >= 2 && args[1].Type != ValueType.Null)
        {
            throw new Exception("decomposeTask() second argument must be an LLMClient or AgentTeam");
        }

        if (args.Count >= 3)
        {
            if (team != null)
                throw new Exception("decomposeTask() team was already passed as the second argument");
            if (args[2].Type != ValueType.Object || args[2].AsObject() is not AgentTeamInstance teamArg)
                throw new Exception("decomposeTask() third argument must be an AgentTeam");
            team = teamArg;
        }

        var systemPrompt = AgentsStdLib.BuildDecomposeSystemPrompt(team);
        var conv = new ConversationInstance();
        if (client != null)
            conv.Initialize(client, null, null, systemPrompt, null);
        else
            conv.Initialize(null, DefaultLocalLlm.GetDefaultLocalClient(), null, systemPrompt, null);
        conv.AddUserMessage(instruction);
        RuntimeValue response;
        try
        {
            response = conv.Send();
        }
        catch (Exception ex)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String($"LLM call failed: {ex.Message}"));
            return RuntimeValue.Object(err);
        }
        if (response.Type != ValueType.Object)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Unexpected response from LLM"));
            return RuntimeValue.Object(err);
        }
        var contentVal = response.AsObject().Get("content", null);
        if (contentVal == null || contentVal.Type != ValueType.String)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("No content in LLM response"));
            return RuntimeValue.Object(err);
        }
        var content = contentVal.AsString().Trim();
        var jsonStr = content;
        if (content.Contains("```"))
        {
            var start = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                start = content.IndexOf('\n', start) + 1;
                var end = content.IndexOf("```", start, StringComparison.Ordinal);
                if (end > start)
                    jsonStr = content.Substring(start, end - start).Trim();
            }
        }
        if (jsonStr.IndexOf('{') >= 0 && jsonStr.IndexOf('}') >= 0)
        {
            var first = jsonStr.IndexOf('{');
            var last = jsonStr.LastIndexOf('}');
            if (last > first)
                jsonStr = jsonStr.Substring(first, last - first + 1);
        }
        RuntimeValue parsed;
        try
        {
            parsed = BuiltInParseJSON(new List<RuntimeValue> { RuntimeValue.String(jsonStr) });
        }
        catch
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("LLM response was not valid JSON. Ensure the model returns only a JSON object with a 'steps' array."));
            return RuntimeValue.Object(err);
        }
        var validation = ValidateAndNormalizePlan(parsed);
        if (validation.Type != ValueType.Object)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Invalid plan structure from LLM"));
            return RuntimeValue.Object(err);
        }
        var vObj = validation.AsObject();
        var errVal = vObj.Get("error", null);
        if (errVal != null && errVal.Type == ValueType.String)
            return validation;
        if (team != null)
        {
            var stepsVal = vObj.Get("steps", null);
            if (stepsVal.Type == ValueType.Array)
            {
                var hopError = AgentsStdLib.ValidateTeamPlan(team, stepsVal.AsArray());
                if (hopError != null)
                {
                    var err = new JsonObject();
                    err.Set("error", RuntimeValue.String("decomposeTask " + hopError));
                    return RuntimeValue.Object(err);
                }
            }
        }

        return validation;
    }

    private static RuntimeValue BuiltInOnAgentProgress(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("onAgentProgress", args, 1, 1, "handlerOrChannel");
        if (args[0].Type == ValueType.String)
        {
            // Transpile-friendly: emit progress via componentLiveEmit(channel, …).
            ConversationInstance.SetAgentProgressLiveChannel(args[0].AsString());
            return RuntimeValue.Null();
        }

        if (args[0].Type != ValueType.Function)
            throw new Exception("onAgentProgress() expects a function handler(event) or a live channel string");
        if (interpreter == null)
            throw new Exception("onAgentProgress(handler) requires an interpreter; use onAgentProgress(\"channel\") in transpile");

        var handler = args[0].AsFunction();
        ConversationInstance.SetAgentProgressHandler(evt =>
        {
            try
            {
                interpreter.CallFunctionAsync(handler, new List<RuntimeValue> { evt })
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Never break the agent loop from a progress handler.
            }
        });
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInClearAgentProgress(List<RuntimeValue> args)
    {
        BuiltInArity.Require("clearAgentProgress", args, 0, 0);
        ConversationInstance.ClearAgentProgressHandler();
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInCancelThink(List<RuntimeValue> args)
    {
        BuiltInArity.Require("cancelThink", args, 1, 1, "channel");
        if (args[0].Type != ValueType.String)
            throw new Exception("cancelThink() expects a live channel string");
        ConversationInstance.RequestThinkCancel(args[0].AsString());
        return RuntimeValue.Null();
    }
    
    // --- Structured task planning: plan validation and topo-sort ---

    /// <summary>
    /// Validates a plan (object with "steps" or array of steps) and returns normalized plan or error object.
    /// Success: returns object with steps, planId (guid), optional taskSummary.
    /// Failure: returns object with "error" key (string message).
    /// </summary>
    public static RuntimeValue ValidateAndNormalizePlan(RuntimeValue planOrSteps)
    {
        List<RuntimeValue> steps;
        string? taskSummary = null;
        if (planOrSteps.Type == ValueType.Array)
        {
            steps = planOrSteps.AsArray();
        }
        else if (planOrSteps.Type == ValueType.Object)
        {
            var obj = planOrSteps.AsObject();
            var stepsVal = obj.Get("steps", null);
            if (stepsVal == null || stepsVal.Type != ValueType.Array)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String("Plan must have a 'steps' array"));
                return RuntimeValue.Object(err);
            }
            steps = stepsVal.AsArray();
            var summaryVal = obj.Get("taskSummary", null);
            if (summaryVal != null && summaryVal.Type == ValueType.String)
                taskSummary = summaryVal.AsString();
        }
        else
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Plan must be an object with 'steps' or an array of steps"));
            return RuntimeValue.Object(err);
        }

        if (steps == null || steps.Count == 0)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Plan must have at least one step"));
            return RuntimeValue.Object(err);
        }

        var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var normalizedSteps = new List<RuntimeValue>();
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.Type != ValueType.Object)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step at index {i} must be an object with 'id' and 'description'"));
                return RuntimeValue.Object(err);
            }
            var so = s.AsObject();
            var idVal = so.Get("id", null);
            var descVal = so.Get("description", null);
            if (idVal == null || idVal.Type != ValueType.String)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step at index {i} must have a string 'id'"));
                return RuntimeValue.Object(err);
            }
            var id = idVal.AsString();
            if (string.IsNullOrWhiteSpace(id))
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step at index {i} has empty 'id'"));
                return RuntimeValue.Object(err);
            }
            if (idToIndex.ContainsKey(id))
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Duplicate step id: '{id}'"));
                return RuntimeValue.Object(err);
            }
            idToIndex[id] = i;
            if (descVal == null || descVal.Type != ValueType.String)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step '{id}' must have a string 'description'"));
                return RuntimeValue.Object(err);
            }
            var dependsOnVal = so.Get("dependsOn", null);
            // Missing dependsOn (Null) is optional; if present it must be an array
            if (dependsOnVal.Type != ValueType.Null && dependsOnVal.Type != ValueType.Array)
            {
                var err = new JsonObject();
                err.Set("error", RuntimeValue.String($"Step '{id}': 'dependsOn' must be an array of step ids"));
                return RuntimeValue.Object(err);
            }
            normalizedSteps.Add(s);
        }

        // Second pass: validate dependsOn references (all ids are now in idToIndex)
        for (int i = 0; i < normalizedSteps.Count; i++)
        {
            var so = normalizedSteps[i].AsObject();
            var id = so.Get("id", null).AsString();
            var dependsOnVal = so.Get("dependsOn", null);
            if (dependsOnVal.Type != ValueType.Null && dependsOnVal.Type == ValueType.Array)
            {
                foreach (var dep in dependsOnVal.AsArray())
                {
                    if (dep.Type != ValueType.String)
                        continue;
                    var depId = dep.AsString();
                    if (!idToIndex.ContainsKey(depId))
                    {
                        var err = new JsonObject();
                        err.Set("error", RuntimeValue.String($"Step '{id}' depends on unknown step id '{depId}'"));
                        return RuntimeValue.Object(err);
                    }
                }
            }
        }

        // Cycle check via Kahn's algorithm: inDegree[i] = number of steps that step i depends on
        var idByIndex = new string[steps.Count];
        foreach (var kv in idToIndex) idByIndex[kv.Value] = kv.Key;
        var inDegree = new int[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            var depVal = normalizedSteps[i].AsObject().Get("dependsOn", null);
            inDegree[i] = (depVal != null && depVal.Type == ValueType.Array) ? depVal.AsArray().Count : 0;
        }
        var queue = new Queue<int>();
        for (int i = 0; i < steps.Count; i++)
            if (inDegree[i] == 0) queue.Enqueue(i);
        int count = 0;
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            count++;
            var uid = idByIndex[u];
            for (int v = 0; v < steps.Count; v++)
            {
                if (v == u) continue;
                var depValV = normalizedSteps[v].AsObject().Get("dependsOn", null);
                if (depValV == null || depValV.Type != ValueType.Array) continue;
                bool dependsOnU = false;
                foreach (var d in depValV.AsArray())
                {
                    if (d.Type == ValueType.String && d.AsString() == uid) { dependsOnU = true; break; }
                }
                if (dependsOnU)
                {
                    inDegree[v]--;
                    if (inDegree[v] == 0) queue.Enqueue(v);
                }
            }
        }
        if (count != steps.Count)
        {
            var err = new JsonObject();
            err.Set("error", RuntimeValue.String("Plan has a cycle in step dependencies"));
            return RuntimeValue.Object(err);
        }

        var planId = Guid.NewGuid().ToString();
        var outPlan = new JsonObject();
        outPlan.Set("steps", RuntimeValue.Array(normalizedSteps));
        outPlan.Set("planId", RuntimeValue.String(planId));
        if (!string.IsNullOrEmpty(taskSummary))
            outPlan.Set("taskSummary", RuntimeValue.String(taskSummary));
        return RuntimeValue.Object(outPlan);
    }

    /// <summary>
    /// Returns steps in topological order (dependency order). Returns null if cycle.
    /// </summary>
    public static List<RuntimeValue>? TopoSortSteps(RuntimeValue plan)
    {
        if (plan.Type != ValueType.Object) return null;
        var stepsVal = plan.AsObject().Get("steps", null);
        if (stepsVal == null || stepsVal.Type != ValueType.Array) return null;
        var steps = stepsVal.AsArray();
        if (steps.Count == 0) return new List<RuntimeValue>();
        var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.Type != ValueType.Object) return null;
            var id = s.AsObject().Get("id", null);
            if (id == null || id.Type != ValueType.String) return null;
            idToIndex[id.AsString()] = i;
        }
        var inDegree = new int[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            var depVal = steps[i].AsObject().Get("dependsOn", null);
            inDegree[i] = (depVal != null && depVal.Type == ValueType.Array) ? depVal.AsArray().Count : 0;
        }
        var queue = new Queue<int>();
        for (int i = 0; i < steps.Count; i++)
            if (inDegree[i] == 0) queue.Enqueue(i);
        var result = new List<RuntimeValue>();
        var idByIndex = new string[steps.Count];
        for (int i = 0; i < steps.Count; i++)
            idByIndex[i] = steps[i].AsObject().Get("id", null).AsString();
        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            result.Add(steps[u]);
            var uid = idByIndex[u];
            for (int v = 0; v < steps.Count; v++)
            {
                if (v == u) continue;
                var depVal = steps[v].AsObject().Get("dependsOn", null);
                if (depVal == null || depVal.Type != ValueType.Array) continue;
                bool dependsOnU = false;
                foreach (var d in depVal.AsArray())
                {
                    if (d.Type == ValueType.String && d.AsString() == uid) { dependsOnU = true; break; }
                }
                if (dependsOnU)
                {
                    inDegree[v]--;
                    if (inDegree[v] == 0) queue.Enqueue(v);
                }
            }
        }
        if (result.Count != steps.Count) return null;
        return result;
    }
}
