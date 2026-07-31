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
public class ACPAsyncTests
{
    [Fact]
    public void ACPClient_SendMessageAsync_ReturnsRunId()
    {
        // This test would require a running ACP server
        // For now, just test that the method exists and has correct signature
        var client = new ACPClientInstance("https://acp.example.com");
        var method = client.Get("sendMessageAsync", null);
        Assert.Equal(ValueType.Function, method.Type);
    }
    
    [Fact]
    public void ACPServer_AsyncMode_Returns202Accepted()
    {
        var server = new ACPServerInstance(8092);
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var args = new List<RuntimeValue>
        {
            RuntimeValue.String("test-agent-async"),
            RuntimeValue.Object(agent)
        };
        
        server.CallMethod("registerAgent", args);
        server.CallMethod("start", new List<RuntimeValue>());
        
        Thread.Sleep(100); // Give server time to start
        
        try
        {
            // Test async mode by making HTTP request
            using var httpClient = new System.Net.Http.HttpClient();
            var requestBody = System.Text.Json.JsonSerializer.Serialize(new
            {
                agent_name = "test-agent-async",
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
                mode = "async"
            });
            
            var content = new System.Net.Http.StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var response = httpClient.PostAsync("http://localhost:8092/agents/test-agent-async/runs", content)
                .GetAwaiter()
                .GetResult();
            
            // Should return 202 Accepted for async mode
            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }
    
    [Fact]
    public void ACPServer_GetRunStatus_ReturnsStatus()
    {
        var server = new ACPServerInstance(8093);
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var args = new List<RuntimeValue>
        {
            RuntimeValue.String("test-agent-status"),
            RuntimeValue.Object(agent)
        };
        
        server.CallMethod("registerAgent", args);
        server.CallMethod("start", new List<RuntimeValue>());
        
        Thread.Sleep(100);
        
        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            
            // Create async run
            var requestBody = System.Text.Json.JsonSerializer.Serialize(new
            {
                agent_name = "test-agent-status",
                input = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { content = "Test", content_type = "text/plain" }
                        }
                    }
                },
                mode = "async"
            });
            
            var content = new System.Net.Http.StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var createResponse = httpClient.PostAsync("http://localhost:8093/agents/test-agent-status/runs", content)
                .GetAwaiter()
                .GetResult();
            
            Assert.Equal(System.Net.HttpStatusCode.Accepted, createResponse.StatusCode);
            
            var createJson = createResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var createObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(createJson);
            var runId = createObj.GetProperty("run_id").GetString();
            
            Assert.NotNull(runId);
            
            // Get run status
            var statusResponse = httpClient.GetAsync($"http://localhost:8093/agents/test-agent-status/runs/{runId}")
                .GetAwaiter()
                .GetResult();
            
            Assert.Equal(System.Net.HttpStatusCode.OK, statusResponse.StatusCode);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }
}
