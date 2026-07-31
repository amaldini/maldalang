// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.ACP;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public class ACPClientInstance : ObjectInstance, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    
    public ACPClientInstance(string baseUrl, string? apiKey = null) : base(null!)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
        
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "baseUrl")
            return RuntimeValue.String(_baseUrl);
        if (name == "isConnected")
            return RuntimeValue.Boolean(true); // Always "connected" for HTTP client
        
        // Handle method access
        if (name == "discoverAgents" || name == "getAgentManifest" || name == "sendMessage" || 
            name == "sendMessageAsync" || name == "sendMessageStream" || name == "getRunStatus" || name == "getSession" || name == "cancelRun" || name == "resumeRun")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on ACPClient.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "discoverAgents":
                return DiscoverAgents(args);
            case "getAgentManifest":
                return GetAgentManifest(args);
            case "sendMessage":
                return SendMessage(args);
            case "sendMessageAsync":
                return SendMessageAsync(args);
            case "sendMessageStream":
                return SendMessageStream(args);
            case "getRunStatus":
                return GetRunStatus(args);
            case "getSession":
                return GetSession(args);
            case "cancelRun":
                return CancelRun(args);
            case "resumeRun":
                return ResumeRun(args);
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    private RuntimeValue DiscoverAgents(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("discoverAgents() expects no arguments");
        
        try
        {
            var response = _httpClient.GetAsync($"{_baseUrl}/agents")
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            var jsonContent = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            
            var agents = JsonSerializer.Deserialize<List<ACPAgentInfo>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<ACPAgentInfo>();
            
            var agentsList = new List<RuntimeValue>();
            foreach (var agent in agents)
            {
                var agentObj = new JsonObject();
                agentObj.Set("id", RuntimeValue.String(agent.Id));
                agentObj.Set("name", RuntimeValue.String(agent.Name));
                agentObj.Set("description", RuntimeValue.String(agent.Description));
                agentObj.Set("version", RuntimeValue.String(agent.Version));
                agentsList.Add(RuntimeValue.Object(agentObj));
            }
            
            return RuntimeValue.Array(agentsList);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to discover agents: {ex.Message}");
        }
    }
    
    private RuntimeValue GetAgentManifest(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("getAgentManifest() expects 1 string argument (agentId)");
        
        var agentId = args[0].AsString();
        
        try
        {
            var response = _httpClient.GetAsync($"{_baseUrl}/agents/{Uri.EscapeDataString(agentId)}")
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            var jsonContent = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            
            var manifest = JsonSerializer.Deserialize<ACPAgentManifest>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (manifest == null)
                throw new Exception("Invalid manifest response");
            
            var manifestObj = new JsonObject();
            manifestObj.Set("name", RuntimeValue.String(manifest.Name));
            manifestObj.Set("description", RuntimeValue.String(manifest.Description));
            manifestObj.Set("version", RuntimeValue.String(manifest.Version));
            
            return RuntimeValue.Object(manifestObj);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get agent manifest: {ex.Message}");
        }
    }
    
    private RuntimeValue SendMessage(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("sendMessage() expects at least 2 arguments: (agentId, message, timeoutMs?, sessionId?)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("sendMessage() agentId must be a string");
        if (args[1].Type != ValueType.String)
            throw new Exception("sendMessage() message must be a string");
        
        var agentId = args[0].AsString();
        var messageText = args[1].AsString();
        
        int? timeoutMs = null;
        string? sessionId = null;
        
        if (args.Count > 2)
        {
            if (args[2].Type == ValueType.Integer)
            {
                timeoutMs = args[2].AsInteger();
            }
            else if (args[2].Type == ValueType.String)
            {
                sessionId = args[2].AsString();
            }
        }
        
        if (args.Count > 3 && args[3].Type == ValueType.String)
        {
            sessionId = args[3].AsString();
        }
        
        // Create ACP message
        var acpMessage = new ACPMessage(messageText);
        
        // Serialize request in ACP format
        var requestObj = new
        {
            agent_name = agentId,
            session_id = sessionId,
            input = new[]
            {
                new
                {
                    role = "user",
                    parts = acpMessage.Parts.Select(p => new
                    {
                        content = p.Content,
                        content_type = p.ContentType
                    }).ToArray()
                }
            },
            mode = "sync"
        };
        
        var messageJson = JsonSerializer.Serialize(requestObj);
        
        var content = new StringContent(messageJson, Encoding.UTF8, "application/json");
        
        try
        {
            using var cts = new System.Threading.CancellationTokenSource();
            if (timeoutMs.HasValue && timeoutMs.Value > 0)
            {
                cts.CancelAfter(timeoutMs.Value);
            }
            
            var response = _httpClient.PostAsync(
                $"{_baseUrl}/agents/{Uri.EscapeDataString(agentId)}/runs",
                content,
                cts.Token)
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            var responseJson = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            
            var runResponse = JsonSerializer.Deserialize<ACPRunResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (runResponse == null)
                throw new Exception("Invalid response from agent");
            
            // Check for errors
            if (runResponse.Error != null)
                throw new Exception($"ACP Error ({runResponse.Error.Code}): {runResponse.Error.Message}");
            
            // Extract message content from response
            var resultText = runResponse.Output.FirstOrDefault()?.GetTextContent() ?? "";
            return RuntimeValue.String(resultText);
        }
        catch (TaskCanceledException)
        {
            throw new Exception($"Request timed out after {timeoutMs ?? 30000}ms");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to send message: {ex.Message}");
        }
    }
    
    private RuntimeValue SendMessageAsync(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("sendMessageAsync() expects at least 2 arguments: (agentId, message, timeoutMs?)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("sendMessageAsync() agentId must be a string");
        if (args[1].Type != ValueType.String)
            throw new Exception("sendMessageAsync() message must be a string");
        
        var agentId = args[0].AsString();
        var messageText = args[1].AsString();
        
        int? timeoutMs = null;
        if (args.Count > 2 && args[2].Type == ValueType.Integer)
        {
            timeoutMs = args[2].AsInteger();
        }
        
        // Create ACP message
        var acpMessage = new ACPMessage(messageText);
        
        // Serialize request with async mode
        var requestJson = JsonSerializer.Serialize(new
        {
            agent_name = agentId,
            input = new[]
            {
                new
                {
                    role = "user",
                    parts = acpMessage.Parts.Select(p => new
                    {
                        content = p.Content,
                        content_type = p.ContentType
                    }).ToArray()
                }
            },
            mode = "async"
        });
        
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        try
        {
            using var cts = new System.Threading.CancellationTokenSource();
            if (timeoutMs.HasValue && timeoutMs.Value > 0)
            {
                cts.CancelAfter(timeoutMs.Value);
            }
            
            var response = _httpClient.PostAsync(
                $"{_baseUrl}/agents/{Uri.EscapeDataString(agentId)}/runs",
                content,
                cts.Token)
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            var responseJson = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            
            var runResponse = JsonSerializer.Deserialize<ACPRunResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (runResponse == null)
                throw new Exception("Invalid response from agent");
            
            // Return run ID for polling
            return RuntimeValue.String(runResponse.RunId);
        }
        catch (TaskCanceledException)
        {
            throw new Exception($"Request timed out after {timeoutMs ?? 30000}ms");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to send message async: {ex.Message}");
        }
    }
    
    private RuntimeValue SendMessageStream(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("sendMessageStream() expects at least 2 arguments: (agentId, message, timeoutMs?)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("sendMessageStream() agentId must be a string");
        if (args[1].Type != ValueType.String)
            throw new Exception("sendMessageStream() message must be a string");
        
        var agentId = args[0].AsString();
        var messageText = args[1].AsString();
        
        int? timeoutMs = null;
        if (args.Count > 2 && args[2].Type == ValueType.Integer)
        {
            timeoutMs = args[2].AsInteger();
        }
        
        // Create ACP message
        var acpMessage = new ACPMessage(messageText);
        
        // Serialize request with stream mode
        var requestJson = JsonSerializer.Serialize(new
        {
            agent_name = agentId,
            input = new[]
            {
                new
                {
                    role = "user",
                    parts = acpMessage.Parts.Select(p => new
                    {
                        content = p.Content,
                        content_type = p.ContentType
                    }).ToArray()
                }
            },
            mode = "stream"
        });
        
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        try
        {
            using var cts = new System.Threading.CancellationTokenSource();
            if (timeoutMs.HasValue && timeoutMs.Value > 0)
            {
                cts.CancelAfter(timeoutMs.Value);
            }
            
            var response = _httpClient.PostAsync(
                $"{_baseUrl}/agents/{Uri.EscapeDataString(agentId)}/runs",
                content,
                cts.Token)
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            // Read SSE stream
            var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            var reader = new StreamReader(stream);
            var events = new List<string>();
            var lastMessage = "";
            
            string? line;
            var currentEvent = "";
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("data: "))
                {
                    currentEvent = line.Substring(6); // Remove "data: " prefix
                    events.Add(currentEvent);
                    
                    // Try to parse as ACP event
                    try
                    {
                        var eventObj = JsonSerializer.Deserialize<JsonElement>(currentEvent);
                        if (eventObj.TryGetProperty("type", out var typeProp))
                        {
                            var eventType = typeProp.GetString();
                            if (eventType == "message.part" && eventObj.TryGetProperty("part", out var partProp))
                            {
                                if (partProp.TryGetProperty("content", out var contentProp))
                                {
                                    lastMessage += contentProp.GetString() ?? "";
                                }
                            }
                            else if (eventType == "run.completed")
                            {
                                break; // Stream complete
                            }
                        }
                    }
                    catch { }
                }
            }
            
            // Return the accumulated message content
            return RuntimeValue.String(lastMessage);
        }
        catch (TaskCanceledException)
        {
            throw new Exception($"Request timed out after {timeoutMs ?? 30000}ms");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to send message stream: {ex.Message}");
        }
    }
    
    private RuntimeValue GetRunStatus(List<RuntimeValue> args)
    {
        if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("getRunStatus() expects 2 string arguments: (agentId, runId)");
        
        var agentId = args[0].AsString();
        var runId = args[1].AsString();
        
        try
        {
            var response = _httpClient.GetAsync($"{_baseUrl}/agents/{Uri.EscapeDataString(agentId)}/runs/{Uri.EscapeDataString(runId)}")
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            var jsonContent = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            
            var runResponse = JsonSerializer.Deserialize<ACPRunResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (runResponse == null)
                throw new Exception("Invalid run response");
            
            var statusObj = new JsonObject();
            statusObj.Set("runId", RuntimeValue.String(runResponse.RunId));
            
            // Convert RunStatus enum to string
            var statusString = runResponse.Status.ToString().ToLower().Replace("InProgress", "in-progress");
            statusObj.Set("status", RuntimeValue.String(statusString));
            
            if (runResponse.Message != null)
            {
                statusObj.Set("message", RuntimeValue.String(runResponse.Message.GetTextContent()));
            }
            
            if (runResponse.Error != null)
            {
                // Access the Message property of ACPError
                var errorMessage = runResponse.Error.Message;
                statusObj.Set("error", RuntimeValue.String(errorMessage));
            }
            
            return RuntimeValue.Object(statusObj);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get run status: {ex.Message}");
        }
    }
    
    private RuntimeValue GetSession(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("getSession() expects 1 string argument (sessionId)");
        
        var sessionId = args[0].AsString();
        
        try
        {
            var response = _httpClient.GetAsync($"{_baseUrl}/session/{Uri.EscapeDataString(sessionId)}")
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            var jsonContent = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            
            var session = JsonSerializer.Deserialize<ACPSession>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (session == null)
                throw new Exception("Invalid session response");
            
            var sessionObj = new JsonObject();
            sessionObj.Set("id", RuntimeValue.String(session.Id));
            sessionObj.Set("history", RuntimeValue.Array(session.History.Select(h => RuntimeValue.String(h)).ToList()));
            if (session.State != null)
                sessionObj.Set("state", RuntimeValue.String(session.State));
            
            return RuntimeValue.Object(sessionObj);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get session: {ex.Message}");
        }
    }
    
    private RuntimeValue CancelRun(List<RuntimeValue> args)
    {
        if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("cancelRun() expects 2 string arguments: (agentId, runId)");
        
        var agentId = args[0].AsString();
        var runId = args[1].AsString();
        
        try
        {
            var response = _httpClient.PostAsync(
                $"{_baseUrl}/agents/{Uri.EscapeDataString(agentId)}/runs/{Uri.EscapeDataString(runId)}/cancel",
                new StringContent("{}", Encoding.UTF8, "application/json"))
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            var responseJson = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            
            var runResponse = JsonSerializer.Deserialize<ACPRunResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (runResponse == null)
                throw new Exception("Invalid cancel response");
            
            var statusObj = new JsonObject();
            statusObj.Set("runId", RuntimeValue.String(runResponse.RunId));
            statusObj.Set("status", RuntimeValue.String(runResponse.Status.ToString().ToLower().Replace("InProgress", "in-progress")));
            
            return RuntimeValue.Object(statusObj);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to cancel run: {ex.Message}");
        }
    }
    
    private RuntimeValue ResumeRun(List<RuntimeValue> args)
    {
        if (args.Count != 3 || args[0].Type != ValueType.String || args[1].Type != ValueType.String || args[2].Type != ValueType.String)
            throw new Exception("resumeRun() expects 3 string arguments: (agentId, runId, input)");
        
        var agentId = args[0].AsString();
        var runId = args[1].AsString();
        var input = args[2].AsString();
        
        try
        {
            var requestJson = JsonSerializer.Serialize(new
            {
                await_resume = new
                {
                    input = input
                }
            });
            
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            
            var response = _httpClient.PostAsync(
                $"{_baseUrl}/agents/{Uri.EscapeDataString(agentId)}/runs/{Uri.EscapeDataString(runId)}",
                content)
                .GetAwaiter()
                .GetResult();
            
            response.EnsureSuccessStatusCode();
            
            var responseJson = response.Content.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();
            
            var runResponse = JsonSerializer.Deserialize<ACPRunResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (runResponse == null)
                throw new Exception("Invalid resume response");
            
            // Check for errors
            if (runResponse.Error != null)
                throw new Exception($"ACP Error ({runResponse.Error.Code}): {runResponse.Error.Message}");
            
            // Extract message content from response
            var resultText = runResponse.Output.FirstOrDefault()?.GetTextContent() ?? "";
            return RuntimeValue.String(resultText);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to resume run: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
