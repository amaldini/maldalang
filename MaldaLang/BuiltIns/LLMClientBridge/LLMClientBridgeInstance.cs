// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;
using MaldaLang.BuiltIns.LLMClientBridge.Middleware;
using MaldaLang.BuiltIns.LLMClientBridge.Routing;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;
using MaldaLang.BuiltIns.LLMClientBridge.Failover;
using MaldaLang.BuiltIns.LLMClientBridge.LoadBalancing;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Unified client bridge that provides a single interface for all LLM backends
/// with optional proxy capabilities (routing, rate limiting, failover, load balancing).
/// </summary>
public class LLMClientBridgeInstance : ObjectInstance
{
    private IBackendAdapter? _primaryAdapter;
    internal List<IBackendAdapter> _backends = new();
    private double _temperature = 0.7;
    private int _maxTokens = 2000;
    
    // Proxy features
    private MiddlewareChain? _middlewareChain;
    private IRouter? _router;
    private RateLimiter? _rateLimiter;
    private QuotaManager? _quotaManager;
    internal FailoverHandler? _failoverHandler;
    internal LoadBalancer? _loadBalancer;
    private int _timeoutMs = 30000; // 30 seconds default
    private bool _auditLogEnabled = false;
    
    public LLMClientBridgeInstance() : base(null)
    {
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "temperature")
            return RuntimeValue.Float(_temperature);
        if (name == "maxTokens")
            return RuntimeValue.Integer(_maxTokens);
        if (name == "backendType")
            return RuntimeValue.String(_primaryAdapter?.BackendType ?? "unknown");
        if (name == "isConnected")
            return RuntimeValue.Boolean(_primaryAdapter?.IsConnected() ?? false);
        
