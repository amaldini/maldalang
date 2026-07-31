// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.MCP;

using System.Text.Json;
using System.Collections.Generic;

public class MCPToolInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public JsonElement InputSchema { get; set; }
}

public class MCPClient : IDisposable
{
    private readonly MCPClientStdioTransport _transport;
    private readonly string _serverName;
    private bool _initialized = false;
    private readonly List<MCPToolInfo> _tools = new();

    public string ServerName => _serverName;
    public bool IsConnected => _transport.IsRunning && _initialized;
    public IReadOnlyList<MCPToolInfo> Tools => _tools.AsReadOnly();

    public MCPClient(string serverName)
    {
        _serverName = serverName;
        _transport = new MCPClientStdioTransport();
    }

    public async Task<bool> ConnectAsync(string command, List<string> args, Dictionary<string, string>? env = null)
    {
        if (IsConnected)
            return true;

        try
        {
            // Start the transport
            var started = await _transport.StartAsync(command, args, env);
            if (!started)
                return false;

            // Initialize the connection
            await InitializeAsync();

            // Discover tools
            await DiscoverToolsAsync();

            return true;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to connect to MCP server '{_serverName}': {ex.Message}", ex);
        }
    }

    private async Task InitializeAsync()
    {
        var initParams = new Dictionary<string, object>
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new Dictionary<string, object>
            {
                ["tools"] = new Dictionary<string, object>
                {
                    ["listChanged"] = false
                }
            },
            ["clientInfo"] = new Dictionary<string, object>
            {
                ["name"] = "spl-mcp-client",
                ["version"] = "1.0.0"
            }
        };

        var request = new JsonRpcRequest
        {
            Method = "initialize",
            Params = JsonDocument.Parse(JsonSerializer.Serialize(initParams)).RootElement,
            Id = 1
        };

        var response = await _transport.SendRequestAsync(request);

        if (response.Error != null)
        {
            throw new Exception($"MCP initialization failed: {response.Error.Message}");
        }

        // Send initialized notification
        var notification = new JsonRpcRequest
        {
            Method = "notifications/initialized"
        };

        try
        {
            await _transport.SendRequestAsync(notification);
        }
        catch
        {
            // Notifications don't require responses, ignore errors
        }

        _initialized = true;
    }

    public async Task DiscoverToolsAsync()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to MCP server");

        var request = new JsonRpcRequest
        {
            Method = "tools/list",
            Id = 2
        };

        var response = await _transport.SendRequestAsync(request);

        if (response.Error != null)
        {
            throw new Exception($"Failed to list tools: {response.Error.Message}");
        }

        _tools.Clear();

        if (response.Result.HasValue && response.Result.Value.ValueKind == JsonValueKind.Object)
        {
            var result = response.Result.Value;
            if (result.TryGetProperty("tools", out var toolsProp) && toolsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolElement in toolsProp.EnumerateArray())
                {
                    var tool = new MCPToolInfo();
                    if (toolElement.TryGetProperty("name", out var nameProp))
                        tool.Name = nameProp.GetString() ?? "";
                    if (toolElement.TryGetProperty("description", out var descProp))
                        tool.Description = descProp.GetString() ?? "";
                    if (toolElement.TryGetProperty("inputSchema", out var schemaProp))
                        tool.InputSchema = schemaProp;

                    if (!string.IsNullOrEmpty(tool.Name))
                    {
                        _tools.Add(tool);
                    }
                }
            }
        }
    }

    public async Task<JsonElement> CallToolAsync(string toolName, JsonElement? arguments = null)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to MCP server");

        var callParams = new Dictionary<string, object>
        {
            ["name"] = toolName
        };

        if (arguments.HasValue)
        {
            callParams["arguments"] = arguments.Value;
        }

        var request = new JsonRpcRequest
        {
            Method = "tools/call",
            Params = JsonDocument.Parse(JsonSerializer.Serialize(callParams)).RootElement,
            Id = 3
        };

        var response = await _transport.SendRequestAsync(request);

        if (response.Error != null)
        {
            throw new Exception($"Tool call failed: {response.Error.Message}");
        }

        if (!response.Result.HasValue)
        {
            return JsonDocument.Parse("null").RootElement;
        }

        // MCP tools/call returns { content: [...] }
        if (response.Result.Value.ValueKind == JsonValueKind.Object)
        {
            if (response.Result.Value.TryGetProperty("content", out var contentProp))
            {
                // Extract the actual result from content array
                if (contentProp.ValueKind == JsonValueKind.Array && contentProp.GetArrayLength() > 0)
                {
                    var firstItem = contentProp[0];
                    if (firstItem.ValueKind == JsonValueKind.Object && firstItem.TryGetProperty("text", out var textProp))
                    {
                        // Return the text content
                        return textProp;
                    }
                    return firstItem;
                }
            }
        }

        return response.Result.Value;
    }

    public void Disconnect()
    {
        _transport.Stop();
        _initialized = false;
        _tools.Clear();
    }

    public void Dispose()
    {
        Disconnect();
        _transport.Dispose();
    }
}