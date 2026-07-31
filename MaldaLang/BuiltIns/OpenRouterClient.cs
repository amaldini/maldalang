// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using MaldaLang.Interpreter;

/// <summary>
/// Specialized client for OpenRouter API that automatically configures the endpoint and API key.
/// </summary>
public class OpenRouterClientInstance : LLMClientInstance
{
    private const string OpenRouterApiUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string DefaultModel = "deepseek/deepseek-v4-flash";
    
    /// <summary>
    /// Creates a new OpenRouterClient instance.
    /// </summary>
    /// <param name="model">Optional model name. Defaults to "deepseek/deepseek-v4-flash" if not provided.</param>
    public OpenRouterClientInstance(string? model = null)
    {
        // Set the OpenRouter API endpoint
        ApiUrl = OpenRouterApiUrl;
        
        // Get API key from environment variable
        var apiKey = System.Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        
        // Try User environment if not found in Process (Windows-specific)
        if (string.IsNullOrEmpty(apiKey) && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            apiKey = System.Environment.GetEnvironmentVariable("OPENROUTER_API_KEY", EnvironmentVariableTarget.User);
        }
        
        // Try Machine environment if still not found (Windows-specific)
        if (string.IsNullOrEmpty(apiKey) && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            apiKey = System.Environment.GetEnvironmentVariable("OPENROUTER_API_KEY", EnvironmentVariableTarget.Machine);
        }
        
        ApiKey = apiKey ?? "";
        Model = model ?? DefaultModel;
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Use the same property and method access as LLMClient
        // But update the error message to reference OpenRouterClient
        try
        {
            return base.Get(name, accessingClass);
        }
        catch (Exception ex) when (ex.Message.Contains("LLMClient"))
        {
            throw new Exception(ex.Message.Replace("LLMClient", "OpenRouterClient"));
        }
    }
}