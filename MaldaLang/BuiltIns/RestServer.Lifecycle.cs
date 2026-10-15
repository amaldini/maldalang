// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;

public partial class RestServerInstance
{
    public bool IsMounted => _mountedHost != null;

    public void AttachToHost(HttpServerInstance host)
    {
        if (_listener != null || _isRunning)
            throw new Exception("Cannot mount a RestServer that is already running its own listener");
        if (_mountedHost != null && !ReferenceEquals(_mountedHost, host))
            throw new Exception("RestServer is already mounted on another HttpServer");

        _mountedHost = host;
        EnsureRoutesScanned();
    }

    public void NotifyHostStarted()
    {
        if (_mountedHost != null)
        {
            EnsureRoutesScanned();
            _isRunning = true;
        }
    }

    public void NotifyHostStopped()
    {
        if (_mountedHost != null)
        {
            _isRunning = false;
        }
    }

    private void EnsureRoutesScanned()
    {
        if (_routesScanned)
        {
            return;
        }

        ScanForRoutes();
        _routeRegistry.ValidateRouteConflicts();
        _routesScanned = true;
    }

    /// <summary>
    /// When mounted on HttpServer, handle the request if a Rest route (or swagger) matches.
    /// Returns false when the host should continue with HTML/static handling.
    /// </summary>
    public async Task<bool> TryProcessMountedRequestAsync(
        HttpListenerContext context,
        string path,
        Dictionary<string, string> queryParams,
        string correlationId)
    {
        if (_mountedHost == null)
        {
            return false;
        }

        EnsureRoutesScanned();
        var request = context.Request;
        var response = context.Response;
        var method = request.HttpMethod;

        if (request.HttpMethod == "OPTIONS" && _corsEnabled)
        {
            HandleCORS(response, request);
            response.StatusCode = 200;
            return true;
        }

        if (_corsEnabled)
        {
            HandleCORS(response, request);
        }

        if (_swaggerEnabled && (path == "/swagger.json" || path == "/openapi.json" || path == "/swagger/openapi.json"))
        {
            if (method == "GET")
            {
                var swaggerJson = GenerateSwaggerJson();
                response.ContentType = "application/json; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(swaggerJson);
                response.ContentLength64 = bytes.Length;
                response.StatusCode = 200;
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                return true;
            }
        }

        if (!_routeRegistry.MatchRoute(method, path, out var route, out var pathParams))
        {
            return false;
        }

        RuntimeValue? requestBody = null;
        if (method == "POST" || method == "PUT" || method == "PATCH")
        {
            requestBody = await ParseRequestBodyAsync(request);
        }

        var requestContext = CreateRequestContext(request, pathParams, queryParams, requestBody, correlationId, pathOverride: path);
        var responseContext = new ResponseContextInstance();
        responseContext.BindListener(response, request, requestContext, pathBase: null, isSecure: request.IsSecureConnection);

        if (!ValidateCsrf(requestContext, responseContext, requestBody, request, response, correlationId))
        {
            return true;
        }

        var continuePipeline = await ExecuteMiddlewareChainAsync(requestContext, responseContext);
        if (!continuePipeline)
        {
            if (responseContext.IsCommitted || responseContext.HasStatusOverride)
            {
                CommitAndApplyResponse(requestContext, responseContext, response, request);
            }
            return true;
        }

        var continueRoutePipeline = await ExecuteRouteMiddlewareChainAsync(route!, requestContext, responseContext);
        if (!continueRoutePipeline)
        {
            if (responseContext.IsCommitted || responseContext.HasStatusOverride)
            {
                CommitAndApplyResponse(requestContext, responseContext, response, request);
            }
            return true;
        }

        if (!ValidateRateLimit(requestContext, response, correlationId))
        {
            return true;
        }

        if (!ValidateRouteInput(route!, pathParams, queryParams, requestBody, correlationId, response))
        {
            return true;
        }

        var functionArgs = BindParameters(route!, pathParams, queryParams, requestBody, request, requestContext, responseContext);
        RuntimeValue result;
        if (_interpreter == null)
        {
            result = await CallTranspiledRouteFunctionAsync(route!, functionArgs);
        }
        else
        {
            var requestInterpreter = _interpreter.CreateExecutionInterpreter();
            var requestFunction = ResolveFunction(requestInterpreter, route!.FunctionName);
            if (requestFunction == null)
            {
                response.StatusCode = 500;
                var notFoundFunction = WebRuntimeHelpers.CreateErrorRuntimeValue(
                    500,
                    "RouteFunctionNotFound",
                    $"Function '{route.FunctionName}' not found",
                    correlationId);
                WriteJsonResponse(response, notFoundFunction);
                return true;
            }

            result = await CallRouteFunctionAsync(requestFunction, functionArgs, requestInterpreter);
        }

        if (responseContext.IsFlushed)
        {
            return true;
        }

        if (responseContext.IsCommitted || responseContext.HasStatusOverride)
        {
            CommitAndApplyResponse(requestContext, responseContext, response, request);
            return true;
        }

        SerializeResponse(response, result, requestContext, responseContext, request);
        return true;
    }

    private void Start()
    {
        if (_isRunning)
            throw new Exception("RestServer is already running");

        if (_mountedHost != null)
        {
            EnsureRoutesScanned();
            var mountedSummary = _routeRegistry.GetRoutesSummary();
            Console.WriteLine(mountedSummary);
            Console.WriteLine("RestServer mounted on HttpServer (shared listener).");
            _isRunning = _mountedHost.Get("isRunning").AsBoolean();
            return;
        }

        if (_port == 0)
            throw new Exception("RestServer has no port; call HttpServer.mount(api) or construct with a port");
        
        var prefix = _host == "0.0.0.0" ? $"http://*:{_port}/" : $"http://{_host}:{_port}/";
        try
        {
            // Scan for routes before starting
            EnsureRoutesScanned();
            
            // Print registered routes
            var routesSummary = _routeRegistry.GetRoutesSummary();
            Console.WriteLine(routesSummary);
            
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _isRunning = true;
            
            // Use Task.Run to start async request handling
            _ = Task.Run(async () => await HandleRequestsAsync());
        }
        catch (Exception ex)
        {
            _isRunning = false;
            throw new Exception($"Failed to start RestServer ({prefix}): {ex}", ex);
        }
    }
    
    private void Stop()
    {
        if (!_isRunning)
            return;
        
        _isRunning = false;
        _listener?.Stop();
        _listener?.Close();
        _listener = null;
    }

    /// <summary>
    /// Test-only: stop every RestServer in this process so HTTP traces do not leak ports.
    /// </summary>
    internal static void StopAllForTesting()
    {
        List<RestServerInstance> snapshot;
        lock (_instancesLock)
        {
            snapshot = _instances.ToList();
        }

        foreach (var server in snapshot)
        {
            try
            {
                server.Stop();
            }
            catch
            {
                // Best-effort — never throw from test cleanup.
            }
        }
    }
}
