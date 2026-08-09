// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Tests;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

[Collection("Sequential")]
public class OpenRouterClientAttributionTests
{
    [Fact]
    public void AttributionHeaders_EmptyByDefault()
    {
        var client = new OpenRouterClientInstance();
        Assert.Empty(client.GetAttributionHeaders());
    }

    [Fact]
    public void AttributionHeaders_IncludeRefererTitleAndCategories()
    {
        var client = new OpenRouterClientInstance();
        client.HttpReferer = " https://example.com/secondbrain ";
        client.AppTitle = " Second Brain ASK ";
        client.AppCategories = " cli-agent ";

        var headers = client.GetAttributionHeaders();
        Assert.Equal(3, headers.Count);
        Assert.Contains(headers, h => h.Name == "HTTP-Referer" && h.Value == "https://example.com/secondbrain");
        Assert.Contains(headers, h => h.Name == "X-OpenRouter-Title" && h.Value == "Second Brain ASK");
        Assert.Contains(headers, h => h.Name == "X-OpenRouter-Categories" && h.Value == "cli-agent");
    }

    [Fact]
    public void AttributionHeaders_OmitBlankFields()
    {
        var client = new OpenRouterClientInstance();
        client.HttpReferer = "https://example.com/app";
        client.AppTitle = "   ";
        client.AppCategories = "";

        var headers = client.GetAttributionHeaders();
        Assert.Single(headers);
        Assert.Equal("HTTP-Referer", headers[0].Name);
        Assert.Equal("https://example.com/app", headers[0].Value);
    }

    [Fact]
    public void OpenRouterClient_SetGet_AttributionProperties()
    {
        var client = new OpenRouterClientInstance("vendor/model");
        client.Set("httpReferer", RuntimeValue.String("https://example.com/my-app"));
        client.Set("appTitle", RuntimeValue.String("My App"));
        client.Set("appCategories", RuntimeValue.String("cli-agent,cloud-agent"));

        Assert.Equal("https://example.com/my-app", client.Get("httpReferer").AsString());
        Assert.Equal("My App", client.Get("appTitle").AsString());
        Assert.Equal("cli-agent,cloud-agent", client.Get("appCategories").AsString());

        var headers = client.GetAttributionHeaders();
        Assert.Equal(3, headers.Count);
    }

    [Fact]
    public void TranspiledOpenRouterClient_AttributionProperties_RoundTrip()
    {
        const string source = """
            var client = new OpenRouterClient("vendor/model");
            client.httpReferer = "https://example.com/my-app";
            client.appTitle = "My App";
            client.appCategories = "cli-agent,cloud-agent";
            print(client.httpReferer);
            print(client.appTitle);
            print(client.appCategories);
            """;

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "https://example.com/my-app\nMy App\ncli-agent,cloud-agent",
            result.StdOut.Replace("\r\n", "\n").TrimEnd());
    }
}
