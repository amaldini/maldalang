// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST.Expressions;

namespace MaldaLang.Compiler.OptionalPack;

/// <summary>
/// Dispatches optional-pack builtin transpile emit to vertical pack plugins (string codegen only; no pack assembly refs).
/// </summary>
internal static class OptionalPackTranspilerEmit
{
    private static readonly IOptionalPackTranspileEmitter[] Emitters =
    [
        TimeseriesPackTranspileEmitter.Instance,
        TradingPackTranspileEmitter.Instance
    ];

    public static bool TryEmit(OptionalPackEmitContext ctx, string name, List<Expression> arguments)
    {
        foreach (var emitter in Emitters)
        {
            if (!emitter.CanEmit(name))
            {
                continue;
            }

            emitter.Emit(ctx, name, arguments);
            return true;
        }

        return false;
    }
}
