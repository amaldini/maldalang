// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.Routing;

using System.Collections.Generic;
using System.Linq;
using MaldaLang.Interpreter;

/// <summary>
/// Router that selects the backend with the least active requests.
/// </summary>
public class LeastLoadedRouter : IRouter
{
    private readonly Dictionary<int, int> _activeRequests = new();
    private readonly object _lock = new object();
    
    public int SelectBackend(RuntimeValue request, List<int> availableBackends)
    {
        if (availableBackends.Count == 0)
            return -1;
        
        lock (_lock)
        {
            // Find backend with minimum active requests
            var selected = availableBackends
                .OrderBy(idx => _activeRequests.GetValueOrDefault(idx, 0))
                .First();
            
            // Increment active request count
            _activeRequests[selected] = _activeRequests.GetValueOrDefault(selected, 0) + 1;
            
            return selected;
        }
    }
    
    /// <summary>
    /// Decrements the active request count for a backend (call when request completes).
    /// </summary>
    public void RequestCompleted(int backendIndex)
    {
        lock (_lock)
        {
            if (_activeRequests.ContainsKey(backendIndex))
            {
                _activeRequests[backendIndex] = Math.Max(0, _activeRequests[backendIndex] - 1);
            }
        }
    }
}