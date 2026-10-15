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
    public static string GenerateCsrfToken(string secret, int ttlSeconds = 7200)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new Exception("CSRF secret cannot be empty.");
        }

        if (ttlSeconds <= 0)
        {
            throw new Exception("CSRF token TTL must be greater than 0 seconds.");
        }

        var random = new byte[32];
        RandomNumberGenerator.Fill(random);
        var nonce = Base64UrlEncode(random);
        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;
        var payload = $"{nonce}.{expiresAt}";
        var signature = ComputeHmac(payload, secret);
        return $"{payload}.{signature}";
    }

    public static bool VerifyCsrfToken(string token, string secret)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var expiresAt))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt)
        {
            return false;
        }

        var payload = $"{parts[0]}.{parts[1]}";
        var expectedSignature = ComputeHmac(payload, secret);
        return FixedTimeEquals(expectedSignature, parts[2]);
    }

    public static string CreateSecureCookieValue(string plainValue, string secret, int? maxAgeSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new Exception("Secure cookie secret cannot be empty.");
        }

        var encodedValue = Base64UrlEncode(Encoding.UTF8.GetBytes(plainValue ?? string.Empty));
        var expiresAt = maxAgeSeconds.HasValue
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + maxAgeSeconds.Value
            : 0;
        var payload = $"{encodedValue}.{expiresAt}";
        var signature = ComputeHmac(payload, secret);
        return $"{payload}.{signature}";
    }

    public static bool TryReadSecureCookieValue(string cookieValue, string secret, out string plainValue)
    {
        plainValue = string.Empty;
        if (string.IsNullOrWhiteSpace(cookieValue) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var parts = cookieValue.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var payload = $"{parts[0]}.{parts[1]}";
        var expectedSignature = ComputeHmac(payload, secret);
        if (!FixedTimeEquals(expectedSignature, parts[2]))
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var expiresAt))
        {
            return false;
        }

        if (expiresAt > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt)
        {
            return false;
        }

        try
        {
            var decoded = Base64UrlDecode(parts[0]);
            plainValue = Encoding.UTF8.GetString(decoded);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string CreateCookieHeader(string name, string value, RuntimeValue options, bool useSecureDefaults)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new Exception("Cookie name cannot be empty.");
        }

        var parsedOptions = ParseCookieOptions(options, useSecureDefaults);
        var builder = new StringBuilder();
        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(value ?? string.Empty));
        builder.Append("; Path=");
        builder.Append(parsedOptions.Path);
        builder.Append("; SameSite=");
        builder.Append(parsedOptions.SameSite);

        if (!string.IsNullOrWhiteSpace(parsedOptions.Domain))
        {
            builder.Append("; Domain=");
            builder.Append(parsedOptions.Domain);
        }

        if (parsedOptions.MaxAge.HasValue)
        {
            builder.Append("; Max-Age=");
            builder.Append(parsedOptions.MaxAge.Value);
        }

        if (parsedOptions.HttpOnly)
        {
            builder.Append("; HttpOnly");
        }

        if (parsedOptions.Secure)
        {
            builder.Append("; Secure");
        }

        return builder.ToString();
    }

    public static bool RequiresCsrfValidation(string method)
    {
        return method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
               method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
               method.Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
               method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetHeaderValue(Dictionary<string, string> headers, string name, out string value)
    {
        if (headers.TryGetValue(name, out value!))
        {
            return true;
        }

        foreach (var kvp in headers)
        {
            if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public static string ResolveVerifiedSubjectFromHeaders(Dictionary<string, string> headers)
    {
        if (!TryReadVerifiedFlag(headers))
        {
            return string.Empty;
        }

        if (TryGetHeaderValue(headers, AuthSubjectHeader, out var canonicalSub) && !string.IsNullOrWhiteSpace(canonicalSub))
        {
            return canonicalSub.Trim();
        }

        if (TryGetHeaderValue(headers, LegacyAuthSubjectHeader, out var legacySub) && !string.IsNullOrWhiteSpace(legacySub))
        {
            return legacySub.Trim();
        }

        if (TryGetHeaderValue(headers, LegacyVerifiedSubjectHeader, out var fallbackSub) && !string.IsNullOrWhiteSpace(fallbackSub))
        {
            return fallbackSub.Trim();
        }

        return string.Empty;
    }

    private static CookieOptions ParseCookieOptions(RuntimeValue options, bool useSecureDefaults)
    {
        var parsed = new CookieOptions
        {
            HttpOnly = useSecureDefaults,
            Secure = useSecureDefaults,
            SameSite = useSecureDefaults ? "Lax" : "None",
            Path = "/"
        };

        if (options.Type != ValueType.Object || options.AsObject() is not JsonObject obj)
        {
            return parsed;
        }

        var httpOnly = obj.Get("httpOnly", null);
        if (httpOnly.Type == ValueType.Boolean)
        {
            parsed.HttpOnly = httpOnly.AsBoolean();
        }

        var secure = obj.Get("secure", null);
        if (secure.Type == ValueType.Boolean)
        {
            parsed.Secure = secure.AsBoolean();
        }

        var sameSite = obj.Get("sameSite", null);
        if (sameSite.Type == ValueType.String)
        {
            parsed.SameSite = NormalizeSameSite(sameSite.AsString());
        }

        var path = obj.Get("path", null);
        if (path.Type == ValueType.String && !string.IsNullOrWhiteSpace(path.AsString()))
        {
            parsed.Path = path.AsString();
        }

        var domain = obj.Get("domain", null);
        if (domain.Type == ValueType.String && !string.IsNullOrWhiteSpace(domain.AsString()))
        {
            parsed.Domain = domain.AsString();
        }

        var maxAge = obj.Get("maxAge", null);
        if (maxAge.Type == ValueType.Integer)
        {
            parsed.MaxAge = maxAge.AsInteger();
        }

        return parsed;
    }

    private static string NormalizeSameSite(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("strict", StringComparison.OrdinalIgnoreCase))
            return "Strict";
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "None";
        return "Lax";
    }

    private static string ComputeHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(signature);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        var mod4 = normalized.Length % 4;
        if (mod4 == 2) normalized += "==";
        else if (mod4 == 3) normalized += "=";
        else if (mod4 != 0) throw new FormatException("Invalid base64url input.");
        return Convert.FromBase64String(normalized);
    }

    private static bool TryReadVerifiedFlag(Dictionary<string, string> headers)
    {
        if (TryGetHeaderValue(headers, AuthVerifiedHeader, out var canonicalVerified))
        {
            return IsTrueHeaderValue(canonicalVerified);
        }

        if (TryGetHeaderValue(headers, LegacyAuthVerifiedHeader, out var legacyVerified))
        {
            return IsTrueHeaderValue(legacyVerified);
        }

        return false;
    }

    private static bool IsTrueHeaderValue(string raw)
    {
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}
