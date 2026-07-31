// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Parser;
using MaldaLang.Tests.Planning;

namespace MaldaLang.Tests.Conformance.Tier0;

/// <summary>
/// Runs Tier 0 programs via <see cref="JsTranspiler"/> + Node.js and <c>malda-js-runtime.js</c>.
/// </summary>
public static class Tier0JavaScriptRunner
{
    private static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    private const string WrapperScript = """
const runtimePath = process.argv[2];
const programPath = process.argv[3];
require(runtimePath);
(async () => {
  const app = require(programPath);
  await app.main();
  if (globalThis.mlRuntime?.actors?.shutdownAsync) {
    await globalThis.mlRuntime.actors.shutdownAsync();
  }
})().catch((error) => {
  const detail = error && (error.stack || error.message) ? (error.stack || error.message) : String(error);
  process.stderr.write(detail + "\n");
  process.exit(1);
});
""";

    public static bool IsAvailable(out string reason)
    {
        try
        {
            _ = ResolveJsRuntimePath();
            _ = ResolveNodeExecutablePath();
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public static async Task<string> RunAsync(string source, string? sourceFilePath = null)
    {
        await RunGate.WaitAsync();
        try
        {
            return await RunAsyncCore(source, sourceFilePath);
        }
        finally
        {
            RunGate.Release();
        }
    }

    private static async Task<string> RunAsyncCore(string source, string? sourceFilePath = null)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        if (parser.Errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, parser.Errors));

        var transpiler = new JsTranspiler();
        var js = transpiler.Transpile(statements, isLibrary: false, sourceFilePath: sourceFilePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_tier0_js", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var programPath = Path.Combine(tempDir, "program.js");
            var wrapperPath = Path.Combine(tempDir, "tier0-run.js");
            await File.WriteAllTextAsync(programPath, js);
            await File.WriteAllTextAsync(wrapperPath, WrapperScript);

            var runtimePath = ResolveJsRuntimePath();
            var nodePath = ResolveNodeExecutablePath();
            var startInfo = new ProcessStartInfo
            {
                FileName = nodePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };
            startInfo.ArgumentList.Add(wrapperPath);
            startInfo.ArgumentList.Add(runtimePath);
            startInfo.ArgumentList.Add(programPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for Tier 0 JavaScript backend.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(DefaultRunTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort kill after timeout.
                }

                throw new TimeoutException(
                    $"JavaScript execution timed out after {DefaultRunTimeout.TotalSeconds:0}s.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"JavaScript exit code {process.ExitCode}. stderr: {stderr.Trim()}");
            }

            return Tier0ConformanceRunner.NormalizeOutput(stdout);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private static string ResolveJsRuntimePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("MALDA_JS_RUNTIME_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        var candidate = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        if (File.Exists(candidate))
            return candidate;

        throw new InvalidOperationException(
            "MALDA JS runtime not found. Set MALDA_JS_RUNTIME_PATH or ensure Examples/Web/wwwroot/malda-js-runtime.js exists.");
    }

    private static string ResolveNodeExecutablePath()
    {
        var configured = Environment.GetEnvironmentVariable("MALDA_NODE_PATH");
        return string.IsNullOrWhiteSpace(configured) ? "node" : configured;
    }
}
