// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global doc object: doc.extractText(path) for Office Open XML (.docx).
/// </summary>
public sealed class DocInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.DocMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        BuiltInFunctions.CallBuiltIn(StdLibNamespaces.ResolveDocBuiltInName(methodName), args, interpreter);

    public override Task<RuntimeValue> CallMethodAsync(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        BuiltInFunctions.CallBuiltInAsync(StdLibNamespaces.ResolveDocBuiltInName(methodName), args, interpreter);
}
