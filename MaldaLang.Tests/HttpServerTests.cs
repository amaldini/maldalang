// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using ValueType = MaldaLang.Interpreter.ValueType;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;

namespace MaldaLang.Tests;

public class HttpServerTests
{
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

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    [Fact]
    public void HttpServer_Creation_WithValidPort()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8080, null, interpreter);
        Assert.Equal(8080, server.Get("port", null).AsInteger());
        Assert.False(server.Get("isRunning", null).AsBoolean());
    }

    [Fact]
    public void HttpServer_Host_DefaultsToLocalhost_AndSetHostNormalizesWildcard()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_HTTP_HOST");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HOST", null);
            var interpreter = new Interpreter.Interpreter();
            var server = new HttpServerInstance(8096, null, interpreter);
            Assert.Equal("localhost", server.Get("host", null).AsString());
            Assert.Equal("http://localhost:8096/", HttpServerInstance.BuildListenerPrefix(server.Host, server.Port));

            server.CallMethod("setHost", new List<RuntimeValue> { RuntimeValue.String("*") });
            Assert.Equal("0.0.0.0", server.Get("host", null).AsString());
            Assert.Equal("http://" + "*:8096/", HttpServerInstance.BuildListenerPrefix(server.Host, server.Port));

            server.CallMethod("setHost", new List<RuntimeValue> { RuntimeValue.String("all") });
            Assert.Equal("0.0.0.0", server.Host);
            Assert.Equal("http://" + "*:8096/", HttpServerInstance.BuildListenerPrefix("0.0.0.0", 8096));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HOST", previous);
        }
    }

    [Fact]
    public void HttpServer_Creation_WithExplicitHost_AndEnvFallback()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_HTTP_HOST");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HOST", "127.0.0.1");
            var interpreter = new Interpreter.Interpreter();
            var fromEnv = new HttpServerInstance(8097, null, interpreter);
            Assert.Equal("127.0.0.1", fromEnv.Host);

            var withHost = new HttpServerInstance(8098, null, interpreter, null, "0.0.0.0");
            Assert.Equal("0.0.0.0", withHost.Host);
            Assert.Equal("http://" + "*:8098/", HttpServerInstance.BuildListenerPrefix(withHost.Host, withHost.Port));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HOST", previous);
        }
    }

    [Fact]
    public void HttpServer_SetHost_ThrowsWhenRunning_OrEmpty()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_HTTP_HOST");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HOST", null);
            var interpreter = new Interpreter.Interpreter();
            var server = new HttpServerInstance(8099, null, interpreter);
            Assert.Throws<Exception>(() =>
                server.CallMethod("setHost", new List<RuntimeValue> { RuntimeValue.String("") }));

            server.CallMethod("start", new List<RuntimeValue>());
            try
            {
                Assert.Throws<Exception>(() =>
                    server.CallMethod("setHost", new List<RuntimeValue> { RuntimeValue.String("0.0.0.0") }));
            }
            finally
            {
                server.CallMethod("stop", new List<RuntimeValue>());
            }
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HOST", previous);
        }
    }

    [Fact]
    public async Task HttpServer_EnableHttps_ServesOverTls()
    {
        var previousHttps = System.Environment.GetEnvironmentVariable("MALDA_HTTP_HTTPS");
        var previousCert = System.Environment.GetEnvironmentVariable("MALDA_HTTP_CERT");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HTTPS", null);
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_CERT", null);

            var tempDir = Path.Combine(Path.GetTempPath(), "malda_https_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            var pfxPath = Path.Combine(tempDir, "test.pfx");
            const string pfxPassword = "test-pass";
            WriteSelfSignedPfx(pfxPath, pfxPassword);

            var port = GetFreeTcpPort();
            var interpreter = new Interpreter.Interpreter();
            var server = new HttpServerInstance(port, null, interpreter);
            server.SetHost("127.0.0.1");
            server.EnableHttps(pfxPath, pfxPassword);
            Assert.True(server.Get("https", null).AsBoolean());
            Assert.Equal(pfxPath, server.Get("certPath", null).AsString());

            server.CallMethod("setHTML", new List<RuntimeValue> { RuntimeValue.String("<html><body>https-ok</body></html>") });
            server.CallMethod("start", new List<RuntimeValue>());
            try
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                HttpResponseMessage? response = null;
                Exception? lastError = null;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        response = await client.GetAsync($"https://127.0.0.1:{port}/");
                        lastError = null;
                        break;
                    }
                    catch (Exception ex) when (attempt < 19)
                    {
                        lastError = ex;
                        await Task.Delay(50);
                    }
                }
                if (response == null)
                    throw lastError ?? new Exception("HTTPS request failed with no response");
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode);
                Assert.Contains("https-ok", body, StringComparison.Ordinal);
            }
            finally
            {
                server.CallMethod("stop", new List<RuntimeValue>());
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HTTPS", previousHttps);
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_CERT", previousCert);
        }
    }

    [Fact]
    public void HttpServer_EnableHttps_MissingCert_FailsAtStart()
    {
        var previousHttps = System.Environment.GetEnvironmentVariable("MALDA_HTTP_HTTPS");
        var previousCert = System.Environment.GetEnvironmentVariable("MALDA_HTTP_CERT");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HTTPS", null);
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_CERT", null);

            var port = GetFreeTcpPort();
            var interpreter = new Interpreter.Interpreter();
            var server = new HttpServerInstance(port, null, interpreter);
            server.SetHost("127.0.0.1");
            server.EnableHttps(Path.Combine(Path.GetTempPath(), "missing-malda-cert-" + Guid.NewGuid().ToString("N") + ".pfx"), "");
            var ex = Assert.Throws<Exception>(() => server.CallMethod("start", new List<RuntimeValue>()));
            Assert.Contains("certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(server.IsRunning);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_HTTPS", previousHttps);
            System.Environment.SetEnvironmentVariable("MALDA_HTTP_CERT", previousCert);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void WriteSelfSignedPfx(string path, string password)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=localhost",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(
            new System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        req.CertificateExtensions.Add(
            new System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension(
                new System.Security.Cryptography.OidCollection
                {
                    new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1") // serverAuth
                },
                false));
        var san = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(path, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, password));
    }
    
    [Fact]
    public void HttpServer_Creation_WithWebDirectory()
    {
        var interpreter = new Interpreter.Interpreter();
        var webDir = Path.Combine(Path.GetTempPath(), "test_web_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(webDir);
        
        try
        {
            var server = new HttpServerInstance(8081, webDir, interpreter);
            var serverWebDir = server.Get("webDirectory", null).AsString();
            // Compare normalized paths (case-insensitive on Windows)
            Assert.Equal(Path.GetFullPath(webDir).ToLowerInvariant(), Path.GetFullPath(serverWebDir).ToLowerInvariant());
        }
        finally
        {
            if (Directory.Exists(webDir))
            {
                Directory.Delete(webDir, true);
            }
        }
    }
    
    [Fact]
    public void HttpServer_Creation_WithDefaultWebDirectory()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8082, null, interpreter);
        var webDir = server.Get("webDirectory", null).AsString();
        Assert.NotNull(webDir);
        Assert.Contains("web", webDir);
    }
    
    [Fact]
    public void HttpServer_StartStop_Works()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8083, null, interpreter);
        
        server.CallMethod("start", new List<RuntimeValue>());
        Assert.True(server.Get("isRunning", null).AsBoolean());
        
        server.CallMethod("stop", new List<RuntimeValue>());
        Assert.False(server.Get("isRunning", null).AsBoolean());
    }
    
    [Fact]
    public void HttpServer_Start_ThrowsWhenAlreadyRunning()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8084, null, interpreter);
        
        server.CallMethod("start", new List<RuntimeValue>());
        Assert.True(server.Get("isRunning", null).AsBoolean());
        
        Assert.Throws<Exception>(() => server.CallMethod("start", new List<RuntimeValue>()));
        
        server.CallMethod("stop", new List<RuntimeValue>());
    }
    
    [Fact]
    public void HttpServer_Stop_WhenNotRunning_DoesNothing()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8085, null, interpreter);
        
        // Should not throw
        server.CallMethod("stop", new List<RuntimeValue>());
        Assert.False(server.Get("isRunning", null).AsBoolean());
    }
    
    [Fact]
    public void HttpServer_SetHTML_Works()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8086, null, interpreter);
        
        var html = "<html><body><h1>Test</h1></body></html>";
        server.CallMethod("setHTML", new List<RuntimeValue> { RuntimeValue.String(html) });
        
        // If we got here without exception, setHTML works
        Assert.True(true);
    }
    
    [Fact]
    public void HttpServer_SetHTML_ThrowsWithInvalidArgument()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8087, null, interpreter);
        
        Assert.Throws<Exception>(() => 
            server.CallMethod("setHTML", new List<RuntimeValue> { RuntimeValue.Integer(123) }));
    }
    
    [Fact]
    public void HttpServer_GetRoutes_ReturnsArray()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8088, null, interpreter);
        
        var routes = server.CallMethod("getRoutes", new List<RuntimeValue>());
        Assert.Equal(ValueType.Array, routes.Type);
        var routesArray = routes.AsArray();
        // Note: Routes may not be empty if transpiled routes were registered globally
        // This test verifies that getRoutes returns a valid array
        Assert.NotNull(routesArray);
    }
    
    [Fact]
    public void HttpServer_GetRoutes_ReturnsRegisteredRoutes()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8089, null, interpreter);
        
        // Register a transpiled route
        HttpServerInstance.RegisterTranspiledRoute("GET", "/test", "testFunction", new List<string>(), null);
        
        var routes = server.CallMethod("getRoutes", new List<RuntimeValue>());
        Assert.Equal(ValueType.Array, routes.Type);
        var routesArray = routes.AsArray();
        Assert.NotEmpty(routesArray);
        
        var routeObj = routesArray[0].AsObject();
        var method = routeObj.Get("method", null);
        var path = routeObj.Get("path", null);
        
        Assert.Equal("GET", method.AsString());
        Assert.Equal("/test", path.AsString());
    }
    
    [Fact]
    public void HttpServer_ClearCache_Works()
    {
        var interpreter = new Interpreter.Interpreter();
        var webDir = Path.Combine(Path.GetTempPath(), "test_web_cache_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(webDir);
        
        try
        {
            // Create a test file
            var testFile = Path.Combine(webDir, "test.html");
            File.WriteAllText(testFile, "<html><body>Test</body></html>");
            
            var server = new HttpServerInstance(8090, webDir, interpreter);
            
            // ClearCache should not throw
            server.CallMethod("clearCache", new List<RuntimeValue>());
            Assert.True(true);
        }
        finally
        {
            if (Directory.Exists(webDir))
            {
                Directory.Delete(webDir, true);
            }
        }
    }
    
    [Fact]
    public void HttpServer_BroadcastSSE_Works()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8091, null, interpreter);
        
        var data = "{\"message\":\"test\"}";
        // Should not throw (even if no SSE connections exist)
        server.CallMethod("broadcastSSE", new List<RuntimeValue> { RuntimeValue.String(data) });
        Assert.True(true);
    }
    
    [Fact]
    public void HttpServer_BroadcastSSE_ThrowsWithInvalidArgument()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8092, null, interpreter);
        
        Assert.Throws<Exception>(() => 
            server.CallMethod("broadcastSSE", new List<RuntimeValue> { RuntimeValue.Integer(123) }));
    }
    
    [Fact]
    public void HttpServer_RegisterTranspiledRoute_Works()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8093, null, interpreter);
        
        var paramNames = new List<string> { "id" };
        HttpServerInstance.RegisterTranspiledRoute("GET", "/users/{id}", "getUser", paramNames, null);
        
        var routes = server.CallMethod("getRoutes", new List<RuntimeValue>());
        var routesArray = routes.AsArray();
        Assert.NotEmpty(routesArray);
        
        var found = false;
        foreach (var route in routesArray)
        {
            var routeObj = route.AsObject();
            if (routeObj.Get("path", null).AsString() == "/users/{id}")
            {
                found = true;
                Assert.Equal("GET", routeObj.Get("method", null).AsString());
                break;
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void HttpServer_RegisterTranspiledRoute_WithGroupAndVersion_ComposesPath()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8102, null, interpreter);

        HttpServerInstance.RegisterTranspiledRoute(
            "GET",
            "/users/{id}",
            "getUserV2",
            new List<string> { "id" },
            null,
            "/api",
            "v2",
            new List<string> { "authMiddleware" },
            "{\"path\":{\"id\":\"int|required\"}}");

        var routes = server.CallMethod("getRoutes", new List<RuntimeValue>()).AsArray();
        Assert.Contains(routes, route =>
        {
            var routeObj = route.AsObject();
            return routeObj.Get("method", null).AsString() == "GET" &&
                   routeObj.Get("path", null).AsString() == "/api/v2/users/{id}";
        });
    }
    
    [Fact]
    public void HttpServer_RegisterTranspiledAIPage_Works()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8094, null, interpreter);
        
        var paramNames = new List<string>();
        HttpServerInstance.RegisterTranspiledAIPage("/ai-page", "generatePage", paramNames, "A test page");
        
        var routes = server.CallMethod("getRoutes", new List<RuntimeValue>());
        var routesArray = routes.AsArray();
        Assert.NotEmpty(routesArray);
        
        var found = false;
        foreach (var route in routesArray)
        {
            var routeObj = route.AsObject();
            if (routeObj.Get("path", null).AsString() == "/ai-page")
            {
                found = true;
                Assert.Equal("GET", routeObj.Get("method", null).AsString());
                break;
            }
        }
        Assert.True(found);
    }
    
    [Fact]
    public void HttpServer_Get_UndefinedProperty_Throws()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8095, null, interpreter);
        
        Assert.Throws<Exception>(() => server.Get("nonexistent", null));
    }
    
    [Fact]
    public void HttpServer_CallMethod_UnknownMethod_Throws()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8096, null, interpreter);
        
        Assert.Throws<Exception>(() => 
            server.CallMethod("unknownMethod", new List<RuntimeValue>()));
    }
    
    [Fact]
    public void HttpServer_Start_WithRouteConflicts_Throws()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8097, null, interpreter);
        
        // Register duplicate routes
        HttpServerInstance.RegisterTranspiledRoute("GET", "/duplicate", "func1", new List<string>(), null);
        HttpServerInstance.RegisterTranspiledRoute("GET", "/duplicate", "func2", new List<string>(), null);
        
        Assert.Throws<Exception>(() => server.CallMethod("start", new List<RuntimeValue>()));
    }
    
    [Fact]
    public void HttpServer_Properties_Accessible()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8098, null, interpreter);
        
        var port = server.Get("port", null);
        Assert.Equal(ValueType.Integer, port.Type);
        Assert.Equal(8098, port.AsInteger());
        
        var isRunning = server.Get("isRunning", null);
        Assert.Equal(ValueType.Boolean, isRunning.Type);
        Assert.False(isRunning.AsBoolean());
        
        var webDirectory = server.Get("webDirectory", null);
        Assert.Equal(ValueType.String, webDirectory.Type);
        Assert.NotNull(webDirectory.AsString());
    }
    
    [Fact]
    public void HttpServer_Methods_Accessible()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8099, null, interpreter);
        
        var start = server.Get("start", null);
        Assert.Equal(ValueType.Function, start.Type);
        
        var stop = server.Get("stop", null);
        Assert.Equal(ValueType.Function, stop.Type);
        
        var clearCache = server.Get("clearCache", null);
        Assert.Equal(ValueType.Function, clearCache.Type);
        
        var getRoutes = server.Get("getRoutes", null);
        Assert.Equal(ValueType.Function, getRoutes.Type);
        
        var setHTML = server.Get("setHTML", null);
        Assert.Equal(ValueType.Function, setHTML.Type);
        
        var broadcastSSE = server.Get("broadcastSSE", null);
        Assert.Equal(ValueType.Function, broadcastSSE.Type);

        var use = server.Get("use", null);
        Assert.Equal(ValueType.Function, use.Type);
    }
    
    [Fact]
    public void HttpServer_Start_WithoutInterpreter_Works()
    {
        // HttpServer can be created without interpreter (for transpiled code)
        var server = new HttpServerInstance(8100, null, null);
        
        // Should not throw when starting without interpreter
        server.CallMethod("start", new List<RuntimeValue>());
        Assert.True(server.Get("isRunning", null).AsBoolean());
        
        server.CallMethod("stop", new List<RuntimeValue>());
    }

    [Fact]
    public void HttpServer_UseMiddleware_AcceptsFunctionAndString()
    {
        var interpreter = new Interpreter.Interpreter();
        var server = new HttpServerInstance(8101, null, interpreter);

        server.CallMethod("use", new List<RuntimeValue> { RuntimeValue.Function(new FunctionValue()) });
        server.CallMethod("use", new List<RuntimeValue> { RuntimeValue.String("globalMiddleware") });

        Assert.True(true);
    }

    [Fact]
    public void HttpServer_ComponentStateSnapshot_NormalizesRecoverableBoundedHistory()
    {
        HttpServerInstance.ClearAllComponentState();

        var history = new JsonObject();
        history.Set("items", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("a"),
            RuntimeValue.String("b")
        }));
        history.Set("count", RuntimeValue.Integer(5));
        history.Set("head", RuntimeValue.Integer(7));
        history.Set("maxItems", RuntimeValue.Integer(4));

        HttpServerInstance.SetComponentState("board", "history", RuntimeValue.Object(history));

        history.Set("count", RuntimeValue.Integer(99));
        history.Set("head", RuntimeValue.Integer(99));

        var restored = Assert.IsType<JsonObject>(HttpServerInstance.GetComponentState("board", "history").AsObject());
        Assert.Equal(2, restored.Get("count", null).AsInteger());
        Assert.Equal(1, restored.Get("head", null).AsInteger());
        Assert.Equal(2, restored.Get("maxItems", null).AsInteger());
        Assert.Equal(2, restored.Get("items", null).AsArray().Count);

        var snapshot = Assert.IsType<JsonObject>(HttpServerInstance.GetComponentStateObject("board").AsObject());
        var snapshotHistory = Assert.IsType<JsonObject>(snapshot.Get("history", null).AsObject());
        Assert.Equal(2, snapshotHistory.Get("count", null).AsInteger());
        Assert.Equal(1, snapshotHistory.Get("head", null).AsInteger());
    }

    [Fact]
    public async Task HttpServer_ApiRouteError_ReturnsJsonWithCorrelationId()
    {
        var port = GetAvailablePort();
        var path = $"/api/fail/{Guid.NewGuid():N}";
        var server = new HttpServerInstance(port, null, null);

        HttpServerInstance.RegisterTranspiledRoute("GET", path, "missingFunctionForApiError", new List<string>(), null);

        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Correlation-ID", "http-api-corr");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType!.ToString());
            Assert.Equal("InternalServerError", root.GetProperty("error").GetString());
            Assert.Equal("http-api-corr", root.GetProperty("correlationId").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_RateLimitExceeded_ReturnsStandardized429WithCorrelationId()
    {
        var port = GetAvailablePort();
        var path = $"/api/http-rate/{Guid.NewGuid():N}";
        var server = new HttpServerInstance(port, null, null);

        HttpServerInstance.RegisterTranspiledRoute("GET", path, "ProtectedRouteHandler", new List<string>(), null);
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

            var secondRequest = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            secondRequest.Headers.Add("Accept", "application/json");
            secondRequest.Headers.Add("X-Correlation-ID", "http-rate-corr");
            using var second = await client.SendAsync(secondRequest);
            var body = await second.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal((HttpStatusCode)429, second.StatusCode);
            Assert.Equal(429, root.GetProperty("status").GetInt32());
            Assert.Equal("RateLimitExceeded", root.GetProperty("error").GetString());
            Assert.Equal("http-rate-corr", root.GetProperty("correlationId").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_CsrfFailure_OnHtmlRoute_StillUsesStandardizedJsonError()
    {
        var port = GetAvailablePort();
        var path = $"/form-submit/{Guid.NewGuid():N}";
        var server = new HttpServerInstance(port, null, null);

        HttpServerInstance.RegisterTranspiledRoute("POST", path, "ProtectedMutationHandler", new List<string> { "body" }, null);
        server.CallMethod("enableCsrf", new List<RuntimeValue> { RuntimeValue.String("http-csrf-secret") });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}{path}");
            request.Headers.Add("X-Correlation-ID", "http-csrf-corr");
            request.Content = new StringContent("{\"x\":1}", System.Text.Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType!.ToString());
            SecurityTestUtils.AssertStandardErrorPayload(doc.RootElement, "CsrfValidationFailed", "http-csrf-corr", 403);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_ApiRoute_ReturnedStandardErrorEnvelope_RemainsJson()
    {
        var port = GetAvailablePort();
        var path = $"/api/http-error/{Guid.NewGuid():N}";
        var server = new HttpServerInstance(port, null, null);

        HttpServerInstance.RegisterTranspiledRoute("GET", path, "ReturnStandardErrorPayload", new List<string>(), null);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}{path}");
            request.Headers.Add("Accept", "application/json");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType!.ToString());
            Assert.Equal(422, root.GetProperty("status").GetInt32());
            Assert.Equal("BusinessRuleViolation", root.GetProperty("error").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_PageRoute_WebRuntimeError_PreservesStatusWithHtmlFallback()
    {
        var port = GetAvailablePort();
        var path = $"/pages/error/{Guid.NewGuid():N}";
        var server = new HttpServerInstance(port, null, null);

        HttpServerInstance.RegisterTranspiledRoute("GET", path, "ThrowBadRequestWebError", new List<string>(), null);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync($"http://localhost:{port}{path}");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
            Assert.Contains("Page validation failed.", body);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public void HttpServer_ComponentActionAndLive_Decorators_RegisterRoutes()
    {
        var source = @"
            component TicketBoard() {
                return ""<h1>board</h1>"";
            }

            @ACTION(""/tickets/update"")
            function updateTicket(body) {
                return componentFragment(""ticket-list"", ""<ul><li>updated</li></ul>"");
            }

            @LIVE(""/tickets/live"")
            function ticketLive() {
                return {""sse"": true};
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(8201, null, interpreter);

        server.CallMethod("start", new List<RuntimeValue>());
        try
        {
            var routes = server.CallMethod("getRoutes", new List<RuntimeValue>()).AsArray();

            Assert.Contains(routes, route =>
            {
                var routeObj = route.AsObject();
                return routeObj.Get("method", null).AsString() == "GET" &&
                       routeObj.Get("path", null).AsString() == "/components/TicketBoard";
            });

            Assert.Contains(routes, route =>
            {
                var routeObj = route.AsObject();
                return routeObj.Get("method", null).AsString() == "POST" &&
                       routeObj.Get("path", null).AsString() == "/tickets/update";
            });

            Assert.Contains(routes, route =>
            {
                var routeObj = route.AsObject();
                return routeObj.Get("method", null).AsString() == "GET" &&
                       routeObj.Get("path", null).AsString() == "/tickets/live";
            });
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_ActionMultipartForm_ParsesAndReturnsFragmentHeaders()
    {
        var port = GetAvailablePort();
        var source = @"
            @ACTION(""/tickets/add"")
            function addTicket(body) {
                var title = body.title == null ? ""none"" : body.title;
                return componentFragment(""ticket-list"", ""<ul><li>"" + title + ""</li></ul>"");
            }
        ";
        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("phase-b-ticket", Encoding.UTF8), "title");
            using var response = await client.PostAsync($"http://localhost:{port}/tickets/add", content);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", response.Headers.GetValues("X-Malda-Fragment").FirstOrDefault());
            Assert.Equal("ticket-list", response.Headers.GetValues("X-Malda-Fragment-Target").FirstOrDefault());
            Assert.Contains("phase-b-ticket", html);
            // Fragment bodies must not receive the full-page AJAX helper payload.
            Assert.DoesNotContain("spl-ajax-helper", html);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_FullPageHtml_InjectsAjaxHelperForFragmentForms()
    {
        var port = GetAvailablePort();
        var source = @"
            @PAGE(""/"")
            function home() {
                return ""<!DOCTYPE html><html><body><form method='post' action='/ask'><button type='submit'>Go</button></form></body></html>"";
            }

            @ACTION(""/ask"")
            function ask(body) {
                return componentFragment(""ask-panel"", ""<p>ok</p>"");
            }
        ";
        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync($"http://localhost:{port}/");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("id=\"spl-ajax-helper\"", html);
            Assert.Contains("X-Malda-Fragment", html);
            // Submit buttons with name/value (e.g. vote=up) must be posted via AJAX.
            Assert.Contains("e.submitter", html, StringComparison.Ordinal);
            Assert.Contains("new FormData(form, submitter)", html, StringComparison.Ordinal);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_SseBroadcast_FilteredByChannel()
    {
        var port = GetAvailablePort();
        var source = @"
            @LIVE(""/events"")
            function eventsLive() {
                return {""sse"": true};
            }
        ";
        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();

            var reqA = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/events?channel=alpha");
            reqA.Headers.Add("Accept", "text/event-stream");
            var reqB = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/events?channel=beta");
            reqB.Headers.Add("Accept", "text/event-stream");

            using var respA = await client.SendAsync(reqA, HttpCompletionOption.ResponseHeadersRead);
            using var respB = await client.SendAsync(reqB, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, respA.StatusCode);
            Assert.Equal(HttpStatusCode.OK, respB.StatusCode);

            await using var streamA = await respA.Content.ReadAsStreamAsync();
            await using var streamB = await respB.Content.ReadAsStreamAsync();
            using var readerA = new StreamReader(streamA);
            using var readerB = new StreamReader(streamB);

            // Consume initial "connected" event lines.
            _ = await ReadLineWithTimeoutAsync(readerA, 1500);
            _ = await ReadLineWithTimeoutAsync(readerA, 1500);
            _ = await ReadLineWithTimeoutAsync(readerB, 1500);
            _ = await ReadLineWithTimeoutAsync(readerB, 1500);

            HttpServerInstance.BroadcastSSEMessage("{\"type\":\"phaseb\",\"channel\":\"alpha\"}", "alpha");

            string? lineA = null;
            for (int i = 0; i < 4; i++)
            {
                var line = await ReadLineWithTimeoutAsync(readerA, 1500);
                if (!string.IsNullOrEmpty(line) && line.Contains("phaseb"))
                {
                    lineA = line;
                    break;
                }
            }

            string? lineB = null;
            for (int i = 0; i < 2; i++)
            {
                var line = await ReadLineWithTimeoutAsync(readerB, 700);
                if (!string.IsNullOrEmpty(line) && line.Contains("phaseb"))
                {
                    lineB = line;
                    break;
                }
            }

            Assert.NotNull(lineA);
            Assert.Null(lineB);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_RequestBagBindingsAndMiddlewareLocals_Work()
    {
        var port = GetAvailablePort();
        var source = @"
            function attachViewModel(req, res, next) {
                req.pageTitle = ""Products"";
                next();
            }

            @GET(""/pages/{slug}"")
            function page(req, res, params, query, headers, cookies) {
                return res.json({
                    ""slug"": params.slug,
                    ""slugFromHelper"": req.param(""slug""),
                    ""filter"": query.filter,
                    ""trace"": req.header(""X-Trace"", ""missing""),
                    ""theme"": req.cookie(""theme"", ""default""),
                    ""pageTitle"": req.pageTitle,
                    ""headerBag"": headers[""X-Trace""],
                    ""cookieBag"": cookies.theme
                });
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("use", new List<RuntimeValue> { RuntimeValue.String("attachViewModel") });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/pages/catalog?filter=active");
            request.Headers.Add("X-Trace", "trace-http");
            request.Headers.Add("Cookie", "theme=dark");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("catalog", root.GetProperty("slug").GetString());
            Assert.Equal("catalog", root.GetProperty("slugFromHelper").GetString());
            Assert.Equal("active", root.GetProperty("filter").GetString());
            Assert.Equal("trace-http", root.GetProperty("trace").GetString());
            Assert.Equal("trace-http", root.GetProperty("headerBag").GetString());
            Assert.Equal("dark", root.GetProperty("theme").GetString());
            Assert.Equal("dark", root.GetProperty("cookieBag").GetString());
            Assert.Equal("Products", root.GetProperty("pageTitle").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_PostRedirectBuiltIn_Uses303AndFollowUpIsGet()
    {
        var port = GetAvailablePort();
        var source = @"
            @POST(""/submit"")
            function submit(body) {
                return redirect(""/done"");
            }

            @GET(""/done"")
            function done(req, res) {
                return res.json({ ""method"": req.method, ""ok"": true });
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var inspectClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            using var inspectResponse = await inspectClient.PostAsync(
                $"http://localhost:{port}/submit",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.SeeOther, inspectResponse.StatusCode);
            Assert.Equal("/done", inspectResponse.Headers.Location?.OriginalString);

            using var followClient = new HttpClient();
            using var finalResponse = await followClient.PostAsync(
                $"http://localhost:{port}/submit",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            var body = await finalResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
            Assert.Equal(HttpMethod.Get, finalResponse.RequestMessage!.Method);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("GET", doc.RootElement.GetProperty("method").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_ResponseContextRedirect_PreservesExplicit303Status()
    {
        var port = GetAvailablePort();
        var source = @"
            @POST(""/save"")
            function save(req, res, body) {
                return res.redirect(""/saved"", 303);
            }

            @GET(""/saved"")
            function saved(req, res) {
                return res.json({ ""method"": req.method, ""path"": req.path });
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var inspectClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            using var inspectResponse = await inspectClient.PostAsync(
                $"http://localhost:{port}/save",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.SeeOther, inspectResponse.StatusCode);
            Assert.Equal("/saved", inspectResponse.Headers.Location?.OriginalString);

            using var followClient = new HttpClient();
            using var finalResponse = await followClient.PostAsync(
                $"http://localhost:{port}/save",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            var body = await finalResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
            Assert.Equal(HttpMethod.Get, finalResponse.RequestMessage!.Method);
            Assert.Equal("GET", doc.RootElement.GetProperty("method").GetString());
            Assert.Equal("/saved", doc.RootElement.GetProperty("path").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_UseMiddleware_CatchAuthFailure_RedirectsToLogin()
    {
        // Regression: WebRuntimeException used to subclass Exception (not RuntimeException),
        // so Malda try/catch never ran and ASK showed a 401 instead of redirecting to /login.
        var port = GetAvailablePort();
        var source = @"
            function requireAuth(req, res, next) {
                try {
                    req.auth.authenticateCookieJwt(""session"", ""cookie-jwt-secret"", ""cookie-sign-secret"");
                    next();
                } catch (authErr) {
                    return res.redirect(""/login"");
                }
            }

            @GET(""/"")
            function home(req, res) {
                return res.html(""<html><body>home</body></html>"");
            }

            @GET(""/login"")
            function login(req, res) {
                return res.html(""<html><body>login</body></html>"");
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        var except = RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("/login") });
        var options = new JsonObject();
        options.Set("except", except);
        server.CallMethod(
            "use",
            new List<RuntimeValue>
            {
                RuntimeValue.String("requireAuth"),
                RuntimeValue.Object(options)
            });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            using var denied = await client.GetAsync($"http://localhost:{port}/");
            Assert.Equal(HttpStatusCode.SeeOther, denied.StatusCode);
            Assert.Equal("/login", denied.Headers.Location?.OriginalString);

            using var login = await client.GetAsync($"http://localhost:{port}/login");
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var loginBody = await login.Content.ReadAsStringAsync();
            Assert.Contains("login", loginBody, StringComparison.Ordinal);
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_FormUrlEncoded_DuplicateKeys_BecomeArray()
    {
        // Checkbox groups (name="tags") post tags=a&tags=b; last-wins used to drop all but one.
        var port = GetAvailablePort();
        var source = @"
            @POST(""/tags"")
            function submit(body, res) {
                var tags = body.tags;
                if (typeOf(tags) == ""array"") {
                    return res.json({
                        ""kind"": ""array"",
                        ""len"": tags.length,
                        ""a"": tags[0],
                        ""b"": tags[1]
                    });
                }
                return res.json({ ""kind"": typeOf(tags), ""value"": tags });
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();
            using var multi = await client.PostAsync(
                $"http://localhost:{port}/tags",
                new StringContent("tags=alpha&tags=beta&question=hi", Encoding.UTF8, "application/x-www-form-urlencoded"));
            var multiBody = await multi.Content.ReadAsStringAsync();
            using var multiDoc = JsonDocument.Parse(multiBody);
            Assert.Equal(HttpStatusCode.OK, multi.StatusCode);
            Assert.Equal("array", multiDoc.RootElement.GetProperty("kind").GetString());
            Assert.Equal(2, multiDoc.RootElement.GetProperty("len").GetInt32());
            Assert.Equal("alpha", multiDoc.RootElement.GetProperty("a").GetString());
            Assert.Equal("beta", multiDoc.RootElement.GetProperty("b").GetString());

            using var single = await client.PostAsync(
                $"http://localhost:{port}/tags",
                new StringContent("tags=only&question=hi", Encoding.UTF8, "application/x-www-form-urlencoded"));
            var singleBody = await single.Content.ReadAsStringAsync();
            using var singleDoc = JsonDocument.Parse(singleBody);
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            Assert.Equal("string", singleDoc.RootElement.GetProperty("kind").GetString());
            Assert.Equal("only", singleDoc.RootElement.GetProperty("value").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    private static string CreateHttpAuthJwt(string secret, string sub, string role = "editor", int expiresInSeconds = 120)
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
    public async Task HttpServer_RequestAuthAuthenticateBearerJwt_ExposesVerifiedClaims()
    {
        var port = GetAvailablePort();
        const string secret = "http-auth-bearer-secret";
        var source = @"
            function requireAuth(req, res, next) {
                req.auth.authenticateBearerJwt(""http-auth-bearer-secret"");
                next();
            }

            @GET(""/api/secure"")
            @Use(""requireAuth"")
            function secure(req, res) {
                return res.json({
                    ""verified"": req.auth.verified,
                    ""sub"": req.auth.sub,
                    ""role"": req.auth.claims.role,
                    ""hasToken"": req.auth.token != null
                });
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            var token = CreateHttpAuthJwt(secret, "http-user-11", "editor");
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/secure");
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("Accept", "application/json");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(root.GetProperty("verified").GetBoolean());
            Assert.Equal("http-user-11", root.GetProperty("sub").GetString());
            Assert.Equal("editor", root.GetProperty("role").GetString());
            Assert.True(root.GetProperty("hasToken").GetBoolean());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_RequestAuthAuthenticateCookieJwt_ExposesVerifiedClaims()
    {
        var port = GetAvailablePort();
        const string jwtSecret = "http-auth-cookie-jwt-secret";
        const string cookieSecret = "http-auth-cookie-signing-secret";
        var source = @"
            function requireCookieAuth(req, res, next) {
                req.auth.authenticateCookieJwt(""session"", ""http-auth-cookie-jwt-secret"", ""http-auth-cookie-signing-secret"");
                next();
            }

            @GET(""/api/me"")
            @Use(""requireCookieAuth"")
            function me(req, res) {
                return res.json({
                    ""verified"": req.auth.verified,
                    ""sub"": req.auth.sub,
                    ""role"": req.auth.claim(""role"", ""unknown"")
                });
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            var token = CreateHttpAuthJwt(jwtSecret, "cookie-user-33", "reviewer");
            var cookieHeader = BuiltInFunctions.CallBuiltIn(
                "createSecureCookie",
                new List<RuntimeValue>
                {
                    RuntimeValue.String("session"),
                    RuntimeValue.String(token),
                    RuntimeValue.String(cookieSecret)
                },
                null!).AsString();
            var cookiePair = cookieHeader.Split(';')[0];

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/me");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Cookie", cookiePair);

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(root.GetProperty("verified").GetBoolean());
            Assert.Equal("cookie-user-33", root.GetProperty("sub").GetString());
            Assert.Equal("reviewer", root.GetProperty("role").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }

    [Fact]
    public async Task HttpServer_UseMiddleware_ExceptPaths_SkipsAuthOnPublicRoutes()
    {
        var port = GetAvailablePort();
        var source = @"
            function requireAuth(req, res, next) {
                req.auth.authenticateBearerJwt(""http-except-secret"");
                next();
            }

            @GET(""/api/health"")
            function health(req, res) {
                return res.json({ ""status"": ""ok"" });
            }

            @GET(""/api/private"")
            function privateRoute(req, res) {
                return res.json({ ""sub"": req.auth.sub });
            }
        ";

        var interpreter = LoadInterpreterFromSource(source);
        var server = new HttpServerInstance(port, null, interpreter);
        var except = RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("/api/health") });
        var options = new JsonObject();
        options.Set("except", except);
        server.CallMethod(
            "use",
            new List<RuntimeValue>
            {
                RuntimeValue.String("requireAuth"),
                RuntimeValue.Object(options)
            });
        server.CallMethod("start", new List<RuntimeValue>());

        try
        {
            using var client = new HttpClient();

            using var health = await client.GetAsync($"http://localhost:{port}/api/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            var privateUnauthed = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/private");
            privateUnauthed.Headers.Add("Accept", "application/json");
            using var denied = await client.SendAsync(privateUnauthed);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            var token = CreateHttpAuthJwt("http-except-secret", "except-user");
            var privateAuthed = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/api/private");
            privateAuthed.Headers.Add("Authorization", $"Bearer {token}");
            privateAuthed.Headers.Add("Accept", "application/json");
            using var ok = await client.SendAsync(privateAuthed);
            var body = await ok.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("except-user", doc.RootElement.GetProperty("sub").GetString());
        }
        finally
        {
            server.CallMethod("stop", new List<RuntimeValue>());
        }
    }
}
