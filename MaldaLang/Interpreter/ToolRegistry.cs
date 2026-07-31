// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.BuiltIns;
using System.Collections.Concurrent;
using System.Linq;

public class ToolRegistry
{
    private static ToolRegistry? _instance;
    private ConcurrentDictionary<string, ToolInstance> _tools = new();
    private HashSet<string> _persistentTools = new(); // Tools that should persist across script executions (e.g., from IDE MCP servers)
    
    private ToolRegistry()
    {
    }
    
    public static ToolRegistry Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new ToolRegistry();
            }
            return _instance;
        }
    }
    
    public void RegisterTool(ToolInstance tool, bool persistent = false)
    {
        if (string.IsNullOrEmpty(tool.Name))
            throw new RuntimeException("Tool name cannot be empty");
        
        if (!_tools.TryAdd(tool.Name, tool))
            throw new RuntimeException($"Tool '{tool.Name}' is already registered");
        
        if (persistent)
        {
            _persistentTools.Add(tool.Name);
        }
    }
    
    /// <summary>
    /// Marks a tool as persistent, meaning it should not be cleared when clearing user-defined tools.
    /// Used by IDE services to mark external MCP server tools.
    /// </summary>
    public void MarkToolAsPersistent(string toolName)
    {
        if (_tools.ContainsKey(toolName))
        {
            _persistentTools.Add(toolName);
        }
    }
    
    public ToolInstance? GetTool(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }
    
    public Dictionary<string, ToolInstance> GetAllTools()
    {
        return new Dictionary<string, ToolInstance>(_tools);
    }
    
    public List<string> GetToolNames()
    {
        return new List<string>(_tools.Keys);
    }
    
    public void Clear()
    {
        _tools.Clear();
        _persistentTools.Clear();
    }
    
    /// <summary>
    /// Clears only user-defined tools (those not marked as persistent).
    /// Persistent tools (e.g., from IDE MCP servers) are preserved across script executions.
    /// Script-defined tools (@Tool decorator) are cleared.
    /// </summary>
    public void ClearUserDefinedTools()
    {
        var toolsToRemove = _tools.Keys.Where(name => !_persistentTools.Contains(name)).ToList();
        foreach (var toolName in toolsToRemove)
        {
            _tools.TryRemove(toolName, out _);
        }
    }
    
    public bool HasTool(string name)
    {
        return _tools.ContainsKey(name);
    }
}