// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.Middleware;

using MaldaLang.Interpreter;

/// <summary>
/// Interface for middleware that can process requests and responses.
/// </summary>
public interface IMiddleware
{
    /// <summary>
    /// Processes a request before it's sent to the backend.
    /// </summary>
    /// <param name="request">The request object.</param>
    /// <param name="next">Function to call the next middleware or backend.</param>
    /// <returns>The processed request or response.</returns>
    RuntimeValue ProcessRequest(RuntimeValue request, Func<RuntimeValue> next);
    
    /// <summary>
    /// Processes a response after it's received from the backend.
    /// </summary>
    /// <param name="response">The response object.</param>
    /// <returns>The processed response.</returns>
    RuntimeValue ProcessResponse(RuntimeValue response);
}