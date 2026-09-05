// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Compiler;
using MaldaLang.Parser;
using MaldaLang.Scaffolding;

namespace MaldaLang.Tests;

/// <summary>
/// Ship-contract HTTP traces: same GET status + JSON body on interpret and
/// C# transpile. Oracle for <c>Templates/webapi/app.malda</c>.
/// </summary>
[Collection("HttpTraceSerial")]
public class HttpTraceParityTests
{
    private const string InlineHealthSource = """
        var server = new RestServer(__PORT__);

        @GET("/api/health")
        function health() {
            return {
                "status": "ok",
                "service": "ship-trace"
            };
        }

        server.start();
        """;

    [Fact]
    public async Task InlineHealth_InterpretAndTranspile_SameStatusAndBody()
    {
        var interpret = await TraceInterpretAsync(InlineHealthSource);
        var transpile = await TraceTranspileAsync(InlineHealthSource);
        AssertSameTrace(interpret, transpile);
    }

    [Fact]
    public async Task WebApiTemplateHealth_InterpretAndTranspile_SameStatusAndBody()
    {
        var root = Path.Combine(Path.GetTempPath(), "malda_http_trace_" + Guid.NewGuid().ToString("N"));
        var dest = Path.Combine(root, "api");
        Directory.CreateDirectory(root);
        try
        {
            var scaffolder = new TemplateScaffolder();
            var code = scaffolder.Scaffold("webapi", dest, new StringWriter(), new StringWriter());
            Assert.Equal(0, code);

            var appPath = Path.Combine(dest, "app.malda");
            Assert.True(File.Exists(appPath), "scaffolded webapi is missing app.malda");
            var source = File.ReadAllText(appPath);
            source = source.Replace("new RestServer(8080)", "new RestServer(__PORT__)", StringComparison.Ordinal);
            Assert.DoesNotContain("new RestServer(8080)", source, StringComparison.Ordinal);

            var interpret = await TraceInterpretAsync(source);
            var transpile = await TraceTranspileAsync(source);
            AssertSameTrace(interpret, transpile);
            Assert.Equal("ok", interpret.Json.GetProperty("status").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static void AssertSameTrace(HttpTrace interpret, HttpTrace transpile)
    {
        Assert.Equal(interpret.StatusCode, transpile.StatusCode);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(interpret.Body), JsonNode.Parse(transpile.Body)),
            "HTTP bodies differ." + Environment.NewLine
            + "interpret: " + interpret.Body + Environment.NewLine
            + "transpile: " + transpile.Body);
    }

    private static async Task<HttpTrace> TraceInterpretAsync(string source)
    {
        var port = GetAvailablePort();
        source = BakePort(source, port);

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var interpreter = new Interpreter.Interpreter();
        await interpreter.InterpretAsync(statements);
        try
        {
            return await CaptureHealthAsync(port, process: null);
        }
        finally
        {
            RestServerInstance.StopAllForTesting();
        }
    }

    private static async Task<HttpTrace> TraceTranspileAsync(string source)
    {
        var port = GetAvailablePort();
        // RestServer.start() returns immediately. Keep Main alive so the published
        // process does not exit (and tear down HttpListener) before the GET.
        source = BakePort(source, port) + "\nsleep(60000);\n";
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_http_trace_exe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "program.malda");
        File.WriteAllText(sourcePath, source, Encoding.UTF8);
        var exePath = CompileToExe(sourcePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            return await CaptureHealthAsync(port, process);
        }
        catch (Exception ex)
        {
            var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
            var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
            throw new Exception(
                ex.Message
                + Environment.NewLine + $"process exited={process.HasExited}"
                + (process.HasExited ? $" code={process.ExitCode}" : "")
                + Environment.NewLine + "stdout: " + stdout
                + Environment.NewLine + "stderr: " + stderr,
                ex);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                process.WaitForExit(5000);
            }
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static string BakePort(string source, int port) =>
        source.Replace("__PORT__", port.ToString(), StringComparison.Ordinal);

    private static string CompileToExe(string sourcePath)
    {
        var compiler = new Compiler.Compiler();
        var outputExe = Path.ChangeExtension(sourcePath, ".exe");
        var result = compiler.Compile(
            sourcePath,
            outputExe,
            CompilationMode.TranspileToCSharp,
            includeLLamaSharp: false,
            includeUiHost: false,
            profilingOptions: null,
            typedTranspileLevel: 1,
            includeOptionalPacks: true);

        if (!result.Success || string.IsNullOrEmpty(result.OutputPath) || !File.Exists(result.OutputPath))
        {
            var errorDir = Path.GetDirectoryName(outputExe) ?? Directory.GetCurrentDirectory();
            var buildErrorsPath = Path.Combine(errorDir, "build_errors.txt");
            var generatedPath = Path.Combine(errorDir, "GeneratedProgram.cs");
            var details = result.ErrorMessage ?? "Compilation failed.";
            if (File.Exists(buildErrorsPath))
                details += Environment.NewLine + "build_errors.txt: " + File.ReadAllText(buildErrorsPath);
            if (File.Exists(generatedPath))
                details += Environment.NewLine + "GeneratedProgram.cs: " + Path.GetFullPath(generatedPath);
            throw new Exception(details);
        }

        return result.OutputPath;
    }

    private static async Task<HttpTrace> CaptureHealthAsync(int port, Process? process)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"http://localhost:{port}/api/health";
        Exception? last = null;
        for (var i = 0; i < 80; i++)
        {
            if (process is { HasExited: true })
            {
                throw new Exception($"GET {url}: transpiled process exited {process.ExitCode} before the server accepted connections.");
            }

            try
            {
                using var response = await client.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                return new HttpTrace((int)response.StatusCode, body);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                await Task.Delay(50);
            }
        }

        throw new Exception($"GET {url} did not become ready. Last error: {last?.Message}");
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private readonly record struct HttpTrace(int StatusCode, string Body)
    {
        public System.Text.Json.JsonElement Json =>
            System.Text.Json.JsonDocument.Parse(Body).RootElement.Clone();
    }
}
