// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;

public class LLMClientInstance : ObjectInstance
{
    public string ApiUrl { get; set; }
    public string ApiKey { get; set; }
    public string Model { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// Optional handler invoked for each streamed content/reasoning delta during Chat().
    /// Set by Conversation when live thinking output is enabled.
    /// </summary>
    internal static Action<LlmStreamDelta>? StreamDeltaHandler { get; set; }
    
    private static readonly HttpClient _httpClient = new HttpClient();
    private static bool? _llmStreamingEnabled;
    
    public LLMClientInstance() : base(null)
    {
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "apiUrl")
            return RuntimeValue.String(ApiUrl ?? "");
        if (name == "apiKey")
            return RuntimeValue.String(ApiKey ?? "");
        if (name == "model")
            return RuntimeValue.String(Model ?? "");
        if (name == "temperature")
            return RuntimeValue.Float(Temperature);
        if (name == "maxTokens")
            return RuntimeValue.Integer(MaxTokens);
        
        // Handle method access - create a FunctionValue wrapper
        if (name == "complete" || name == "chat" || name == "setTemperature" || name == "setMaxTokens")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on LLMClient.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        switch (methodName)
        {
            case "setTemperature":
                if (args.Count != 1 || args[0].Type != ValueType.Float)
                    throw new Exception("setTemperature() expects 1 float argument");
                Temperature = args[0].AsFloat();
                return RuntimeValue.Null();
            
            case "setMaxTokens":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("setMaxTokens() expects 1 integer argument");
                MaxTokens = args[0].AsInteger();
                return RuntimeValue.Null();
            
            case "chat":
                if (args.Count < 1)
                    throw new Exception("chat() expects at least 1 argument");
                var messages = args[0];
                var tools = args.Count > 1 ? args[1] : null;
                var responseFormat = args.Count > 2 ? args[2] : null;
                return Chat(messages, tools, responseFormat);
            
            case "complete":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("complete() expects 1 string argument");
                return Complete(args[0].AsString());
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    public RuntimeValue Chat(RuntimeValue messages, RuntimeValue? tools, RuntimeValue? responseFormat = null, LlmRequestOverrides? overrides = null)
    {
        try
        {
            if (messages.Type != ValueType.Array)
                return RuntimeValue.Null();

            var requestBody = BuildRequestBody(messages, tools, responseFormat, overrides);
            RuntimeValue result;
            if (ShouldUseStreaming(responseFormat))
            {
                requestBody["stream"] = true;
                result = ChatStreaming(requestBody);
            }
            else
            {
                result = ChatNonStreaming(requestBody);
            }

            if (ShouldRetryWithoutResponseFormat(responseFormat, result, exceptionMessage: null))
            {
                WarnResponseFormatRejectedOnce();
                // Force non-streaming recovery so callers get a normal completion payload.
                var fallbackBody = BuildRequestBody(messages, tools, responseFormat: null, overrides);
                return ChatNonStreaming(fallbackBody);
            }

            return result;
        }
        catch (Exception ex)
        {
            if (ShouldRetryWithoutResponseFormat(responseFormat, result: null, exceptionMessage: ex.Message))
            {
                try
                {
                    WarnResponseFormatRejectedOnce();
                    var fallbackBody = BuildRequestBody(messages, tools, responseFormat: null, overrides);
                    return ChatNonStreaming(fallbackBody);
                }
                catch (Exception retryEx)
                {
                    var retryError = new JsonObject();
                    retryError.Set("content", RuntimeValue.String($"Error: Exception during API call: {retryEx.Message}"));
                    return RuntimeValue.Object(retryError);
                }
            }

            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String($"Error: Exception during API call: {ex.Message}"));
            return RuntimeValue.Object(errorObj);
        }
    }

    private static bool _warnedResponseFormatRejected;

    internal static void WarnResponseFormatRejectedOnce()
    {
        if (_warnedResponseFormatRejected)
            return;
        _warnedResponseFormatRejected = true;
        Console.Error.WriteLine(
            "MALDA: LLM rejected response_format / structured output; retrying once without it.");
    }

    /// <summary>
    /// True when a chat that included <c>response_format</c> failed in a way that
    /// suggests the backend does not support structured outputs.
    /// </summary>
    internal static bool ShouldRetryWithoutResponseFormat(
        RuntimeValue? responseFormat,
        RuntimeValue? result,
        string? exceptionMessage)
    {
        if (responseFormat == null || responseFormat.Type != ValueType.Object)
            return false;

        if (!string.IsNullOrEmpty(exceptionMessage) &&
            LooksLikeResponseFormatRejectionText(exceptionMessage))
        {
            return true;
        }

        if (result == null || result.Type != ValueType.Object)
            return false;

        var content = result.AsObject().Get("content", null);
        if (content == null || content.Type != ValueType.String)
            return false;

        var text = content.AsString();
        if (string.IsNullOrEmpty(text) ||
            !text.StartsWith("Error:", StringComparison.Ordinal))
        {
            return false;
        }

        return LooksLikeResponseFormatRejectionText(text);
    }

    internal static bool LooksLikeResponseFormatRejectionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        return lower.Contains("response_format", StringComparison.Ordinal)
            || lower.Contains("json_schema", StringComparison.Ordinal)
            || lower.Contains("structured output", StringComparison.Ordinal)
            || lower.Contains("structured_output", StringComparison.Ordinal)
            || lower.Contains("structured outputs", StringComparison.Ordinal);
    }

