// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class DesktopEditorCommandPolicyTests
{
    [Fact]
    public void F5_WhenPaused_Continues()
    {
        var command = DesktopEditorCommandPolicy.ResolveF5(new DesktopEditorCommandContext(
            IsDebugRunning: true,
            IsPaused: true,
            IsRunRunning: false));

        Assert.Equal(DesktopEditorCommand.Continue, command);
    }

    [Fact]
    public void F5_WhenIdle_StartsDebugging()
    {
        var command = DesktopEditorCommandPolicy.ResolveF5(new DesktopEditorCommandContext(
            IsDebugRunning: false,
            IsPaused: false,
            IsRunRunning: false));

        Assert.Equal(DesktopEditorCommand.StartDebugging, command);
    }

    [Fact]
    public void F5_WhenDebugRunningNotPaused_IsNoOp()
    {
        var command = DesktopEditorCommandPolicy.ResolveF5(new DesktopEditorCommandContext(
            IsDebugRunning: true,
            IsPaused: false,
            IsRunRunning: false));

        Assert.Equal(DesktopEditorCommand.None, command);
    }

    [Fact]
    public void CtrlF5_WhenIdle_RunsWithoutDebugging()
    {
        var command = DesktopEditorCommandPolicy.ResolveCtrlF5(new DesktopEditorCommandContext(
            IsDebugRunning: false,
            IsPaused: false,
            IsRunRunning: false));

        Assert.Equal(DesktopEditorCommand.RunWithoutDebugging, command);
    }

    [Fact]
    public void F9_AlwaysTogglesBreakpoint()
    {
        var command = DesktopEditorCommandPolicy.ResolveF9(new DesktopEditorCommandContext(
            IsDebugRunning: true,
            IsPaused: true,
            IsRunRunning: false));

        Assert.Equal(DesktopEditorCommand.ToggleBreakpoint, command);
    }
}
