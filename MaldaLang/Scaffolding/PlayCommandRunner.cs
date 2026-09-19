// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Scaffolding;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Cli;

public sealed class PlayCommandRunner
{
    internal const string LiveReloadMarker = "<!-- malda-play-reload -->";
    internal const string GenerationPath = "/__malda_play/generation";
    private const int WatchDebounceMs = 250;

    private readonly Func<string, string, JavaScriptCompileResult> _compile;

    public PlayCommandRunner(Func<string, string, JavaScriptCompileResult>? compile = null)
    {
        _compile = compile ?? JavaScriptCompileHost.CompileToJavaScript;
    }

    public int Run(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        if (!PlayCommandOptionsParser.TryParse(args, error, out var options) || options == null)
        {
            return 1;
        }

        var prepared = Prepare(options, output, error);
        if (prepared == null)
        {
            return 1;
        }

        PlayPreviewServer? server;
        try
        {
            server = StartServer(options, prepared.PreviewDirectory, error);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Could not start the preview server: {ex.Message}");
            return 1;
        }

        if (server == null)
        {
            return 1;
        }

        IDisposable? watch = null;
        using (server)
        {
            output.WriteLine($"Serving {prepared.PreviewDirectory}");
            output.WriteLine($"Open {server.Url}");
            if (options.Watch)
            {
                watch = TryStartWatch(prepared, server, output, error);
                if (watch != null)
                {
                    output.WriteLine("Watching source, index.html, and assets/ for changes.");
                }
            }

            output.WriteLine("Press Ctrl+C to stop.");
            if (options.OpenBrowser)
            {
                TryOpenBrowser(server.Url, output);
            }

            WaitUntilCancelled(cancellationToken);
        }

        watch?.Dispose();
        return 0;
    }

