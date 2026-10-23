// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace MaldaLang.UIHost;

/// <summary>
/// The UI host's security defaults, shared by the embedded and the standalone host:
/// <list type="bullet">
/// <item>Origin: a browser request (one with an <c>Origin</c> header) must come from the host's own
/// origin, a loopback origin, or one listed in <c>MALDA_UI_ALLOWED_ORIGIN</c> (comma-separated;
/// <c>*</c> allows any). Requests without <c>Origin</c> are not from a browser page and pass.</item>
/// <item>Token: when <c>MALDA_UI_AUTH_TOKEN</c> is set, HTTP calls carry it in <c>X-Malda-UI-Auth</c>
/// and WebSockets carry a single-use <c>?ticket=</c> from <c>POST /ui/ticket</c>. The token itself
/// never goes in a query string.</item>
/// <item>Listening: a host bound off-loopback without a token refuses to start.</item>
/// </list>
/// Settings are read from the environment on each request, so a test can change them.
/// </summary>
public static class UiHostSecurity
{
    public const string TokenVariable = "MALDA_UI_AUTH_TOKEN";
    public const string AllowedOriginVariable = "MALDA_UI_ALLOWED_ORIGIN";
    public const string TokenHeader = "X-Malda-UI-Auth";

    /// <summary>The WebSocket tickets handed out by <c>POST /ui/ticket</c>.</summary>
    public static UiTicketStore Tickets { get; } = new(TimeSpan.FromSeconds(30));

    private static string? Token =>
        System.Environment.GetEnvironmentVariable(TokenVariable) is { } token && !string.IsNullOrWhiteSpace(token) ? token : null;

    /// <summary>True when the host requires the token.</summary>
    public static bool TokenRequired => Token != null;

    /// <summary>
    /// Null when the host may listen on <paramref name="urls"/> (semicolon-separated, as ASP.NET
    /// Core takes them); otherwise why not: an address off loopback with no token set.
    /// </summary>
    public static string? CheckListenUrls(string urls, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            return null;
        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsLoopbackUrl(url))
                return $"refusing to listen on {url} without {TokenVariable}: set a token or bind to localhost / 127.0.0.1";
        }
        return null;
    }

    /// <inheritdoc cref="CheckListenUrls(string, string?)"/>
    public static string? CheckListenUrls(string urls) => CheckListenUrls(urls, Token);

    /// <summary>
    /// A request from a browser page passes when its origin is the host's own, loopback, or listed.
    /// A request with no <c>Origin</c> header is not from a page and passes.
    /// </summary>
    public static bool IsOriginAllowed(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return true;
        var self = context.Request.Scheme + "://" + context.Request.Host.Value;
        return string.Equals(origin, self, StringComparison.OrdinalIgnoreCase) || IsCrossOriginAllowed(origin);
    }

    /// <summary>The cross-origin rule, for <c>Origin</c> checks and the standalone host's CORS policy.</summary>
    public static bool IsCrossOriginAllowed(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return false;
        if (IsLoopbackHost(uri.Host))
            return true;

        var listed = System.Environment.GetEnvironmentVariable(AllowedOriginVariable);
        if (string.IsNullOrWhiteSpace(listed))
            return false;
        foreach (var entry in listed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry == "*" || string.Equals(entry.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>HTTP calls: open without a token, otherwise the <c>X-Malda-UI-Auth</c> header must match.</summary>
    public static bool IsAuthorized(HttpContext context)
    {
        var token = Token;
        return token == null || FixedTimeEquals(context.Request.Headers[TokenHeader].ToString(), token);
    }

    /// <summary>WebSockets: open without a token, otherwise <c>?ticket=</c> must be a live, unused ticket.</summary>
    public static bool IsWebSocketAuthorized(HttpContext context)
    {
        if (Token == null)
            return true;
        var ticket = context.Request.Query["ticket"].ToString();
        return ticket.Length > 0 && Tickets.TryRedeem(ticket);
    }

    private static bool FixedTimeEquals(string presented, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));

    private static bool IsLoopbackUrl(string url)
    {
        // ASP.NET Core wildcards (http://*:5000, http://+:5000) do not parse as URIs.
        var host = url;
        var scheme = host.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            host = host[(scheme + 3)..];
        var end = host.IndexOf('/');
        if (end >= 0)
            host = host[..end];
        if (host.StartsWith('['))
            host = host[..(host.IndexOf(']') + 1)];
        else if (host.LastIndexOf(':') is var colon and >= 0)
            host = host[..colon];
        return IsLoopbackHost(host);
    }

    private static bool IsLoopbackHost(string host)
    {
        host = host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}

/// <summary>Short-lived single-use WebSocket tickets.</summary>
public sealed class UiTicketStore
{
    private readonly ConcurrentDictionary<string, DateTime> _expiryByTicket = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _now;

    public UiTicketStore(TimeSpan lifetime, Func<DateTime>? now = null)
    {
        Lifetime = lifetime;
        _now = now ?? (() => DateTime.UtcNow);
    }

    public TimeSpan Lifetime { get; }

    /// <summary>A new ticket, valid once within <see cref="Lifetime"/>.</summary>
    public string Issue()
    {
        var now = _now();
        foreach (var entry in _expiryByTicket)
        {
            if (entry.Value <= now)
                _expiryByTicket.TryRemove(entry.Key, out _);
        }

        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _expiryByTicket[ticket] = now + Lifetime;
        return ticket;
    }

    /// <summary>True for a ticket issued here, unexpired and not redeemed before; it cannot be redeemed again.</summary>
    public bool TryRedeem(string ticket) => _expiryByTicket.TryRemove(ticket, out var expiry) && expiry > _now();
}
