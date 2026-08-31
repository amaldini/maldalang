// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.MCP;

using System.Text.Json;
using System.Collections.Concurrent;
using System.Reflection;
using MaldaLang.Interpreter;
using MaldaLang.BuiltIns;
using ValueType = MaldaLang.Interpreter.ValueType;

public class MCPProtocolHandler
{
    private readonly Interpreter? _interpreter;
    private readonly ConcurrentDictionary<string, MCPToolDefinition> _tools = new();

    public MCPProtocolHandler(Interpreter? interpreter)
    {
        _interpreter = interpreter; // Allow null for transpiled code
    }

    public void RegisterTool(MCPToolDefinition tool)
    {
        _tools[tool.Name] = tool;
    }

    /// <summary>
    /// Discovers <c>@MCPTool</c> functions and returns their definitions (interpret or transpile).
    /// Safe to call without an MCP initialize handshake.
    /// </summary>
    public IReadOnlyList<MCPToolDefinition> ListDiscoveredTools()
    {
        DiscoverTools();
        return new List<MCPToolDefinition>(_tools.Values);
    }

    public JsonRpcResponse HandleRequest(JsonRpcRequest request)
    {
        try
        {
            // Handle notifications (no ID)
            if (request.Id == null)
            {
                HandleNotification(request);
                return new JsonRpcResponse { JsonRpc = "2.0" }; // No response for notifications
            }

            // Handle requests
            switch (request.Method)
            {
                case "initialize":
                    return HandleInitialize(request);
                case "tools/list":
                    return HandleToolsList(request);
                case "tools/call":
                    return HandleToolsCall(request);
                default:
                    return CreateErrorResponse(
                        request.Id,
                        JsonRpcErrorCodes.MethodNotFound,
                        $"Method not found: {request.Method}");
            }
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(
                request.Id,
                JsonRpcErrorCodes.InternalError,
                $"Internal error: {ex.Message}");
        }
    }

