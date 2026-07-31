// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Models;

public class DebugState
{
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public int? CurrentLine { get; set; }
    public string? CurrentFile { get; set; }
    public List<CallStackFrame> CallStack { get; set; } = new();
    public Dictionary<string, object> Variables { get; set; } = new();
}

public class CallStackFrame
{
    public string FunctionName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public int Line { get; set; }
    public string File { get; set; } = string.Empty;
}