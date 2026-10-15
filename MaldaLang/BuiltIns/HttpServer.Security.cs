// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using System.IO;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

public partial class HttpServerInstance
{
    private void RegisterMiddleware(RuntimeValue middlewareValue, RuntimeValue? optionsValue)
    {
        var exceptPaths = ParseMiddlewareExceptPaths(optionsValue);
        if (middlewareValue.Type == ValueType.Function)
        {
            _middlewareChain.Add(middlewareValue.AsFunction(), exceptPaths);
            return;
        }

        _middlewareChain.Add(middlewareValue.AsString(), exceptPaths);
    }

    private static List<string>? ParseMiddlewareExceptPaths(RuntimeValue? optionsValue)
    {
        if (optionsValue == null)
        {
            return null;
        }

        if (optionsValue.Type != ValueType.Object)
        {
            throw new Exception("use() options must be an object when provided");
        }

        // Interpreter object literals are JsonObject; C# transpile emits DictionaryInstance.
        var exceptValue = optionsValue.AsObject().Get("except", null);
        if (exceptValue.Type == ValueType.Null)
        {
            return null;
        }

        if (exceptValue.Type != ValueType.Array)
        {
            throw new Exception("use() options.except must be an array of path strings");
        }

        var paths = new List<string>();
        foreach (var entry in exceptValue.AsArray())
        {
            if (entry.Type != ValueType.String || string.IsNullOrWhiteSpace(entry.AsString()))
            {
                throw new Exception("use() options.except must contain non-empty path strings");
            }

            paths.Add(entry.AsString().Trim());
        }

        return paths;
    }

    private void ConfigureRateLimit(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3)
            throw new Exception("setRateLimit() expects limit, windowSeconds, and optional keyStrategy");
        if (args[0].Type != ValueType.Integer || args[1].Type != ValueType.Integer)
            throw new Exception("setRateLimit() expects integer limit and integer windowSeconds");

        var limit = args[0].AsInteger();
        var windowSeconds = args[1].AsInteger();
        if (limit <= 0 || windowSeconds <= 0)
            throw new Exception("setRateLimit() limit and windowSeconds must be > 0");

