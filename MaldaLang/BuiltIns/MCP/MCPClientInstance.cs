// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text.Json;
using System.Linq;
using MaldaLang.Interpreter;
using MaldaLang.BuiltIns.MCP;
using ValueType = MaldaLang.Interpreter.ValueType;

public class MCPClientInstance : ObjectInstance
{
    private MCPClient? _client;
    private string _serverName;

    public MCPClientInstance(string serverName) : base(null)
    {
        _serverName = serverName;
        _client = new MCPClient(serverName);
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "serverName")
            return RuntimeValue.String(_serverName);
        if (name == "isConnected")
            return RuntimeValue.Boolean(_client != null && _client.IsConnected);

        // Handle method access
        if (name == "connect" || name == "disconnect" || name == "getTools" || name == "createTool" || 
            name == "callTool" || name == "refreshTools" || name == "createWrapperAgent")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }

        throw new Exception($"Undefined property '{name}' on MCPClient.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "connect":
                return Connect(args);
            case "disconnect":
                return Disconnect(args);
            case "getTools":
                return GetTools(args);
            case "createTool":
                return CreateTool(args);
            case "callTool":
                return CallTool(args);
            case "refreshTools":
                return RefreshTools(args);
            case "createWrapperAgent":
                return CreateWrapperAgent(args);
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }

    private RuntimeValue Connect(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("connect() expects at least 1 argument: (command, args?, env?)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("connect() command must be a string");
        
        var command = args[0].AsString();
        var commandArgs = new List<string>();
        var env = new Dictionary<string, string>();

        // Parse optional args array
        if (args.Count > 1 && args[1].Type == ValueType.Array)
        {
            var argsArray = args[1].AsArray();
            foreach (var arg in argsArray)
            {
                if (arg.Type == ValueType.String)
                    commandArgs.Add(arg.AsString());
            }
        }

        // Parse optional env object
        if (args.Count > 2 && args[2].Type == ValueType.Object)
        {
            var envObj = args[2].AsObject();
            if (envObj is JsonObject jsonEnv)
            {
                var props = jsonEnv.GetProperties();
                foreach (var kvp in props)
                {
                    if (kvp.Value.Type == ValueType.String)
                        env[kvp.Key] = kvp.Value.AsString();
                }
            }
        }

        if (_client == null)
            throw new Exception("MCPClient has been disposed");

        try
        {
            var connected = _client.ConnectAsync(command, commandArgs, env.Count > 0 ? env : null)
                .GetAwaiter()
                .GetResult();
            
            return RuntimeValue.Boolean(connected);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to connect to MCP server: {ex.Message}");
        }
    }

    private RuntimeValue Disconnect(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("disconnect() expects 0 arguments");

        if (_client != null)
        {
            _client.Disconnect();
        }

        return RuntimeValue.Null();
    }

    private RuntimeValue GetTools(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("getTools() expects 0 arguments");

        if (_client == null || !_client.IsConnected)
            throw new Exception("MCPClient is not connected. Call connect() first.");

        var tools = new List<RuntimeValue>();
        foreach (var toolInfo in _client.Tools)
        {
            var toolObj = new JsonObject();
            toolObj.Set("name", RuntimeValue.String(toolInfo.Name));
            toolObj.Set("description", RuntimeValue.String(toolInfo.Description));
            
            // Convert schema to string representation
            var schemaStr = toolInfo.InputSchema.ToString();
            toolObj.Set("schema", RuntimeValue.String(schemaStr));
            
            tools.Add(RuntimeValue.Object(toolObj));
        }

        return RuntimeValue.Array(tools);
    }

    private RuntimeValue CreateTool(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("createTool() expects at least 1 argument: (toolName, registeredName?, register?)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("createTool() toolName must be a string");

        if (_client == null || !_client.IsConnected)
            throw new Exception("MCPClient is not connected. Call connect() first.");

        var toolName = args[0].AsString();
        
        // Find the tool info
        MCPToolInfo? toolInfo = null;
        foreach (var info in _client.Tools)
        {
            if (info.Name == toolName)
            {
                toolInfo = info;
                break;
            }
        }

        if (toolInfo == null)
            throw new Exception($"Tool '{toolName}' not found on MCP server '{_serverName}'");

        // Generate registered name (default: mcp:serverName:toolName, or custom if provided)
        string registeredName;
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            registeredName = args[1].AsString();
        }
        else
        {
            registeredName = $"mcp:{_serverName}:{toolName}";
        }

        // Check if we should register the tool
        bool shouldRegister = false;
        if (args.Count > 2 && args[2].Type == ValueType.Boolean)
        {
            shouldRegister = args[2].AsBoolean();
        }
        else if (args.Count > 1 && args[1].Type == ValueType.Boolean)
        {
            // Allow register flag as second argument if registeredName is not provided
            shouldRegister = args[1].AsBoolean();
            registeredName = $"mcp:{_serverName}:{toolName}";
        }

        // Create MCPToolInstance
        var mcpTool = new MCPToolInstance(_client, toolName, toolInfo, registeredName);
        
        // Register tool if requested
        if (shouldRegister)
        {
            try
            {
                ToolRegistry.Instance.RegisterTool(mcpTool, persistent: false);
            }
            catch (Exception ex)
            {
                // If tool already exists, that's okay - just return the instance
                if (!ex.Message.Contains("already registered"))
                {
                    throw new Exception($"Failed to register tool '{registeredName}': {ex.Message}");
                }
            }
        }
        
        return RuntimeValue.Object(mcpTool);
    }

    private RuntimeValue CallTool(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("callTool() expects at least 1 argument: (toolName, arguments?)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("callTool() toolName must be a string");

        if (_client == null || !_client.IsConnected)
            throw new Exception("MCPClient is not connected. Call connect() first.");

        var toolName = args[0].AsString();
        
        // Verify tool exists
        bool toolExists = false;
        foreach (var info in _client.Tools)
        {
            if (info.Name == toolName)
            {
                toolExists = true;
                break;
            }
        }

        if (!toolExists)
            throw new Exception($"Tool '{toolName}' not found on MCP server '{_serverName}'");

        // Convert arguments to JSON if provided
        JsonElement? jsonArgs = null;
        if (args.Count > 1 && args[1].Type == ValueType.Object)
        {
            jsonArgs = ConvertSPLToJson(args[1]);
        }

        try
        {
            // Call the tool
            var result = _client.CallToolAsync(toolName, jsonArgs)
                .GetAwaiter()
                .GetResult();

            // Convert JSON result back to MALDA RuntimeValue
            return ConvertJsonToSPL(result);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to call tool '{toolName}': {ex.Message}");
        }
    }

    private RuntimeValue RefreshTools(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("refreshTools() expects 0 arguments");

        if (_client == null || !_client.IsConnected)
            throw new Exception("MCPClient is not connected. Call connect() first.");

        try
        {
            _client.DiscoverToolsAsync()
                .GetAwaiter()
                .GetResult();
            
            return RuntimeValue.Boolean(true);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to refresh tools: {ex.Message}");
        }
    }

    private RuntimeValue CreateWrapperAgent(List<RuntimeValue> args)
    {
        if (args.Count < 3)
            throw new Exception("createWrapperAgent() expects at least 3 arguments: (name, role, instructions, llmClient?)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("createWrapperAgent() name must be a string");
        if (args[1].Type != ValueType.String)
            throw new Exception("createWrapperAgent() role must be a string");
        if (args[2].Type != ValueType.String)
            throw new Exception("createWrapperAgent() instructions must be a string");

        if (_client == null || !_client.IsConnected)
            throw new Exception("MCPClient is not connected. Call connect() first.");

        var agentName = args[0].AsString();
        var agentRole = args[1].AsString();
        var agentInstructions = args[2].AsString();

        // Parse optional LLM client (4th argument)
        LLMClientInstance? llmClient = null;
        LlamaCppClientInstance? llamaClient = null;
        LLMClientBridge.LLMClientBridgeInstance? bridgeClient = null;

        if (args.Count > 3 && args[3].Type == ValueType.Object)
        {
            var clientObj = args[3].AsObject();
            
            // Check for different client types
            if (clientObj is LLMClientInstance llm)
            {
                llmClient = llm;
            }
            else if (clientObj is LlamaCppClientInstance llama)
            {
                llamaClient = llama;
            }
            else if (clientObj is LLMClientBridge.LLMClientBridgeInstance bridge)
            {
                bridgeClient = bridge;
            }
            else
            {
                throw new Exception("createWrapperAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge instance");
            }
        }
        else if (args.Count > 3)
        {
            throw new Exception("createWrapperAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge instance");
        }

        // If no client provided, use default local LLM (auto-download from Hugging Face)
        if (llmClient == null && llamaClient == null && bridgeClient == null)
        {
            llamaClient = DefaultLocalLlm.GetDefaultLocalClient();
        }

        // Create the agent instance
        var agent = new AgentInstance();
        agent.Initialize(agentName, agentRole, agentInstructions, llmClient, llamaClient, bridgeClient, null);

        // Get all tools from the MCP server and add them to the agent
        foreach (var toolInfo in _client.Tools)
        {
            var toolName = toolInfo.Name;
            var registeredName = $"mcp:{_serverName}:{toolName}";
            var mcpTool = new MCPToolInstance(_client, toolName, toolInfo, registeredName);
            agent.AddTool(mcpTool);
        }

        return RuntimeValue.Object(agent);
    }

    private JsonElement ConvertSPLToJson(RuntimeValue value)
    {
        var jsonObj = ConvertSPLToJsonObject(value);
        return JsonDocument.Parse(JsonSerializer.Serialize(jsonObj)).RootElement;
    }

    private object ConvertSPLToJsonObject(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.String:
                return value.AsString();
            case ValueType.Integer:
                return value.AsInteger();
            case ValueType.Float:
                return value.AsFloat();
            case ValueType.Boolean:
                return value.AsBoolean();
            case ValueType.Null:
                return null!;
            case ValueType.Array:
                var array = value.AsArray();
                return array.Select(ConvertSPLToJsonObject).ToList();
            case ValueType.Object:
                var obj = value.AsObject();
                var dict = new Dictionary<string, object>();
                
                if (obj is JsonObject jsonObj)
                {
                    var props = jsonObj.GetProperties();
                    foreach (var kvp in props)
                    {
                        dict[kvp.Key] = ConvertSPLToJsonObject(kvp.Value);
                    }
                }
                return dict;
            default:
                return null!;
        }
    }

    private RuntimeValue ConvertJsonToSPL(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return RuntimeValue.Integer(intVal);
                if (element.TryGetDouble(out var doubleVal))
                    return RuntimeValue.Float(doubleVal);
                return RuntimeValue.Float(0);
            case JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            case JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            case JsonValueKind.Null:
                return RuntimeValue.Null();
            case JsonValueKind.Array:
                var array = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(ConvertJsonToSPL(item));
                }
                return RuntimeValue.Array(array);
            case JsonValueKind.Object:
                var jsonObj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    jsonObj.Set(prop.Name, ConvertJsonToSPL(prop.Value));
                }
                return RuntimeValue.Object(jsonObj);
            default:
                return RuntimeValue.Null();
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
