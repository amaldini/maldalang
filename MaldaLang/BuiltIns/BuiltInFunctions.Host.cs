// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInGetMaldaHome(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getMaldaHome", args, 0, 0);
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return RuntimeValue.String("");
        var maldaHome = Path.Combine(userProfile, ".malda");
        return RuntimeValue.String(maldaHome);
    }

    /// <summary>
    /// Directory of the running .malda script (interpreter) or of the compiled executable
    /// (<see cref="AppContext.BaseDirectory"/>). Useful for bundling assets next to the program.
    /// </summary>
    private static RuntimeValue BuiltInGetProgramDirectory(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("getProgramDirectory", args, 0, 0);

        // Prefer a real on-disk source script (interpreter). Ignore placeholders such as
        // TranspiledBuiltinRuntime's currentFile "transpiled", which would otherwise resolve
        // to the process CWD and miss assets placed beside the .exe (e.g. BGE-M3 GGUF).
        var currentFile = interpreter?.GetCurrentFile();
        if (!string.IsNullOrWhiteSpace(currentFile))
        {
            try
            {
                var full = Path.GetFullPath(currentFile);
                if (File.Exists(full))
                {
                    var dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir))
                        return RuntimeValue.String(dir);
                }
            }
            catch
            {
                // Fall through to executable directory.
            }
        }

        try
        {
            var processPath = System.Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var exeDir = Path.GetDirectoryName(Path.GetFullPath(processPath));
                if (!string.IsNullOrEmpty(exeDir))
                    return RuntimeValue.String(exeDir);
            }
        }
        catch
        {
            // Fall through to AppContext.BaseDirectory.
        }

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            try
            {
                return RuntimeValue.String(Path.GetFullPath(baseDir));
            }
            catch
            {
                return RuntimeValue.String(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        return RuntimeValue.String(Directory.GetCurrentDirectory());
    }

    private static RuntimeValue BuiltInGetAssistantMemory(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("getAssistantMemory", args, 0, 1, "path?");
        if (interpreter == null)
            throw new MaldaLang.Interpreter.RuntimeException("getAssistantMemory() requires interpreter context");
        string? path = null;
        if (args.Count >= 1 && args[0].Type == MaldaLang.Interpreter.ValueType.String)
            path = args[0].AsString();
        var memory = GraphMemoryBootstrap.CreateAssistantMemory(interpreter, path);
        return RuntimeValue.Object(memory);
    }
    
    private static RuntimeValue? TryLoadMaldaConfigFile(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            return JsonToRuntimeValue(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static RuntimeValue BuiltInGetMaldaConfig(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getMaldaConfig", args, 0, 0);
        // 1. Explicit path (MALDA_CONFIG)
        var configOverride = System.Environment.GetEnvironmentVariable("MALDA_CONFIG");
        if (!string.IsNullOrWhiteSpace(configOverride))
        {
            var fromEnv = TryLoadMaldaConfigFile(configOverride);
            if (fromEnv != null)
                return fromEnv;
        }

        // 2. ./.malda/config.json in current directory, then parent directories (project root when cwd is a subfolder)
        var dir = System.Environment.CurrentDirectory;
        for (var depth = 0; depth < 12 && !string.IsNullOrEmpty(dir); depth++)
        {
            var localPath = Path.Combine(dir, ".malda", "config.json");
            var fromLocal = TryLoadMaldaConfigFile(localPath);
            if (fromLocal != null)
                return fromLocal;

            var parent = Directory.GetParent(dir);
            if (parent == null)
                break;
            dir = parent.FullName;
        }

        // 3. ~/.malda/config.json
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            var userPath = Path.Combine(userProfile, ".malda", "config.json");
            var fromUser = TryLoadMaldaConfigFile(userPath);
            if (fromUser != null)
                return fromUser;
        }

        return RuntimeValue.Null();
    }
    
    private static async Task<RuntimeValue> BuiltInSleepAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        if (args.Count != 1)
            throw new Exception("sleep() expects 1 argument: (milliseconds)");
        
        if (!NumericCoercion.TryAsInteger(args[0], out var milliseconds))
            throw new Exception("sleep() milliseconds must be an integer");

        if (milliseconds < 0)
            throw new Exception("sleep() milliseconds must be non-negative");
        
        // Trigger output update before sleeping (similar to input)
        interpreter?.TriggerOutputUpdate();

        WithinBoundsContext.EnsureWithinBound();
        await Task.Delay(milliseconds);
        WithinBoundsContext.EnsureWithinBound();
        return RuntimeValue.Null();
    }
    
    private static async Task<RuntimeValue> BuiltInInputAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        BuiltInArity.Require("input", args, 0, 1, "prompt?");
        string prompt = "";
        if (args.Count > 0)
        {
            if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
                throw new Exception("input() prompt must be a string");
            prompt = args[0].AsString();
        }
        
        // If we have an input provider (web environment), use it
        var inputProvider = interpreter.GetInputProvider();
        
        if (inputProvider != null)
        {
            // Try to get input synchronously from queue first
            var hasQueued = inputProvider.HasQueuedInput();
            
            if (hasQueued)
            {
                var queuedInput = inputProvider.GetQueuedInput();
                return RuntimeValue.String(queuedInput);
            }
            // No queued input available - await it directly
            var input = await inputProvider.GetInputAsync(prompt);
            return RuntimeValue.String(input ?? "");
        }
        
        // Fallback for console environment
        Console.Write(prompt);
        var consoleInput = Console.ReadLine() ?? "";
        return RuntimeValue.String(consoleInput);
    }
    
    private static string? ResolveEnvironmentVariable(string varName)
    {
        // Try Process environment first (most specific)
        var value = System.Environment.GetEnvironmentVariable(varName);

        // If not found, try User environment (Windows-specific)
        if (value == null && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
            value = System.Environment.GetEnvironmentVariable(varName, System.EnvironmentVariableTarget.User);

        // If still not found, try Machine environment (Windows-specific)
        if (value == null && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
            value = System.Environment.GetEnvironmentVariable(varName, System.EnvironmentVariableTarget.Machine);

        return value;
    }

    /// <summary>Clears the process-lifetime <c>getEnv</c> cache so tests can change environment variables between runs.</summary>
    public static void ClearGetEnvCacheForTesting() => GetEnvCache.Clear();

    private static RuntimeValue BuiltInGetEnv(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("getEnv() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("getEnv() expects a string argument");

        var varName = args[0].AsString();
        var value = GetEnvCache.GetOrAdd(varName, static n => ResolveEnvironmentVariable(n));
        return value == null ? RuntimeValue.Null() : RuntimeValue.String(value);
    }

    /// <summary>
    /// Like <c>getEnv</c>, but never returns null — missing variables yield the optional default
    /// (empty string when omitted). Safe to pass straight into <c>str.trim</c>.
    /// </summary>
    private static RuntimeValue BuiltInGetEnvOr(List<RuntimeValue> args)
    {
        BuiltInArity.Require("getEnvOr", args, 1, 2, "name, default?");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("getEnvOr() expects a string name");

        var varName = args[0].AsString();
        var value = GetEnvCache.GetOrAdd(varName, static n => ResolveEnvironmentVariable(n));
        if (value != null)
            return RuntimeValue.String(value);

        if (args.Count < 2 || args[1].Type == MaldaLang.Interpreter.ValueType.Null)
            return RuntimeValue.String("");
        return RuntimeValue.String(args[1].ToString());
    }

    private static RuntimeValue BuiltInGetHostPlatform(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new Exception("getHostPlatform() expects 0 arguments");

        var obj = new JsonObject();
        obj.Set("os", RuntimeValue.String(AgentPlatformContext.OsFamily));
        obj.Set("description", RuntimeValue.String(System.Runtime.InteropServices.RuntimeInformation.OSDescription));
        obj.Set("arch", RuntimeValue.String(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()));
        obj.Set("pathSeparator", RuntimeValue.String(Path.DirectorySeparatorChar.ToString()));
        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue BuiltInGetCommandLineArgs(List<RuntimeValue> args)
    {
        if (args.Count != 0) throw new Exception("getCommandLineArgs() expects 0 arguments");

        return RuntimeValue.Array(
            System.Environment.GetCommandLineArgs()
                .Skip(1)
                .Select(RuntimeValue.String)
                .ToList()
        );
    }
    
    private static RuntimeValue BuiltInHasEnv(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("hasEnv() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("hasEnv() expects a string argument");
        
        var varName = args[0].AsString();
        
        // Try Process environment first (most specific)
        var value = System.Environment.GetEnvironmentVariable(varName);
        
        // If not found, try User environment (Windows-specific)
        if (value == null && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            value = System.Environment.GetEnvironmentVariable(varName, EnvironmentVariableTarget.User);
        }
        
        // If still not found, try Machine environment (Windows-specific)
        if (value == null && System.Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            value = System.Environment.GetEnvironmentVariable(varName, EnvironmentVariableTarget.Machine);
        }
        
        return RuntimeValue.Boolean(value != null);
    }
}
