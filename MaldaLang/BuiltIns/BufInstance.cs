// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>Global <c>buf</c> module (fixed-size numeric buffers, binary data). No flat aliases.</summary>
public sealed class BufInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.BufMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        BufStdLib.Call(methodName, args);
}
