// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// A specialized ToolInstance that wraps an AgentInstance, allowing agents to be used as tools.
/// When executed, it calls the agent's think() method with the prompt parameter.
/// </summary>
public class AgentToolInstance : ToolInstance
{
    private AgentInstance _agent;
    
    public AgentToolInstance(AgentInstance agent, string toolName, string toolDescription) : base()
    {
        _agent = agent;
        
        // Generate schema with prompt parameter
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("object"));
        
        var properties = new JsonObject();
        var promptParam = new JsonObject();
        promptParam.Set("type", RuntimeValue.String("string"));
        promptParam.Set("description", RuntimeValue.String("The prompt or query to send to the agent"));
        properties.Set("prompt", RuntimeValue.Object(promptParam));
        
        schema.Set("properties", RuntimeValue.Object(properties));
        
        var required = new List<RuntimeValue>();
        required.Add(RuntimeValue.String("prompt"));
        schema.Set("required", RuntimeValue.Array(required));
        
        // Initialize the tool with name, description, and schema
        Initialize(toolName, toolDescription, RuntimeValue.Object(schema), null, "");
    }
    
    public AgentInstance GetAgent()
    {
        return _agent;
    }
    
    /// <summary>
    /// Execute the agent tool by calling the agent's think method.
    /// Expects arguments to contain a "prompt" parameter.
    /// </summary>
    public override RuntimeValue Execute(RuntimeValue arguments, Interpreter? interpreter = null)
    {
        try
        {
            // Extract prompt from arguments
            if (arguments.Type != ValueType.Object)
            {
                return RuntimeValue.String("Error: Agent tool arguments must be an object with a 'prompt' parameter");
            }
            
            var argsObj = arguments.AsObject();
            var promptValue = argsObj.Get("prompt", null);
            
            if (promptValue == null || promptValue.Type != ValueType.String)
            {
                return RuntimeValue.String("Error: Agent tool requires a 'prompt' parameter of type string");
            }
            
            var prompt = promptValue.AsString();
            
            // Call the agent's think method
            var response = _agent.Think(RuntimeValue.String(prompt));
            
            // Extract content from response
            if (response.Type == ValueType.Object)
            {
                var responseObj = response.AsObject();
                var contentValue = responseObj.Get("content", null);
                if (contentValue != null && contentValue.Type == ValueType.String)
                {
                    return contentValue;
                }
            }
            
            // If response is already a string, return it
            if (response.Type == ValueType.String)
            {
                return response;
            }
            
            // Fallback: convert response to string
            return RuntimeValue.String(response.ToString() ?? "Agent returned no response");
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing agent tool: {ex.Message}");
        }
    }
}