        // Handle method access
        if (name == "chat" || name == "complete" || name == "setTemperature" || name == "setMaxTokens" ||
            name == "addBackend" || name == "removeBackend" || name == "setRoutingStrategy" ||
            name == "setRateLimit" || name == "setQuota" || name == "setRetryPolicy" ||
            name == "addMiddleware" || name == "setTimeout" || name == "enableHealthCheck" ||
            name == "enableAuditLog" || name == "getBackendStatus" || name == "getRateLimitStatus" ||
            name == "getQuotaStatus" || name == "getMetrics" || name == "getBackendType")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on LLMClientBridge.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        switch (methodName)
        {
            case "setTemperature":
                if (args.Count != 1 || args[0].Type != ValueType.Float)
                    throw new Exception("setTemperature() expects 1 float argument");
                _temperature = args[0].AsFloat();
                if (_primaryAdapter != null)
                    _primaryAdapter.Temperature = _temperature;
                foreach (var backend in _backends)
                    backend.Temperature = _temperature;
                return RuntimeValue.Null();
            
            case "setMaxTokens":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("setMaxTokens() expects 1 integer argument");
                _maxTokens = args[0].AsInteger();
                if (_primaryAdapter != null)
                    _primaryAdapter.MaxTokens = _maxTokens;
                foreach (var backend in _backends)
                    backend.MaxTokens = _maxTokens;
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
            
            case "addBackend":
                if (args.Count < 1)
                    throw new Exception("addBackend() expects at least 1 argument");
                // For now, this is a placeholder - full implementation would require parsing backend config
                throw new Exception("addBackend() requires backend configuration object - not yet fully implemented");
            
            case "removeBackend":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("removeBackend() expects 1 integer argument (backend index)");
                RemoveBackend(args[0].AsInteger());
                return RuntimeValue.Null();
            
            case "setRoutingStrategy":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setRoutingStrategy() expects 1 string argument");
                SetRoutingStrategy(args[0].AsString());
                return RuntimeValue.Null();
            
            case "setRateLimit":
                if (args.Count < 2)
                    throw new Exception("setRateLimit() expects at least 2 arguments: (limit, window)");
                if (args[0].Type != ValueType.Integer || args[1].Type != ValueType.String)
                    throw new Exception("setRateLimit() expects (int, string)");
                var limit = args[0].AsInteger();
                SetRateLimit(limit, args[1].AsString());
                return RuntimeValue.Null();
            
            case "setQuota":
                if (args.Count < 2)
                    throw new Exception("setQuota() expects at least 2 arguments: (limit, window)");
                var quotaLimit = args[0].AsInteger();
                if (args[1].Type != ValueType.String)
                    throw new Exception("setQuota() window must be a string");
                SetQuota(quotaLimit, args[1].AsString());
                return RuntimeValue.Null();
            
            case "setRetryPolicy":
                if (args.Count < 1)
                    throw new Exception("setRetryPolicy() expects at least 1 argument: (maxAttempts, backoff?)");
                var maxAttempts = args[0].AsInteger();
                var backoff = args.Count > 1 ? args[1].AsString() : "exponential";
                SetRetryPolicy(maxAttempts, backoff);
                return RuntimeValue.Null();
            
            case "addMiddleware":
                // Middleware would be a function - for now, placeholder
                throw new Exception("addMiddleware() not yet fully implemented - requires function support");
            
            case "setTimeout":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("setTimeout() expects 1 integer argument (milliseconds)");
                _timeoutMs = args[0].AsInteger();
                return RuntimeValue.Null();
            
            case "enableHealthCheck":
                if (args.Count < 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("enableHealthCheck() expects 1 integer argument (interval in seconds)");
                EnableHealthCheck(args[0].AsInteger());
                return RuntimeValue.Null();
            
            case "enableAuditLog":
                if (args.Count != 1 || args[0].Type != ValueType.Boolean)
                    throw new Exception("enableAuditLog() expects 1 boolean argument");
                _auditLogEnabled = args[0].AsBoolean();
                return RuntimeValue.Null();
            
            case "getBackendStatus":
                return GetBackendStatus();
            
            case "getRateLimitStatus":
                var userId = args.Count > 0 && args[0].Type == ValueType.String ? args[0].AsString() : null;
                return GetRateLimitStatus(userId);
            
            case "getQuotaStatus":
                var quotaUserId = args.Count > 0 && args[0].Type == ValueType.String ? args[0].AsString() : null;
                return GetQuotaStatus(quotaUserId);
            
            case "getMetrics":
                return GetMetrics();
            
            case "getBackendType":
                return RuntimeValue.String(_primaryAdapter?.BackendType ?? "unknown");
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    public void SetPrimaryAdapter(IBackendAdapter adapter)
    {
        _primaryAdapter = adapter;
        _primaryAdapter.Temperature = _temperature;
        _primaryAdapter.MaxTokens = _maxTokens;
    }
    
    public void AddBackend(IBackendAdapter adapter)
    {
        _backends.Add(adapter);
        adapter.Temperature = _temperature;
        adapter.MaxTokens = _maxTokens;
    }
    
    public RuntimeValue Chat(RuntimeValue messages, RuntimeValue? tools, RuntimeValue? responseFormat = null, LlmRequestOverrides? overrides = null)
    {
        // Check rate limit
        if (_rateLimiter != null && !_rateLimiter.CheckRateLimit())
        {
            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String("Error: Rate limit exceeded"));
            return RuntimeValue.Object(errorObj);
        }
        
        // Try with retries if failover is enabled
        int maxAttempts = _failoverHandler != null ? 3 : 1;
        Exception? lastException = null;
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Select backend
            var adapter = SelectBackend(messages);
            if (adapter == null)
            {
                var errorObj = new JsonObject();
                errorObj.Set("content", RuntimeValue.String("Error: No backend adapter available"));
                return RuntimeValue.Object(errorObj);
            }
            
            int backendIndex = -1;
            if (_backends.Count > 0)
            {
                backendIndex = _backends.IndexOf(adapter);
            }
            
            try
            {
                RuntimeValue response;
                
                // Apply middleware if configured
                if (_middlewareChain != null)
                {
                    var requestObj = new JsonObject();
                    requestObj.Set("messages", messages);
                    if (tools != null)
                        requestObj.Set("tools", tools);
                    if (responseFormat != null)
                        requestObj.Set("responseFormat", responseFormat);

                    response = _middlewareChain.Execute(
                        RuntimeValue.Object(requestObj),
                        () => adapter.Chat(messages, tools, responseFormat, overrides)
                    );
                }
                else
                {
                    // Direct call
                    response = adapter.Chat(messages, tools, responseFormat, overrides);
                }
                
                // Check if response indicates an error
                if (response.Type == ValueType.Object)
                {
                    var responseObj = response.AsObject();
                    if (responseObj is JsonObject jsonObj)
                    {
                        var content = jsonObj.Get("content", null);
                        if (content != null && content.Type == ValueType.String)
                        {
                            var contentStr = content.AsString();
                            if (contentStr.StartsWith("Error:"))
                            {
                                // This is an error response - try next backend if available
                                if (attempt < maxAttempts && backendIndex >= 0 && _failoverHandler != null)
                                {
                                    _failoverHandler.RecordFailure(backendIndex);
                                    Thread.Sleep(_failoverHandler.GetBackoffDelay(attempt));
                                    continue;
                                }
                                return response; // Return error if no more attempts
                            }
                        }
                    }
                }
                
                // Record success for failover handler
                if (_failoverHandler != null && backendIndex >= 0)
                {
                    _failoverHandler.RecordSuccess(backendIndex);
                }
                
                return response;
            }
            catch (Exception ex)
            {
                lastException = ex;
                
                // Try next backend if available
                if (attempt < maxAttempts && backendIndex >= 0 && _failoverHandler != null)
                {
                    _failoverHandler.RecordFailure(backendIndex);
                    Thread.Sleep(_failoverHandler.GetBackoffDelay(attempt));
                    continue;
                }
            }
        }
        
        // All attempts failed
        var finalErrorObj = new JsonObject();
        var errorMsg = lastException != null 
            ? $"Error: All backends failed. Last error: {lastException.Message}"
            : "Error: All backends failed";
        finalErrorObj.Set("content", RuntimeValue.String(errorMsg));
        return RuntimeValue.Object(finalErrorObj);
    }
    
