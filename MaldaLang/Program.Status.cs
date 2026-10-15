// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using System;
using System.Text;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using MaldaLang.Testing;
using MaldaLang.Scaffolding;
using MaldaLang.Deployment;
using MaldaLang.UIHost;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Cli;
using MaldaLang.Eval;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.BuiltIns;
using MaldaLang.DebugAdapter;
using SystemEnvironment = System.Environment;
using PackageManager = MaldaLang.PackageManager.PackageManager;

namespace MaldaLang;

partial class Program
{
    static void StatusCommand(string[] args)
    {
        var json = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json")
                json = true;
        }

        var dir = GetMaldaHomePath();
        var memoryPath = GetDefaultMemoryPath();
        var snapshot = MaldaStatusCollector.Collect(
            dir,
            GetTelegramBotToken(),
            memoryPath,
            () => LoadStatusMemoryStats(memoryPath),
            LoadStatusCronJobs);

        if (json)
        {
            var payload = new Dictionary<string, object?>
            {
                ["maldaHome"] = snapshot.MaldaHome,
                ["config"] = new Dictionary<string, object?>
                {
                    ["path"] = snapshot.ConfigPath,
                    ["exists"] = snapshot.ConfigExists,
                    ["openRouterApiKeySet"] = snapshot.OpenRouterApiKeySet,
                    ["defaultModel"] = snapshot.DefaultModel,
                    ["defaultBackend"] = snapshot.DefaultBackend,
                    ["localLlamaModelPath"] = snapshot.LocalLlamaModelPath
                },
                ["channels"] = new Dictionary<string, object?>
                {
                    ["telegramConfigured"] = snapshot.TelegramConfigured
                },
                ["skills"] = new Dictionary<string, object?>
                {
                    ["directory"] = snapshot.SkillsDirectory,
                    ["count"] = snapshot.SkillCount
                },
                ["gateway"] = new Dictionary<string, object?>
                {
                    ["state"] = snapshot.GatewayState,
                    ["pid"] = snapshot.GatewayPid,
                    ["stalePidRemoved"] = snapshot.StaleGatewayPidRemoved
                },
                ["memory"] = new Dictionary<string, object?>
                {
                    ["path"] = snapshot.Memory.Path,
                    ["initialized"] = snapshot.Memory.Initialized,
                    ["nodes"] = snapshot.Memory.Nodes,
                    ["edges"] = snapshot.Memory.Edges,
                    ["lastReflectAt"] = snapshot.Memory.LastReflectAt,
                    ["error"] = snapshot.Memory.Error
                },
                ["cronJobs"] = snapshot.CronJobs.Select(j => new Dictionary<string, object?>
                {
                    ["id"] = j.Id,
                    ["name"] = j.Name,
                    ["scope"] = j.Scope,
                    ["message"] = j.Message,
                    ["cron"] = j.Cron
                }).ToList()
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        Console.WriteLine($"MALDA home: {snapshot.MaldaHome}");
        Console.WriteLine($"Config: {snapshot.ConfigPath}");
        Console.WriteLine($"  Exists: {snapshot.ConfigExists}");
        Console.WriteLine($"  OpenRouter API key set: {snapshot.OpenRouterApiKeySet}");
        if (!string.IsNullOrWhiteSpace(snapshot.DefaultModel))
            Console.WriteLine($"  Default model: {snapshot.DefaultModel}");
        if (!string.IsNullOrWhiteSpace(snapshot.DefaultBackend))
            Console.WriteLine($"  Default backend: {snapshot.DefaultBackend}");
        if (!string.IsNullOrWhiteSpace(snapshot.LocalLlamaModelPath))
            Console.WriteLine($"  Local llama model: {snapshot.LocalLlamaModelPath}");

        Console.WriteLine("Channels:");
        Console.WriteLine($"  Telegram configured: {snapshot.TelegramConfigured}");
        Console.WriteLine($"Skills: {snapshot.SkillCount} in {snapshot.SkillsDirectory}");

        if (snapshot.GatewayState == "running")
            Console.WriteLine($"Gateway: running (pid {snapshot.GatewayPid})");
        else
        {
            Console.WriteLine("Gateway: stopped");
            if (snapshot.StaleGatewayPidRemoved)
                Console.WriteLine("  Note: stale gateway.pid removed");
        }

        if (snapshot.Memory.Initialized)
        {
            Console.WriteLine($"Memory: {snapshot.Memory.Path}");
            if (snapshot.Memory.Nodes.HasValue)
                Console.WriteLine($"  Nodes: {snapshot.Memory.Nodes.Value}");
            if (snapshot.Memory.Edges.HasValue)
                Console.WriteLine($"  Edges: {snapshot.Memory.Edges.Value}");
            if (!string.IsNullOrWhiteSpace(snapshot.Memory.LastReflectAt))
                Console.WriteLine($"  Last reflect: {snapshot.Memory.LastReflectAt}");
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.Memory.Error))
            Console.WriteLine($"Memory: present at {snapshot.Memory.Path} (stats unavailable: {snapshot.Memory.Error})");
        else
            Console.WriteLine($"Memory: not initialized ({snapshot.Memory.Path})");

        Console.WriteLine($"Cron jobs: {snapshot.CronJobs.Count}");
        foreach (var job in snapshot.CronJobs)
            Console.WriteLine($"  {job.Id}  {job.Name}  scope={job.Scope}  \"{job.Message}\"  {job.Cron}");
    }

