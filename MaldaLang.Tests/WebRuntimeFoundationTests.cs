// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class WebRuntimeFoundationTests
{
    private static string CreateJwt(string secret, string sub, string role = "admin", int expiresInSeconds = 120)
    {
        var payload = new JsonObject();
        payload.Set("sub", RuntimeValue.String(sub));
        payload.Set("role", RuntimeValue.String(role));

        return BuiltInFunctions.CallBuiltIn(
                "createJwt",
                new List<RuntimeValue>
                {
                    RuntimeValue.Object(payload),
                    RuntimeValue.String(secret),
                    RuntimeValue.Integer(expiresInSeconds)
                },
                null!)
            .AsString();
    }

    [Fact]
    public async Task WebMiddlewareChain_ExecutesInOrder_WhenNextIsCalled()
    {
        var chain = new WebMiddlewareChain();
        chain.Add("m1");
        chain.Add("m2");

        var visited = new List<string>();
        var request = new RequestContextInstance(
            "GET",
            "/items/42",
            new Dictionary<string, string> { ["q"] = "search" },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            RuntimeValue.Null(),
            new Dictionary<string, string> { ["id"] = "42" });
        var response = new ResponseContextInstance();

        var continued = await chain.ExecuteAsync(request, response, (registration, args) =>
        {
            visited.Add(registration.FunctionName ?? "unknown");

            var next = args[2].AsFunction();
            var callback = (MiddlewareNextCallbackInstance)next.BuiltInInstance!;
            callback.CallMethod("invoke", new List<RuntimeValue>());

            return Task.FromResult(RuntimeValue.Null());
        });

        Assert.True(continued);
        Assert.Equal(new[] { "m1", "m2" }, visited);
    }

    [Fact]
    public async Task WebMiddlewareChain_ShortCircuits_WhenNextIsNotCalled()
    {
        var chain = new WebMiddlewareChain();
        chain.Add("m1");
        chain.Add("m2");

        var visited = new List<string>();
        var request = new RequestContextInstance(
            "GET",
            "/items/42",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            RuntimeValue.Null());
        var response = new ResponseContextInstance();

        var continued = await chain.ExecuteAsync(request, response, (registration, args) =>
        {
            visited.Add(registration.FunctionName ?? "unknown");
            return Task.FromResult(RuntimeValue.Null());
        });

        Assert.False(continued);
        Assert.Equal(new[] { "m1" }, visited);
    }

    [Fact]
    public void RequestContext_ExposesPathQueryHeadersBodyAndCookies()
    {
        var request = new RequestContextInstance(
            "POST",
            "/users/123",
            new Dictionary<string, string> { ["limit"] = "10" },
            new Dictionary<string, string> { ["X-Trace"] = "abc" },
            new Dictionary<string, string> { ["session"] = "cookie-value" },
            RuntimeValue.String("payload"),
            new Dictionary<string, string> { ["id"] = "123" },
            "corr-123",
            "127.0.0.1");

        Assert.Equal("POST", request.Get("method", null).AsString());
        Assert.Equal("/users/123", request.Get("path", null).AsString());
        Assert.Equal("corr-123", request.Get("correlationId", null).AsString());
        Assert.Equal("127.0.0.1", request.Get("ip", null).AsString());
        Assert.Equal("payload", request.Get("body", null).AsString());

        var query = (JsonObject)request.Get("query", null).AsObject();
        var headers = (JsonObject)request.Get("headers", null).AsObject();
        var cookies = (JsonObject)request.Get("cookies", null).AsObject();
        var pathParams = (JsonObject)request.Get("params", null).AsObject();

        Assert.Equal("10", query.Get("limit", null).AsString());
        Assert.Equal("abc", headers.Get("X-Trace", null).AsString());
        Assert.Equal("cookie-value", cookies.Get("session", null).AsString());
        Assert.Equal("123", pathParams.Get("id", null).AsString());
    }

    [Fact]
    public void RequestContext_HelperMethodsAndCustomState_Work()
    {
        var request = new RequestContextInstance(
            "GET",
            "/users/123",
            new Dictionary<string, string> { ["limit"] = "10" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Trace"] = "abc" },
            new Dictionary<string, string> { ["session"] = "cookie-value" },
            RuntimeValue.Null(),
            new Dictionary<string, string> { ["id"] = "123" },
            "corr-456",
            "127.0.0.1",
            "?limit=10",
            "http://localhost/users/123?limit=10",
            "http",
            "localhost",
            "application/json");

        Assert.Equal("abc", request.CallMethod("header", new List<RuntimeValue> { RuntimeValue.String("x-trace") }).AsString());
        Assert.Equal("10", request.CallMethod("queryParam", new List<RuntimeValue> { RuntimeValue.String("limit") }).AsString());
        Assert.Equal("123", request.CallMethod("param", new List<RuntimeValue> { RuntimeValue.String("id") }).AsString());
        Assert.Equal("cookie-value", request.CallMethod("cookie", new List<RuntimeValue> { RuntimeValue.String("session") }).AsString());
        Assert.True(request.CallMethod("hasHeader", new List<RuntimeValue> { RuntimeValue.String("X-Trace") }).AsBoolean());
        Assert.Equal("http://localhost/users/123?limit=10", request.Get("url", null).AsString());
        Assert.Equal("?limit=10", request.Get("queryString", null).AsString());
        Assert.Equal("application/json", request.Get("contentType", null).AsString());

        request.Set("tenant", RuntimeValue.String("tenant-42"));
        Assert.Equal("tenant-42", request.Get("tenant", null).AsString());
    }

    [Fact]
    public void RequestContext_Validate_ReturnsStructuredErrors()
    {
        var body = new JsonObject();
        body.Set("name", RuntimeValue.String("ab"));

        var schema = new JsonObject();
        var path = new JsonObject();
        path.Set("id", RuntimeValue.String("int|required"));
        var query = new JsonObject();
        query.Set("mode", RuntimeValue.String("required|string"));
        var requestBody = new JsonObject();
        requestBody.Set("name", RuntimeValue.String("required|string|minLength=3"));
        schema.Set("path", RuntimeValue.Object(path));
        schema.Set("query", RuntimeValue.Object(query));
        schema.Set("body", RuntimeValue.Object(requestBody));

        var request = new RequestContextInstance(
            "POST",
            "/users/not-a-number",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            RuntimeValue.Object(body),
            new Dictionary<string, string> { ["id"] = "not-a-number" },
            "corr-validate");

        var result = request.CallMethod("validate", new List<RuntimeValue> { RuntimeValue.Object(schema) });
        var resultObject = Assert.IsType<JsonObject>(result.AsObject());
        var errors = resultObject.Get("errors", null).AsArray();

        Assert.False(resultObject.Get("ok", null).AsBoolean());
        Assert.Equal(3, errors.Count);
        Assert.Contains("Request validation failed", resultObject.Get("message", null).AsString());
    }

    [Fact]
    public void RequestAuthContext_AuthenticateBearerJwt_SetsClaimsAndToken()
    {
        const string secret = "web-runtime-foundation-secret";
        var token = CreateJwt(secret, "user-42", "editor");
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
        var request = new RequestContextInstance(
            "GET",
            "/secure",
            new Dictionary<string, string>(),
            headers,
            new Dictionary<string, string>(),
            RuntimeValue.Null());

        var auth = Assert.IsType<RequestAuthContextInstance>(request.Get("auth", null).AsObject());
        var claims = auth.CallMethod("authenticateBearerJwt", new List<RuntimeValue> { RuntimeValue.String(secret) });
        var claimsObject = Assert.IsType<JsonObject>(claims.AsObject());

        Assert.True(auth.IsVerified);
        Assert.Equal("user-42", auth.VerifiedSubject);
        Assert.Equal("editor", claimsObject.Get("role", null).AsString());
        Assert.Equal("user-42", request.Get("verifiedSub", null).AsString());
        Assert.Equal(token, auth.Get("token", null).AsString());
    }

    [Fact]
    public void RequestAuthContext_RequireVerified_ThrowsUnauthorizedWhenAnonymous()
    {
        var request = new RequestContextInstance(
            "GET",
            "/secure",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            RuntimeValue.Null());

        var auth = Assert.IsType<RequestAuthContextInstance>(request.Get("auth", null).AsObject());
        var ex = Assert.Throws<WebRuntimeException>(() =>
            auth.CallMethod("requireVerified", new List<RuntimeValue>()));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("Unauthorized", ex.ErrorCode);
    }

    [Fact]
    public void RequestAuthContext_ClaimRoleAndPermissionHelpers_Work()
    {
        const string secret = "web-runtime-auth-helpers-secret";
        var payload = new JsonObject();
        payload.Set("sub", RuntimeValue.String("user-claims-42"));
        payload.Set("role", RuntimeValue.String("admin"));
        payload.Set("permissions", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("tickets.read"),
            RuntimeValue.String("tickets.write")
        }));
        payload.Set("scope", RuntimeValue.String("profile.read reports.read"));
        payload.Set("tenantId", RuntimeValue.String("tenant-7"));

        var token = BuiltInFunctions.CallBuiltIn(
            "createJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.Object(payload),
                RuntimeValue.String(secret),
                RuntimeValue.Integer(120)
            },
            null!).AsString();

        var request = new RequestContextInstance(
            "GET",
            "/secure",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            new Dictionary<string, string>(),
            RuntimeValue.Null());

        var auth = Assert.IsType<RequestAuthContextInstance>(request.Get("auth", null).AsObject());
        auth.CallMethod("authenticateBearerJwt", new List<RuntimeValue> { RuntimeValue.String(secret) });

        Assert.Equal("tenant-7", auth.CallMethod("claim", new List<RuntimeValue> { RuntimeValue.String("tenantId") }).AsString());
        Assert.Equal("fallback", auth.CallMethod("claim", new List<RuntimeValue>
        {
            RuntimeValue.String("missingClaim"),
            RuntimeValue.String("fallback")
        }).AsString());
        Assert.True(auth.CallMethod("hasClaim", new List<RuntimeValue> { RuntimeValue.String("tenantId") }).AsBoolean());
        Assert.True(auth.CallMethod("hasRole", new List<RuntimeValue> { RuntimeValue.String("ADMIN") }).AsBoolean());
        Assert.True(auth.CallMethod("hasPermission", new List<RuntimeValue> { RuntimeValue.String("reports.read") }).AsBoolean());

        var roles = auth.Get("roles", null).AsArray();
        Assert.Single(roles);
        Assert.Equal("admin", roles[0].AsString());

        var permissions = auth.Get("permissions", null).AsArray();
        Assert.Contains(permissions, value => value.Type == ValueType.String && value.AsString() == "tickets.read");
        Assert.Contains(permissions, value => value.Type == ValueType.String && value.AsString() == "tickets.write");
        Assert.Contains(permissions, value => value.Type == ValueType.String && value.AsString() == "profile.read");
        Assert.Contains(permissions, value => value.Type == ValueType.String && value.AsString() == "reports.read");

        auth.CallMethod("requireRole", new List<RuntimeValue> { RuntimeValue.String("admin") });
        auth.CallMethod("requirePermission", new List<RuntimeValue> { RuntimeValue.String("tickets.write") });
    }

    [Fact]
    public void RequestAuthContext_RequireRole_ThrowsForbiddenWhenRoleMissing()
    {
        const string secret = "web-runtime-forbidden-role-secret";
        var token = CreateJwt(secret, "user-43", "editor");
        var request = new RequestContextInstance(
            "GET",
            "/secure",
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            new Dictionary<string, string>(),
            RuntimeValue.Null());

        var auth = Assert.IsType<RequestAuthContextInstance>(request.Get("auth", null).AsObject());
        auth.CallMethod("authenticateBearerJwt", new List<RuntimeValue> { RuntimeValue.String(secret) });

        var ex = Assert.Throws<WebRuntimeException>(() =>
            auth.CallMethod("requireRole", new List<RuntimeValue> { RuntimeValue.String("admin") }));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("Forbidden", ex.ErrorCode);
    }

    [Fact]
    public void RequestAuthContext_AuthenticateCookieJwt_ReadsSignedCookieToken()
    {
        const string jwtSecret = "web-runtime-cookie-jwt-secret";
        const string cookieSecret = "web-runtime-cookie-signing-secret";

        var token = CreateJwt(jwtSecret, "cookie-user-22", "reviewer");
        var cookieHeader = BuiltInFunctions.CallBuiltIn(
            "createSecureCookie",
            new List<RuntimeValue>
            {
                RuntimeValue.String("session"),
                RuntimeValue.String(token),
                RuntimeValue.String(cookieSecret)
            },
            null!).AsString();
        var cookieValue = cookieHeader.Split(';')[0].Split('=', 2)[1];

        var request = new RequestContextInstance(
            "GET",
            "/secure",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["session"] = cookieValue },
            RuntimeValue.Null());

        var auth = Assert.IsType<RequestAuthContextInstance>(request.Get("auth", null).AsObject());
        var claims = auth.CallMethod("authenticateCookieJwt", new List<RuntimeValue>
        {
            RuntimeValue.String("session"),
            RuntimeValue.String(jwtSecret),
            RuntimeValue.String(cookieSecret)
        });
        var claimsObject = Assert.IsType<JsonObject>(claims.AsObject());

        Assert.True(auth.IsVerified);
        Assert.Equal("cookie-user-22", auth.VerifiedSubject);
        Assert.Equal("reviewer", claimsObject.Get("role", null).AsString());
        Assert.Equal(token, auth.Get("token", null).AsString());
    }

    [Fact]
    public void ResponseContext_StatusAndJsonHelpers_Work()
    {
        var response = new ResponseContextInstance();

        var statusResult = response.CallMethod("status", new List<RuntimeValue> { RuntimeValue.Integer(201) });
        var jsonResult = response.CallMethod("json", new List<RuntimeValue> { RuntimeValue.String("created") });

        Assert.Equal(ValueType.Object, statusResult.Type);
        Assert.Equal(ValueType.Object, jsonResult.Type);
        Assert.Equal(201, response.StatusCode);
        Assert.True(response.IsCommitted);
        Assert.Equal("application/json; charset=utf-8", response.ContentType);
        Assert.Equal("created", response.Body.AsString());
    }

    [Fact]
    public void ResponseContext_RedirectHelper_DefaultsToSeeOther()
    {
        var response = new ResponseContextInstance();

        response.CallMethod("redirect", new List<RuntimeValue> { RuntimeValue.String("/login") });

        Assert.True(response.IsCommitted);
        Assert.Equal(303, response.StatusCode);
        Assert.Equal("/login", response.Headers["Location"]);
    }

    [Fact]
    public void BuiltInRedirect_CreatesSeeOtherResponseObject()
    {
        var redirect = BuiltInFunctions.CallBuiltIn(
            "redirect",
            new List<RuntimeValue> { RuntimeValue.String("/dashboard") },
            null!);
        var payload = Assert.IsType<JsonObject>(redirect.AsObject());
        var headers = Assert.IsType<JsonObject>(payload.Get("headers", null).AsObject());

        Assert.Equal(303, payload.Get("status", null).AsInteger());
        Assert.Equal("/dashboard", headers.Get("Location", null).AsString());
    }

    [Fact]
    public void BuiltInRedirectTo_RemainsSupported_AndUsesSeeOther()
    {
        var redirect = BuiltInFunctions.CallBuiltIn(
            "RedirectTo",
            new List<RuntimeValue> { RuntimeValue.String("/legacy") },
            null!);
        var payload = Assert.IsType<JsonObject>(redirect.AsObject());

        Assert.Equal(303, payload.Get("status", null).AsInteger());
    }

    [Fact]
    public void ApplyPathBaseToRelativeRedirect_PrependsWhenLocationIsAppRelative()
    {
        Assert.Equal("/schoolprep/login", WebRuntimeHelpers.ApplyPathBaseToRelativeRedirect("/login", "/schoolprep"));
    }

    [Fact]
    public void ApplyPathBaseToRelativeRedirect_DoesNotDoublePathBasePrefix()
    {
        Assert.Equal(
            "/schoolprep/login?returnTo=%2F",
            WebRuntimeHelpers.ApplyPathBaseToRelativeRedirect("/schoolprep/login?returnTo=%2F", "/schoolprep"));
    }

    [Fact]
    public void ApplyPathBaseToRelativeRedirect_LeavesAbsoluteUrlsUnchanged()
    {
        Assert.Equal("https://example.com/y", WebRuntimeHelpers.ApplyPathBaseToRelativeRedirect("https://example.com/y", "/schoolprep"));
    }

    [Fact]
    public void ResponseContext_CookieHelper_UsesSecureDefaults()
    {
        var response = new ResponseContextInstance();

        response.CallMethod(
            "cookie",
            new List<RuntimeValue>
            {
                RuntimeValue.String("csrf_token"),
                RuntimeValue.String("token-value")
            });

        var cookieHeader = WebRuntimeHelpers.CreateCookieHeader(
            "csrf_token",
            "token-value",
            RuntimeValue.Null(),
            useSecureDefaults: true);

        Assert.Contains("HttpOnly", cookieHeader);
        Assert.Contains("Secure", cookieHeader);
        Assert.Contains("SameSite=Lax", cookieHeader);
    }

    [Fact]
    public void ResponseContext_SendBase64_RoundTripsBinaryBytes()
    {
        var bytes = new byte[] { 0x00, 0xFF, 0x10, 0x80, 0x7F };
        var response = new ResponseContextInstance();
        response.CallMethod("setContentType", new List<RuntimeValue> { RuntimeValue.String("application/pdf") });
        response.CallMethod("sendBase64", new List<RuntimeValue> { RuntimeValue.String(Convert.ToBase64String(bytes)) });

        Assert.True(response.IsCommitted);
        var encode = typeof(ResponseContextInstance).GetMethod(
            "EncodeBodyBytes",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(encode);
        var got = Assert.IsType<byte[]>(encode!.Invoke(response, null));
        Assert.Equal(bytes, got);
    }

    [Fact]
    public void ResponseContext_SendBase64_RejectsInvalidBase64()
    {
        var response = new ResponseContextInstance();
        var ex = Assert.Throws<Exception>(() =>
            response.CallMethod("sendBase64", new List<RuntimeValue> { RuntimeValue.String("not-valid!!!") }));
        Assert.Contains("base64", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseContext_ContentTypeAndClearCookieHelpers_Work()
    {
        var response = new ResponseContextInstance();

        response.CallMethod("setContentType", new List<RuntimeValue> { RuntimeValue.String("text/csv; charset=utf-8") });
        response.CallMethod("clearCookie", new List<RuntimeValue> { RuntimeValue.String("session") });

        var setCookieField = typeof(ResponseContextInstance).GetField("_setCookieHeaders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var setCookieHeaders = Assert.IsType<List<string>>(setCookieField!.GetValue(response));

        Assert.Equal("text/csv; charset=utf-8", response.Get("contentType", null).AsString());
        Assert.False(response.Get("sent", null).AsBoolean());
        Assert.True(response.HasHeaders);
        Assert.Contains(setCookieHeaders, value => value.Contains("session=", StringComparison.Ordinal) && value.Contains("Max-Age=0", StringComparison.Ordinal));
    }

    [Fact]
    public void WebRuntimeHelpers_StatusForbidsHttpResponseBody_MatchesHttpListenerRules()
    {
        Assert.True(WebRuntimeHelpers.StatusForbidsHttpResponseBody(100));
        Assert.True(WebRuntimeHelpers.StatusForbidsHttpResponseBody(101));
        Assert.True(WebRuntimeHelpers.StatusForbidsHttpResponseBody(204));
        Assert.True(WebRuntimeHelpers.StatusForbidsHttpResponseBody(205));
        Assert.True(WebRuntimeHelpers.StatusForbidsHttpResponseBody(304));
        Assert.False(WebRuntimeHelpers.StatusForbidsHttpResponseBody(200));
        Assert.False(WebRuntimeHelpers.StatusForbidsHttpResponseBody(303));
        Assert.False(WebRuntimeHelpers.StatusForbidsHttpResponseBody(401));
        Assert.True(WebRuntimeHelpers.ShouldOmitHttpResponseBody(null, 204));
        Assert.False(WebRuntimeHelpers.ShouldOmitHttpResponseBody(null, 200));
        Assert.False(WebRuntimeHelpers.IsHeadRequest(null));
    }

    [Fact]
    public void ConvertTranspiledResultToRuntimeValue_DictionaryLiteral_BecomesJsonObject()
    {
        var payload = new Dictionary<string, object?>
        {
            ["app"] = "tapscore",
            ["status"] = "ok",
            ["scores"] = new List<object>
            {
                new Dictionary<string, object?> { ["name"] = "Ada", ["points"] = 12 }
            }
        };

        var value = WebRuntimeHelpers.ConvertTranspiledResultToRuntimeValue(payload);
        Assert.Equal(ValueType.Object, value.Type);
        var obj = Assert.IsType<JsonObject>(value.AsObject());
        Assert.Equal("tapscore", obj.Get("app", null).AsString());
        Assert.Equal("ok", obj.Get("status", null).AsString());
        var scores = obj.Get("scores", null).AsArray();
        Assert.Single(scores);
        var row = Assert.IsType<JsonObject>(scores[0].AsObject());
        Assert.Equal("Ada", row.Get("name", null).AsString());
        Assert.Equal(12, row.Get("points", null).AsInteger());
    }
}
