// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;

namespace MaldaLang.DesktopIDE.Services;

public class ToolCallLogEntry
{
    public DateTime Timestamp { get; set; }
    public string ToolName { get; set; } = "";
    public string Arguments { get; set; } = ""; // Display version (may be truncated)
    public string FullArguments { get; set; } = ""; // Full arguments for copying
    public string Result { get; set; } = "";
    public bool IsError { get; set; }
    public int ArgumentsSize { get; set; } // Size in characters (full arguments)
    public int ResultSize { get; set; } // Size in characters
}

public class ToolCallLogService
{
    private readonly List<ToolCallLogEntry> _entries = new();
    private readonly object _lock = new();
    
    public event Action<ToolCallLogEntry>? ToolCallLogged;
    
    public void LogToolCall(string toolName, string arguments, string result, bool isError = false, string? fullArguments = null)
    {
        var entry = new ToolCallLogEntry
        {
            Timestamp = DateTime.Now,
            ToolName = toolName,
            Arguments = arguments ?? "",
            FullArguments = fullArguments ?? arguments ?? "",
            Result = result ?? "",
            IsError = isError,
            ArgumentsSize = (fullArguments ?? arguments)?.Length ?? 0,
            ResultSize = result?.Length ?? 0
        };
        
        lock (_lock)
        {
            _entries.Add(entry);
        }
        
        ToolCallLogged?.Invoke(entry);
    }
    
    public List<ToolCallLogEntry> GetEntries()
    {
        lock (_lock)
        {
            return new List<ToolCallLogEntry>(_entries);
        }
    }
    
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
    
    public string GetFormattedLog()
    {
        var sb = new StringBuilder();
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                sb.AppendLine($"[{entry.Timestamp:HH:mm:ss}] 🔧 {entry.ToolName}");
                if (!string.IsNullOrEmpty(entry.Arguments))
                {
                    sb.AppendLine($"  📥 Arguments: {entry.Arguments}");
                }
                if (!string.IsNullOrEmpty(entry.Result))
                {
                    var resultPrefix = entry.IsError ? "  ❌ Error: " : "  ✅ Result: ";
                    sb.AppendLine($"{resultPrefix}{entry.Result}");
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }
    
    public List<ToolCallLogEntry> GetEntriesForHtml()
    {
        lock (_lock)
        {
            return new List<ToolCallLogEntry>(_entries);
        }
    }
    
    public (int TotalArgumentsSize, int TotalResultSize, int TotalSize, int ToolCallCount) GetContextUsage()
    {
        lock (_lock)
        {
            int totalArgs = 0;
            int totalResult = 0;
            foreach (var entry in _entries)
            {
                totalArgs += entry.ArgumentsSize;
                totalResult += entry.ResultSize;
            }
            return (totalArgs, totalResult, totalArgs + totalResult, _entries.Count);
        }
    }
}