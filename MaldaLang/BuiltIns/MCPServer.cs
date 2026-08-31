// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text.Json;
using MaldaLang.Interpreter;
using MaldaLang.BuiltIns.MCP;
using ValueType = MaldaLang.Interpreter.ValueType;

public class MCPServerInstance : ObjectInstance
{
    private StdioTransport? _transport;
    private MCPProtocolHandler? _protocolHandler;
    private Interpreter? _interpreter;
    private bool _isRunning = false;
    private string _transportType = "stdio";
    private int? _port = null;

    public MCPServerInstance(string? transportType = null, int? port = null, Interpreter? interpreter = null) : base(null)
    {
        _transportType = transportType ?? "stdio";
        _port = port;
        _interpreter = interpreter; // Allow null for transpiled code
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "isRunning")
            return RuntimeValue.Boolean(_isRunning);
        if (name == "transportType")
            return RuntimeValue.String(_transportType);

        // Handle method access
        if (name == "start" || name == "stop" || name == "getTools")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }

        throw new Exception($"Undefined property '{name}' on MCPServer.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "start":
                if (args.Count != 0)
                    throw new Exception("start() expects 0 arguments");
                Start();
                return RuntimeValue.Null();

            case "stop":
                if (args.Count != 0)
                    throw new Exception("stop() expects 0 arguments");
                Stop();
                return RuntimeValue.Null();

            case "getTools":
                if (args.Count != 0)
                    throw new Exception("getTools() expects 0 arguments");
                return GetTools();

            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }

    private void Start()
    {
        if (_isRunning)
            throw new Exception("MCPServer is already running");

        try
        {
            // Initialize protocol handler (supports null interpreter for transpiled code)
            _protocolHandler = new MCPProtocolHandler(_interpreter);

            // Initialize transport based on type
            if (_transportType == "stdio")
            {
                _transport = new StdioTransport();
                _transport.MessageReceived += OnMessageReceived;
                _transport.Start();
            }
            else if (_transportType == "http")
            {
                // HTTP+SSE transport not implemented yet
                throw new Exception("HTTP transport is not yet implemented. Use 'stdio' transport.");
            }
            else
            {
                throw new Exception($"Unknown transport type: {_transportType}. Use 'stdio' or 'http'.");
            }

            _isRunning = true;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to start MCPServer: {ex.Message}");
        }
    }

    private void Stop()
    {
        if (!_isRunning)
            return;

        try
        {
            _transport?.Stop();
            _transport = null;
            _protocolHandler = null;
            _isRunning = false;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error stopping MCPServer: {ex.Message}");
        }
    }

    private RuntimeValue GetTools()
    {
        var handler = _protocolHandler ?? new MCPProtocolHandler(_interpreter);
        var tools = new List<RuntimeValue>();
        foreach (var tool in handler.ListDiscoveredTools())
        {
            var row = new JsonObject();
            row.Set("name", RuntimeValue.String(tool.Name));
            row.Set("description", RuntimeValue.String(tool.Description));
            var inputSchema = MCP.ToolSchemaGenerator.GenerateToolSchema(tool);
            row.Set("inputSchema", ToolSchemaResolver.FromJsonElement(inputSchema));
            tools.Add(RuntimeValue.Object(row));
        }

        return RuntimeValue.Array(tools);
    }

    private void OnMessageReceived(object? sender, string jsonMessage)
    {
        if (_protocolHandler == null || _transport == null)
            return;

        try
        {
            // Parse JSON-RPC request
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(jsonMessage);
            if (request == null)
            {
                SendErrorResponse(null, JsonRpcErrorCodes.ParseError, "Invalid JSON-RPC request");
                return;
            }

            // Handle request
            var response = _protocolHandler.HandleRequest(request);

            // Send response (if not a notification)
            if (request.Id != null)
            {
                var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                _transport.SendMessage(responseJson);
            }
        }
        catch (JsonException ex)
        {
            SendErrorResponse(null, JsonRpcErrorCodes.ParseError, $"JSON parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            SendErrorResponse(null, JsonRpcErrorCodes.InternalError, $"Internal error: {ex.Message}");
        }
    }

    private void SendErrorResponse(object? id, int code, string message)
    {
        if (_transport == null)
            return;

        var errorResponse = new JsonRpcResponse
        {
            JsonRpc = "2.0",
            Id = id,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message
            }
        };

        var responseJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        _transport.SendMessage(responseJson);
    }
}