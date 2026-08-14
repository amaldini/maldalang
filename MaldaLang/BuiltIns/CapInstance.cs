// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global <c>cap</c> module: mint and consume unforgeable file capabilities (L6). No flat alias.
/// </summary>
public sealed class CapInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.CapMethodNames;

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        methodName switch
        {
            "fileRead" => CapStdLib.FileRead(args),
            "fileWrite" => CapStdLib.FileWrite(args),
            "dirList" => CapStdLib.DirList(args),
            "is" => CapStdLib.Is(args),
            "confine" => CapStdLib.Confine(args),
            "read" => CapStdLib.Read(args, interpreter),
            "write" => CapStdLib.Write(args, interpreter),
            "list" => CapStdLib.List(args, interpreter),
            _ => throw new Exception($"Unknown cap method: {methodName}")
        };
}
