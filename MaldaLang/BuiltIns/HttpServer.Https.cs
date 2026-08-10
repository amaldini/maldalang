// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(remoteIp))
        {
            proxyRequest.Headers.Remove("X-Forwarded-For");
            proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-For", remoteIp);
        }

        using var proxyResponse = await _httpsProxyClient.SendAsync(
            proxyRequest,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted).ConfigureAwait(false);

        context.Response.StatusCode = (int)proxyResponse.StatusCode;

        // Disable ASP.NET response buffering so progressive writes (SSE heartbeats /
        // ask-progress) reach the browser instead of arriving only at stream close.
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
            // Chunked streaming must not advertise a fixed length (SSE stays open).
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
