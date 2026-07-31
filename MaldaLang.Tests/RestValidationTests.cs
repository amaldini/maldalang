// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace MaldaLang.Tests;

public class RestValidationTests
{
    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public void ValidateRequest_ReturnsErrors_ForMissingAndWrongTypes()
    {
        var schema = new JsonObject();
        var query = new JsonObject();
        query.Set("limit", RuntimeValue.String("int|required|min=1"));
        schema.Set("query", RuntimeValue.Object(query));

        var pathParams = new Dictionary<string, string>();
        var queryParams = new Dictionary<string, string> { ["limit"] = "abc" };
        var body = RuntimeValue.Null();

        var isValid = WebRuntimeHelpers.ValidateRequest(
            RuntimeValue.Object(schema),
            pathParams,
            queryParams,
            body,
            out var errors);

        Assert.False(isValid);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Location == "query" && e.Field == "limit");
    }

    [Fact]
    public void ValidateRequest_Passes_ForValidPathQueryAndBody()
    {
        var schema = new JsonObject();

        var pathRules = new JsonObject();
        pathRules.Set("id", RuntimeValue.String("int|required|min=1"));
        schema.Set("path", RuntimeValue.Object(pathRules));

        var queryRules = new JsonObject();
        queryRules.Set("q", RuntimeValue.String("string|required|minLength=2"));
        schema.Set("query", RuntimeValue.Object(queryRules));

        var bodyRules = new JsonObject();
        var nameRule = new JsonObject();
        nameRule.Set("type", RuntimeValue.String("string"));
        nameRule.Set("required", RuntimeValue.Boolean(true));
        nameRule.Set("minLength", RuntimeValue.Integer(3));
        bodyRules.Set("name", RuntimeValue.Object(nameRule));
        schema.Set("body", RuntimeValue.Object(bodyRules));

        var body = new JsonObject();
        body.Set("name", RuntimeValue.String("Alice"));

        var isValid = WebRuntimeHelpers.ValidateRequest(
            RuntimeValue.Object(schema),
            new Dictionary<string, string> { ["id"] = "42" },
            new Dictionary<string, string> { ["q"] = "ok" },
            RuntimeValue.Object(body),
            out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);
    }

    [Fact]
    public void ComposeRoutePath_ComposesGroupAndVersion()
    {
        var path = WebRuntimeHelpers.ComposeRoutePath("/users/{id}", "/api", "v2");
        Assert.Equal("/api/v2/users/{id}", path);
    }

    [Fact]
    public async Task RestServer_Validation_E2E_Returns400AndCorrelationId()
    {
        var port = GetAvailablePort();
        var routePath = $"/validation/{Guid.NewGuid():N}/{{id}}";
        var fullPath = "/api/v1" + routePath.Replace("{id}", "abc");
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute(
            "GET",
            routePath,
            "llmServerHealthCheck",
            new List<string> { "id", "q" },
            null,
            "/api",
            "v1",
            null,
            "{\"path\":{\"id\":\"int|required|min=1\"},\"query\":{\"q\":\"string|required|minLength=2\"}}");

        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{fullPath}?q=x");
            request.Headers.Add("X-Correlation-ID", "test-correlation-id");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var corrValues));
            Assert.Contains("test-correlation-id", corrValues!);
            Assert.Equal("ValidationError", root.GetProperty("error").GetString());
            Assert.Equal("test-correlation-id", root.GetProperty("correlationId").GetString());
            Assert.True(root.TryGetProperty("details", out var details));
            Assert.True(details.GetArrayLength() >= 1);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }
}

