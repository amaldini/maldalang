// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.Middleware;

using System.Collections.Generic;
using System.Linq;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Chain of middleware that processes requests and responses in order.
/// </summary>
public class MiddlewareChain
{
    private readonly List<IMiddleware> _middleware = new();
    
    /// <summary>
    /// Adds a middleware to the chain.
    /// </summary>
    public void AddMiddleware(IMiddleware middleware)
    {
        _middleware.Add(middleware);
    }
    
    /// <summary>
    /// Removes a middleware from the chain by index.
    /// </summary>
    public void RemoveMiddleware(int index)
    {
        if (index >= 0 && index < _middleware.Count)
        {
            _middleware.RemoveAt(index);
        }
    }
    
    /// <summary>
    /// Clears all middleware from the chain.
    /// </summary>
    public void Clear()
    {
        _middleware.Clear();
    }
    
    /// <summary>
    /// Executes the middleware chain on a request, then calls the backend function, then processes the response.
    /// </summary>
    public RuntimeValue Execute(RuntimeValue request, Func<RuntimeValue> backendCall)
    {
        // Build the execution chain
        Func<RuntimeValue> chain = backendCall;
        
        // Add response middleware in reverse order (last added processes first)
        for (int i = _middleware.Count - 1; i >= 0; i--)
        {
            var middleware = _middleware[i];
            var next = chain;
            chain = () =>
            {
                var response = next();
                return middleware.ProcessResponse(response);
            };
        }
        
        // Add request middleware in forward order (first added processes first)
        for (int i = 0; i < _middleware.Count; i++)
        {
            var middleware = _middleware[i];
            var next = chain;
            chain = () => middleware.ProcessRequest(request, next);
        }
        
        // Execute the chain
        return chain();
    }
    
    /// <summary>
    /// Gets the number of middleware in the chain.
    /// </summary>
    public int Count => _middleware.Count;
}