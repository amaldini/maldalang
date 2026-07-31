using System;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

namespace MaldaLang.Tests;

public class SecurityMiddlewareTests
{
    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CreateToken(int expiresInSeconds)
    {
        var payload = new JsonObject();
        payload.Set("sub", RuntimeValue.String("secure-user"));

        var token = BuiltInFunctions.CallBuiltIn(
            "createJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.Object(payload),
                RuntimeValue.String(GeneratedCode.Program.JwtSecret),
                RuntimeValue.Integer(expiresInSeconds)
            },
            null!);
        return token.AsString();
    }

    [Fact]
    public async Task MissingToken_ReturnsStandardized401WithCorrelationId()
    {
        var port = GetAvailablePort();
        var path = $"/api/secure/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute(
            "GET",
            path,
            "ProtectedRouteHandler",
            new List<string>(),
            null,
            null,
            null,
            new List<string> { "AuthGuardMiddleware" },
            null);

        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            request.Headers.Add("X-Correlation-ID", "corr-missing-token");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(response.Headers.TryGetValues(WebRuntimeHelpers.CorrelationIdHeader, out var corrValues));
            Assert.Contains("corr-missing-token", corrValues!);
            SecurityTestUtils.AssertStandardErrorPayload(doc.RootElement, "MissingToken", "corr-missing-token", 401);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task InvalidToken_ReturnsStandardized401WithCorrelationId()
    {
        var port = GetAvailablePort();
        var path = $"/api/secure/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute(
            "GET",
            path,
            "ProtectedRouteHandler",
            new List<string>(),
            null,
            null,
            null,
            new List<string> { "AuthGuardMiddleware" },
            null);

        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            request.Headers.Add("X-Correlation-ID", "corr-invalid-token");
            request.Headers.Add("Authorization", "Bearer invalid.jwt.value");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(doc.RootElement, "InvalidToken", "corr-invalid-token", 401);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task ExpiredToken_ReturnsStandardized401WithCorrelationId()
    {
        var port = GetAvailablePort();
        var path = $"/api/secure/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute(
            "GET",
            path,
            "ProtectedRouteHandler",
            new List<string>(),
            null,
            null,
            null,
            new List<string> { "AuthGuardMiddleware" },
            null);

        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            var expiredToken = CreateToken(-2);

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            request.Headers.Add("X-Correlation-ID", "corr-expired-token");
            request.Headers.Add("Authorization", $"Bearer {expiredToken}");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(doc.RootElement, "TokenExpired", "corr-expired-token", 401);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task ProtectedRoute_WithValidToken_ReturnsSuccess()
    {
        var port = GetAvailablePort();
        var path = $"/api/secure/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute(
            "GET",
            path,
            "ProtectedRouteHandler",
            new List<string>(),
            null,
            null,
            null,
            new List<string> { "AuthGuardMiddleware" },
            null);

        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            var validToken = CreateToken(120);

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            request.Headers.Add("Authorization", $"Bearer {validToken}");
            request.Headers.Add("X-Correlation-ID", "corr-success");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(doc.RootElement.TryGetProperty("ok", out var ok));
            Assert.True(ok.GetBoolean());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task CsrfFailure_ReturnsStandardized403WithCorrelationId()
    {
        var port = GetAvailablePort();
        var path = $"/api/secure-mutation/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute(
            "POST",
            path,
            "ProtectedMutationHandler",
            new List<string> { "body" },
            null,
            null,
            null,
            new List<string> { "AuthGuardMiddleware" },
            null);

        server.CallMethod(
            "enableCsrf",
            new List<RuntimeValue>
            {
                RuntimeValue.String("csrf-test-secret")
            });
        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            var validToken = CreateToken(120);
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}{path}");
            request.Headers.Add("Authorization", $"Bearer {validToken}");
            request.Headers.Add("X-Correlation-ID", "corr-csrf-fail");
            request.Content = new StringContent("{\"name\":\"x\"}", System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(doc.RootElement, "CsrfValidationFailed", "corr-csrf-fail", 403);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RateLimitExceeded_ReturnsStandardized429WithCorrelationId()
    {
        var port = GetAvailablePort();
        var path = $"/api/rate-limit/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute(
            "GET",
            path,
            "ProtectedRouteHandler",
            new List<string>(),
            null,
            null,
            null,
            null,
            null);

        server.CallMethod(
            "setRateLimit",
            new List<RuntimeValue>
            {
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(60),
                RuntimeValue.String("ip")
            });
        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            using var client = new HttpClient();
            using var first = await client.GetAsync($"http://localhost:{port}{path}");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var secondReq = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            secondReq.Headers.Add("X-Correlation-ID", "corr-rate-limit");
            using var second = await client.SendAsync(secondReq);
            var body = await second.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal((HttpStatusCode)429, second.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(doc.RootElement, "RateLimitExceeded", "corr-rate-limit", 429);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task AuthCsrfRateLimit_MiddlewareComposition_Works()
    {
        var port = GetAvailablePort();
        var path = $"/api/composed/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute(
            "GET",
            path,
            "ProtectedRouteHandler",
            new List<string>(),
            null,
            null,
            null,
            new List<string> { "AuthGuardMiddleware" },
            null);
        RestServerInstance.RegisterTranspiledRoute(
            "POST",
            path,
            "ProtectedMutationHandler",
            new List<string> { "body" },
            null,
            null,
            null,
            new List<string> { "AuthGuardMiddleware" },
            null);

        server.CallMethod("enableCsrf", new List<RuntimeValue> { RuntimeValue.String("csrf-compose-secret") });
        server.CallMethod(
            "setRateLimit",
            new List<RuntimeValue>
            {
                RuntimeValue.Integer(2),
                RuntimeValue.Integer(60),
                RuntimeValue.String("ip")
            });
        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            var validToken = CreateToken(120);
            using var client = new HttpClient();

            var bootstrap = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            bootstrap.Headers.Add("Authorization", $"Bearer {validToken}");
            using var bootstrapResponse = await client.SendAsync(bootstrap);
            Assert.Equal(HttpStatusCode.OK, bootstrapResponse.StatusCode);
            var csrfToken = SecurityTestUtils.ExtractCookieValue(bootstrapResponse, "csrf_token");
            Assert.False(string.IsNullOrWhiteSpace(csrfToken));

            var firstMutation = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}{path}");
            firstMutation.Headers.Add("Authorization", $"Bearer {validToken}");
            firstMutation.Headers.Add("X-CSRF-Token", csrfToken);
            firstMutation.Headers.Add("Cookie", $"csrf_token={Uri.EscapeDataString(csrfToken)}");
            firstMutation.Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json");
            using var firstMutationResponse = await client.SendAsync(firstMutation);
            Assert.Equal(HttpStatusCode.OK, firstMutationResponse.StatusCode);

            var secondMutation = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}{path}");
            secondMutation.Headers.Add("Authorization", $"Bearer {validToken}");
            secondMutation.Headers.Add("X-CSRF-Token", csrfToken);
            secondMutation.Headers.Add("X-Correlation-ID", "corr-compose-rate");
            secondMutation.Headers.Add("Cookie", $"csrf_token={Uri.EscapeDataString(csrfToken)}");
            secondMutation.Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json");
            using var secondMutationResponse = await client.SendAsync(secondMutation);
            var secondBody = await secondMutationResponse.Content.ReadAsStringAsync();
            using var secondDoc = JsonDocument.Parse(secondBody);

            Assert.Equal((HttpStatusCode)429, secondMutationResponse.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(secondDoc.RootElement, "RateLimitExceeded", "corr-compose-rate", 429);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RateLimit_SubStrategy_UsesCanonicalAuthHeadersWhenPresent()
    {
        var port = GetAvailablePort();
        var path = $"/api/sub-rate/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute("GET", path, "ProtectedRouteHandler", new List<string>(), null);
        server.CallMethod("configureTrustedProxy", new List<RuntimeValue> { RuntimeValue.Boolean(true) });
        server.CallMethod(
            "setRateLimit",
            new List<RuntimeValue>
            {
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(60),
                RuntimeValue.String("sub")
            });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();

            var first = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            first.Headers.Add("X-Forwarded-For", "10.0.0.1");
            first.Headers.Add(WebRuntimeHelpers.AuthVerifiedHeader, "true");
            first.Headers.Add(WebRuntimeHelpers.AuthSubjectHeader, "user-42");
            using var firstResponse = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var second = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            second.Headers.Add("X-Forwarded-For", "10.0.0.99");
            second.Headers.Add(WebRuntimeHelpers.AuthVerifiedHeader, "true");
            second.Headers.Add(WebRuntimeHelpers.AuthSubjectHeader, "user-42");
            second.Headers.Add("X-Correlation-ID", "corr-sub-key");
            using var secondResponse = await client.SendAsync(second);
            var secondBody = await secondResponse.Content.ReadAsStringAsync();
            using var secondDoc = JsonDocument.Parse(secondBody);

            Assert.Equal((HttpStatusCode)429, secondResponse.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(secondDoc.RootElement, "RateLimitExceeded", "corr-sub-key", 429);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public void RequestAuthContext_SetVerifiedSub_UpdatesCanonicalSurface()
    {
        var request = new RequestContextInstance(
            "GET",
            "/api/test",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            RuntimeValue.Null());

        var auth = request.Get("auth", null).AsObject() as RequestAuthContextInstance;
        Assert.NotNull(auth);

        auth!.CallMethod("setVerifiedSub", new List<RuntimeValue> { RuntimeValue.String("user-ctx-42") });
        Assert.True(request.Get("auth", null).AsObject() is RequestAuthContextInstance updated && updated.IsVerified);
        Assert.Equal("user-ctx-42", request.Get("verifiedSub", null).AsString());
    }

    [Fact]
    public async Task RateLimit_SubStrategy_FallsBackToIpWhenSubMissing()
    {
        var port = GetAvailablePort();
        var path = $"/api/sub-fallback/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute("GET", path, "ProtectedRouteHandler", new List<string>(), null);
        server.CallMethod(
            "setRateLimit",
            new List<RuntimeValue>
            {
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(60),
                RuntimeValue.String("sub")
            });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var first = await client.GetAsync($"http://localhost:{port}{path}");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var second = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            second.Headers.Add("X-Correlation-ID", "corr-sub-fallback");
            using var secondResponse = await client.SendAsync(second);
            var secondBody = await secondResponse.Content.ReadAsStringAsync();
            using var secondDoc = JsonDocument.Parse(secondBody);

            Assert.Equal((HttpStatusCode)429, secondResponse.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(secondDoc.RootElement, "RateLimitExceeded", "corr-sub-fallback", 429);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task TrustedProxy_DefaultDisabled_IgnoresForwardedFor()
    {
        var port = GetAvailablePort();
        var path = $"/api/proxy-default/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute("GET", path, "ProtectedRouteHandler", new List<string>(), null);
        server.CallMethod(
            "setRateLimit",
            new List<RuntimeValue>
            {
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(60),
                RuntimeValue.String("ip")
            });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            var first = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            first.Headers.Add("X-Forwarded-For", "10.1.1.1");
            using var firstResponse = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var second = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            second.Headers.Add("X-Forwarded-For", "10.1.1.2");
            second.Headers.Add("X-Correlation-ID", "corr-proxy-default");
            using var secondResponse = await client.SendAsync(second);
            var secondBody = await secondResponse.Content.ReadAsStringAsync();
            using var secondDoc = JsonDocument.Parse(secondBody);

            Assert.Equal((HttpStatusCode)429, secondResponse.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(secondDoc.RootElement, "RateLimitExceeded", "corr-proxy-default", 429);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task TrustedProxy_Enabled_UsesForwardedForForIpKeying()
    {
        var port = GetAvailablePort();
        var path = $"/api/proxy-enabled/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute("GET", path, "ProtectedRouteHandler", new List<string>(), null);
        server.CallMethod("configureTrustedProxy", new List<RuntimeValue> { RuntimeValue.Boolean(true) });
        server.CallMethod(
            "setRateLimit",
            new List<RuntimeValue>
            {
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(60),
                RuntimeValue.String("ip")
            });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            var first = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            first.Headers.Add("X-Forwarded-For", "10.2.2.1");
            using var firstResponse = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var second = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            second.Headers.Add("X-Forwarded-For", "10.2.2.2");
            using var secondResponse = await client.SendAsync(second);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RateLimitHeaders_WhenEnabled_ReturnsRetryAfterAndRemaining()
    {
        var port = GetAvailablePort();
        var path = $"/api/rate-headers/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute("GET", path, "ProtectedRouteHandler", new List<string>(), null);
        server.CallMethod(
            "setRateLimit",
            new List<RuntimeValue>
            {
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(60),
                RuntimeValue.String("ip")
            });
        server.CallMethod("setRateLimitHeaders", new List<RuntimeValue> { RuntimeValue.Boolean(true), RuntimeValue.Boolean(true) });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var first = await client.GetAsync($"http://localhost:{port}{path}");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.True(first.Headers.Contains("X-RateLimit-Limit"));
            Assert.True(first.Headers.Contains("X-RateLimit-Remaining"));

            var second = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            second.Headers.Add("X-Correlation-ID", "corr-rate-headers");
            using var secondResponse = await client.SendAsync(second);
            var secondBody = await secondResponse.Content.ReadAsStringAsync();
            using var secondDoc = JsonDocument.Parse(secondBody);

            Assert.Equal((HttpStatusCode)429, secondResponse.StatusCode);
            Assert.True(secondResponse.Headers.Contains("Retry-After"));
            Assert.True(secondResponse.Headers.Contains("X-RateLimit-Remaining"));
            SecurityTestUtils.AssertStandardErrorPayload(secondDoc.RootElement, "RateLimitExceeded", "corr-rate-headers", 429);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }
}
