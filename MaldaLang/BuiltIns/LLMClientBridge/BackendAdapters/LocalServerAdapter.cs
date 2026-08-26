// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Adapter for connecting to local LLM servers via HTTP.
/// </summary>
public class LocalServerAdapter : IBackendAdapter
{
    public string BackendType => "server";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2000;
    
    private readonly string _serverUrl;
    private readonly string? _apiKey;
    private static readonly HttpClient _httpClient = new HttpClient();
    
    public LocalServerAdapter(string serverUrl, string? apiKey = null)
    {
        _serverUrl = serverUrl.TrimEnd('/');
        _apiKey = apiKey;
    }
    
    public RuntimeValue Chat(RuntimeValue messages, RuntimeValue? tools, RuntimeValue? responseFormat = null, LlmRequestOverrides? overrides = null)
    {
        var model = overrides?.Model;
        var formatToSend = LLMClientInstance.EffectiveResponseFormat(_serverUrl, model, responseFormat);
        try
        {
            if (messages.Type != ValueType.Array)
            {
                var errorObj = new JsonObject();
                errorObj.Set("content", RuntimeValue.String("Error: messages must be an array"));
                return RuntimeValue.Object(errorObj);
            }
            
            var messagesList = messages.AsArray();
            var requestMessages = new List<object>();
            
            foreach (var msg in messagesList)
            {
                if (msg.Type != ValueType.Object)
                    continue;
                
                var msgObj = msg.AsObject();
                var role = GetStringProperty(msgObj, "role") ?? "user";
                var content = GetStringProperty(msgObj, "content");
                
                // Handle tool result messages
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
                
                var msgDict = new Dictionary<string, object?>
                {
                    ["role"] = role
                };
                
                if (content != null)
                    msgDict["content"] = content;
                
                // Handle tool calls
                var toolCalls = GetProperty(msgObj, "tool_calls");
                if (toolCalls != null && toolCalls.Type == ValueType.Array)
                {
                    var toolCallsList = new List<object>();
                    foreach (var tc in toolCalls.AsArray())
                    {
                        if (tc.Type == ValueType.Object)
                        {
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
                                var funcDict = new Dictionary<string, object?>
                                {
                                    ["name"] = GetStringProperty(funcObj, "name"),
                                    ["arguments"] = GetStringProperty(funcObj, "arguments")
                                };
                                tcDict["function"] = funcDict;
                            }
                            
                            toolCallsList.Add(tcDict);
                        }
                    }
                    if (toolCallsList.Count > 0)
                        msgDict["tool_calls"] = toolCallsList;
                }
                
                requestMessages.Add(msgDict);
            }
            
            var requestBody = new Dictionary<string, object?>
            {
                ["messages"] = requestMessages,
                ["temperature"] = Temperature,
                ["max_tokens"] = MaxTokens
            };
            
            // Add tools if provided
            if (tools != null && tools.Type == ValueType.Array)
            {
                var toolsList = new List<object>();
                foreach (var tool in tools.AsArray())
                {
                    if (tool.Type == ValueType.Object)
                    {
                        var toolObj = tool.AsObject();
                        var toolType = GetStringProperty(toolObj, "type") ?? "function";
                        var func = GetProperty(toolObj, "function");
                        
                        if (func != null && func.Type == ValueType.Object)
                        {
                            var funcObj = func.AsObject();
                            var funcDict = new Dictionary<string, object?>
                            {
                                ["name"] = GetStringProperty(funcObj, "name"),
                                ["description"] = GetStringProperty(funcObj, "description"),
                                ["parameters"] = GetProperty(funcObj, "parameters") != null 
                                    ? JsonToObject(GetProperty(funcObj, "parameters")!) 
                                    : null
                            };
                            
                            toolsList.Add(new Dictionary<string, object?>
                            {
                                ["type"] = toolType,
                                ["function"] = funcDict
                            });
                        }
                    }
                }
                if (toolsList.Count > 0)
                    requestBody["tools"] = toolsList;
            }

            // Add response_format when provided (e.g. for typed prompts / Mode B if supported)
            if (formatToSend != null && formatToSend.Type == ValueType.Object)
            {
                var formatObj = JsonToObject(formatToSend);
                if (formatObj != null)
                    requestBody["response_format"] = formatObj;
            }

            var json = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            
            var endpoint = _serverUrl + "/v1/chat/completions";
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = httpContent;
            
            if (!string.IsNullOrEmpty(_apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            }
            
            var response = _httpClient.Send(request);
            var responseContent = response.Content.ReadAsStringAsync().Result;
            
            if (!response.IsSuccessStatusCode)
            {
                var errorObj = new JsonObject();
                errorObj.Set("content", RuntimeValue.String($"Error: Server request failed with status {response.StatusCode}. Response: {responseContent}"));
                var err = RuntimeValue.Object(errorObj);
                if (LLMClientInstance.ShouldRetryWithoutResponseFormat(formatToSend, err, exceptionMessage: null))
                {
                    LLMClientInstance.WarnResponseFormatRejectedOnce();
                    LLMClientInstance.RememberResponseFormatRejected(_serverUrl, model);
                    return Chat(messages, tools, responseFormat: null, overrides);
                }
                return err;
            }
            
            var responseDoc = JsonDocument.Parse(responseContent);
            if (!responseDoc.RootElement.TryGetProperty("choices", out var choices))
            {
                var errorObj = new JsonObject();
                errorObj.Set("content", RuntimeValue.String($"Error: Invalid server response format. Response: {responseContent}"));
                return RuntimeValue.Object(errorObj);
            }
            
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                var errorObj = new JsonObject();
                errorObj.Set("content", RuntimeValue.String($"Error: No choices in server response. Response: {responseContent}"));
                return RuntimeValue.Object(errorObj);
            }
            
            var choice = choices[0];
            var message = choice.GetProperty("message");
            
            var resultObj = new JsonObject();
            var contentProp = message.TryGetProperty("content", out var contentElem) 
                ? contentElem.GetString() 
                : null;
            resultObj.Set("content", contentProp != null ? RuntimeValue.String(contentProp) : RuntimeValue.Null());
            
            // Handle tool_calls
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
                            funcObj.Set("arguments", RuntimeValue.String(argsElem.GetString() ?? ""));
                        }
                        tcObj.Set("function", RuntimeValue.Object(funcObj));
                    }
                    toolCallsArray.Add(RuntimeValue.Object(tcObj));
                }
                resultObj.Set("tool_calls", RuntimeValue.Array(toolCallsArray));
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch (Exception ex)
        {
            if (LLMClientInstance.ShouldRetryWithoutResponseFormat(formatToSend, result: null, exceptionMessage: ex.Message))
            {
                try
                {
                    LLMClientInstance.WarnResponseFormatRejectedOnce();
                    LLMClientInstance.RememberResponseFormatRejected(_serverUrl, model);
                    return Chat(messages, tools, responseFormat: null, overrides);
                }
                catch (Exception retryEx)
                {
                    var retryError = new JsonObject();
                    retryError.Set("content", RuntimeValue.String($"Error: Exception during server call: {retryEx.Message}"));
                    return RuntimeValue.Object(retryError);
                }
            }

            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String($"Error: Exception during server call: {ex.Message}"));
            return RuntimeValue.Object(errorObj);
        }
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
    
    public bool IsConnected()
    {
        try
        {
            // Try to ping the health endpoint
            var healthUrl = _serverUrl + "/health";
            var response = _httpClient.GetAsync(healthUrl).Result;
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
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
}