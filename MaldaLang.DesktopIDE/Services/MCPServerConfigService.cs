// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

using System.IO;
using System.Text.Json;
using MaldaLang.DesktopIDE.Models;

/// <summary>
/// Service for persisting MCP server configurations.
/// </summary>
public class MCPServerConfigService
{
    private readonly string _configFilePath;

    public MCPServerConfigService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "MaldaLang");
        Directory.CreateDirectory(appFolder);
        _configFilePath = Path.Combine(appFolder, "mcp-servers.json");
    }

    /// <summary>
    /// Loads MCP server configurations from disk.
    /// </summary>
    public MCPServersConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<MCPServersConfig>(json);
                return config ?? new MCPServersConfig();
            }
        }
        catch
        {
            // If loading fails, return default config
        }
        return new MCPServersConfig();
    }

    /// <summary>
    /// Saves MCP server configurations to disk.
    /// </summary>
    public void SaveConfig(MCPServersConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch
        {
            // If saving fails, silently ignore
        }
    }
}