    public RuntimeValue Complete(string prompt)
    {
        // Check rate limit
        if (_rateLimiter != null && !_rateLimiter.CheckRateLimit())
        {
            var errorObj = new JsonObject();
            errorObj.Set("content", RuntimeValue.String("Error: Rate limit exceeded"));
            return RuntimeValue.Object(errorObj);
        }
        
        // Try with retries if failover is enabled
        int maxAttempts = _failoverHandler != null ? 3 : 1;
        Exception? lastException = null;
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Select backend
            var adapter = SelectBackend(null);
            if (adapter == null)
            {
                var errorObj = new JsonObject();
                errorObj.Set("content", RuntimeValue.String("Error: No backend adapter available"));
                return RuntimeValue.Object(errorObj);
            }
            
            int backendIndex = -1;
            if (_backends.Count > 0)
            {
                backendIndex = _backends.IndexOf(adapter);
            }
            
            try
            {
                // Direct call (complete doesn't use middleware for simplicity)
                var response = adapter.Complete(prompt);
                
                // Check if response indicates an error
                if (response.Type == ValueType.Object)
                {
                    var responseObj = response.AsObject();
                    if (responseObj is JsonObject jsonObj)
                    {
                        var content = jsonObj.Get("content", null);
                        if (content != null && content.Type == ValueType.String)
                        {
                            var contentStr = content.AsString();
                            if (contentStr.StartsWith("Error:"))
                            {
                                // This is an error response - try next backend if available
                                if (attempt < maxAttempts && backendIndex >= 0 && _failoverHandler != null)
                                {
                                    _failoverHandler.RecordFailure(backendIndex);
                                    Thread.Sleep(_failoverHandler.GetBackoffDelay(attempt));
                                    continue;
                                }
                                return response; // Return error if no more attempts
                            }
                        }
                    }
                }
                
                // Record success for failover handler
                if (_failoverHandler != null && backendIndex >= 0)
                {
                    _failoverHandler.RecordSuccess(backendIndex);
                }
                
                return response;
            }
            catch (Exception ex)
            {
                lastException = ex;
                
                // Try next backend if available
                if (attempt < maxAttempts && backendIndex >= 0 && _failoverHandler != null)
                {
                    _failoverHandler.RecordFailure(backendIndex);
                    Thread.Sleep(_failoverHandler.GetBackoffDelay(attempt));
                    continue;
                }
            }
        }
        
