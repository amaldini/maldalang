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
using MaldaLang.Tests.Planning;

namespace MaldaLang.Tests;

/// <summary>
/// Ship-contract HTTP traces: same GET status + JSON body on interpret and
/// C# transpile. Oracles for <c>Templates/webapi</c>, <c>Templates/fullstack</c>,
/// and the offline Second Brain ASK wrapper.
/// </summary>
[Collection("HttpTraceSerial")]
public class HttpTraceParityTests
{
    private const string InlineHealthSource = """
        var server = new RestServer(__PORT__, "127.0.0.1");

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
        AssertSameTraces(interpret, transpile);
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
            source = source.Replace("new RestServer(8080)", "new RestServer(__PORT__, \"127.0.0.1\")", StringComparison.Ordinal);
            Assert.DoesNotContain("new RestServer(8080)", source, StringComparison.Ordinal);

            var interpret = await TraceInterpretAsync(source);
            var transpile = await TraceTranspileAsync(source);
            AssertSameTraces(interpret, transpile);
            Assert.Equal("ok", interpret[0].Json.GetProperty("status").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task FullstackTemplateHealth_InterpretAndTranspile_SameStatusAndBody()
    {
        var root = Path.Combine(Path.GetTempPath(), "malda_http_trace_fs_" + Guid.NewGuid().ToString("N"));
        var dest = Path.Combine(root, "app");
        Directory.CreateDirectory(root);
        try
        {
            var scaffolder = new TemplateScaffolder();
            var code = scaffolder.Scaffold("fullstack", dest, new StringWriter(), new StringWriter());
            Assert.Equal(0, code);

            var appPath = Path.Combine(dest, "backend", "app.malda");
            Assert.True(File.Exists(appPath), "scaffolded fullstack is missing backend/app.malda");
            var source = File.ReadAllText(appPath);
            source = source.Replace("new HttpServer(8080)", "new HttpServer(__PORT__)", StringComparison.Ordinal);
            Assert.DoesNotContain("new HttpServer(8080)", source, StringComparison.Ordinal);

            var extraEnv = new Dictionary<string, string> { ["MALDA_HTTP_HOST"] = "127.0.0.1" };
            var interpret = await TraceInterpretAsync(source, extraEnv, "/api/health");
            var transpile = await TraceTranspileAsync(source, extraEnv, "/api/health");
            AssertSameTraces(interpret, transpile);
            Assert.Equal("ok", interpret[0].Json.GetProperty("status").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task SecondBrainAskWrapper_InterpretAndTranspile_SameStatusAndBody()
    {
        var appPath = PlanningPaths.ResolveRepoFile("Examples", "Agents", "secondbrain_ask_wrapper.malda");
        Assert.True(File.Exists(appPath), $"Missing ASK wrapper: {appPath}");
        var source = File.ReadAllText(appPath);
        source = source.Replace("new RestServer(8080, \"127.0.0.1\")", "new RestServer(__PORT__, \"127.0.0.1\")", StringComparison.Ordinal);
        Assert.Contains("__PORT__", source, StringComparison.Ordinal);

        var interpret = await TraceInterpretAsync(source, null, "/health", "/ask");
        var transpile = await TraceTranspileAsync(source, null, "/health", "/ask");
        AssertSameTraces(interpret, transpile);
        Assert.Equal("ok", interpret[0].Json.GetProperty("status").GetString());
        Assert.Equal("secondbrain-ask", interpret[0].Json.GetProperty("service").GetString());
        Assert.Equal("offline fixture", interpret[1].Json.GetProperty("answer").GetString());
    }

    private static void AssertSameTraces(IReadOnlyList<HttpTrace> interpret, IReadOnlyList<HttpTrace> transpile)
    {
        Assert.Equal(interpret.Count, transpile.Count);
        for (var i = 0; i < interpret.Count; i++)
        {
            Assert.Equal(interpret[i].Path, transpile[i].Path);
            Assert.Equal(interpret[i].StatusCode, transpile[i].StatusCode);
            Assert.True(
                JsonNode.DeepEquals(JsonNode.Parse(interpret[i].Body), JsonNode.Parse(transpile[i].Body)),
                "HTTP bodies differ at " + interpret[i].Path + Environment.NewLine
                + "interpret: " + interpret[i].Body + Environment.NewLine
                + "transpile: " + transpile[i].Body);
        }
    }

    private static Task<IReadOnlyList<HttpTrace>> TraceInterpretAsync(string source) =>
        TraceInterpretAsync(source, extraEnv: null, "/api/health");

    private static async Task<IReadOnlyList<HttpTrace>> TraceInterpretAsync(
        string source,
        IDictionary<string, string>? extraEnv,
        params string[] paths)
    {
        if (paths.Length == 0)
            paths = ["/api/health"];

        var port = GetAvailablePort();
        source = BakePort(source, port);
        var restore = PushEnv(extraEnv);
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);

            await TestBase.WithIsolatedConsoleAsync(async () =>
            {
                var interpreter = new Interpreter.Interpreter();
                await interpreter.InterpretAsync(statements);
            });
            try
            {
                return await CaptureGetsAsync(port, process: null, paths);
            }
            finally
            {
                StopHostsForTesting();
            }
        }
        finally
        {
            restore();
        }
    }

    private static Task<IReadOnlyList<HttpTrace>> TraceTranspileAsync(string source) =>
        TraceTranspileAsync(source, extraEnv: null, "/api/health");

    private static async Task<IReadOnlyList<HttpTrace>> TraceTranspileAsync(
        string source,
        IDictionary<string, string>? extraEnv,
        params string[] paths)
    {
        if (paths.Length == 0)
            paths = ["/api/health"];

        var port = GetAvailablePort();
        // RestServer.start() / HttpServer.start() return immediately. Keep Main
        // alive so the published process does not exit before the GET.
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
        if (extraEnv != null)
        {
            foreach (var (key, value) in extraEnv)
                startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            return await CaptureGetsAsync(port, process, paths);
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

    private static async Task<IReadOnlyList<HttpTrace>> CaptureGetsAsync(int port, Process? process, string[] paths)
    {
        var traces = new List<HttpTrace>(paths.Length);
        foreach (var path in paths)
            traces.Add(await CaptureGetAsync(port, process, path));
        return traces;
    }

    private static async Task<HttpTrace> CaptureGetAsync(int port, Process? process, string path)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"http://127.0.0.1:{port}{path}";
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
                return new HttpTrace(path, (int)response.StatusCode, body);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                await Task.Delay(50);
            }
        }

        throw new Exception($"GET {url} did not become ready. Last error: {last?.Message}");
    }

    private static void StopHostsForTesting()
    {
        RestServerInstance.StopAllForTesting();
        HttpServerInstance.StopAllForTesting();
    }

    private static Action PushEnv(IDictionary<string, string>? extraEnv)
    {
        if (extraEnv == null || extraEnv.Count == 0)
            return static () => { };

        var previous = extraEnv.ToDictionary(
            kv => kv.Key,
            kv => Environment.GetEnvironmentVariable(kv.Key),
            StringComparer.Ordinal);
        foreach (var (key, value) in extraEnv)
            Environment.SetEnvironmentVariable(key, value);

        return () =>
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value);
        };
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private readonly record struct HttpTrace(string Path, int StatusCode, string Body)
    {
        public System.Text.Json.JsonElement Json =>
            System.Text.Json.JsonDocument.Parse(Body).RootElement.Clone();
    }
}
