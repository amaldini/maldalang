// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global <c>nn</c> module. No flat aliases.
/// <c>math.sigmoid</c> / <c>tanh</c> / <c>softmax</c> remain; <c>relu</c>, <c>mse</c>, and
/// <c>crossEntropyFromLogits</c> are only on <c>nn</c>.
/// </summary>
public sealed class NnInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.NnMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        NnStdLib.Call(methodName, args);
}
