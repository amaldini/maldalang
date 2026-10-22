// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Embedded UI host runtime for the CLI, the Desktop IDE and other in-process consumers.

using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MaldaLang.UIHost;

/// <summary>
/// In-process UI host runtime. Can be started by the Desktop IDE (or other hosts) instead of launching UIHost.exe.
/// The protocol lives in <see cref="UiHostHub"/>.
/// </summary>
public static class EmbeddedUiHostRuntime
{
    private static readonly object Gate = new();
    private static bool _started;

    private static string? _indexHtml;
    private static string? _uiClientJs;

    /// <summary>The URL the host listens on once <see cref="TryStartAsync"/> succeeded.</summary>
    public static string? BaseUrl { get; private set; }

    /// <summary>The protocol hub behind the host's routes.</summary>
    public static UiHostHub Hub { get; } = new();

    /// <summary>
    /// Starts the embedded UI host if not already running. Uses MALDA_UI_HOST_URL or default http://localhost:50114.
    /// </summary>
    public static async Task<bool> TryStartAsync()
    {
        lock (Gate)
        {
            if (_started)
                return true;
            _started = true;
        }

        try
        {
            LoadEmbeddedAssets();
            var baseUrl = ResolveBaseUrl();
            var builder = WebApplication.CreateBuilder();
            // Embedded host must not spam the MALDA console (e.g. secondbrain --help).
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(baseUrl);
            var app = builder.Build();
            app.UseWebSockets();

            app.MapGet("/health", () => Results.Ok(new { ok = true, protocolVersion = UiHostHub.ProtocolVersion }));
            app.MapGet("/", () => Results.Text(_indexHtml ?? FallbackIndexHtml(), "text/html; charset=utf-8"));
            app.MapGet("/index.html", () => Results.Text(_indexHtml ?? FallbackIndexHtml(), "text/html; charset=utf-8"));
            app.MapGet("/malda-ui-client.js", () => Results.Text(_uiClientJs ?? FallbackClientJs(), "application/javascript; charset=utf-8"));

            app.Map("/ui/ws/{sessionId}", HandleWebSocketAsync);
            app.MapPost("/ui/mount/{sessionId}", (string sessionId, HttpContext context) => HandleEnvelopeAsync(sessionId, "mount", context));
            app.MapPost("/ui/patch/{sessionId}", (string sessionId, HttpContext context) => HandleEnvelopeAsync(sessionId, "patch", context));

            _ = Hub.RunHeartbeatAsync(app.Lifetime.ApplicationStopping);
            _ = app.RunAsync();
            await WaitForHealthAsync(baseUrl);
            BaseUrl = baseUrl;
            return true;
        }
        catch
        {
            lock (Gate)
                _started = false;
            return false;
        }
    }

    private static void LoadEmbeddedAssets()
    {
        var asm = Assembly.GetExecutingAssembly();
        _indexHtml = ReadEmbeddedResource(asm, "MaldaLang.UIHost.wwwroot.index.html") ?? FallbackIndexHtml();
        _uiClientJs = ReadEmbeddedResource(asm, "MaldaLang.UIHost.wwwroot.malda-ui-client.js") ?? FallbackClientJs();
    }

    private static string? ReadEmbeddedResource(Assembly assembly, string name)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string FallbackIndexHtml() =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>MALDA UI Host</title></head><body><div id=\"app\"></div><script src=\"/malda-ui-client.js\"></script></body></html>";

    private static string FallbackClientJs() => "console.warn('Embedded MALDA UI client was not found.');";

    private static string ResolveBaseUrl()
    {
        var configured = System.Environment.GetEnvironmentVariable("MALDA_UI_HOST_URL");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        return "http://localhost:50114";
    }

    private static async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!AuthorizeRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var sessionId = context.Request.RouteValues.TryGetValue("sessionId", out var sessionValue) ? sessionValue?.ToString() ?? "default" : "default";
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await Hub.AcceptAsync(socket, sessionId, context.RequestAborted);
    }

    private static async Task<IResult> HandleEnvelopeAsync(string sessionId, string envelopeType, HttpContext context)
    {
        if (!AuthorizeRequest(context))
            return Results.Unauthorized();

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var error = Hub.Publish(sessionId, envelopeType, await reader.ReadToEndAsync());
        return error == null
            ? Results.Ok(new { delivered = true, protocolVersion = UiHostHub.ProtocolVersion })
            : Results.BadRequest(new { error });
    }

    private static bool AuthorizeRequest(HttpContext context)
    {
        var authToken = System.Environment.GetEnvironmentVariable("MALDA_UI_AUTH_TOKEN");
        if (string.IsNullOrWhiteSpace(authToken))
            return true;
        if (!context.Request.Headers.TryGetValue("X-Malda-UI-Auth", out var token))
            token = context.Request.Query["token"];
        return string.Equals(token.ToString(), authToken, StringComparison.Ordinal);
    }

    private static async Task WaitForHealthAsync(string baseUrl)
    {
        using var client = new HttpClient();
        for (var i = 0; i < 10; i++)
        {
            try
            {
                var response = await client.GetAsync(baseUrl.TrimEnd('/') + "/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Host still starting.
            }
            await Task.Delay(100);
        }
    }
}
