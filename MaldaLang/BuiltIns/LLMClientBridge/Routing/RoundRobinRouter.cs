// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.Routing;

using System.Collections.Generic;
using MaldaLang.Interpreter;

/// <summary>
/// Router that distributes requests evenly across backends using round-robin.
/// </summary>
public class RoundRobinRouter : IRouter
{
    private int _currentIndex = 0;
    private readonly object _lock = new object();
    
    public int SelectBackend(RuntimeValue request, List<int> availableBackends)
    {
        if (availableBackends.Count == 0)
            return -1;
        
        lock (_lock)
        {
            var selected = availableBackends[_currentIndex % availableBackends.Count];
            _currentIndex = (_currentIndex + 1) % availableBackends.Count;
            return selected;
        }
    }
}