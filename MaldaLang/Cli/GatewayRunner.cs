// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

/// <summary>
/// Long-running gateway helpers: pid file, cron due checks, and in-process scheduler.
/// </summary>
public static class GatewayRunner
{
    public static string GetGatewayPidPath(string maldaHome) => Path.Combine(maldaHome, "gateway.pid");

    public static bool TryReadGatewayPid(string pidPath, out int pid)
    {
        pid = 0;
        if (!File.Exists(pidPath))
            return false;
        try
        {
            var text = File.ReadAllText(pidPath).Trim();
            return int.TryParse(text, out pid) && pid > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsGatewayProcessRunning(string pidPath)
    {
        if (!TryReadGatewayPid(pidPath, out var pid))
            return false;
        try
        {
            var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteGatewayPid(string pidPath)
    {
        var dir = Path.GetDirectoryName(pidPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(pidPath, Process.GetCurrentProcess().Id.ToString());
    }

    public static void RemoveGatewayPid(string pidPath)
    {
        try
        {
            if (File.Exists(pidPath))
                File.Delete(pidPath);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Stops a running gateway process identified by <paramref name="pidPath"/>.
    /// Removes the pid file when the gateway is not running or after a successful stop.
    /// </summary>
    public static GatewayStopResult TryStopGateway(string pidPath)
    {
        if (!TryReadGatewayPid(pidPath, out var pid))
        {
            RemoveGatewayPid(pidPath);
            return new GatewayStopResult(false, 0, "Gateway is not running.");
        }

        try
        {
            var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(5000);
            }
        }
        catch (ArgumentException)
        {
            RemoveGatewayPid(pidPath);
            return new GatewayStopResult(true, pid, $"Gateway already stopped (removed stale pid {pid}).");
        }
        catch (Exception ex)
        {
            RemoveGatewayPid(pidPath);
            return new GatewayStopResult(false, pid, $"Failed to stop gateway (pid {pid}): {ex.Message}");
        }

        RemoveGatewayPid(pidPath);
        return new GatewayStopResult(true, pid, $"Gateway stopped (pid {pid}).");
    }

    public static bool IsCronDueNow(string cronExpr, DateTime localNow)
    {
        if (CronExpression.TryParse(cronExpr, out var expression))
            return expression.IsDue(localNow);

        if (CronSchedule.TryParse(cronExpr, out var schedule) && schedule.IsValid)
            return CronSchedule.IsDue(schedule, localNow);

        return false;
    }

    public static IDisposable StartCronScheduler(
        IReadOnlyList<GatewayCronJob> jobs,
        Action<GatewayCronJob> onJobDue,
        TimeSpan pollInterval)
    {
        if (jobs.Count == 0)
            return new NoopDisposable();

        var lastFired = new Dictionary<string, string>(StringComparer.Ordinal);
        var cts = new CancellationTokenSource();
        var timer = new Timer(_ =>
        {
            try
            {
                var now = DateTime.Now;
                var slot = now.ToString("yyyy-MM-dd HH:mm");
                foreach (var job in jobs)
                {
                    if (!IsCronDueNow(job.Cron, now))
                        continue;
                    var key = job.Id + ":" + slot;
                    lock (lastFired)
                    {
                        if (lastFired.ContainsKey(key))
                            continue;
                        lastFired[key] = slot;
                    }
                    onJobDue(job);
                }
            }
            catch
            {
            }
        }, null, pollInterval, pollInterval);

        return new CronSchedulerHandle(cts, timer);
    }

    private sealed class CronSchedulerHandle : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Timer _timer;

        public CronSchedulerHandle(CancellationTokenSource cts, Timer timer)
        {
            _cts = cts;
            _timer = timer;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _timer.Dispose();
            _cts.Dispose();
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

public sealed class GatewayCronJob
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Message { get; set; } = "";
    public string Cron { get; set; } = "";
    public string Scope { get; set; } = "";
}

public readonly struct CronSchedule
{
    public bool IsValid { get; init; }
    public string ScheduleType { get; init; }
    public string[]? Days { get; init; }
    public TimeSpan StartTime { get; init; }

    public static bool IsDue(CronSchedule schedule, DateTime localNow)
    {
        if (schedule.StartTime.Hours != localNow.Hour || schedule.StartTime.Minutes != localNow.Minute)
            return false;

        if (schedule.ScheduleType == "DAILY")
            return true;

        if (schedule.ScheduleType == "WEEKLY" && schedule.Days != null)
        {
            var day = localNow.DayOfWeek switch
            {
                DayOfWeek.Sunday => "SUN",
                DayOfWeek.Monday => "MON",
                DayOfWeek.Tuesday => "TUE",
                DayOfWeek.Wednesday => "WED",
                DayOfWeek.Thursday => "THU",
                DayOfWeek.Friday => "FRI",
                DayOfWeek.Saturday => "SAT",
                _ => ""
            };
            return Array.Exists(schedule.Days, d => string.Equals(d, day, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    public static bool TryParse(string expr, out CronSchedule schedule)
    {
        schedule = new CronSchedule { IsValid = false, ScheduleType = "", Days = null, StartTime = TimeSpan.Zero };
        if (string.IsNullOrWhiteSpace(expr))
            return false;

        var parts = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return false;

        var minutePart = parts[0];
        var hourPart = parts[1];
        var dayOfMonth = parts[2];
        var month = parts[3];
        var dayOfWeek = parts[4];

        if (dayOfMonth != "*" || month != "*")
            return false;

        if (!int.TryParse(minutePart, out var minute) || !int.TryParse(hourPart, out var hour))
            return false;
        if (minute < 0 || minute > 59 || hour < 0 || hour > 23)
            return false;

        schedule = schedule with { StartTime = new TimeSpan(hour, minute, 0) };

        if (dayOfWeek == "*")
        {
            schedule = schedule with { ScheduleType = "DAILY", Days = null, IsValid = true };
            return true;
        }

        if (dayOfWeek == "1-5")
        {
            schedule = schedule with
            {
                ScheduleType = "WEEKLY",
                Days = new[] { "MON", "TUE", "WED", "THU", "FRI" },
                IsValid = true
            };
            return true;
        }

        if (int.TryParse(dayOfWeek, out var dow))
        {
            string? day = dow switch
            {
                0 => "SUN",
                1 => "MON",
                2 => "TUE",
                3 => "WED",
                4 => "THU",
                5 => "FRI",
                6 => "SAT",
                _ => null
            };
            if (day != null)
            {
                schedule = schedule with { ScheduleType = "WEEKLY", Days = new[] { day }, IsValid = true };
                return true;
            }
        }

        return false;
    }
}