        // All attempts failed
        var finalErrorObj = new JsonObject();
        var errorMsg = lastException != null 
            ? $"Error: All backends failed. Last error: {lastException.Message}"
            : "Error: All backends failed";
        finalErrorObj.Set("content", RuntimeValue.String(errorMsg));
        return RuntimeValue.Object(finalErrorObj);
    }
    
    private IBackendAdapter? SelectBackend(RuntimeValue? request)
    {
        // If only one backend, use it
        if (_backends.Count == 0)
        {
            return _primaryAdapter;
        }
        
        // Get available backends
        var availableIndices = new List<int>();
        if (_failoverHandler != null)
        {
            availableIndices = _failoverHandler.GetHealthyBackends();
        }
        else
        {
            availableIndices = Enumerable.Range(0, _backends.Count).ToList();
        }
        
        if (availableIndices.Count == 0)
        {
            // Fallback to primary if no healthy backends
            return _primaryAdapter;
        }
        
        // Use router if configured
        int selectedIndex;
        if (_router != null && request != null)
        {
            selectedIndex = _router.SelectBackend(request, availableIndices);
        }
        else if (_loadBalancer != null)
        {
            selectedIndex = _loadBalancer.GetNextBackend(availableIndices);
        }
        else
        {
            // Default: use first available
            selectedIndex = availableIndices[0];
        }
        
        if (selectedIndex >= 0 && selectedIndex < _backends.Count)
        {
            return _backends[selectedIndex];
        }
        
        return _primaryAdapter;
    }
    
    private void RemoveBackend(int index)
    {
        if (index >= 0 && index < _backends.Count)
        {
            _backends.RemoveAt(index);
        }
    }
    
    private void SetRoutingStrategy(string strategy)
    {
        if (_backends.Count == 0)
        {
            throw new Exception("No backends configured for routing");
        }
        
        switch (strategy.ToLower())
        {
            case "roundrobin":
            case "round-robin":
                _router = new RoundRobinRouter();
                break;
            
            case "leastloaded":
            case "least-loaded":
                _router = new LeastLoadedRouter();
                break;
            
            case "costbased":
            case "cost-based":
                // For cost-based, we need to identify which backends are local vs cloud
                // For now, assume first half are local, second half are cloud
                var localCount = _backends.Count / 2;
                var localIndices = Enumerable.Range(0, localCount).ToList();
                var cloudIndices = Enumerable.Range(localCount, _backends.Count - localCount).ToList();
                _router = new CostBasedRouter(localIndices, cloudIndices);
                break;
            
            default:
                throw new Exception($"Unknown routing strategy: {strategy}");
        }
    }
    
    private void SetRateLimit(int limit, string window)
    {
        var timeSpan = ParseTimeWindow(window);
        _rateLimiter = new RateLimiter(limit, timeSpan);
    }
    
    private void SetQuota(long limit, string window)
    {
        var timeSpan = ParseTimeWindow(window);
        _quotaManager = new QuotaManager(limit, timeSpan);
    }
    
    private void SetRetryPolicy(int maxAttempts, string backoff)
    {
        if (_failoverHandler == null)
        {
            _failoverHandler = new FailoverHandler();
            // Add all backends to failover handler
            for (int i = 0; i < _backends.Count; i++)
            {
                _failoverHandler.AddBackend(i, i); // Priority = index
            }
        }
        
        _failoverHandler.SetRetryPolicy(maxAttempts, backoff);
    }
    
    private void EnableHealthCheck(int intervalSeconds)
    {
        if (_failoverHandler == null)
        {
            _failoverHandler = new FailoverHandler();
            for (int i = 0; i < _backends.Count; i++)
            {
                _failoverHandler.AddBackend(i, i);
            }
        }
        
        _failoverHandler.EnableHealthCheck(TimeSpan.FromSeconds(intervalSeconds));
    }
    
    private RuntimeValue GetBackendStatus()
    {
        var statusObj = new JsonObject();
        var backendsArray = new List<RuntimeValue>();
        
        for (int i = 0; i < _backends.Count; i++)
        {
            var backendObj = new JsonObject();
            backendObj.Set("index", RuntimeValue.Integer(i));
            backendObj.Set("type", RuntimeValue.String(_backends[i].BackendType));
            backendObj.Set("connected", RuntimeValue.Boolean(_backends[i].IsConnected()));
            
            if (_failoverHandler != null)
            {
                var healthyBackends = _failoverHandler.GetHealthyBackends();
                backendObj.Set("healthy", RuntimeValue.Boolean(healthyBackends.Contains(i)));
            }
            else
            {
                backendObj.Set("healthy", RuntimeValue.Boolean(true));
            }
            
            backendsArray.Add(RuntimeValue.Object(backendObj));
        }
        
        statusObj.Set("backends", RuntimeValue.Array(backendsArray));
        return RuntimeValue.Object(statusObj);
    }
    
    private RuntimeValue GetRateLimitStatus(string? userId)
    {
        if (_rateLimiter == null)
        {
            var errorObj = new JsonObject();
            errorObj.Set("error", RuntimeValue.String("Rate limiting not configured"));
            return RuntimeValue.Object(errorObj);
        }
        
        var statusObj = new JsonObject();
        statusObj.Set("remaining", RuntimeValue.Integer(_rateLimiter.GetRemainingRequests(userId)));
        return RuntimeValue.Object(statusObj);
    }
    
    private RuntimeValue GetQuotaStatus(string? userId)
    {
        if (_quotaManager == null)
        {
            var errorObj = new JsonObject();
            errorObj.Set("error", RuntimeValue.String("Quota management not configured"));
            return RuntimeValue.Object(errorObj);
        }
        
        var statusObj = new JsonObject();
        statusObj.Set("remaining", RuntimeValue.Integer((int)_quotaManager.GetRemainingQuota(userId)));
        return RuntimeValue.Object(statusObj);
    }
    
    private RuntimeValue GetMetrics()
    {
        var metricsObj = new JsonObject();
        metricsObj.Set("backendCount", RuntimeValue.Integer(_backends.Count));
        metricsObj.Set("primaryBackendType", RuntimeValue.String(_primaryAdapter?.BackendType ?? "none"));
        return RuntimeValue.Object(metricsObj);
    }
    
    private TimeSpan ParseTimeWindow(string window)
    {
        window = window.ToLower().Trim();
        
        if (window.EndsWith("second") || window.EndsWith("seconds") || window == "second")
        {
            var num = ExtractNumber(window) ?? 1;
            return TimeSpan.FromSeconds(num);
        }
        else if (window.EndsWith("minute") || window.EndsWith("minutes") || window == "minute")
        {
            var num = ExtractNumber(window) ?? 1;
            return TimeSpan.FromMinutes(num);
        }
        else if (window.EndsWith("hour") || window.EndsWith("hours") || window == "hour")
        {
            var num = ExtractNumber(window) ?? 1;
            return TimeSpan.FromHours(num);
        }
        else if (window.EndsWith("day") || window.EndsWith("days") || window == "day")
        {
            var num = ExtractNumber(window) ?? 1;
            return TimeSpan.FromDays(num);
        }
        else
        {
            // Default to minutes
            return TimeSpan.FromMinutes(1);
        }
    }
    
    private int? ExtractNumber(string str)
    {
        var digits = new string(str.TakeWhile(c => char.IsDigit(c)).ToArray());
        if (int.TryParse(digits, out var num))
            return num;
        return null;
    }
}