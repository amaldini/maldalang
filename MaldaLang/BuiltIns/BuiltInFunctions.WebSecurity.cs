// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInCreateJwt(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3)
            throw new Exception("createJwt() expects 2 or 3 arguments: (payload, secret, expiresInSeconds? | options?)");
        if (args[1].Type != ValueType.String)
            throw new Exception("createJwt() second argument (secret) must be a string");

        var payloadObj = NormalizeJwtPayload(args[0]);
        var secret = args[1].AsString();
        if (string.IsNullOrWhiteSpace(secret))
            throw new Exception("createJwt() secret cannot be empty");

        int? expiresInSeconds = null;
        string? issuer = null;
        string? audience = null;
        int? notBeforeSeconds = null;

        if (args.Count == 3)
        {
            if (args[2].Type == ValueType.Integer)
            {
                expiresInSeconds = args[2].AsInteger();
            }
            else if (args[2].Type == ValueType.Object && args[2].AsObject() is JsonObject options)
            {
                var expiresValue = options.Get("expiresInSeconds", null);
                if (expiresValue.Type != ValueType.Null)
                {
                    if (expiresValue.Type != ValueType.Integer)
                        throw new Exception("createJwt() options.expiresInSeconds must be an integer");
                    expiresInSeconds = expiresValue.AsInteger();
                }

                var issuerValue = options.Get("issuer", null);
                if (issuerValue.Type != ValueType.Null)
                {
                    if (issuerValue.Type != ValueType.String || string.IsNullOrWhiteSpace(issuerValue.AsString()))
                        throw new Exception("createJwt() options.issuer must be a non-empty string");
                    issuer = issuerValue.AsString().Trim();
                }

                var audienceValue = options.Get("audience", null);
                if (audienceValue.Type != ValueType.Null)
                {
                    if (audienceValue.Type != ValueType.String || string.IsNullOrWhiteSpace(audienceValue.AsString()))
                        throw new Exception("createJwt() options.audience must be a non-empty string");
                    audience = audienceValue.AsString().Trim();
                }

                var nbfValue = options.Get("notBeforeSeconds", null);
                if (nbfValue.Type != ValueType.Null)
                {
                    if (nbfValue.Type != ValueType.Integer)
                        throw new Exception("createJwt() options.notBeforeSeconds must be an integer");
                    notBeforeSeconds = nbfValue.AsInteger();
                }
            }
            else
            {
                throw new Exception("createJwt() third argument must be an integer expiresInSeconds or options object");
            }
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        payloadObj.Set("iat", RuntimeValue.Integer((int)now));

        if (expiresInSeconds.HasValue)
        {
            payloadObj.Set("exp", RuntimeValue.Integer((int)(now + expiresInSeconds.Value)));
        }

        if (issuer != null)
        {
            payloadObj.Set("iss", RuntimeValue.String(issuer));
        }

        if (audience != null)
        {
            payloadObj.Set("aud", RuntimeValue.String(audience));
        }

        if (notBeforeSeconds.HasValue)
        {
            payloadObj.Set("nbf", RuntimeValue.Integer((int)(now + notBeforeSeconds.Value)));
        }

        var header = RuntimeValue.String("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = RuntimeValue.String(RuntimeValueToJson(RuntimeValue.Object(payloadObj)));
        var encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(header.AsString()));
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload.AsString()));
        var signingInput = $"{encodedHeader}.{encodedPayload}";
        var signature = ComputeJwtSignature(signingInput, secret);

        return RuntimeValue.String($"{signingInput}.{signature}");
    }

    private static RuntimeValue BuiltInVerifyJwt(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3)
            throw new Exception("verifyJwt() expects 2 or 3 arguments: (token, secret, options?)");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("verifyJwt() expects (string, string, object?)");

        var token = args[0].AsString();
        var secret = args[1].AsString();
        string? expectedIssuer = null;
        string? expectedAudience = null;

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Object || args[2].AsObject() is not JsonObject options)
                throw new Exception("verifyJwt() third argument must be an options object");

            var issuerValue = options.Get("issuer", null);
            if (issuerValue.Type != ValueType.Null)
            {
                if (issuerValue.Type != ValueType.String || string.IsNullOrWhiteSpace(issuerValue.AsString()))
                    throw new Exception("verifyJwt() options.issuer must be a non-empty string");
                expectedIssuer = issuerValue.AsString().Trim();
            }

            var audienceValue = options.Get("audience", null);
            if (audienceValue.Type != ValueType.Null)
            {
                if (audienceValue.Type != ValueType.String || string.IsNullOrWhiteSpace(audienceValue.AsString()))
                    throw new Exception("verifyJwt() options.audience must be a non-empty string");
                expectedAudience = audienceValue.AsString().Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(token))
            throw new WebRuntimeException(401, "MissingToken", "Missing token.");
        if (string.IsNullOrWhiteSpace(secret))
            throw new Exception("verifyJwt() secret cannot be empty");

        var parts = token.Split('.');
        if (parts.Length != 3)
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token.");

        var signingInput = $"{parts[0]}.{parts[1]}";
        var expectedSignature = ComputeJwtSignature(signingInput, secret);
        var actualSignatureBytes = Encoding.UTF8.GetBytes(parts[2]);
        var expectedSignatureBytes = Encoding.UTF8.GetBytes(expectedSignature);
        if (!CryptographicOperations.FixedTimeEquals(actualSignatureBytes, expectedSignatureBytes))
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token signature.");

        RuntimeValue payloadValue;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var payloadDoc = JsonDocument.Parse(payloadJson);
            payloadValue = JsonToRuntimeValue(payloadDoc.RootElement);
        }
        catch
        {
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token payload.");
        }

        if (payloadValue.Type != ValueType.Object || payloadValue.AsObject() is not JsonObject payloadObject)
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token payload.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (TryReadUnixTimestamp(payloadObject, "nbf", out var nbf) && now < nbf)
            throw new WebRuntimeException(401, "InvalidToken", "Token not yet valid.");

        if (TryReadUnixTimestamp(payloadObject, "exp", out var exp) && now >= exp)
            throw new WebRuntimeException(401, "TokenExpired", "Token expired.");

        if (expectedIssuer != null)
        {
            var iss = payloadObject.Get("iss", null);
            if (iss.Type != ValueType.String ||
                !string.Equals(iss.AsString().Trim(), expectedIssuer, StringComparison.Ordinal))
            {
                throw new WebRuntimeException(401, "InvalidToken", "Invalid token issuer.");
            }
        }

        if (expectedAudience != null)
        {
            var aud = payloadObject.Get("aud", null);
            if (aud.Type != ValueType.String ||
                !string.Equals(aud.AsString().Trim(), expectedAudience, StringComparison.Ordinal))
            {
                throw new WebRuntimeException(401, "InvalidToken", "Invalid token audience.");
            }
        }

        return RuntimeValue.Object(payloadObject);
    }

    private static RuntimeValue BuiltInGenerateCsrfToken(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("generateCsrfToken() expects secret and optional ttlSeconds");
        if (args[0].Type != ValueType.String)
            throw new Exception("generateCsrfToken() secret must be a string");

        var ttlSeconds = 7200;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Integer)
                throw new Exception("generateCsrfToken() ttlSeconds must be an integer");
            ttlSeconds = args[1].AsInteger();
        }

        var token = WebRuntimeHelpers.GenerateCsrfToken(args[0].AsString(), ttlSeconds);
        return RuntimeValue.String(token);
    }

    private static RuntimeValue BuiltInVerifyCsrfToken(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("verifyCsrfToken() expects token and secret");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("verifyCsrfToken() expects (string, string)");

        var isValid = WebRuntimeHelpers.VerifyCsrfToken(args[0].AsString(), args[1].AsString());
        return RuntimeValue.Boolean(isValid);
    }

    private static RuntimeValue BuiltInCsrfField(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("csrfField() expects secret and optional ttlSeconds");
        if (args[0].Type != ValueType.String)
            throw new Exception("csrfField() secret must be a string");

        var ttlSeconds = 7200;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Integer)
                throw new Exception("csrfField() ttlSeconds must be an integer");
            ttlSeconds = args[1].AsInteger();
        }

        var token = WebRuntimeHelpers.GenerateCsrfToken(args[0].AsString(), ttlSeconds);
        var escaped = System.Net.WebUtility.HtmlEncode(token);
        return RuntimeValue.String($"<input type=\"hidden\" name=\"_csrf\" value=\"{escaped}\">");
    }

    private static RuntimeValue BuiltInCreateSecureCookie(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args.Count > 4)
            throw new Exception("createSecureCookie() expects name, value, secret, and optional options object");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String || args[2].Type != ValueType.String)
            throw new Exception("createSecureCookie() expects (string, string, string, object?)");

        RuntimeValue options = RuntimeValue.Null();
        if (args.Count == 4)
        {
            options = args[3];
            if (options.Type != ValueType.Null && options.Type != ValueType.Object)
                throw new Exception("createSecureCookie() options must be an object when provided");
        }

        int? maxAge = null;
        if (options.Type == ValueType.Object && options.AsObject() is JsonObject optionsObj)
        {
            var maxAgeValue = optionsObj.Get("maxAge", null);
            if (maxAgeValue.Type == ValueType.Integer)
            {
                maxAge = maxAgeValue.AsInteger();
            }
        }

        var signedValue = WebRuntimeHelpers.CreateSecureCookieValue(args[1].AsString(), args[2].AsString(), maxAge);
        var header = WebRuntimeHelpers.CreateCookieHeader(args[0].AsString(), signedValue, options, useSecureDefaults: true);
        return RuntimeValue.String(header);
    }

    private static RuntimeValue BuiltInReadSecureCookie(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("readSecureCookie() expects cookieValue and secret");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("readSecureCookie() expects (string, string)");

        if (WebRuntimeHelpers.TryReadSecureCookieValue(args[0].AsString(), args[1].AsString(), out var plainValue))
        {
            return RuntimeValue.String(plainValue);
        }

        return RuntimeValue.Null();
    }

    private static JsonObject NormalizeJwtPayload(RuntimeValue value)
    {
        if (value.Type == ValueType.Object)
        {
            var objectValue = value.AsObject();
            if (objectValue is JsonObject obj)
            {
                var jsonCopy = new JsonObject();
                foreach (var kvp in obj.GetProperties())
                {
                    jsonCopy.Set(kvp.Key, kvp.Value);
                }
                return jsonCopy;
            }

            var copy = new JsonObject();
            foreach (var key in objectValue.GetAllKeys())
            {
                copy.Set(key, objectValue.Get(key, null));
            }
            return copy;
        }

        if (value.Type == ValueType.String)
        {
            try
            {
                using var doc = JsonDocument.Parse(value.AsString());
                var parsed = JsonToRuntimeValue(doc.RootElement);
                if (parsed.Type == ValueType.Object && parsed.AsObject() is JsonObject parsedObj)
                {
                    return parsedObj;
                }
            }
            catch
            {
                // Handled below.
            }
        }

        throw new Exception("createJwt() payload must be an object or JSON object string");
    }

    private static string ComputeJwtSignature(string signingInput, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        return Base64UrlEncode(signatureBytes);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        var mod4 = padded.Length % 4;
        if (mod4 == 2) padded += "==";
        else if (mod4 == 3) padded += "=";
        else if (mod4 != 0) throw new FormatException("Invalid base64url payload.");
        return Convert.FromBase64String(padded);
    }

    private static bool TryReadUnixTimestamp(JsonObject payload, string fieldName, out long unixTimestamp)
    {
        unixTimestamp = 0;
        var value = payload.Get(fieldName, null);
        if (value.Type == ValueType.Integer)
        {
            unixTimestamp = value.AsInteger();
            return true;
        }

        if (value.Type == ValueType.Float)
        {
            unixTimestamp = (long)value.AsFloat();
            return true;
        }

        if (value.Type == ValueType.String && long.TryParse(value.AsString(), out var parsed))
        {
            unixTimestamp = parsed;
            return true;
        }

        return false;
    }
}