    internal static bool IsLlmStreamingEnabled()
    {
        if (_llmStreamingEnabled.HasValue)
            return _llmStreamingEnabled.Value;

        var env = System.Environment.GetEnvironmentVariable("MALDA_AGENT_LLM_STREAM");
        if (string.IsNullOrWhiteSpace(env))
            env = System.Environment.GetEnvironmentVariable("MALDA_RALPH_LLM_STREAM");

        if (string.IsNullOrWhiteSpace(env))
        {
            _llmStreamingEnabled = true;
            return true;
        }

        var lower = env.Trim().ToLowerInvariant();
        _llmStreamingEnabled = lower is not ("0" or "false" or "no" or "off");
        return _llmStreamingEnabled.Value;
    }

    private static bool ShouldUseStreaming(RuntimeValue? responseFormat)
    {
        if (!IsLlmStreamingEnabled())
            return false;

        return responseFormat == null || responseFormat.Type != ValueType.Object;
    }

    internal Dictionary<string, object?> BuildRequestBody(RuntimeValue messages, RuntimeValue? tools, RuntimeValue? responseFormat, LlmRequestOverrides? overrides = null)
    {
        var messagesList = messages.AsArray();
        var requestMessages = new List<object>();

        foreach (var msg in messagesList)
        {
            if (msg.Type != ValueType.Object)
                continue;

            var msgObj = msg.AsObject();
            var role = GetStringProperty(msgObj, "role") ?? "user";
            var content = GetStringProperty(msgObj, "content");

            if (role == "tool")
            {
                var toolCallId = GetStringProperty(msgObj, "tool_call_id");
                if (string.IsNullOrEmpty(toolCallId))
                    continue;

                var toolMsgDict = new Dictionary<string, object?>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = toolCallId
                };

                if (content != null)
                    toolMsgDict["content"] = content;

                requestMessages.Add(toolMsgDict);
                continue;
            }

            var msgDict = new Dictionary<string, object?> { ["role"] = role };

            if (content != null)
                msgDict["content"] = content;

            // Replay thinking-model CoT when present. DeepSeek V4 requires this as
            // reasoning_content on assistant turns that included tool_calls.
            // Only emit non-empty values — empty placeholders are rejected by some routes.
            var reasoning = GetStringProperty(msgObj, "reasoning")
                ?? GetStringProperty(msgObj, "reasoning_content");
            if (role == "assistant" && !string.IsNullOrWhiteSpace(reasoning))
                msgDict["reasoning_content"] = reasoning;

            var toolCalls = GetProperty(msgObj, "tool_calls");
            if (toolCalls != null && toolCalls.Type == ValueType.Array)
            {
                var toolCallsList = new List<object>();
                foreach (var tc in toolCalls.AsArray())
                {
                    if (tc.Type != ValueType.Object)
                        continue;

                    var tcObj = tc.AsObject();
                    var toolCallId = GetStringProperty(tcObj, "id");
                    if (string.IsNullOrEmpty(toolCallId))
                        continue;

                    var tcDict = new Dictionary<string, object?>
                    {
                        ["id"] = toolCallId,
                        ["type"] = GetStringProperty(tcObj, "type") ?? "function"
                    };

                    var func = GetProperty(tcObj, "function");
                    if (func != null && func.Type == ValueType.Object)
                    {
                        var funcObj = func.AsObject();
                        tcDict["function"] = new Dictionary<string, object?>
                        {
                            ["name"] = GetStringProperty(funcObj, "name"),
                            ["arguments"] = GetStringProperty(funcObj, "arguments")
                        };
                    }

                    toolCallsList.Add(tcDict);
                }

                if (toolCallsList.Count > 0)
                    msgDict["tool_calls"] = toolCallsList;
            }

            requestMessages.Add(msgDict);
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = overrides?.Model ?? Model,
            ["messages"] = requestMessages,
            ["temperature"] = overrides?.Temperature ?? Temperature,
            ["max_tokens"] = overrides?.MaxTokens ?? MaxTokens
        };

