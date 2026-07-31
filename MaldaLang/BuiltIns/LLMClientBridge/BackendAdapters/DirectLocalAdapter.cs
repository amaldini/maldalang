// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Adapter that wraps an existing LlamaCppClientInstance for direct local model access.
/// </summary>
public class DirectLocalAdapter : IBackendAdapter
{
    public string BackendType => "local";
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
    
    private readonly LlamaCppClientInstance _client;
    
    public DirectLocalAdapter(LlamaCppClientInstance client)
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
        // For direct local models, check if model is loaded
        // We can't directly check this, so we assume connected if client exists
        return _client != null;
    }
}