using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Compiler;
using MaldaLang.Runtime.Profiling;

namespace MaldaLang.Tests;

public static class TranspiledTestRunner
{
    private const string TradingEnvPrefix = "MT4_TRADING_";
    private static readonly object TradingCompileLock = new();

    public sealed class RunResult
    {
        public int ExitCode { get; init; }
        public string StdOut { get; init; } = string.Empty;
        public string StdErr { get; init; } = string.Empty;
    }

    /// <summary>
    /// Compile the given MALDA source (from a string) in transpile mode and run the resulting executable.
    /// </summary>
    public static RunResult CompileAndRunFromSource(string source)
    {
        return CompileAndRunFromSource(source, includeUiHost: false, environmentVariables: null, commandLineArgs: null, profilingOptions: null);
    }

    public static RunResult CompileAndRunFromSource(string source, bool includeUiHost, IDictionary<string, string>? environmentVariables, IReadOnlyList<string>? commandLineArgs = null, ProfilingOptions? profilingOptions = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_transpiled_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var sourcePath = Path.Combine(tempDir, "program.malda");
        File.WriteAllText(sourcePath, source, Encoding.UTF8);

        try
        {
            return CompileAndRunFromFile(sourcePath, includeUiHost, environmentVariables, commandLineArgs, profilingOptions);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Compile the given MALDA file in transpile mode and run the resulting executable.
    /// </summary>
    public static RunResult CompileAndRunFromFile(string sourcePath)
    {
        return CompileAndRunFromFile(sourcePath, includeUiHost: false, environmentVariables: null, commandLineArgs: null, profilingOptions: null);
    }

    public static RunResult CompileAndRunFromFile(string sourcePath, bool includeUiHost, IDictionary<string, string>? environmentVariables, IReadOnlyList<string>? commandLineArgs = null, ProfilingOptions? profilingOptions = null)
    {
        lock (TradingCompileLock)
        {
            ReleaseTradingExampleFileLocks();
            return CompileAndRunFromFileUnlocked(sourcePath, includeUiHost, environmentVariables, commandLineArgs, profilingOptions);
        }
    }

    private static RunResult CompileAndRunFromFileUnlocked(string sourcePath, bool includeUiHost, IDictionary<string, string>? environmentVariables, IReadOnlyList<string>? commandLineArgs = null, ProfilingOptions? profilingOptions = null)
    {
        var compiler = new Compiler.Compiler();
        var outputExe = Path.ChangeExtension(sourcePath, ".exe");

        var result = compiler.Compile(sourcePath, outputExe,
            CompilationMode.TranspileToCSharp,
            includeLLamaSharp: false,
            includeUiHost: includeUiHost,
            profilingOptions: profilingOptions,
            typedTranspileLevel: 1,
            includeOptionalPacks: true);

        if (!result.Success || result.OutputPath == null || !File.Exists(result.OutputPath))
        {
            var errorDir = Path.GetDirectoryName(outputExe) ?? Directory.GetCurrentDirectory();
            var errorPath = Path.Combine(errorDir, "build_errors.txt");
            var extra = File.Exists(errorPath) ? File.ReadAllText(errorPath) : "";
            throw new Exception($"Transpiled compilation failed: {result.ErrorMessage}\n{extra}");
        }

        try
        {
            return RunExe(result.OutputPath, environmentVariables, commandLineArgs);
        }
        finally
        {
            ReleaseTradingExampleFileLocks();
        }
    }

    internal static void ReleaseTradingExampleFileLocks()
    {
        foreach (var processName in new[] { "mt4_connector_app", "MaldaLang.Executable" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch
                {
                    // ignore cleanup failures
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        Thread.Sleep(500);
    }

    private static RunResult RunExe(string exePath, IDictionary<string, string>? environmentVariables, IReadOnlyList<string>? commandLineArgs)
    {
        return RunExeAsync(exePath, environmentVariables, commandLineArgs).GetAwaiter().GetResult();
    }

    private static async Task<RunResult> RunExeAsync(string exePath, IDictionary<string, string>? environmentVariables, IReadOnlyList<string>? commandLineArgs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ResetTradingEnvironment(startInfo);
        // Ensure transpiled test executables run with deterministic, low-noise logging.
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["Logging__LogLevel__Default"] = "Error";
        startInfo.Environment["Logging__LogLevel__Microsoft"] = "Error";
        startInfo.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Error";
        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }
        }
        if (commandLineArgs != null)
        {
            foreach (var arg in commandLineArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new Exception("Failed to start transpiled executable process.");
        }

        // Read output asynchronously to capture it as it's produced
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // Wait for process to exit (with timeout to prevent hanging)
        var exitTask = Task.Run(() => process.WaitForExit());
        var timeoutMs = 120000;
        if (environmentVariables != null &&
            environmentVariables.TryGetValue("MT4_TRADING_TEST_PROCESS_TIMEOUT_MS", out var timeoutOverride) &&
            int.TryParse(timeoutOverride, out var parsedTimeout) &&
            parsedTimeout > 0)
        {
            timeoutMs = parsedTimeout;
        }
        var timeoutTask = Task.Delay(timeoutMs);
        var completedTask = await Task.WhenAny(exitTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            try { process.Kill(); } catch { /* ignore */ }
            throw new Exception($"Process did not exit within {timeoutMs / 1000} seconds");
        }

        // Wait for output reading to complete
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // Give actors additional time to process messages and produce output
        // This is important because actors are asynchronous and may produce output
        // after the main program logic completes. We wait a bit to allow any
        // buffered or delayed output to be captured.
        // Note: Since the process has exited, we can't read more output, but this
        // delay helps ensure the output we already read is complete and flushed.
        // 
        // IMPORTANT: If actor tests fail intermittently, also ensure:
        // 1. Test source code includes delay loops (200+ iterations) before assertions
        // 2. Example files include delay loops if they use actors
        // 3. See .cursorrules for more details on actor test timing issues
        await Task.Delay(300);

        return new RunResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdout.Replace("\r", "").Trim(),
            StdErr = stderr.Replace("\r", "").Trim()
        };
    }

    private static void ResetTradingEnvironment(ProcessStartInfo startInfo)
    {
        var inheritedKeys = new List<string>();
        foreach (var key in startInfo.Environment.Keys)
        {
            if (key.StartsWith(TradingEnvPrefix, StringComparison.Ordinal))
                inheritedKeys.Add(key);
        }

        foreach (var key in inheritedKeys)
            startInfo.Environment.Remove(key);
    }
}

