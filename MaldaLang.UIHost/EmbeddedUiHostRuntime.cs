// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Embedded UI host runtime: same behavior as compiler-inlined UIHost for use by Desktop IDE and other in-process consumers.

using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MaldaLang.UIHost;

/// <summary>
/// In-process UI host runtime. Can be started by the Desktop IDE (or other hosts) instead of launching UIHost.exe.
/// Matches the behavior of the compiler's inlined EmbeddedUiHostRuntime in transpiled apps.
/// </summary>
public static class EmbeddedUiHostRuntime
{
    private const string ProtocolVersion = "1.0";
    private static readonly object Gate = new();
    private static bool _started;
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, WebSocket>> SocketsBySession = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, object> LastEnvelopeBySession = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> SequenceBySession = new(StringComparer.Ordinal);

    private static string? _indexHtml;
    private static string? _uiClientJs;

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

            app.MapGet("/health", () => Results.Ok(new { ok = true, protocolVersion = ProtocolVersion }));
            app.MapGet("/", () => Results.Text(_indexHtml ?? FallbackIndexHtml(), "text/html; charset=utf-8"));
            app.MapGet("/index.html", () => Results.Text(_indexHtml ?? FallbackIndexHtml(), "text/html; charset=utf-8"));
            app.MapGet("/malda-ui-client.js", () => Results.Text(_uiClientJs ?? FallbackClientJs(), "application/javascript; charset=utf-8"));

            app.Map("/ui/ws/{sessionId}", HandleWebSocketAsync);
            app.MapPost("/ui/mount/{sessionId}", (string sessionId, HttpContext context) => HandleEnvelopeAsync(sessionId, "mount", context));
            app.MapPost("/ui/patch/{sessionId}", (string sessionId, HttpContext context) => HandleEnvelopeAsync(sessionId, "patch", context));

            _ = app.RunAsync();
            await WaitForHealthAsync(baseUrl);
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
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var socketId = Guid.NewGuid();
        var sessionSockets = SocketsBySession.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, WebSocket>());
        sessionSockets[socketId] = socket;

        await SendJsonAsync(socket, new
        {
            type = "connected",
            sessionId,
            version = ProtocolVersion,
            envelopeId = Guid.NewGuid().ToString("N"),
            sequence = 1,
            serverTimeUtc = DateTime.UtcNow.ToString("O")
        });

        var buffer = new byte[8 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var payload = await ReceiveTextMessageAsync(socket, buffer, context.RequestAborted);
                if (payload == null)
                    break;
                if (string.IsNullOrWhiteSpace(payload))
                    continue;

                await BroadcastAsync(sessionId, new
                {
                    type = "event",
                    version = ProtocolVersion,
                    sessionId,
                    sequence = NextServerSequence(sessionId),
                    envelopeId = Guid.NewGuid().ToString("N"),
                    serverTimeUtc = DateTime.UtcNow.ToString("O"),
                    payload = ParseOrRaw(payload)
                });
            }
        }
        finally
        {
            sessionSockets.TryRemove(socketId, out _);
            if (sessionSockets.IsEmpty)
                SocketsBySession.TryRemove(sessionId, out _);
        }
    }

    private static async Task<IResult> HandleEnvelopeAsync(string sessionId, string envelopeType, HttpContext context)
    {
        if (!AuthorizeRequest(context))
            return Results.Unauthorized();

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var payloadText = await reader.ReadToEndAsync();
        var payload = ParseOrRaw(payloadText);
        var envelope = new
        {
            type = envelopeType,
            version = ProtocolVersion,
            sessionId,
            sequence = NextServerSequence(sessionId),
            envelopeId = Guid.NewGuid().ToString("N"),
            serverTimeUtc = DateTime.UtcNow.ToString("O"),
            payload
        };
        LastEnvelopeBySession[sessionId] = envelope;
        await BroadcastAsync(sessionId, envelope);
        return Results.Ok(new { delivered = true, protocolVersion = ProtocolVersion });
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

    private static object ParseOrRaw(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(text) ?? new { raw = text };
        }
        catch
        {
            return new { raw = text };
        }
    }

    private static int NextServerSequence(string sessionId) =>
        SequenceBySession.AddOrUpdate(sessionId, 1, (_, current) => current + 1);

    private static async Task BroadcastAsync(string sessionId, object message)
    {
        if (!SocketsBySession.TryGetValue(sessionId, out var sockets) || sockets.IsEmpty)
            return;
        var deadSockets = new List<Guid>();
        foreach (var entry in sockets)
        {
            var socket = entry.Value;
            if (socket.State != WebSocketState.Open)
            {
                deadSockets.Add(entry.Key);
                continue;
            }
            try
            {
                await SendJsonAsync(socket, message);
            }
            catch
            {
                deadSockets.Add(entry.Key);
            }
        }
        foreach (var deadSocket in deadSockets)
            sockets.TryRemove(deadSocket, out _);
    }

    private static async Task SendJsonAsync(WebSocket socket, object message)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string?> ReceiveTextMessageAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                return null;
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }
        return Encoding.UTF8.GetString(stream.ToArray());
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
