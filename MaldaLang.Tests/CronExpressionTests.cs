// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.Cli;
using System;

namespace MaldaLang.Tests;

public class CronExpressionTests
{
    [Theory]
    [InlineData("*/15 * * * *", 2026, 6, 6, 10, 0, true)]
    [InlineData("*/15 * * * *", 2026, 6, 6, 10, 7, false)]
    [InlineData("0 9,18 * * *", 2026, 6, 6, 9, 0, true)]
    [InlineData("0 9,18 * * *", 2026, 6, 6, 12, 0, false)]
    [InlineData("0 9 1 * *", 2026, 6, 1, 9, 0, true)]
    [InlineData("0 9 1 * *", 2026, 6, 2, 9, 0, false)]
    [InlineData("0 9 * * 1,3,5", 2026, 6, 1, 9, 0, true)]
    [InlineData("0 9 * * 1,3,5", 2026, 6, 2, 9, 0, false)]
    public void CronExpression_IsDue_matches_advanced_patterns(
        string cron, int year, int month, int day, int hour, int minute, bool expected)
    {
        Assert.True(CronExpression.TryParse(cron, out var expression));
        var when = new DateTime(year, month, day, hour, minute, 0);
        Assert.Equal(expected, expression.IsDue(when));
    }

    [Fact]
    public void GatewayRunner_IsCronDueNow_uses_advanced_parser()
    {
        var when = new DateTime(2026, 6, 6, 10, 30, 0);
        Assert.True(GatewayRunner.IsCronDueNow("*/30 * * * *", when));
        Assert.False(GatewayRunner.IsCronDueNow("*/30 * * * *", when.AddMinutes(5)));
    }
}
