// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

public enum DesktopEditorCommand
{
    None,
    RunWithoutDebugging,
    StartDebugging,
    Continue,
    ToggleBreakpoint
}

public readonly record struct DesktopEditorCommandContext(
    bool IsDebugRunning,
    bool IsPaused,
    bool IsRunRunning);

/// <summary>
/// Resolves overlapping IDE keys. WPF InputBindings stay in <c>MainWindow</c>.
/// </summary>
public static class DesktopEditorCommandPolicy
{
    public static DesktopEditorCommand ResolveF5(DesktopEditorCommandContext context)
    {
        if (context.IsPaused)
        {
            return DesktopEditorCommand.Continue;
        }

        if (context.IsDebugRunning || context.IsRunRunning)
        {
            return DesktopEditorCommand.None;
        }

        return DesktopEditorCommand.StartDebugging;
    }

    public static DesktopEditorCommand ResolveCtrlF5(DesktopEditorCommandContext context)
    {
        if (context.IsDebugRunning || context.IsRunRunning)
        {
            return DesktopEditorCommand.None;
        }

        return DesktopEditorCommand.RunWithoutDebugging;
    }

    public static DesktopEditorCommand ResolveF9(DesktopEditorCommandContext context)
    {
        _ = context;
        return DesktopEditorCommand.ToggleBreakpoint;
    }
}
