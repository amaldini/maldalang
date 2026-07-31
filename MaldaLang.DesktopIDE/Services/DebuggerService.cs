// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Models;

namespace MaldaLang.DesktopIDE.Services;

public class DebuggerService
{
    private readonly List<Breakpoint> _breakpoints = new();
    private DebugState _debugState = new();
    
    public List<Breakpoint> Breakpoints => _breakpoints;
    
    public DebugState State => _debugState;
    
    public event Action? BreakpointsChanged;
    
    public void AddBreakpoint(Breakpoint breakpoint)
    {
        if (!_breakpoints.Any(b => b.Line == breakpoint.Line && b.FilePath == breakpoint.FilePath))
        {
            _breakpoints.Add(breakpoint);
            BreakpointsChanged?.Invoke();
        }
    }
    
    public void RemoveBreakpoint(int line, string filePath)
    {
        var removed = _breakpoints.RemoveAll(b => b.Line == line && b.FilePath == filePath);
        if (removed > 0)
        {
            BreakpointsChanged?.Invoke();
        }
    }
    
    public void ToggleBreakpoint(int line, string filePath)
    {
        var existing = _breakpoints.FirstOrDefault(b => b.Line == line && b.FilePath == filePath);
        if (existing != null)
        {
            RemoveBreakpoint(line, filePath);
        }
        else
        {
            AddBreakpoint(new Breakpoint { Line = line, FilePath = filePath });
        }
    }
    
    public bool IsBreakpoint(int line, string filePath)
    {
        return _breakpoints.Any(b => b.Line == line && b.FilePath == filePath && b.Enabled);
    }
    
    public void Start()
    {
        _debugState.IsRunning = true;
        _debugState.IsPaused = false;
    }
    
    public void Pause()
    {
        _debugState.IsPaused = true;
    }
    
    public void Resume()
    {
        _debugState.IsPaused = false;
    }
    
    public void Stop()
    {
        _debugState.IsRunning = false;
        _debugState.IsPaused = false;
        _debugState.CurrentLine = null;
        _debugState.CallStack.Clear();
        _debugState.Variables.Clear();
    }
    
    public void SetCurrentLine(int line, string file)
    {
        _debugState.CurrentLine = line;
        _debugState.CurrentFile = file;
    }
    
    public void UpdateCallStack(List<CallStackFrame> frames)
    {
        _debugState.CallStack = frames;
    }
    
    public void UpdateVariables(Dictionary<string, object> variables)
    {
        _debugState.Variables = variables;
    }
    
    /// <summary>
    /// Adjusts breakpoints when lines are inserted or deleted in the document.
    /// </summary>
    /// <param name="filePath">The file path where the change occurred</param>
    /// <param name="lineNumber">The line number where the change occurred (0-based)</param>
    /// <param name="delta">The number of lines added (positive) or removed (negative)</param>
    public void AdjustBreakpointsForLineChange(string filePath, int lineNumber, int delta)
    {
        bool changed = false;
        
        // Process breakpoints in reverse order to avoid index issues
        for (int i = _breakpoints.Count - 1; i >= 0; i--)
        {
            var bp = _breakpoints[i];
            
            // Only adjust breakpoints for the affected file
            if (bp.FilePath != filePath)
                continue;
            
            if (delta > 0)
            {
                // Lines were inserted: move breakpoints at or after the insertion point down
                if (bp.Line >= lineNumber)
                {
                    bp.Line += delta;
                    changed = true;
                }
            }
            else if (delta < 0)
            {
                // Lines were deleted
                int deletedLineCount = -delta;
                int deletionEnd = lineNumber + deletedLineCount - 1;
                
                if (bp.Line >= lineNumber && bp.Line <= deletionEnd)
                {
                    // Breakpoint is on a deleted line - remove it
                    _breakpoints.RemoveAt(i);
                    changed = true;
                }
                else if (bp.Line > deletionEnd)
                {
                    // Breakpoint is after the deletion - move it up
                    bp.Line += delta;
                    changed = true;
                }
            }
        }
        
        if (changed)
        {
            BreakpointsChanged?.Invoke();
        }
    }
    
    /// <summary>
    /// Removes all breakpoints for a specific file.
    /// </summary>
    public void ClearBreakpointsForFile(string filePath)
    {
        var removed = _breakpoints.RemoveAll(bp => bp.FilePath == filePath);
        if (removed > 0)
        {
            BreakpointsChanged?.Invoke();
        }
    }
}