// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Lightweight 5-field cron matcher for gateway in-process scheduling.
/// Supports *, lists, ranges, and */step on minute/hour/day-of-month/month/day-of-week.
/// </summary>
public sealed class CronExpression
{
    private readonly CronField _minute;
    private readonly CronField _hour;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;

    private CronExpression(CronField minute, CronField hour, CronField dayOfMonth, CronField month, CronField dayOfWeek)
    {
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    public static bool TryParse(string expr, out CronExpression expression)
    {
        expression = null!;
        if (string.IsNullOrWhiteSpace(expr))
            return false;

        var parts = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return false;

        try
        {
            expression = new CronExpression(
                CronField.Parse(parts[0], 0, 59),
                CronField.Parse(parts[1], 0, 23),
                CronField.Parse(parts[2], 1, 31),
                CronField.Parse(parts[3], 1, 12),
                CronField.ParseDayOfWeek(parts[4]));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsDue(DateTime localNow) =>
        _minute.Matches(localNow.Minute)
        && _hour.Matches(localNow.Hour)
        && _dayOfMonth.Matches(localNow.Day)
        && _month.Matches(localNow.Month)
        && _dayOfWeek.Matches(ToCronDayOfWeek(localNow.DayOfWeek));

    private static int ToCronDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => 0,
        DayOfWeek.Monday => 1,
        DayOfWeek.Tuesday => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Thursday => 4,
        DayOfWeek.Friday => 5,
        DayOfWeek.Saturday => 6,
        _ => 0
    };
}

internal sealed class CronField
{
    private readonly HashSet<int>? _values;
    private readonly int? _step;
    private readonly bool _any;

    private CronField(bool any, HashSet<int>? values, int? step)
    {
        _any = any;
        _values = values;
        _step = step;
    }

    public static CronField Parse(string field, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new FormatException("Empty cron field");

        field = field.Trim();
        if (field == "*")
            return new CronField(true, null, null);

        if (field.StartsWith("*/", StringComparison.Ordinal))
        {
            if (!int.TryParse(field.AsSpan(2), out var step) || step <= 0)
                throw new FormatException("Invalid cron step");
            return new CronField(false, null, step);
        }

        var values = new HashSet<int>();
        foreach (var token in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var step = 1;
            var rangePart = token;
            if (token.Contains('/'))
            {
                var slash = token.Split('/', 2);
                rangePart = slash[0];
                if (!int.TryParse(slash[1], out step) || step <= 0)
                    throw new FormatException("Invalid cron step");
            }

            if (rangePart.Contains('-'))
            {
                var bounds = rangePart.Split('-', 2);
                if (!int.TryParse(bounds[0], out var start) || !int.TryParse(bounds[1], out var end))
                    throw new FormatException("Invalid cron range");
                if (start > end)
                    (start, end) = (end, start);
                for (var value = start; value <= end; value += step)
                {
                    if (value < min || value > max)
                        throw new FormatException("Cron value out of range");
                    values.Add(value);
                }
            }
            else
            {
                if (!int.TryParse(rangePart, out var value))
                    throw new FormatException("Invalid cron value");
                if (value < min || value > max)
                    throw new FormatException("Cron value out of range");
                values.Add(value);
            }
        }

        if (values.Count == 0)
            throw new FormatException("Empty cron field values");
        return new CronField(false, values, null);
    }

    public static CronField ParseDayOfWeek(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new FormatException("Empty day-of-week field");

        field = field.Trim();
        if (field == "*")
            return new CronField(true, null, null);

        if (field == "1-5")
            return new CronField(false, new HashSet<int> { 1, 2, 3, 4, 5 }, null);

        var values = new HashSet<int>();
        foreach (var token in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Contains('-'))
            {
                var bounds = token.Split('-', 2);
                if (!int.TryParse(bounds[0], out var start) || !int.TryParse(bounds[1], out var end))
                    throw new FormatException("Invalid day-of-week range");
                if (start > end)
                    (start, end) = (end, start);
                for (var value = start; value <= end; value++)
                {
                    if (value < 0 || value > 7)
                        throw new FormatException("Day-of-week out of range");
                    values.Add(value == 7 ? 0 : value);
                }
            }
            else
            {
                if (!int.TryParse(token, out var value))
                    throw new FormatException("Invalid day-of-week value");
                if (value < 0 || value > 7)
                    throw new FormatException("Day-of-week out of range");
                values.Add(value == 7 ? 0 : value);
            }
        }

        return new CronField(false, values, null);
    }

    public bool Matches(int value)
    {
        if (_any)
            return true;
        if (_step.HasValue)
            return value % _step.Value == 0;
        return _values!.Contains(value);
    }
}
