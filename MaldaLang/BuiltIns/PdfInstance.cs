// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global pdf object: pdf.extractText(path, password?).
/// </summary>
public sealed class PdfInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.PdfMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        BuiltInFunctions.CallBuiltIn(StdLibNamespaces.ResolvePdfBuiltInName(methodName), args, interpreter);

    public override Task<RuntimeValue> CallMethodAsync(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        BuiltInFunctions.CallBuiltInAsync(StdLibNamespaces.ResolvePdfBuiltInName(methodName), args, interpreter);
}