    static MaldaStatusCollector.MemoryStats LoadStatusMemoryStats(string memoryPath)
    {
        if (!MemoryArtifactsExist(memoryPath))
            return new MaldaStatusCollector.MemoryStats { Path = memoryPath, Initialized = false };

        try
        {
            var interpreter = new Interpreter.Interpreter();
            var memory = CreateCliMemory(interpreter);
            memory.CallMethod("load", new List<RuntimeValue> { RuntimeValue.String(memoryPath) }, interpreter);
            var stats = memory.CallMethod("stats", new List<RuntimeValue>(), interpreter);
            if (stats.Type == Interpreter.ValueType.Object && stats.AsObject() is JsonObject statsObj)
            {
                var nodes = statsObj.Get("nodes", null);
                var edges = statsObj.Get("edges", null);
                var lastReflect = statsObj.Get("lastReflectAt", null);
                return new MaldaStatusCollector.MemoryStats
                {
                    Path = memoryPath,
                    Initialized = true,
                    Nodes = nodes != null && nodes.Type == Interpreter.ValueType.Integer ? nodes.AsInteger() : null,
                    Edges = edges != null && edges.Type == Interpreter.ValueType.Integer ? edges.AsInteger() : null,
                    LastReflectAt = lastReflect != null && lastReflect.Type == Interpreter.ValueType.String
                        ? lastReflect.AsString()
                        : null
                };
            }
        }
        catch (Exception ex)
        {
            return new MaldaStatusCollector.MemoryStats
            {
                Path = memoryPath,
                Initialized = false,
                Error = ex.Message
            };
        }

        return new MaldaStatusCollector.MemoryStats { Path = memoryPath, Initialized = true };
    }

    static IReadOnlyList<MaldaStatusCollector.CronJobStatus> LoadStatusCronJobs()
    {
        var cronPath = GetCronFilePath();
        if (!File.Exists(cronPath))
            return Array.Empty<MaldaStatusCollector.CronJobStatus>();

        var file = LoadCronFile();
        return file.Jobs.Select(job => new MaldaStatusCollector.CronJobStatus
        {
            Id = job.Id,
            Name = job.Name,
            Scope = ResolveCronJobScope(job),
            Message = job.Message,
            Cron = job.Cron
        }).ToList();
    }
}
