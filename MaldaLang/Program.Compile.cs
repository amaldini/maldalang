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
            case "esm":
            case "js-module":
                compilationModeStr = "JavaScriptModule";
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

        if (normalized == "esm")
        {
            compilationModeStr = "JavaScriptModule";
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

    /// <summary><c>--mode esm</c> writes an ES module, <c>.mjs</c> so Node loads it as one.</summary>
    static string JavaScriptOutputExtension(string compilationModeStr) =>
        compilationModeStr == "JavaScriptModule" ? ".mjs" : ".js";

    static string GetCompilationOutputType(string compilationModeStr)
    {
        return compilationModeStr switch
        {
            "TranspileToDll" => "DLL",
            "JavaScript" => "JavaScript distribution",
            "JavaScriptModule" => "JavaScript module",
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
        bool strictTypes = false;
        bool lenientTypes = false;
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
            else if (TryParseCompileTypeFlags(arg, ref strictTypes, ref lenientTypes, out var typeFlagConflict))
            {
                if (typeFlagConflict)
                {
                    Console.Error.WriteLine("Error: --strict-types and --lenient-types cannot be combined.");
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
                outputPath = Path.ChangeExtension(outputPath, JavaScriptOutputExtension(compilationModeStr));
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
        
        CompileFromSource(code, compilationModeStr, outputPath, includeUiHost, BuildProfilingOptions(profilingSettings), typedTranspileLevel, strictTypes, lenientTypes);
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
        bool strictTypes = false;
        bool lenientTypes = false;
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
            else if (TryParseCompileTypeFlags(arg, ref strictTypes, ref lenientTypes, out var stdinTypeFlagConflict))
            {
                if (stdinTypeFlagConflict)
                {
                    Console.Error.WriteLine("Error: --strict-types and --lenient-types cannot be combined.");
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
                outputPath = Path.ChangeExtension(outputPath, JavaScriptOutputExtension(compilationModeStr));
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
        
        CompileFromSource(code, compilationModeStr, outputPath, includeUiHost, BuildProfilingOptions(profilingSettings), typedTranspileLevel, strictTypes, lenientTypes);
    }

    static void CompileCommand(string[] args, bool forceTranspilePublish = false)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: malda compile|publish <input.malda|input.malda.html> [-o <output.exe|output.dll|output.js|output-dir>] [--mode interpreter|transpile|dll|js|pwa|fullstack] [--target js|pwa|fullstack] [--include-ui-host] [--minify] [--embed-folder <dir[=alias]>] [--with-trading] [--typed-transpile-level 0|1|2] [--strict-types] [--lenient-types] [--profile] [--profile-output <path>] [--profile-format text|json|both] [--profile-periodic-seconds N]");
            Console.WriteLine("  publish       - Alias for compile --mode transpile (executable publish layout)");
            Console.WriteLine("  --with-trading - Bundle MaldaLang.Timeseries, Trading.Core, Trading.Plugin, and Trading.Abstractions DLLs");
            Console.WriteLine("  input.malda    - Source file to compile (.malda.html supported in --mode js, --mode pwa, and --mode fullstack)");
            Console.WriteLine("  -o output      - Output path (defaults to input.exe, input.dll, input.js, or an input-named PWA directory)");
            Console.WriteLine("  --mode mode   - Compilation mode: 'interpreter' (default), 'transpile', 'dll', 'js', 'pwa', or 'fullstack'");
            Console.WriteLine("  --target pwa  - Alias for --mode pwa (PWA output is a directory)");
            Console.WriteLine("  --target js   - Alias for --mode js");
            Console.WriteLine("  --target fullstack - Alias for --mode fullstack (output is a deployable directory)");
            Console.WriteLine("  --include-ui-host - Force embedding UIHost runtime in transpiled executable");
            Console.WriteLine("  --minify - js / esm / pwa: minify the output with esbuild when installed (MALDA_ESBUILD, PATH or node_modules), else strip comments from the runtime");
            Console.WriteLine("  --embed-folder - Embed a directory as embed:<alias>/... (alias defaults to folder name; repeatable; path=alias optional)");
            Console.WriteLine("  --typed-transpile-level - 0=legacy dynamic transpile, 1=typed-safe (default), 2=typed-aggressive");
            Console.WriteLine("  --strict-types - Refuse emit when type/match/@pure/bounds analysis has Errors (default for --mode transpile / dll)");
            Console.WriteLine("  --lenient-types - Skip that analysis (interpret-style emit). Do not combine with --strict-types");
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
        var minifyJavaScript = false;
        int typedTranspileLevel = 1;
        bool strictTypes = false;
        bool lenientTypes = false;
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
            else if (arg == "--minify")
            {
                minifyJavaScript = true;
            }
            else if (TryParseCompileTypeFlags(arg, ref strictTypes, ref lenientTypes, out var typeFlagConflict))
            {
                if (typeFlagConflict)
                {
                    Console.Error.WriteLine("Error: --strict-types and --lenient-types cannot be combined.");
                    SystemEnvironment.Exit(1);
                    return;
                }
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

        RejectCompileOnStrictTypes(File.ReadAllText(inputPath), inputPath, compilationModeStr, strictTypes, lenientTypes);

        // Set default output extension based on mode
        if (!outputPathExplicitlySet && isDllMode)
        {
            outputPath = Path.ChangeExtension(inputPath, ".dll");
        }
        else if (!outputPathExplicitlySet && isJavaScriptMode)
        {
            outputPath = Path.ChangeExtension(inputPath, JavaScriptOutputExtension(compilationModeStr));
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
            if (minifyJavaScript)
                compilerType.GetProperty("MinifyJavaScript")?.SetValue(compiler, true);
            
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
                    if (resultType?.GetProperty("Report")?.GetValue(result) is string { Length: > 0 } report)
                        Console.WriteLine(report);
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
                    if (resultType?.GetProperty("Report")?.GetValue(result) is string { Length: > 0 } report)
                        Console.WriteLine(report);
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

    static void CompileFromSource(string source, string compilationModeStr, string? outputPath = null, bool includeUiHost = false, ProfilingOptions? profilingOptions = null, int typedTranspileLevel = 1, bool strictTypes = false, bool lenientTypes = false)
    {
        RejectCompileOnStrictTypes(source, null, compilationModeStr, strictTypes, lenientTypes);

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
                    "JavaScriptModule" => ".mjs",
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
                    if (resultType?.GetProperty("Report")?.GetValue(result) is string { Length: > 0 } report)
                        Console.WriteLine(report);
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
                    if (resultType?.GetProperty("Report")?.GetValue(result) is string { Length: > 0 } report)
                        Console.WriteLine(report);
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

    static bool TryParseCompileTypeFlags(string arg, ref bool strictTypes, ref bool lenientTypes, out bool conflict)
    {
        conflict = false;
        if (arg == "--strict-types")
        {
            if (lenientTypes)
                conflict = true;
            strictTypes = true;
            return true;
        }

        if (arg == "--lenient-types")
        {
            if (strictTypes)
                conflict = true;
            lenientTypes = true;
            return true;
        }

        return false;
    }

    static void RejectCompileOnStrictTypes(string source, string? sourceFileName, string compilationModeStr, bool strictTypes, bool lenientTypes)
    {
        if (strictTypes && lenientTypes)
        {
            Console.Error.WriteLine("Error: --strict-types and --lenient-types cannot be combined.");
            SystemEnvironment.Exit(1);
            return;
        }

        if (!CompileStrictTypesGate.ShouldAnalyze(compilationModeStr, strictTypes, lenientTypes))
            return;

        if (CompileStrictTypesGate.TryGetRejection(source, sourceFileName, out var errorText))
        {
            Console.Error.WriteLine(errorText);
            SystemEnvironment.Exit(1);
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
               source.Contains("uiPullEvent(", StringComparison.Ordinal) ||
               source.Contains("ui.run(", StringComparison.Ordinal);
    }
}
