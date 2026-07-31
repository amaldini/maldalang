// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Adapter that wraps an existing OpenRouterClientInstance.
/// </summary>
public class OpenRouterAdapter : IBackendAdapter
{
    public string BackendType => "openrouter";
    public double Temperature 
    { 
        get => _client.Temperature; 
        set => _client.Temperature = value; 
    }
    public int MaxTokens 
    { 
        get => _client.MaxTokens; 
        set => _client.MaxTokens = value; 
    }
    
    private readonly OpenRouterClientInstance _client;
    
    public OpenRouterAdapter(OpenRouterClientInstance client)
    {
        _client = client;
    }
    
    public RuntimeValue Chat(RuntimeValue messages, RuntimeValue? tools, RuntimeValue? responseFormat = null, LlmRequestOverrides? overrides = null)
    {
        return _client.Chat(messages, tools, responseFormat, overrides);
    }
    
    public RuntimeValue Complete(string prompt)
    {
        return _client.Complete(prompt);
    }
    
    public bool IsConnected()
    {
        // For OpenRouter, we assume they're always "connected"
        // Actual connectivity is tested when making requests
        return true;
    }
}