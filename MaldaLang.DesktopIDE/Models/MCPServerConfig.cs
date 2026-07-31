// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

public class MCPServerConfig
{
    public string Name { get; set; } = "";  // Unique identifier
    public string Command { get; set; } = "";  // Executable command
    public List<string> Args { get; set; } = new();  // Command arguments
    public Dictionary<string, string> Env { get; set; } = new();  // Environment variables
    public bool AutoConnect { get; set; } = false;  // Connect on startup
    public bool IsConnected { get; set; } = false;  // Runtime state (not persisted)
}