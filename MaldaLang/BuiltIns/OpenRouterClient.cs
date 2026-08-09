// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.Net.Http;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Specialized client for OpenRouter API that automatically configures the endpoint and API key.
/// Supports OpenRouter app attribution headers via <c>httpReferer</c>, <c>appTitle</c>, and
/// <c>appCategories</c> so usage can be split per application in OpenRouter analytics.
/// </summary>
public class OpenRouterClientInstance : LLMClientInstance
{
    private const string OpenRouterApiUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string DefaultModel = "deepseek/deepseek-v4-flash";

    /// <summary>Maps to the <c>HTTP-Referer</c> header (primary app URL / identifier).</summary>
    public string HttpReferer { get; set; } = "";

    /// <summary>Maps to the <c>X-OpenRouter-Title</c> header (display name in analytics).</summary>
    public string AppTitle { get; set; } = "";

    /// <summary>Maps to the <c>X-OpenRouter-Categories</c> header (optional, comma-separated).</summary>
    public string AppCategories { get; set; } = "";

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
        if (name == "httpReferer")
            return RuntimeValue.String(HttpReferer ?? "");
        if (name == "appTitle")
            return RuntimeValue.String(AppTitle ?? "");
        if (name == "appCategories")
            return RuntimeValue.String(AppCategories ?? "");

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

    public override void Set(string name, RuntimeValue value)
    {
        if (name == "httpReferer")
        {
            HttpReferer = CoerceAttributionString(value);
            return;
        }

        if (name == "appTitle")
        {
            AppTitle = CoerceAttributionString(value);
            return;
        }

        if (name == "appCategories")
        {
            AppCategories = CoerceAttributionString(value);
            return;
        }

        base.Set(name, value);
    }

    protected override void ApplyRequestHeaders(HttpRequestMessage request)
    {
        foreach (var (headerName, headerValue) in GetAttributionHeaders())
        {
            request.Headers.TryAddWithoutValidation(headerName, headerValue);
        }
    }

    /// <summary>
    /// Attribution headers that will be sent on OpenRouter requests (for tests / inspection).
    /// </summary>
    internal IReadOnlyList<(string Name, string Value)> GetAttributionHeaders()
    {
        var headers = new List<(string Name, string Value)>();
        var referer = (HttpReferer ?? "").Trim();
        var title = (AppTitle ?? "").Trim();
        var categories = (AppCategories ?? "").Trim();

        if (referer.Length > 0)
            headers.Add(("HTTP-Referer", referer));
        if (title.Length > 0)
            headers.Add(("X-OpenRouter-Title", title));
        if (categories.Length > 0)
            headers.Add(("X-OpenRouter-Categories", categories));

        return headers;
    }

    private static string CoerceAttributionString(RuntimeValue value)
    {
        if (value.Type == ValueType.Null)
            return "";
        if (value.Type == ValueType.String)
            return value.AsString() ?? "";
        return value.ToString() ?? "";
    }
}
