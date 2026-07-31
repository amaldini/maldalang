// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime;

using MaldaLang.Interpreter;

/// <summary>
/// Shared interpreter for transpiled executables. Built-in agents and GraphMemory
/// need an <see cref="Interpreter"/> for memory tools and embeddings; the interpreter
/// path sets this explicitly, but transpiled code does not unless wired here.
/// </summary>
public static class TranspiledBuiltinRuntime
{
    private static readonly object Lock = new();
    private static Interpreter? _interpreter;

    public static void Initialize()
    {
        _ = GetOrCreateInterpreter();
    }

    public static Interpreter GetOrCreateInterpreter()
    {
        if (_interpreter != null)
            return _interpreter;

        lock (Lock)
        {
            if (_interpreter == null)
                _interpreter = new Interpreter(currentFile: "transpiled");
            return _interpreter;
        }
    }

    public static void SetInterpreter(Interpreter interpreter)
    {
        lock (Lock)
        {
            _interpreter = interpreter;
        }
    }
}
