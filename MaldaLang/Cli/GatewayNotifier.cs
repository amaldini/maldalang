// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MaldaLang.Channels;

/// <summary>
/// Gateway alert log, crash markers, and optional Telegram notifications.
/// </summary>
public static class GatewayNotifier
{
    public static string GetAlertsLogPath(string maldaHome) => Path.Combine(maldaHome, "gateway-alerts.log");

    public static string GetCrashMarkerPath(string maldaHome) => Path.Combine(maldaHome, "gateway-crash.json");

    public static void AppendAlert(string maldaHome, string title, string detail)
    {
        if (string.IsNullOrWhiteSpace(maldaHome))
            return;

        try
        {
            var path = GetAlertsLogPath(maldaHome);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var line = $"{DateTime.UtcNow:O} [{title}] {detail}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
        }
    }

    public static void RecordCrash(string maldaHome, string reason)
    {
        if (string.IsNullOrWhiteSpace(maldaHome))
            return;

        try
        {
            var path = GetCrashMarkerPath(maldaHome);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var payload = new GatewayCrashMarker
            {
                AtUtc = DateTime.UtcNow.ToString("O"),
                Reason = reason ?? "unknown"
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }

        AppendAlert(maldaHome, "crash", reason ?? "unknown");
    }

    public static bool TryReadCrashMarker(string maldaHome, out GatewayCrashMarker marker)
    {
        marker = new GatewayCrashMarker();
        var path = GetCrashMarkerPath(maldaHome);
        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<GatewayCrashMarker>(json);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.AtUtc))
                return false;
            marker = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ClearCrashMarker(string maldaHome)
    {
        try
        {
            var path = GetCrashMarkerPath(maldaHome);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    public static async Task NotifyAsync(
        string maldaHome,
        string title,
        string detail,
        string? telegramBotToken,
        string? notifyChatId)
    {
        AppendAlert(maldaHome, title, detail);
        if (string.IsNullOrWhiteSpace(telegramBotToken) || string.IsNullOrWhiteSpace(notifyChatId))
            return;

        try
        {
            var channel = new TelegramChannel(telegramBotToken);
            await channel.SendMessageAsync($"MALDA gateway: {title}\n{detail}", notifyChatId).ConfigureAwait(false);
            channel.Stop();
        }
        catch
        {
        }
    }

    public static void NotifyFireAndForget(
        string maldaHome,
        string title,
        string detail,
        string? telegramBotToken,
        string? notifyChatId)
    {
        _ = NotifyAsync(maldaHome, title, detail, telegramBotToken, notifyChatId);
    }
}

public sealed class GatewayCrashMarker
{
    public string AtUtc { get; set; } = "";
    public string Reason { get; set; } = "";
}
