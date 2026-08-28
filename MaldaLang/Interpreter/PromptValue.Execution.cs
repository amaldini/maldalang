// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public partial class PromptValue
{
    public const string GatherNotesMarker = "\n\nGathered notes:\n";
    public const string GatherExtractSystemSuffix =
        "\nThe gather step already used tools. Reply with structured JSON only; do not call tools.";

    private async Task<RuntimeValue> BuildPromptInstanceAsync(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        if (arguments.Count != Declaration.Parameters.Count)
        {
            throw new RuntimeException($"Expected {Declaration.Parameters.Count} arguments but got {arguments.Count}.");
        }

        var environment = new Environment(Closure);
        for (int i = 0; i < Declaration.Parameters.Count; i++)
        {
            environment.Define(Declaration.Parameters[i], arguments[i]);
        }

        var previousEnvironment = interpreter._environment;

        try
        {
            interpreter._environment = environment;

            string? system = null;
            string? user = null;
            string? model = null;
            double? temperature = null;
            List<string>? tools = null;
            List<string>? gather = null;
            int? maxTokens = null;
            List<PromptExample>? examples = null;

            if (Declaration.BodyType == PromptBodyType.ObjectLiteral)
            {
                var bodyValue = await interpreter.EvaluateAsync(Declaration.ObjectBody!);

                if (bodyValue.Type != ValueType.Object)
                {
                    throw new RuntimeException("Prompt body must evaluate to an object.");
                }

                var bodyObj = bodyValue.AsObject();
                if (bodyObj is not JsonObject jsonObj)
                {
                    throw new RuntimeException("Prompt body must be a JSON object.");
                }

                var systemValue = jsonObj.Get("system");
                var userValue = jsonObj.Get("user");
                var modelValue = jsonObj.Get("model");
                var temperatureValue = jsonObj.Get("temperature");
                var toolsValue = jsonObj.Get("tools");
                var gatherValue = jsonObj.Get("gather");
                var maxTokensValue = jsonObj.Get("maxTokens");
                var examplesValue = jsonObj.Get("examples");

                if (userValue.Type == ValueType.Null || userValue.Type != ValueType.String)
                {
                    throw new RuntimeException("Prompt body must have a 'user' field of type string.");
                }

                if (systemValue.Type == ValueType.String)
                {
                    system = systemValue.AsString();
                }

                user = userValue.AsString();

                if (modelValue.Type == ValueType.String)
                {
                    model = modelValue.AsString();
                }

                if (temperatureValue.Type != ValueType.Null)
                {
                    if (temperatureValue.Type == ValueType.Float)
                    {
                        temperature = temperatureValue.AsFloat();
                    }
                    else if (temperatureValue.Type == ValueType.Integer)
                    {
                        temperature = (double)temperatureValue.AsInteger();
                    }
                }

                tools = ReadOptionalStringList(toolsValue, "tools", requireNonEmpty: false);
                gather = ReadOptionalStringList(gatherValue, "gather", requireNonEmpty: true);

                if (maxTokensValue.Type != ValueType.Null)
                {
                    if (maxTokensValue.Type == ValueType.Integer)
                    {
                        maxTokens = maxTokensValue.AsInteger();
                    }
                    else if (maxTokensValue.Type == ValueType.Float)
                    {
                        maxTokens = (int)maxTokensValue.AsFloat();
                    }
                }

                examples = PromptExampleHelpers.ParseExamplesOrNull(examplesValue)?.ToList();
            }
            else
            {
                if (Declaration.StatementBody == null)
                {
                    throw new RuntimeException("Prompt body statements are null.");
                }

                foreach (var stmt in Declaration.StatementBody)
                {
                    if (stmt is PromptBodyStatement bodyStmt)
                    {
                        var value = await interpreter.EvaluateAsync(bodyStmt.Expression);

                        switch (bodyStmt.Keyword)
                        {
                            case "system":
                                if (value.Type != ValueType.String)
                                {
                                    throw new RuntimeException("Prompt 'system' field must be a string.");
                                }
                                system = value.AsString();
                                break;
                            case "user":
                                user = value.ToString();
                                break;
                            case "model":
                                if (value.Type != ValueType.String)
                                {
                                    throw new RuntimeException("Prompt 'model' field must be a string.");
                                }
                                model = value.AsString();
                                break;
                            case "temperature":
                                if (value.Type == ValueType.Float)
                                {
                                    temperature = value.AsFloat();
                                }
                                else if (value.Type == ValueType.Integer)
                                {
                                    temperature = (double)value.AsInteger();
                                }
                                else
                                {
                                    throw new RuntimeException("Prompt 'temperature' field must be a number.");
                                }
                                break;
                            case "tools":
                                if (value.Type != ValueType.Array)
                                {
                                    throw new RuntimeException("Prompt 'tools' field must be an array.");
                                }
                                tools = ReadRequiredStringList(value, "tools", requireNonEmpty: false);
                                break;
                            case "gather":
                                if (value.Type != ValueType.Array)
                                {
                                    throw new RuntimeException("Prompt 'gather' field must be an array.");
                                }
                                gather = ReadRequiredStringList(value, "gather", requireNonEmpty: true);
                                break;
                            case "maxTokens":
                                if (value.Type == ValueType.Integer)
                                {
                                    maxTokens = value.AsInteger();
                                }
                                else if (value.Type == ValueType.Float)
                                {
                                    maxTokens = (int)value.AsFloat();
                                }
                                else
                                {
                                    throw new RuntimeException("Prompt 'maxTokens' field must be an integer.");
                                }
                                break;
                            case "examples":
                                examples = PromptExampleHelpers.ParseExamplesOrNull(value)?.ToList();
                                break;
                        }
                    }
                }

                if (user == null)
                {
                    throw new RuntimeException("Prompt body must have a 'user' field.");
                }
            }

            ValidateGatherContract(Declaration.Name, Declaration.ReturnType, tools, gather);

            for (int i = 0; i < Declaration.Parameters.Count; i++)
            {
                var paramName = Declaration.Parameters[i];
                var argValue = arguments[i];
                var replacement = argValue.ToString();
                var placeholder = "{" + paramName + "}";

                if (system != null && system.Contains(placeholder))
                {
                    system = system.Replace(placeholder, replacement);
                }

                if (user != null && user.Contains(placeholder))
                {
                    user = user.Replace(placeholder, replacement);
                }
            }

            if (examples != null && examples.Count > 0)
            {
                PromptExampleHelpers.ApplyParameterInterpolation(
                    examples,
                    Declaration.Parameters,
                    arguments);
            }

            var hasGather = gather != null && gather.Count > 0;
            RuntimeValue? responseFormatSchema = null;
            // Mode A and Mode B: attach response_format + appendix when -> Type is set.
            // Mode C gather stays unconstrained (extract step attaches schema after tools).
            // If a backend rejects tools+json_schema, LLMClient retries once without format.
            // In-process GGUF (LlamaCppClient) compiles the same schema to GBNF for Mode A /
            // extract; Mode B tool rounds skip the grammar so the model can emit tool calls.
            if (!string.IsNullOrWhiteSpace(Declaration.ReturnType) && !hasGather)
            {
                if (TypedPromptSchemaResolver.TryResolve(Declaration.ReturnType!, interpreter, out var schema, out _))
                {
                    responseFormatSchema = TypedPromptValidator.BuildResponseFormat(schema);
                    system = TypedPromptValidator.ApplySchemaAppendix(system, Declaration.ReturnType!, schema);
                }
            }

            var withinTimeoutMs = DeclarationBounds.TryGetWithinTimeoutMs(Declaration);
            var budget = DeclarationBounds.TryGetResourceBudget(Declaration);
            var promptInstance = new PromptInstance(
                system,
                user!,
                model,
                temperature,
                tools,
                maxTokens,
                responseFormatSchema,
                examples,
                withinTimeoutMs,
                gather,
                budget,
                Declaration.ReturnType);
            return RuntimeValue.Object(promptInstance);
        }
        finally
        {
            interpreter._environment = previousEnvironment;
        }
    }

    private async Task<RuntimeValue> ExecutePromptAsync(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        // One statement: do not add a fake LLM stack frame. Tell the debugger
        // the model is running so the UI does not look frozen. Do not use the
        // print/io.print callback — that is program stdout.
        const string waitMessage = "await prompt …";
        if (interpreter.GetDebuggerHook() is Debug.DebugSession debugSession)
            debugSession.EmitOutput(waitMessage);
        else if (interpreter.GetDebuggerHook() is Debug.IHasDebugSession hasDebug)
            hasDebug.Session.EmitOutput(waitMessage);

        var promptInstanceValue = await BuildPromptInstanceAsync(arguments, interpreter);
        var promptInstance = promptInstanceValue.AsObject() as PromptInstance;
        if (promptInstance == null)
        {
            throw new RuntimeException("Failed to create PromptInstance.");
        }

        AgentInstance agent;
        if (interpreter._defaultAgent != null)
        {
            agent = interpreter._defaultAgent;
        }
        else
        {
            var defaultClient = DefaultLocalLlm.GetDefaultLocalClient();
            agent = new AgentInstance();
            agent.Initialize("PromptAgent", "AI Assistant", "You are a helpful AI assistant.", null, defaultClient, null, null);
        }

        var budget = promptInstance.Budget;
        var pushedBudget = budget != null && budget.HasAnyBound;
        if (pushedBudget)
            ResourceBoundsContext.Push(budget!, Declaration.Name);

        try
        {
            if (promptInstance.HasGather)
            {
                promptInstance = RunGatherThenBuildExtract(promptInstance, agent, interpreter);
                promptInstanceValue = RuntimeValue.Object(promptInstance);
            }

            if (string.IsNullOrWhiteSpace(Declaration.ReturnType))
            {
                WithinBoundsContext.EnsureWithinBound(Declaration.Name);
                var untypedResponse = agent.Think(promptInstanceValue);
                var content = TryExtractResponseContent(untypedResponse);
                return content != null ? RuntimeValue.String(content) : RuntimeValue.String(untypedResponse.ToString());
            }

            const int maxAttempts = 3;
            var returnType = Declaration.ReturnType!;
            var originalUser = promptInstance.User;
            string? lastError = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                WithinBoundsContext.EnsureWithinBound(Declaration.Name);
                if (attempt > 1)
                {
                    var repair = TypedPromptValidator.BuildRepairInstruction(returnType, lastError ?? "Unknown validation error.");
                    promptInstance = new PromptInstance(
                        promptInstance.System,
                        originalUser + "\n\n" + repair,
                        promptInstance.Model,
                        promptInstance.Temperature,
                        promptInstance.Tools,
                        promptInstance.MaxTokens,
                        promptInstance.ResponseFormatSchema,
                        promptInstance.Examples,
                        promptInstance.WithinTimeoutMs,
                        promptInstance.Gather,
                        promptInstance.Budget,
                        promptInstance.ReturnType);
                    promptInstanceValue = RuntimeValue.Object(promptInstance);
                }

                var response = agent.Think(promptInstanceValue);
                var attemptContent = TryExtractResponseContent(response);
                if (attemptContent == null)
                {
                    lastError = "No string content in LLM response.";
                    continue;
                }

                if (!TypedPromptValidator.TryExtractJsonCandidate(attemptContent, out var jsonCandidate, out var extractError))
                {
                    lastError = extractError;
                    continue;
                }

                if (!TypedPromptValidator.TryParseJson(jsonCandidate, out var parsed, out var parseError))
                {
                    lastError = parseError;
                    continue;
                }

                if (!TypedPromptValidator.TryValidateReturnType(parsed, returnType, interpreter, out var validated, out var validationError))
                {
                    lastError = validationError;
                    continue;
                }

                return validated;
            }

            throw new RuntimeException(
                $"Typed prompt '{Declaration.Name}' output validation failed after {maxAttempts} attempts. " +
                $"Return type: {returnType}. Last error: {lastError ?? "Unknown error."}");
        }
        finally
        {
            if (pushedBudget)
                ResourceBoundsContext.Pop();
        }
    }

    private PromptInstance RunGatherThenBuildExtract(
        PromptInstance promptInstance,
        AgentInstance agent,
        Interpreter interpreter)
    {
        string notes;
        try
        {
            WithinBoundsContext.EnsureWithinBound(Declaration.Name);
            var gatherInstance = new PromptInstance(
                promptInstance.System,
                promptInstance.User,
                promptInstance.Model,
                promptInstance.Temperature,
                promptInstance.Gather,
                promptInstance.MaxTokens,
                responseFormatSchema: null,
                promptInstance.Examples,
                promptInstance.WithinTimeoutMs,
                promptInstance.Gather,
                promptInstance.Budget,
                promptInstance.ReturnType);
            var gatherResponse = agent.Think(RuntimeValue.Object(gatherInstance));
            var content = TryExtractResponseContent(gatherResponse);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new RuntimeException(
                    $"Gather step of prompt '{Declaration.Name}' failed: no string content in LLM response.");
            }

            notes = content;
        }
        catch (RuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"Gather step of prompt '{Declaration.Name}' failed: {ex.Message}");
        }

        var extractSystem = promptInstance.System;
        RuntimeValue? responseFormatSchema = null;
        if (!string.IsNullOrWhiteSpace(Declaration.ReturnType) &&
            TypedPromptSchemaResolver.TryResolve(Declaration.ReturnType!, interpreter, out var schema, out _))
        {
            responseFormatSchema = TypedPromptValidator.BuildResponseFormat(schema);
            extractSystem = TypedPromptValidator.ApplySchemaAppendix(extractSystem, Declaration.ReturnType!, schema);
        }

        extractSystem = (extractSystem ?? "") + GatherExtractSystemSuffix;
        var extractUser = promptInstance.User + GatherNotesMarker + notes;
        return new PromptInstance(
            extractSystem,
            extractUser,
            promptInstance.Model,
            promptInstance.Temperature,
            tools: null,
            promptInstance.MaxTokens,
            responseFormatSchema,
            promptInstance.Examples,
            promptInstance.WithinTimeoutMs,
            gather: null,
            promptInstance.Budget,
            promptInstance.ReturnType);
    }

    internal static void ValidateGatherContract(
        string promptName,
        string? returnType,
        List<string>? tools,
        List<string>? gather)
    {
        var hasTools = tools != null && tools.Count > 0;
        var hasGather = gather != null && gather.Count > 0;
        if (!hasGather)
            return;

        if (hasTools)
        {
            throw new RuntimeException(
                $"Prompt '{promptName}' cannot list both gather: and tools:. " +
                "Use gather: with -> Type for two-phase extract, or tools: for Mode B.");
        }

        if (string.IsNullOrWhiteSpace(returnType))
        {
            throw new RuntimeException(
                $"Prompt '{promptName}' uses gather: which requires a -> Type extract target " +
                "(schema, sum type, or program(Api)).");
        }
    }

    private static List<string>? ReadOptionalStringList(RuntimeValue value, string fieldName, bool requireNonEmpty)
    {
        if (value.Type == ValueType.Null)
            return null;
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"Prompt '{fieldName}' field must be an array.");
        return ReadRequiredStringList(value, fieldName, requireNonEmpty);
    }

    private static List<string> ReadRequiredStringList(RuntimeValue value, string fieldName, bool requireNonEmpty)
    {
        var list = new List<string>();
        foreach (var item in value.AsArray())
        {
            if (item.Type == ValueType.String)
                list.Add(item.AsString());
        }

        if (requireNonEmpty && list.Count == 0)
        {
            throw new RuntimeException($"Prompt '{fieldName}' field must be a non-empty array of tool name strings.");
        }

        return list;
    }
}
