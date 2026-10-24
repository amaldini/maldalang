// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global io object: io.readFile, io.print, etc.
/// </summary>
public sealed class IoInstance : StdLibModuleInstance
{
    private static readonly IReadOnlySet<string> Exported =
        StdLibNamespaces.IoMethodNames.Concat(StdLibNamespaces.IoStoreMethodNames).ToHashSet(StringComparer.Ordinal);

    protected override IReadOnlySet<string> ExportedMethods => Exported;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        StdLibNamespaces.IoStoreMethodNames.Contains(methodName)
            ? IoStore.Call(methodName, args)
            : base.CallMethod(methodName, args, interpreter);
}
