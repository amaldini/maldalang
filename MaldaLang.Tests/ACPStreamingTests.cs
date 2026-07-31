// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.ACP;
using MaldaLang.Interpreter;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

// Use Collection attribute to ensure ACP tests don't run in parallel with InterpreterTests
// This prevents ACP server Console.WriteLine output from polluting other tests
[Collection("Sequential")]
public class ACPStreamingTests
{
    [Fact]
    public void ACPClient_SendMessageStream_Exists()
    {
        var client = new ACPClientInstance("https://acp.example.com");
        var method = client.Get("sendMessageStream", null);
        Assert.Equal(ValueType.Function, method.Type);
    }
    
    [Fact]
    public void ACPServer_StreamMode_ReturnsSSE()
    {
        var server = new ACPServerInstance(8096);
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var args = new List<RuntimeValue>
        {
            RuntimeValue.String("test-agent-stream"),
            RuntimeValue.Object(agent)
        };
        
        server.CallMethod("registerAgent", args);
        server.CallMethod("start", new List<RuntimeValue>());
        
        Thread.Sleep(100);
        
        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            
            var requestBody = System.Text.Json.JsonSerializer.Serialize(new
            {
                agent_name = "test-agent-stream",
                input = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { content = "Hello", content_type = "text/plain" }
                        }
                    }
                },
                mode = "stream"
            });
            
            var content = new System.Net.Http.StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var response = httpClient.PostAsync("http://localhost:8096/agents/test-agent-stream/runs", content)
                .GetAwaiter()
                .GetResult();
            
            // Should return 200 with text/event-stream content type
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }
}
