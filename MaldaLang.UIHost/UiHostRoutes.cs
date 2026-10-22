// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MaldaLang.UIHost;

/// <summary>
/// The UI host's HTTP surface, the same for the embedded host (<see cref="EmbeddedUiHostRuntime"/>,
/// used by the CLI, the Desktop IDE and transpiled programs) and the standalone
/// <c>MaldaLang.UIHost</c> process: <c>/health</c>, the page and browser client from embedded
/// resources, <c>/ui/ws/{sessionId}</c>, and <c>POST /ui/mount|patch/{sessionId}</c>.
/// </summary>
public static class UiHostRoutes
{
    private static readonly Lazy<string> IndexHtml = new(() =>
        ReadResource("MaldaLang.UIHost.wwwroot.index.html") ??
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>MALDA UI Host</title></head><body><div id=\"app\"></div><script src=\"/malda-ui-client.js\"></script></body></html>");

    private static readonly Lazy<string> ClientJs = new(() =>
        ReadResource("MaldaLang.UIHost.wwwroot.malda-ui-client.js") ??
        "console.warn('Embedded MALDA UI client was not found.');");

    /// <summary>Maps the UI host routes onto <paramref name="app"/> and starts the hub's heartbeat.</summary>
    public static void MapUiHost(this WebApplication app, UiHostHub hub)
    {
        app.UseWebSockets();

        app.MapGet("/health", () => Results.Ok(new { ok = true, protocolVersion = UiHostHub.ProtocolVersion }));
        app.MapGet("/", () => Results.Text(IndexHtml.Value, "text/html; charset=utf-8"));
        app.MapGet("/index.html", () => Results.Text(IndexHtml.Value, "text/html; charset=utf-8"));
        app.MapGet("/malda-ui-client.js", () => Results.Text(ClientJs.Value, "application/javascript; charset=utf-8"));

        app.Map("/ui/ws/{sessionId}", async (HttpContext context, string sessionId) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!Authorize(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.AcceptAsync(socket, sessionId, context.RequestAborted);
        });
        app.MapPost("/ui/mount/{sessionId}", (string sessionId, HttpContext context) => PublishAsync(hub, sessionId, "mount", context));
        app.MapPost("/ui/patch/{sessionId}", (string sessionId, HttpContext context) => PublishAsync(hub, sessionId, "patch", context));

        _ = hub.RunHeartbeatAsync(app.Lifetime.ApplicationStopping);
    }

    private static async Task<IResult> PublishAsync(UiHostHub hub, string sessionId, string kind, HttpContext context)
    {
        if (!Authorize(context))
            return Results.Unauthorized();

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var error = hub.Publish(sessionId, kind, await reader.ReadToEndAsync());
        return error == null
            ? Results.Ok(new { delivered = true, protocolVersion = UiHostHub.ProtocolVersion })
            : Results.BadRequest(new { error });
    }

    /// <summary>Open when <c>MALDA_UI_AUTH_TOKEN</c> is unset; otherwise the header or <c>?token=</c> must match.</summary>
    private static bool Authorize(HttpContext context)
    {
        var authToken = System.Environment.GetEnvironmentVariable("MALDA_UI_AUTH_TOKEN");
        if (string.IsNullOrWhiteSpace(authToken))
            return true;
        if (!context.Request.Headers.TryGetValue("X-Malda-UI-Auth", out var token))
            token = context.Request.Query["token"];
        return string.Equals(token.ToString(), authToken, StringComparison.Ordinal);
    }

    private static string? ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
