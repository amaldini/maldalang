// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global <c>nn</c> module. No flat aliases: <c>nn.relu</c>, not <c>relu</c> for the new names.
/// <c>math.relu</c> / <c>sigmoid</c> / <c>tanh</c> / <c>mse</c> remain.
/// </summary>
public sealed class NnInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.NnMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        NnStdLib.Call(methodName, args);
}
