// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Global <c>option</c> module: Some/None helpers, map, andThen, unwrapOr (Phase 4.4).
/// </summary>
public sealed class OptionInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.OptionMethodNames;

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        methodName switch
        {
            "some" => VariantStdLib.OptionSome(args),
            "none" => VariantStdLib.OptionNone(args),
            "map" => VariantStdLib.OptionMap(args, interpreter),
            "andThen" => VariantStdLib.OptionAndThen(args, interpreter),
            "unwrapOr" => VariantStdLib.OptionUnwrapOr(args),
            "isSome" => VariantStdLib.OptionIsSome(args),
            "isNone" => VariantStdLib.OptionIsNone(args),
            _ => throw new Exception($"Unknown option method: {methodName}")
        };
}
