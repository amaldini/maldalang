// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Runtime.Profiling;

namespace MaldaLang.Compiler;

public partial class Compiler
{
    private static bool UsesUiFramework(string source)
    {
        // In-memory ui.state / ui.setState (HttpServer components) does not need UIHost.
        // Only the browser UI protocol (mount / events) requires the embedded host.
        return source.Contains("ui.mount(", StringComparison.Ordinal) ||
               source.Contains("ui.mountEnvelope(", StringComparison.Ordinal) ||
               source.Contains("uiMount(", StringComparison.Ordinal) ||
               source.Contains("uiMountEnvelope(", StringComparison.Ordinal) ||
               source.Contains("ui.render(", StringComparison.Ordinal) ||
               source.Contains("uiRender(", StringComparison.Ordinal) ||
               source.Contains("ui.dispatchEvent(", StringComparison.Ordinal) ||
               source.Contains("uiDispatchEvent(", StringComparison.Ordinal) ||
               source.Contains("ui.pullEvent(", StringComparison.Ordinal) ||
               source.Contains("uiPullEvent(", StringComparison.Ordinal);
    }

    private static string AddEmbeddedUiHostToTranspiledCode(string csharpCode)
    {
        var patched = csharpCode;
        if (!patched.Contains("using Microsoft.AspNetCore.Builder;", StringComparison.Ordinal))
        {
            patched = "using Microsoft.AspNetCore.Builder;\n" + patched;
        }
        if (!patched.Contains("using Microsoft.AspNetCore.Hosting;", StringComparison.Ordinal))
        {
            patched = "using Microsoft.AspNetCore.Hosting;\n" + patched;
        }
        if (!patched.Contains("using Microsoft.AspNetCore.Http;", StringComparison.Ordinal))
        {
            patched = "using Microsoft.AspNetCore.Http;\n" + patched;
        }
        if (!patched.Contains("using Microsoft.Extensions.Logging;", StringComparison.Ordinal))
        {
            patched = "using Microsoft.Extensions.Logging;\n" + patched;
        }

        if (patched.Contains(EmbeddedUiHostStartMarker, StringComparison.Ordinal))
        {
            var startCall = $"{EmbeddedUiHostStartMarker}\n                    await EmbeddedUiHostRuntime.TryStartAsync();";
            patched = patched.Replace(EmbeddedUiHostStartMarker, startCall, StringComparison.Ordinal);
        }
        else
        {
            var mainSignature = "public static async Task Main(string[] args)";
            var mainIndex = patched.IndexOf(mainSignature, StringComparison.Ordinal);
            if (mainIndex >= 0)
            {
                var openBraceIndex = patched.IndexOf('{', mainIndex);
                if (openBraceIndex >= 0)
                {
                    var insertion = "\n                await EmbeddedUiHostRuntime.TryStartAsync();";
                    patched = patched.Insert(openBraceIndex + 1, insertion);
                }
            }
        }

        var indexHtml = GetUiHostAssetContent("index.html");
        var clientJs = GetUiHostAssetContent("malda-ui-client.js");

        var runtimeCode = $@"

internal static class EmbeddedUiHostRuntime
{{
    private const string ProtocolVersion = ""1.0"";
    private static readonly object Gate = new();
    private static bool _started;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<System.Guid, System.Net.WebSockets.WebSocket>> SocketsBySession = new(System.StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> LastEnvelopeBySession = new(System.StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> SequenceBySession = new(System.StringComparer.Ordinal);

    private static readonly string IndexHtml = {ToVerbatimStringLiteral(indexHtml)};
    private static readonly string UiClientJs = {ToVerbatimStringLiteral(clientJs)};

    public static async System.Threading.Tasks.Task<bool> TryStartAsync()
    {{
        lock (Gate)
        {{
            if (_started)
                return true;
            _started = true;
        }}

        try
        {{
            var baseUrl = ResolveBaseUrl();
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
            // Embedded host must not spam the MALDA console (e.g. CLI --help).
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(baseUrl);
            var app = builder.Build();
            app.UseWebSockets();

            app.MapGet(""/health"", () => Microsoft.AspNetCore.Http.Results.Ok(new {{ ok = true, protocolVersion = ProtocolVersion }}));
            app.MapGet(""/"", () => Microsoft.AspNetCore.Http.Results.Text(IndexHtml, ""text/html; charset=utf-8""));
            app.MapGet(""/index.html"", () => Microsoft.AspNetCore.Http.Results.Text(IndexHtml, ""text/html; charset=utf-8""));
            app.MapGet(""/malda-ui-client.js"", () => Microsoft.AspNetCore.Http.Results.Text(UiClientJs, ""application/javascript; charset=utf-8""));

            app.Map(""/ui/ws/{{sessionId}}"", HandleWebSocketAsync);
            app.MapPost(""/ui/mount/{{sessionId}}"", (string sessionId, Microsoft.AspNetCore.Http.HttpContext context) => HandleEnvelopeAsync(sessionId, ""mount"", context));
            app.MapPost(""/ui/patch/{{sessionId}}"", (string sessionId, Microsoft.AspNetCore.Http.HttpContext context) => HandleEnvelopeAsync(sessionId, ""patch"", context));

            _ = app.RunAsync();
            await WaitForHealthAsync(baseUrl);
            return true;
        }}
        catch
        {{
            return false;
        }}
    }}

    private static string ResolveBaseUrl()
    {{
        var configured = System.Environment.GetEnvironmentVariable(""MALDA_UI_HOST_URL"");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        return ""http://localhost:50114"";
    }}

    private static async System.Threading.Tasks.Task HandleWebSocketAsync(Microsoft.AspNetCore.Http.HttpContext context)
    {{
        if (!context.WebSockets.IsWebSocketRequest)
        {{
            context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
            return;
        }}

        if (!AuthorizeRequest(context))
        {{
            context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
            return;
        }}

        var sessionId = context.Request.RouteValues.TryGetValue(""sessionId"", out var sessionValue) ? sessionValue?.ToString() ?? ""default"" : ""default"";
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var socketId = System.Guid.NewGuid();
        var sessionSockets = SocketsBySession.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentDictionary<System.Guid, System.Net.WebSockets.WebSocket>());
        sessionSockets[socketId] = socket;

        await SendJsonAsync(socket, new
        {{
            type = ""connected"",
            sessionId,
            version = ProtocolVersion,
            envelopeId = System.Guid.NewGuid().ToString(""N""),
            sequence = 1,
            serverTimeUtc = System.DateTime.UtcNow.ToString(""O"")
        }});

        var buffer = new byte[8 * 1024];
        try
        {{
            while (socket.State == System.Net.WebSockets.WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {{
                var payload = await ReceiveTextMessageAsync(socket, buffer, context.RequestAborted);
                if (payload == null)
                    break;

                if (string.IsNullOrWhiteSpace(payload))
                    continue;

                await BroadcastAsync(sessionId, new
                {{
                    type = ""event"",
                    version = ProtocolVersion,
                    sessionId,
                    sequence = NextServerSequence(sessionId),
                    envelopeId = System.Guid.NewGuid().ToString(""N""),
                    serverTimeUtc = System.DateTime.UtcNow.ToString(""O""),
                    payload = ParseOrRaw(payload)
                }});
            }}
        }}
        finally
        {{
            sessionSockets.TryRemove(socketId, out _);
            if (sessionSockets.IsEmpty)
            {{
                SocketsBySession.TryRemove(sessionId, out _);
            }}
        }}
    }}

    private static async System.Threading.Tasks.Task<Microsoft.AspNetCore.Http.IResult> HandleEnvelopeAsync(string sessionId, string envelopeType, Microsoft.AspNetCore.Http.HttpContext context)
    {{
        if (!AuthorizeRequest(context))
        {{
            return Microsoft.AspNetCore.Http.Results.Unauthorized();
        }}

        using var reader = new System.IO.StreamReader(context.Request.Body, System.Text.Encoding.UTF8);
        var payloadText = await reader.ReadToEndAsync();
        var payload = ParseOrRaw(payloadText);
        var envelope = new
        {{
            type = envelopeType,
            version = ProtocolVersion,
            sessionId,
            sequence = NextServerSequence(sessionId),
            envelopeId = System.Guid.NewGuid().ToString(""N""),
            serverTimeUtc = System.DateTime.UtcNow.ToString(""O""),
            payload
        }};
        LastEnvelopeBySession[sessionId] = envelope;
        await BroadcastAsync(sessionId, envelope);
        return Microsoft.AspNetCore.Http.Results.Ok(new {{ delivered = true, protocolVersion = ProtocolVersion }});
    }}

    private static bool AuthorizeRequest(Microsoft.AspNetCore.Http.HttpContext context)
    {{
        var authToken = System.Environment.GetEnvironmentVariable(""MALDA_UI_AUTH_TOKEN"");
        if (string.IsNullOrWhiteSpace(authToken))
            return true;

        if (!context.Request.Headers.TryGetValue(""X-Malda-UI-Auth"", out var token))
        {{
            token = context.Request.Query[""token""];
        }}

        return string.Equals(token.ToString(), authToken, System.StringComparison.Ordinal);
    }}

    private static object ParseOrRaw(string text)
    {{
        try
        {{
            return System.Text.Json.JsonSerializer.Deserialize<object>(text) ?? new {{ raw = text }};
        }}
        catch
        {{
            return new {{ raw = text }};
        }}
    }}

    private static int NextServerSequence(string sessionId)
    {{
        return SequenceBySession.AddOrUpdate(sessionId, 1, (_, current) => current + 1);
    }}

    private static async System.Threading.Tasks.Task BroadcastAsync(string sessionId, object message)
    {{
        if (!SocketsBySession.TryGetValue(sessionId, out var sockets) || sockets.IsEmpty)
            return;

        var deadSockets = new System.Collections.Generic.List<System.Guid>();
        foreach (var entry in sockets)
        {{
            var socket = entry.Value;
            if (socket.State != System.Net.WebSockets.WebSocketState.Open)
            {{
                deadSockets.Add(entry.Key);
                continue;
            }}

            try
            {{
                await SendJsonAsync(socket, message);
            }}
            catch
            {{
                deadSockets.Add(entry.Key);
            }}
        }}

        foreach (var deadSocket in deadSockets)
        {{
            sockets.TryRemove(deadSocket, out _);
        }}
    }}

    private static async System.Threading.Tasks.Task SendJsonAsync(System.Net.WebSockets.WebSocket socket, object message)
    {{
        var json = System.Text.Json.JsonSerializer.Serialize(message);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
    }}

    private static async System.Threading.Tasks.Task<string?> ReceiveTextMessageAsync(System.Net.WebSockets.WebSocket socket, byte[] buffer, System.Threading.CancellationToken cancellationToken)
    {{
        using var stream = new System.IO.MemoryStream();
        while (true)
        {{
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                return null;
            if (result.MessageType != System.Net.WebSockets.WebSocketMessageType.Text)
                return null;

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }}

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }}

    private static async System.Threading.Tasks.Task WaitForHealthAsync(string baseUrl)
    {{
        using var client = new System.Net.Http.HttpClient();
        for (var i = 0; i < 10; i++)
        {{
            try
            {{
                var response = await client.GetAsync(baseUrl.TrimEnd('/') + ""/health"");
                if (response.IsSuccessStatusCode)
                    return;
            }}
            catch
            {{
                // Host still starting.
            }}
            await System.Threading.Tasks.Task.Delay(100);
        }}
    }}
}}";

        return patched + runtimeCode;
    }

    private static string GetUiHostAssetContent(string fileName)
    {
        var candidatePaths = new[]
        {
            Path.Combine(System.Environment.CurrentDirectory, "MaldaLang.UIHost", "wwwroot", fileName),
            Path.Combine(System.Environment.CurrentDirectory, "..", "MaldaLang.UIHost", "wwwroot", fileName)
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        return fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>MALDA UI Host</title></head><body><div id=\"app\"></div><script src=\"/malda-ui-client.js\"></script></body></html>"
            : "console.warn('Embedded MALDA UI client was not found at compile time.');";
    }

    private static string ToVerbatimStringLiteral(string value)
    {
        return "@\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
