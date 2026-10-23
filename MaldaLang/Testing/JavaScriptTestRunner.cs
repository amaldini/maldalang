// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Testing;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MaldaLang.Cli;
using MaldaLang.Scaffolding;

/// <summary>
/// <c>malda test --target js</c>: compiles one test file with the JavaScript backend and runs it
/// in Node. The file passes when <c>main()</c> completes; a thrown error (failed <c>assert</c>,
/// rejected Promise) fails it with the error's first line and its <c>.malda</c> location.
/// </summary>
internal sealed class JavaScriptTestRunner
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    // Runs main() and the actor shutdown, like __maldaRunMain, but reports the error itself.
    private const string WrapperScript = """
const runtimePath = process.argv[2];
const programPath = process.argv[3];
require(runtimePath);
(async () => {
  const app = require(programPath);
  try {
    await app.main();
  } finally {
    if (globalThis.mlRuntime?.actors?.shutdownAsync) {
      await globalThis.mlRuntime.actors.shutdownAsync();
    }
  }
})().catch((error) => {
  let detail = error && (error.stack || error.message) ? (error.stack || error.message) : String(error);
  // `throw value` in MALDA throws an Error carrying the value: report the value, as the
  // interpreter does, and keep the stack for the .malda location.
  if (error && Object.prototype.hasOwnProperty.call(error, "__maldaValue")) {
    const stack = typeof error.stack === "string" ? error.stack.split("\n").slice(1).join("\n") : "";
    detail = globalThis.mlRuntime.formatValue(error.__maldaValue) + (stack ? "\n" + stack : "");
  }
  process.stderr.write(detail + "\n");
  process.exit(1);
});
""";

    private static readonly Regex MaldaLocation = new(@"[^\s()]+\.malda(?:\.html)?:\d+:\d+", RegexOptions.Compiled);

    private readonly Func<string, string, JavaScriptCompileResult> _compile;
    private readonly string _nodePath;

    public JavaScriptTestRunner(Func<string, string, JavaScriptCompileResult>? compile = null, string? nodePath = null)
    {
        _compile = compile ?? JavaScriptCompileHost.CompileToJavaScript;
        _nodePath = nodePath ?? ResolveNodePath();
    }

    public static string ResolveNodePath()
    {
        var configured = System.Environment.GetEnvironmentVariable("MALDA_NODE_PATH");
        return string.IsNullOrWhiteSpace(configured) ? "node" : configured;
    }

    public TestExecutionResult Run(string testPath)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "malda_test_js", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        // The program sits beside the test file so relative `import js` specifiers and
        // node_modules resolve as they do for the compiled program. `.cjs` keeps it CommonJS
        // under a package.json with "type": "module".
        var id = Guid.NewGuid().ToString("N")[..8];
        var testDirectory = Path.GetDirectoryName(Path.GetFullPath(testPath)) ?? Directory.GetCurrentDirectory();
        var programPath = Path.Combine(testDirectory, $".malda-test-{id}.cjs");
        var mapPath = programPath + ".map";

        try
        {
            var compiledPath = Path.Combine(workDir, "program.js");
            var compiled = _compile(testPath, compiledPath);
            if (!compiled.Success)
            {
                return new TestExecutionResult(testPath, passed: false, "JavaScript compile failed: " + FirstLine(compiled.ErrorMessage));
            }

            var outputPath = compiled.OutputPath ?? compiledPath;
            var javaScript = File.ReadAllText(outputPath);
            var compiledMapPath = outputPath + ".map";
            if (File.Exists(compiledMapPath))
            {
                javaScript = Regex.Replace(
                    javaScript,
                    @"//# sourceMappingURL=.*$",
                    "//# sourceMappingURL=" + Path.GetFileName(mapPath),
                    RegexOptions.Multiline);
                File.Copy(compiledMapPath, mapPath, overwrite: true);
            }

            File.WriteAllText(programPath, javaScript, new UTF8Encoding(false));
            var wrapperPath = Path.Combine(workDir, "malda-test-run.cjs");
            File.WriteAllText(wrapperPath, WrapperScript, new UTF8Encoding(false));
            var runtimePath = Path.Combine(Path.GetDirectoryName(outputPath) ?? workDir, "malda-js-runtime.js");

            return RunNode(testPath, wrapperPath, runtimePath, programPath, testDirectory);
        }
        finally
        {
            TryDelete(programPath);
            TryDelete(mapPath);
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private TestExecutionResult RunNode(string testPath, string wrapperPath, string runtimePath, string programPath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _nodePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add("--enable-source-maps");
        startInfo.ArgumentList.Add(wrapperPath);
        startInfo.ArgumentList.Add(runtimePath);
        startInfo.ArgumentList.Add(programPath);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            return new TestExecutionResult(
                testPath,
                passed: false,
                $"Node.js could not start ({ex.Message}). Install Node or set MALDA_NODE_PATH.");
        }

        if (process == null)
        {
            return new TestExecutionResult(testPath, passed: false, "Node.js could not start. Install Node or set MALDA_NODE_PATH.");
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)RunTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort kill after timeout.
                }

                return new TestExecutionResult(testPath, passed: false, $"Timed out after {RunTimeout.TotalSeconds:0}s on JavaScript.");
            }

            process.WaitForExit();
            _ = stdoutTask.Result;
            var stderr = stderrTask.Result;
            return process.ExitCode == 0
                ? new TestExecutionResult(testPath, passed: true)
                : new TestExecutionResult(testPath, passed: false, DescribeFailure(stderr, process.ExitCode));
        }
    }

    /// <summary>First line of the error plus the first <c>.malda</c> frame of its stack, when Node mapped one.</summary>
    internal static string DescribeFailure(string stderr, int exitCode)
    {
        var lines = stderr
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToList();
        if (lines.Count == 0)
        {
            return $"JavaScript exit code {exitCode}.";
        }

        var message = lines[0];
        var location = lines.Skip(1)
            .Select(line => MaldaLocation.Match(line))
            .FirstOrDefault(match => match.Success);
        return location == null ? message : $"{message} ({Path.GetFileName(location.Value)})";
    }

    private static string FirstLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown error.";
        }

        return message.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            ?? "unknown error.";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the program copy beside the test.
        }
    }
}
