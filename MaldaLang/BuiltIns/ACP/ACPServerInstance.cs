// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.ACP;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public class ACPServerInstance : ObjectInstance
{
    private HttpListener? _listener;
    private int _port;
    private bool _isRunning = false;
    private readonly ConcurrentDictionary<string, ACPAgentWrapper> _registeredAgents = new();
    private readonly ConcurrentDictionary<string, ACPSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Task<ACPRunResponse>> _runningTasks = new();
    private readonly ConcurrentDictionary<string, ACPRunResponse> _runResults = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runCancellations = new();
    private readonly ConcurrentDictionary<string, AwaitHandler> _awaitingRuns = new();
    private readonly ConcurrentDictionary<string, List<ACPEvent>> _runEvents = new();
    
    public ACPServerInstance(int port) : base(null!)
    {
        _port = port;
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "port")
            return RuntimeValue.Integer(_port);
        if (name == "isRunning")
            return RuntimeValue.Boolean(_isRunning);
        
        // Handle method access
        if (name == "registerAgent" || name == "start" || name == "stop" || name == "getRegisteredAgents")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on ACPServer.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "registerAgent":
                return RegisterAgent(args);
            case "start":
                if (args.Count != 0)
                    throw new Exception("start() expects 0 arguments");
                Start();
                return RuntimeValue.Null();
            case "stop":
                if (args.Count != 0)
                    throw new Exception("stop() expects 0 arguments");
                Stop();
                return RuntimeValue.Null();
            case "getRegisteredAgents":
                if (args.Count != 0)
                    throw new Exception("getRegisteredAgents() expects 0 arguments");
                return GetRegisteredAgents();
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    private RuntimeValue RegisterAgent(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("registerAgent() expects at least 2 arguments: (agentId, agentInstance, manifest?)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("registerAgent() agentId must be a string");
        if (args[1].Type != ValueType.Object)
            throw new Exception("registerAgent() agentInstance must be an Agent instance");
        
        var agentId = args[0].AsString();
        var agentObj = args[1].AsObject();
        
        if (agentObj is not AgentInstance agent)
            throw new Exception("registerAgent() agentInstance must be an Agent instance");
        
        ACPAgentManifest? manifest = null;
        if (args.Count > 2 && args[2].Type == ValueType.Object)
        {
            var manifestObj = args[2].AsObject();
            if (manifestObj is JsonObject jsonObj)
            {
                manifest = new ACPAgentManifest
                {
                    Name = jsonObj.Get("name")?.AsString() ?? agent.Name,
                    Description = jsonObj.Get("description")?.AsString() ?? $"{agent.Role}: {agent.Instructions}",
                    Version = jsonObj.Get("version")?.AsString() ?? "1.0.0"
                };
            }
        }
        
        var wrapper = new ACPAgentWrapper(agent, manifest);
        _registeredAgents[agentId] = wrapper;
        
        return RuntimeValue.Null();
    }
    
    private void Start()
    {
        if (_isRunning)
            throw new Exception("ACPServer is already running");
        
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _isRunning = true;
            
            // Start async request handling
            _ = Task.Run(async () => await HandleRequestsAsync());
            
            Console.WriteLine($"ACP Server started on http://localhost:{_port}/");
        }
        catch (Exception ex)
        {
            _isRunning = false;
            throw new Exception($"Failed to start ACPServer: {ex.Message}");
        }
    }
    
    private void Stop()
    {
        if (!_isRunning)
            return;
        
        _isRunning = false;
        _listener?.Stop();
        _listener?.Close();
        _listener = null;
    }
    
    private RuntimeValue GetRegisteredAgents()
    {
        var agentsList = new List<RuntimeValue>();
        foreach (var kvp in _registeredAgents)
        {
            var agentObj = new JsonObject();
            agentObj.Set("id", RuntimeValue.String(kvp.Key));
            agentObj.Set("name", RuntimeValue.String(kvp.Value.Manifest.Name));
            agentObj.Set("description", RuntimeValue.String(kvp.Value.Manifest.Description));
            agentObj.Set("version", RuntimeValue.String(kvp.Value.Manifest.Version));
            agentsList.Add(RuntimeValue.Object(agentObj));
        }
        return RuntimeValue.Array(agentsList);
    }
    
    private async Task HandleRequestsAsync()
    {
        while (_isRunning && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(async () => await HandleRequestAsync(context));
            }
            catch (HttpListenerException)
            {
                // Listener was stopped
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ACPServer request handler: {ex.Message}");
            }
        }
    }
    
    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        
        try
        {
            // Handle CORS preflight
            if (request.HttpMethod == "OPTIONS")
            {
                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
                response.StatusCode = 200;
                response.Close();
                return;
            }
            
            // Add CORS headers to all responses
            response.AddHeader("Access-Control-Allow-Origin", "*");
            
            var path = request.Url?.AbsolutePath ?? "";
            
            // Route: GET /ping
            if (request.HttpMethod == "GET" && path == "/ping")
            {
                await HandlePingAsync(response);
                return;
            }
            
            // Route: GET /agents
            if (request.HttpMethod == "GET" && path == "/agents")
            {
                await HandleListAgentsAsync(response, request);
                return;
            }
            
            // Route: GET /agents/{id}/runs/{runId} (must come before GET /agents/{id})
            if (request.HttpMethod == "GET" && path.Contains("/runs/") && !path.Contains("/events"))
            {
                var parts = path.Split('/');
                if (parts.Length >= 4 && parts[1] == "agents" && parts[3] == "runs")
                {
                    var agentId = parts[2];
                    var runId = parts[4];
                    await HandleGetRunStatusAsync(response, agentId, runId);
                    return;
                }
            }
            
            // Route: POST /agents/{id}/runs/{runId}/cancel (must come before other /runs routes)
            if (request.HttpMethod == "POST" && path.Contains("/runs/") && path.EndsWith("/cancel"))
            {
                var parts = path.Split('/');
                if (parts.Length >= 6 && parts[1] == "agents" && parts[3] == "runs" && parts[5] == "cancel")
                {
                    var agentId = parts[2];
                    var runId = parts[4];
                    await HandleCancelRunAsync(response, agentId, runId);
                    return;
                }
            }
            
            // Route: POST /agents/{id}/runs/{runId} (resume) (must come before POST /agents/{id}/runs)
            if (request.HttpMethod == "POST" && path.Contains("/runs/") && !path.Contains("/cancel") && !path.Contains("/events"))
            {
                var parts = path.Split('/');
                if (parts.Length >= 4 && parts[1] == "agents" && parts[3] == "runs")
                {
                    var agentId = parts[2];
                    var runId = parts[4];
                    await HandleResumeRunAsync(request, response, agentId, runId);
                    return;
                }
            }
            
            // Route: POST /agents/{id}/runs
            if (request.HttpMethod == "POST" && path.Contains("/runs") && !path.Contains("/runs/"))
            {
                var parts = path.Split('/');
                if (parts.Length >= 3 && parts[1] == "agents")
                {
                    var agentId = parts[2];
                    await HandleRunAgentAsync(request, response, agentId);
                    return;
                }
            }
            
            // Route: GET /agents/{id}
            if (request.HttpMethod == "GET" && path.StartsWith("/agents/") && !path.Contains("/runs"))
            {
                var agentId = path.Substring("/agents/".Length);
                await HandleGetAgentManifestAsync(response, agentId);
                return;
            }
            
            // Route: GET /session/{session_id}
            if (request.HttpMethod == "GET" && path.StartsWith("/session/"))
            {
                var sessionId = path.Substring("/session/".Length);
                await HandleGetSessionAsync(response, sessionId);
                return;
            }
            
            // 404 Not Found
            response.StatusCode = 404;
            var error = new ACPError("not_found", "Not Found");
            var notFoundJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var notFoundBytes = Encoding.UTF8.GetBytes(notFoundJson);
            response.ContentType = "application/json";
            response.ContentLength64 = notFoundBytes.Length;
            await response.OutputStream.WriteAsync(notFoundBytes, 0, notFoundBytes.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            var error = new ACPError("server_error", ex.Message);
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
        }
    }
    
    private async Task HandlePingAsync(HttpListenerResponse response)
    {
        var pingResponse = new { status = "ok" };
        var json = JsonSerializer.Serialize(pingResponse);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.StatusCode = 200;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }
    
    private async Task HandleListAgentsAsync(HttpListenerResponse response, HttpListenerRequest request)
    {
        // Parse pagination parameters
        int limit = 10;
        int offset = 0;
        
        var queryString = request.Url?.Query;
        if (!string.IsNullOrEmpty(queryString))
        {
            // Parse query string manually (format: ?limit=10&offset=0)
            var query = queryString.TrimStart('?');
            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = Uri.UnescapeDataString(parts[0]);
                    var value = Uri.UnescapeDataString(parts[1]);
                    
                    if (key == "limit" && int.TryParse(value, out var limitVal))
                    {
                        limit = Math.Max(1, Math.Min(1000, limitVal)); // Clamp between 1 and 1000
                    }
                    else if (key == "offset" && int.TryParse(value, out var offsetVal))
                    {
                        offset = Math.Max(0, offsetVal);
                    }
                }
            }
        }
        
        var allAgents = new List<object>();
        foreach (var kvp in _registeredAgents)
        {
            allAgents.Add(new
            {
                id = kvp.Key,
                name = kvp.Value.Manifest.Name,
                description = kvp.Value.Manifest.Description,
                version = kvp.Value.Manifest.Version
            });
        }
        
        // Apply pagination
        var totalCount = allAgents.Count;
        var paginatedAgents = allAgents.Skip(offset).Take(limit).ToList();
        
        var responseObj = new
        {
            agents = paginatedAgents,
            total = totalCount,
            limit = limit,
            offset = offset
        };
        
        var json = JsonSerializer.Serialize(responseObj);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.StatusCode = 200;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }
    
    private async Task HandleGetAgentManifestAsync(HttpListenerResponse response, string agentId)
    {
        if (!_registeredAgents.TryGetValue(agentId, out var wrapper))
        {
            response.StatusCode = 404;
            var error = new ACPError("not_found", $"Agent '{agentId}' not found");
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
            return;
        }
        
        var manifest = new
        {
            name = wrapper.Manifest.Name,
            description = wrapper.Manifest.Description,
            version = wrapper.Manifest.Version
        };
        
        var json = JsonSerializer.Serialize(manifest);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.StatusCode = 200;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }
    
    private async Task HandleRunAgentAsync(HttpListenerRequest request, HttpListenerResponse response, string agentId)
    {
        if (!_registeredAgents.TryGetValue(agentId, out var wrapper))
        {
            response.StatusCode = 404;
            var error = new ACPError("not_found", $"Agent '{agentId}' not found");
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
            return;
        }
        
        // Read request body
        string requestBody;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            requestBody = await reader.ReadToEndAsync();
        }
        
        // Parse request body for message, session_id, and mode
        string? sessionId = null;
        string mode = "sync"; // Default to sync for backward compatibility
        ACPMessage? acpMessage = null;
        try
        {
            var jsonDoc = JsonDocument.Parse(requestBody);
            
            // Extract session_id if present
            if (jsonDoc.RootElement.TryGetProperty("session_id", out var sessionIdProp))
                sessionId = sessionIdProp.GetString();
            
            // Extract mode if present (sync, async, stream)
            if (jsonDoc.RootElement.TryGetProperty("mode", out var modeProp))
                mode = modeProp.GetString() ?? "sync";
            
            // Parse message
            if (jsonDoc.RootElement.TryGetProperty("input", out var inputElement))
            {
                // ACP format: input is array of messages
                if (inputElement.ValueKind == JsonValueKind.Array && inputElement.GetArrayLength() > 0)
                {
                    var firstMessage = inputElement[0];
                    if (firstMessage.TryGetProperty("parts", out var partsElement))
                    {
                        var parts = new List<ACPMessagePart>();
                        foreach (var partElement in partsElement.EnumerateArray())
                        {
                            var part = new ACPMessagePart();
                            if (partElement.TryGetProperty("content", out var contentProp))
                                part.Content = contentProp.GetString();
                            if (partElement.TryGetProperty("content_type", out var contentTypeProp))
                                part.ContentType = contentTypeProp.GetString() ?? "text/plain";
                            else if (partElement.TryGetProperty("mimeType", out var mimeProp)) // Legacy support
                                part.ContentType = mimeProp.GetString() ?? "text/plain";
                            if (partElement.TryGetProperty("content_encoding", out var encodingProp))
                                part.ContentEncoding = encodingProp.GetString() ?? "plain";
                            if (partElement.TryGetProperty("content_url", out var urlProp))
                                part.ContentUrl = urlProp.GetString();
                            if (partElement.TryGetProperty("name", out var nameProp))
                                part.Name = nameProp.GetString();
                            if (partElement.TryGetProperty("metadata", out var metadataProp))
                                part.Metadata = JsonSerializer.Deserialize<object>(metadataProp.GetRawText());
                            parts.Add(part);
                        }
                        acpMessage = new ACPMessage(parts);
                        if (firstMessage.TryGetProperty("role", out var roleProp))
                            acpMessage.Role = roleProp.GetString() ?? "user";
                    }
                }
            }
            else if (jsonDoc.RootElement.TryGetProperty("parts", out var partsElement))
            {
                // Legacy format: parts directly in root
                var parts = new List<ACPMessagePart>();
                foreach (var partElement in partsElement.EnumerateArray())
                {
                    var part = new ACPMessagePart();
                    if (partElement.TryGetProperty("content", out var contentProp))
                        part.Content = contentProp.GetString();
                    if (partElement.TryGetProperty("content_type", out var contentTypeProp))
                        part.ContentType = contentTypeProp.GetString() ?? "text/plain";
                    else if (partElement.TryGetProperty("mimeType", out var mimeProp)) // Legacy support
                        part.ContentType = mimeProp.GetString() ?? "text/plain";
                    if (partElement.TryGetProperty("content_encoding", out var encodingProp))
                        part.ContentEncoding = encodingProp.GetString() ?? "plain";
                    if (partElement.TryGetProperty("content_url", out var urlProp))
                        part.ContentUrl = urlProp.GetString();
                    if (partElement.TryGetProperty("name", out var nameProp))
                        part.Name = nameProp.GetString();
                    if (partElement.TryGetProperty("metadata", out var metadataProp))
                        part.Metadata = JsonSerializer.Deserialize<object>(metadataProp.GetRawText());
                    parts.Add(part);
                }
                acpMessage = new ACPMessage(parts);
                if (jsonDoc.RootElement.TryGetProperty("role", out var roleProp))
                    acpMessage.Role = roleProp.GetString() ?? "user";
            }
            else
            {
                // Fallback: treat entire body as text
                acpMessage = new ACPMessage(requestBody);
            }
        }
        catch
        {
            // If parsing fails, treat entire body as text message
            acpMessage = new ACPMessage(requestBody);
        }
        
        // Get or create session
        ACPSession? session = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            session = _sessions.GetOrAdd(sessionId, id => new ACPSession(id));
        }
        
        // Generate run ID
        var runId = Guid.NewGuid().ToString();
        
        // Handle streaming mode
        if (mode == "stream")
        {
            // Set up SSE response
            response.StatusCode = 200;
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");
            
            // Send initial run.created event
            // Note: runId is already declared above, reuse it
            var initialResponse = new ACPRunResponse
            {
                AgentName = agentId,
                SessionId = sessionId,
                RunId = runId,
                Status = RunStatus.Created,
                CreatedAt = DateTime.UtcNow
            };
            
            var createdEvent = new RunCreatedEvent { Run = initialResponse };
            EmitEvent(runId, createdEvent);
            var eventJson = $"data: {JsonSerializer.Serialize(createdEvent)}\n\n";
            var eventBytes = Encoding.UTF8.GetBytes(eventJson);
            await response.OutputStream.WriteAsync(eventBytes, 0, eventBytes.Length);
            await response.OutputStream.FlushAsync();
            
            // Send run.in-progress event
            var inProgressEvent = new RunInProgressEvent { Run = initialResponse };
            EmitEvent(runId, inProgressEvent);
            eventJson = $"data: {JsonSerializer.Serialize(inProgressEvent)}\n\n";
            eventBytes = Encoding.UTF8.GetBytes(eventJson);
            await response.OutputStream.WriteAsync(eventBytes, 0, eventBytes.Length);
            await response.OutputStream.FlushAsync();
            
            // Run agent in background and stream events
            _ = Task.Run(async () =>
            {
                try
                {
                    var runResponse = wrapper.Run(acpMessage, CancellationToken.None);
                    runResponse.AgentName = agentId;
                    runResponse.SessionId = sessionId;
                    runResponse.RunId = runId;
                    
                    // Check for await
                    if (runResponse.Status == RunStatus.Awaiting)
                    {
                        var awaitingEvent = new RunAwaitingEvent { Run = runResponse };
                        EmitEvent(runId, awaitingEvent);
                        eventJson = $"data: {JsonSerializer.Serialize(awaitingEvent)}\n\n";
                        eventBytes = Encoding.UTF8.GetBytes(eventJson);
                        await response.OutputStream.WriteAsync(eventBytes, 0, eventBytes.Length);
                        await response.OutputStream.FlushAsync();
                        return;
                    }
                    
                    // Stream message events
                    foreach (var message in runResponse.Output)
                    {
                        var messageCreatedEvent = new MessageCreatedEvent { Message = message };
                        EmitEvent(runId, messageCreatedEvent);
                        eventJson = $"data: {JsonSerializer.Serialize(messageCreatedEvent)}\n\n";
                        eventBytes = Encoding.UTF8.GetBytes(eventJson);
                        await response.OutputStream.WriteAsync(eventBytes, 0, eventBytes.Length);
                        await response.OutputStream.FlushAsync();
                        
                        // Stream message parts
                        foreach (var part in message.Parts)
                        {
                            var partEvent = new MessagePartEvent { Part = part };
                            EmitEvent(runId, partEvent);
                            eventJson = $"data: {JsonSerializer.Serialize(partEvent)}\n\n";
                            eventBytes = Encoding.UTF8.GetBytes(eventJson);
                            await response.OutputStream.WriteAsync(eventBytes, 0, eventBytes.Length);
                            await response.OutputStream.FlushAsync();
                        }
                        
                        var messageCompletedEvent = new MessageCompletedEvent { Message = message };
                        EmitEvent(runId, messageCompletedEvent);
                        eventJson = $"data: {JsonSerializer.Serialize(messageCompletedEvent)}\n\n";
                        eventBytes = Encoding.UTF8.GetBytes(eventJson);
                        await response.OutputStream.WriteAsync(eventBytes, 0, eventBytes.Length);
                        await response.OutputStream.FlushAsync();
                    }
                    
                    // Send run.completed event
                    runResponse.Status = RunStatus.Completed;
                    runResponse.FinishedAt = DateTime.UtcNow;
                    var completedEvent = new RunCompletedEvent { Run = runResponse };
                    EmitEvent(runId, completedEvent);
                    eventJson = $"data: {JsonSerializer.Serialize(completedEvent)}\n\n";
                    eventBytes = Encoding.UTF8.GetBytes(eventJson);
                    await response.OutputStream.WriteAsync(eventBytes, 0, eventBytes.Length);
                    await response.OutputStream.FlushAsync();
                }
                catch (Exception ex)
                {
                    var errorResponse = new ACPRunResponse
                    {
                        AgentName = agentId,
                        SessionId = sessionId,
                        RunId = runId,
                        Status = RunStatus.Failed,
                        Error = new ACPError("server_error", ex.Message),
                        CreatedAt = DateTime.UtcNow,
                        FinishedAt = DateTime.UtcNow
                    };
                    var failedEvent = new RunFailedEvent { Run = errorResponse };
                    EmitEvent(runId, failedEvent);
                    var errorEvent = new ErrorEvent { Error = errorResponse.Error };
                    EmitEvent(runId, errorEvent);
                    var eventJson = $"data: {JsonSerializer.Serialize(failedEvent)}\n\n";
                    var eventBytes = Encoding.UTF8.GetBytes(eventJson);
                    try
                    {
                        await response.OutputStream.WriteAsync(eventBytes, 0, eventBytes.Length);
                        await response.OutputStream.FlushAsync();
                    }
                    catch { }
                }
                finally
                {
                    try
                    {
                        response.Close();
                    }
                    catch { }
                }
            });
            
            return; // Don't close response - streaming will handle it
        }
        
        // Handle async mode
        if (mode == "async")
        {
            // Create initial run response
            var initialResponse = new ACPRunResponse
            {
                AgentName = agentId,
                SessionId = sessionId,
                RunId = runId,
                Status = RunStatus.Created,
                CreatedAt = DateTime.UtcNow
            };
            
            // Emit run.created event
            EmitEvent(runId, new RunCreatedEvent { Run = initialResponse });
            
            // Store initial response
            _runResults[runId] = initialResponse;
            
            // Create cancellation token source
            var cts = new CancellationTokenSource();
            _runCancellations[runId] = cts;
            
            // Start agent execution in background
            var task = Task.Run(() =>
            {
                try
                {
                    var runResponse = wrapper.Run(acpMessage, cts.Token);
                    runResponse.AgentName = agentId;
                    runResponse.SessionId = sessionId;
                    runResponse.RunId = runId;
                    
                    // Emit run.in-progress event
                    EmitEvent(runId, new RunInProgressEvent { Run = runResponse });
                    
                    // Check if agent needs to await
                    if (runResponse.Status == RunStatus.Awaiting && runResponse.AwaitRequest != null)
                    {
                        // Create await handler
                        var awaitHandler = new AwaitHandler(runId, agentId, runResponse.AwaitRequest);
                        _awaitingRuns[runId] = awaitHandler;
                        
                        // Emit run.awaiting event
                        EmitEvent(runId, new RunAwaitingEvent { Run = runResponse });
                        
                        // Store awaiting response
                        _runResults[runId] = runResponse;
                        _runningTasks.TryRemove(runId, out _);
                        _runCancellations.TryRemove(runId, out _);
                        return runResponse;
                    }
                    
                    runResponse.Status = RunStatus.Completed;
                    runResponse.FinishedAt = DateTime.UtcNow;
                    
                    // Emit message events
                    foreach (var message in runResponse.Output)
                    {
                        EmitEvent(runId, new MessageCreatedEvent { Message = message });
                        foreach (var part in message.Parts)
                        {
                            EmitEvent(runId, new MessagePartEvent { Part = part });
                        }
                        EmitEvent(runId, new MessageCompletedEvent { Message = message });
                    }
                    
                    // Emit run.completed event
                    EmitEvent(runId, new RunCompletedEvent { Run = runResponse });
                    
                    // Update session history
                    if (session != null)
                    {
                        var runUri = $"/agents/{agentId}/runs/{runId}";
                        if (!session.History.Contains(runUri))
                            session.History.Add(runUri);
                    }
                    
                    // Store final result
                    _runResults[runId] = runResponse;
                    _runningTasks.TryRemove(runId, out _);
                    _runCancellations.TryRemove(runId, out _);
                    
                    return runResponse;
                }
                catch (Exception ex)
                {
                    var errorResponse = new ACPRunResponse
                    {
                        AgentName = agentId,
                        SessionId = sessionId,
                        RunId = runId,
                        Status = RunStatus.Failed,
                        Error = new ACPError("server_error", ex.Message),
                        CreatedAt = DateTime.UtcNow,
                        FinishedAt = DateTime.UtcNow
                    };
                    _runResults[runId] = errorResponse;
                    _runningTasks.TryRemove(runId, out _);
                    return errorResponse;
                }
            });
            
            _runningTasks[runId] = task;
            
            // Return 202 Accepted with run ID
            response.StatusCode = 202;
            var asyncResponseObj = new
            {
                agent_name = agentId,
                session_id = sessionId,
                run_id = runId,
                status = "created",
                created_at = initialResponse.CreatedAt.ToString("O")
            };
            
            var asyncJson = JsonSerializer.Serialize(asyncResponseObj);
            var asyncBytes = Encoding.UTF8.GetBytes(asyncJson);
            response.ContentType = "application/json";
            response.ContentLength64 = asyncBytes.Length;
            await response.OutputStream.WriteAsync(asyncBytes, 0, asyncBytes.Length);
            response.Close();
            return;
        }
        
        // Sync mode (existing behavior)
        var runResponse = wrapper.Run(acpMessage, CancellationToken.None);
        runResponse.AgentName = agentId;
        runResponse.SessionId = sessionId;
        runResponse.RunId = runId;
        
        // Emit events for sync mode too
        EmitEvent(runId, new RunCreatedEvent { Run = runResponse });
        EmitEvent(runId, new RunInProgressEvent { Run = runResponse });
        
        if (runResponse.Status == RunStatus.Awaiting)
        {
            EmitEvent(runId, new RunAwaitingEvent { Run = runResponse });
        }
        else
        {
            foreach (var message in runResponse.Output)
            {
                EmitEvent(runId, new MessageCreatedEvent { Message = message });
                foreach (var part in message.Parts)
                {
                    EmitEvent(runId, new MessagePartEvent { Part = part });
                }
                EmitEvent(runId, new MessageCompletedEvent { Message = message });
            }
            
            if (runResponse.Status == RunStatus.Completed)
            {
                EmitEvent(runId, new RunCompletedEvent { Run = runResponse });
            }
            else if (runResponse.Status == RunStatus.Failed)
            {
                EmitEvent(runId, new RunFailedEvent { Run = runResponse });
                if (runResponse.Error != null)
                    EmitEvent(runId, new ErrorEvent { Error = runResponse.Error });
            }
        }
        
        // Update session history
        if (session != null)
        {
            var runUri = $"/agents/{agentId}/runs/{runId}";
            if (!session.History.Contains(runUri))
                session.History.Add(runUri);
        }
        
        // Serialize response
        var responseObj = new
        {
            agent_name = agentId,
            session_id = runResponse.SessionId,
            run_id = runResponse.RunId,
            status = runResponse.Status.ToString().ToLower().Replace("InProgress", "in-progress"),
            await_request = runResponse.AwaitRequest,
            output = runResponse.Output.Select(m => new
            {
                role = m.Role,
                parts = m.Parts.Select(p => new
                {
                    name = p.Name,
                    content_type = p.ContentType,
                    content = p.Content,
                    content_encoding = p.ContentEncoding,
                    content_url = p.ContentUrl,
                    metadata = p.Metadata
                }).ToArray(),
                created_at = m.CreatedAt?.ToString("O"),
                completed_at = m.CompletedAt?.ToString("O")
            }).ToArray(),
            error = runResponse.Error != null ? new
            {
                code = runResponse.Error.Code,
                message = runResponse.Error.Message,
                data = runResponse.Error.Data
            } : null,
            created_at = runResponse.CreatedAt.ToString("O"),
            finished_at = runResponse.FinishedAt?.ToString("O")
        };
        
        var json = JsonSerializer.Serialize(responseObj);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.StatusCode = runResponse.Status == RunStatus.Failed ? 500 : 200;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }
    
    private async Task HandleGetSessionAsync(HttpListenerResponse response, string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            response.StatusCode = 404;
            var error = new ACPError("not_found", $"Session '{sessionId}' not found");
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
            return;
        }
        
        var sessionObj = new
        {
            id = session.Id,
            history = session.History,
            state = session.State
        };
        
        var json = JsonSerializer.Serialize(sessionObj);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.StatusCode = 200;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }
    
    private async Task HandleGetRunStatusAsync(HttpListenerResponse response, string agentId, string runId)
    {
        // Check if run is in progress
        if (_runningTasks.TryGetValue(runId, out var task))
        {
            // Check if task is completed
            if (task.IsCompleted)
            {
                try
                {
                    var runResponse = await task;
                    _runResults[runId] = runResponse;
                    _runningTasks.TryRemove(runId, out _);
                    
                    var responseObj = SerializeRunResponse(runResponse);
                    var json = JsonSerializer.Serialize(responseObj);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.StatusCode = 200;
                    response.ContentLength64 = bytes.Length;
                    await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    response.Close();
                    return;
                }
                catch (Exception ex)
                {
                    var errorResponse = new ACPRunResponse
                    {
                        AgentName = agentId,
                        RunId = runId,
                        Status = RunStatus.Failed,
                        Error = new ACPError("server_error", ex.Message),
                        CreatedAt = DateTime.UtcNow,
                        FinishedAt = DateTime.UtcNow
                    };
                    _runResults[runId] = errorResponse;
                    _runningTasks.TryRemove(runId, out _);
                    
                    var responseObj = SerializeRunResponse(errorResponse);
                    var json = JsonSerializer.Serialize(responseObj);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.StatusCode = 500;
                    response.ContentLength64 = bytes.Length;
                    await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    response.Close();
                    return;
                }
            }
            else
            {
                // Task still running
                var inProgressResponse = new ACPRunResponse
                {
                    AgentName = agentId,
                    RunId = runId,
                    Status = RunStatus.InProgress,
                    CreatedAt = DateTime.UtcNow
                };
                
                var responseObj = SerializeRunResponse(inProgressResponse);
                var json = JsonSerializer.Serialize(responseObj);
                var bytes = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.StatusCode = 200;
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                response.Close();
                return;
            }
        }
        
        // Check if run result is stored
        if (_runResults.TryGetValue(runId, out var storedResponse))
        {
            var responseObj = SerializeRunResponse(storedResponse);
            var json = JsonSerializer.Serialize(responseObj);
            var bytes = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.StatusCode = 200;
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
            return;
        }
        
        // Run not found
        response.StatusCode = 404;
        var error = new ACPError("not_found", $"Run '{runId}' not found");
        var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
        var errorBytes = Encoding.UTF8.GetBytes(errorJson);
        response.ContentType = "application/json";
        response.ContentLength64 = errorBytes.Length;
        await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
        response.Close();
    }
    
    private object SerializeRunResponse(ACPRunResponse runResponse)
    {
        return new
        {
            agent_name = runResponse.AgentName,
            session_id = runResponse.SessionId,
            run_id = runResponse.RunId,
            status = runResponse.Status.ToString().ToLower().Replace("InProgress", "in-progress"),
            await_request = runResponse.AwaitRequest,
            output = runResponse.Output.Select(m => new
            {
                role = m.Role,
                parts = m.Parts.Select(p => new
                {
                    name = p.Name,
                    content_type = p.ContentType,
                    content = p.Content,
                    content_encoding = p.ContentEncoding,
                    content_url = p.ContentUrl,
                    metadata = p.Metadata
                }).ToArray(),
                created_at = m.CreatedAt?.ToString("O"),
                completed_at = m.CompletedAt?.ToString("O")
            }).ToArray(),
            error = runResponse.Error != null ? new
            {
                code = runResponse.Error.Code,
                message = runResponse.Error.Message,
                data = runResponse.Error.Data
            } : null,
            created_at = runResponse.CreatedAt.ToString("O"),
            finished_at = runResponse.FinishedAt?.ToString("O")
        };
    }
    
    private async Task HandleCancelRunAsync(HttpListenerResponse response, string agentId, string runId)
    {
        if (!_runCancellations.TryGetValue(runId, out var cts))
        {
            // Run not found or already completed
            response.StatusCode = 404;
            var error = new ACPError("not_found", $"Run '{runId}' not found or already completed");
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
            return;
        }
        
        // Cancel the run
        cts.Cancel();
        
        // Update status to cancelling
        if (_runResults.TryGetValue(runId, out var existingResponse))
        {
            existingResponse.Status = RunStatus.Cancelling;
        }
        
        // Return 202 Accepted
        response.StatusCode = 202;
        var responseObj = new
        {
            agent_name = agentId,
            run_id = runId,
            status = "cancelling"
        };
        
        var json = JsonSerializer.Serialize(responseObj);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }
    
    private async Task HandleResumeRunAsync(HttpListenerRequest request, HttpListenerResponse response, string agentId, string runId)
    {
        if (!_awaitingRuns.TryGetValue(runId, out var awaitHandler))
        {
            response.StatusCode = 404;
            var error = new ACPError("not_found", $"Run '{runId}' is not awaiting or not found");
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
            return;
        }
        
        if (!_registeredAgents.TryGetValue(agentId, out var wrapper))
        {
            response.StatusCode = 404;
            var error = new ACPError("not_found", $"Agent '{agentId}' not found");
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
            return;
        }
        
        // Read request body for resume input
        string requestBody;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            requestBody = await reader.ReadToEndAsync();
        }
        
        // Parse resume input
        string? resumeInput = null;
        try
        {
            var jsonDoc = JsonDocument.Parse(requestBody);
            if (jsonDoc.RootElement.TryGetProperty("await_resume", out var awaitResumeProp))
            {
                if (awaitResumeProp.TryGetProperty("input", out var inputProp))
                    resumeInput = inputProp.GetString();
            }
            else if (jsonDoc.RootElement.TryGetProperty("input", out var inputProp))
            {
                resumeInput = inputProp.GetString();
            }
        }
        catch
        {
            // If parsing fails, treat entire body as input
            resumeInput = requestBody;
        }
        
        if (string.IsNullOrEmpty(resumeInput))
        {
            response.StatusCode = 400;
            var error = new ACPError("invalid_input", "Resume input is required");
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
            return;
        }
        
        // Resume the await handler
        awaitHandler.Resume(resumeInput);
        _awaitingRuns.TryRemove(runId, out _);
        
        // Continue agent execution with resume input
        var resumeMessage = new ACPMessage(resumeInput);
        var runResponse = wrapper.Run(resumeMessage, CancellationToken.None);
        runResponse.AgentName = agentId;
        runResponse.RunId = runId;
        runResponse.Status = RunStatus.Completed;
        
        // Store result
        _runResults[runId] = runResponse;
        
        // Serialize response
        var responseObj = SerializeRunResponse(runResponse);
        var json = JsonSerializer.Serialize(responseObj);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.StatusCode = 200;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }
    
    private async Task HandleGetRunEventsAsync(HttpListenerResponse response, string agentId, string runId)
    {
        if (!_runEvents.TryGetValue(runId, out var events))
        {
            response.StatusCode = 404;
            var error = new ACPError("not_found", $"Run '{runId}' not found or has no events");
            var errorJson = JsonSerializer.Serialize(new { code = error.Code, message = error.Message, data = error.Data });
            var errorBytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentType = "application/json";
            response.ContentLength64 = errorBytes.Length;
            await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            response.Close();
            return;
        }
        
        var responseObj = new
        {
            events = events.Select(e => new
            {
                type = e.Type,
                run = e is RunCreatedEvent rce ? SerializeRunResponse(rce.Run) : null,
                message = e is MessageCreatedEvent mce ? SerializeMessage(mce.Message) : null,
                part = e is MessagePartEvent mpe ? new
                {
                    name = mpe.Part.Name,
                    content_type = mpe.Part.ContentType,
                    content = mpe.Part.Content,
                    content_encoding = mpe.Part.ContentEncoding,
                    content_url = mpe.Part.ContentUrl,
                    metadata = mpe.Part.Metadata
                } : null,
                error = e is ErrorEvent ee ? new
                {
                    code = ee.Error.Code,
                    message = ee.Error.Message,
                    data = ee.Error.Data
                } : null,
                generic = e is GenericEvent ge ? ge.Generic : null
            }).ToArray()
        };
        
        var json = JsonSerializer.Serialize(responseObj);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.StatusCode = 200;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }
    
    private object SerializeMessage(ACPMessage message)
    {
        return new
        {
            role = message.Role,
            parts = message.Parts.Select(p => new
            {
                name = p.Name,
                content_type = p.ContentType,
                content = p.Content,
                content_encoding = p.ContentEncoding,
                content_url = p.ContentUrl,
                metadata = p.Metadata
            }).ToArray(),
            created_at = message.CreatedAt?.ToString("O"),
            completed_at = message.CompletedAt?.ToString("O")
        };
    }
    
    private void EmitEvent(string runId, ACPEvent evt)
    {
        var events = _runEvents.GetOrAdd(runId, _ => new List<ACPEvent>());
        events.Add(evt);
    }
}
