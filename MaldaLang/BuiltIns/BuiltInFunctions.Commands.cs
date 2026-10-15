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
    private static RuntimeValue BuiltInCreateRunCommandTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createRunCommandTool", args, 0, 2, "workingDir?, shellCap?");
        var workingDir = "";
        CapabilityToken? shellCap = null;
        if (args.Count == 1)
        {
            if (CapStdLib.TryGetToken(args[0], out _))
                shellCap = CapStdLib.RequireToken(args[0], CapabilityToken.KindShell, "createRunCommandTool");
            else if (args[0].Type == ValueType.String)
                workingDir = args[0].AsString();
        }
        else if (args.Count == 2)
        {
            if (args[0].Type == ValueType.String)
                workingDir = args[0].AsString();
            if (args[1].Type != ValueType.Null)
                shellCap = CapStdLib.RequireToken(args[1], CapabilityToken.KindShell, "createRunCommandTool");
        }
        return BuiltInTools.CreateRunCommandTool(workingDir, shellCap);
    }
    
    private static Dictionary<string, string>? ExtractCommandEnvironment(RuntimeValue envValue)
    {
        if (envValue.Type != ValueType.Object)
            throw new Exception("runCommand() environment must be an object");

        if (envValue.AsObject() is not JsonObject jsonObject)
            throw new Exception("runCommand() environment must be a plain object");

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in jsonObject.GetProperties())
        {
            if (value.Type == ValueType.Null)
                continue;
            environment[key] = value.Type == ValueType.String ? value.AsString() : value.ToString() ?? "";
        }

        return environment.Count == 0 ? null : environment;
    }

    private static void ApplyCommandEnvironment(System.Diagnostics.ProcessStartInfo startInfo, Dictionary<string, string>? extraEnvironment)
    {
        if (extraEnvironment == null)
            return;

        foreach (var (key, value) in extraEnvironment)
            startInfo.Environment[key] = value;
    }

    // Command execution function
    private static RuntimeValue BuiltInRunCommand(List<RuntimeValue> args)
    {
        if (args.Count == 0)
            throw new Exception("runCommand() expects at least 1 argument: command (string)");

        if (CapStdLib.TryGetToken(args[0], out _))
            return CapStdLib.Run(args, interpreter: null);

        if (args[0].Type != ValueType.String)
            throw new Exception("runCommand() expects at least 1 argument: command (string)");

        Dictionary<string, string>? extraEnvironment = null;
        if (args.Count > 1 && args[^1].Type == ValueType.Object)
        {
            extraEnvironment = ExtractCommandEnvironment(args[^1]);
            args = args.Take(args.Count - 1).ToList();
        }
        
        var command = args[0].AsString();
        if (string.IsNullOrWhiteSpace(command))
            throw new Exception("runCommand() command cannot be empty");
        
        // Safety check: blocked commands and shell wrappers
        var commandName = Path.GetFileNameWithoutExtension(command).ToLowerInvariant();
        if (CommandApprovalPolicy.DeniedAlwaysCommands.Contains(commandName))
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String($"Error: Command '{commandName}' is not allowed for security reasons"));
            return RuntimeValue.Object(errorObj);
        }

        if (CommandApprovalPolicy.ShellWrapperCommands.Contains(commandName) && !CommandExecutionContext.IsUserApproved)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String(
                $"Error: Command '{commandName}' requires user approval. The run_command tool will prompt when an interactive UI is available."));
            return RuntimeValue.Object(errorObj);
        }
        
        // Safety check: Validate command path if it's an absolute path
        if (Path.IsPathRooted(command))
        {
            try
            {
                var normalizedCommandPath = Path.GetFullPath(command);
                
                // Check for path traversal attempts
                if (command.Contains("..") || command.Contains("~"))
                {
                    var errorObj = new JsonObject();
                    errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                    errorObj.Set("stdout", RuntimeValue.String(""));
                    errorObj.Set("stderr", RuntimeValue.String($"Error: Command path contains suspicious characters (path traversal attempt)"));
                    return RuntimeValue.Object(errorObj);
                }
                
                // Check if the file exists (for absolute paths)
                if (!File.Exists(normalizedCommandPath))
                {
                    var errorObj = new JsonObject();
                    errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                    errorObj.Set("stdout", RuntimeValue.String(""));
                    errorObj.Set("stderr", RuntimeValue.String($"Error: Command executable not found: '{normalizedCommandPath}'"));
                    return RuntimeValue.Object(errorObj);
                }
            }
            catch (Exception ex)
            {
                var errorObj = new JsonObject();
                errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                errorObj.Set("stdout", RuntimeValue.String(""));
                errorObj.Set("stderr", RuntimeValue.String($"Error: Invalid command path: {ex.Message}"));
                return RuntimeValue.Object(errorObj);
            }
        }
        
        // Parse optional arguments
        List<string>? commandArgs = null;
        string? workingDirectory = null;
        int? timeoutMs = null;
        var detached = false;
        
        if (args.Count > 1 && args[1].Type == ValueType.Array)
        {
            var argsArray = args[1].AsArray();
            commandArgs = new List<string>();
            foreach (var arg in argsArray)
            {
                if (arg.Type == ValueType.String)
                    commandArgs.Add(arg.AsString());
            }
        }
        
        if (args.Count > 2 && args[2].Type == ValueType.String)
        {
            workingDirectory = args[2].AsString();
        }
        
        if (args.Count > 3 && args[3].Type == ValueType.Integer)
        {
            timeoutMs = (int)args[3].AsInteger();
            if (timeoutMs <= 0)
                timeoutMs = null;
        }
        else if (args.Count > 3 && args[3].Type == ValueType.Boolean)
        {
            detached = args[3].AsBoolean();
        }

        if (args.Count > 4 && args[4].Type == ValueType.Boolean)
        {
            detached = args[4].AsBoolean();
        }

        timeoutMs = RunCommandShellHelper.ResolveTimeoutMs(command, timeoutMs);
        
        var shellError = RunCommandShellHelper.ValidateAndNormalize(command, commandArgs);
        if (shellError != null)
            return shellError;
        
        // Safety check: Maximum timeout limit (1 hour = 3600000ms)
        const int maxTimeoutMs = 3600000;
        if (timeoutMs.HasValue && timeoutMs.Value > maxTimeoutMs)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String($"Error: Timeout cannot exceed {maxTimeoutMs}ms (1 hour)"));
            return RuntimeValue.Object(errorObj);
        }

        if (detached && timeoutMs.HasValue)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String("Error: timeout is not supported in detached mode"));
            return RuntimeValue.Object(errorObj);
        }
        
        // Safety check: Validate and normalize working directory
        string finalWorkingDirectory;
        try
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                // Resolve to absolute path to prevent path traversal
                finalWorkingDirectory = Path.GetFullPath(workingDirectory);
                
                // Verify the directory exists
                if (!Directory.Exists(finalWorkingDirectory))
                {
                    var errorObj = new JsonObject();
                    errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                    errorObj.Set("stdout", RuntimeValue.String(""));
                    errorObj.Set("stderr", RuntimeValue.String($"Error: Working directory does not exist: '{finalWorkingDirectory}'"));
                    return RuntimeValue.Object(errorObj);
                }
            }
            else
            {
                finalWorkingDirectory = System.Environment.CurrentDirectory;
            }
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String($"Error: Invalid working directory: {ex.Message}"));
            return RuntimeValue.Object(errorObj);
        }
        
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var pseudoResult = RunCommandPseudo.TryExecute(command, commandArgs, finalWorkingDirectory);
            if (pseudoResult != null)
            {
                stopwatch.Stop();
                return pseudoResult;
            }
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                WorkingDirectory = finalWorkingDirectory,
                RedirectStandardOutput = !detached,
                RedirectStandardError = !detached,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (commandArgs != null)
            {
                foreach (var arg in commandArgs)
                    startInfo.ArgumentList.Add(arg);
            }

            ApplyCommandEnvironment(startInfo, extraEnvironment);
            
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                var errorObj = new JsonObject();
                errorObj.Set("exitCode", RuntimeValue.Integer(-1));
                errorObj.Set("stdout", RuntimeValue.String(""));
                errorObj.Set("stderr", RuntimeValue.String($"Error: Failed to start process '{command}'"));
                
                // Trace failed process start
                try
                {
                    TraceManager.Record(
                        TraceEventType.RunCommand,
                        new
                        {
                            command,
                            arguments = commandArgs,
                            workingDirectory = finalWorkingDirectory,
                            exitCode = -1,
                            stdout = "",
                            stderr = $"Error: Failed to start process '{command}'",
                            durationMs = (int?)stopwatch.ElapsedMilliseconds
                        });
                }
                catch
                {
                    // Tracing must never interfere with normal execution
                }
                
                return RuntimeValue.Object(errorObj);
            }

            if (detached)
            {
                stopwatch.Stop();
                var detachedObj = new JsonObject();
                detachedObj.Set("exitCode", RuntimeValue.Integer(0));
                detachedObj.Set("stdout", RuntimeValue.String(""));
                detachedObj.Set("stderr", RuntimeValue.String(""));
                detachedObj.Set("detached", RuntimeValue.Boolean(true));
                detachedObj.Set("pid", RuntimeValue.Integer(process.Id));

                try
                {
                    TraceManager.Record(
                        TraceEventType.RunCommand,
                        new
                        {
                            command,
                            arguments = commandArgs,
                            workingDirectory = finalWorkingDirectory,
                            exitCode = 0,
                            stdout = "",
                            stderr = "",
                            durationMs = (int?)stopwatch.ElapsedMilliseconds,
                            detached = true,
                            pid = process.Id
                        });
                }
                catch
                {
                    // Tracing must never interfere with normal execution
                }

                return RuntimeValue.Object(detachedObj);
            }
            
            // Read output asynchronously to avoid deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            
            // Wait for process with optional timeout
            bool exited;
            if (timeoutMs.HasValue)
            {
                exited = process.WaitForExit(timeoutMs.Value);
                if (!exited)
                {
                    stopwatch.Stop();
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                    
                    var timeoutObj = new JsonObject();
                    timeoutObj.Set("exitCode", RuntimeValue.Integer(-1));
                    timeoutObj.Set("stdout", RuntimeValue.String(""));
                    timeoutObj.Set("stderr", RuntimeValue.String($"Error: Command timed out after {timeoutMs}ms"));
                    
                    // Trace timeout
                    try
                    {
                        TraceManager.Record(
                            TraceEventType.RunCommand,
                            new
                            {
                                command,
                                arguments = commandArgs,
                                workingDirectory = finalWorkingDirectory,
                                exitCode = -1,
                                stdout = "",
                                stderr = $"Error: Command timed out after {timeoutMs}ms",
                                durationMs = (int?)stopwatch.ElapsedMilliseconds
                            });
                    }
                    catch
                    {
                        // Tracing must never interfere with normal execution
                    }
                    
                    return RuntimeValue.Object(timeoutObj);
                }
            }
            else
            {
                process.WaitForExit();
                exited = true;
            }
            
            // Wait for output reading to complete
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            stopwatch.Stop();
            
            var resultObj = new JsonObject();
            resultObj.Set("exitCode", RuntimeValue.Integer(process.ExitCode));
            resultObj.Set("stdout", RuntimeValue.String(stdout ?? ""));
            resultObj.Set("stderr", RuntimeValue.String(stderr ?? ""));
            
            // Trace successful command execution
            try
            {
                TraceManager.Record(
                    TraceEventType.RunCommand,
                    new
                    {
                        command,
                        arguments = commandArgs,
                        workingDirectory = finalWorkingDirectory,
                        exitCode = process.ExitCode,
                        stdout = stdout ?? "",
                        stderr = stderr ?? "",
                        durationMs = (int?)stopwatch.ElapsedMilliseconds
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Object(resultObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("exitCode", RuntimeValue.Integer(-1));
            errorObj.Set("stdout", RuntimeValue.String(""));
            errorObj.Set("stderr", RuntimeValue.String($"Error executing command: {ex.Message}"));
            
            // Trace execution error
            try
            {
                TraceManager.Record(
                    TraceEventType.RunCommand,
                    new
                    {
                        command,
                        arguments = commandArgs,
                        workingDirectory = finalWorkingDirectory,
                        exitCode = -1,
                        stdout = "",
                        stderr = $"Error executing command: {ex.Message}",
                        durationMs = (int?)null
                    });
            }
            catch
            {
                // Tracing must never interfere with normal execution
            }
            
            return RuntimeValue.Object(errorObj);
        }
    }
    
    /// <summary>
    /// Rewrites interpreter failures that mean the program needs the JavaScript backend
    /// so the coding agent switches to compile_malda mode js instead of rewriting the file.
    /// </summary>
    internal static string DescribeBrowserOnlyRunFailure(string message)
    {
        if (string.IsNullOrEmpty(message) || !IsBrowserOnlyRuntimeFailure(message))
            return message;
        return "JavaScript-only API (game.*, dom.*, or three.*). run_malda executes the interpreter and cannot play a browser game. Verify with compile_malda mode \"js\". Interpreter and C# transpile are not proof a game runs. Original: " + message;
    }

    private static bool IsBrowserOnlyRuntimeFailure(string message)
    {
        if (message.Contains("only available in browser-hosted JavaScript runtime", StringComparison.Ordinal))
            return true;
        return message.Contains("Undefined variable 'dom'", StringComparison.Ordinal)
            || message.Contains("Undefined variable 'game'", StringComparison.Ordinal)
            || message.Contains("Undefined variable 'three'", StringComparison.Ordinal);
    }
}
