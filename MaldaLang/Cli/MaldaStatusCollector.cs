// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Collects MALDA home / gateway / cron status for <c>malda status</c>.
/// </summary>
public static class MaldaStatusCollector
{
    public sealed class MemoryStats
    {
        public string Path { get; init; } = "";
        public bool Initialized { get; init; }
        public int? Nodes { get; init; }
        public int? Edges { get; init; }
        public string? LastReflectAt { get; init; }
        public string? Error { get; init; }
    }

    public sealed class CronJobStatus
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Scope { get; init; } = "";
        public string Message { get; init; } = "";
        public string Cron { get; init; } = "";
    }

    public sealed class Snapshot
    {
        public string MaldaHome { get; init; } = "";
        public string ConfigPath { get; init; } = "";
        public bool ConfigExists { get; init; }
        public bool OpenRouterApiKeySet { get; init; }
        public string? DefaultModel { get; init; }
        public string? DefaultBackend { get; init; }
        public string? LocalLlamaModelPath { get; init; }
        public bool TelegramConfigured { get; init; }
        public string SkillsDirectory { get; init; } = "";
        public int SkillCount { get; init; }
        public string GatewayState { get; init; } = "stopped";
        public int? GatewayPid { get; init; }
        public bool StaleGatewayPidRemoved { get; init; }
        public MemoryStats Memory { get; init; } = new();
        public IReadOnlyList<CronJobStatus> CronJobs { get; init; } = Array.Empty<CronJobStatus>();
    }

    public static Snapshot Collect(
        string maldaHome,
        string? telegramBotToken,
        string memoryPath,
        Func<MemoryStats>? loadMemoryStats = null,
        Func<IReadOnlyList<CronJobStatus>>? loadCronJobs = null)
    {
        var configPath = Path.Combine(maldaHome, "config.json");
        var configExists = File.Exists(configPath);
        var apiKeySet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));
        string? backend = null;
        string? localModelPath = null;
        string? model = null;

        if (!apiKeySet && configExists)
        {
            try
            {
                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("providers", out var prov) &&
                    prov.ValueKind == JsonValueKind.Object &&
                    prov.TryGetProperty("openrouter", out var or) &&
                    or.ValueKind == JsonValueKind.Object &&
                    or.TryGetProperty("apiKey", out var key))
                {
                    var k = key.GetString();
                    apiKeySet = !string.IsNullOrWhiteSpace(k);
                }
                if (root.TryGetProperty("agents", out var ag2) &&
                    ag2.ValueKind == JsonValueKind.Object &&
                    ag2.TryGetProperty("defaults", out var def2) &&
                    def2.ValueKind == JsonValueKind.Object &&
                    def2.TryGetProperty("backend", out var b))
                {
                    backend = b.GetString();
                }
                if (root.TryGetProperty("providers", out var prov2) &&
                    prov2.ValueKind == JsonValueKind.Object &&
                    prov2.TryGetProperty("local_llama", out var ll) &&
                    ll.ValueKind == JsonValueKind.Object &&
                    ll.TryGetProperty("modelPath", out var mp))
                {
                    localModelPath = mp.GetString();
                }
                if (root.TryGetProperty("agents", out var ag) &&
                    ag.ValueKind == JsonValueKind.Object &&
                    ag.TryGetProperty("defaults", out var def) &&
                    def.ValueKind == JsonValueKind.Object &&
                    def.TryGetProperty("model", out var m))
                {
                    model = m.GetString();
                }
            }
            catch
            {
            }
        }

        var skillsDir = Path.Combine(maldaHome, "skills");
        var skillCount = Directory.Exists(skillsDir)
            ? Directory.GetFiles(skillsDir, "*.malda").Length
            : 0;

        var pidPath = GatewayRunner.GetGatewayPidPath(maldaHome);
        string gatewayState;
        int? gatewayPid = null;
        var stalePidRemoved = false;
        if (GatewayRunner.IsGatewayProcessRunning(pidPath))
        {
            GatewayRunner.TryReadGatewayPid(pidPath, out var pid);
            gatewayPid = pid;
            gatewayState = "running";
        }
        else
        {
            gatewayState = "stopped";
            if (File.Exists(pidPath))
            {
                stalePidRemoved = true;
                GatewayRunner.RemoveGatewayPid(pidPath);
            }
        }

        var memory = loadMemoryStats?.Invoke() ?? new MemoryStats { Path = memoryPath, Initialized = false };
        var cronJobs = loadCronJobs?.Invoke() ?? Array.Empty<CronJobStatus>();

        return new Snapshot
        {
            MaldaHome = maldaHome,
            ConfigPath = configPath,
            ConfigExists = configExists,
            OpenRouterApiKeySet = apiKeySet,
            DefaultModel = model,
            DefaultBackend = backend,
            LocalLlamaModelPath = localModelPath,
            TelegramConfigured = !string.IsNullOrWhiteSpace(telegramBotToken),
            SkillsDirectory = skillsDir,
            SkillCount = skillCount,
            GatewayState = gatewayState,
            GatewayPid = gatewayPid,
            StaleGatewayPidRemoved = stalePidRemoved,
            Memory = memory,
            CronJobs = cronJobs
        };
    }
}

public readonly struct GatewayStopResult
{
    public bool Stopped { get; init; }
    public int Pid { get; init; }
    public string Message { get; init; }

    public GatewayStopResult(bool stopped, int pid, string message)
    {
        Stopped = stopped;
        Pid = pid;
        Message = message;
    }
}
