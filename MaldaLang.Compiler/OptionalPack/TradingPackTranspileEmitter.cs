// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST.Expressions;

namespace MaldaLang.Compiler.OptionalPack;

internal sealed class TradingPackTranspileEmitter : IOptionalPackTranspileEmitter
{
    public static TradingPackTranspileEmitter Instance { get; } = new();

    public bool CanEmit(string name) => OptionalPackTranspilerBuiltIns.IsTradingName(name);

    public void Emit(OptionalPackEmitContext ctx, string name, List<Expression> arguments)
    {
        switch (name)
        {
            case "collectBacktestPendingIntents":
                ctx.Output.Append("MaldaLang.Trading.Core.BacktestPendingIntentCollectorBuiltIn.CollectTranspiled(");
                OptionalPackEmitHelpers.EmitCommaSeparatedExpressions(ctx.Output, ctx, arguments);
                ctx.Output.Append(')');
                break;
            case "createIndicatorEngine":
                OptionalPackEmitHelpers.EmitTradingCreateCall(
                    ctx.Output,
                    ctx,
                    "MaldaLang.Trading.Core.TradingIndicatorEngineBuiltIn",
                    "Create",
                    arguments);
                break;
            case "applyIndicatorEngineState":
                ctx.Output.Append("MaldaLang.Trading.Core.TradingIndicatorEngineBuiltIn.ApplyStateTranspiled(");
                OptionalPackEmitHelpers.EmitCommaSeparatedExpressions(ctx.Output, ctx, arguments);
                ctx.Output.Append(')');
                break;
            case "createBacktestTelemetryEngine":
                OptionalPackEmitHelpers.EmitTradingCreateCall(
                    ctx.Output,
                    ctx,
                    "MaldaLang.Trading.Core.BacktestTelemetryEngineBuiltIn",
                    "Create",
                    arguments);
                break;
            case "prepareBacktestCsvFiles":
                OptionalPackEmitHelpers.EmitTradingCreateCall(
                    ctx.Output,
                    ctx,
                    "MaldaLang.Trading.Core.BacktestCsvBuiltIn",
                    "Prepare",
                    arguments);
                break;
            case "parseBacktestCsvBars":
                OptionalPackEmitHelpers.EmitTradingCreateCall(
                    ctx.Output,
                    ctx,
                    "MaldaLang.Trading.Core.BacktestCsvBuiltIn",
                    "Parse",
                    arguments);
                break;
            default:
                throw new InvalidOperationException($"Trading pack emitter does not handle '{name}'.");
        }
    }
}
