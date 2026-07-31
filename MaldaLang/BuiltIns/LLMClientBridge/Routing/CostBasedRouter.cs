// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.Routing;

using System.Collections.Generic;
using System.Linq;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Router that routes simple queries to local backends and complex queries to cloud backends.
/// </summary>
public class CostBasedRouter : IRouter
{
    private readonly List<int> _localBackends = new();
    private readonly List<int> _cloudBackends = new();
    
    public CostBasedRouter(List<int> localBackends, List<int> cloudBackends)
    {
        _localBackends = localBackends;
        _cloudBackends = cloudBackends;
    }
    
    public int SelectBackend(RuntimeValue request, List<int> availableBackends)
    {
        if (availableBackends.Count == 0)
            return -1;
        
        // Determine if request is simple or complex
        bool isSimple = IsSimpleRequest(request);
        
        // Prefer local for simple, cloud for complex
        var preferred = isSimple ? _localBackends : _cloudBackends;
        var availablePreferred = preferred.Where(availableBackends.Contains).ToList();
        
        if (availablePreferred.Count > 0)
        {
            return availablePreferred[0]; // Use first available preferred backend
        }
        
        // Fallback to any available backend
        return availableBackends[0];
    }
    
    private bool IsSimpleRequest(RuntimeValue request)
    {
        // Simple heuristic: requests with fewer messages are simpler
        if (request.Type == ValueType.Object)
        {
            var obj = request.AsObject();
            var messages = GetProperty(obj, "messages");
            if (messages != null && messages.Type == ValueType.Array)
            {
                var messageCount = messages.AsArray().Count;
                // Consider requests with 3 or fewer messages as simple
                return messageCount <= 3;
            }
        }
        return true; // Default to simple if we can't determine
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
}