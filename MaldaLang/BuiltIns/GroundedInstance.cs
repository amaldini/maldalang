// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global <c>grounded</c> module: wrap a payload with citations (L5). No flat <c>grounded()</c> alias.
/// </summary>
public sealed class GroundedInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.GroundedMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        methodName switch
        {
            "wrap" => GroundedStdLib.Wrap(args),
            _ => throw new Exception($"Unknown grounded method: {methodName}")
        };
}
