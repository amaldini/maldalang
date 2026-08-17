// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using MaldaLang.Interpreter.Debug;
using MaldaLang.DesktopIDE.Models;
using System.Linq;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Thin UI adapter: stepping and the pause gate live on <see cref="DebugSession"/>.
/// </summary>
public class DebuggerHook : IDebuggerHook, IHasDebugSession
{
    private readonly DebuggerService _debuggerService;

    public DebugSession Session { get; }

    public event Action<int, string?>? OnPaused;

    public DebuggerHook(DebuggerService debuggerService)
    {
        _debuggerService = debuggerService;
        Session = new DebugSession();
        Session.Paused += (line, file) =>
        {
            _debuggerService.SetCurrentLine(line, file ?? "main.malda");
            _debuggerService.Pause();
            OnPaused?.Invoke(line, file);
        };
        SyncBreakpoints();
        _debuggerService.BreakpointsChanged += SyncBreakpoints;
    }

    public bool OnStatement(int line, string? file = null) => Session.OnStatement(line, file);

    public void OnPause(int line, string? file = null) => Session.OnPause(line, file);

    public void SetInterpreter(Interpreter.Interpreter? interpreter)
    {
        if (interpreter != null)
            Session.Bind(interpreter);
    }

    public void OnFunctionEnter(string functionName, string? className, int line)
        => Session.OnFunctionEnter(functionName, className, line);

    public void OnFunctionExit(string functionName) => Session.OnFunctionExit(functionName);

    public bool HasBreakpoint(int line, string? file = null) => Session.HasBreakpoint(line, file);

    public bool CheckBreakpointCondition(int line, string? file, Func<bool> evaluator)
        => Session.CheckBreakpointCondition(line, file, evaluator);

    public DebugMode GetDebugMode() => Session.GetDebugMode();

    public void SetDebugMode(DebugMode mode) => Session.SetDebugMode(mode);

    public void SetStepOutFunction(string functionName)
    {
        // Depth-based StepOut on DebugSession does not need the function name.
    }

    public void Stop() => Session.Stop();

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
        => Session.WaitIfPausedAsync(cancellationToken);

    public void UpdateDebugInfo(Interpreter.Interpreter interpreter)
    {
        if (interpreter == null)
        {
            return;
        }

        Session.Bind(interpreter);
        var frames = Session.GetStackFrames()
            .Select(frame => new CallStackFrame
            {
                FunctionName = frame.FunctionName,
                ClassName = frame.ClassName,
                Line = frame.Line,
                File = frame.File
            })
            .ToList();

        _debuggerService.UpdateCallStack(frames);
        _debuggerService.UpdateVariables(new Dictionary<string, object>());
    }

    private void SyncBreakpoints()
    {
        Session.ClearBreakpoints();
        foreach (var bp in _debuggerService.Breakpoints.Where(b => b.Enabled))
        {
            var file = string.IsNullOrEmpty(bp.FilePath) ? "main.malda" : bp.FilePath;
            Session.SetBreakpoint(file, bp.Line, bp.Condition);
        }
    }
}
