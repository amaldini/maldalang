// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.ACP;

using System;
using System.Collections.Generic;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Wraps a MALDA AgentInstance to make it ACP-compliant.
/// Handles conversion between ACP message format and MALDA agent.think() calls.
/// </summary>
public class ACPAgentWrapper
{
    private readonly AgentInstance _agent;
    private readonly ACPAgentManifest _manifest;
    
    public ACPAgentWrapper(AgentInstance agent, ACPAgentManifest? manifest = null)
    {
        _agent = agent;
        _manifest = manifest ?? GenerateDefaultManifest(agent);
    }
    
    public ACPAgentManifest Manifest => _manifest;
    
    /// <summary>
    /// Run the agent with an ACP message and return an ACP response.
    /// </summary>
    public ACPRunResponse Run(ACPMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check for cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();
            
            // Convert ACP message to MALDA prompt string
            var prompt = message.GetTextContent();
            
            // Call agent.think() synchronously
            // Note: Agent.think() doesn't support cancellation yet, but we check before and after
            var response = _agent.Think(RuntimeValue.String(prompt));
            
            // Check for cancellation after agent execution
            cancellationToken.ThrowIfCancellationRequested();
            
            // Extract content from response
            string responseText = "";
            if (response.Type == ValueType.Object)
            {
                var responseObj = response.AsObject();
                if (responseObj is JsonObject jsonObj)
                {
                    // Check for await indicator (special tool call or response format)
                    var toolCallsProp = jsonObj.Get("tool_calls", null);
                    if (toolCallsProp != null && toolCallsProp.Type == ValueType.Array)
                    {
                        var toolCalls = toolCallsProp.AsArray();
                        foreach (var toolCall in toolCalls)
                        {
                            if (toolCall.Type == ValueType.Object)
                            {
                                var toolCallObj = toolCall.AsObject();
                                if (toolCallObj is JsonObject toolCallJson)
                                {
                                    var functionName = toolCallJson.Get("function", null);
                                    if (functionName != null && functionName.Type == ValueType.Object)
                                    {
                                        var funcObj = functionName.AsObject();
                                        if (funcObj is JsonObject funcJson)
                                        {
                                            var nameProp = funcJson.Get("name", null);
                                            if (nameProp != null && nameProp.Type == ValueType.String)
                                            {
                                                var toolName = nameProp.AsString();
                                                // Check if it's an await tool
                                                if (toolName == "await_input" || toolName == "awaitInput")
                                                {
                                                    var argsProp = funcJson.Get("arguments", null);
                                                    string awaitPrompt = "Please provide input";
                                                    if (argsProp != null && argsProp.Type == ValueType.String)
                                                    {
                                                        try
                                                        {
                                                            var argsJson = System.Text.Json.JsonDocument.Parse(argsProp.AsString());
                                                            if (argsJson.RootElement.TryGetProperty("prompt", out var promptProp))
                                                                awaitPrompt = promptProp.GetString() ?? awaitPrompt;
                                                        }
                                                        catch { }
                                                    }
                                                    
                                                    // Return awaiting status
                                                    return new ACPRunResponse
                                                    {
                                                        RunId = Guid.NewGuid().ToString(),
                                                        Status = RunStatus.Awaiting,
                                                        AwaitRequest = new ACPAwaitRequest { Prompt = awaitPrompt },
                                                        CreatedAt = DateTime.UtcNow
                                                    };
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    var contentProp = jsonObj.Get("content");
                    if (contentProp.Type == ValueType.String)
                    {
                        responseText = contentProp.AsString();
                        
                        // Check for special await marker in content
                        if (responseText.StartsWith("__ACP_AWAIT__:"))
                        {
                            var awaitPrompt = responseText.Substring("__ACP_AWAIT__:".Length).Trim();
                            return new ACPRunResponse
                            {
                                RunId = Guid.NewGuid().ToString(),
                                Status = RunStatus.Awaiting,
                                AwaitRequest = new ACPAwaitRequest { Prompt = awaitPrompt },
                                CreatedAt = DateTime.UtcNow
                            };
                        }
                    }
                }
            }
            else if (response.Type == ValueType.String)
            {
                responseText = response.AsString();
                
                // Check for special await marker
                if (responseText.StartsWith("__ACP_AWAIT__:"))
                {
                    var awaitPrompt = responseText.Substring("__ACP_AWAIT__:".Length).Trim();
                    return new ACPRunResponse
                    {
                        RunId = Guid.NewGuid().ToString(),
                        Status = RunStatus.Awaiting,
                        AwaitRequest = new ACPAwaitRequest { Prompt = awaitPrompt },
                        CreatedAt = DateTime.UtcNow
                    };
                }
            }
            
            // Convert MALDA response to ACP format
            var acpResponse = new ACPMessage(responseText);
            
            return new ACPRunResponse
            {
                RunId = Guid.NewGuid().ToString(),
                Status = RunStatus.Completed,
                Output = new List<ACPMessage> { acpResponse },
                CreatedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new ACPRunResponse
            {
                RunId = Guid.NewGuid().ToString(),
                Status = RunStatus.Failed,
                Error = new ACPError("server_error", ex.Message),
                CreatedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow
            };
        }
    }
    
    private ACPAgentManifest GenerateDefaultManifest(AgentInstance agent)
    {
        return new ACPAgentManifest
        {
            Name = agent.Name,
            Description = $"{agent.Role}: {agent.Instructions}",
            Version = "1.0.0"
        };
    }
}