        if (tools != null && tools.Type == ValueType.Array)
        {
            var toolsList = new List<object>();
            foreach (var tool in tools.AsArray())
            {
                if (tool.Type != ValueType.Object)
                    continue;

                var toolObj = tool.AsObject();
                var toolType = GetStringProperty(toolObj, "type") ?? "function";
                var func = GetProperty(toolObj, "function");

                if (func == null || func.Type != ValueType.Object)
                    continue;

                var funcObj = func.AsObject();
                toolsList.Add(new Dictionary<string, object?>
                {
                    ["type"] = toolType,
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = GetStringProperty(funcObj, "name"),
                        ["description"] = GetStringProperty(funcObj, "description"),
                        ["parameters"] = GetProperty(funcObj, "parameters") != null
                            ? JsonToObject(GetProperty(funcObj, "parameters")!)
                            : null
                    }
                });
            }

            if (toolsList.Count > 0)
                requestBody["tools"] = toolsList;
        }

        if (responseFormat != null && responseFormat.Type == ValueType.Object)
        {
            var formatObj = JsonToObject(responseFormat);
            if (formatObj != null)
                requestBody["response_format"] = formatObj;
        }

        return requestBody;
    }

    private RuntimeValue ChatNonStreaming(Dictionary<string, object?> requestBody)
    {
        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = httpContent };

        if (!string.IsNullOrEmpty(ApiKey) && ApiKey != "lm-studio" && ApiKey != "ollama")
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        }

        ApplyRequestHeaders(request);

        var response = _httpClient.Send(request);
        var responseContent = response.Content.ReadAsStringAsync().Result;

        if (responseContent.Length > 10000)
        {
            System.Diagnostics.Debug.WriteLine($"LLM API Response length: {responseContent.Length} characters");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String($"Error: API request failed with status {response.StatusCode}. Response: {responseContent}"));
            return RuntimeValue.Object(errorObj);
        }

        return ParseNonStreamingResponse(responseContent);
    }

    private RuntimeValue ChatStreaming(Dictionary<string, object?> requestBody)
    {
        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = httpContent };

        if (!string.IsNullOrEmpty(ApiKey) && ApiKey != "lm-studio" && ApiKey != "ollama")
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        }

        ApplyRequestHeaders(request);

        using var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = response.Content.ReadAsStringAsync().Result;
            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String($"Error: API request failed with status {response.StatusCode}. Response: {responseContent}"));
            return RuntimeValue.Object(errorObj);
        }

        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream);

        var accumulator = new OpenAiChatStreamAccumulator
        {
            OnDelta = StreamDeltaHandler
        };

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line.Length > 5 ? line.Substring(5).TrimStart() : "";
            if (data.Length == 0)
                continue;

            try
            {
                accumulator.ProcessSseDataLine(data);
            }
            catch (InvalidOperationException ex)
            {
                var errorObj = new JsonObject();
                errorObj.Set("content", RuntimeValue.String($"Error: {ex.Message}"));
                return RuntimeValue.Object(errorObj);
            }
            catch (JsonException)
            {
            }
        }

        return RuntimeValue.Object(accumulator.ToResultObject());
    }

    private RuntimeValue ParseNonStreamingResponse(string responseContent)
    {
        var responseDoc = JsonDocument.Parse(responseContent);
        if (!responseDoc.RootElement.TryGetProperty("choices", out var choices))
        {
            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String($"Error: Invalid API response format. Response: {responseContent}"));
            return RuntimeValue.Object(errorObj);
        }

        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String($"Error: No choices in API response. Response: {responseContent}"));
            return RuntimeValue.Object(errorObj);
        }

        var choice = choices[0];
        var message = choice.GetProperty("message");

        var resultObj = new JsonObject();
        var contentProp = message.TryGetProperty("content", out var contentElem)
            ? contentElem.GetString()
            : null;
        resultObj.Set("content", contentProp != null ? RuntimeValue.String(contentProp) : RuntimeValue.Null());

        var reasoningProp = ExtractReasoningFromMessage(message);
        if (!string.IsNullOrWhiteSpace(reasoningProp))
            resultObj.Set("reasoning", RuntimeValue.String(reasoningProp));

        if (responseDoc.RootElement.TryGetProperty("usage", out var usageElem) && usageElem.ValueKind == JsonValueKind.Object)
        {
            var usageObj = new JsonObject();
            var hasUsage = false;
            if (usageElem.TryGetProperty("prompt_tokens", out var promptTokens) && promptTokens.TryGetInt32(out var pt))
            {
                usageObj.Set("promptTokens", RuntimeValue.Integer(pt));
                hasUsage = true;
            }
            if (usageElem.TryGetProperty("completion_tokens", out var completionTokens) && completionTokens.TryGetInt32(out var ct))
            {
                usageObj.Set("completionTokens", RuntimeValue.Integer(ct));
                hasUsage = true;
            }
            if (usageElem.TryGetProperty("total_tokens", out var totalTokens) && totalTokens.TryGetInt32(out var tt))
            {
                usageObj.Set("totalTokens", RuntimeValue.Integer(tt));
                hasUsage = true;
            }
            if (TryReadUsageCost(usageElem, out var cost))
            {
                usageObj.Set("cost", RuntimeValue.Float(cost));
                hasUsage = true;
            }
            if (hasUsage)
                resultObj.Set("usage", RuntimeValue.Object(usageObj));
        }

        if (message.TryGetProperty("tool_calls", out var toolCallsElem) && toolCallsElem.ValueKind == JsonValueKind.Array)
        {
            var toolCallsArray = new List<RuntimeValue>();
            foreach (var tc in toolCallsElem.EnumerateArray())
            {
                if (!tc.TryGetProperty("id", out var idElem) || idElem.ValueKind != JsonValueKind.String)
                    continue;

                var toolCallId = idElem.GetString();
                if (string.IsNullOrEmpty(toolCallId))
                    continue;

                var tcObj = new JsonObject();
                tcObj.Set("id", RuntimeValue.String(toolCallId));

                if (tc.TryGetProperty("type", out var typeElem))
                    tcObj.Set("type", RuntimeValue.String(typeElem.GetString() ?? "function"));
                if (tc.TryGetProperty("function", out var funcElem))
                {
                    var funcObj = new JsonObject();
                    if (funcElem.TryGetProperty("name", out var nameElem))
                        funcObj.Set("name", RuntimeValue.String(nameElem.GetString() ?? ""));
                    if (funcElem.TryGetProperty("arguments", out var argsElem))
                    {
                        var argumentsString = argsElem.GetString() ?? "";
                        funcObj.Set("arguments", RuntimeValue.String(argumentsString));
                    }
                    tcObj.Set("function", RuntimeValue.Object(funcObj));
                }
                toolCallsArray.Add(RuntimeValue.Object(tcObj));
            }
            resultObj.Set("tool_calls", RuntimeValue.Array(toolCallsArray));
        }

        return RuntimeValue.Object(resultObj);
    }
    
    /// <summary>
    /// Hook for subclasses (e.g. OpenRouterClient) to add provider-specific HTTP headers.
    /// </summary>
    protected virtual void ApplyRequestHeaders(HttpRequestMessage request)
    {
    }

    public RuntimeValue Complete(string prompt)
    {
        var messages = new List<RuntimeValue>
        {
            RuntimeValue.Object(CreateMessage("user", prompt))
        };
        
        var response = Chat(RuntimeValue.Array(messages), null);
        if (response.Type == ValueType.Object)
        {
            var obj = response.AsObject();
            if (obj is JsonObject jsonObj)
            {
                var content = jsonObj.Get("content", null);
                return content ?? RuntimeValue.Null();
            }
        }
        return RuntimeValue.Null();
    }
    
    private JsonObject CreateMessage(string role, string content)
    {
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String(role));
        msg.Set("content", RuntimeValue.String(content));
        return msg;
    }
    
    private string? GetStringProperty(ObjectInstance obj, string name)
    {
        try
        {
            var prop = obj.Get(name, null);
            return prop?.AsString();
        }
        catch
        {
            return null;
        }
    }
    
    private RuntimeValue? GetProperty(ObjectInstance obj, string name)
    {
        try
        {
            return obj.Get(name, null);
        }
        catch
        {
            return null;
        }
    }
    
    private object? JsonToObject(RuntimeValue value)
    {
        // Convert RuntimeValue to JSON-serializable object
        if (value.Type == ValueType.Object && value.AsObject() is JsonObject jsonObj)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var kvp in jsonObj.GetProperties())
            {
                dict[kvp.Key] = RuntimeValueToJsonObject(kvp.Value);
            }
            return dict;
        }
        else if (value.Type == ValueType.Array)
        {
            return value.AsArray().Select(RuntimeValueToJsonObject).ToList();
        }
        else if (value.Type == ValueType.String)
        {
            return value.AsString();
        }
        else if (value.Type == ValueType.Integer)
        {
            return value.AsInteger();
        }
        else if (value.Type == ValueType.Float)
        {
            return value.AsFloat();
        }
        else if (value.Type == ValueType.Boolean)
        {
            return value.AsBoolean();
        }
        return null;
    }
    
    private object? RuntimeValueToJsonObject(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.String:
                return value.AsString();
            
            case ValueType.Integer:
                return value.AsInteger();
            
            case ValueType.Float:
                return value.AsFloat();
            
            case ValueType.Boolean:
                return value.AsBoolean();
            
            case ValueType.Null:
                return null;
            
            case ValueType.Object:
                if (value.AsObject() is JsonObject jsonObj)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var kvp in jsonObj.GetProperties())
                    {
                        dict[kvp.Key] = RuntimeValueToJsonObject(kvp.Value);
                    }
                    return dict;
                }
                return null;
            
            case ValueType.Array:
                return value.AsArray().Select(RuntimeValueToJsonObject).ToList();
            
            default:
                return null;
        }
    }

    /// <summary>
    /// Extracts model-native reasoning/thinking text from an OpenAI-compatible chat message.
    /// Supports reasoning, reasoning_content, and reasoning_details[] shapes used by OpenRouter providers.
    /// </summary>
    internal static string? ExtractReasoningFromMessage(JsonElement message)
    {
        foreach (var propName in new[] { "reasoning", "reasoning_content" })
        {
            if (message.TryGetProperty(propName, out var elem) && elem.ValueKind == JsonValueKind.String)
            {
                var s = elem.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }
        }

        if (!message.TryGetProperty("reasoning_details", out var details) || details.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new StringBuilder();
        foreach (var item in details.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var key in new[] { "text", "content", "summary" })
            {
                if (!item.TryGetProperty(key, out var textEl) || textEl.ValueKind != JsonValueKind.String)
                    continue;

                var t = textEl.GetString();
                if (string.IsNullOrWhiteSpace(t))
                    continue;

                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(t.Trim());
                break;
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static bool TryReadUsageCost(JsonElement usageElem, out double cost)
    {
        cost = 0;
        if (usageElem.TryGetProperty("cost", out var costElem) && costElem.TryGetDouble(out cost))
            return true;
        if (usageElem.TryGetProperty("total_cost", out var totalCostElem) && totalCostElem.TryGetDouble(out cost))
            return true;
        return false;
    }
}
