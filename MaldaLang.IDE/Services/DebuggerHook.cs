// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using MaldaLang.IDE.Models;
using System.Linq;

namespace MaldaLang.IDE.Services;

/// <summary>
/// Implementation of IDebuggerHook that integrates with DebuggerService
/// </summary>
public class DebuggerHook : IDebuggerHook
{
    private readonly DebuggerService _debuggerService;
    private DebugMode _debugMode = DebugMode.Continue;
    private int _stepOverDepth = 0;
    private int _currentDepth = 0;
    private string? _stepOutFunction = null;
    public event Action<int, string?>? OnPaused;
    
    public DebuggerHook(DebuggerService debuggerService)
    {
        _debuggerService = debuggerService;
    }
    
    public bool OnStatement(int line, string? file = null)
    {
        // If paused, don't continue
        if (_debugMode == DebugMode.Paused)
        {
            return false;
        }
        
        // Check for breakpoint
        if (HasBreakpoint(line, file))
        {
            // Check condition if present
            if (!CheckBreakpointCondition(line, file, () => true))
            {
                return true; // Condition not met, continue
            }
            
            _debugMode = DebugMode.Paused;
            OnPause(line, file);
            return false; // Pause execution
        }
        
        // Handle step modes
        if (_debugMode == DebugMode.StepOver)
        {
            if (_currentDepth <= _stepOverDepth)
            {
                _debugMode = DebugMode.Paused;
                OnPause(line, file);
                return false;
            }
        }
        else if (_debugMode == DebugMode.StepInto)
        {
            _debugMode = DebugMode.Paused;
            OnPause(line, file);
            return false;
        }
        else if (_debugMode == DebugMode.StepOut)
        {
            // Continue until we exit the function
            // This is handled in OnFunctionExit
        }
        
        return true; // Continue execution
    }
    
    public void OnPause(int line, string? file = null)
    {
        _debuggerService.SetCurrentLine(line, file ?? "main.malda");
        _debuggerService.Pause();
        // Notify that we paused so UI can update
        OnPaused?.Invoke(line, file);
    }
    
    public void SetInterpreter(Interpreter.Interpreter? interpreter)
    {
        // Store interpreter reference for updating debug info
        // This will be set by ExecutionService
    }
    
    public void OnFunctionEnter(string functionName, string? className, int line)
    {
        _currentDepth++;
        
        if (_debugMode == DebugMode.StepOver)
        {
            // Don't pause when stepping over - continue execution
            return;
        }
        
        if (_debugMode == DebugMode.StepInto)
        {
            _debugMode = DebugMode.Paused;
            OnPause(line);
        }
        else if (_debugMode == DebugMode.StepOut)
        {
            // Continue until we exit
            return;
        }
    }
    
    public void OnFunctionExit(string functionName)
    {
        _currentDepth--;
        
        if (_debugMode == DebugMode.StepOut)
        {
            if (string.IsNullOrEmpty(_stepOutFunction) || _stepOutFunction == functionName)
            {
                _debugMode = DebugMode.Paused;
                _stepOutFunction = null;
                // Note: line number will be set by the next OnStatement call
            }
        }
    }
    
    public bool HasBreakpoint(int line, string? file = null)
    {
        return _debuggerService.IsBreakpoint(line, file ?? "main.malda");
    }
    
    public bool CheckBreakpointCondition(int line, string? file, Func<bool> evaluator)
    {
        var breakpoint = _debuggerService.Breakpoints
            .FirstOrDefault(b => b.Line == line && b.FilePath == (file ?? "main.malda") && b.Enabled);
        
        if (breakpoint == null)
            return true; // No breakpoint, condition is "met"
        
        if (string.IsNullOrEmpty(breakpoint.Condition))
            return true; // No condition, always break
        
        // Condition evaluation would need to be done by the interpreter
        // For now, we'll assume the evaluator function handles it
        try
        {
            return evaluator();
        }
        catch
        {
            // If condition evaluation fails, break anyway
            return true;
        }
    }
    
    public DebugMode GetDebugMode()
    {
        return _debugMode;
    }
    
    public void SetDebugMode(DebugMode mode)
    {
        _debugMode = mode;
        
        if (mode == DebugMode.StepOver)
        {
            _stepOverDepth = _currentDepth;
        }
        else if (mode == DebugMode.StepOut)
        {
            // Will be set when we enter the function we want to step out of
        }
        else if (mode == DebugMode.Continue)
        {
            _stepOverDepth = 0;
            _stepOutFunction = null;
        }
    }
    
    public void SetStepOutFunction(string functionName)
    {
        _stepOutFunction = functionName;
    }
    
    public void UpdateDebugInfo(Interpreter.Interpreter interpreter)
    {
        if (interpreter == null) return;
        
        var callStack = interpreter.GetCallStack();
        var variables = interpreter.GetVariables();
        
        // Convert call stack to IDE models
        var frames = callStack.Select(f => new Models.CallStackFrame
        {
            FunctionName = f.FunctionName,
            ClassName = f.ClassName,
            Line = f.Line,
            File = f.File
        }).ToList();
        
        _debuggerService.UpdateCallStack(frames);
        
        // Convert variables to object dictionary
        var varDict = variables.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
        _debuggerService.UpdateVariables(varDict);
    }
}