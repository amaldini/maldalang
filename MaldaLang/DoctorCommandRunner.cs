namespace MaldaLang;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Cli;
using MaldaLang.Parser;

internal sealed class DoctorCommandRunner
{
    private readonly string _maldaHomePath;

    public DoctorCommandRunner(string maldaHomePath)
    {
        _maldaHomePath = maldaHomePath;
    }

    public int Run(string[] args, TextWriter output, TextWriter error, string? workingDirectory = null)
    {
        if (args.Any(IsHelpFlag))
        {
            PrintUsage(output);
            return 0;
        }

        var root = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(workingDirectory!);

        var checks = CollectChecks(root);
        WriteReport(output, root, checks);
        return checks.Any(x => x.Status == DoctorStatus.Error) ? 1 : 0;
    }

    internal IReadOnlyList<DoctorCheck> CollectChecks(string workingDirectory)
    {
        var config = LoadConfigSummary();
        var checks = new List<DoctorCheck>
        {
            BuildRuntimeCheck(),
            BuildHomeCheck(config),
            BuildConfigCheck(config),
            BuildProviderCheck(config),
            BuildTelegramCheck(config),
            BuildGatewayCheck(),
            BuildMemoryCheck(),
            BuildOnnxRerankCheck(config, _maldaHomePath),
            BuildSkillsCheck(),
            BuildSearchCheck(config),
            BuildProjectCheck(workingDirectory)
        };

        return checks;
    }

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage: malda doctor");
        output.WriteLine("  Inspect local runtime, MALDA home/config, gateway, memory, ONNX, skills,");
        output.WriteLine("  provider readiness, and scaffold conventions for the current directory.");
        output.WriteLine();
        output.WriteLine("Notes:");
        output.WriteLine("  - doctor only inspects local runtime, config, and filesystem state");
        output.WriteLine("  - doctor does not call provider APIs or detect IDE/LSP setup");
        output.WriteLine("  - use 'malda help' to see related setup and workflow commands");
    }

    private static bool IsHelpFlag(string value)
    {
        return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static DoctorCheck BuildRuntimeCheck()
    {
        var runtime = RuntimeInformation.FrameworkDescription;
        var os = RuntimeInformation.OSDescription.Trim();
        return DoctorCheck.Ok("Runtime", $"{runtime} on {os}");
    }

    private DoctorCheck BuildHomeCheck(DoctorConfigSummary config)
    {
        if (config.HomeExists)
        {
            return DoctorCheck.Ok("MALDA home", _maldaHomePath);
        }

        return DoctorCheck.Warning(
            "MALDA home",
            $"{_maldaHomePath} does not exist yet.",
            "Run 'malda onboard' to create the standard MALDA home and starter config.");
    }

    private DoctorCheck BuildConfigCheck(DoctorConfigSummary config)
    {
        if (!config.ConfigExists)
        {
            return DoctorCheck.Warning(
                "CLI config",
                $"{config.ConfigPath} was not found.",
                "Run 'malda onboard' or create ~/.malda/config.json manually.");
        }

        if (!config.ConfigParsed)
        {
            return DoctorCheck.Error(
                "CLI config",
                $"Could not parse {config.ConfigPath}. {config.ParseError}",
                "Fix the JSON syntax before using provider-backed CLI features.");
        }

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.DefaultBackend))
        {
            details.Add($"backend={config.DefaultBackend}");
        }

        if (!string.IsNullOrWhiteSpace(config.DefaultModel))
        {
            details.Add($"model={config.DefaultModel}");
        }

        return DoctorCheck.Ok(
            "CLI config",
            details.Count == 0
                ? $"{config.ConfigPath} is present and valid."
                : $"{config.ConfigPath} is present and valid ({string.Join(", ", details)}).");
    }

    private static DoctorCheck BuildProviderCheck(DoctorConfigSummary config)
    {
        var backend = NormalizeBackend(config.DefaultBackend);
        var openRouterConfigured = HasValue(GetEnvironmentVariableWithFallback("OPENROUTER_API_KEY")) || HasValue(config.OpenRouterApiKey);
        var customLocalModelPath = ExpandPath(config.LocalLlamaModelPath);

        if (!string.IsNullOrWhiteSpace(customLocalModelPath))
        {
            if (!File.Exists(customLocalModelPath))
            {
                return DoctorCheck.Error(
                    "Assistant provider",
                    $"providers.local_llama.modelPath points to a missing file: {customLocalModelPath}",
                    "Update the path or remove it so MALDA can fall back to its default local model behavior.");
            }

            if (backend == "local-llama")
            {
                return DoctorCheck.Ok("Assistant provider", $"default backend is local-llama with model {customLocalModelPath}");
            }

            return DoctorCheck.Ok("Assistant provider", $"local llama model is configured at {customLocalModelPath}");
        }

        if (backend == "local-llama")
        {
            return DoctorCheck.Info(
                "Assistant provider",
                "default backend is local-llama with no custom modelPath; MALDA will use its default local model flow on first run.");
        }

        if (openRouterConfigured)
        {
            if (backend == "openrouter")
            {
                return DoctorCheck.Ok("Assistant provider", "default backend is openrouter and an API key is configured.");
            }

            return DoctorCheck.Ok("Assistant provider", "an OpenRouter API key is configured.");
        }

        return DoctorCheck.Warning(
            "Assistant provider",
            "no assistant provider is configured yet.",
            "Set OPENROUTER_API_KEY or providers.openrouter.apiKey, or configure providers.local_llama.modelPath in ~/.malda/config.json.");
    }

    private static DoctorCheck BuildTelegramCheck(DoctorConfigSummary config)
    {
        var token = GetEnvironmentVariableWithFallback("TELEGRAM_BOT_TOKEN") ?? config.TelegramBotToken;
        if (HasValue(token))
        {
            if (HasValue(config.TelegramNotifyChatId))
            {
                return DoctorCheck.Ok(
                    "Telegram channel",
                    "bot token and channels.telegram.notifyChatId are configured for gateway alerts.");
            }

            return DoctorCheck.Ok(
                "Telegram channel",
                "bot token is configured.",
                "Set channels.telegram.notifyChatId in ~/.malda/config.json to receive gateway/cron alerts.");
        }

        return DoctorCheck.Info(
            "Telegram channel",
            "Telegram is not configured.",
            "Set TELEGRAM_BOT_TOKEN or channels.telegram.botToken before running malda gateway.");
    }

    private DoctorCheck BuildGatewayCheck()
    {
        var pidPath = GatewayRunner.GetGatewayPidPath(_maldaHomePath);
        if (GatewayRunner.IsGatewayProcessRunning(pidPath))
        {
            GatewayRunner.TryReadGatewayPid(pidPath, out var pid);
            return DoctorCheck.Ok("Gateway", $"running (pid {pid}).");
        }

        if (GatewayNotifier.TryReadCrashMarker(_maldaHomePath, out var crash))
        {
            return DoctorCheck.Warning(
                "Gateway",
                $"previous crash recorded at {crash.AtUtc}: {crash.Reason}",
                "Investigate ~/.malda/gateway-alerts.log, fix the issue, then run malda gateway again.");
        }

        if (File.Exists(pidPath))
        {
            return DoctorCheck.Warning(
                "Gateway",
                "stale gateway.pid was found (process not running).",
                "Run malda gateway or remove ~/.malda/gateway.pid if no gateway should be active.");
        }

        var alertsPath = GatewayNotifier.GetAlertsLogPath(_maldaHomePath);
        if (File.Exists(alertsPath))
        {
            return DoctorCheck.Info(
                "Gateway",
                "stopped; alert log exists at ~/.malda/gateway-alerts.log.",
                "Run malda gateway when ready.");
        }

        return DoctorCheck.Info("Gateway", "not running.");
    }

    private DoctorCheck BuildMemoryCheck()
    {
        var memoryPath = Path.Combine(_maldaHomePath, "memory", "assistant");
        var graphPath = memoryPath + ".graph.json";
        if (File.Exists(graphPath))
        {
            return DoctorCheck.Ok("GraphMemory", $"assistant memory found at {memoryPath}.");
        }

        var memoryDir = Path.Combine(_maldaHomePath, "memory");
        if (Directory.Exists(memoryDir))
        {
            return DoctorCheck.Info(
                "GraphMemory",
                "memory directory exists but assistant memory has not been saved yet.",
                "Run malda agent once to initialize ~/.malda/memory/assistant.");
        }

        return DoctorCheck.Info(
            "GraphMemory",
            "no assistant memory on disk yet.",
            "Run malda onboard and malda agent to create ~/.malda/memory/assistant.");
    }

    private static DoctorCheck BuildOnnxRerankCheck(DoctorConfigSummary config, string maldaHome)
    {
        var rerankMode = (config.MemoryRerankMode ?? "").Trim().ToLowerInvariant();
        if (rerankMode != "onnx")
        {
            return DoctorCheck.Info(
                "ONNX rerank",
                string.IsNullOrWhiteSpace(rerankMode)
                    ? "agents.memory.rerankMode is not set to onnx."
                    : $"agents.memory.rerankMode is '{rerankMode}'.");
        }

        var configuredPath = CrossEncoderOnnxModels.ResolveRerankModelPath(config.MemoryRerankModelPath, maldaHome);
        if (!string.IsNullOrWhiteSpace(configuredPath) && CrossEncoderOnnxModels.IsInstalled(configuredPath))
        {
            return DoctorCheck.Ok("ONNX rerank", $"cross-encoder model ready at {configuredPath}.");
        }

        var defaultDir = CrossEncoderOnnxModels.GetDefaultModelDirectory(maldaHome);
        if (CrossEncoderOnnxModels.IsInstalled(defaultDir))
        {
            return DoctorCheck.Ok("ONNX rerank", $"cross-encoder model ready at {defaultDir}.");
        }

        return DoctorCheck.Warning(
            "ONNX rerank",
            "rerankMode is onnx but no model.onnx + vocab.txt were found.",
            "Run malda memory download-rerank or malda onboard --download-rerank.");
    }

    private DoctorCheck BuildSkillsCheck()
    {
        var skillsDir = Path.Combine(_maldaHomePath, "skills");
        if (!Directory.Exists(skillsDir))
        {
            return DoctorCheck.Warning(
                "Skills",
                $"{skillsDir} does not exist.",
                "Run malda onboard to create ~/.malda/skills/ and install the greeting skill template.");
        }

        var files = Directory.GetFiles(skillsDir, "*.malda");
        if (files.Length == 0)
        {
            return DoctorCheck.Warning(
                "Skills",
                "no .malda skill files found.",
                "Copy Examples/Assistant/skills/greeting.malda to ~/.malda/skills/ or run malda onboard.");
        }

        var parseErrors = new List<string>();
        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var source = File.ReadAllText(file);
                var lexer = new Lexer(source, file);
                var parser = new Parser.Parser(lexer.Tokenize(), file);
                parser.Parse();
                if (parser.Errors.Count > 0)
                    parseErrors.Add($"{Path.GetFileName(file)}: {parser.Errors[0].Message}");
            }
            catch (Exception ex)
            {
                parseErrors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (parseErrors.Count > 0)
        {
            return DoctorCheck.Error(
                "Skills",
                $"{files.Length} skill file(s); {parseErrors.Count} failed parse check ({parseErrors[0]}).",
                "Fix skill syntax; the assistant skips skills that fail to load.");
        }

        var withAgent = files.Count(f => File.ReadAllText(f).Contains("var agent", StringComparison.Ordinal));
        return DoctorCheck.Ok(
            "Skills",
            $"{files.Length} skill file(s) in ~/.malda/skills ({withAgent} export agent sub-agents).");
    }

    private static DoctorCheck BuildSearchCheck(DoctorConfigSummary config)
    {
        var braveConfigured = HasValue(GetEnvironmentVariableWithFallback("BRAVE_SEARCH_API_KEY")) || HasValue(config.BraveSearchApiKey);
        if (braveConfigured)
        {
            return DoctorCheck.Ok("Optional tools", "Brave Search credentials are configured for webSearch().");
        }

        return DoctorCheck.Info(
            "Optional tools",
            "webSearch() is not configured.",
            "Set BRAVE_SEARCH_API_KEY or tools.web.search.apiKey in ~/.malda/config.json if you want built-in web search.");
    }

    private static DoctorCheck BuildProjectCheck(string workingDirectory)
    {
        var rootApp = Path.Combine(workingDirectory, "app.malda");
        var backendApp = Path.Combine(workingDirectory, "backend", "app.malda");
        var configDir = Path.Combine(workingDirectory, "config");
        var projectLike = File.Exists(rootApp) || File.Exists(backendApp) || Directory.Exists(configDir);

        if (!projectLike)
        {
            return DoctorCheck.Info(
                "Project scaffold",
                "current directory does not look like a scaffolded MALDA app; skipped deploy/profile checks.",
                "Run 'malda new webapi <dir>' or 'malda new fullstack <dir>' to create a scaffolded project.");
        }

        var missing = new List<string>();
        if (!File.Exists(rootApp) && !File.Exists(backendApp))
        {
            missing.Add("app.malda or backend/app.malda");
        }

        var requiredFiles = new[]
        {
            Path.Combine("config", "deploy.example.json"),
            Path.Combine("config", "observability.example.json"),
            Path.Combine("config", "environments", "prod.json")
        };

        foreach (var relativePath in requiredFiles)
        {
            if (!File.Exists(Path.Combine(workingDirectory, relativePath)))
            {
                missing.Add(relativePath.Replace('\\', '/'));
            }
        }

        if (missing.Count == 0)
        {
            return DoctorCheck.Ok(
                "Project scaffold",
                "found MALDA entrypoint plus deploy/observability/profile defaults for this directory.");
        }

        return DoctorCheck.Warning(
            "Project scaffold",
            $"missing {string.Join(", ", missing)}.",
            "'malda deploy' expects these defaults unless you pass explicit paths.");
    }

    private void WriteReport(TextWriter output, string workingDirectory, IReadOnlyList<DoctorCheck> checks)
    {
        output.WriteLine("MALDA doctor");
        output.WriteLine($"  Working directory: {workingDirectory}");
        output.WriteLine($"  MALDA home: {_maldaHomePath}");
        output.WriteLine();

        foreach (var check in checks)
        {
            output.WriteLine($"{FormatStatus(check.Status)} {check.Title}: {check.Message}");
            if (!string.IsNullOrWhiteSpace(check.Action))
            {
                output.WriteLine($"       {check.Action}");
            }
        }

        var okCount = checks.Count(x => x.Status == DoctorStatus.Ok);
        var warnCount = checks.Count(x => x.Status == DoctorStatus.Warning);
        var errorCount = checks.Count(x => x.Status == DoctorStatus.Error);
        var infoCount = checks.Count(x => x.Status == DoctorStatus.Info);

        output.WriteLine();
        output.WriteLine($"Summary: {okCount} ok, {warnCount} warning(s), {errorCount} error(s), {infoCount} info.");
        output.WriteLine("Note: doctor checks local config/files only. It does not call provider APIs or probe IDE setup.");
    }

    private DoctorConfigSummary LoadConfigSummary()
    {
        var configPath = Path.Combine(_maldaHomePath, "config.json");
        var summary = new DoctorConfigSummary
        {
            HomeExists = Directory.Exists(_maldaHomePath),
            ConfigPath = configPath,
            ConfigExists = File.Exists(configPath)
        };

        if (!summary.ConfigExists)
        {
            return summary;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;

            summary.ConfigParsed = true;
            summary.OpenRouterApiKey = GetNestedString(root, "providers", "openrouter", "apiKey");
            summary.LocalLlamaModelPath = GetNestedString(root, "providers", "local_llama", "modelPath");
            summary.DefaultBackend = GetNestedString(root, "agents", "defaults", "backend");
            summary.DefaultModel = GetNestedString(root, "agents", "defaults", "model");
            summary.BraveSearchApiKey = GetNestedString(root, "tools", "web", "search", "apiKey");
            summary.TelegramBotToken = GetNestedString(root, "channels", "telegram", "botToken");
            summary.TelegramNotifyChatId = GetNestedString(root, "channels", "telegram", "notifyChatId");
            summary.MemoryRerankMode = GetNestedString(root, "agents", "memory", "rerankMode");
            summary.MemoryRerankModelPath = GetNestedString(root, "agents", "memory", "rerankModelPath");
        }
        catch (Exception ex) when (ex is JsonException || ex is IOException)
        {
            summary.ParseError = ex.Message;
        }

        return summary;
    }

    private static string? GetNestedString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Null => null,
            _ => current.ToString()
        };
    }

    private static string? GetEnvironmentVariableWithFallback(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (HasValue(value))
        {
            return value;
        }

        foreach (var target in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            try
            {
                value = Environment.GetEnvironmentVariable(name, target);
                if (HasValue(value))
                {
                    return value;
                }
            }
            catch
            {
                // Best effort only: some targets are not available on all platforms.
            }
        }

        return null;
    }

    private static string NormalizeBackend(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "local_llama" => "local-llama",
            "localllama" => "local-llama",
            _ => normalized
        };
    }

    private static string? ExpandPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (expanded.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            expanded.StartsWith("~" + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                expanded = Path.Combine(home, expanded.Substring(2));
            }
        }

        return Path.GetFullPath(expanded);
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string FormatStatus(DoctorStatus status)
    {
        return status switch
        {
            DoctorStatus.Ok => "[ok]",
            DoctorStatus.Warning => "[warn]",
            DoctorStatus.Error => "[error]",
            _ => "[info]"
        };
    }

    internal sealed class DoctorConfigSummary
    {
        public bool HomeExists { get; init; }
        public string ConfigPath { get; init; } = string.Empty;
        public bool ConfigExists { get; init; }
        public bool ConfigParsed { get; set; }
        public string? ParseError { get; set; }
        public string? OpenRouterApiKey { get; set; }
        public string? LocalLlamaModelPath { get; set; }
        public string? DefaultBackend { get; set; }
        public string? DefaultModel { get; set; }
        public string? BraveSearchApiKey { get; set; }
        public string? TelegramBotToken { get; set; }
        public string? TelegramNotifyChatId { get; set; }
        public string? MemoryRerankMode { get; set; }
        public string? MemoryRerankModelPath { get; set; }
    }

    internal sealed class DoctorCheck
    {
        private DoctorCheck(DoctorStatus status, string title, string message, string? action)
        {
            Status = status;
            Title = title;
            Message = message;
            Action = action;
        }

        public DoctorStatus Status { get; }
        public string Title { get; }
        public string Message { get; }
        public string? Action { get; }

        public static DoctorCheck Ok(string title, string message, string? action = null)
            => new(DoctorStatus.Ok, title, message, action);

        public static DoctorCheck Warning(string title, string message, string? action = null)
            => new(DoctorStatus.Warning, title, message, action);

        public static DoctorCheck Error(string title, string message, string? action = null)
            => new(DoctorStatus.Error, title, message, action);

        public static DoctorCheck Info(string title, string message, string? action = null)
            => new(DoctorStatus.Info, title, message, action);
    }

    internal enum DoctorStatus
    {
        Ok,
        Warning,
        Error,
        Info
    }
}
