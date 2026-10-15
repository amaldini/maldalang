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
    static void OnboardCommand(string[] args)
    {
        var downloadRerank = false;
        var downloadLocalLlama = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--download-rerank")
                downloadRerank = true;
            else if (args[i] == "--download-local-llama")
                downloadLocalLlama = true;
        }

        var dir = GetMaldaHomePath();
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            Console.WriteLine($"Created {dir}");
        }
        var configPath = Path.Combine(dir, "config.json");
        var skillsDir = Path.Combine(dir, "skills");
        if (!Directory.Exists(skillsDir))
        {
            Directory.CreateDirectory(skillsDir);
            Console.WriteLine($"Created {skillsDir}");
        }
        InstallDefaultSkillTemplate(skillsDir);
        var memoryDir = Path.Combine(dir, "memory");
        if (!Directory.Exists(memoryDir))
        {
            Directory.CreateDirectory(memoryDir);
            Console.WriteLine($"Created {memoryDir}");
        }
        var createdConfig = false;
        if (!File.Exists(configPath))
        {
            var template = @"{
  ""providers"": {
    ""openrouter"": {
      ""apiKey"": """",
      ""model"": ""deepseek/deepseek-v4-flash""
    },
    ""local_llama"": {
      ""modelPath"": """",
      ""contextLength"": 4096,
      ""gpuLayers"": 0,
      ""temperature"": 0.7,
      ""maxTokens"": 2000
    }
  },
  ""channels"": {
    ""telegram"": {
      ""botToken"": """",
      ""notifyChatId"": """"
    }
  },
  ""agents"": {
    ""defaults"": {
      ""backend"": ""openrouter"",
      ""model"": ""deepseek/deepseek-v4-flash""
    },
    ""memory"": {
      ""embed"": ""hash"",
      ""rerank"": true,
      ""rerankMode"": ""onnx"",
      ""rerankModelPath"": ""~/.malda/models/cross-encoder"",
      ""rerankTopK"": 10,
      ""reflectEnabled"": false,
      ""kbDir"": """",
      ""kbPattern"": ""**/*.md""
    }
  },
  ""tools"": {
    ""web"": {
      ""search"": {
        ""apiKey"": """"
      }
    }
  }
}";
            File.WriteAllText(configPath, template);
            Console.WriteLine($"Created {configPath}");
            createdConfig = true;
        }

        if (downloadRerank)
            RunCrossEncoderDownload(dir);

        if (downloadLocalLlama)
        {
            Console.WriteLine("Downloading default local GGUF model (first use may take a few minutes)...");
            DefaultLocalLlm.DownloadDefaultModelAsync(new Progress<(long bytesReceived, long? totalBytes)>(state =>
            {
                if (state.totalBytes.HasValue && state.totalBytes.Value > 0)
                {
                    var pct = (int)Math.Round(100.0 * state.bytesReceived / state.totalBytes.Value);
                    Console.Write($"\rDownloading local model: {pct}%   ");
                }
                else
                {
                    Console.Write($"\rDownloading local model: {state.bytesReceived / (1024 * 1024)} MB   ");
                }
                if (state.totalBytes.HasValue && state.bytesReceived >= state.totalBytes.Value)
                    Console.WriteLine();
            })).GetAwaiter().GetResult();
            var modelPath = DefaultLocalLlm.GetOrDownloadDefaultModelPath();
            Console.WriteLine($"Local model ready at {modelPath}");
            if (createdConfig || string.IsNullOrWhiteSpace(ReadConfigLocalLlamaModelPath(configPath)))
                TryPatchConfigLocalLlamaModelPath(configPath, modelPath);
        }

        PrintOnboardNextSteps(downloadRerank, downloadLocalLlama, CrossEncoderOnnxModels.IsInstalled(CrossEncoderOnnxModels.GetDefaultModelDirectory(dir)));
    }

    static IProgress<(string fileName, long bytesReceived, long? totalBytes)> CreateDownloadProgressReporter()
    {
        return new Progress<(string fileName, long bytesReceived, long? totalBytes)>(state =>
        {
            if (state.totalBytes.HasValue && state.totalBytes.Value > 0)
            {
                var pct = (int)Math.Round(100.0 * state.bytesReceived / state.totalBytes.Value);
                Console.Write($"\rDownloading {state.fileName}: {pct}%   ");
            }
            else
            {
                Console.Write($"\rDownloading {state.fileName}: {state.bytesReceived / (1024 * 1024)} MB   ");
            }
            if (state.totalBytes.HasValue && state.bytesReceived >= state.totalBytes.Value)
                Console.WriteLine();
        });
    }

    static void RunCrossEncoderDownload(string maldaHome)
    {
        Console.WriteLine($"Downloading cross-encoder ONNX model to {CrossEncoderOnnxModels.GetDefaultModelDirectory(maldaHome)} ...");
        CrossEncoderOnnxModels.EnsureDownloadedAsync(CreateDownloadProgressReporter(), maldaHome).GetAwaiter().GetResult();
        Console.WriteLine($"Cross-encoder ready at {CrossEncoderOnnxModels.GetDefaultModelDirectory(maldaHome)}");
    }

    static string? ReadConfigLocalLlamaModelPath(string configPath)
    {
        if (!File.Exists(configPath))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("providers", out var providers) &&
                providers.TryGetProperty("local_llama", out var localLlama) &&
                localLlama.TryGetProperty("modelPath", out var modelPath))
            {
                return modelPath.GetString();
            }
        }
        catch { }
        return null;
    }

    static void TryPatchConfigLocalLlamaModelPath(string configPath, string modelPath)
    {
        if (!File.Exists(configPath))
            return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("providers"))
                    {
                        writer.WritePropertyName("providers");
                        writer.WriteStartObject();
                        foreach (var provider in property.Value.EnumerateObject())
                        {
                            if (provider.NameEquals("local_llama"))
                            {
                                writer.WritePropertyName("local_llama");
                                writer.WriteStartObject();
                                foreach (var field in provider.Value.EnumerateObject())
                                {
                                    if (field.NameEquals("modelPath"))
                                        writer.WriteString("modelPath", modelPath);
                                    else
                                        field.WriteTo(writer);
                                }
                                writer.WriteEndObject();
                            }
                            else
                            {
                                provider.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            File.WriteAllBytes(configPath, stream.ToArray());
            Console.WriteLine($"Updated providers.local_llama.modelPath in {configPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not update config.json: {ex.Message}");
        }
    }

    static void InstallDefaultSkillTemplate(string skillsDir)
    {
        var dest = Path.Combine(skillsDir, "greeting.malda");
        if (File.Exists(dest))
            return;

        var source = GetBundledGreetingSkillPath();
        if (!string.IsNullOrWhiteSpace(source) && File.Exists(source))
        {
            File.Copy(source, dest);
            Console.WriteLine($"Installed skill template {dest}");
            return;
        }

        File.WriteAllText(dest, GetEmbeddedGreetingSkillSource());
        Console.WriteLine($"Installed skill template {dest}");
    }

    static string? GetBundledGreetingSkillPath()
    {
        static string? FindGreetingSkill(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Examples", "Assistant", "skills", "greeting.malda");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        var fromCwd = FindGreetingSkill(Directory.GetCurrentDirectory());
        if (fromCwd != null)
            return fromCwd;

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var fromBase = FindGreetingSkill(baseDir);
            if (fromBase != null)
                return fromBase;
        }

        return null;
    }

    static string GetEmbeddedGreetingSkillSource() =>
        """
        @Tool("greet_user", "Greets someone by name", "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"Name to greet\"}},\"required\":[\"name\"]}")
        function greetUserTool(args) {
            var name = args.name;
            if (name == null || name == "") {
                name = "friend";
            }
            return "Hello, " + name + "! — Greeting skill";
        }

        var tools = ["greet_user"];
        var agentDescription = "Greets users by name using greet_user when asked to say hello.";

        var skillClient = null;
        var apiKey = getEnv("OPENROUTER_API_KEY");
        var config = getMaldaConfig();
        if (config != null && config.providers != null && config.providers.openrouter != null && config.providers.openrouter.apiKey != null && config.providers.openrouter.apiKey != "") {
            apiKey = config.providers.openrouter.apiKey;
        }
        if (apiKey != null && apiKey != "") {
            skillClient = new OpenRouterClient();
        }

        var agent = new Agent(
            "GreetingSkill",
            "specialist",
            "You greet users warmly. Use greet_user with their name when they want a hello.",
            skillClient
        );
        agent.addTool("greet_user");
        """;

    static void PrintOnboardNextSteps(bool downloadedRerank, bool downloadedLocalLlama, bool rerankInstalled)
    {
        Console.WriteLine();
        Console.WriteLine("MALDA onboard — next steps:");
        Console.WriteLine("  1. Set OPENROUTER_API_KEY or providers.openrouter.apiKey in ~/.malda/config.json");
        Console.WriteLine("  2. Optional Telegram: set TELEGRAM_BOT_TOKEN or channels.telegram.botToken; set channels.telegram.notifyChatId for gateway/cron alerts");
        if (!rerankInstalled && !downloadedRerank)
            Console.WriteLine("  3. ONNX rerank: run malda memory download-rerank (or malda onboard --download-rerank)");
        else
            Console.WriteLine("  3. ONNX rerank model is installed under ~/.malda/models/cross-encoder");
        if (!downloadedLocalLlama)
            Console.WriteLine("  4. Local llama embed/backend: malda onboard --download-local-llama or set providers.local_llama.modelPath");
        Console.WriteLine("  5. Run malda doctor, then malda agent (or malda agent -m \"hello\")");
    }
}
