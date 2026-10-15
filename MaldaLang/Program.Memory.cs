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
    static string GetDefaultMemoryPath()
    {
        return Path.Combine(GetMaldaHomePath(), "memory", "assistant");
    }

    static GraphMemoryInstance CreateCliMemory(Interpreter.Interpreter interpreter)
    {
        var memory = new GraphMemoryInstance();
        memory.SetInterpreter(interpreter);
        var embedMode = (SystemEnvironment.GetEnvironmentVariable("MALDA_MEMORY_EMBED") ?? "hash").Trim().ToLowerInvariant();
        var embedFn = CreateCliEmbeddingFunction(interpreter, embedMode);
        var initArgs = new List<RuntimeValue> { RuntimeValue.Integer(384), RuntimeValue.String("single") };
        if (embedFn != null)
            initArgs.Add(RuntimeValue.Function(embedFn));
        memory.CallMethod("initialize", initArgs, interpreter);
        return memory;
    }

    static FunctionValue? CreateCliEmbeddingFunction(Interpreter.Interpreter interpreter, string embedMode)
    {
        var source = embedMode == "bow"
            ? "function __memoryEmbed(text) { return embedBagOfWords(text, 384); }"
            : "function __memoryEmbed(text) { return embedHash(text, 384); }";
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();
            if (statements.Count > 0 && statements[0] is FunctionDeclaration fn)
                return new FunctionValue(fn, interpreter._globals, false, null);
        }
        catch
        {
        }
        return null;
    }

    static bool MemoryArtifactsExist(string basePath)
    {
        var canonicalBase = basePath;
        var graphPath = $"{canonicalBase}.graph.json";
        if (File.Exists(graphPath))
            return true;
        var dir = Path.GetDirectoryName(canonicalBase);
        if (string.IsNullOrEmpty(dir))
            dir = ".";
        return File.Exists(Path.Combine(dir, ".graph.json"));
    }

    static void MemoryCommand(string[] args)
    {
        if (args.Length < 2)
        {
            ShowMemoryHelp();
            SystemEnvironment.Exit(1);
            return;
        }

        var sub = args[1].Trim().ToLowerInvariant();
        var json = false;
        string? path = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json")
                json = true;
            else if (args[i] == "--path" && i + 1 < args.Length)
            {
                path = args[i + 1];
                i++;
            }
        }

        path ??= GetDefaultMemoryPath();
        var interpreter = new Interpreter.Interpreter();
        var memory = CreateCliMemory(interpreter);
        if (MemoryArtifactsExist(path))
        {
            memory.CallMethod("load", new List<RuntimeValue> { RuntimeValue.String(path) }, interpreter);
        }

        try
        {
            switch (sub)
            {
                case "stats":
                {
                    var stats = memory.CallMethod("stats", new List<RuntimeValue>(), interpreter);
                    if (json)
                        Console.WriteLine(RuntimeValueToJsonString(stats));
                    else
                        Console.WriteLine(GraphMemoryInstance.FormatMemoryLine(stats));
                    return;
                }
                case "validate":
                {
                    var report = memory.CallMethod("validate", new List<RuntimeValue>(), interpreter);
                    Console.WriteLine(json ? RuntimeValueToJsonString(report) : GraphMemoryInstance.FormatMemoryLine(report));
                    var okVal = report.Type == MaldaLang.Interpreter.ValueType.Object
                        && report.AsObject() is MaldaLang.BuiltIns.JsonObject reportObj
                        ? reportObj.Get("ok", null)
                        : null;
                    var ok = okVal != null && okVal.Type == MaldaLang.Interpreter.ValueType.Boolean && okVal.AsBoolean();
                    if (!ok)
                        SystemEnvironment.Exit(1);
                    return;
                }
                case "reindex":
                {
                    var dir = ".";
                    var pattern = "**/*.md";
                    var scope = "global";
                    for (var i = 2; i < args.Length; i++)
                    {
                        if (args[i] == "--dir" && i + 1 < args.Length) { dir = args[++i]; continue; }
                        if (args[i] == "--pattern" && i + 1 < args.Length) { pattern = args[++i]; continue; }
                        if (args[i] == "--scope" && i + 1 < args.Length) { scope = args[++i]; continue; }
                    }
                    var options = new JsonObject();
                    options.Set("changedOnly", RuntimeValue.Boolean(true));
                    options.Set("scope", RuntimeValue.String(scope));
                    var result = memory.CallMethod("reindexDocuments", new List<RuntimeValue>
                    {
                        RuntimeValue.String(pattern),
                        RuntimeValue.String(dir),
                        RuntimeValue.Object(options)
                    }, interpreter);
                    memory.CallMethod("save", new List<RuntimeValue> { RuntimeValue.String(path) }, interpreter);
                    Console.WriteLine(json ? RuntimeValueToJsonString(result) : GraphMemoryInstance.FormatMemoryLine(result));
                    return;
                }
                case "prune":
                {
                    var options = new JsonObject();
                    for (var i = 2; i < args.Length; i++)
                    {
                        if (args[i] == "--type" && i + 1 < args.Length) { options.Set("type", RuntimeValue.String(args[++i])); continue; }
                        if (args[i] == "--scope" && i + 1 < args.Length) { options.Set("scope", RuntimeValue.String(args[++i])); continue; }
                        if (args[i] == "--older-than-days" && i + 1 < args.Length && int.TryParse(args[++i], out var days)) { options.Set("olderThanDays", RuntimeValue.Integer(days)); continue; }
                        if (args[i] == "--consolidated") { options.Set("consolidated", RuntimeValue.Boolean(true)); continue; }
                    }
                    var removed = memory.CallMethod("prune", new List<RuntimeValue> { RuntimeValue.Object(options) }, interpreter);
                    memory.CallMethod("save", new List<RuntimeValue> { RuntimeValue.String(path) }, interpreter);
                    Console.WriteLine(json ? RuntimeValueToJsonString(removed) : removed.AsInteger().ToString());
                    return;
                }
                case "reflect":
                {
                    var options = new JsonObject();
                    for (var i = 2; i < args.Length; i++)
                    {
                        if (args[i] == "--scope" && i + 1 < args.Length) { options.Set("scope", RuntimeValue.String(args[++i])); continue; }
                        if (args[i] == "--dry-run") { options.Set("dryRun", RuntimeValue.Boolean(true)); continue; }
                        if (args[i] == "--min-confidence" && i + 1 < args.Length &&
                            double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var conf))
                        {
                            options.Set("minConfidence", RuntimeValue.Float(conf));
                            continue;
                        }
                    }
                    var result = memory.CallMethod("reflect", new List<RuntimeValue> { RuntimeValue.Object(options) }, interpreter);
                    memory.CallMethod("save", new List<RuntimeValue> { RuntimeValue.String(path) }, interpreter);
                    Console.WriteLine(json ? RuntimeValueToJsonString(result) : GraphMemoryInstance.FormatMemoryLine(result));
                    return;
                }
                case "export-bundle":
                {
                    var outputPath = path;
                    for (var i = 2; i < args.Length; i++)
                    {
                        if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
                        {
                            outputPath = args[++i];
                            break;
                        }
                    }
                    var bundlePath = memory.CallMethod("exportBundle", new List<RuntimeValue> { RuntimeValue.String(outputPath) }, interpreter);
                    Console.WriteLine(json ? RuntimeValueToJsonString(bundlePath) : bundlePath.AsString());
                    return;
                }
                case "watch":
                {
                    var pattern = "**/*.md";
                    var dir = ".";
                    for (var i = 2; i < args.Length; i++)
                    {
                        if (args[i] == "--dir" && i + 1 < args.Length) { dir = args[++i]; continue; }
                        if (args[i] == "--pattern" && i + 1 < args.Length) { pattern = args[++i]; continue; }
                    }
                    var service = new KbWatchService(dir, pattern, () =>
                    {
                        var options = new JsonObject();
                        options.Set("changedOnly", RuntimeValue.Boolean(true));
                        options.Set("scope", RuntimeValue.String("global"));
                        memory.CallMethod("reindexDocuments", new List<RuntimeValue>
                        {
                            RuntimeValue.String(pattern),
                            RuntimeValue.String(dir),
                            RuntimeValue.Object(options)
                        }, interpreter);
                        memory.CallMethod("save", new List<RuntimeValue> { RuntimeValue.String(path) }, interpreter);
                        Console.WriteLine("kb reindexed");
                    });
                    service.Start();
                    Console.WriteLine($"Watching {dir} ({pattern}). Press Ctrl+C to stop.");
                    var done = new ManualResetEvent(false);
                    Console.CancelKeyPress += (_, e) =>
                    {
                        e.Cancel = true;
                        done.Set();
                    };
                    done.WaitOne();
                    service.Dispose();
                    return;
                }
                case "download-rerank":
                {
                    RunCrossEncoderDownload(GetMaldaHomePath());
                    return;
                }
                default:
                    Console.Error.WriteLine($"Unknown memory subcommand: {sub}");
                    ShowMemoryHelp();
                    SystemEnvironment.Exit(1);
                    return;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            SystemEnvironment.Exit(1);
        }
    }

    static string RuntimeValueToJsonString(RuntimeValue value)
    {
        object? Convert(RuntimeValue v)
        {
            switch (v.Type)
            {
                case MaldaLang.Interpreter.ValueType.String: return v.AsString();
                case MaldaLang.Interpreter.ValueType.Integer: return v.AsInteger();
                case MaldaLang.Interpreter.ValueType.Float: return v.AsFloat();
                case MaldaLang.Interpreter.ValueType.Boolean: return v.AsBoolean();
                case MaldaLang.Interpreter.ValueType.Null: return null;
                case MaldaLang.Interpreter.ValueType.Array:
                    return v.AsArray().Select(Convert).ToList();
                case MaldaLang.Interpreter.ValueType.Object:
                    if (v.AsObject() is JsonObject jsonObj)
                    {
                        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                        foreach (var key in jsonObj.GetAllKeys())
                            map[key] = Convert(jsonObj.Get(key));
                        return map;
                    }
                    if (v.AsObject() is DictionaryInstance dict)
                    {
                        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                        foreach (var kvp in dict.Entries)
                            map[kvp.Key] = Convert(kvp.Value);
                        return map;
                    }
                    return v.AsObject()?.ToString();
                default:
                    return v.ToString();
            }
        }
        return JsonSerializer.Serialize(Convert(value));
    }
}
