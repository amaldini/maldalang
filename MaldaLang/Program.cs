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
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    static void ConfigureConsoleEncoding()
    {
        try
        {
            // Set console output encoding to UTF-8
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            
            // On Windows, also set the console code page to UTF-8 (65001)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetConsoleOutputCP(65001); // UTF-8 code page
                SetConsoleCP(65001); // UTF-8 code page
                
                // Enable ANSI escape codes in Windows console for Spectre.Console support
                try
                {
                    var stdoutHandle = GetStdHandle(STD_OUTPUT_HANDLE);
                    if (GetConsoleMode(stdoutHandle, out uint mode))
                    {
                        mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                        SetConsoleMode(stdoutHandle, mode);
                    }
                }
                catch
                {
                    // Ignore errors - ANSI might not be supported on older Windows versions
                }
            }
        }
        catch
        {
            // Ignore errors - encoding setup is best effort
            // Some systems may not support changing code page
        }
    }

    static void Main(string[] args)
    {
        // Configure console for Unicode support
        ConfigureConsoleEncoding();

        // DAP owns stdin/stdout. Branch before help, banners, or stdin reads.
        if (args.Length > 0 && args[0].Equals("debug-adapter", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1 && IsHelpFlag(args[1]))
            {
                TryShowCommandHelp("debug-adapter");
                return;
            }

            DebugAdapterSession.RunStdioAsync().GetAwaiter().GetResult();
            return;
        }
        
        // Check for help command
        if (args.Length > 0)
        {
            var firstArg = args[0].ToLower();
            if (firstArg == "help" || firstArg == "--help" || firstArg == "-h")
            {
                if (args.Length > 1 && TryShowCommandHelp(args[1]))
                    return;
                ShowHelp();
                return;
            }
        }

        if (args.Length > 1 && IsHelpFlag(args[1]) && TryShowCommandHelp(args[0]))
            return;
        
        // Check for symbols command
        if (args.Length > 0)
        {
            var firstArg = args[0].ToLower();
            if (firstArg == "--symbols" || firstArg == "-s" || firstArg == "symbols")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: malda --symbols <file.malda>");
                    Console.WriteLine("  --symbols, -s, symbols  - Print symbols (classes, functions, actors) from a MALDA file");
                    SystemEnvironment.Exit(1);
                    return;
                }
                PrintSymbolsFromFile(args[1]);
                return;
            }
        }
        
        // Check for command-line code execution/compilation flags
        if (args.Length > 0)
        {
            var firstArg = args[0].ToLower();
            
            if (firstArg == "--eval")
            {
                Console.Error.WriteLine("'malda --eval' was removed because it collided with 'malda eval' (suite / case).");
                Console.Error.WriteLine("  Execute a snippet:  malda -e \"<code>\"   or   malda --exec \"<code>\"");
                Console.Error.WriteLine("  Run eval suites:    malda eval <file.malda>");
                SystemEnvironment.Exit(2);
                return;
            }

            if (firstArg == "-c" || firstArg == "--compile" || firstArg == "-e" || firstArg == "--exec" || firstArg == "--check")
            {
                // Compile, execute, or validate code directly from command line
                if (args.Length < 2)
                {
                    Console.WriteLine($"Usage: malda {firstArg} <code> [options]");
                    Console.WriteLine($"  {firstArg} <code> - Code to {(firstArg == "-c" || firstArg == "--compile" ? "compile" : firstArg == "--check" ? "validate" : "execute")}");
                    Console.WriteLine($"  Options:");
                    if (firstArg == "-c" || firstArg == "--compile")
                    {
                        Console.WriteLine($"    -o <output.exe|output.dll|output.js|output-dir> - Output executable, DLL, JavaScript file, or PWA directory");
                        Console.WriteLine($"    --mode <mode>   - Compilation mode: 'interpreter' (default), 'transpile', 'dll', 'js', 'pwa', or 'fullstack'");
                        Console.WriteLine($"    --target <target> - Use 'js', 'pwa', or 'fullstack'");
                    }
                    SystemEnvironment.Exit(1);
                    return;
                }
                
                var code = args[1];
                if (firstArg == "-c" || firstArg == "--compile")
                {
                    CompileFromCommandLine(args);
                }
                else if (firstArg == "--check")
                {
                    ValidateFromCommandLine(code);
                }
                else
                {
                    ExecuteFromCommandLine(code, ParseCliRunOptions(args, 2, writeToError: true));
                }
                return;
            }
            else if (firstArg == "compile" || firstArg == "publish")
            {
                CompileCommand(args, forceTranspilePublish: firstArg == "publish");
                return;
            }
            else if (firstArg == "test")
            {
                var runner = new TestCommandRunner();
                var exitCode = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
                SystemEnvironment.Exit(exitCode);
                return;
            }
            else if (firstArg == "eval")
            {
                var runner = new EvalCommandRunner();
                var exitCode = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
                SystemEnvironment.Exit(exitCode);
                return;
            }
            else if (firstArg == "prompts")
            {
                var runner = new PromptsCommandRunner();
                var exitCode = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
                SystemEnvironment.Exit(exitCode);
                return;
            }
            else if (firstArg == "check")
            {
                var runner = new CheckCommandRunner();
                var exitCode = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
                SystemEnvironment.Exit(exitCode);
                return;
            }
            else if (firstArg == "fmt")
            {
                FmtCommand(args);
                return;
            }
            else if (firstArg == "new")
            {
                NewCommand(args);
                return;
            }
            else if (firstArg == "play")
            {
                var runner = new PlayCommandRunner();
                var exitCode = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
                SystemEnvironment.Exit(exitCode);
                return;
            }
            else if (firstArg == "trace")
            {
                // malda trace <subcommand> ...
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  malda trace summary <traceFile>");
                    Console.WriteLine("  malda trace show <traceFile> [--from N] [--to M] [--type TYPE]");
                    Console.WriteLine("  malda trace replay <traceFile> [--output <directory>]");
                    Console.WriteLine("  malda trace replay-tools <traceFile> --workdir <directory>");
                    SystemEnvironment.Exit(1);
                    return;
                }

                var sub = args[1].ToLower();
                var traceFile = args[2];

                if (sub == "summary")
                {
                    var code = TraceCli.Summary(traceFile, Console.Out, Console.Error);
                    SystemEnvironment.Exit(code);
                    return;
                }

                if (sub == "show")
                {
                    int from = 0;
                    int to = 50;
                    string? typeFilterString = null;

                    for (int i = 3; i < args.Length; i++)
                    {
                        var a = args[i];
                        if (a == "--from" && i + 1 < args.Length && int.TryParse(args[i + 1], out var f))
                        {
                            from = f;
                            i++;
                        }
                        else if (a == "--to" && i + 1 < args.Length && int.TryParse(args[i + 1], out var t))
                        {
                            to = t;
                            i++;
                        }
                        else if (a == "--type" && i + 1 < args.Length)
                        {
                            typeFilterString = args[i + 1];
                            i++;
                        }
                    }

                    MaldaLang.Runtime.Tracing.TraceEventType? typeFilter = null;
                    if (!string.IsNullOrWhiteSpace(typeFilterString) &&
                        Enum.TryParse<MaldaLang.Runtime.Tracing.TraceEventType>(typeFilterString, ignoreCase: true, out var parsed))
                    {
                        typeFilter = parsed;
                    }

                    var code = TraceCli.Show(traceFile, from, to, typeFilter, Console.Out, Console.Error);
                    SystemEnvironment.Exit(code);
                    return;
                }

                if (sub == "replay")
                {
                    // Default output directory: current working directory
                    var outputDir = Directory.GetCurrentDirectory();
                    for (int i = 3; i < args.Length; i++)
                    {
                        var a = args[i];
                        if ((a == "--output" || a == "-o") && i + 1 < args.Length)
                        {
                            outputDir = args[i + 1];
                            i++;
                        }
                    }

                    var code = TraceCli.Replay(traceFile, outputDir, Console.Out, Console.Error);
                    SystemEnvironment.Exit(code);
                    return;
                }

                if (sub == "replay-tools")
                {
                    string? workdir = null;
                    for (int i = 3; i < args.Length; i++)
                    {
                        var a = args[i];
                        if (a == "--workdir" && i + 1 < args.Length)
                        {
                            workdir = args[i + 1];
                            i++;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(workdir))
                    {
                        Console.Error.WriteLine("Error: --workdir is required for replay-tools.");
                        SystemEnvironment.Exit(1);
                        return;
                    }

                    var code = TraceCli.ReplayTools(traceFile, workdir, Console.Out, Console.Error);
                    SystemEnvironment.Exit(code);
                    return;
                }

                Console.WriteLine($"Unknown trace subcommand: {sub}");
                SystemEnvironment.Exit(1);
                return;
            }
            else if (firstArg == "install" || firstArg == "uninstall" || firstArg == "list" || 
                     firstArg == "search" || firstArg == "init")
            {
                PackageManagerCommand(args).GetAwaiter().GetResult();
                return;
            }
            else if (firstArg == "agent")
            {
                var launch = ParseAgentLaunch(args);
                if (launch.ShowHelp)
                {
                    ShowAgentHelp();
                    return;
                }
                if (!string.IsNullOrEmpty(launch.Error))
                {
                    Console.Error.WriteLine(launch.Error);
                    ShowAgentHelp();
                    SystemEnvironment.Exit(1);
                    return;
                }
                if (!string.IsNullOrWhiteSpace(launch.Message))
                {
                    System.Environment.SetEnvironmentVariable("MALDA_AGENT_MESSAGE", launch.Message);
                }
                if (!string.IsNullOrWhiteSpace(launch.Backend))
                {
                    System.Environment.SetEnvironmentVariable("MALDA_AGENT_BACKEND", launch.Backend);
                }
                if (!string.IsNullOrEmpty(launch.DevelopKind))
                {
                    var workdir = string.IsNullOrWhiteSpace(launch.Workdir)
                        ? Directory.GetCurrentDirectory()
                        : Path.GetFullPath(launch.Workdir);
                    if (!Directory.Exists(workdir))
                    {
                        Console.Error.WriteLine($"Working directory not found: {workdir}");
                        SystemEnvironment.Exit(1);
                        return;
                    }
                    System.Environment.SetEnvironmentVariable("MALDA_AGENT_KIND", launch.DevelopKind);
                    System.Environment.SetEnvironmentVariable("MALDA_AGENT_WORKDIR", workdir);
                    if (string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("MALDA_MEMORY_SCOPE")))
                    {
                        var leaf = new DirectoryInfo(workdir).Name;
                        if (string.IsNullOrWhiteSpace(leaf))
                            leaf = "project";
                        System.Environment.SetEnvironmentVariable("MALDA_MEMORY_SCOPE", "code:" + leaf);
                    }
                }
                RunAssistantWithChannel(launch.Channel, skipUserScript: !string.IsNullOrEmpty(launch.DevelopKind));
                return;
            }
            else if (firstArg == "gateway")
            {
                GatewayCommand(args);
                return;
            }
            else if (firstArg == "cron")
            {
                CronCommand(args);
                return;
            }
            else if (firstArg == "memory")
            {
                MemoryCommand(args);
                return;
            }
            else if (firstArg == "onboard")
            {
                OnboardCommand(args);
                return;
            }
            else if (firstArg == "status")
            {
                StatusCommand(args);
                return;
            }
            else if (firstArg == "doctor")
            {
                var runner = new DoctorCommandRunner(GetMaldaHomePath());
                var code = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error, Directory.GetCurrentDirectory());
                SystemEnvironment.Exit(code);
                return;
            }
            else if (firstArg == "db")
            {
                var runner = new DbCommandRunner();
                var code = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error, Directory.GetCurrentDirectory());
                SystemEnvironment.Exit(code);
                return;
            }
            else if (firstArg == "workflow")
            {
                var code = WorkflowCommand(args.Skip(1).ToArray());
                SystemEnvironment.Exit(code);
                return;
            }
            else if (firstArg == "deploy")
            {
                var runner = new DeployCommandRunner();
                var code = runner.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
                SystemEnvironment.Exit(code);
                return;
            }
        }
        
        // Stdin pipe/redirect: only read when data is available. Do not block before RunFile when a
        // .malda path was passed (IDE shells often set IsInputRedirected with an open empty stdin).
        if (Console.IsInputRedirected && !ShouldPreferScriptFileArg(args))
        {
            var stdinCode = TryReadAvailableStdin();
            if (!string.IsNullOrWhiteSpace(stdinCode))
            {
                // Check if we should compile, validate, or execute
                var firstArg = args.Length > 0 ? args[0].ToLower() : "";
                if (firstArg == "-c" || firstArg == "--compile")
                {
                    CompileFromStdin(stdinCode, args);
                }
                else if (firstArg == "--check")
                {
                    ValidateFromCommandLine(stdinCode);
                }
                else
                {
                    ExecuteFromCommandLine(stdinCode, ParseCliRunOptions(args, 0, writeToError: true));
                }
                return;
            }
        }
        
        // Default behavior: file or prompt
        if (args.Length > 0)
        {
            RunFile(args[0], ParseCliRunOptions(args, 1, writeToError: true));
        }
        else
        {
            RunPrompt();
        }
    }
    
    static bool ShouldPreferScriptFileArg(string[] args)
    {
        return args.Length > 0 && IsMaldaScriptPathArg(args[0]);
    }

    static bool IsMaldaScriptPathArg(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("-", StringComparison.Ordinal))
        {
            return false;
        }

        var lower = arg.ToLowerInvariant();
        return lower.EndsWith(".malda", StringComparison.Ordinal) ||
               lower.EndsWith(".malda.html", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads stdin only when redirected and at least one line is already available (Peek &gt;= 0).
    /// Avoids blocking forever on an open but empty stdin (common in IDE-integrated terminals).
    /// </summary>
    static string TryReadAvailableStdin()
    {
        if (!Console.IsInputRedirected)
        {
            return "";
        }

        try
        {
            if (Console.In.Peek() < 0)
            {
                return "";
            }
        }
        catch
        {
            return "";
        }

        return ReadFromStdin();
    }

    static string ReadFromStdin()
    {
        var sb = new StringBuilder();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            sb.AppendLine(line);
        }
        return sb.ToString();
    }
    
    static void ExecuteFromCommandLine(string code, CliRunOptions? runOptions = null)
    {
        try
        {
            Run(code, runOptions: runOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(RuntimeDiagnostics.FormatForConsole(ex));
            SystemEnvironment.Exit(1);
        }
    }
    
    // The compiler is loaded dynamically to avoid a circular project reference. Probe the
    // configuration this CLI was built with first, so a Release CLI does not silently pick up
    // a stale Debug compiler that happens to be on disk.
    private static readonly string[] CompilerConfigurationPreference =
#if DEBUG
        new[] { "Debug", "Release" };
#else
        new[] { "Release", "Debug" };
#endif

    static string ResolveCompilerAssemblyPath()
    {
        // Published / zip distributions ship the compiler next to malda.exe.
        var besideExe = Path.Combine(AppContext.BaseDirectory, "MaldaLang.Compiler.dll");
        if (File.Exists(besideExe))
        {
            return besideExe;
        }

        string? firstCandidate = null;

        foreach (var configuration in CompilerConfigurationPreference)
        {
            var candidate = Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "MaldaLang.Compiler", "bin", configuration, "net8.0", "MaldaLang.Compiler.dll"
            );

            firstCandidate ??= candidate;

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Prefer the beside-exe location in error messages for packaged installs.
        return besideExe;
    }

    static string GetVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            
            // Try to get informational version first (most descriptive)
            var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informationalVersion != null && !string.IsNullOrEmpty(informationalVersion.InformationalVersion))
            {
                // Remove git commit hash suffix (everything after '+')
                var version = informationalVersion.InformationalVersion;
                var plusIndex = version.IndexOf('+');
                if (plusIndex >= 0)
                {
                    version = version.Substring(0, plusIndex);
                }
                return version;
            }
            
            // Fall back to file version
            var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            if (fileVersion != null && !string.IsNullOrEmpty(fileVersion.Version))
            {
                return fileVersion.Version;
            }
            
            // Fall back to assembly version
            var versionObj = assembly.GetName().Version;
            if (versionObj != null)
            {
                return versionObj.ToString();
            }
        }
        catch
        {
            // If version info is not available, return empty string
        }
        
        return string.Empty;
    }
}
