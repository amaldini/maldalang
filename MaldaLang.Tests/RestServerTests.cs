// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Text.Json;

namespace MaldaLang.Tests;

public class RestServerTests
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

    private static Interpreter.Interpreter LoadInterpreterFromSource(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interpreter = new Interpreter.Interpreter();
        interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        return interpreter;
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public void RestServer_Creation_WithValidPort()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new RestServerInstance(8080, null, interpreter);
        Assert.Equal(8080, server.Get("port", null).AsInteger());
        Assert.False(server.Get("isRunning", null).AsBoolean());
    }
    
    [Fact]
    public void RestServer_Creation_WithHost()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new RestServerInstance(8080, "0.0.0.0", interpreter);
        Assert.Equal(8080, server.Get("port", null).AsInteger());
    }
    
    [Fact]
    public void RestServer_StartStop_Works()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new RestServerInstance(GetAvailablePort(), null, interpreter);
        
        server.CallMethod("start", new List<RuntimeValue>());
        Assert.True(server.Get("isRunning", null).AsBoolean());
        
        server.CallMethod("stop", new List<RuntimeValue>());
        Assert.False(server.Get("isRunning", null).AsBoolean());
    }
    
    [Fact]
    public void RestServer_CORS_Configuration()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new RestServerInstance(8085, null, interpreter);
        
        server.CallMethod("enableCORS", new List<RuntimeValue> { RuntimeValue.Boolean(true) });
        server.CallMethod("setCORSOrigin", new List<RuntimeValue> { RuntimeValue.String("https://example.com") });
        
        var methods = new List<RuntimeValue> 
        { 
            RuntimeValue.String("GET"), 
            RuntimeValue.String("POST") 
        };
        server.CallMethod("setCORSMethods", new List<RuntimeValue> { RuntimeValue.Array(methods) });
        
        // If we got here without exception, CORS configuration works
        Assert.True(true);
    }
    
    [Fact]
    public void RestServer_Swagger_Configuration()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new RestServerInstance(8086, null, interpreter);
        
        server.CallMethod("enableSwagger", new List<RuntimeValue> { RuntimeValue.Boolean(true) });
        server.CallMethod("enableSwagger", new List<RuntimeValue> { RuntimeValue.Boolean(false) });
        
        // If we got here without exception, Swagger configuration works
        Assert.True(true);
    }

    [Fact]
    public void RestServer_UseMiddleware_AcceptsFunctionAndString()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new RestServerInstance(8087, null, interpreter);

        server.CallMethod("use", new List<RuntimeValue> { RuntimeValue.Function(new FunctionValue()) });
        server.CallMethod("use", new List<RuntimeValue> { RuntimeValue.String("globalMiddleware") });

        var except = RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("/api/health") });
        var options = new DictionaryInstance();
        options.SetEntry("except", except);
        server.CallMethod(
            "use",
            new List<RuntimeValue>
            {
                RuntimeValue.String("globalMiddleware"),
                RuntimeValue.Object(options)
            });
    }

    [Fact]
    public async Task RestServer_MiddlewareRequestHelpersAndBagBindings_WorkTogether()
    {
        var port = GetAvailablePort();
        var source = @"
            function attachTenant(req, res, next) {
                req.tenant = ""tenant-42"";
                next();
            }

            @GET(""/api/items/{id}"")
            function getItem(req, params, query, headers, cookies) {
                return {
                    ""tenant"": req.tenant,
                    ""id"": params.id,
                    ""idFromHelper"": req.param(""id""),
                    ""search"": query.search,
                    ""trace"": req.header(""X-Trace"", ""missing""),
                    ""session"": req.cookie(""session"", ""none""),
                    ""host"": req.host,
                    ""contentType"": req.contentType,
                    ""hasTrace"": req.hasHeader(""X-Trace""),
                    ""headerBag"": headers[""X-Trace""],
                    ""cookieBag"": cookies.session
                };
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new RestServerInstance(port, "localhost", interpreter);
        server.CallMethod("use", new List<RuntimeValue> { RuntimeValue.String("attachTenant") });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/items/123?search=alpha");
            request.Headers.Add("X-Trace", "trace-abc");
            request.Headers.Add("Cookie", "session=cookie-123");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("tenant-42", root.GetProperty("tenant").GetString());
            Assert.Equal("123", root.GetProperty("id").GetString());
            Assert.Equal("123", root.GetProperty("idFromHelper").GetString());
            Assert.Equal("alpha", root.GetProperty("search").GetString());
            Assert.Equal("trace-abc", root.GetProperty("trace").GetString());
            Assert.Equal("trace-abc", root.GetProperty("headerBag").GetString());
            Assert.Equal("cookie-123", root.GetProperty("session").GetString());
            Assert.Equal("cookie-123", root.GetProperty("cookieBag").GetString());
            Assert.True(root.GetProperty("hasTrace").GetBoolean());
            Assert.Contains("localhost", root.GetProperty("host").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RestServer_RequestAuthAuthenticateBearerJwt_ExposesVerifiedClaims()
    {
        var port = GetAvailablePort();
        const string secret = "rest-auth-slice-secret";
        var source = @"
            function requireAuth(req, res, next) {
                req.auth.authenticateBearerJwt(""rest-auth-slice-secret"");
                next();
            }

            @GET(""/api/secure"")
            @Use(""requireAuth"")
            function secure(req) {
                return {
                    ""verified"": req.auth.verified,
                    ""sub"": req.auth.sub,
                    ""role"": req.auth.claims.role,
                    ""hasToken"": req.auth.token != null
                };
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new RestServerInstance(port, "localhost", interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            var token = CreateJwt(secret, "user-77", "editor");
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/secure");
            request.Headers.Add("Authorization", $"Bearer {token}");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(root.GetProperty("verified").GetBoolean());
            Assert.Equal("user-77", root.GetProperty("sub").GetString());
            Assert.Equal("editor", root.GetProperty("role").GetString());
            Assert.True(root.GetProperty("hasToken").GetBoolean());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RestServer_RequestAuthRequireRole_ReturnsStandardized403()
    {
        var port = GetAvailablePort();
        const string secret = "rest-auth-role-guard-secret";
        var source = @"
            function requireAdmin(req, res, next) {
                req.auth.authenticateBearerJwt(""rest-auth-role-guard-secret"");
                req.auth.requireRole(""admin"");
                next();
            }

            @GET(""/api/admin"")
            @Use(""requireAdmin"")
            function admin(req) {
                return {
                    ""ok"": true,
                    ""role"": req.auth.claim(""role"", ""unknown"")
                };
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new RestServerInstance(port, "localhost", interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            var token = CreateJwt(secret, "user-88", "editor");
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/admin");
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("X-Correlation-ID", "corr-rest-forbidden");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            SecurityTestUtils.AssertStandardErrorPayload(doc.RootElement, "Forbidden", "corr-rest-forbidden", 403);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RestServer_RequestRequireValid_ReturnsStandardizedValidationError()
    {
        var port = GetAvailablePort();
        var source = @"
            @POST(""/api/users/{id}"")
            function updateUser(req) {
                req.requireValid({
                    ""path"": {
                        ""id"": ""int|required""
                    },
                    ""query"": {
                        ""mode"": ""string|required""
                    },
                    ""body"": {
                        ""name"": ""string|required|minLength=3""
                    }
                });

                return {
                    ""ok"": true
                };
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new RestServerInstance(port, "localhost", interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/api/users/not-a-number");
            request.Headers.Add("X-Correlation-ID", "corr-req-require-valid");
            request.Content = new StringContent("{\"name\":\"ab\"}", System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("ValidationError", root.GetProperty("error").GetString());
            Assert.Equal("corr-req-require-valid", root.GetProperty("correlationId").GetString());
            Assert.Equal(3, root.GetProperty("details").GetArrayLength());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RestServer_ReturnedStandardErrorEnvelope_PreservesStatusAndBody()
    {
        var port = GetAvailablePort();
        var path = $"/api/error/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute("GET", path, "ReturnStandardErrorPayload", new List<string>(), null);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync($"http://localhost:{port}{path}");
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            Assert.Equal(422, root.GetProperty("status").GetInt32());
            Assert.Equal("BusinessRuleViolation", root.GetProperty("error").GetString());
            Assert.Equal("handler-correlation", root.GetProperty("correlationId").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RestServer_TranspiledDictionaryLiteral_SerializesJsonObject()
    {
        var port = GetAvailablePort();
        var path = $"/api/health/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute("GET", path, "ReturnDictionaryHealthPayload", new List<string>(), null);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync($"http://localhost:{port}{path}");
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("tapscore", root.GetProperty("app").GetString());
            Assert.Equal("ok", root.GetProperty("status").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task RestServer_TranspiledNestedDictionaryList_SerializesJsonArray()
    {
        var port = GetAvailablePort();
        var path = $"/api/scores/{Guid.NewGuid():N}";
        var server = new RestServerInstance(port, "localhost", null);

        RestServerInstance.RegisterTranspiledRoute("GET", path, "ReturnDictionaryScoresPayload", new List<string>(), null);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync($"http://localhost:{port}{path}");
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(root.GetProperty("ok").GetBoolean());
            var scores = root.GetProperty("scores");
            Assert.Equal(1, scores.GetArrayLength());
            Assert.Equal("Ada", scores[0].GetProperty("name").GetString());
            Assert.Equal(12, scores[0].GetProperty("points").GetInt32());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }
}

public class RouteRegistryTests
{
    [Fact]
    public void RouteRegistry_RegisterRoute_StoresRoute()
    {
        var registry = new RouteRegistry();
        var function = new FunctionValue();
        var paramNames = new List<string> { "id" };
        var metadata = new RouteMetadata("/api", "v1", new List<string> { "authMiddleware" }, RuntimeValue.String("{\"query\":{\"id\":\"int|required\"}}"));
        
        registry.RegisterRoute("GET", "/api/v1/users/{id}", function, "testFunction", paramNames, null, metadata);
        
        var routes = registry.GetAllRoutes();
        Assert.Single(routes);
        Assert.Equal("GET", routes[0].Method);
        Assert.Equal("/api/v1/users/{id}", routes[0].PathPattern);
        Assert.Equal("/api", routes[0].Metadata.GroupPrefix);
        Assert.Equal("v1", routes[0].Metadata.VersionPrefix);
        Assert.Contains("authMiddleware", routes[0].Metadata.MiddlewareFunctionNames);
    }
    
    [Fact]
    public void RouteRegistry_MatchRoute_ExactMatch()
    {
        var registry = new RouteRegistry();
        var function = new FunctionValue();
        var paramNames = new List<string>();
        
        registry.RegisterRoute("GET", "/api/users", function, "testFunction", paramNames, null);
        
        var matched = registry.MatchRoute("GET", "/api/users", out var route, out var pathParams);
        
        Assert.True(matched);
        Assert.NotNull(route);
        Assert.Equal("/api/users", route!.PathPattern);
        Assert.Empty(pathParams);
    }
    
    [Fact]
    public void RouteRegistry_MatchRoute_PathParameters()
    {
        var registry = new RouteRegistry();
        var function = new FunctionValue();
        var paramNames = new List<string> { "id" };
        
        registry.RegisterRoute("GET", "/api/users/{id}", function, "testFunction", paramNames, null);
        
        var matched = registry.MatchRoute("GET", "/api/users/123", out var route, out var pathParams);
        
        Assert.True(matched);
        Assert.NotNull(route);
        Assert.Equal("123", pathParams["id"]);
    }
    
    [Fact]
    public void RouteRegistry_ExtractQueryParams()
    {
        var registry = new RouteRegistry();
        
        var queryParams = registry.ExtractQueryParams("?limit=10&offset=20");
        
        Assert.Equal("10", queryParams["limit"]);
        Assert.Equal("20", queryParams["offset"]);
    }
    
    [Fact]
    public void RouteRegistry_ValidateRouteConflicts_DetectsDuplicates()
    {
        var registry = new RouteRegistry();
        var function1 = new FunctionValue();
        var function2 = new FunctionValue();
        var paramNames = new List<string>();
        
        registry.RegisterRoute("GET", "/api/users", function1, "testFunction1", paramNames, null);
        registry.RegisterRoute("GET", "/api/users", function2, "testFunction2", paramNames, null);
        
        Assert.Throws<Exception>(() => registry.ValidateRouteConflicts());
    }
}