// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.Routing;

using MaldaLang.Interpreter;

/// <summary>
/// Interface for routing strategies that select which backend to use for a request.
/// </summary>
public interface IRouter
{
    /// <summary>
    /// Selects a backend for the given request.
    /// </summary>
    /// <param name="request">The request object.</param>
    /// <param name="availableBackends">List of available backend indices.</param>
    /// <returns>The index of the selected backend, or -1 if none available.</returns>
    int SelectBackend(RuntimeValue request, List<int> availableBackends);
}