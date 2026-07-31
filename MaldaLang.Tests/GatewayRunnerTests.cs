// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.Cli;
using System;
using System.Diagnostics;
using System.IO;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class GatewayRunnerTests : TestBase
{
    [Theory]
    [InlineData("0 9 * * *", 9, 0, true)]
    [InlineData("0 9 * * *", 9, 1, false)]
    public void IsCronDueNow_matches_minute_and_hour(string cron, int hour, int minute, bool expectDue)
    {
        var when = new DateTime(2026, 6, 6, hour, minute, 0);
        Assert.Equal(expectDue, GatewayRunner.IsCronDueNow(cron, when));
    }

    [Fact]
    public void IsCronDueNow_weekly_matches_configured_weekday()
    {
        var monday = NextWeekday(DayOfWeek.Monday, 18, 30);
        var tuesday = NextWeekday(DayOfWeek.Tuesday, 18, 30);
        Assert.True(GatewayRunner.IsCronDueNow("30 18 * * 1", monday));
        Assert.False(GatewayRunner.IsCronDueNow("30 18 * * 1", tuesday));
    }

    [Fact]
    public void TryStopGateway_whenNotRunning_returnsStoppedFalse()
    {
        var tempDir = CreateTempDirectory("gateway_stop_");
        try
        {
            var pidPath = GatewayRunner.GetGatewayPidPath(tempDir);
            var result = GatewayRunner.TryStopGateway(pidPath);

            Assert.False(result.Stopped);
            Assert.Equal(0, result.Pid);
            Assert.Contains("not running", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(pidPath));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TryStopGateway_whenPidIsStale_processAlreadyExited()
    {
        var tempDir = CreateTempDirectory("gateway_stale_");
        try
        {
            var pidPath = GatewayRunner.GetGatewayPidPath(tempDir);
            File.WriteAllText(pidPath, "999999");

            var result = GatewayRunner.TryStopGateway(pidPath);

            Assert.True(result.Stopped);
            Assert.Equal(999999, result.Pid);
            Assert.Contains("stale", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(pidPath));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GatewayPid_roundtrip_detects_current_process()
    {
        var tempDir = CreateTempDirectory("gateway_pid_");
        try
        {
            var pidPath = GatewayRunner.GetGatewayPidPath(tempDir);
            Assert.False(GatewayRunner.IsGatewayProcessRunning(pidPath));

            GatewayRunner.WriteGatewayPid(pidPath);
            Assert.True(File.Exists(pidPath));
            Assert.True(GatewayRunner.TryReadGatewayPid(pidPath, out var pid));
            Assert.Equal(Process.GetCurrentProcess().Id, pid);
            Assert.True(GatewayRunner.IsGatewayProcessRunning(pidPath));

            GatewayRunner.RemoveGatewayPid(pidPath);
            Assert.False(File.Exists(pidPath));
            Assert.False(GatewayRunner.IsGatewayProcessRunning(pidPath));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    static DateTime NextWeekday(DayOfWeek day, int hour, int minute)
    {
        var d = DateTime.Today;
        while (d.DayOfWeek != day)
            d = d.AddDays(1);
        return new DateTime(d.Year, d.Month, d.Day, hour, minute, 0);
    }
}
