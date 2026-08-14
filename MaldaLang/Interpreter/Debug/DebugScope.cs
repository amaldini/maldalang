// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter.Debug;

/// <summary>
/// DAP-shaped scope. <see cref="VariablesReference"/> is 0 when the scope has no children.
/// Names are <c>Locals</c>, <c>Closure</c>, <c>Globals</c>, or <c>This</c>.
/// </summary>
public sealed class DebugScope
{
    public string Name { get; init; } = "";
    public int VariablesReference { get; init; }
}

/// <summary>
/// DAP-shaped variable. <see cref="VariablesReference"/> is 0 for leaves;
/// a positive handle is expanded via <see cref="DebugSession.GetVariables"/>.
/// </summary>
public sealed class DebugVariable
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Type { get; init; } = "";
    public int VariablesReference { get; init; }
}
