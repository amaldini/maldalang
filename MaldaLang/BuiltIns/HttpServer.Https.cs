// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// HTTPS front-end for <see cref="HttpServerInstance"/>: Kestrel terminates TLS and
/// reverse-proxies to a loopback HttpListener that runs the existing request pipeline.
/// </summary>
public partial class HttpServerInstance
{
    private WebApplication? _httpsApp;
    private HttpClient? _httpsProxyClient;
    private X509Certificate2? _httpsCertificate;
    private int _httpsLoopbackPort;

    private void ResolveHttpsFromEnvironment()
    {
        if (_httpsEnabled)
            return;

        var flag = (System.Environment.GetEnvironmentVariable("MALDA_HTTP_HTTPS") ?? string.Empty).Trim();
        if (!string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(flag, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var cert = (System.Environment.GetEnvironmentVariable("MALDA_HTTP_CERT") ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cert))
            throw new Exception("MALDA_HTTP_HTTPS is set but MALDA_HTTP_CERT is missing");

        _httpsEnabled = true;
        _certPath = cert;
        _certPassword = System.Environment.GetEnvironmentVariable("MALDA_HTTP_CERT_PASSWORD") ?? string.Empty;
    }

    private bool IsRequestSecure(HttpListenerRequest request)
    {
        if (request.IsSecureConnection)
            return true;
        if (!_httpsEnabled)
            return false;
        var proto = request.Headers["X-Forwarded-Proto"];
        return string.Equals(proto, "https", StringComparison.OrdinalIgnoreCase);
    }

    private void StartHttpsFrontAndLoopback()
    {
        if (string.IsNullOrWhiteSpace(_certPath))
            throw new Exception("HTTPS is enabled but certPath is empty; call enableHttps(path) or set MALDA_HTTP_CERT");

        _httpsCertificate = LoadHttpsCertificate(_certPath, _certPassword);
        _httpsLoopbackPort = GetFreeLoopbackPort();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_httpsLoopbackPort}/");
        _listener.Start();

        _httpsProxyClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None
        })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_httpsLoopbackPort}/"),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>()
        });
        builder.Logging.ClearProviders();
        // Avoid default http://localhost:5000 binding from host config / env.
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.ConfigureKestrel(options =>
        {
            ConfigureHttpsListen(options, _httpsCertificate);
        });

        var app = builder.Build();
        app.Run(ProxyHttpsRequestToLoopbackAsync);
        _httpsApp = app;

        app.StartAsync().GetAwaiter().GetResult();

        _isRunning = true;
        _mountedRest?.NotifyHostStarted();
        _ = Task.Run(async () => await HandleRequestsAsync());
    }

    private void ConfigureHttpsListen(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions options, X509Certificate2 cert)
    {
        void UseHttps(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listen) =>
            listen.UseHttps(cert);

        if (_host == "0.0.0.0" || _host == "*")
        {
            options.ListenAnyIP(_port, UseHttps);
            return;
        }

        if (_host == "localhost" || _host == "127.0.0.1")
        {
            options.ListenLocalhost(_port, UseHttps);
            return;
        }

        if (IPAddress.TryParse(_host, out var ip))
        {
            options.Listen(ip, _port, UseHttps);
            return;
        }

        // Hostname: bind all interfaces; clients still use the hostname in the URL.
        options.ListenAnyIP(_port, UseHttps);
    }

    private async Task ProxyHttpsRequestToLoopbackAsync(HttpContext context)
    {
        if (_httpsProxyClient == null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var request = context.Request;
        var accept = request.Headers.Accept.ToString();
        var wantsSse = accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
        var target = request.Path + request.QueryString;
        using var proxyRequest = new HttpRequestMessage(new HttpMethod(request.Method), target);

        var transferEncoding = request.Headers["Transfer-Encoding"].ToString();
        var hasBody = HttpMethods.IsPost(request.Method) ||
                      HttpMethods.IsPut(request.Method) ||
                      HttpMethods.IsPatch(request.Method) ||
                      HttpMethods.IsDelete(request.Method) ||
                      (request.ContentLength ?? 0) > 0 ||
                      transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase);

        if (hasBody)
        {
            proxyRequest.Content = new StreamContent(request.Body);
            if (!string.IsNullOrEmpty(request.ContentType))
                proxyRequest.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
        }

        foreach (var header in request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!proxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) &&
                proxyRequest.Content != null)
            {
                proxyRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        proxyRequest.Headers.Remove("X-Forwarded-Proto");
        proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        if (wantsSse)
        {
            proxyRequest.Headers.Remove(SseDelegateHeaderName);
            proxyRequest.Headers.TryAddWithoutValidation(SseDelegateHeaderName, "1");
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(remoteIp))
        {
            proxyRequest.Headers.Remove("X-Forwarded-For");
            proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-For", remoteIp);
        }

        var serveKestrelSse = false;
        HashSet<string>? sseChannels = null;

        using (var proxyResponse = await _httpsProxyClient.SendAsync(
                   proxyRequest,
                   HttpCompletionOption.ResponseHeadersRead,
                   context.RequestAborted).ConfigureAwait(false))
        {
            // LIVE endpoints: loopback only authorizes; Kestrel owns the event stream so
            // ask-progress is not trapped in HttpClient/HttpListener response buffering.
            if (wantsSse &&
                proxyResponse.IsSuccessStatusCode &&
                proxyResponse.Headers.Contains(SseReadyHeaderName))
            {
                sseChannels = ParseSseChannelsFromHeader(
                    proxyResponse.Headers.TryGetValues(SseChannelsHeaderName, out var values)
                        ? values.FirstOrDefault()
                        : null);
                // Drain the short JSON ready body, then release the loopback connection.
                await proxyResponse.Content.CopyToAsync(Stream.Null, context.RequestAborted)
                    .ConfigureAwait(false);
                serveKestrelSse = true;
            }
            else
            {
                context.Response.StatusCode = (int)proxyResponse.StatusCode;
                context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

                var contentType = proxyResponse.Content.Headers.ContentType?.MediaType;
                var isEventStream = string.Equals(
                    contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase);

                foreach (var header in proxyResponse.Headers)
                {
                    if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        continue;
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }
                foreach (var header in proxyResponse.Content.Headers)
                {
                    if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (isEventStream &&
                        header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                if (isEventStream)
                {
                    context.Response.Headers["Cache-Control"] = "no-cache, no-transform";
                    context.Response.Headers["X-Accel-Buffering"] = "no";
                }

                await using var upstream = await proxyResponse.Content
                    .ReadAsStreamAsync(context.RequestAborted)
                    .ConfigureAwait(false);
                var buffer = new byte[8192];
                while (true)
                {
                    var read = await upstream
                        .ReadAsync(buffer.AsMemory(0, buffer.Length), context.RequestAborted)
                        .ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    await context.Response.Body
                        .WriteAsync(buffer.AsMemory(0, read), context.RequestAborted)
                        .ConfigureAwait(false);
                    await context.Response.Body
                        .FlushAsync(context.RequestAborted)
                        .ConfigureAwait(false);
                }
            }
        }

        if (serveKestrelSse)
        {
            var channels = sseChannels ?? new HashSet<string>(StringComparer.Ordinal);
            if (channels.Count == 0)
            {
                channels = ParseSseChannelsFromHeader(context.Request.Query["channel"].ToString());
            }
            await ServeKestrelSseAsync(context, channels).ConfigureAwait(false);
        }
    }

    private async Task ServeKestrelSseAsync(HttpContext context, HashSet<string> subscribedChannels)
    {
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache, no-transform";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Response.Headers["Connection"] = "keep-alive";

        var body = context.Response.Body;
        var connectionId = $"sse_{Interlocked.Increment(ref _sseConnectionCounter)}_{DateTime.UtcNow.Ticks}";
        var connection = new SseConnection(
            bytes =>
            {
                body.WriteAsync(bytes.AsMemory(), context.RequestAborted).AsTask().GetAwaiter().GetResult();
                body.FlushAsync(context.RequestAborted).GetAwaiter().GetResult();
            },
            subscribedChannels);

        lock (_sseConnectionsLock)
        {
            _sseConnections[connectionId] = connection;
        }

        var channelsJson = subscribedChannels.Count == 0
            ? "[]"
            : "[" + string.Join(",", subscribedChannels.Select(c => JsonSerializer.Serialize(c))) + "]";
        var initMessage =
            $"data: {{\"type\":\"connected\",\"connectionId\":\"{connectionId}\",\"channels\":{channelsJson}}}\n\n";
        var initBytes = Encoding.UTF8.GetBytes(initMessage);
        await body.WriteAsync(initBytes, context.RequestAborted).ConfigureAwait(false);
        await body.FlushAsync(context.RequestAborted).ConfigureAwait(false);

        try
        {
            while (!context.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(30000, context.RequestAborted).ConfigureAwait(false);
                SseConnection? current;
                lock (_sseConnectionsLock)
                {
                    if (!_sseConnections.TryGetValue(connectionId, out current))
                        break;
                }

                var heartbeat = "data: {\"type\":\"heartbeat\"}\n\n";
                var heartbeatBytes = Encoding.UTF8.GetBytes(heartbeat);
                if (!current.TryWrite(heartbeatBytes))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Browser closed EventSource.
        }
        finally
        {
            lock (_sseConnectionsLock)
            {
                _sseConnections.Remove(connectionId);
            }
        }
    }

    private static HashSet<string> ParseSseChannelsFromHeader(string? headerValue)
    {
        var channels = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return channels;
        }

        foreach (var value in headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                channels.Add(value);
            }
        }

        return channels;
    }

    private void StopHttpsFront()
    {
        try
        {
            if (_httpsApp != null)
            {
                _httpsApp.StopAsync().GetAwaiter().GetResult();
                _httpsApp.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Best-effort shutdown.
        }
        finally
        {
            _httpsApp = null;
        }

        try
        {
            _httpsProxyClient?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _httpsProxyClient = null;
        }

        try
        {
            _httpsCertificate?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _httpsCertificate = null;
        }
    }

    internal static X509Certificate2 LoadHttpsCertificate(string certPath, string? password)
    {
        var full = Path.GetFullPath(certPath);
        if (!File.Exists(full))
            throw new Exception($"HTTPS certificate file not found: {full}");

        var ext = Path.GetExtension(full).ToLowerInvariant();
        var flags = X509KeyStorageFlags.Exportable;
        if (OperatingSystem.IsWindows())
            flags |= X509KeyStorageFlags.UserKeySet;

        if (ext is ".pfx" or ".p12")
        {
            return new X509Certificate2(full, password ?? string.Empty, flags);
        }

        if (ext is ".pem" or ".crt" or ".cer")
        {
            var keyPath = Path.ChangeExtension(full, ".key");
            if (!File.Exists(keyPath))
                keyPath = full + ".key";
            using var pemCert = File.Exists(keyPath)
                ? X509Certificate2.CreateFromPemFile(full, keyPath)
                : X509Certificate2.CreateFromPemFile(full);
            // Re-import via PFX so Kestrel can use the private key reliably on Windows.
            return new X509Certificate2(pemCert.Export(X509ContentType.Pfx), (string?)null, flags);
        }

        // Unknown extension: try PFX load.
        return new X509Certificate2(full, password ?? string.Empty, flags);
    }

    private static int GetFreeLoopbackPort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        try
        {
            return ((IPEndPoint)tcp.LocalEndpoint).Port;
        }
        finally
        {
            tcp.Stop();
        }
    }
}