    public PlayPrepareResult? Prepare(PlayCommandOptions options, TextWriter output, TextWriter error)
    {
        var sourcePath = Path.GetFullPath(options.SourcePath);
        if (!File.Exists(sourcePath))
        {
            error.WriteLine($"Error: Input file not found: {options.SourcePath}");
            return null;
        }

        string sourceText;
        try
        {
            sourceText = File.ReadAllText(sourcePath);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Error: Could not read '{options.SourcePath}': {ex.Message}");
            return null;
        }

        if (LooksLikeFullStackSource(sourceText))
        {
            error.WriteLine("malda play is a JavaScript-only preview.");
            error.WriteLine("This file is fullstack (@client plus @server or a route). Compile it instead:");
            error.WriteLine($"  malda compile {options.SourcePath} --mode fullstack -o dist");
            error.WriteLine("Then run the server with MALDA_WEB_DIRECTORY pointing at dist/web (see the generated README).");
            return null;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (stem.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
        {
            stem = Path.GetFileNameWithoutExtension(stem);
        }

        var previewDirectory = string.IsNullOrWhiteSpace(options.PreviewDirectory)
            ? Path.Combine(sourceDirectory, PlayCommandOptions.PreviewDirectoryName)
            : Path.GetFullPath(options.PreviewDirectory);

        try
        {
            if (Directory.Exists(previewDirectory))
            {
                Directory.Delete(previewDirectory, recursive: true);
            }

            Directory.CreateDirectory(previewDirectory);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Could not create preview directory '{previewDirectory}': {ex.Message}");
            return null;
        }

        var jsOutputPath = Path.Combine(previewDirectory, stem + ".js");
        var compiledJs = CompilePreview(sourcePath, jsOutputPath, sourceDirectory, previewDirectory, output, error);
        if (compiledJs == null)
        {
            return null;
        }

        var hostHtmlPath = Path.Combine(previewDirectory, "index.html");
        if (!File.Exists(hostHtmlPath) || !File.Exists(Path.Combine(previewDirectory, "malda-js-runtime.js")))
        {
            error.WriteLine("Compilation did not write index.html and malda-js-runtime.js next to the preview script.");
            return null;
        }

        output.WriteLine($"Preview: {previewDirectory}");
        return new PlayPrepareResult
        {
            SourcePath = sourcePath,
            PreviewDirectory = previewDirectory,
            JavaScriptPath = compiledJs,
            HostHtmlPath = hostHtmlPath
        };
    }

    public bool Rebuild(PlayPrepareResult prepared, TextWriter output, TextWriter error)
    {
        if (!File.Exists(prepared.SourcePath))
        {
            error.WriteLine($"Reload skipped: source disappeared ({prepared.SourcePath}).");
            return false;
        }

        string sourceText;
        try
        {
            sourceText = File.ReadAllText(prepared.SourcePath);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Reload skipped: could not read source ({ex.Message}).");
            return false;
        }

        if (LooksLikeFullStackSource(sourceText))
        {
            error.WriteLine("Reload skipped: file is now fullstack (@client plus @server or a route).");
            error.WriteLine($"  malda compile {prepared.SourcePath} --mode fullstack -o dist");
            return false;
        }

        var sourceDirectory = Path.GetDirectoryName(prepared.SourcePath) ?? Directory.GetCurrentDirectory();
        return CompilePreview(
            prepared.SourcePath,
            prepared.JavaScriptPath,
            sourceDirectory,
            prepared.PreviewDirectory,
            output,
            error) != null;
    }

    public PlayPreviewServer? StartServer(
        PlayCommandOptions options,
        string previewDirectory,
        TextWriter error)
    {
        var host = string.IsNullOrWhiteSpace(options.Host) ? PlayCommandOptions.DefaultHost : options.Host.Trim();
        var bindHost = NormalizeBindHost(host);
        if (options.Port is int explicitPort && explicitPort > 0)
        {
            if (!PlayPreviewServer.TryStart(previewDirectory, bindHost, explicitPort, out var exact, out var bindError))
            {
                error.WriteLine($"Could not bind http://{bindHost}:{explicitPort}/: {bindError}");
                return null;
            }

            return exact;
        }

        var startPort = PlayCommandOptions.DefaultPort;
        for (var port = startPort; port < startPort + 100; port++)
        {
            if (PlayPreviewServer.TryStart(previewDirectory, bindHost, port, out var server, out _))
            {
                return server;
            }
        }

        error.WriteLine($"Could not find a free port starting at {startPort}.");
        return null;
    }

    public static void TryOpenBrowser(string url, TextWriter output)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return;
            }

            var opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = opener,
                Arguments = url,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            process?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            output.WriteLine($"Could not open a browser ({ex.Message}). Open {url} manually.");
        }
    }

    private string? CompilePreview(
        string sourcePath,
        string jsOutputPath,
        string sourceDirectory,
        string previewDirectory,
        TextWriter output,
        TextWriter error)
    {
        output.WriteLine($"Compiling {sourcePath}...");
        var compileResult = _compile(sourcePath, jsOutputPath);
        if (!compileResult.Success)
        {
            error.WriteLine($"Compilation failed: {compileResult.ErrorMessage}");
            return null;
        }

        var compiledJs = compileResult.OutputPath ?? jsOutputPath;
        CopyAssetsIfPresent(sourceDirectory, previewDirectory);
        OverlayCustomHostHtml(sourceDirectory, previewDirectory, Path.GetFileName(compiledJs));
        InjectLiveReloadScript(Path.Combine(previewDirectory, "index.html"));
        return compiledJs;
    }

    internal static void InjectLiveReloadScript(string hostHtmlPath)
    {
        if (!File.Exists(hostHtmlPath))
        {
            return;
        }

        var html = File.ReadAllText(hostHtmlPath);
        if (html.Contains(LiveReloadMarker, StringComparison.Ordinal))
        {
            return;
        }

        var snippet =
            LiveReloadMarker +
            "\n<script>\n(function(){var g=null;function poll(){fetch(\"" +
            GenerationPath +
            "\",{cache:\"no-store\"}).then(function(r){return r.ok?r.text():Promise.reject();}).then(function(t){if(g===null)g=t;else if(t!==g)location.reload();}).catch(function(){});}setInterval(poll,400);})();\n</script>\n";

        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        html = bodyClose >= 0 ? html.Insert(bodyClose, snippet) : html + snippet;
        File.WriteAllText(hostHtmlPath, html);
    }

    private IDisposable? TryStartWatch(
        PlayPrepareResult prepared,
        PlayPreviewServer server,
        TextWriter output,
        TextWriter error)
    {
        var sourceDirectory = Path.GetDirectoryName(prepared.SourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            error.WriteLine("Watch disabled: could not resolve the source directory.");
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(sourceDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*.*"
            };

            var gate = new object();
            var rebuildLock = new object();
            Timer? debounce = null;
            void Schedule()
            {
                lock (gate)
                {
                    debounce?.Change(WatchDebounceMs, Timeout.Infinite);
                    debounce ??= new Timer(_ =>
                    {
                        lock (rebuildLock)
                        {
                            try
                            {
                                if (Rebuild(prepared, output, error))
                                {
                                    server.BumpGeneration();
                                    output.WriteLine("Reloaded preview.");
                                }
                            }
                            catch (Exception ex)
                            {
                                error.WriteLine($"Reload failed: {ex.Message}");
                            }
                        }
                    }, null, WatchDebounceMs, Timeout.Infinite);
                }
            }

            FileSystemEventHandler onChange = (_, e) =>
            {
                if (IsPlayWatchTarget(e.FullPath, prepared))
                {
                    Schedule();
                }
            };
            RenamedEventHandler onRename = (_, e) =>
            {
                if (IsPlayWatchTarget(e.FullPath, prepared) || IsPlayWatchTarget(e.OldFullPath, prepared))
                {
                    Schedule();
                }
            };

            watcher.Changed += onChange;
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Renamed += onRename;
            watcher.EnableRaisingEvents = true;
            return new PlayWatchSession(watcher, () =>
            {
                lock (gate)
                {
                    debounce?.Dispose();
                    debounce = null;
                }
            });
        }
        catch (Exception ex)
        {
            error.WriteLine($"Watch disabled: {ex.Message}");
            return null;
        }
    }

    internal static bool IsPlayWatchTarget(string path, PlayPrepareResult prepared)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (IsUnderDirectory(full, prepared.PreviewDirectory))
        {
            return false;
        }

        if (string.Equals(full, prepared.SourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sourceDirectory = Path.GetDirectoryName(prepared.SourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return false;
        }

        sourceDirectory = Path.GetFullPath(sourceDirectory);
        if (string.Equals(Path.GetFileName(full), "index.html", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetDirectoryName(full), sourceDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (full.EndsWith(".malda", StringComparison.OrdinalIgnoreCase) &&
            IsUnderDirectory(full, sourceDirectory))
        {
            return true;
        }

        foreach (var assetsName in new[] { "assets", "Assets" })
        {
            if (IsUnderDirectory(full, Path.Combine(sourceDirectory, assetsName)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderDirectory(string fullPath, string directory)
    {
        var root = Path.GetFullPath(directory);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeFullStackSource(string source)
    {
        // Same contract as FullStackSourceInspector.IsFullStackSource (CLI cannot reference MaldaLang.Compiler).
        var hasClient = Regex.IsMatch(source, @"^\s*@(?:client|javascript)\s*\(", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!hasClient)
        {
            return false;
        }

        var hasServer = Regex.IsMatch(source, @"^\s*@(?:server|csharp)\s*\(", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var hasRoute = Regex.IsMatch(source, @"^\s*@(GET|POST|PUT|PATCH|DELETE|OPTIONS|PAGE|AIPAGE|ACTION|COMPONENT|LIVE)\s*\(", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return hasServer || hasRoute;
    }

    private static string NormalizeBindHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1";
        }

        if (host is "0.0.0.0" or "*" or "+")
        {
            return "+";
        }

        return host;
    }

    private static void CopyAssetsIfPresent(string sourceDirectory, string previewDirectory)
    {
        foreach (var assetsName in new[] { "assets", "Assets" })
        {
            var assetsSource = Path.Combine(sourceDirectory, assetsName);
            if (!Directory.Exists(assetsSource))
            {
                continue;
            }

            CopyDirectory(assetsSource, Path.Combine(previewDirectory, "assets"));
            break;
        }
    }

    private static void OverlayCustomHostHtml(string sourceDirectory, string previewDirectory, string compiledScriptFileName)
    {
        var sourceHtml = Path.Combine(sourceDirectory, "index.html");
        if (!File.Exists(sourceHtml))
        {
            return;
        }

        var html = File.ReadAllText(sourceHtml);
        if (!string.Equals(compiledScriptFileName, "app.js", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace("./app.js", "./" + compiledScriptFileName, StringComparison.Ordinal);
            html = html.Replace("\"app.js\"", "\"" + compiledScriptFileName + "\"", StringComparison.Ordinal);
        }

        File.WriteAllText(Path.Combine(previewDirectory, "index.html"), html);
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void WaitUntilCancelled(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.WaitHandle.WaitOne();
            return;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            cts.Token.WaitHandle.WaitOne();
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}

public sealed class PlayPreviewServer : IDisposable
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".mp3"] = "audio/mpeg",
        [".txt"] = "text/plain; charset=utf-8",
        [".ico"] = "image/x-icon"
    };

    private readonly HttpListener _listener;
    private readonly string _root;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private int _generation = 1;

    private PlayPreviewServer(HttpListener listener, string root, string url)
    {
        _listener = listener;
        _root = root;
        Url = url;
        _loop = Task.Run(ListenAsync);
    }

    public string Url { get; }

    public int Generation => Volatile.Read(ref _generation);

    public void BumpGeneration() => Interlocked.Increment(ref _generation);

    public static bool TryStart(string root, string bindHost, int port, out PlayPreviewServer? server, out string? error)
    {
        server = null;
        error = null;
        var listener = new HttpListener();
        var prefix = $"http://{bindHost}:{port}/";
        listener.Prefixes.Add(prefix);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            try
            {
                listener.Close();
            }
            catch
            {
            }

            error = ex.Message;
            return false;
        }

        var displayHost = bindHost == "+" ? "127.0.0.1" : bindHost;
        server = new PlayPreviewServer(listener, Path.GetFullPath(root), $"http://{displayHost}:{port}/");
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
        catch
        {
        }

        try
        {
            _listener.Close();
        }
        catch
        {
        }

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_cts.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }

            _ = Task.Run(() => Serve(context));
        }
    }

    private void Serve(HttpListenerContext context)
    {
        try
        {
            var requestPath = context.Request.Url?.AbsolutePath ?? "/";
            if (string.Equals(requestPath.TrimEnd('/'), PlayCommandRunner.GenerationPath, StringComparison.OrdinalIgnoreCase))
            {
                var text = Generation.ToString(CultureInfo.InvariantCulture);
                var payload = Encoding.UTF8.GetBytes(text);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.Headers["Cache-Control"] = "no-store";
                if (!string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.ContentLength64 = payload.Length;
                    context.Response.OutputStream.Write(payload, 0, payload.Length);
                }

                context.Response.Close();
                return;
            }

            if (string.IsNullOrEmpty(requestPath) || requestPath == "/")
            {
                requestPath = "/index.html";
            }

            var decoded = Uri.UnescapeDataString(requestPath).TrimStart('/');
            var candidate = Path.GetFullPath(Path.Combine(_root, decoded.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            {
                WriteStatus(context.Response, 400, "Bad request");
                return;
            }

            if (Directory.Exists(candidate))
            {
                candidate = Path.Combine(candidate, "index.html");
            }

            if (!File.Exists(candidate))
            {
                WriteStatus(context.Response, 404, "Not found");
                return;
            }

            var bytes = File.ReadAllBytes(candidate);
            var extension = Path.GetExtension(candidate);
            context.Response.StatusCode = 200;
            context.Response.ContentType = ContentTypes.TryGetValue(extension, out var contentType)
                ? contentType
                : "application/octet-stream";
            context.Response.Headers["Cache-Control"] = "no-store";
            if (!string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }

            context.Response.Close();
        }
        catch
        {
            try
            {
                context.Response.Abort();
            }
            catch
            {
            }
        }
    }

    private static void WriteStatus(HttpListenerResponse response, int statusCode, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        response.StatusCode = statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }
}

internal sealed class PlayWatchSession : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Action _onDispose;
    private bool _disposed;

    public PlayWatchSession(FileSystemWatcher watcher, Action onDispose)
    {
        _watcher = watcher;
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _onDispose();
    }
}
