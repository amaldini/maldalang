// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

/// <summary>
/// Optional-pack builtin names recognized by the transpiler after moving out of core <see cref="MaldaLang.BuiltIns.BuiltInRegistry"/>.
/// Codegen lives in <see cref="OptionalPack.OptionalPackTranspilerEmit"/> plugins (string emit only; no pack assembly refs).
/// </summary>
internal static class OptionalPackTranspilerBuiltIns
{
    public static bool IsName(string name)
    {
        return IsTimeseriesName(name) || IsTradingName(name);
    }

    public static bool IsTimeseriesName(string name)
    {
        return name is "sma" or "ema" or "rsi" or "atr" or "adx" or "bbands" or "highest" or "lowest" or "stddev" or "stochastic";
    }

    public static bool IsTradingName(string name)
    {
        return name is "createIndicatorEngine" or "applyIndicatorEngineState" or "createBacktestTelemetryEngine" or "collectBacktestPendingIntents"
            or "prepareBacktestCsvFiles" or "parseBacktestCsvBars";
    }
}
