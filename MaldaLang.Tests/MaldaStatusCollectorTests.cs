// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.Cli;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class MaldaStatusCollectorTests : TestBase
{
    [Fact]
    public void Collect_reportsGatewayStoppedAndSkillCount()
    {
        var tempDir = CreateTempDirectory("status_collect_");
        try
        {
            var skillsDir = Path.Combine(tempDir, "skills");
            Directory.CreateDirectory(skillsDir);
            File.WriteAllText(Path.Combine(skillsDir, "one.malda"), "var tools = [];");
            File.WriteAllText(Path.Combine(skillsDir, "two.malda"), "var tools = [];");

            var snapshot = MaldaStatusCollector.Collect(
                tempDir,
                telegramBotToken: null,
                memoryPath: Path.Combine(tempDir, "memory", "assistant"),
                loadMemoryStats: () => new MaldaStatusCollector.MemoryStats
                {
                    Path = Path.Combine(tempDir, "memory", "assistant"),
                    Initialized = false
                },
                loadCronJobs: () => Array.Empty<MaldaStatusCollector.CronJobStatus>());

            Assert.Equal(tempDir, snapshot.MaldaHome);
            Assert.Equal(2, snapshot.SkillCount);
            Assert.Equal("stopped", snapshot.GatewayState);
            Assert.Null(snapshot.GatewayPid);
            Assert.False(snapshot.TelegramConfigured);
            Assert.Empty(snapshot.CronJobs);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Collect_serializesToJson_withExpectedTopLevelKeys()
    {
        var tempDir = CreateTempDirectory("status_json_");
        try
        {
            var snapshot = MaldaStatusCollector.Collect(
                tempDir,
                telegramBotToken: "token",
                memoryPath: Path.Combine(tempDir, "memory", "assistant"),
                loadMemoryStats: () => new MaldaStatusCollector.MemoryStats
                {
                    Path = Path.Combine(tempDir, "memory", "assistant"),
                    Initialized = true,
                    Nodes = 3,
                    Edges = 2
                },
                loadCronJobs: () => new[]
                {
                    new MaldaStatusCollector.CronJobStatus
                    {
                        Id = "job1",
                        Name = "daily",
                        Scope = "cron:daily",
                        Message = "hello",
                        Cron = "0 9 * * *"
                    }
                });

            var payload = new
            {
                maldaHome = snapshot.MaldaHome,
                gateway = new { state = snapshot.GatewayState, pid = snapshot.GatewayPid },
                memory = new
                {
                    initialized = snapshot.Memory.Initialized,
                    nodes = snapshot.Memory.Nodes,
                    edges = snapshot.Memory.Edges
                },
                cronJobs = snapshot.CronJobs.Select(j => new { j.Id, j.Scope }).ToList()
            };

            var json = JsonSerializer.Serialize(payload);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("maldaHome", out _));
            Assert.True(root.TryGetProperty("gateway", out var gateway));
            Assert.Equal("stopped", gateway.GetProperty("state").GetString());
            Assert.True(root.TryGetProperty("memory", out var memory));
            Assert.True(memory.GetProperty("initialized").GetBoolean());
            Assert.Equal(3, memory.GetProperty("nodes").GetInt32());
            Assert.True(root.TryGetProperty("cronJobs", out var jobs));
            Assert.Equal(1, jobs.GetArrayLength());
            Assert.Equal("cron:daily", jobs[0].GetProperty("Scope").GetString());
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
