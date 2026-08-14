// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter.Debug;

/// <summary>
/// IDE <c>DebuggerHook</c> wrappers expose the shared <see cref="DebugSession"/>
/// so the interpreter can bind inspect/evaluate to live paused state.
/// </summary>
public interface IHasDebugSession
{
    DebugSession Session { get; }
}
