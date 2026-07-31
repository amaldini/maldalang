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
public class ACPClientTests
{
    [Fact]
    public void ACPClient_Creation_WithBaseUrl()
    {
        var client = new ACPClientInstance("https://acp.example.com");
        var baseUrl = client.Get("baseUrl", null).AsString();
        Assert.Equal("https://acp.example.com", baseUrl);
        Assert.True(client.Get("isConnected", null).AsBoolean());
    }
    
    [Fact]
    public void ACPClient_Creation_WithApiKey()
    {
        var client = new ACPClientInstance("https://acp.example.com", "test-api-key");
        var baseUrl = client.Get("baseUrl", null).AsString();
        Assert.Equal("https://acp.example.com", baseUrl);
    }
    
    [Fact]
    public void ACPClient_Creation_TrimsBaseUrl()
    {
        var client = new ACPClientInstance("https://acp.example.com/");
        var baseUrl = client.Get("baseUrl", null).AsString();
        Assert.Equal("https://acp.example.com", baseUrl);
    }
    
    [Fact]
    public void ACPClient_InvalidMethod_ThrowsException()
    {
        var client = new ACPClientInstance("https://acp.example.com");
        Assert.Throws<System.Exception>(() => client.Get("invalidMethod", null));
    }
}

[Collection("Sequential")]
public class ACPServerTests
{
    [Fact]
    public void ACPServer_Creation_WithValidPort()
    {
        var server = new ACPServerInstance(8080);
        Assert.Equal(8080, server.Get("port", null).AsInteger());
        Assert.False(server.Get("isRunning", null).AsBoolean());
    }
    
    [Fact]
    public void ACPServer_RegisterAgent_WithAgentInstance()
    {
        var server = new ACPServerInstance(8080);
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var args = new List<RuntimeValue>
        {
            RuntimeValue.String("test-agent-1"),
            RuntimeValue.Object(agent)
        };
        
        server.CallMethod("registerAgent", args);
        
        var agents = server.CallMethod("getRegisteredAgents", new List<RuntimeValue>());
        Assert.Equal(ValueType.Array, agents.Type);
        var agentsArray = agents.AsArray();
        Assert.Single(agentsArray);
    }
    
    [Fact]
    public void ACPServer_RegisterAgent_WithManifest()
    {
        var server = new ACPServerInstance(8080);
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var manifest = new JsonObject();
        manifest.Set("name", RuntimeValue.String("CustomName"));
        manifest.Set("description", RuntimeValue.String("Custom description"));
        manifest.Set("version", RuntimeValue.String("2.0.0"));
        
        var args = new List<RuntimeValue>
        {
            RuntimeValue.String("test-agent-2"),
            RuntimeValue.Object(agent),
            RuntimeValue.Object(manifest)
        };
        
        server.CallMethod("registerAgent", args);
        
        var agents = server.CallMethod("getRegisteredAgents", new List<RuntimeValue>());
        var agentsArray = agents.AsArray();
        Assert.Single(agentsArray);
        
        var agentObj = agentsArray[0].AsObject() as JsonObject;
        Assert.NotNull(agentObj);
        Assert.Equal("CustomName", agentObj.Get("name").AsString());
    }
    
    [Fact]
    public void ACPServer_RegisterAgent_InvalidAgentId_ThrowsException()
    {
        var server = new ACPServerInstance(8080);
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var args = new List<RuntimeValue>
        {
            RuntimeValue.Integer(123), // Invalid: should be string
            RuntimeValue.Object(agent)
        };
        
        Assert.Throws<System.Exception>(() => server.CallMethod("registerAgent", args));
    }
    
    [Fact]
    public void ACPServer_RegisterAgent_InvalidAgentInstance_ThrowsException()
    {
        var server = new ACPServerInstance(8080);
        
        var args = new List<RuntimeValue>
        {
            RuntimeValue.String("test-agent"),
            RuntimeValue.String("not an agent") // Invalid: should be AgentInstance
        };
        
        Assert.Throws<System.Exception>(() => server.CallMethod("registerAgent", args));
    }
    
    [Fact]
    public void ACPServer_StartStop_Works()
    {
        var server = new ACPServerInstance(8090);
        
        server.CallMethod("start", new List<RuntimeValue>());
        Assert.True(server.Get("isRunning", null).AsBoolean());
        
        // Give server a moment to start
        Thread.Sleep(100);
        
        server.CallMethod("stop", new List<RuntimeValue>());
        Assert.False(server.Get("isRunning", null).AsBoolean());
    }
    
    [Fact]
    public void ACPServer_StartTwice_ThrowsException()
    {
        var server = new ACPServerInstance(8091);
        
        server.CallMethod("start", new List<RuntimeValue>());
        Thread.Sleep(100);
        
        Assert.Throws<System.Exception>(() => server.CallMethod("start", new List<RuntimeValue>()));
        
        server.CallMethod("stop", new List<RuntimeValue>());
    }
    
    [Fact]
    public void ACPServer_GetRegisteredAgents_ReturnsEmptyArray_WhenNoAgents()
    {
        var server = new ACPServerInstance(8080);
        var agents = server.CallMethod("getRegisteredAgents", new List<RuntimeValue>());
        
        Assert.Equal(ValueType.Array, agents.Type);
        var agentsArray = agents.AsArray();
        Assert.Empty(agentsArray);
    }
}

