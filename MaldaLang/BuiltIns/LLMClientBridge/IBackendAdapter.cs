// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Interface for backend adapters that provide LLM functionality.
/// </summary>
public interface IBackendAdapter
{
    /// <summary>
    /// Gets the type of backend (e.g., "server", "api", "openrouter", "local").
    /// </summary>
    string BackendType { get; }
    
    /// <summary>
    /// Gets or sets the temperature for generation.
    /// </summary>
    double Temperature { get; set; }
    
    /// <summary>
    /// Gets or sets the maximum tokens for generation.
    /// </summary>
    int MaxTokens { get; set; }
    
    /// <summary>
    /// Sends a chat request to the backend.
    /// </summary>
    /// <param name="messages">Array of message objects.</param>
    /// <param name="tools">Optional array of tool definitions.</param>
    /// <param name="responseFormat">Optional OpenAI response_format for structured output.</param>
    /// <returns>Response object with content and optional tool_calls.</returns>
    RuntimeValue Chat(RuntimeValue messages, RuntimeValue? tools, RuntimeValue? responseFormat = null, LlmRequestOverrides? overrides = null);
    
    /// <summary>
    /// Sends a completion request to the backend.
    /// </summary>
    /// <param name="prompt">The prompt text.</param>
    /// <returns>Response object with content.</returns>
    RuntimeValue Complete(string prompt);
    
    /// <summary>
    /// Checks if the backend is available/connected.
    /// </summary>
    /// <returns>True if the backend is available, false otherwise.</returns>
    bool IsConnected();
}