// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.ACP;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// A specialized ToolInstance that wraps an external ACP agent, allowing it to be used as a MALDA tool.
/// When executed, it sends a message to the external ACP agent via ACPClient.
/// </summary>
public class ACPAgentToolInstance : ToolInstance
{
    private ACPClientInstance _acpClient;
    private string _agentId;
    
    public ACPAgentToolInstance(ACPClientInstance acpClient, string agentId, string toolDescription) : base()
    {
        _acpClient = acpClient;
        _agentId = agentId;
        
        // Generate schema with prompt parameter
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("object"));
        
        var properties = new JsonObject();
        var promptParam = new JsonObject();
        promptParam.Set("type", RuntimeValue.String("string"));
        promptParam.Set("description", RuntimeValue.String("The prompt or query to send to the ACP agent"));
        properties.Set("prompt", RuntimeValue.Object(promptParam));
        
        schema.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        required.Add(RuntimeValue.String("prompt"));
        schema.Set("required", RuntimeValue.Array(required));
        
        // Initialize the tool with name, description, and schema
        var toolName = $"acp_agent_{agentId}";
        Initialize(toolName, toolDescription, RuntimeValue.Object(schema), null, "");
    }
    
    /// <summary>
    /// Execute the ACP agent tool by sending a message to the external ACP agent.
    /// Expects arguments to contain a "prompt" parameter.
    /// </summary>
    public override RuntimeValue Execute(RuntimeValue arguments, Interpreter? interpreter = null)
    {
        try
        {
            // Extract prompt from arguments
            if (arguments.Type != ValueType.Object)
            {
                return RuntimeValue.String("Error: ACP agent tool arguments must be an object with a 'prompt' parameter");
            }
            
            var argsObj = arguments.AsObject();
            var promptValue = argsObj.Get("prompt", null);
            
            if (promptValue == null || promptValue.Type != ValueType.String)
            {
                return RuntimeValue.String("Error: ACP agent tool requires a 'prompt' parameter of type string");
            }
            
            var prompt = promptValue.AsString();
            
            // Send message to ACP agent via ACPClient
            var args = new List<RuntimeValue>
            {
                RuntimeValue.String(_agentId),
                RuntimeValue.String(prompt)
            };
            
            var response = _acpClient.CallMethod("sendMessage", args);
            
            // Response should be a string from the ACP agent
            return response;
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing ACP agent tool: {ex.Message}");
        }
    }
}
