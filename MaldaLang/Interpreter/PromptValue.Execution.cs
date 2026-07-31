// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public partial class PromptValue
{
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

                if (toolsValue.Type == ValueType.Array)
                {
                    tools = new List<string>();
                    foreach (var tool in toolsValue.AsArray())
                    {
                        if (tool.Type == ValueType.String)
                        {
                            tools.Add(tool.AsString());
                        }
                    }
                }

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
                                tools = new List<string>();
                                foreach (var tool in value.AsArray())
                                {
                                    if (tool.Type == ValueType.String)
                                    {
                                        tools.Add(tool.AsString());
                                    }
                                }
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

            RuntimeValue? responseFormatSchema = null;
            if (!string.IsNullOrWhiteSpace(Declaration.ReturnType) && (tools == null || tools.Count == 0))
            {
                if (TypedPromptSchemaResolver.TryResolve(Declaration.ReturnType!, interpreter, out var schema, out _))
                {
                    responseFormatSchema = TypedPromptValidator.BuildResponseFormat(schema);
                }
            }

            var withinTimeoutMs = DeclarationBounds.TryGetWithinTimeoutMs(Declaration);
            var promptInstance = new PromptInstance(
                system,
                user!,
                model,
                temperature,
                tools,
                maxTokens,
                responseFormatSchema,
                examples,
                withinTimeoutMs);
            return RuntimeValue.Object(promptInstance);
        }
        finally
        {
            interpreter._environment = previousEnvironment;
        }
    }

    private async Task<RuntimeValue> ExecutePromptAsync(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        var promptInstanceValue = await BuildPromptInstanceAsync(arguments, interpreter);
        var promptInstance = promptInstanceValue.AsObject() as PromptInstance;
        if (promptInstance == null)
        {
            throw new RuntimeException("Failed to create PromptInstance.");
        }

        AgentInstance? agent = null;

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
                    promptInstance.WithinTimeoutMs);
                promptInstanceValue = RuntimeValue.Object(promptInstance);
            }

            var response = agent.Think(promptInstanceValue);
            var content = TryExtractResponseContent(response);
            if (content == null)
            {
                lastError = "No string content in LLM response.";
                continue;
            }

            if (!TypedPromptValidator.TryExtractJsonCandidate(content, out var jsonCandidate, out var extractError))
            {
                lastError = extractError;
                continue;
            }

            if (!TypedPromptValidator.TryParseJson(jsonCandidate, out var parsed, out var parseError))
            {
                lastError = parseError;
                continue;
            }

            if (!TypedPromptValidator.TryValidateReturnType(parsed, returnType, interpreter, out var validationError))
            {
                lastError = validationError;
                continue;
            }

            return parsed;
        }

        throw new RuntimeException(
            $"Typed prompt '{Declaration.Name}' output validation failed after {maxAttempts} attempts. " +
            $"Return type: {returnType}. Last error: {lastError ?? "Unknown error."}");
    }
}
