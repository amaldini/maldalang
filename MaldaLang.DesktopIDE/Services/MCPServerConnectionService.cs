// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.MCP;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.Interpreter;

/// <summary>
/// Service for managing MCP server connections and tool registration.
/// </summary>
public class MCPServerConnectionService
{
    private readonly MCPServerConfigService _configService;
    private readonly Dictionary<string, MCPClient> _connections = new();
    private readonly Dictionary<string, List<string>> _registeredTools = new(); // serverName -> list of tool names

    public MCPServerConnectionService(MCPServerConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// Initialize the service by loading configurations and auto-connecting servers.
    /// </summary>
    public async Task InitializeAsync()
    {
        var config = _configService.LoadConfig();
        
        foreach (var serverConfig in config.Servers)
        {
            if (serverConfig.AutoConnect)
            {
                try
                {
                    await ConnectServerAsync(serverConfig.Name);
                }
                catch
                {
                    // Log error but continue with other servers
                    System.Diagnostics.Debug.WriteLine($"Failed to auto-connect to MCP server: {serverConfig.Name}");
                }
            }
        }
    }

    /// <summary>
    /// Connect to an MCP server by name.
    /// </summary>
    public async Task<bool> ConnectServerAsync(string serverName)
    {
        if (_connections.ContainsKey(serverName))
        {
            // Already connected
            return true;
        }

        var config = _configService.LoadConfig();
        var serverConfig = config.Servers.FirstOrDefault(s => s.Name == serverName);
        
        if (serverConfig == null)
        {
            throw new Exception($"MCP server '{serverName}' not found in configuration");
        }

        try
        {
            var client = new MCPClient(serverName);
            var connected = await client.ConnectAsync(
                serverConfig.Command,
                serverConfig.Args,
                serverConfig.Env.Count > 0 ? serverConfig.Env : null
            );

            if (connected)
            {
                _connections[serverName] = client;
                serverConfig.IsConnected = true;
                
                // Register tools from this server
                await RegisterToolsFromServerAsync(serverName, client);
                
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            serverConfig.IsConnected = false;
            throw new Exception($"Failed to connect to MCP server '{serverName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Disconnect from an MCP server.
    /// </summary>
    public void DisconnectServer(string serverName)
    {
        if (!_connections.TryGetValue(serverName, out var client))
        {
            return;
        }

        // Unregister tools
        if (_registeredTools.TryGetValue(serverName, out var toolNames))
        {
            foreach (var toolName in toolNames)
            {
                // Note: ToolRegistry doesn't have an Unregister method, but we can clear and re-register
                // For now, we'll leave tools registered even after disconnect
                // They just won't work if called
            }
            _registeredTools.Remove(serverName);
        }

        client.Dispose();
        _connections.Remove(serverName);

        // Update config
        var config = _configService.LoadConfig();
        var serverConfig = config.Servers.FirstOrDefault(s => s.Name == serverName);
        if (serverConfig != null)
        {
            serverConfig.IsConnected = false;
        }
    }

    /// <summary>
    /// Get list of connected server names.
    /// </summary>
    public List<string> GetConnectedServers()
    {
        return _connections.Keys.ToList();
    }

    /// <summary>
    /// Check if a server is connected.
    /// </summary>
    public bool IsServerConnected(string serverName)
    {
        return _connections.ContainsKey(serverName) && _connections[serverName].IsConnected;
    }

    /// <summary>
    /// Refresh tools from a connected server.
    /// </summary>
    public async Task RefreshToolsAsync(string serverName)
    {
        if (!_connections.TryGetValue(serverName, out var client))
        {
            throw new Exception($"Server '{serverName}' is not connected");
        }

        // Unregister old tools
        if (_registeredTools.TryGetValue(serverName, out var oldToolNames))
        {
            _registeredTools.Remove(serverName);
        }

        // Re-discover and register tools
        await RegisterToolsFromServerAsync(serverName, client);
    }

    /// <summary>
    /// Get all configured server names.
    /// </summary>
    public List<string> GetConfiguredServers()
    {
        var config = _configService.LoadConfig();
        return config.Servers.Select(s => s.Name).ToList();
    }

    /// <summary>
    /// Get server configuration by name.
    /// </summary>
    public MCPServerConfig? GetServerConfig(string serverName)
    {
        var config = _configService.LoadConfig();
        return config.Servers.FirstOrDefault(s => s.Name == serverName);
    }

    private async Task RegisterToolsFromServerAsync(string serverName, MCPClient client)
    {
        try
        {
            await client.DiscoverToolsAsync();
            
            var toolNames = new List<string>();
            
            foreach (var toolInfo in client.Tools)
            {
                // Register tool with namespace prefix: mcp:serverName:toolName
                var toolName = $"mcp:{serverName}:{toolInfo.Name}";
                
                var mcpTool = new MCPToolInstance(client, toolInfo.Name, toolInfo, toolName);
                
                try
                {
                    // Register tool as persistent so it survives script execution clears
                    ToolRegistry.Instance.RegisterTool(mcpTool, persistent: true);
                    toolNames.Add(toolName);
                }
                catch (Exception ex)
                {
                    // Tool might already be registered - mark it as persistent anyway
                    try
                    {
                        ToolRegistry.Instance.MarkToolAsPersistent(toolName);
                        toolNames.Add(toolName);
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to register or mark tool '{toolName}' as persistent: {ex.Message}");
                    }
                }
            }
            
            _registeredTools[serverName] = toolNames;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to register tools from server '{serverName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Dispose all connections.
    /// </summary>
    public void Dispose()
    {
        foreach (var client in _connections.Values)
        {
            client.Dispose();
        }
        _connections.Clear();
        _registeredTools.Clear();
    }
}