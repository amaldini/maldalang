// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Embedded UI host runtime for the CLI, the Desktop IDE and transpiled programs.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace MaldaLang.UIHost;

/// <summary>
/// In-process UI host runtime, started by the CLI, the Desktop IDE and transpiled programs that
/// use <c>ui.*</c>. Routes are <see cref="UiHostRoutes"/>; the protocol is <see cref="UiHostHub"/>.
/// </summary>
public static class EmbeddedUiHostRuntime
{
    private static readonly object Gate = new();
    private static bool _started;

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
            var baseUrl = ResolveBaseUrl();
            var builder = WebApplication.CreateBuilder();
            // Embedded host must not spam the MALDA console (e.g. secondbrain --help).
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(baseUrl);
            var app = builder.Build();
            app.MapUiHost(Hub);
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

    private static string ResolveBaseUrl()
    {
        var configured = System.Environment.GetEnvironmentVariable("MALDA_UI_HOST_URL");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        return "http://localhost:50114";
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
