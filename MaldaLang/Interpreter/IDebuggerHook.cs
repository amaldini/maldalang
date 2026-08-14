// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

/// <summary>
/// Interface for debugger hooks that allow the interpreter to communicate with the debugger
/// </summary>
public interface IDebuggerHook
{
    /// <summary>
    /// Called before executing a statement at the given line
    /// </summary>
    /// <param name="line">1-based line number of the statement</param>
    /// <param name="file">File path (optional)</param>
    /// <returns>True if execution should continue, false if it should pause</returns>
    bool OnStatement(int line, string? file = null);
    
    /// <summary>
    /// Called when execution should pause (breakpoint hit, step, etc.)
    /// </summary>
    /// <param name="line">1-based current line number</param>
    /// <param name="file">File path (optional)</param>
    void OnPause(int line, string? file = null);
    
    /// <summary>
    /// Called when a function is entered
    /// </summary>
    /// <param name="functionName">Name of the function</param>
    /// <param name="className">Name of the class if it's a method (optional)</param>
    /// <param name="line">Line number where function is defined</param>
    void OnFunctionEnter(string functionName, string? className, int line);
    
    /// <summary>
    /// Called when a function is exited
    /// </summary>
    /// <param name="functionName">Name of the function</param>
    void OnFunctionExit(string functionName);
    
    /// <summary>
    /// Checks if there's a breakpoint at the given line
    /// </summary>
    /// <param name="line">1-based line number</param>
    /// <param name="file">File path (optional)</param>
    /// <returns>True if there's a breakpoint, false otherwise</returns>
    bool HasBreakpoint(int line, string? file = null);
    
    /// <summary>
    /// Checks if a breakpoint condition is met
    /// </summary>
    /// <param name="line">1-based line number</param>
    /// <param name="file">File path (optional)</param>
    /// <param name="evaluator">Function to evaluate the condition expression</param>
    /// <returns>True if condition is met (or no condition), false otherwise</returns>
    bool CheckBreakpointCondition(int line, string? file, Func<bool> evaluator);

    /// <summary>
    /// Waits while execution is paused. Continue / step / stop release the gate.
    /// Must not busy-wait. Cancellation (interpret token or <c>Stop</c>) completes the wait.
    /// </summary>
    Task WaitIfPausedAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Gets the current debug mode (step over, step into, continue, etc.)
    /// </summary>
    DebugMode GetDebugMode();
    
    /// <summary>
    /// Sets the debug mode
    /// </summary>
    void SetDebugMode(DebugMode mode);
}

public enum DebugMode
{
    Continue,      // Continue execution until next breakpoint
    StepOver,      // Step over current statement
    StepInto,      // Step into function calls
    StepOut,       // Step out of current function
    Paused         // Execution is paused
}