// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST.Expressions;

namespace MaldaLang.Compiler.OptionalPack;

internal sealed class TimeseriesPackTranspileEmitter : IOptionalPackTranspileEmitter
{
    public static TimeseriesPackTranspileEmitter Instance { get; } = new();

    public bool CanEmit(string name) => OptionalPackTranspilerBuiltIns.IsTimeseriesName(name);

    public void Emit(OptionalPackEmitContext ctx, string name, List<Expression> arguments)
    {
        OptionalPackEmitHelpers.EmitTimeseriesCallBuiltIn(ctx.Output, ctx, name, arguments);
    }
}
