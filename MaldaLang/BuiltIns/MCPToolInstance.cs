// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text.Json;
using MaldaLang.BuiltIns.MCP;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public class MCPToolInstance : ToolInstance
{
    private readonly MCPClient _client;
    private readonly string _toolName;
    private readonly MCPToolInfo _toolInfo;

    public MCPToolInstance(MCPClient client, string toolName, MCPToolInfo toolInfo, string registeredName) : base()
    {
        _client = client;
        _toolName = toolName;
        _toolInfo = toolInfo;

        // Initialize base tool properties with the registered name (mcp:serverName:toolName)
        Name = registeredName;
        Description = toolInfo.Description;
        
        // Convert MCP input schema to MALDA parameters format
        var parameters = ConvertMCPSchemaToSPLParameters(toolInfo.InputSchema);
        Initialize(Name, Description, parameters, null, "");
    }

    public override RuntimeValue Execute(RuntimeValue arguments, Interpreter? interpreter = null)
    {
        try
        {
            // Convert MALDA RuntimeValue arguments to JSON
            JsonElement? jsonArgs = null;
            if (arguments.Type == ValueType.Object)
            {
                jsonArgs = ConvertSPLToJson(arguments);
            }

            // Call the remote MCP tool
            var result = _client.CallToolAsync(_toolName, jsonArgs).GetAwaiter().GetResult();

            // Convert JSON result back to MALDA RuntimeValue
            return ConvertJsonToSPL(result);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing MCP tool '{_toolName}': {ex.Message}");
        }
    }

    private RuntimeValue ConvertMCPSchemaToSPLParameters(JsonElement schema)
    {
        // Create a JSON schema object for the tool parameters
        var schemaObj = new JsonObject();
        
        if (schema.ValueKind == JsonValueKind.Object)
        {
            // If schema has properties, use them
            if (schema.TryGetProperty("properties", out var properties))
            {
                var propsObj = new JsonObject();
                schemaObj.Set("type", RuntimeValue.String("object"));
                schemaObj.Set("properties", RuntimeValue.Object(propsObj));

                foreach (var prop in properties.EnumerateObject())
                {
                    var propObj = new JsonObject();
                    if (prop.Value.TryGetProperty("type", out var typeProp))
                    {
                        propObj.Set("type", RuntimeValue.String(typeProp.GetString() ?? "string"));
                    }
                    if (prop.Value.TryGetProperty("description", out var descProp))
                    {
                        propObj.Set("description", RuntimeValue.String(descProp.GetString() ?? ""));
                    }
                    propsObj.Set(prop.Name, RuntimeValue.Object(propObj));
                }

                // Handle required fields
                if (schema.TryGetProperty("required", out var required))
                {
                    var requiredArray = new List<RuntimeValue>();
                    foreach (var req in required.EnumerateArray())
                    {
                        requiredArray.Add(RuntimeValue.String(req.GetString() ?? ""));
                    }
                    schemaObj.Set("required", RuntimeValue.Array(requiredArray));
                }
            }
            else
            {
                // Fallback: empty schema
                schemaObj.Set("type", RuntimeValue.String("object"));
                schemaObj.Set("properties", RuntimeValue.Object(new JsonObject()));
            }
        }
        else
        {
            // Fallback: empty schema
            schemaObj.Set("type", RuntimeValue.String("object"));
            schemaObj.Set("properties", RuntimeValue.Object(new JsonObject()));
        }

        return RuntimeValue.Object(schemaObj);
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
                else
                {
                    // For other object types, we can't enumerate properties easily
                    // Just return an empty dict or try to get known properties
                    // This is a limitation - we'd need reflection or a property list
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
}