    private void HandleNotification(JsonRpcRequest request)
    {
        switch (request.Method)
        {
            case "notifications/initialized":
                // Initialization notification received
                break;
            case "notifications/cancelled":
                // Handle cancellation if needed
                break;
        }
    }

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
    {
        if (request.Params.HasValue)
        {
            var paramsObj = request.Params.Value;
            if (paramsObj.ValueKind == JsonValueKind.Object)
            {
                // Store client info if provided (can be used later)
                // For now, just mark as initialized
            }
        }

        // Discover tools from interpreter
        DiscoverTools();

        // Build capabilities
        var capabilities = new Dictionary<string, object>
        {
            ["tools"] = new Dictionary<string, object>
            {
                ["listChanged"] = false
            }
        };

        var result = new Dictionary<string, object>
        {
            ["protocolVersion"] = "2024-11-05",
            ["version"] = "1.0.0",
            ["capabilities"] = capabilities,
            ["serverInfo"] = new Dictionary<string, object>
            {
                ["name"] = "spl-mcp-server",
                ["version"] = "1.0.0"
            }
        };

        return new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = request.Id,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement
        };
    }

    private JsonRpcResponse HandleToolsList(JsonRpcRequest request)
    {
        var tools = new List<object>();

        foreach (var tool in _tools.Values)
        {
            var toolSchema = ToolSchemaGenerator.GenerateMCPToolSchema(tool);
            tools.Add(JsonSerializer.Deserialize<object>(toolSchema.GetRawText())!);
        }

        var result = new Dictionary<string, object>
        {
            ["tools"] = tools
        };

        return new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = request.Id,
            Result = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement
        };
    }

    private async Task<JsonRpcResponse> HandleToolsCallAsync(JsonRpcRequest request)
    {
        if (!request.Params.HasValue)
        {
            return CreateErrorResponse(
                request.Id,
                JsonRpcErrorCodes.InvalidParams,
                "Missing params");
        }

        var paramsObj = request.Params.Value;
        if (paramsObj.ValueKind != JsonValueKind.Object)
        {
            return CreateErrorResponse(
                request.Id,
                JsonRpcErrorCodes.InvalidParams,
                "Params must be an object");
        }

        // Extract tool name and arguments
        string? toolName = null;
        JsonElement? arguments = null;

        if (paramsObj.TryGetProperty("name", out var nameProp))
        {
            toolName = nameProp.GetString();
        }

        if (paramsObj.TryGetProperty("arguments", out var argsProp))
        {
            arguments = argsProp;
        }

        if (string.IsNullOrEmpty(toolName))
        {
            return CreateErrorResponse(
                request.Id,
                JsonRpcErrorCodes.InvalidParams,
                "Missing tool name");
        }

        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return CreateErrorResponse(
                request.Id,
                JsonRpcErrorCodes.InvalidParams,
                $"Tool '{toolName}' not found");
        }

        try
        {
            RuntimeValue result;
            
            // Check if this is a transpiled tool
            if (tool.TranspiledMethod != null)
            {
                // Transpiled mode: call method directly via reflection
                result = await ExecuteTranspiledToolAsync(tool, arguments);
            }
            else if (_interpreter != null)
            {
                // Interpreted mode: use interpreter
                // Create isolated interpreter for this request
                var requestInterpreter = _interpreter.CreateExecutionInterpreter();
                
                // Look up the function by name in the new interpreter's context
                FunctionValue? requestFunction = null;
                if (tool.FunctionName.Contains("."))
                {
                    // Class method: "ClassName.methodName"
                    var parts = tool.FunctionName.Split('.', 2);
                    var className = parts[0];
                    var methodName = parts[1];
                    
                    if (requestInterpreter._classes.TryGetValue(className, out var klass))
                    {
                        if (klass.Methods.TryGetValue(methodName, out var method))
                        {
                            requestFunction = method;
                        }
                        else if (klass.StaticMethods.TryGetValue(methodName, out var staticMethod))
                        {
                            requestFunction = staticMethod;
                        }
                    }
                }
                else
                {
                    // Global function
                    try
                    {
                        var funcValue = requestInterpreter._globals.Get(tool.FunctionName);
                        if (funcValue.Type == ValueType.Function)
                        {
                            requestFunction = funcValue.AsFunction();
                        }
                    }
                    catch
                    {
                        // Function not found in new interpreter
                    }
                }
                
                if (requestFunction == null)
                {
                    return CreateErrorResponse(
                        request.Id,
                        JsonRpcErrorCodes.InternalError,
                        $"Function '{tool.FunctionName}' not found in interpreter");
                }
                
                // Convert JSON arguments to MALDA RuntimeValues
                var splArgs = ConvertJsonToSplArgs(arguments, tool);

                // Execute MALDA function using isolated interpreter
                result = await requestInterpreter.CallFunctionAsync(requestFunction, splArgs, null);
            }
            else
            {
                return CreateErrorResponse(
                    request.Id,
                    JsonRpcErrorCodes.InternalError,
                    "Tool execution requires either interpreter or transpiled method");
            }

            // Convert MALDA result to JSON string
            var jsonResultObj = ConvertSplToJsonObject(result);
            var resultJsonString = JsonSerializer.Serialize(jsonResultObj);

            // Wrap result in MCP content array format (required by MCP spec)
            var mcpResult = new Dictionary<string, object>
            {
                ["content"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "text",
                        ["text"] = resultJsonString
                    }
                },
                ["isError"] = false
            };

            return new JsonRpcResponse
            {
                JsonRpc = "2.0",
                Id = request.Id,
                Result = JsonDocument.Parse(JsonSerializer.Serialize(mcpResult)).RootElement
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(
                request.Id,
                JsonRpcErrorCodes.InternalError,
                $"Tool execution error: {ex.Message}");
        }
    }

    private JsonRpcResponse HandleToolsCall(JsonRpcRequest request)
    {
        // Synchronous wrapper for async method
        try
        {
            return HandleToolsCallAsync(request).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(
                request.Id,
                JsonRpcErrorCodes.InternalError,
                $"Tool execution error: {ex.Message}");
        }
    }

    private async Task<RuntimeValue> ExecuteTranspiledToolAsync(MCPToolDefinition tool, JsonElement? arguments)
    {
        if (tool.TranspiledMethod == null)
        {
            throw new Exception("TranspiledMethod is null");
        }
        
        var method = tool.TranspiledMethod;
        var methodParams = method.GetParameters();
        var argsArray = new object?[methodParams.Length];
        
        // Convert JSON arguments to method parameters
        if (arguments.HasValue && arguments.Value.ValueKind == JsonValueKind.Object)
        {
            var argsObj = arguments.Value;
            for (int i = 0; i < methodParams.Length; i++)
            {
                var paramName = methodParams[i].Name;
                if (paramName != null && argsObj.TryGetProperty(paramName, out var prop))
                {
                    argsArray[i] = ConvertJsonElementToObject(prop);
                }
                else
                {
                    argsArray[i] = null; // Parameter not found or error
                }
            }
        }
        
        // Call the method (it returns Task<object>)
        var task = (Task<object>)method.Invoke(null, argsArray)!;
        var result = await task;
        
        // Convert result to RuntimeValue
        return ConvertObjectToRuntimeValue(result);
    }
    
    private object? ConvertJsonElementToObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return intVal;
                if (element.TryGetDouble(out var doubleVal))
                    return doubleVal;
                return 0;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.Array:
                var array = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(ConvertJsonElementToObject(item));
                }
                return array;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = ConvertJsonElementToObject(prop.Value);
                }
                return dict;
            default:
                return null;
        }
    }
    
    private RuntimeValue ConvertObjectToRuntimeValue(object? value)
    {
        if (value == null)
            return RuntimeValue.Null();
        
        if (value is Dictionary<string, object?> dict)
        {
            var jsonObj = new BuiltIns.JsonObject();
            foreach (var kvp in dict)
            {
                jsonObj.Set(kvp.Key, ConvertObjectToRuntimeValue(kvp.Value));
            }
            return RuntimeValue.Object(jsonObj);
        }
        
        return value switch
        {
            int i => RuntimeValue.Integer(i),
            long l => RuntimeValue.Integer((int)l),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            string s => RuntimeValue.String(s),
            bool b => RuntimeValue.Boolean(b),
            MaldaLang.Interpreter.ObjectInstance oi => RuntimeValue.Object(oi),
            List<object?> list => RuntimeValue.Array(list.Select(ConvertObjectToRuntimeValue).ToList()),
            _ => RuntimeValue.String(value.ToString() ?? "null")
        };
    }
    
    private List<RuntimeValue> ConvertJsonToSplArgs(JsonElement? arguments, MCPToolDefinition tool)
    {
        var splArgs = new List<RuntimeValue>();

        if (tool.Function?.Declaration == null)
            return splArgs;

        var paramNames = tool.Function.Declaration.Parameters;

        if (arguments.HasValue && arguments.Value.ValueKind == JsonValueKind.Object)
        {
            var argsObj = arguments.Value;
            foreach (var paramName in paramNames)
            {
                if (argsObj.TryGetProperty(paramName, out var prop))
                {
                    splArgs.Add(ConvertJsonElementToRuntimeValue(prop));
                }
                else
                {
                    splArgs.Add(RuntimeValue.Null());
                }
            }
        }
        else
        {
            // No arguments provided, use null for all parameters
            foreach (var _ in paramNames)
            {
                splArgs.Add(RuntimeValue.Null());
            }
        }

        return splArgs;
    }

    private RuntimeValue ConvertJsonElementToRuntimeValue(JsonElement element)
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
                    array.Add(ConvertJsonElementToRuntimeValue(item));
                }
                return RuntimeValue.Array(array);
            case JsonValueKind.Object:
                // Convert JSON object to MALDA object (JsonObject)
                var jsonObj = new BuiltIns.JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    jsonObj.Set(prop.Name, ConvertJsonElementToRuntimeValue(prop.Value));
                }
                return RuntimeValue.Object(jsonObj);
            default:
                return RuntimeValue.Null();
        }
    }

    private JsonElement ConvertSplToJson(RuntimeValue value)
    {
        var json = ConvertSplToJsonObject(value);
        return JsonDocument.Parse(JsonSerializer.Serialize(json)).RootElement;
    }

    private object ConvertSplToJsonObject(RuntimeValue value)
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
                return array.Select(ConvertSplToJsonObject).ToList();
            case ValueType.Object:
                var obj = value.AsObject();
                var dict = new Dictionary<string, object>();
                // Get all properties from the object
                if (obj is BuiltIns.JsonObject jsonObj)
                {
                    var props = jsonObj.GetProperties();
                    foreach (var kvp in props)
                    {
                        dict[kvp.Key] = ConvertSplToJsonObject(kvp.Value);
                    }
                }
                else
                {
                    // For other object types, try to get common properties
                    try
                    {
                        var name = obj.Get("name", null);
                        if (name.Type != ValueType.Null)
                            dict["name"] = ConvertSplToJsonObject(name);
                    }
                    catch { }
                }
                return dict;
            default:
                return null!;
        }
    }

    private void DiscoverTools()
    {
        _tools.Clear();
        
        if (_interpreter != null)
        {
            // Interpreted mode: discover tools from interpreter
            var functions = _interpreter.GetDecoratedFunctions("MCPTool");

            foreach (var (function, functionName) in functions)
            {
                var toolDef = ToolSchemaGenerator.CreateToolDefinition(function, functionName, _interpreter);
                if (toolDef != null)
                {
                    RegisterTool(toolDef);
                }
            }
        }
        else
        {
            // Transpiled mode: discover tools from ToolRegistry
            var allTools = ToolRegistry.Instance.GetAllTools();
            foreach (var kvp in allTools)
            {
                var tool = kvp.Value;
                
                // Check if this is an MCPTool (we need a way to identify MCPTools)
                // For now, we'll check if the tool has a transpiled method and was registered as MCPTool
                // We can use the tool name or check if it's in a specific registry
                
                // Get the transpiled method if available
                var transpiledMethod = tool.GetTranspiledMethod();
                if (transpiledMethod != null)
                {
                    // Check if this method has MCPTool attribute
                    var attributes = transpiledMethod.GetCustomAttributes(false);
                    bool isMCPTool = false;
                    string? toolName = null;
                    string? toolDescription = null;
                    
                    foreach (var attr in attributes)
                    {
                        var attrTypeName = attr.GetType().Name;
                        if (attrTypeName == "MCPToolAttribute" || attrTypeName.EndsWith("Attribute") && 
                            attrTypeName.Substring(0, attrTypeName.Length - 9) == "MCPTool")
                        {
                            isMCPTool = true;
                            
                            // Extract tool name and description from attribute
                            var argsProp = attr.GetType().GetProperty("Arguments");
                            if (argsProp != null)
                            {
                                var args = argsProp.GetValue(attr) as object[];
                                if (args != null && args.Length >= 2)
                                {
                                    toolName = args[0]?.ToString() ?? tool.Name;
                                    toolDescription = args[1]?.ToString() ?? tool.Description;
                                }
                            }
                            break;
                        }
                    }
                    
                    if (isMCPTool)
                    {
                        JsonElement? customSchema = null;
                        var parameters = tool.GetParametersSchema();
                        if (parameters.Type == ValueType.Object)
                            customSchema = ToolSchemaResolver.ToJsonElement(parameters);

                        // Create MCPToolDefinition for transpiled tool
                        var toolDef = new MCPToolDefinition
                        {
                            Name = toolName ?? tool.Name,
                            Description = toolDescription ?? tool.Description,
                            Function = null!, // No FunctionValue for transpiled tools
                            FunctionName = transpiledMethod.Name,
                            TranspiledMethod = transpiledMethod,
                            CustomSchema = customSchema
                        };
                        RegisterTool(toolDef);
                    }
                }
            }
        }
    }

    private JsonRpcResponse CreateErrorResponse(object? id, int code, string message)
    {
        return new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = id,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message
            }
        };
    }
}