// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;

public static partial class WebRuntimeHelpers
{
    private sealed class CookieOptions
    {
        public bool HttpOnly { get; set; } = true;
        public bool Secure { get; set; } = true;
        public string SameSite { get; set; } = "Lax";
        public string Path { get; set; } = "/";
        public string? Domain { get; set; }
        public int? MaxAge { get; set; }
    }

    public static bool IsRelativeRedirect(string location)
    {
        if (string.IsNullOrEmpty(location)) return false;
        if (location.StartsWith("//", StringComparison.Ordinal)) return false; // protocol-relative
        if (location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>
    /// Prepends <paramref name="pathBase"/> to app-relative redirect URLs (e.g. <c>/login</c>).
    /// If <paramref name="location"/> already begins with <paramref name="pathBase"/> followed by <c>/</c> or end-of-string,
    /// returns <paramref name="location"/> unchanged so hosts are not doubled (e.g. <c>/schoolprep/schoolprep/login</c>).
    /// </summary>
    public static string ApplyPathBaseToRelativeRedirect(string location, string? pathBase)
    {
        if (string.IsNullOrEmpty(location)) return location;
        if (string.IsNullOrEmpty(pathBase) || !IsRelativeRedirect(location))
            return location;
        if (location.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
        {
            if (location.Length == pathBase.Length)
                return location;
            if (location.Length > pathBase.Length && location[pathBase.Length] == '/')
                return location;
        }

        return pathBase + (location.StartsWith("/", StringComparison.Ordinal) ? location : "/" + location);
    }

    public static int NormalizeRedirectStatusCode(int? statusCode, string callerName, int defaultStatusCode = 303)
    {
        var normalized = statusCode ?? defaultStatusCode;
        if (normalized < 300 || normalized >= 400)
        {
            throw new Exception($"{callerName} redirect status must be a 3xx code.");
        }

        return normalized;
    }

    public static JsonObject CreateRedirectResponse(string location, int? statusCode = null)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new Exception("redirect location cannot be empty.");
        }

        var response = new JsonObject();
        response.Set("status", RuntimeValue.Integer(NormalizeRedirectStatusCode(statusCode, "redirect()")));

        var headers = new JsonObject();
        headers.Set("Location", RuntimeValue.String(location));
        response.Set("headers", RuntimeValue.Object(headers));
        response.Set("body", RuntimeValue.String(BuildRedirectHtml(location)));
        return response;
    }

    public static void ApplyRedirectLocation(HttpListenerResponse response, string location, string? pathBase = null)
    {
        response.RedirectLocation = ApplyPathBaseToRelativeRedirect(location, pathBase);
    }

    public static string BuildRedirectHtml(string location)
    {
        var escapedLocation = WebUtility.HtmlEncode(location ?? string.Empty);
        return $"<html><head><title>Redirecting</title><meta http-equiv=\"refresh\" content=\"0;url={escapedLocation}\"></head><body><p>Redirecting to <a href=\"{escapedLocation}\">{escapedLocation}</a>...</p></body></html>";
    }

    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string DefaultCsrfHeaderName = "X-CSRF-Token";
    public const string DefaultCsrfCookieName = "csrf_token";
    public const string DefaultSessionCookieName = "malda_session";
    public const string AuthVerifiedHeader = "X-Malda-Auth-Verified";
    public const string AuthSubjectHeader = "X-Malda-Auth-Sub";
    public const string LegacyAuthVerifiedHeader = "X-Auth-Verified";
    public const string LegacyAuthSubjectHeader = "X-Auth-Sub";
    public const string LegacyVerifiedSubjectHeader = "X-Verified-Sub";

    public static string ResolveCorrelationId(HttpListenerRequest request)
    {
        var headerValue = request.Headers[CorrelationIdHeader];
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.Trim();
        }

        return Guid.NewGuid().ToString("N");
    }

    public static void ApplyCorrelationId(HttpListenerResponse response, string correlationId)
    {
        response.Headers[CorrelationIdHeader] = correlationId;
    }

    /// <summary>
    /// HTTP/1.1 forbids a message body for these statuses. HttpListener also forces
    /// Content-Length to 0 for HEAD, so writing a body throws ProtocolViolationException.
    /// </summary>
    public static bool StatusForbidsHttpResponseBody(int statusCode) =>
        statusCode is 100 or 101 or 204 or 205 or 304;

    public static bool IsHeadRequest(HttpListenerRequest? request) =>
        request != null && string.Equals(request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldOmitHttpResponseBody(HttpListenerRequest? request, int statusCode) =>
        StatusForbidsHttpResponseBody(statusCode) || IsHeadRequest(request);

    public static void WriteHttpListenerBody(HttpListenerResponse response, byte[] bytes, HttpListenerRequest? request)
    {
        if (StatusForbidsHttpResponseBody(response.StatusCode))
        {
            response.ContentLength64 = 0;
            return;
        }

        response.ContentLength64 = bytes.Length;
        if (IsHeadRequest(request) || bytes.Length == 0)
        {
            return;
        }

        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    public static void TryCloseHttpListenerResponse(HttpListenerResponse response)
    {
        try
        {
            response.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (HttpListenerException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static string ComposeRoutePath(string routePath, string? groupPrefix, string? versionPrefix)
    {
        var segments = new List<string>();

        var normalizedGroup = NormalizeRouteSegment(groupPrefix);
        if (!string.IsNullOrEmpty(normalizedGroup))
        {
            segments.Add(normalizedGroup);
        }

        var normalizedVersion = NormalizeRouteSegment(versionPrefix);
        if (!string.IsNullOrEmpty(normalizedVersion))
        {
            segments.Add(normalizedVersion);
        }

        var normalizedRoute = NormalizeRouteSegment(routePath);
        if (!string.IsNullOrEmpty(normalizedRoute))
        {
            segments.Add(normalizedRoute);
        }

        if (segments.Count == 0)
        {
            return "/";
        }

        return "/" + string.Join("/", segments);
    }

    private static string NormalizeRouteSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        var trimmed = segment.Trim();
        trimmed = trimmed.Trim('/');
        return trimmed;
    }

    private static RuntimeValue JsonElementToRuntimeValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonObjectToRuntimeValue(element),
            JsonValueKind.Array => JsonArrayToRuntimeValue(element),
            JsonValueKind.String => RuntimeValue.String(element.GetString() ?? string.Empty),
            JsonValueKind.Number => element.TryGetInt32(out var intVal)
                ? RuntimeValue.Integer(intVal)
                : RuntimeValue.Float(element.GetDouble()),
            JsonValueKind.True => RuntimeValue.Boolean(true),
            JsonValueKind.False => RuntimeValue.Boolean(false),
            JsonValueKind.Null => RuntimeValue.Null(),
            _ => RuntimeValue.Null()
        };
    }

    private static RuntimeValue JsonObjectToRuntimeValue(JsonElement element)
    {
        var jsonObj = new JsonObject();
        foreach (var prop in element.EnumerateObject())
        {
            jsonObj.Set(prop.Name, JsonElementToRuntimeValue(prop.Value));
        }
        return RuntimeValue.Object(jsonObj);
    }

    private static RuntimeValue JsonArrayToRuntimeValue(JsonElement element)
    {
        var list = new List<RuntimeValue>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(JsonElementToRuntimeValue(item));
        }
        return RuntimeValue.Array(list);
    }
}