        _rateLimitKeyStrategy = "ipOrToken";
        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.String)
                throw new Exception("setRateLimit() keyStrategy must be a string when provided");
            _rateLimitKeyStrategy = args[2].AsString();
        }

        _rateLimiter = _rateLimiter == null
            ? new RateLimiter(limit, TimeSpan.FromSeconds(windowSeconds))
            : _rateLimiter;
        _rateLimiter.SetRateLimit(limit, TimeSpan.FromSeconds(windowSeconds));
        _rateLimitLimit = limit;
        _rateLimitWindowSeconds = windowSeconds;
    }

    private void ConfigureTrustedProxy(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new Exception("configureTrustedProxy() expects enabled, optional headerName, optional hopIndex");
        if (args[0].Type != ValueType.Boolean)
            throw new Exception("configureTrustedProxy() enabled must be a boolean");

        _trustProxy = args[0].AsBoolean();
        _trustedProxyHeader = "X-Forwarded-For";
        _trustedProxyHopIndex = 0;

        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.String)
                throw new Exception("configureTrustedProxy() headerName must be a string");
            var headerName = args[1].AsString().Trim();
            if (!string.IsNullOrWhiteSpace(headerName))
            {
                _trustedProxyHeader = headerName;
            }
        }

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Integer)
                throw new Exception("configureTrustedProxy() hopIndex must be an integer");
            var hopIndex = args[2].AsInteger();
            if (hopIndex < 0)
                throw new Exception("configureTrustedProxy() hopIndex must be >= 0");
            _trustedProxyHopIndex = hopIndex;
        }
    }

    private void ConfigureRateLimitHeaders(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("setRateLimitHeaders() expects enabled, and optional includeRemaining");
        if (args[0].Type != ValueType.Boolean)
            throw new Exception("setRateLimitHeaders() enabled must be a boolean");

        _rateLimitHeadersEnabled = args[0].AsBoolean();
        _rateLimitRemainingEnabled = true;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Boolean)
                throw new Exception("setRateLimitHeaders() includeRemaining must be a boolean");
            _rateLimitRemainingEnabled = args[1].AsBoolean();
        }
    }

    private void ConfigureCsrf(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new Exception("enableCsrf() expects secret, and optional cookie/header names");
        if (args[0].Type != ValueType.String)
            throw new Exception("enableCsrf() secret must be a string");

        var secret = args[0].AsString();
        if (string.IsNullOrWhiteSpace(secret))
            throw new Exception("enableCsrf() secret cannot be empty");

        _csrfSecret = secret;
        _csrfCookieName = WebRuntimeHelpers.DefaultCsrfCookieName;
        _csrfHeaderName = WebRuntimeHelpers.DefaultCsrfHeaderName;
        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.String)
                throw new Exception("enableCsrf() cookieName must be a string");
            _csrfCookieName = args[1].AsString();
        }

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.String)
                throw new Exception("enableCsrf() headerName must be a string");
            _csrfHeaderName = args[2].AsString();
        }

        _csrfEnabled = true;
    }

    private bool ValidateRateLimit(
        RequestContextInstance requestContext,
        HttpListenerResponse response,
        HttpListenerRequest request,
        string correlationId)
    {
        if (_rateLimiter == null)
        {
            return true;
        }

        var key = ResolveRateLimitKey(requestContext);
        if (_rateLimiter.CheckRateLimit(key))
        {
            ApplyRateLimitHeaders(response, key, rateLimited: false);
            return true;
        }

        response.StatusCode = 429;
        ApplyRateLimitHeaders(response, key, rateLimited: true);
        var payload = WebRuntimeHelpers.CreateErrorRuntimeValue(
            429,
            "RateLimitExceeded",
            "Too many requests. Please try again later.",
            correlationId);
        WriteJsonRuntimeValue(response, payload, request);
        return false;
    }

    private void ApplyRateLimitHeaders(HttpListenerResponse response, string key, bool rateLimited)
    {
        if (!_rateLimitHeadersEnabled || _rateLimiter == null)
        {
            return;
        }

        response.Headers["X-RateLimit-Limit"] = _rateLimitLimit.ToString();
        if (_rateLimitRemainingEnabled)
        {
            var remaining = rateLimited ? 0 : _rateLimiter.GetRemainingRequests(key);
            response.Headers["X-RateLimit-Remaining"] = Math.Max(0, remaining).ToString();
        }

        var retryAfter = rateLimited ? _rateLimiter.GetRetryAfterSeconds(key) : 0;
        if (retryAfter <= 0 && rateLimited)
        {
            retryAfter = Math.Max(1, _rateLimitWindowSeconds);
        }
        if (retryAfter > 0)
        {
            response.Headers["Retry-After"] = retryAfter.ToString();
        }
    }

    private string ResolveClientIp(HttpListenerRequest request)
    {
        var remoteIp = request.RemoteEndPoint?.Address?.ToString() ?? string.Empty;
        if (!_trustProxy)
        {
            return remoteIp;
        }

        var rawForwarded = request.Headers[_trustedProxyHeader];
        if (string.IsNullOrWhiteSpace(rawForwarded))
        {
            return remoteIp;
        }

        var hops = rawForwarded
            .Split(',')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (hops.Count == 0)
        {
            return remoteIp;
        }

        var index = _trustedProxyHopIndex;
        if (index < 0 || index >= hops.Count)
        {
            index = 0;
        }
        return hops[index];
    }

    private string ResolveRateLimitKey(RequestContextInstance requestContext)
    {
        var headers = (JsonObject)requestContext.Get("headers", null).AsObject();
        var ip = requestContext.Get("ip", null).AsString();
        var authHeader = headers.Get("Authorization", null);
        var token = authHeader.Type == ValueType.String ? authHeader.AsString() : string.Empty;
        var userHeader = headers.Get("X-User-Id", null);
        var userId = userHeader.Type == ValueType.String ? userHeader.AsString() : string.Empty;
        var verifiedSub = requestContext.GetVerifiedSubject();

        return _rateLimitKeyStrategy switch
        {
            "user" => !string.IsNullOrWhiteSpace(userId) ? $"user:{userId}" : FallbackRateLimitKey(ip, token),
            "token" => !string.IsNullOrWhiteSpace(token) ? $"token:{token}" : FallbackRateLimitKey(ip, token),
            "ip" => !string.IsNullOrWhiteSpace(ip) ? $"ip:{ip}" : "global",
            "sub" => !string.IsNullOrWhiteSpace(verifiedSub) ? $"sub:{verifiedSub}" : FallbackRateLimitKey(ip, token),
            "verifiedSub" => !string.IsNullOrWhiteSpace(verifiedSub) ? $"sub:{verifiedSub}" : FallbackRateLimitKey(ip, token),
            "verifiedSubOrIp" => !string.IsNullOrWhiteSpace(verifiedSub) ? $"sub:{verifiedSub}" : FallbackRateLimitKey(ip, token),
            _ => FallbackRateLimitKey(ip, token)
        };
    }

    private static string FallbackRateLimitKey(string ip, string token)
    {
        if (!string.IsNullOrWhiteSpace(ip))
        {
            return $"ip:{ip}";
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            return $"token:{token}";
        }

        return "global";
    }

    private bool ValidateCsrf(
        RequestContextInstance requestContext,
        ResponseContextInstance responseContext,
        RuntimeValue? requestBody,
        HttpListenerRequest request,
        HttpListenerResponse response,
        string correlationId)
    {
        if (!_csrfEnabled)
        {
            return true;
        }

        var cookies = (JsonObject)requestContext.Get("cookies", null).AsObject();
        var cookieToken = cookies.Get(_csrfCookieName, null);
        var cookieTokenValue = cookieToken.Type == ValueType.String ? cookieToken.AsString() : string.Empty;

        if (WebRuntimeHelpers.RequiresCsrfValidation(requestContext.Method))
        {
            var requestToken = TryResolveCsrfRequestToken(requestContext, requestBody);
            var valid =
                !string.IsNullOrWhiteSpace(cookieTokenValue) &&
                !string.IsNullOrWhiteSpace(requestToken) &&
                cookieTokenValue == requestToken &&
                WebRuntimeHelpers.VerifyCsrfToken(requestToken, _csrfSecret);

            if (!valid)
            {
                response.StatusCode = 403;
                var payload = WebRuntimeHelpers.CreateErrorRuntimeValue(
                    403,
                    "CsrfValidationFailed",
                    "Invalid or missing CSRF token.",
                    correlationId);
                WriteJsonRuntimeValue(response, payload, request);

                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(cookieTokenValue) || !WebRuntimeHelpers.VerifyCsrfToken(cookieTokenValue, _csrfSecret))
        {
            var csrfToken = WebRuntimeHelpers.GenerateCsrfToken(_csrfSecret);
            var cookieOptions = new JsonObject();
            cookieOptions.Set("secure", RuntimeValue.Boolean(IsRequestSecure(request)));
            cookieOptions.Set("httpOnly", RuntimeValue.Boolean(false));
            responseContext.CallMethod(
                "cookie",
                new List<RuntimeValue>
                {
                    RuntimeValue.String(_csrfCookieName),
                    RuntimeValue.String(csrfToken),
                    RuntimeValue.Object(cookieOptions)
                });
        }

        return true;
    }

    private string TryResolveCsrfRequestToken(RequestContextInstance requestContext, RuntimeValue? requestBody)
    {
        var headers = (JsonObject)requestContext.Get("headers", null).AsObject();
        var headerToken = headers.Get(_csrfHeaderName, null);
        if (headerToken.Type == ValueType.String && !string.IsNullOrWhiteSpace(headerToken.AsString()))
        {
            return headerToken.AsString();
        }

        if (requestBody != null && requestBody.Type == ValueType.Object && requestBody.AsObject() is JsonObject bodyObj)
        {
            var csrf = bodyObj.Get("csrfToken", null);
            if (csrf.Type == ValueType.String && !string.IsNullOrWhiteSpace(csrf.AsString()))
            {
                return csrf.AsString();
            }

            var alt = bodyObj.Get("_csrf", null);
            if (alt.Type == ValueType.String && !string.IsNullOrWhiteSpace(alt.AsString()))
            {
                return alt.AsString();
            }
        }

        return string.Empty;
    }
}
