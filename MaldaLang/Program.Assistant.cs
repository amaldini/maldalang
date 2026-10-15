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

class CronJob
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Message { get; set; } = "";
    public string Cron { get; set; } = "";
    public string Scope { get; set; } = "";
}

class CronFile
{
    public List<CronJob> Jobs { get; set; } = new List<CronJob>();
}

partial class Program
{
    static string? GetAssistantScriptPath(bool skipUserScript = false)
    {
        // 1. MALDA_AGENT_SCRIPT environment variable
        var envPath = System.Environment.GetEnvironmentVariable("MALDA_AGENT_SCRIPT");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        // 2. ~/.malda/assistant.malda (personal chat). Develop mode skips this so a
        // customized assistant cannot drop the DevAgent / MALDACodingAgent tools.
        if (!skipUserScript)
        {
            var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                var userPath = Path.Combine(userProfile, ".malda", "assistant.malda");
                if (File.Exists(userPath))
                    return userPath;
            }
        }
        
        // 3. Examples/Assistant/assistant.malda (walk up from CWD, then base dir, then assembly)
        static string? FindExamplesAssistant(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Examples", "Assistant", "assistant.malda");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            return null;
        }
        
        var fromCwd = FindExamplesAssistant(Directory.GetCurrentDirectory());
        if (fromCwd != null)
            return fromCwd;
        
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var fromBase = FindExamplesAssistant(baseDir);
            if (fromBase != null)
                return fromBase;
        }
        
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                var fromAssembly = FindExamplesAssistant(assemblyDir);
                if (fromAssembly != null)
                    return fromAssembly;
            }
        }
        
        return null;
    }
    
    static string GetMaldaHomePath()
    {
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return Path.Combine(Directory.GetCurrentDirectory(), ".malda");
        return Path.Combine(userProfile, ".malda");
    }
    
    static string? GetTelegramBotToken()
    {
        var fromEnv = System.Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;
        return GetTelegramConfigString("botToken");
    }

    static string? GetTelegramNotifyChatId()
    {
        var fromEnv = System.Environment.GetEnvironmentVariable("MALDA_GATEWAY_NOTIFY_CHAT_ID");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;
        return GetTelegramConfigString("notifyChatId");
    }

    static string? GetTelegramConfigString(string propertyName)
    {
        var dir = GetMaldaHomePath();
        var configPath = Path.Combine(dir, "config.json");
        if (!File.Exists(configPath))
            return null;
        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("channels", out var ch) &&
                ch.ValueKind == JsonValueKind.Object &&
                ch.TryGetProperty("telegram", out var tg) &&
                tg.ValueKind == JsonValueKind.Object &&
                tg.TryGetProperty(propertyName, out var tokenEl))
                return tokenEl.GetString();
        }
        catch { }
        return null;
    }
    
    static string GetCronFilePath() => Path.Combine(GetMaldaHomePath(), "cron.json");
    
    static CronFile LoadCronFile()
    {
        var path = GetCronFilePath();
        if (!File.Exists(path))
            return new CronFile();
        try
        {
            var json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<CronFile>(json, opts);
            return file ?? new CronFile();
        }
        catch
        {
            return new CronFile();
        }
    }
    
    static void SaveCronFile(CronFile file)
    {
        var dir = GetMaldaHomePath();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var path = GetCronFilePath();
        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(path, json);
    }
    
    private struct WindowsSchedule
    {
        public bool IsValid;
        public string ScheduleType; // "DAILY" or "WEEKLY"
        public string[]? Days;      // For WEEKLY: e.g. ["MON","TUE"]
        public TimeSpan StartTime;
    }
    
    static bool TryParseCronExpression(string expr, out WindowsSchedule schedule)
    {
        schedule = new WindowsSchedule { IsValid = false, ScheduleType = "", Days = null, StartTime = TimeSpan.Zero };
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
        
        schedule.StartTime = new TimeSpan(hour, minute, 0);
        
        if (dayOfWeek == "*")
        {
            schedule.ScheduleType = "DAILY";
            schedule.Days = null;
            schedule.IsValid = true;
            return true;
        }
        
        if (dayOfWeek == "1-5")
        {
            schedule.ScheduleType = "WEEKLY";
            schedule.Days = new[] { "MON", "TUE", "WED", "THU", "FRI" };
            schedule.IsValid = true;
            return true;
        }
        
        // Optional: single weekday number 0-6 (0 or 7 = Sunday in many crons, here treat 0 as SUN)
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
                schedule.ScheduleType = "WEEKLY";
                schedule.Days = new[] { day };
                schedule.IsValid = true;
                return true;
            }
        }
        
        return false;
    }
    
    static string? GetMaldaExePath()
    {
        // 1. Try where malda
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "malda",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadLine();
                proc.WaitForExit(5000);
                if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
                    return output.Trim();
            }
        }
        catch
        {
            // Ignore and fall back to repo-relative paths
        }
        
        // 2. Try repo-relative build output (Debug/Release)
        try
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(assemblyLocation))
            {
                var assemblyDir = Path.GetDirectoryName(assemblyLocation);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    // Walk up looking for MaldaLang\bin\*\net8.0\malda.exe
                    var dir = new DirectoryInfo(assemblyDir);
                    while (dir != null)
                    {
                        var releasePath = Path.Combine(dir.FullName, "MaldaLang", "bin", "Release", "net8.0", "malda.exe");
                        if (File.Exists(releasePath))
                            return releasePath;
                        var debugPath = Path.Combine(dir.FullName, "MaldaLang", "bin", "Debug", "net8.0", "malda.exe");
                        if (File.Exists(debugPath))
                            return debugPath;
                        dir = dir.Parent;
                    }
                }
            }
        }
        catch
        {
            // Ignore and fall through
        }
        
        return null;
    }
    
    static void RunProcess(string fileName, string arguments, out string stdOut, out string stdErr, out int exitCode)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            stdOut = "";
            stdErr = "Failed to start process.";
            exitCode = -1;
            return;
        }
        stdOut = proc.StandardOutput.ReadToEnd();
        stdErr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        exitCode = proc.ExitCode;
    }
    
    static void InstallCronJobsOnWindows(CronFile file)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.Error.WriteLine("cron install is only supported on Windows. Use your system scheduler manually.");
            SystemEnvironment.Exit(1);
            return;
        }
        
        if (file.Jobs.Count == 0)
        {
            Console.WriteLine("No cron jobs defined in ~/.malda/cron.json; nothing to install.");
            return;
        }
        
        var exePath = GetMaldaExePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            Console.Error.WriteLine("Could not locate malda.exe; install malda on PATH or build the project.");
            SystemEnvironment.Exit(1);
            return;
        }
        
        // Delete existing MALDA_cron_* tasks
        try
        {
            RunProcess("schtasks", "/Query /FO LIST", out var queryOut, out var queryErr, out var queryCode);
            if (queryCode == 0)
            {
                using var reader = new StringReader(queryOut);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.StartsWith("TaskName:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = line.Substring("TaskName:".Length).Trim();
                        if (name.StartsWith("\\MALDA_cron_", StringComparison.OrdinalIgnoreCase))
                        {
                            RunProcess("schtasks", $"/Delete /TN \"{name}\" /F", out _, out var delErr, out var delCode);
                            if (delCode != 0 && !string.IsNullOrWhiteSpace(delErr))
                            {
                                Console.Error.WriteLine($"Failed to delete task {name}: {delErr.Trim()}");
                            }
                        }
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(queryErr))
            {
                Console.Error.WriteLine($"Failed to query scheduled tasks: {queryErr.Trim()}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error while deleting previous MALDA tasks: {ex.Message}");
        }
        
        // Create tasks for current jobs
        foreach (var job in file.Jobs)
        {
            if (!TryParseCronExpression(job.Cron, out var schedule) || !schedule.IsValid)
            {
                Console.Error.WriteLine($"Unsupported cron expression '{job.Cron}' for job {job.Id}; skipping.");
                continue;
            }
            
            var st = schedule.StartTime;
            var stString = st.Hours.ToString("00") + ":" + st.Minutes.ToString("00");
            
            // Escape quotes in message for schtasks command
            var message = job.Message ?? "";
            message = message.Replace("\"", "\\\"");
            var scope = ResolveCronJobScope(job);
            var tr = $"cmd /c \"set MALDA_MEMORY_SCOPE={scope}&& \\\"{exePath}\\\" agent -m \\\"{message}\\\"\"";
            
            var taskName = $"MALDA_cron_{job.Id}";
            string args;
            if (schedule.ScheduleType == "DAILY")
            {
                args = $"/Create /TN \"{taskName}\" /TR \"{tr}\" /SC DAILY /ST {stString} /RU \"%USERNAME%\" /F";
            }
            else // WEEKLY
            {
                var days = schedule.Days != null && schedule.Days.Length > 0
                    ? string.Join(",", schedule.Days)
                    : "MON";
                args = $"/Create /TN \"{taskName}\" /TR \"{tr}\" /SC WEEKLY /D {days} /ST {stString} /RU \"%USERNAME%\" /F";
            }
            
            RunProcess("schtasks", args, out _, out var createErr, out var createCode);
            if (createCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(createErr))
                {
                    Console.Error.WriteLine($"Failed to create task for job {job.Id}: {createErr.Trim()}");
                }
                else
                {
                    Console.Error.WriteLine($"Failed to create task for job {job.Id}: schtasks exited with code {createCode}");
                }
            }
            else
            {
                Console.WriteLine($"Installed Task Scheduler job for {job.Id}: {job.Name}");
            }
        }
    }
    
    static string ResolveCronJobScope(CronJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.Scope))
            return job.Scope.Trim();
        var name = string.IsNullOrWhiteSpace(job.Name) ? job.Id : job.Name.Trim();
        return "cron:" + name;
    }

    static void RunAssistantWithChannel(string? channel, bool skipUserScript = false)
    {
        var scriptPath = GetAssistantScriptPath(skipUserScript);
        if (string.IsNullOrEmpty(scriptPath))
        {
            Console.Error.WriteLine("Assistant script not found. Set MALDA_AGENT_SCRIPT or create ~/.malda/assistant.malda or run from repo with Examples/Assistant/assistant.malda.");
            SystemEnvironment.Exit(1);
            return;
        }
        var source = File.ReadAllText(scriptPath);
        if (string.Equals(channel, "telegram", StringComparison.OrdinalIgnoreCase))
        {
            var botToken = GetTelegramBotToken();
            if (string.IsNullOrWhiteSpace(botToken))
            {
                Console.Error.WriteLine("Telegram channel requires a bot token. Set TELEGRAM_BOT_TOKEN or add channels.telegram.botToken to ~/.malda/config.json");
                SystemEnvironment.Exit(1);
                return;
            }
            var telegramChannel = new MaldaLang.Channels.TelegramChannel(botToken);
            var adapter = new MaldaLang.Channels.ChannelInputProvider(telegramChannel);
            var interpreter = new Interpreter.Interpreter(inputProvider: adapter);
            interpreter.SetOutputCallback(adapter.SendOutput);
            Run(source, interpreter, scriptPath);
            telegramChannel.Stop();
            return;
        }
        Run(source, null, scriptPath);
    }

    static void RunGatewayCronJob(GatewayCronJob job)
    {
        var home = GetMaldaHomePath();
        var botToken = GetTelegramBotToken();
        var notifyChatId = GetTelegramNotifyChatId();
        var exePath = GetMaldaExePath();
        if (string.IsNullOrWhiteSpace(exePath))
            exePath = SystemEnvironment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return;

        var escapedMessage = (job.Message ?? "").Replace("\"", "\\\"");
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"agent -m \"{escapedMessage}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (!string.IsNullOrWhiteSpace(job.Scope))
            psi.Environment["MALDA_MEMORY_SCOPE"] = job.Scope;

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                var detail = $"cron job '{job.Name}' failed to start.";
                Console.Error.WriteLine($"[gateway cron] {detail}");
                GatewayNotifier.NotifyFireAndForget(home, "Cron failed", detail, botToken, notifyChatId);
                return;
            }
            if (!proc.WaitForExit(300_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                var detail = $"cron job '{job.Name}' timed out after 5 minutes.";
                Console.Error.WriteLine($"[gateway cron] {detail}");
                GatewayNotifier.NotifyFireAndForget(home, "Cron timeout", detail, botToken, notifyChatId);
                return;
            }
            Console.WriteLine($"[gateway cron] {job.Name}: exit {proc.ExitCode}");
            if (proc.ExitCode != 0)
            {
                var stderr = proc.StandardError.ReadToEnd();
                var detail = $"cron job '{job.Name}' exited with code {proc.ExitCode}.";
                if (!string.IsNullOrWhiteSpace(stderr))
                    detail += " " + stderr.Trim();
                GatewayNotifier.NotifyFireAndForget(home, "Cron failed", detail, botToken, notifyChatId);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[gateway cron] {job.Name}: {ex.Message}");
            GatewayNotifier.NotifyFireAndForget(home, "Cron error", $"{job.Name}: {ex.Message}", botToken, notifyChatId);
        }
    }

    static void GatewayCommand(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[1], "stop", StringComparison.OrdinalIgnoreCase))
        {
            var stopHome = GetMaldaHomePath();
            var stopPidPath = GatewayRunner.GetGatewayPidPath(stopHome);
            var result = GatewayRunner.TryStopGateway(stopPidPath);
            if (result.Stopped)
                Console.WriteLine(result.Message);
            else
                Console.WriteLine(result.Message);
            if (!result.Stopped && result.Pid > 0)
                SystemEnvironment.Exit(1);
            return;
        }

        string? channel = "telegram";
        var withCron = true;
        for (var i = 1; i < args.Length; i++)
        {
            if ((args[i] == "-c" || args[i] == "--channel") && i + 1 < args.Length)
            {
                channel = args[i + 1];
                i++;
            }
            else if (args[i] == "--no-cron")
            {
                withCron = false;
            }
        }

        if (!string.Equals(channel, "telegram", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unsupported gateway channel: {channel}. Only 'telegram' is supported.");
            SystemEnvironment.Exit(1);
            return;
        }

        var home = GetMaldaHomePath();
        var pidPath = GatewayRunner.GetGatewayPidPath(home);
        if (GatewayRunner.IsGatewayProcessRunning(pidPath))
        {
            if (GatewayRunner.TryReadGatewayPid(pidPath, out var existingPid))
                Console.Error.WriteLine($"Gateway is already running (pid {existingPid}). Stop it before starting a new gateway.");
            else
                Console.Error.WriteLine("Gateway is already running. Stop the existing process or remove ~/.malda/gateway.pid");
            SystemEnvironment.Exit(1);
            return;
        }

        if (string.IsNullOrWhiteSpace(GetTelegramBotToken()))
        {
            Console.Error.WriteLine("Gateway requires Telegram. Set TELEGRAM_BOT_TOKEN or channels.telegram.botToken in ~/.malda/config.json");
            SystemEnvironment.Exit(1);
            return;
        }

        if (GatewayNotifier.TryReadCrashMarker(home, out var previousCrash))
        {
            Console.Error.WriteLine($"[gateway] Previous crash at {previousCrash.AtUtc}: {previousCrash.Reason}");
            GatewayNotifier.NotifyFireAndForget(
                home,
                "Gateway restarted after crash",
                $"{previousCrash.AtUtc}: {previousCrash.Reason}",
                GetTelegramBotToken(),
                GetTelegramNotifyChatId());
            GatewayNotifier.ClearCrashMarker(home);
        }

        GatewayRunner.WriteGatewayPid(pidPath);
        IDisposable? cronHandle = null;
        if (withCron)
        {
            var cronFile = LoadCronFile();
            var jobs = cronFile.Jobs.Select(j => new GatewayCronJob
            {
                Id = j.Id,
                Name = j.Name,
                Message = j.Message,
                Cron = j.Cron,
                Scope = ResolveCronJobScope(j)
            }).ToList();
            cronHandle = GatewayRunner.StartCronScheduler(jobs, RunGatewayCronJob, TimeSpan.FromSeconds(60));
            if (jobs.Count > 0)
                Console.WriteLine($"Gateway cron scheduler: {jobs.Count} job(s)");
        }

        var notifyChatId = GetTelegramNotifyChatId();
        if (!string.IsNullOrWhiteSpace(notifyChatId))
            Console.WriteLine($"Gateway alerts will be sent to Telegram chat {notifyChatId}.");

        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        var gatewayFailed = false;
        string? gatewayFailureReason = null;
        try
        {
            Console.WriteLine("MALDA gateway starting (Telegram). Press Ctrl+C to stop.");
            RunAssistantWithChannel("telegram");
        }
        catch (Exception ex)
        {
            gatewayFailed = true;
            gatewayFailureReason = ex.Message;
            Console.Error.WriteLine($"[gateway] {ex.Message}");
            throw;
        }
        finally
        {
            cronHandle?.Dispose();
            GatewayRunner.RemoveGatewayPid(pidPath);
            if (gatewayFailed)
            {
                GatewayNotifier.RecordCrash(home, gatewayFailureReason ?? "gateway exited with error");
                GatewayNotifier.NotifyFireAndForget(
                    home,
                    "Gateway crashed",
                    gatewayFailureReason ?? "gateway exited with error",
                    GetTelegramBotToken(),
                    GetTelegramNotifyChatId());
            }
        }
    }

    static void CronCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: malda cron <add|list|remove|install> [options]");
            Console.WriteLine("  malda cron add --name <name> --message <message> --cron <cron-expr> [--scope <scope>]");
            Console.WriteLine("  malda cron list");
            Console.WriteLine("  malda cron remove <job-id>");
            Console.WriteLine("  malda cron install                 - On Windows, sync cron.json with Task Scheduler");
            SystemEnvironment.Exit(1);
            return;
        }
        var sub = args[1].ToLower();
        if (sub == "add")
        {
            string? name = null, message = null, cron = null, scope = null;
            for (int i = 2; i < args.Length; i++)
            {
                if ((args[i] == "--name" || args[i] == "-n") && i + 1 < args.Length) { name = args[i + 1]; i++; }
                else if ((args[i] == "--message" || args[i] == "-m") && i + 1 < args.Length) { message = args[i + 1]; i++; }
                else if ((args[i] == "--cron" || args[i] == "-c") && i + 1 < args.Length) { cron = args[i + 1]; i++; }
                else if ((args[i] == "--scope" || args[i] == "-s") && i + 1 < args.Length) { scope = args[i + 1]; i++; }
            }
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(cron))
            {
                Console.Error.WriteLine("cron add requires --name, --message, and --cron");
                SystemEnvironment.Exit(1);
                return;
            }
            var file = LoadCronFile();
            var job = new CronJob
            {
                Id = Guid.NewGuid().ToString(),
                Name = name!,
                Message = message!,
                Cron = cron!,
                Scope = string.IsNullOrWhiteSpace(scope) ? "cron:" + name! : scope!
            };
            file.Jobs.Add(job);
            SaveCronFile(file);
            Console.WriteLine($"Added job {job.Id}: {job.Name} (scope={job.Scope})");
        }
        else if (sub == "list")
        {
            var file = LoadCronFile();
            if (file.Jobs.Count == 0)
            {
                Console.WriteLine("No cron jobs.");
                return;
            }
            foreach (var job in file.Jobs)
            {
                var scope = ResolveCronJobScope(job);
                Console.WriteLine($"{job.Id}  name={job.Name}  scope={scope}  message=\"{job.Message}\"  cron={job.Cron}");
            }
        }
        else if (sub == "remove")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("malda cron remove <job-id>");
                SystemEnvironment.Exit(1);
                return;
            }
            var id = args[2];
            var file = LoadCronFile();
            var removed = file.Jobs.RemoveAll(j => j.Id == id);
            if (removed == 0)
            {
                Console.Error.WriteLine($"Job '{id}' not found.");
                SystemEnvironment.Exit(1);
                return;
            }
            SaveCronFile(file);
            Console.WriteLine($"Removed job {id}");
        }
        else if (sub == "install")
        {
            var file = LoadCronFile();
            InstallCronJobsOnWindows(file);
        }
        else
        {
            Console.Error.WriteLine($"Unknown cron subcommand: {sub}");
            SystemEnvironment.Exit(1);
        }
    }
}
