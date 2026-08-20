// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Interpreter.Debug;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Builds inspect trees from <see cref="DebugSession"/>. Call after each pause:
/// <see cref="DebugSession.GetFrameScopes"/> resets handles.
/// </summary>
public static class DebugInspectSnapshotBuilder
{
    public static IReadOnlyList<DebugInspectNode> BuildScopes(DebugSession session, int frameId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var scopes = session.GetFrameScopes(frameId);
        return scopes.Select(scope => new DebugInspectNode
        {
            Display = scope.Name,
            Name = scope.Name,
            VariablesReference = scope.VariablesReference,
            IsScope = true,
            FrameId = frameId
        }).ToList();
    }

    public static IReadOnlyList<DebugInspectNode> Expand(DebugSession session, int variablesReference, int frameId = 1)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (variablesReference <= 0)
        {
            return Array.Empty<DebugInspectNode>();
        }

        return session.GetVariables(variablesReference)
            .Select(variable => FromVariable(variable, frameId))
            .ToList();
    }

    public static string FormatFrame(InterpreterCallStackFrame frame)
    {
        return string.IsNullOrEmpty(frame.ClassName)
            ? $"{frame.FunctionName} ({frame.File}:{frame.Line})"
            : $"{frame.ClassName}.{frame.FunctionName} ({frame.File}:{frame.Line})";
    }

    public static string FormatFrame(CallStackFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return string.IsNullOrEmpty(frame.ClassName)
            ? $"{frame.FunctionName} ({frame.File}:{frame.Line})"
            : $"{frame.ClassName}.{frame.FunctionName} ({frame.File}:{frame.Line})";
    }

    public static DebugInspectNode FromVariable(DebugVariable variable, int frameId)
    {
        ArgumentNullException.ThrowIfNull(variable);
        return new DebugInspectNode
        {
            Display = FormatVariable(variable),
            Name = variable.Name,
            Value = variable.Value,
            Type = variable.Type,
            VariablesReference = variable.VariablesReference,
            IsScope = false,
            FrameId = frameId
        };
    }

    public static DebugInspectNode FromWatchError(string expression, string message, int frameId)
    {
        var preview = $"<{message}>";
        return new DebugInspectNode
        {
            Display = $"{expression} = {preview}",
            Name = expression,
            Value = preview,
            FrameId = frameId
        };
    }

    private static string FormatVariable(DebugVariable variable)
    {
        if (string.IsNullOrEmpty(variable.Type))
        {
            return $"{variable.Name} = {variable.Value}";
        }

        return $"{variable.Name} = {variable.Value} : {variable.Type}";
    }
}