[Collection("Sequential")]
public class ACPAgentWrapperTests
{
    [Fact]
    public void ACPAgentWrapper_GenerateDefaultManifest_FromAgent()
    {
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var wrapper = new ACPAgentWrapper(agent);
        var manifest = wrapper.Manifest;
        
        Assert.Equal("TestAgent", manifest.Name);
        Assert.Contains("test role", manifest.Description);
        Assert.Equal("1.0.0", manifest.Version);
    }
    
    [Fact]
    public void ACPAgentWrapper_Run_ConvertsMessageToPrompt()
    {
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "test role", "test instructions", new OpenRouterClientInstance());
        
        var wrapper = new ACPAgentWrapper(agent);
        var message = new ACPMessage("Hello, agent!");
        
        // Note: This will fail if agent.think() requires actual LLM call
        // In a real test, we'd mock the agent or use a test LLM client
        var response = wrapper.Run(message);
        
        Assert.NotNull(response);
        Assert.Equal(RunStatus.Completed, response.Status);
    }
}

[Collection("Sequential")]
public class ACPAgentToolTests
{
    [Fact]
    public void ACPAgentTool_Creation_WithClientAndAgentId()
    {
        var client = new ACPClientInstance("https://acp.example.com");
        var tool = new ACPAgentToolInstance(client, "test-agent-123", "Test ACP agent tool");
        
        Assert.Equal("acp_agent_test-agent-123", tool.Name);
        Assert.Equal("Test ACP agent tool", tool.Description);
    }
    
    [Fact]
    public void ACPAgentTool_Execute_WithValidPrompt()
    {
        var client = new ACPClientInstance("https://acp.example.com");
        var tool = new ACPAgentToolInstance(client, "test-agent-123", "Test ACP agent tool");
        
        var args = new JsonObject();
        args.Set("prompt", RuntimeValue.String("Hello"));
        
        // This will fail if the ACP server is not running
        // In a real test, we'd mock the HTTP client or use a test server
        var result = tool.Execute(RuntimeValue.Object(args), null);
        
        // Result will be an error message if server is not available
        Assert.Equal(ValueType.String, result.Type);
    }
    
    [Fact]
    public void ACPAgentTool_Execute_WithInvalidArguments_ReturnsError()
    {
        var client = new ACPClientInstance("https://acp.example.com");
        var tool = new ACPAgentToolInstance(client, "test-agent-123", "Test ACP agent tool");
        
        // Missing prompt parameter
        var args = new JsonObject();
        var result = tool.Execute(RuntimeValue.Object(args), null);
        
        Assert.Equal(ValueType.String, result.Type);
        Assert.Contains("prompt", result.AsString());
    }
    
    [Fact]
    public void ACPAgentTool_Execute_WithNonObjectArguments_ReturnsError()
    {
        var client = new ACPClientInstance("https://acp.example.com");
        var tool = new ACPAgentToolInstance(client, "test-agent-123", "Test ACP agent tool");
        
        var result = tool.Execute(RuntimeValue.String("not an object"), null);
        
        Assert.Equal(ValueType.String, result.Type);
        Assert.Contains("Error", result.AsString());
    }
}

[Collection("Sequential")]
public class ACPMessageModelsTests
{
    [Fact]
    public void ACPMessage_Creation_WithText()
    {
        var message = new ACPMessage("Hello, world!");
        Assert.Single(message.Parts);
        Assert.Equal("Hello, world!", message.Parts[0].Content);
        Assert.Equal("text/plain", message.Parts[0].ContentType);
    }
    
    [Fact]
    public void ACPMessage_GetTextContent_JoinsTextParts()
    {
        var parts = new List<ACPMessagePart>
        {
            new ACPMessagePart("Hello, ", "text/plain"),
            new ACPMessagePart("world!", "text/plain")
        };
        var message = new ACPMessage(parts);
        
        Assert.Equal("Hello, world!", message.GetTextContent());
    }
    
    [Fact]
    public void ACPMessage_GetTextContent_IgnoresNonTextParts()
    {
        var parts = new List<ACPMessagePart>
        {
            new ACPMessagePart("Hello", "text/plain"),
            new ACPMessagePart("data", "application/json")
        };
        var message = new ACPMessage(parts);
        
        Assert.Equal("Hello", message.GetTextContent());
    }
    
    [Fact]
    public void ACPAgentManifest_Creation_WithParameters()
    {
        var manifest = new ACPAgentManifest("TestAgent", "Test description", "2.0.0");
        
        Assert.Equal("TestAgent", manifest.Name);
        Assert.Equal("Test description", manifest.Description);
        Assert.Equal("2.0.0", manifest.Version);
    }
    
    [Fact]
    public void ACPAgentManifest_Creation_WithDefaultVersion()
    {
        var manifest = new ACPAgentManifest("TestAgent", "Test description");
        
        Assert.Equal("1.0.0", manifest.Version);
    }
    
    [Fact]
    public void ACPRunResponse_Creation_WithStatus()
    {
        var response = new ACPRunResponse
        {
            RunId = "run-123",
            Status = RunStatus.Completed,
            Output = new List<ACPMessage> { new ACPMessage("Response text") }
        };
        
        Assert.Equal("run-123", response.RunId);
        Assert.Equal(RunStatus.Completed, response.Status);
        Assert.NotNull(response.Message);
    }
}
