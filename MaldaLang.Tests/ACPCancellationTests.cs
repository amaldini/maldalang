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
public class ACPCancellationTests
{
    [Fact]
    public void ACPClient_CancelRun_Exists()
    {
        var client = new ACPClientInstance("https://acp.example.com");
        var method = client.Get("cancelRun", null);
        Assert.Equal(ValueType.Function, method.Type);
    }
    
    [Fact]
    public void ACPServer_CancelRun_Returns202Accepted()
    {
        var server = new ACPServerInstance(8094);
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var args = new List<RuntimeValue>
        {
            RuntimeValue.String("test-agent-cancel"),
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
                agent_name = "test-agent-cancel",
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
            var createResponse = httpClient.PostAsync("http://localhost:8094/agents/test-agent-cancel/runs", content)
                .GetAwaiter()
                .GetResult();
            
            var createJson = createResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var createObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(createJson);
            var runId = createObj.GetProperty("run_id").GetString();
            
            // Cancel the run
            var cancelContent = new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var cancelResponse = httpClient.PostAsync($"http://localhost:8094/agents/test-agent-cancel/runs/{runId}/cancel", cancelContent)
                .GetAwaiter()
                .GetResult();
            
            // Should return 202 Accepted
            Assert.Equal(System.Net.HttpStatusCode.Accepted, cancelResponse.StatusCode);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }
}
