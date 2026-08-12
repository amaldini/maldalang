// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
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
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.BuiltIns;
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

class InputResult
{
    public string? Code { get; set; }
    public string Action { get; set; } = "run"; // "run", "compile", "transpile", "exit", "help"
}

sealed class CliProfilingSettings
{
    public bool Enabled { get; set; }
    public string? OutputPath { get; set; }
    public ProfilingFormat Format { get; set; } = ProfilingFormat.Text;
    /// <summary>When &gt; 0, write profile file(s) on this interval (seconds) while the program runs.</summary>
    public double PeriodicSnapshotSeconds { get; set; }
}

class Program
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
            
            if (firstArg == "-c" || firstArg == "--compile" || firstArg == "-e" || firstArg == "--eval" || firstArg == "--check")
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
            else if (firstArg == "new")
            {
                NewCommand(args);
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
                // Parse optional -m / --message, -c / --channel, and -b / --backend
                string? message = null;
                string? channel = null;
                string? backend = null;
                for (int i = 1; i < args.Length; i++)
                {
                    if ((args[i] == "-m" || args[i] == "--message") && i + 1 < args.Length)
                    {
                        message = args[i + 1];
                    }
                    else if ((args[i] == "-c" || args[i] == "--channel") && i + 1 < args.Length)
                    {
                        channel = args[i + 1];
                    }
                    else if ((args[i] == "-b" || args[i] == "--backend") && i + 1 < args.Length)
                    {
                        backend = args[i + 1];
                    }
                }
                if (!string.IsNullOrWhiteSpace(message))
                {
                    System.Environment.SetEnvironmentVariable("MALDA_AGENT_MESSAGE", message);
                }
                if (!string.IsNullOrWhiteSpace(backend))
                {
                    System.Environment.SetEnvironmentVariable("MALDA_AGENT_BACKEND", backend);
                }
                RunAssistantWithChannel(channel);
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

    static void ValidateFromCommandLine(string code)
    {
        try
        {
            var compilerAssemblyPath = ResolveCompilerAssemblyPath();
            
            if (!File.Exists(compilerAssemblyPath))
            {
                Console.Error.WriteLine("Error: Compiler not found. Please build MaldaLang.Compiler project first.");
                SystemEnvironment.Exit(1);
                return;
            }
            
            var assembly = Assembly.LoadFrom(compilerAssemblyPath);
            var compilerType = assembly.GetType("MaldaLang.Compiler.Compiler");
            if (compilerType == null)
            {
                Console.Error.WriteLine("Error: Could not find Compiler class in MaldaLang.Compiler assembly.");
                SystemEnvironment.Exit(1);
                return;
            }
            
            var compiler = Activator.CreateInstance(compilerType);
            var validateMethod = compilerType.GetMethod("Validate", new[] { typeof(string) });
            if (validateMethod == null)
            {
                Console.Error.WriteLine("Error: Could not find Validate method in Compiler class.");
                SystemEnvironment.Exit(1);
                return;
            }
            
            var result = validateMethod.Invoke(compiler, new object[] { code });
            var resultType = result?.GetType();
            var successProperty = resultType?.GetProperty("Success");
            var errorMessageProperty = resultType?.GetProperty("ErrorMessage");
            var errorsProperty = resultType?.GetProperty("Errors");
            
            var success = (bool)(successProperty?.GetValue(result) ?? false);
            var errorMessage = errorMessageProperty?.GetValue(result) as string;
            var errors = errorsProperty?.GetValue(result) as System.Collections.IList;
            
            if (success)
            {
                Console.WriteLine("Validation successful: No syntax errors found.");
            }
            else
            {
                if (errors != null && errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        Console.Error.WriteLine($"Error: {error}");
                    }
                }
                else if (!string.IsNullOrEmpty(errorMessage))
                {
                    Console.Error.WriteLine($"Error: {errorMessage}");
                }
                else
                {
                    Console.Error.WriteLine("Validation failed: Syntax errors found.");
                }
                SystemEnvironment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during validation: {ex.Message}");
            SystemEnvironment.Exit(1);
        }
    }
    
    static void PrintSymbolsFromFile(string filePath)
    {
        try
        {
            // Resolve file path
            string fullPath;
            if (Path.IsPathRooted(filePath))
            {
                fullPath = Path.GetFullPath(filePath);
            }
            else
            {
                fullPath = Path.GetFullPath(Path.Combine(SystemEnvironment.CurrentDirectory, filePath));
            }
            
            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"Error: File not found: {fullPath}");
                SystemEnvironment.Exit(1);
                return;
            }
            
            // Read file
            var source = File.ReadAllText(fullPath);
            
            // Parse the source code
            var classes = new List<ClassDeclaration>();
            var functions = new List<FunctionDeclaration>();
            var actors = new List<ActorDeclaration>();
            var parseErrors = new List<Parser.ParseException>();
            
            try
            {
                var lexer = new Lexer(source, fullPath);
                var tokens = lexer.Tokenize();
                var parser = new MaldaLang.Parser.Parser(tokens, fullPath);
                var statements = parser.Parse();
                
                // Collect parse errors
                parseErrors.AddRange(parser.Errors);
                
                // Extract symbols from AST
                foreach (var stmt in statements)
                {
                    if (stmt is ClassDeclaration classDecl)
                    {
                        classes.Add(classDecl);
                    }
                    else if (stmt is FunctionDeclaration funcDecl)
                    {
                        functions.Add(funcDecl);
                    }
                    else if (stmt is ActorDeclaration actorDecl)
                    {
                        actors.Add(actorDecl);
                    }
                }
            }
            catch (Parser.ParseException ex)
            {
                parseErrors.Add(ex);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error parsing file: {ex.Message}");
                SystemEnvironment.Exit(1);
                return;
            }
            
            // Print results
            Console.WriteLine($"Symbols in {Path.GetFileName(fullPath)}:");
            Console.WriteLine();
            
            // Print parse errors if any
            if (parseErrors.Count > 0)
            {
                Console.WriteLine("⚠️  Parse Errors:");
                foreach (var error in parseErrors)
                {
                    Console.WriteLine($"  Line {error.Line}, Column {error.Column}: {error.Message}");
                }
                Console.WriteLine();
            }
            
            // Print classes
            if (classes.Count > 0)
            {
                Console.WriteLine($"📦 Classes ({classes.Count}):");
                foreach (var classDecl in classes)
                {
                    var superclassStr = classDecl.Superclass != null ? $" : {classDecl.Superclass}" : "";
                    Console.WriteLine($"  class {classDecl.Name}{superclassStr} (line {classDecl.Line})");
                    
                    if (classDecl.Members.Count > 0)
                    {
                        foreach (var member in classDecl.Members)
                        {
                            var staticStr = member.IsStatic ? "static " : "";
                            var accessStr = member.Access != AccessModifier.Public ? $"{member.Access.ToString().ToLower()} " : "";
                            
                            if (member.Type == MemberType.Method || member.Type == MemberType.Constructor)
                            {
                                if (member.Value is FunctionDeclaration memberFuncDecl)
                                {
                                    var paramsStr = string.Join(", ", memberFuncDecl.Parameters);
                                    var methodType = member.Type == MemberType.Constructor ? "constructor" : "method";
                                    Console.WriteLine($"    {accessStr}{staticStr}{methodType} {member.Name}({paramsStr}) (line {memberFuncDecl.Line})");
                                }
                                else
                                {
                                    Console.WriteLine($"    {accessStr}{staticStr}{member.Type.ToString().ToLower()} {member.Name}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"    {accessStr}{staticStr}{member.Type.ToString().ToLower()} {member.Name}");
                            }
                        }
                    }
                }
                Console.WriteLine();
            }
            
            // Print actors
            if (actors.Count > 0)
            {
                Console.WriteLine($"🎭 Actors ({actors.Count}):");
                foreach (var actorDecl in actors)
                {
                    Console.WriteLine($"  actor {actorDecl.Name} (line {actorDecl.Line})");
                    
                    if (actorDecl.Members.Count > 0)
                    {
                        foreach (var member in actorDecl.Members)
                        {
                            var staticStr = member.IsStatic ? "static " : "";
                            var accessStr = member.Access != AccessModifier.Public ? $"{member.Access.ToString().ToLower()} " : "";
                            
                            if (member.Type == MemberType.Method || member.Type == MemberType.Constructor)
                            {
                                if (member.Value is FunctionDeclaration memberFuncDecl)
                                {
                                    var paramsStr = string.Join(", ", memberFuncDecl.Parameters);
                                    var methodType = member.Type == MemberType.Constructor ? "constructor" : "method";
                                    Console.WriteLine($"    {accessStr}{staticStr}{methodType} {member.Name}({paramsStr}) (line {memberFuncDecl.Line})");
                                }
                                else
                                {
                                    Console.WriteLine($"    {accessStr}{staticStr}{member.Type.ToString().ToLower()} {member.Name}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"    {accessStr}{staticStr}{member.Type.ToString().ToLower()} {member.Name}");
                            }
                        }
                    }
                }
                Console.WriteLine();
            }
            
            // Print functions
            if (functions.Count > 0)
            {
                Console.WriteLine($"🔧 Functions ({functions.Count}):");
                foreach (var funcDecl in functions)
                {
                    var paramsStr = string.Join(", ", funcDecl.Parameters);
                    Console.WriteLine($"  function {funcDecl.Name}({paramsStr}) (line {funcDecl.Line})");
                }
                Console.WriteLine();
            }
            
            // Summary
            if (classes.Count == 0 && actors.Count == 0 && functions.Count == 0 && parseErrors.Count == 0)
            {
                Console.WriteLine("No symbols found in file.");
            }
            else
            {
                var totalSymbols = classes.Count + actors.Count + functions.Count;
                Console.WriteLine($"Total: {totalSymbols} symbol(s) found.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            SystemEnvironment.Exit(1);
        }
    }
    
    static bool TryParseCompilationMode(string modeInput, out string compilationModeStr, out bool isDllMode, out bool isJavaScriptMode, out bool isPwaMode, out bool isFullStackMode)
    {
        compilationModeStr = "Interpreter";
        isDllMode = false;
        isJavaScriptMode = false;
        isPwaMode = false;
        isFullStackMode = false;

        var normalized = modeInput.ToLowerInvariant();
        switch (normalized)
        {
            case "interpreter":
                compilationModeStr = "Interpreter";
                return true;
            case "transpile":
            case "transpiletocsharp":
                compilationModeStr = "TranspileToCSharp";
                return true;
            case "dll":
            case "transpiletodll":
                compilationModeStr = "TranspileToDll";
                isDllMode = true;
                return true;
            case "js":
            case "javascript":
            case "transpiletojavascript":
                compilationModeStr = "JavaScript";
                isJavaScriptMode = true;
                return true;
            case "pwa":
                compilationModeStr = "PWA";
                isPwaMode = true;
                return true;
            case "fullstack":
            case "full-stack":
                compilationModeStr = "FullStack";
                isFullStackMode = true;
                return true;
            default:
                return false;
        }
    }

    static bool TryParseCompilationTarget(string targetInput, out string compilationModeStr, out bool isDllMode, out bool isJavaScriptMode, out bool isPwaMode, out bool isFullStackMode)
    {
        compilationModeStr = "Interpreter";
        isDllMode = false;
        isJavaScriptMode = false;
        isPwaMode = false;
        isFullStackMode = false;

        var normalized = targetInput.ToLowerInvariant();
        if (normalized == "js" || normalized == "javascript")
        {
            compilationModeStr = "JavaScript";
            isJavaScriptMode = true;
            return true;
        }

        if (normalized == "pwa")
        {
            compilationModeStr = "PWA";
            isPwaMode = true;
            return true;
        }

        if (normalized == "fullstack" || normalized == "full-stack")
        {
            compilationModeStr = "FullStack";
            isFullStackMode = true;
            return true;
        }

        return false;
    }

    static string GetCompilationOutputType(string compilationModeStr)
    {
        return compilationModeStr switch
        {
            "TranspileToDll" => "DLL",
            "JavaScript" => "JavaScript distribution",
            "PWA" => "PWA",
            "FullStack" => "Full-stack distribution",
            _ => "Executable"
        };
    }

    static string GetDefaultPwaOutputDirectory(string inputPath)
    {
        var fileName = Path.GetFileName(inputPath);
        const string templateSuffix = ".malda.html";
        var baseName = fileName.EndsWith(templateSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^templateSuffix.Length]
            : Path.GetFileNameWithoutExtension(inputPath);
        var parentDirectory = Path.GetDirectoryName(inputPath);
        return string.IsNullOrWhiteSpace(parentDirectory)
            ? baseName
            : Path.Combine(parentDirectory, baseName);
    }

    static string NormalizePwaOutputPath(string outputPath)
    {
        if (outputPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            var parentDirectory = Path.GetDirectoryName(outputPath);
            var directoryName = Path.GetFileNameWithoutExtension(outputPath);
            return string.IsNullOrWhiteSpace(parentDirectory)
                ? directoryName
                : Path.Combine(parentDirectory, directoryName);
        }

        return outputPath;
    }

    static string GetDefaultFullStackOutputDirectory(string inputPath)
    {
        var baseDirectory = GetDefaultPwaOutputDirectory(inputPath);
        return baseDirectory + "-fullstack";
    }

    static string NormalizeFullStackOutputPath(string outputPath)
    {
        if (outputPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            outputPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            outputPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            var parentDirectory = Path.GetDirectoryName(outputPath);
            var directoryName = Path.GetFileNameWithoutExtension(outputPath);
            return string.IsNullOrWhiteSpace(parentDirectory)
                ? directoryName
                : Path.Combine(parentDirectory, directoryName);
        }

        return outputPath;
    }

    static bool TryConsumeProfilingArgument(string[] args, ref int index, CliProfilingSettings settings, bool writeToError = false)
    {
        void WriteMessage(string message)
        {
            if (writeToError)
            {
                Console.Error.WriteLine(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }

        var arg = args[index].ToLowerInvariant();
        if (arg == "--profile")
        {
            settings.Enabled = true;
            return true;
        }

        if (arg == "--profile-output")
        {
            if (index + 1 >= args.Length)
            {
                WriteMessage("Error: --profile-output requires a path");
                SystemEnvironment.Exit(1);
                return true;
            }

            settings.Enabled = true;
            settings.OutputPath = args[index + 1];
            index++;
            return true;
        }

        if (arg == "--profile-format")
        {
            if (index + 1 >= args.Length)
            {
                WriteMessage("Error: --profile-format requires text, json, or both");
                SystemEnvironment.Exit(1);
                return true;
            }

            var formatValue = args[index + 1].ToLowerInvariant();
            settings.Enabled = true;
            if (formatValue == "text")
            {
                settings.Format = ProfilingFormat.Text;
            }
            else if (formatValue == "json")
            {
                settings.Format = ProfilingFormat.Json;
            }
            else if (formatValue == "both")
            {
                settings.Format = ProfilingFormat.Both;
            }
            else
            {
                WriteMessage($"Error: Invalid profile format '{args[index + 1]}'. Use text, json, or both.");
                SystemEnvironment.Exit(1);
                return true;
            }
            index++;
            return true;
        }

        if (arg == "--profile-periodic-seconds")
        {
            if (index + 1 >= args.Length)
            {
                WriteMessage("Error: --profile-periodic-seconds requires a non-negative number");
                SystemEnvironment.Exit(1);
                return true;
            }

            if (!double.TryParse(args[index + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds < 0.0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                WriteMessage("Error: --profile-periodic-seconds must be a non-negative number");
                SystemEnvironment.Exit(1);
                return true;
            }

            settings.Enabled = true;
            settings.PeriodicSnapshotSeconds = seconds;
            index++;
            return true;
        }

        return false;
    }

    static ProfilingOptions? BuildProfilingOptions(CliProfilingSettings settings, bool writeToConsole = true)
    {
        if (!settings.Enabled)
        {
            return null;
        }

        return new ProfilingOptions
        {
            Enabled = true,
            OutputPath = settings.OutputPath,
            Format = settings.Format,
            WriteToConsole = writeToConsole,
            PeriodicSnapshotSeconds = settings.PeriodicSnapshotSeconds
        };
    }

    static CliRunOptions ParseCliRunOptions(string[] args, int startIndex, bool writeToError = false)
    {
        var settings = new CliProfilingSettings();
        var strictTypes = false;
        for (int i = startIndex; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--strict-types", StringComparison.OrdinalIgnoreCase))
            {
                strictTypes = true;
                continue;
            }

            if (TryConsumeProfilingArgument(args, ref i, settings, writeToError))
            {
                continue;
            }
        }

        return new CliRunOptions
        {
            Profiling = BuildProfilingOptions(settings),
            StrictTypes = strictTypes
        };
    }

    static void CompileFromCommandLine(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: malda -c <code> [-o <output.exe|output.dll|output.js|output-dir>] [--mode interpreter|transpile|dll|js|pwa|fullstack] [--target js|pwa|fullstack] [--include-ui-host] [--profile] [--profile-output <path>] [--profile-format text|json|both]");
            SystemEnvironment.Exit(1);
            return;
        }
        
        var code = args[1];
        string outputPath = Path.Combine(SystemEnvironment.CurrentDirectory, $"output_{DateTime.Now:yyyyMMdd_HHmmss}.exe");
        string compilationModeStr = "Interpreter";
        bool isDllMode = false;
        bool isJavaScriptMode = false;
        bool isPwaMode = false;
        bool isFullStackMode = false;
        bool outputPathExplicitlySet = false;
        bool includeUiHost = false;
        int typedTranspileLevel = 1;
        var profilingSettings = new CliProfilingSettings();
        
        // Parse command-line arguments
        for (int i = 2; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "-o" || arg == "--output")
            {
                if (i + 1 < args.Length)
                {
                    outputPath = args[i + 1];
                    outputPathExplicitlySet = true;
                    i++;
                }
                else
                {
                    Console.WriteLine("Error: -o option requires an output path");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (arg == "--mode" || arg == "-m")
            {
                if (i + 1 < args.Length)
                {
                    if (!TryParseCompilationMode(args[i + 1], out var parsedMode, out var parsedIsDllMode, out var parsedIsJavaScriptMode, out var parsedIsPwaMode, out var parsedIsFullStackMode))
                    {
                        Console.WriteLine($"Error: Invalid compilation mode '{args[i + 1]}'. Use 'interpreter', 'transpile', 'dll', 'js', 'pwa', or 'fullstack'");
                        SystemEnvironment.Exit(1);
                        return;
                    }
                    compilationModeStr = parsedMode;
                    isDllMode = parsedIsDllMode;
                    isJavaScriptMode = parsedIsJavaScriptMode;
                    isPwaMode = parsedIsPwaMode;
                    isFullStackMode = parsedIsFullStackMode;
                    i++;
                }
                else
                {
                    Console.WriteLine("Error: --mode option requires a mode value");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (arg == "--target")
            {
                if (i + 1 < args.Length)
                {
                    if (!TryParseCompilationTarget(args[i + 1], out var parsedMode, out var parsedIsDllMode, out var parsedIsJavaScriptMode, out var parsedIsPwaMode, out var parsedIsFullStackMode))
                    {
                        Console.WriteLine($"Error: Invalid target '{args[i + 1]}'. Use 'js', 'pwa', or 'fullstack'.");
                        SystemEnvironment.Exit(1);
                        return;
                    }
                    compilationModeStr = parsedMode;
                    isDllMode = parsedIsDllMode;
                    isJavaScriptMode = parsedIsJavaScriptMode;
                    isPwaMode = parsedIsPwaMode;
                    isFullStackMode = parsedIsFullStackMode;
                    i++;
                }
                else
                {
                    Console.WriteLine("Error: --target option requires a target value");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (arg == "--include-ui-host" || arg == "--with-ui-host")
            {
                includeUiHost = true;
            }
            else if (arg == "--typed-transpile-level")
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedLevel) && parsedLevel >= 0 && parsedLevel <= 2)
                {
                    typedTranspileLevel = parsedLevel;
                    i++;
                }
                else
                {
                    Console.WriteLine("Error: --typed-transpile-level requires an integer value: 0, 1, or 2");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (TryConsumeProfilingArgument(args, ref i, profilingSettings))
            {
            }
        }

        if (!outputPathExplicitlySet)
        {
            if (isDllMode)
            {
                outputPath = Path.ChangeExtension(outputPath, ".dll");
            }
            else if (isJavaScriptMode)
            {
                outputPath = Path.ChangeExtension(outputPath, ".js");
            }
            else if (isPwaMode)
            {
                var outputDirectory = Path.GetDirectoryName(outputPath);
                var outputDirectoryName = Path.GetFileNameWithoutExtension(outputPath);
                outputPath = string.IsNullOrWhiteSpace(outputDirectory)
                    ? outputDirectoryName
                    : Path.Combine(outputDirectory, outputDirectoryName);
            }
            else if (isFullStackMode)
            {
                var outputDirectory = Path.GetDirectoryName(outputPath);
                var outputDirectoryName = Path.GetFileNameWithoutExtension(outputPath);
                outputPath = string.IsNullOrWhiteSpace(outputDirectory)
                    ? outputDirectoryName
                    : Path.Combine(outputDirectory, outputDirectoryName);
            }
        }

        if (isPwaMode)
        {
            outputPath = NormalizePwaOutputPath(outputPath);
        }
        else if (isFullStackMode)
        {
            outputPath = NormalizeFullStackOutputPath(outputPath);
        }
        
        CompileFromSource(code, compilationModeStr, outputPath, includeUiHost, BuildProfilingOptions(profilingSettings), typedTranspileLevel);
    }
    
    static void CompileFromStdin(string code, string[] args)
    {
        string outputPath = Path.Combine(SystemEnvironment.CurrentDirectory, $"output_{DateTime.Now:yyyyMMdd_HHmmss}.exe");
        string compilationModeStr = "Interpreter";
        bool isDllMode = false;
        bool isJavaScriptMode = false;
        bool isPwaMode = false;
        bool isFullStackMode = false;
        bool outputPathExplicitlySet = false;
        bool includeUiHost = false;
        int typedTranspileLevel = 1;
        var profilingSettings = new CliProfilingSettings();
        
        // Parse command-line arguments (skip first if it's -c or --compile)
        int startIdx = 0;
        if (args.Length > 0 && (args[0].ToLower() == "-c" || args[0].ToLower() == "--compile"))
        {
            startIdx = 1;
        }
        
        for (int i = startIdx; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "-o" || arg == "--output")
            {
                if (i + 1 < args.Length)
                {
                    outputPath = args[i + 1];
                    outputPathExplicitlySet = true;
                    i++;
                }
            }
            else if (arg == "--mode" || arg == "-m")
            {
                if (i + 1 < args.Length)
                {
                    if (!TryParseCompilationMode(args[i + 1], out var parsedMode, out var parsedIsDllMode, out var parsedIsJavaScriptMode, out var parsedIsPwaMode, out var parsedIsFullStackMode))
                    {
                        Console.WriteLine($"Error: Invalid compilation mode '{args[i + 1]}'. Use 'interpreter', 'transpile', 'dll', 'js', 'pwa', or 'fullstack'");
                        SystemEnvironment.Exit(1);
                        return;
                    }
                    compilationModeStr = parsedMode;
                    isDllMode = parsedIsDllMode;
                    isJavaScriptMode = parsedIsJavaScriptMode;
                    isPwaMode = parsedIsPwaMode;
                    isFullStackMode = parsedIsFullStackMode;
                    i++;
                }
            }
            else if (arg == "--target")
            {
                if (i + 1 < args.Length)
                {
                    if (!TryParseCompilationTarget(args[i + 1], out var parsedMode, out var parsedIsDllMode, out var parsedIsJavaScriptMode, out var parsedIsPwaMode, out var parsedIsFullStackMode))
                    {
                        Console.WriteLine($"Error: Invalid target '{args[i + 1]}'. Use 'js', 'pwa', or 'fullstack'.");
                        SystemEnvironment.Exit(1);
                        return;
                    }
                    compilationModeStr = parsedMode;
                    isDllMode = parsedIsDllMode;
                    isJavaScriptMode = parsedIsJavaScriptMode;
                    isPwaMode = parsedIsPwaMode;
                    isFullStackMode = parsedIsFullStackMode;
                    i++;
                }
            }
            else if (arg == "--include-ui-host" || arg == "--with-ui-host")
            {
                includeUiHost = true;
            }
            else if (arg == "--typed-transpile-level")
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedLevel) && parsedLevel >= 0 && parsedLevel <= 2)
                {
                    typedTranspileLevel = parsedLevel;
                    i++;
                }
                else
                {
                    Console.WriteLine("Error: --typed-transpile-level requires an integer value: 0, 1, or 2");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (TryConsumeProfilingArgument(args, ref i, profilingSettings))
            {
            }
        }

        if (!outputPathExplicitlySet)
        {
            if (isDllMode)
            {
                outputPath = Path.ChangeExtension(outputPath, ".dll");
            }
            else if (isJavaScriptMode)
            {
                outputPath = Path.ChangeExtension(outputPath, ".js");
            }
            else if (isPwaMode)
            {
                var outputDirectory = Path.GetDirectoryName(outputPath);
                var outputDirectoryName = Path.GetFileNameWithoutExtension(outputPath);
                outputPath = string.IsNullOrWhiteSpace(outputDirectory)
                    ? outputDirectoryName
                    : Path.Combine(outputDirectory, outputDirectoryName);
            }
            else if (isFullStackMode)
            {
                var outputDirectory = Path.GetDirectoryName(outputPath);
                var outputDirectoryName = Path.GetFileNameWithoutExtension(outputPath);
                outputPath = string.IsNullOrWhiteSpace(outputDirectory)
                    ? outputDirectoryName
                    : Path.Combine(outputDirectory, outputDirectoryName);
            }
        }

        if (isPwaMode)
        {
            outputPath = NormalizePwaOutputPath(outputPath);
        }
        else if (isFullStackMode)
        {
            outputPath = NormalizeFullStackOutputPath(outputPath);
        }
        
        CompileFromSource(code, compilationModeStr, outputPath, includeUiHost, BuildProfilingOptions(profilingSettings), typedTranspileLevel);
    }

    static void CompileCommand(string[] args, bool forceTranspilePublish = false)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: malda compile|publish <input.malda|input.malda.html> [-o <output.exe|output.dll|output.js|output-dir>] [--mode interpreter|transpile|dll|js|pwa|fullstack] [--target js|pwa|fullstack] [--include-ui-host] [--embed-folder <dir[=alias]>] [--with-trading] [--typed-transpile-level 0|1|2] [--profile] [--profile-output <path>] [--profile-format text|json|both] [--profile-periodic-seconds N]");
            Console.WriteLine("  publish       - Alias for compile --mode transpile (executable publish layout)");
            Console.WriteLine("  --with-trading - Bundle MaldaLang.Timeseries, Trading.Core, Trading.Plugin, and Trading.Abstractions DLLs");
            Console.WriteLine("  input.malda    - Source file to compile (.malda.html supported in --mode js, --mode pwa, and --mode fullstack)");
            Console.WriteLine("  -o output      - Output path (defaults to input.exe, input.dll, input.js, or an input-named PWA directory)");
            Console.WriteLine("  --mode mode   - Compilation mode: 'interpreter' (default), 'transpile', 'dll', 'js', 'pwa', or 'fullstack'");
            Console.WriteLine("  --target pwa  - Alias for --mode pwa (PWA output is a directory)");
            Console.WriteLine("  --target js   - Alias for --mode js");
            Console.WriteLine("  --target fullstack - Alias for --mode fullstack (output is a deployable directory)");
            Console.WriteLine("  --include-ui-host - Force embedding UIHost runtime in transpiled executable");
            Console.WriteLine("  --embed-folder - Embed a directory as embed:<alias>/... (alias defaults to folder name; repeatable; path=alias optional)");
            Console.WriteLine("  --typed-transpile-level - 0=legacy dynamic transpile, 1=typed-safe (default), 2=typed-aggressive");
            Console.WriteLine("  --profile     - Enable MALDA profiling in the compiled executable");
            Console.WriteLine("  --profile-output - Write the profile report to a path");
            Console.WriteLine("  --profile-format - Report format: text, json, or both");
            Console.WriteLine("  --profile-periodic-seconds - While running, rewrite profile file(s) every N seconds (0 = end only)");
            SystemEnvironment.Exit(1);
            return;
        }

        var inputPath = args[1];
        string outputPath = Path.ChangeExtension(inputPath, ".exe");
        string compilationModeStr = "Interpreter"; // Store as string, convert to enum via reflection
        bool isDllMode = false;
        bool isJavaScriptMode = false;
        bool isPwaMode = false;
        bool isFullStackMode = false;
        bool outputPathExplicitlySet = false;
        bool includeUiHost = false;
        bool includeOptionalPacks = false;
        int typedTranspileLevel = 1;
        var embedFolderArgs = new List<string>();
        var profilingSettings = new CliProfilingSettings();

        if (forceTranspilePublish)
        {
            compilationModeStr = "TranspileToCSharp";
        }

        // Parse command-line arguments
        for (int i = 2; i < args.Length; i++)
        {
            var arg = args[i].ToLower();
            if (arg == "-o" || arg == "--output")
            {
                if (i + 1 < args.Length)
                {
                    outputPath = args[i + 1];
                    outputPathExplicitlySet = true;
                    i++; // Skip next argument as it's the output path
                }
                else
                {
                    Console.WriteLine("Error: -o option requires an output path");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (arg == "--mode" || arg == "-m")
            {
                if (i + 1 < args.Length)
                {
                    if (!TryParseCompilationMode(args[i + 1], out var parsedMode, out var parsedIsDllMode, out var parsedIsJavaScriptMode, out var parsedIsPwaMode, out var parsedIsFullStackMode))
                    {
                        Console.WriteLine($"Error: Invalid compilation mode '{args[i + 1]}'. Use 'interpreter', 'transpile', 'dll', 'js', 'pwa', or 'fullstack'");
                        SystemEnvironment.Exit(1);
                        return;
                    }
                    compilationModeStr = parsedMode;
                    isDllMode = parsedIsDllMode;
                    isJavaScriptMode = parsedIsJavaScriptMode;
                    isPwaMode = parsedIsPwaMode;
                    isFullStackMode = parsedIsFullStackMode;
                    i++; // Skip next argument as it's the mode value
                }
                else
                {
                    Console.WriteLine("Error: --mode option requires a mode value");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (arg == "--target")
            {
                if (i + 1 < args.Length)
                {
                    if (!TryParseCompilationTarget(args[i + 1], out var parsedMode, out var parsedIsDllMode, out var parsedIsJavaScriptMode, out var parsedIsPwaMode, out var parsedIsFullStackMode))
                    {
                        Console.WriteLine($"Error: Invalid target '{args[i + 1]}'. Use 'js', 'pwa', or 'fullstack'.");
                        SystemEnvironment.Exit(1);
                        return;
                    }
                    compilationModeStr = parsedMode;
                    isDllMode = parsedIsDllMode;
                    isJavaScriptMode = parsedIsJavaScriptMode;
                    isPwaMode = parsedIsPwaMode;
                    isFullStackMode = parsedIsFullStackMode;
                    i++;
                }
                else
                {
                    Console.WriteLine("Error: --target option requires a target value");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (arg == "--include-ui-host" || arg == "--with-ui-host")
            {
                includeUiHost = true;
            }
            else if (arg == "--embed-folder")
            {
                if (i + 1 < args.Length)
                {
                    embedFolderArgs.Add(args[i + 1]);
                    i++;
                }
                else
                {
                    Console.WriteLine("Error: --embed-folder requires a directory path (optional =alias)");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (arg == "--with-trading")
            {
                includeOptionalPacks = true;
            }
            else if (arg == "--typed-transpile-level")
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedLevel) && parsedLevel >= 0 && parsedLevel <= 2)
                {
                    typedTranspileLevel = parsedLevel;
                    i++;
                }
                else
                {
                    Console.WriteLine("Error: --typed-transpile-level requires an integer value: 0, 1, or 2");
                    SystemEnvironment.Exit(1);
                    return;
                }
            }
            else if (TryConsumeProfilingArgument(args, ref i, profilingSettings))
            {
            }
        }

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Error: Input file not found: {inputPath}");
            SystemEnvironment.Exit(1);
            return;
        }

        // Set default output extension based on mode
        if (!outputPathExplicitlySet && isDllMode)
        {
            outputPath = Path.ChangeExtension(inputPath, ".dll");
        }
        else if (!outputPathExplicitlySet && isJavaScriptMode)
        {
            outputPath = Path.ChangeExtension(inputPath, ".js");
        }
        else if (!outputPathExplicitlySet && isPwaMode)
        {
            outputPath = GetDefaultPwaOutputDirectory(inputPath);
        }
        else if (!outputPathExplicitlySet && isFullStackMode)
        {
            outputPath = GetDefaultFullStackOutputDirectory(inputPath);
        }

        if (isPwaMode)
        {
            outputPath = NormalizePwaOutputPath(outputPath);
        }
        else if (isFullStackMode)
        {
            outputPath = NormalizeFullStackOutputPath(outputPath);
        }

        Console.WriteLine($"Compiling {inputPath}...");
        Console.WriteLine($"Output: {outputPath}");
        Console.WriteLine($"Mode: {compilationModeStr}");
        if (includeUiHost)
        {
            Console.WriteLine("UI Host: forced embedded");
        }
        if (embedFolderArgs.Count > 0)
        {
            Console.WriteLine("Embed folders: " + string.Join(", ", embedFolderArgs));
        }
        Console.WriteLine($"Typed transpile level: {typedTranspileLevel}");
        var profilingOptions = BuildProfilingOptions(profilingSettings);

        // Try to load compiler dynamically to avoid circular dependency
        try
        {
            var compilerAssemblyPath = ResolveCompilerAssemblyPath();

            if (!File.Exists(compilerAssemblyPath))
            {
                Console.WriteLine("Error: Compiler not found. Please build MaldaLang.Compiler project first.");
                Console.WriteLine($"Expected at: {compilerAssemblyPath}");
                SystemEnvironment.Exit(1);
                return;
            }

            var assembly = Assembly.LoadFrom(compilerAssemblyPath);
            var compilerType = assembly.GetType("MaldaLang.Compiler.Compiler");
            if (compilerType == null)
            {
                Console.WriteLine("Error: Could not find Compiler class in MaldaLang.Compiler assembly.");
                SystemEnvironment.Exit(1);
                return;
            }

            // Get the CompilationMode enum type from the loaded assembly
            var compilationModeType = assembly.GetType("MaldaLang.Compiler.CompilationMode");
            object? compilationMode = null;
            if (compilationModeType != null)
            {
                // Parse the enum value from string
                compilationMode = Enum.Parse(compilationModeType, compilationModeStr);
            }

            var compiler = Activator.CreateInstance(compilerType);
            
            // Get the Compile method with the new signature (if compilationMode was loaded)
            MethodInfo? compileMethod = null;
            if (compilationModeType != null && compilationMode != null)
            {
                compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool), typeof(bool), typeof(ProfilingOptions), typeof(int), typeof(bool), typeof(string[]) });
                if (compileMethod == null)
                {
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool), typeof(bool), typeof(ProfilingOptions), typeof(int), typeof(bool) });
                }
                if (compileMethod == null)
                {
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool), typeof(bool), typeof(ProfilingOptions), typeof(int) });
                }
                if (compileMethod == null)
                {
                    // Backward-compatible overloads
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool), typeof(bool), typeof(ProfilingOptions) });
                }
                if (compileMethod == null)
                {
                    // Try 5-parameter version first (with includeLLamaSharp and includeUiHost)
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool), typeof(bool) });
                }
                if (compileMethod == null)
                {
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool) });
                }
                if (compileMethod == null)
                {
                    // Fallback to 3-parameter version
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType });
                }
            }
            
            if (compileMethod == null)
            {
                // Fallback to old signature for backward compatibility
                compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string) });
                if (compileMethod == null)
                {
                    Console.WriteLine("Error: Could not find Compile method in Compiler class.");
                    SystemEnvironment.Exit(1);
                    return;
                }
                // Use old method signature (defaults to Interpreter mode)
                var result = compileMethod.Invoke(compiler, new object[] { inputPath, outputPath });
                var resultType = result?.GetType();
                var successProperty = resultType?.GetProperty("Success");
                var outputPathProperty = resultType?.GetProperty("OutputPath");
                var errorMessageProperty = resultType?.GetProperty("ErrorMessage");

                var success = (bool)(successProperty?.GetValue(result) ?? false);
                var resultOutputPath = outputPathProperty?.GetValue(result) as string;
                var errorMessage = errorMessageProperty?.GetValue(result) as string;

                if (success)
                {
                    var outputType = GetCompilationOutputType(compilationModeStr);
                    Console.WriteLine($"Compilation successful! {outputType} saved to: {resultOutputPath}");
                }
                else
                {
                    Console.WriteLine($"Compilation failed: {errorMessage}");
                    SystemEnvironment.Exit(1);
                }
            }
            else
            {
                // Use new method signature with compilation mode
                object? result;
                var embedArgsArray = embedFolderArgs.Count > 0 ? embedFolderArgs.ToArray() : null;
                if (compileMethod!.GetParameters().Length == 9)
                {
                    result = compileMethod.Invoke(compiler, new object?[] { inputPath, outputPath, compilationMode!, false, includeUiHost, profilingOptions, typedTranspileLevel, includeOptionalPacks, embedArgsArray });
                }
                else if (compileMethod!.GetParameters().Length == 8)
                {
                    result = compileMethod.Invoke(compiler, new object?[] { inputPath, outputPath, compilationMode!, false, includeUiHost, profilingOptions, typedTranspileLevel, includeOptionalPacks });
                }
                else if (compileMethod!.GetParameters().Length == 7)
                {
                    result = compileMethod.Invoke(compiler, new object?[] { inputPath, outputPath, compilationMode!, false, includeUiHost, profilingOptions, typedTranspileLevel });
                }
                else if (compileMethod.GetParameters().Length == 6)
                {
                    result = compileMethod.Invoke(compiler, new object?[] { inputPath, outputPath, compilationMode!, false, includeUiHost, profilingOptions });
                }
                else if (compileMethod.GetParameters().Length == 5)
                {
                    result = compileMethod.Invoke(compiler, new object[] { inputPath, outputPath, compilationMode!, false, includeUiHost });
                }
                else if (compileMethod.GetParameters().Length == 4)
                {
                    // 4-parameter version: include includeLLamaSharp = false
                    result = compileMethod.Invoke(compiler, new object[] { inputPath, outputPath, compilationMode!, false });
                }
                else
                {
                    // 3-parameter version
                    result = compileMethod.Invoke(compiler, new object[] { inputPath, outputPath, compilationMode! });
                }
                var resultType = result?.GetType();
                var successProperty = resultType?.GetProperty("Success");
                var outputPathProperty = resultType?.GetProperty("OutputPath");
                var errorMessageProperty = resultType?.GetProperty("ErrorMessage");

                var success = (bool)(successProperty?.GetValue(result) ?? false);
                var resultOutputPath = outputPathProperty?.GetValue(result) as string;
                var errorMessage = errorMessageProperty?.GetValue(result) as string;

                if (success)
                {
                    var outputType = GetCompilationOutputType(compilationModeStr);
                    Console.WriteLine($"Compilation successful! {outputType} saved to: {resultOutputPath}");
                }
                else
                {
                    Console.WriteLine($"Compilation failed: {errorMessage}");
                    SystemEnvironment.Exit(1);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during compilation: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            SystemEnvironment.Exit(1);
        }
    }
    
    static void RunFile(string path, CliRunOptions? runOptions = null)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: File not found: {path}");
            SystemEnvironment.Exit(1);
            return;
        }

        var source = File.ReadAllText(path);
        try
        {
            Run(source, null, path, runOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(RuntimeDiagnostics.FormatForConsole(ex));
            SystemEnvironment.Exit(1);
        }
    }
    
    static string? GetAssistantScriptPath()
    {
        // 1. MALDA_AGENT_SCRIPT environment variable
        var envPath = System.Environment.GetEnvironmentVariable("MALDA_AGENT_SCRIPT");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;
        
        // 2. ~/.malda/assistant.malda
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            var userPath = Path.Combine(userProfile, ".malda", "assistant.malda");
            if (File.Exists(userPath))
                return userPath;
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

    static void RunAssistantWithChannel(string? channel)
    {
        var scriptPath = GetAssistantScriptPath();
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
    
    static int WorkflowCommand(string[] args)
    {
        static void PrintWorkflowUsage()
        {
            Console.Error.WriteLine("Usage: malda workflow <start|list|get|steps|events|metrics|resume|retry|cancel|approve|signal|dlq|maintenance> [options]");
            Console.Error.WriteLine("  malda workflow start <file.malda> <workflowName> [--input file.json]");
            Console.Error.WriteLine("  malda workflow list [--status ...] [--name ...] [--limit ...]");
            Console.Error.WriteLine("  malda workflow get <instanceId>");
            Console.Error.WriteLine("  malda workflow steps <instanceId>");
            Console.Error.WriteLine("  malda workflow events <instanceId> [--limit ...]");
            Console.Error.WriteLine("  malda workflow metrics");
            Console.Error.WriteLine("  malda workflow resume <instanceId>");
            Console.Error.WriteLine("  malda workflow retry <instanceId>");
            Console.Error.WriteLine("  malda workflow cancel <instanceId> [--reason \"...\"]");
            Console.Error.WriteLine("  malda workflow approve <instanceId> <stepId> [--decision approve|reject|timeout] [--payload file.json]");
            Console.Error.WriteLine("  malda workflow signal <instanceId> <signalName> [--payload file.json]");
            Console.Error.WriteLine("  malda workflow dlq list [--limit ...]");
            Console.Error.WriteLine("  malda workflow dlq requeue <deadLetterId> [--reason \"...\"] [--by \"operator\"] [--correlation-id \"...\"]");
            Console.Error.WriteLine("  malda workflow maintenance run [--operational-days ...] [--audit-days ...] [--compaction-days ...] [--batch ...] [--dry-run]");
            Console.Error.WriteLine("  global options: --format human|json, --json");
        }

        if (args.Length < 1)
        {
            PrintWorkflowUsage();
            return 1;
        }

        var normalizedArgs = new List<string>();
        var format = "human";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--json")
            {
                format = "json";
                continue;
            }
            if ((args[i] == "--format" || args[i] == "-f") && i + 1 < args.Length)
            {
                format = args[i + 1].ToLowerInvariant();
                i++;
                continue;
            }
            normalizedArgs.Add(args[i]);
        }

        if (format != "human" && format != "json")
        {
            Console.Error.WriteLine("Invalid format. Supported values: human, json.");
            return 4;
        }

        args = normalizedArgs.ToArray();
        if (args.Length < 1)
        {
            PrintWorkflowUsage();
            return 1;
        }

        var jsonMode = format == "json";
        var sub = args[0].ToLowerInvariant();
        var engine = WorkflowEngine.Instance;
        void WriteJson(object payload) => Console.WriteLine(JsonSerializer.Serialize(payload));

        try
        {
            if (sub == "start")
            {
                engine.EnsureWorkflowsEnabled("workflow start");
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: malda workflow start <file.malda> <workflowName> [--input file.json]");
                    return 1;
                }
                var filePath = args[1];
                var workflowName = args[2];
                var inputJson = "{}";
                for (var i = 3; i < args.Length; i++)
                {
                    if ((args[i] == "--input" || args[i] == "-i") && i + 1 < args.Length)
                    {
                        inputJson = File.ReadAllText(args[i + 1]);
                        i++;
                    }
                }
                if (!File.Exists(filePath))
                {
                    Console.Error.WriteLine($"File not found: {filePath}");
                    return 2;
                }
                var fileContent = File.ReadAllText(filePath);
                var escaped = inputJson.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
                var harness = jsonMode
                    ? fileContent + "\nvar __wfId = startWorkflow(\"" + workflowName + "\", parseJSON(\"" + escaped + "\"));\nprint(toJSON({\"instanceId\": __wfId}));"
                    : fileContent + "\nprint(startWorkflow(\"" + workflowName + "\", parseJSON(\"" + escaped + "\")));";
                Run(harness, null, filePath);
                return 0;
            }
            if (sub == "list")
            {
                var status = (string?)null;
                var name = (string?)null;
                var limit = 100;
                for (var i = 1; i < args.Length; i++)
                {
                    if ((args[i] == "--status" || args[i] == "-s") && i + 1 < args.Length) { status = args[i + 1]; i++; }
                    else if ((args[i] == "--name" || args[i] == "-n") && i + 1 < args.Length) { name = args[i + 1]; i++; }
                    else if ((args[i] == "--limit" || args[i] == "-l") && i + 1 < args.Length) { int.TryParse(args[i + 1], out limit); i++; }
                }
                var instances = engine.ListInstances(status, name, limit);
                if (jsonMode)
                    WriteJson(instances);
                else
                    foreach (var inst in instances)
                        Console.WriteLine($"{inst.Id}  {inst.Name}  {inst.Status}  {inst.CreatedAtUtc}");
                return 0;
            }
            if (sub == "get")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow get <instanceId>");
                    return 1;
                }
                var inst = engine.GetInstance(args[1]);
                if (inst == null)
                {
                    Console.Error.WriteLine($"Workflow instance not found: {args[1]}");
                    return 2;
                }
                if (jsonMode)
                    WriteJson(inst);
                else
                {
                    Console.WriteLine($"id: {inst.Id}");
                    Console.WriteLine($"name: {inst.Name}");
                    Console.WriteLine($"status: {inst.Status}");
                    Console.WriteLine($"created_at_utc: {inst.CreatedAtUtc}");
                    if (!string.IsNullOrWhiteSpace(inst.CorrelationId)) Console.WriteLine($"correlation_id: {inst.CorrelationId}");
                    if (inst.ResultJson != null) Console.WriteLine($"result: {inst.ResultJson}");
                    if (inst.ErrorJson != null) Console.WriteLine($"error: {inst.ErrorJson}");
                }
                return 0;
            }
            if (sub == "steps")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow steps <instanceId>");
                    return 1;
                }
                var steps = engine.GetSteps(args[1]);
                if (jsonMode)
                    WriteJson(steps);
                else
                    foreach (var s in steps)
                        Console.WriteLine($"{s.StepName}  {s.State}  attempt={s.Attempt}  output={s.OutputJson ?? s.ErrorJson ?? "-"}");
                return 0;
            }
            if (sub == "events")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow events <instanceId> [--limit ...]");
                    return 1;
                }

                var limit = 200;
                for (var i = 2; i < args.Length; i++)
                {
                    if ((args[i] == "--limit" || args[i] == "-l") && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out limit);
                        i++;
                    }
                }

                var events = engine.GetEvents(args[1], limit);
                if (jsonMode)
                {
                    WriteJson(events);
                }
                else
                {
                    foreach (var evt in events)
                        Console.WriteLine($"{evt.CreatedAtUtc}  {evt.EventType}  {evt.PayloadJson}");
                }
                return 0;
            }
            if (sub == "metrics")
            {
                var metrics = engine.GetMinimumMetricSnapshot();
                if (jsonMode)
                {
                    WriteJson(metrics);
                }
                else
                {
                    foreach (var kvp in metrics.OrderBy(k => k.Key, StringComparer.Ordinal))
                        Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                }
                return 0;
            }
            if (sub == "resume")
            {
                if (args.Length < 2) { Console.Error.WriteLine("Usage: malda workflow resume <instanceId>"); return 1; }
                var ok = engine.ResumeInstance(args[1]);
                if (jsonMode) WriteJson(new { success = ok, instanceId = args[1] });
                else Console.WriteLine(ok ? "Resumed" : "Not resumed (invalid state)");
                return ok ? 0 : 3;
            }
            if (sub == "retry")
            {
                if (args.Length < 2) { Console.Error.WriteLine("Usage: malda workflow retry <instanceId>"); return 1; }
                var ok = engine.RetryInstance(args[1]);
                if (jsonMode) WriteJson(new { success = ok, instanceId = args[1] });
                else Console.WriteLine(ok ? "Retry requested" : "Not retried (invalid state)");
                return ok ? 0 : 3;
            }
            if (sub == "cancel")
            {
                if (args.Length < 2) { Console.Error.WriteLine("Usage: malda workflow cancel <instanceId> [--reason \"...\"]"); return 1; }
                var reason = (args.Length >= 4 && args[2] == "--reason") ? args[3] : null;
                var ok = engine.CancelInstance(args[1], reason);
                if (jsonMode) WriteJson(new { success = ok, instanceId = args[1], reason });
                else Console.WriteLine(ok ? "Cancelled" : "Not cancelled (invalid state)");
                return ok ? 0 : 3;
            }
            if (sub == "approve")
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: malda workflow approve <instanceId> <stepId> [--decision approve|reject|timeout] [--payload file.json]");
                    return 1;
                }

                var instanceId = args[1];
                var stepId = args[2];
                var decision = "approve";
                string? payloadJson = null;

                for (var i = 3; i < args.Length; i++)
                {
                    if ((args[i] == "--decision" || args[i] == "-d") && i + 1 < args.Length)
                    {
                        decision = args[i + 1];
                        i++;
                    }
                    else if ((args[i] == "--payload" || args[i] == "-p") && i + 1 < args.Length)
                    {
                        payloadJson = File.ReadAllText(args[i + 1]);
                        i++;
                    }
                }

                if (!engine.ResolveApproval(instanceId, stepId, decision, payloadJson, out var approvalError))
                {
                    Console.Error.WriteLine(approvalError ?? "Approval resolution failed.");
                    if (!string.IsNullOrWhiteSpace(approvalError) && approvalError.Contains("not found", StringComparison.OrdinalIgnoreCase))
                        return 2;
                    if (!string.IsNullOrWhiteSpace(approvalError) && approvalError.Contains("WF1006", StringComparison.Ordinal))
                        return 3;
                    return 4;
                }

                if (jsonMode) WriteJson(new { success = true, instanceId, stepId, decision });
                else Console.WriteLine($"Approval '{stepId}' resolved with decision '{decision}'.");
                return 0;
            }
            if (sub == "signal")
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: malda workflow signal <instanceId> <signalName> [--payload file.json]");
                    return 1;
                }

                var instanceId = args[1];
                var signalName = args[2];
                string? payloadJson = null;
                for (var i = 3; i < args.Length; i++)
                {
                    if ((args[i] == "--payload" || args[i] == "-p") && i + 1 < args.Length)
                    {
                        payloadJson = File.ReadAllText(args[i + 1]);
                        i++;
                    }
                }

                if (!engine.DeliverSignal(instanceId, signalName, payloadJson, out var signalError))
                {
                    Console.Error.WriteLine(signalError ?? "Signal delivery failed.");
                    if (!string.IsNullOrWhiteSpace(signalError) && signalError.Contains("not found", StringComparison.OrdinalIgnoreCase))
                        return 2;
                    if (!string.IsNullOrWhiteSpace(signalError) && signalError.Contains("WF1006", StringComparison.Ordinal))
                        return 3;
                    return 4;
                }

                if (jsonMode) WriteJson(new { success = true, instanceId, signalName });
                else Console.WriteLine($"Signal '{signalName}' delivered.");
                return 0;
            }
            if (sub == "dlq")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: malda workflow dlq <list|requeue> [options]");
                    return 1;
                }

                var dlqSub = args[1].ToLowerInvariant();
                if (dlqSub == "list")
                {
                    var limit = 100;
                    var includeRequeued = true;
                    for (var i = 2; i < args.Length; i++)
                    {
                        if ((args[i] == "--limit" || args[i] == "-l") && i + 1 < args.Length)
                        {
                            int.TryParse(args[i + 1], out limit);
                            i++;
                        }
                        else if (args[i] == "--pending-only")
                        {
                            includeRequeued = false;
                        }
                    }
                    var deadLetters = engine.ListDeadLetters(limit, includeRequeued);
                    if (jsonMode)
                        WriteJson(deadLetters);
                    else
                        foreach (var dlq in deadLetters)
                            Console.WriteLine($"{dlq.Id}  wf={dlq.WorkflowInstanceId}  step={dlq.StepName}  reason={dlq.Reason}  requeued={dlq.RequeuedAtUtc ?? "-"}");
                    return 0;
                }

                if (dlqSub == "requeue")
                {
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("Usage: malda workflow dlq requeue <deadLetterId>");
                        return 1;
                    }
                    var deadLetterId = args[2];
                    string? reason = null;
                    string? requestedBy = null;
                    string? correlationId = null;
                    for (var i = 3; i < args.Length; i++)
                    {
                        if (args[i] == "--reason" && i + 1 < args.Length)
                        {
                            reason = args[i + 1];
                            i++;
                        }
                        else if (args[i] == "--by" && i + 1 < args.Length)
                        {
                            requestedBy = args[i + 1];
                            i++;
                        }
                        else if (args[i] == "--correlation-id" && i + 1 < args.Length)
                        {
                            correlationId = args[i + 1];
                            i++;
                        }
                    }

                    var ok = engine.RequeueDeadLetter(deadLetterId, reason, requestedBy, correlationId, out var requeueError);
                    if (!ok)
                    {
                        Console.Error.WriteLine(requeueError ?? $"Dead letter not found or already requeued: {deadLetterId}");
                        if (!string.IsNullOrWhiteSpace(requeueError) && requeueError.Contains("WF1006", StringComparison.Ordinal))
                            return 3;
                        return 2;
                    }
                    if (jsonMode) WriteJson(new { success = true, deadLetterId, reason, requestedBy, correlationId });
                    else Console.WriteLine($"Requeued dead letter '{deadLetterId}'.");
                    return 0;
                }

                Console.Error.WriteLine($"Unknown dlq subcommand: {dlqSub}");
                return 1;
            }
            if (sub == "maintenance")
            {
                if (args.Length < 2 || args[1].ToLowerInvariant() != "run")
                {
                    Console.Error.WriteLine("Usage: malda workflow maintenance run [--operational-days ...] [--audit-days ...] [--compaction-days ...] [--batch ...] [--dry-run]");
                    return 1;
                }

                var runtimeOptions = engine.GetRuntimeOptions();
                var maintenance = new WorkflowMaintenanceOptions
                {
                    OperationalRetentionDays = runtimeOptions.OperationalRetentionDays,
                    AuditRetentionDays = runtimeOptions.AuditRetentionDays,
                    CompactionRetentionDays = runtimeOptions.CompactionRetentionDays,
                    CleanupBatchSize = runtimeOptions.CleanupBatchSize
                };

                for (var i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--operational-days" && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out var parsed);
                        maintenance.OperationalRetentionDays = parsed;
                        i++;
                    }
                    else if (args[i] == "--audit-days" && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out var parsed);
                        maintenance.AuditRetentionDays = parsed;
                        i++;
                    }
                    else if (args[i] == "--compaction-days" && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out var parsed);
                        maintenance.CompactionRetentionDays = parsed;
                        i++;
                    }
                    else if (args[i] == "--batch" && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out var parsed);
                        maintenance.CleanupBatchSize = parsed;
                        i++;
                    }
                    else if (args[i] == "--dry-run")
                    {
                        maintenance.DryRun = true;
                    }
                }

                maintenance.ValidateOrThrow();
                var report = engine.RunMaintenanceJob(maintenance);
                if (jsonMode)
                {
                    WriteJson(report);
                }
                else
                {
                    Console.WriteLine($"maintenance_id: {report.MaintenanceId}");
                    Console.WriteLine($"dry_run: {report.DryRun}");
                    Console.WriteLine($"archived_instances: {report.ArchivedInstances}");
                    Console.WriteLine($"deleted_steps: {report.DeletedSteps}");
                    Console.WriteLine($"deleted_events: {report.DeletedEvents}");
                    Console.WriteLine($"deleted_dead_letters: {report.DeletedDeadLetters}");
                    Console.WriteLine($"compacted_steps: {report.CompactedSteps}");
                }
                return 0;
            }
            Console.Error.WriteLine($"Unknown workflow subcommand: {sub}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
    
    static void CompileFromSource(string source, string compilationModeStr, string? outputPath = null, bool includeUiHost = false, ProfilingOptions? profilingOptions = null, int typedTranspileLevel = 1)
    {
        // Create temporary source file
        var tempSourceFile = Path.Combine(Path.GetTempPath(), $"malda_prompt_{Guid.NewGuid()}.malda");
        if (outputPath == null)
        {
            if (compilationModeStr == "PWA")
            {
                outputPath = Path.Combine(SystemEnvironment.CurrentDirectory, $"output_{DateTime.Now:yyyyMMdd_HHmmss}");
            }
            else
            {
                var extension = compilationModeStr switch
                {
                    "TranspileToDll" => ".dll",
                    "JavaScript" => ".js",
                    _ => ".exe"
                };
                outputPath = Path.Combine(SystemEnvironment.CurrentDirectory, $"output_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
            }
        }

        if (compilationModeStr == "PWA")
        {
            outputPath = NormalizePwaOutputPath(outputPath);
        }
        
        try
        {
            // Write source to temp file
            File.WriteAllText(tempSourceFile, source);
            
            Console.WriteLine($"Compiling to {outputPath}...");
            Console.WriteLine($"Mode: {compilationModeStr}");
            
            // Load compiler dynamically (same as CompileCommand)
            var compilerAssemblyPath = ResolveCompilerAssemblyPath();
            
            if (!File.Exists(compilerAssemblyPath))
            {
                Console.Error.WriteLine("Error: Compiler not found. Please build MaldaLang.Compiler project first.");
                SystemEnvironment.Exit(1);
                return;
            }
            
            var assembly = Assembly.LoadFrom(compilerAssemblyPath);
            var compilerType = assembly.GetType("MaldaLang.Compiler.Compiler");
            if (compilerType == null)
            {
                Console.Error.WriteLine("Error: Could not find Compiler class in MaldaLang.Compiler assembly.");
                SystemEnvironment.Exit(1);
                return;
            }
            
            // Get the CompilationMode enum type
            var compilationModeType = assembly.GetType("MaldaLang.Compiler.CompilationMode");
            object? compilationMode = null;
            if (compilationModeType != null)
            {
                compilationMode = Enum.Parse(compilationModeType, compilationModeStr);
            }
            
            var compiler = Activator.CreateInstance(compilerType);
            
            // Get the Compile method
            MethodInfo? compileMethod = null;
            if (compilationModeType != null && compilationMode != null)
            {
                compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool), typeof(bool), typeof(ProfilingOptions), typeof(int) });
                if (compileMethod == null)
                {
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool), typeof(bool), typeof(ProfilingOptions) });
                }
                if (compileMethod == null)
                {
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool), typeof(bool) });
                }
                if (compileMethod == null)
                {
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType, typeof(bool) });
                }
                if (compileMethod == null)
                {
                    compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string), compilationModeType });
                }
            }
            
            if (compileMethod == null)
            {
                compileMethod = compilerType.GetMethod("Compile", new[] { typeof(string), typeof(string) });
                if (compileMethod == null)
                {
                    Console.Error.WriteLine("Error: Could not find Compile method in Compiler class.");
                    SystemEnvironment.Exit(1);
                    return;
                }
                var result = compileMethod.Invoke(compiler, new object[] { tempSourceFile, outputPath });
                var resultType = result?.GetType();
                var successProperty = resultType?.GetProperty("Success");
                var outputPathProperty = resultType?.GetProperty("OutputPath");
                var errorMessageProperty = resultType?.GetProperty("ErrorMessage");
                
                var success = (bool)(successProperty?.GetValue(result) ?? false);
                var resultOutputPath = outputPathProperty?.GetValue(result) as string;
                var errorMessage = errorMessageProperty?.GetValue(result) as string;
                
                if (success)
                {
                    var outputType = GetCompilationOutputType(compilationModeStr);
                    Console.WriteLine($"Compilation successful! {outputType} saved to: {resultOutputPath}");
                }
                else
                {
                    Console.Error.WriteLine($"Compilation failed: {errorMessage}");
                    SystemEnvironment.Exit(1);
                }
            }
            else
            {
                object? result;
                if (compileMethod!.GetParameters().Length == 7)
                {
                    result = compileMethod.Invoke(compiler, new object?[] { tempSourceFile, outputPath, compilationMode!, false, includeUiHost, profilingOptions, typedTranspileLevel });
                }
                else if (compileMethod.GetParameters().Length == 6)
                {
                    result = compileMethod.Invoke(compiler, new object?[] { tempSourceFile, outputPath, compilationMode!, false, includeUiHost, profilingOptions });
                }
                else if (compileMethod.GetParameters().Length == 5)
                {
                    result = compileMethod.Invoke(compiler, new object[] { tempSourceFile, outputPath, compilationMode!, false, includeUiHost });
                }
                else if (compileMethod.GetParameters().Length == 4)
                {
                    result = compileMethod.Invoke(compiler, new object[] { tempSourceFile, outputPath, compilationMode!, false });
                }
                else
                {
                    result = compileMethod.Invoke(compiler, new object[] { tempSourceFile, outputPath, compilationMode! });
                }
                var resultType = result?.GetType();
                var successProperty = resultType?.GetProperty("Success");
                var outputPathProperty = resultType?.GetProperty("OutputPath");
                var errorMessageProperty = resultType?.GetProperty("ErrorMessage");
                
                var success = (bool)(successProperty?.GetValue(result) ?? false);
                var resultOutputPath = outputPathProperty?.GetValue(result) as string;
                var errorMessage = errorMessageProperty?.GetValue(result) as string;
                
                if (success)
                {
                    var outputType = GetCompilationOutputType(compilationModeStr);
                    Console.WriteLine($"Compilation successful! {outputType} saved to: {resultOutputPath}");
                }
                else
                {
                    Console.Error.WriteLine($"Compilation failed: {errorMessage}");
                    SystemEnvironment.Exit(1);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during compilation: {ex.Message}");
            SystemEnvironment.Exit(1);
        }
        finally
        {
            // Clean up temp file
            try
            {
                if (File.Exists(tempSourceFile))
                    File.Delete(tempSourceFile);
            }
            catch { }
        }
    }
    
    static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("MALDA CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  malda <file.malda> [--strict-types] [--profile ...]");
        Console.WriteLine("  malda <command> [options]");
        Console.WriteLine("  malda -e \"<code>\" [--strict-types] | malda -c \"<code>\" | malda --check \"<code>\"");
        Console.WriteLine("  --strict-types                Reject unknown type hints, non-exhaustive sum-type match, and type-hint mismatches (literals + known identifiers)");
        Console.WriteLine("  echo \"<code>\" | malda");
        Console.WriteLine();
        Console.WriteLine("Getting started:");
        Console.WriteLine("  doctor      Fast diagnostics for runtime, config, providers, and project scaffold");
        Console.WriteLine("  onboard     Create ~/.malda, starter config, optional model downloads");
        Console.WriteLine("  status      Show MALDA home, config, provider, and cron status");
        Console.WriteLine("  new         Scaffold a webapi or fullstack project");
        Console.WriteLine();
        Console.WriteLine("Build, test, and ship:");
        Console.WriteLine("  compile     Compile a MALDA file to exe, dll, js, pwa, or fullstack output");
        Console.WriteLine("  test        Discover and run MALDA tests");
        Console.WriteLine("  db          Inspect, migrate, seed, and roll back scaffolded local-first SQLite state");
        Console.WriteLine("  deploy      Validate deploy, profile, and observability contracts");
        Console.WriteLine("  workflow    Start and inspect durable workflows");
        Console.WriteLine();
        Console.WriteLine("AI and automation:");
        Console.WriteLine("  agent       Chat with the MALDA assistant or run a one-shot prompt");
        Console.WriteLine("  gateway     Long-running Telegram gateway with optional in-process cron");
        Console.WriteLine("  memory      Inspect and maintain GraphMemory artifacts");
        Console.WriteLine("  cron        Add, list, remove, or install scheduled jobs");
        Console.WriteLine();
        Console.WriteLine("Packages and diagnostics:");
        Console.WriteLine("  init        Initialize package metadata");
        Console.WriteLine("  install     Install a package");
        Console.WriteLine("  uninstall   Remove a package");
        Console.WriteLine("  list        List installed packages");
        Console.WriteLine("  search      Search published packages");
        Console.WriteLine("  trace       Summarize, inspect, or replay trace files");
        Console.WriteLine("  symbols     Print classes, functions, and actors from a MALDA file");
        Console.WriteLine("  help        Show top-level help or help for a specific command");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  malda doctor");
        Console.WriteLine("  malda new webapi my-api");
        Console.WriteLine("  malda new fullstack sales-portal --local-first");
        Console.WriteLine("  malda db status");
        Console.WriteLine("  malda compile app.malda -o app.exe");
        Console.WriteLine("  malda test tests --filter Smoke");
        Console.WriteLine("  malda workflow list");
        Console.WriteLine("  malda agent -m \"Summarize this file\"");
        Console.WriteLine("  malda help deploy");
        Console.WriteLine();
        Console.WriteLine("REPL commands:");
        Console.WriteLine("  run | compile | transpile | help | exit");
        Console.WriteLine();
        Console.WriteLine("Use 'malda help <command>' for command-specific usage.");
        Console.WriteLine();
    }

    static bool TryShowCommandHelp(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "doctor":
                DoctorCommandRunner.PrintUsage(Console.Out);
                return true;
            case "compile":
                ShowCompileHelp();
                return true;
            case "test":
                ShowTestHelp();
                return true;
            case "db":
                DbCommandRunner.PrintUsage(Console.Out);
                return true;
            case "new":
                NewCommandOptionsParser.WriteUsage(Console.Out);
                return true;
            case "deploy":
                _ = new DeployCommandRunner().Run(new[] { "--help" }, Console.Out, Console.Error);
                return true;
            case "trace":
                ShowTraceHelp();
                return true;
            case "cron":
                ShowCronHelp();
                return true;
            case "workflow":
                ShowWorkflowHelp();
                return true;
            case "agent":
                ShowAgentHelp();
                return true;
            case "gateway":
                ShowGatewayHelp();
                return true;
            case "memory":
                ShowMemoryHelp();
                return true;
            case "status":
                ShowStatusHelp();
                return true;
            case "onboard":
                ShowOnboardHelp();
                return true;
            case "symbols":
                ShowSymbolsHelp();
                return true;
            case "install":
            case "uninstall":
            case "list":
            case "search":
            case "init":
                ShowPackageManagerHelp();
                return true;
            default:
                return false;
        }
    }

    static bool IsHelpFlag(string value)
    {
        return value == "--help" || value == "-h";
    }

    static void ShowCompileHelp()
    {
        Console.WriteLine("Usage: malda compile|publish <input.malda|input.malda.html> [-o <output.exe|output.dll|output.js|output-dir>] [--mode interpreter|transpile|dll|js|pwa|fullstack] [--target js|pwa|fullstack] [--include-ui-host] [--embed-folder <dir[=alias]>] [--with-trading] [--profile] [--profile-output <path>] [--profile-format text|json|both] [--profile-periodic-seconds N]");
        Console.WriteLine("  publish                       Alias for compile --mode transpile (executable publish layout)");
        Console.WriteLine("  -o <path>                     Output executable, DLL, JS file, or PWA directory");
        Console.WriteLine("  --mode <mode>                 interpreter (default), transpile, dll, js, pwa, or fullstack");
        Console.WriteLine("  --target <js|pwa|fullstack>   Alias for --mode js, --mode pwa, or --mode fullstack");
        Console.WriteLine("  --include-ui-host             Force embedding UIHost runtime in transpiled executable");
        Console.WriteLine("  --embed-folder <dir[=alias]>  Embed a directory as embed:<alias>/... (repeatable)");
        Console.WriteLine("  --with-trading                Bundle optional timeseries and trading pack DLLs beside the executable");
        Console.WriteLine("  --profile                     Enable MALDA profiling in the compiled executable");
        Console.WriteLine("  --profile-output <path>       Write the profile report to a file path");
        Console.WriteLine("  --profile-format <format>     text, json, or both");
        Console.WriteLine("  --profile-periodic-seconds N  Rewrite profile output every N seconds while running (0 = only at exit)");
    }

    static void ShowTestHelp()
    {
        Console.WriteLine("Usage: malda test [path] [--list] [--filter text] [--format human|ci] [--iterations N] [--seed S] [--write-regression] [--regression-dir <path>]");
        Console.WriteLine("  path                          Root directory or test file to discover from (default: current directory)");
        Console.WriteLine("  --list                        Print discovered tests without executing them");
        Console.WriteLine("  --filter <text>               Restrict discovery to matching file names");
        Console.WriteLine("  --format <human|ci>           Human-readable or CI-oriented output");
        Console.WriteLine("  --iterations <N>              Property-test iterations");
        Console.WriteLine("  --seed <S>                    Property-test seed");
        Console.WriteLine("  --write-regression            Write regression artifacts for failing properties");
        Console.WriteLine("  --regression-dir <path>       Output directory for regression artifacts");
    }

    static void ShowTraceHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  malda trace summary <traceFile>");
        Console.WriteLine("  malda trace show <traceFile> [--from N] [--to M] [--type TYPE]");
        Console.WriteLine("  malda trace replay <traceFile> [--output <directory>]");
    }

    static void ShowCronHelp()
    {
        Console.WriteLine("Usage: malda cron <add|list|remove|install> [options]");
        Console.WriteLine("  add      Add a scheduled MALDA job (--scope defaults to cron:<name>)");
        Console.WriteLine("  list     List stored cron jobs");
        Console.WriteLine("  remove   Remove a stored cron job");
        Console.WriteLine("  install  Install jobs with the OS scheduler");
    }

    static void ShowGatewayHelp()
    {
        Console.WriteLine("Usage: malda gateway [stop] [-c telegram] [--no-cron]");
        Console.WriteLine("  stop                   Stop a running gateway (removes ~/.malda/gateway.pid)");
        Console.WriteLine("  -c, --channel <name>   Channel adapter (default: telegram)");
        Console.WriteLine("  --no-cron              Disable in-process cron scheduler");
        Console.WriteLine("  Runs the assistant over Telegram until Ctrl+C. Writes ~/.malda/gateway.pid while active.");
        Console.WriteLine("  Set channels.telegram.notifyChatId (or MALDA_GATEWAY_NOTIFY_CHAT_ID) for cron/crash Telegram alerts.");
    }

    static void ShowWorkflowHelp()
    {
        Console.WriteLine("Usage: malda workflow <start|list|get|steps|events|metrics|resume|retry|cancel|approve|signal|dlq|maintenance> [options]");
        Console.WriteLine("  start       Start a workflow instance from a MALDA file");
        Console.WriteLine("  list        List workflow instances");
        Console.WriteLine("  get         Show workflow instance details");
        Console.WriteLine("  steps       Show persisted step state");
        Console.WriteLine("  events      Show workflow events");
        Console.WriteLine("  metrics     Show workflow metrics");
        Console.WriteLine("  resume      Resume a paused workflow");
        Console.WriteLine("  retry       Retry a workflow instance");
        Console.WriteLine("  cancel      Cancel a workflow instance");
        Console.WriteLine("  approve     Resolve a human approval step");
        Console.WriteLine("  signal      Send a signal to a workflow");
        Console.WriteLine("  dlq         Inspect or requeue dead letters");
        Console.WriteLine("  maintenance Run workflow maintenance tasks");
    }

    static void ShowAgentHelp()
    {
        Console.WriteLine("Usage: malda agent [-m <message>] [-c <channel>] [-b <backend>]");
        Console.WriteLine("  -m, --message <text>          Send a one-shot message instead of starting chat mode");
        Console.WriteLine("  -c, --channel <name>          Route through a channel such as telegram");
        Console.WriteLine("  -b, --backend <name>          Override the assistant backend (for example local-llama)");
    }

    static void ShowMemoryHelp()
    {
        Console.WriteLine("Usage: malda memory <stats|validate|reindex|prune|reflect|export-bundle|watch|download-rerank> [options]");
        Console.WriteLine("  --path <path>                 Memory base path (default: ~/.malda/memory/assistant)");
        Console.WriteLine("  --json                        Output JSON");
        Console.WriteLine("  stats                         Show memory statistics");
        Console.WriteLine("  validate                      Health check (exit 1 if issues found)");
        Console.WriteLine("  reindex --dir <dir> --pattern <glob> [--scope <scope>]");
        Console.WriteLine("  prune [--type <type>] [--scope <scope>] [--older-than-days <n>] [--consolidated]");
        Console.WriteLine("  reflect [--scope <scope>] [--dry-run] [--min-confidence <0..1>]");
        Console.WriteLine("  export-bundle [-o <path>]");
        Console.WriteLine("  watch --dir <dir> [--pattern <glob>]   Reindex on KB changes");
        Console.WriteLine("  download-rerank               Download ONNX cross-encoder to ~/.malda/models/cross-encoder");
    }

    static void ShowStatusHelp()
    {
        Console.WriteLine("Usage: malda status [--json]");
        Console.WriteLine("  Show MALDA home, config, channels, skills, gateway, memory stats, and cron jobs.");
        Console.WriteLine("  --json                 Machine-readable JSON output");
    }

    static void ShowOnboardHelp()
    {
        Console.WriteLine("Usage: malda onboard [--download-rerank] [--download-local-llama]");
        Console.WriteLine("  Create ~/.malda, skills/, memory/, and a starter config.json.");
        Console.WriteLine("  --download-rerank        Download ONNX cross-encoder for agents.memory.rerankMode onnx");
        Console.WriteLine("  --download-local-llama   Download default GGUF model and set providers.local_llama.modelPath");
    }

    static void ShowSymbolsHelp()
    {
        Console.WriteLine("Usage: malda --symbols <file.malda>");
        Console.WriteLine("  Print classes, functions, actors, and other top-level symbols from a MALDA file.");
    }
    
    static void RunPrompt()
    {
        var version = GetVersion();
        Console.WriteLine("MALDA (Multi Agent Language with Development Automation) Interpreter");
        if (!string.IsNullOrEmpty(version))
        {
            Console.WriteLine($"Version {version}");
        }
        Console.WriteLine("You can enter multiline code - the interpreter will continue reading until you type 'run', 'compile', or 'transpile'");
        Console.WriteLine("Type 'exit' to quit, 'run' to execute, 'compile' or 'transpile' to build executable, 'help' for help");
        Console.WriteLine("(c) 2026 - Andrea Maldini");
        while (true)
        {
            var result = ReadMultilineInput();
            if (result == null || result.Action == "exit")
                break;
            if (string.IsNullOrWhiteSpace(result.Code))
                continue;
            try
            {
                if (result.Action == "help")
                {
                    ShowHelp();
                }
                else if (result.Action == "run")
                {
                    Run(result.Code);
                }
                else if (result.Action == "compile")
                {
                    CompileFromSource(result.Code, "Interpreter");
                }
                else if (result.Action == "transpile")
                {
                    CompileFromSource(result.Code, "TranspileToCSharp");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
    
    static InputResult? ReadMultilineInput()
    {
        Console.Write("> ");
        var firstLine = Console.ReadLine();
        if (firstLine == null) return null;
        
        var trimmedFirst = firstLine.Trim().ToLower();
        
        // Check if first line is a command
        if (trimmedFirst == "exit")
            return new InputResult { Code = null, Action = "exit" };
        if (trimmedFirst == "help")
            return new InputResult { Code = null, Action = "help" };
        
        // Check if we need multiline input
        if (!NeedsMoreInput(firstLine))
        {
            // Single line, execute immediately
            return new InputResult { Code = firstLine, Action = "run" };
        }
        
        // Start multiline collection - continue until "run", "compile", or "transpile" is entered
        var sb = new StringBuilder(firstLine);
        
        while (true)
        {
            Console.Write("..> ");
            var line = Console.ReadLine();
            if (line == null) return null;
            
            var trimmed = line.Trim().ToLower();
            
            // Check if user wants to run, compile, transpile, or show help
            if (trimmed == "run")
            {
                return new InputResult { Code = sb.ToString(), Action = "run" };
            }
            else if (trimmed == "compile")
            {
                return new InputResult { Code = sb.ToString(), Action = "compile" };
            }
            else if (trimmed == "transpile")
            {
                return new InputResult { Code = sb.ToString(), Action = "transpile" };
            }
            else if (trimmed == "help")
            {
                return new InputResult { Code = null, Action = "help" };
            }
            
            // Allow user to cancel with empty line
            if (string.IsNullOrWhiteSpace(line))
                return null;
                
            sb.AppendLine();
            sb.Append(line);
        }
    }
    
    static bool NeedsMoreInput(string code)
    {
        var trimmed = code.TrimEnd();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;
        
        // Heuristic: check for incomplete patterns
        // Ends with operators or opening brackets
        if (trimmed.EndsWith("{") || trimmed.EndsWith("(") || 
            trimmed.EndsWith("[") || trimmed.EndsWith("+") ||
            trimmed.EndsWith("-") || trimmed.EndsWith("*") ||
            trimmed.EndsWith("/") || trimmed.EndsWith("=") ||
            trimmed.EndsWith("&&") || trimmed.EndsWith("||") ||
            trimmed.EndsWith(",") || trimmed.EndsWith("."))
            return true;
        
        // Check for incomplete keywords (heuristic)
        var lastWord = trimmed.Split(new[] { ' ', '\t', '\n', '\r', '(', '{', '[' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (lastWord != null)
        {
            var incompleteKeywords = new[] { "if", "for", "while", "function", "fn", "def", "class", "else", "elif" };
            if (incompleteKeywords.Contains(lastWord.ToLower()))
                return true;
        }
        
        // Try parsing - if it fails with "unexpected end" or similar, need more
        try
        {
            var lexer = new Lexer(code);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            parser.Parse();
            return false; // Parsed successfully, input is complete
        }
        catch (Exception ex)
        {
            // If error suggests incomplete input, continue
            var errorMsg = ex.Message.ToLower();
            if (errorMsg.Contains("unexpected end") || 
                errorMsg.Contains("expected") && (errorMsg.Contains("token") || errorMsg.Contains("character")))
            {
                // Check if it's a real syntax error or just incomplete
                // If we have tokens, it might be incomplete
                try
                {
                    var lexer = new Lexer(code);
                    var tokens = lexer.Tokenize();
                    if (tokens.Count > 0)
                        return true; // Have tokens but parse failed, likely incomplete
                }
                catch
                {
                    // Lexer error, probably incomplete
                    return true;
                }
            }
            return false; // Other error, let it propagate to Run()
        }
    }
    
    static bool UsesUiFramework(string source)
    {
        // In-memory ui.state / ui.setState (HttpServer components) does not need UIHost.
        // Only the browser UI protocol (mount / events) requires the embedded host.
        return source.Contains("ui.mount(", StringComparison.Ordinal) ||
               source.Contains("ui.mountEnvelope(", StringComparison.Ordinal) ||
               source.Contains("uiMount(", StringComparison.Ordinal) ||
               source.Contains("uiMountEnvelope(", StringComparison.Ordinal) ||
               source.Contains("ui.render(", StringComparison.Ordinal) ||
               source.Contains("uiRender(", StringComparison.Ordinal) ||
               source.Contains("ui.dispatchEvent(", StringComparison.Ordinal) ||
               source.Contains("uiDispatchEvent(", StringComparison.Ordinal) ||
               source.Contains("ui.pullEvent(", StringComparison.Ordinal) ||
               source.Contains("uiPullEvent(", StringComparison.Ordinal);
    }

    static void Run(string source, Interpreter.Interpreter? interpreter = null, string? sourceFileName = null, CliRunOptions? runOptions = null)
    {
        if (UsesUiFramework(source))
        {
            EmbeddedUiHostRuntime.TryStartAsync().GetAwaiter().GetResult();
        }

        var lexer = new Lexer(source, sourceFileName);
        var tokens = lexer.Tokenize();
        
        var parser = new Parser.Parser(tokens, sourceFileName);
        var statements = parser.Parse();

        // Declaration() catches ParseException, records it, and returns null — so Parse() can
        // finish with an empty statement list and zero thrown exceptions. Without this check a
        // program like `print("hi")` (missing semicolon) exits 0 with no output.
        if (parser.Errors.Count > 0)
        {
            throw parser.Errors.Count == 1
                ? parser.Errors[0]
                : new Parser.ParseException(string.Join(System.Environment.NewLine, parser.Errors.Select(e => e.Message)));
        }

        if (runOptions?.StrictTypes == true)
        {
            var strictDiagnostics = new List<Diagnostic>();
            StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, strictDiagnostics, sourceFileName);
            if (StrictTypesAnalysis.HasErrors(strictDiagnostics))
            {
                Console.Error.WriteLine(StrictTypesAnalysis.FormatErrorsForConsole(strictDiagnostics));
                SystemEnvironment.Exit(1);
                return;
            }
        }
        
        var interp = interpreter ?? new Interpreter.Interpreter(currentFile: sourceFileName);
        interp.SetSourceCode(source);
        // For console environment, we can use GetAwaiter().GetResult() since there's no async context
        MaldaProfiler.StartSession(runOptions?.Profiling, sourceFileName ?? "inline");
        try
        {
            interp.InterpretAsync(statements).GetAwaiter().GetResult();
        }
        finally
        {
            MaldaProfiler.CompleteSession();
        }
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
    
    static async Task PackageManagerCommand(string[] args)
    {
        if (args.Length < 1)
        {
            ShowPackageManagerHelp();
            return;
        }
        
        var command = args[0].ToLower();
        var pm = new MaldaLang.PackageManager.PackageManager();
        
        switch (command)
        {
            case "install":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: malda install <package>[@<version>]");
                    SystemEnvironment.Exit(1);
                    return;
                }
                
                var packageSpec = args[1];
                string? packageName = null;
                string? version = null;
                
                if (packageSpec.Contains("@"))
                {
                    var parts = packageSpec.Split('@', 2);
                    packageName = parts[0];
                    version = parts[1];
                }
                else
                {
                    packageName = packageSpec;
                }
                
                var success = await pm.InstallAsync(packageName, version);
                SystemEnvironment.Exit(success ? 0 : 1);
                break;
                
            case "uninstall":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: malda uninstall <package>[@<version>]");
                    SystemEnvironment.Exit(1);
                    return;
                }
                
                var uninstallSpec = args[1];
                string? uninstallPackageName = null;
                string? uninstallVersion = null;
                
                if (uninstallSpec.Contains("@"))
                {
                    var parts = uninstallSpec.Split('@', 2);
                    uninstallPackageName = parts[0];
                    uninstallVersion = parts[1];
                }
                else
                {
                    uninstallPackageName = uninstallSpec;
                }
                
                var uninstallSuccess = pm.Uninstall(uninstallPackageName, uninstallVersion);
                SystemEnvironment.Exit(uninstallSuccess ? 0 : 1);
                break;
                
            case "list":
                pm.List();
                break;
                
            case "search":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: malda search <query>");
                    SystemEnvironment.Exit(1);
                    return;
                }
                
                var query = args[1];
                var results = await pm.SearchAsync(query);
                if (results == null || results.Count == 0)
                {
                    Console.WriteLine("No packages found");
                }
                else
                {
                    var count = results.Count;
                    Console.WriteLine($"Found {count} package(s):");
                    foreach (var pkg in results)
                    {
                        Console.WriteLine($"  {pkg.Name}@{pkg.Version} - {pkg.Description ?? "No description"}");
                    }
                }
                break;
                
            case "init":
                var dir = args.Length > 1 ? args[1] : null;
                var initSuccess = pm.Init(dir);
                SystemEnvironment.Exit(initSuccess ? 0 : 1);
                break;
                
            default:
                ShowPackageManagerHelp();
                SystemEnvironment.Exit(1);
                break;
        }
    }
    
    static void ShowPackageManagerHelp()
    {
        Console.WriteLine("Package Manager Commands:");
        Console.WriteLine("  malda install <package>[@<version>]  - Install a package");
        Console.WriteLine("  malda uninstall <package>[@<version>] - Uninstall a package");
        Console.WriteLine("  malda list                          - List installed packages");
        Console.WriteLine("  malda search <query>                - Search for packages");
        Console.WriteLine("  malda init [directory]              - Initialize package.json");
    }

    static void NewCommand(string[] args)
    {
        if (!NewCommandOptionsParser.TryParse(args, Console.Error, out var options) || options == null)
        {
            SystemEnvironment.Exit(1);
            return;
        }

        var scaffolder = new TemplateScaffolder();
        var code = scaffolder.Scaffold(options.TemplateName, options.DestinationPath, Console.Out, Console.Error, options);
        SystemEnvironment.Exit(code);
    }
}
