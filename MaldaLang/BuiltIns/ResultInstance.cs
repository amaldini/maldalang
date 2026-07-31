// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global <c>result</c> module: Ok/Err helpers, map, unwrapOr (Phase 4.4).
/// </summary>
public sealed class ResultInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.ResultMethodNames;

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        methodName switch
        {
            "ok" => VariantStdLib.ResultOk(args),
            "err" => VariantStdLib.ResultErr(args),
            "map" => VariantStdLib.ResultMap(args, interpreter),
            "unwrapOr" => VariantStdLib.ResultUnwrapOr(args),
            "isOk" => VariantStdLib.ResultIsOk(args),
            "isErr" => VariantStdLib.ResultIsErr(args),
            _ => throw new Exception($"Unknown result method: {methodName}")
        };
}
