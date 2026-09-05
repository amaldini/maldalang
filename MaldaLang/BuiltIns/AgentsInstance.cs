// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global <c>agents</c> module: define role specs and bind them to a relation graph.
/// No flat <c>agents()</c> alias.
/// </summary>
public sealed class AgentsInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.AgentsMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        methodName switch
        {
            "define" => AgentsStdLib.Define(args, interpreter),
            "team" => AgentsStdLib.Team(args, interpreter),
            _ => throw new Exception($"Unknown agents method: {methodName}")
        };
}
