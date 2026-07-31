// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.Routing;

using System;
using System.Collections.Generic;
using MaldaLang.Interpreter;

/// <summary>
/// Router that uses a custom function to select backends.
/// </summary>
public class CustomRouter : IRouter
{
    private readonly Func<RuntimeValue, List<int>, int> _routerFunction;
    
    public CustomRouter(Func<RuntimeValue, List<int>, int> routerFunction)
    {
        _routerFunction = routerFunction;
    }
    
    public int SelectBackend(RuntimeValue request, List<int> availableBackends)
    {
        if (availableBackends.Count == 0)
            return -1;
        
        return _routerFunction(request, availableBackends);
    }
}