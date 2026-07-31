// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using MaldaLang.Compiler.OptionalPack;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

public class OptionalPackTranspileEmitTests
{
    [Theory]
    [InlineData("sma", "MaldaLang.Timeseries.TimeseriesFunctions.CallBuiltIn(\"sma\"")]
    [InlineData("stochastic", "MaldaLang.Timeseries.TimeseriesFunctions.CallBuiltIn(\"stochastic\"")]
    [InlineData("createIndicatorEngine", "MaldaLang.Trading.Core.TradingIndicatorEngineBuiltIn.Create")]
    [InlineData("applyIndicatorEngineState", "MaldaLang.Trading.Core.TradingIndicatorEngineBuiltIn.ApplyStateTranspiled")]
    [InlineData("prepareBacktestCsvFiles", "MaldaLang.Trading.Core.BacktestCsvBuiltIn.Prepare")]
    public void TryEmit_KnownOptionalPackBuiltin_EmitsExpectedTarget(string name, string expectedFragment)
    {
        var output = new StringBuilder();
        var arguments = new List<Expression> { new LiteralExpression(1) };
        var emitted = OptionalPackTranspilerEmit.TryEmit(
            new OptionalPackEmitContext(output, expr => output.Append("1")),
            name,
            arguments);

        Assert.True(emitted);
        Assert.Contains(expectedFragment, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryEmit_UnknownBuiltin_ReturnsFalse()
    {
        var output = new StringBuilder();
        var emitted = OptionalPackTranspilerEmit.TryEmit(
            new OptionalPackEmitContext(output, _ => { }),
            "print",
            []);

        Assert.False(emitted);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void CSharpTranspiler_DoesNotEmbedOptionalPackTypeNames()
    {
        var transpilerPath = PlanningPaths.ResolveRepoFile("MaldaLang.Compiler", "CSharpTranspiler.cs");
        var source = File.ReadAllText(transpilerPath);

        Assert.DoesNotContain("MaldaLang.Timeseries", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MaldaLang.Trading.Core", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalPackRegistry_CoversAllOptionalPackEmitters()
    {
        foreach (var name in new[] { "sma", "ema", "rsi", "atr", "adx", "bbands", "highest", "lowest", "stddev", "stochastic" })
        {
            Assert.True(TimeseriesPackTranspileEmitter.Instance.CanEmit(name), name);
        }

        foreach (var name in new[]
                 {
                     "createIndicatorEngine", "applyIndicatorEngineState", "createBacktestTelemetryEngine",
                     "collectBacktestPendingIntents", "prepareBacktestCsvFiles", "parseBacktestCsvBars"
                 })
        {
            Assert.True(TradingPackTranspileEmitter.Instance.CanEmit(name), name);
        }
    }
}
