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
    private async Task HandleRequestsAsync()
    {
        while (_isRunning && _listener != null)
        {
            try
            {
                // Use async version to avoid blocking
                var context = await _listener.GetContextAsync();
                // Fire and forget - each request processes concurrently
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessRequestAsync(context);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("HttpServer unhandled request error");
                        Console.Error.WriteLine(RuntimeDiagnostics.FormatExceptionForConsole(ex, _interpreter));
                    }
                });
            }
            catch (HttpListenerException)
            {
                // Listener was stopped
                break;
            }
            catch (ObjectDisposedException)
            {
                // Listener was disposed
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("HttpServer accept loop error");
                Console.Error.WriteLine(RuntimeDiagnostics.FormatExceptionForConsole(ex, _interpreter));
            }
        }
    }
    
    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var correlationId = WebRuntimeHelpers.ResolveCorrelationId(request);
        WebRuntimeHelpers.ApplyCorrelationId(response, correlationId);
        
        // Check for Server-Sent Events (SSE) request (declare outside try for finally block access)
        var acceptHeader = request.Headers["Accept"] ?? "";
        var isSSERequest = acceptHeader.Contains("text/event-stream");
        ResponseContextInstance? responseContext = null;
        
        try
        {
            var method = request.HttpMethod;
            var rawPath = request.Url?.AbsolutePath ?? "/";
            var queryString = request.Url?.Query ?? "";
            
            // Strip path base when behind reverse proxy (e.g. /schoolprep/progress -> /progress)
            var path = rawPath;
            if (_pathBase.Length > 0)
            {
                if (rawPath.StartsWith(_pathBase, StringComparison.OrdinalIgnoreCase))
                {
                    path = rawPath.Length == _pathBase.Length ? "/" : rawPath[_pathBase.Length..];
                    if (string.IsNullOrEmpty(path)) path = "/";
                }
            }
            
            // Handle setHTML content for root path (backward compatibility)
            if (method == "GET" && path == "/" && _setHtmlContent != null && !isSSERequest)
            {
                WriteHtmlResponse(response, _setHtmlContent);
                response.Close();
                return;
            }
            
            // Extract query parameters
            var queryParams = _routeRegistry.ExtractQueryParams(queryString);

            // Mounted RestServer owns matching API routes on this listener.
            if (_mountedRest != null)
            {
                var mountedHandled = await _mountedRest.TryProcessMountedRequestAsync(
                    context,
                    path,
                    queryParams,
                    correlationId);
                if (mountedHandled)
                {
                    return;
                }
            }
            
            // Parse request body if present
            RuntimeValue? requestBody = null;
            if (method == "POST" || method == "PUT" || method == "PATCH")
            {
                requestBody = await ParseRequestBodyAsync(request);
            }

            var requestContext = CreateRequestContext(request, path, new Dictionary<string, string>(), queryParams, requestBody, correlationId);
            responseContext = new ResponseContextInstance();
            responseContext.BindListener(response, request, requestContext, _pathBase, IsRequestSecure(request));

            if (!ValidateCsrf(requestContext, responseContext, requestBody, request, response, correlationId))
            {
                return;
            }

            var continuePipeline = await ExecuteMiddlewareChainAsync(requestContext, responseContext);
            if (!continuePipeline)
            {
                if (!responseContext.IsFlushed && (responseContext.IsCommitted || responseContext.HasStatusOverride))
                {
                    CommitAndApplyResponse(requestContext, responseContext, response, request);
                }
                return;
            }

            // Check dynamic routes first (they take precedence)
            if (_routeRegistry.MatchRoute(method, path, out var route, out var pathParams))
            {
                requestContext.SetPathParams(pathParams);
                await HandleDynamicRouteAsync(route!, pathParams, queryParams, requestBody, response, request, isSSERequest, requestContext, responseContext, correlationId);
                return;
            }

            // Non-route handlers (static files / fallbacks) rate-limit after global auth middleware.
            if (!ValidateRateLimit(requestContext, response, request, correlationId))
            {
                return;
            }
            
            // Only check static files for GET requests
            if (method == "GET")
            {
                if (TryServeStaticFile(path, response, request))
                {
                    return;
                }
            }
            
            // 404 Not Found
            if (WantsJsonResponse(request, path))
            {
                var payload = WebRuntimeHelpers.CreateErrorRuntimeValue(
                    404,
                    "NotFound",
                    "Not Found",
                    correlationId);
                WriteErrorResult(response, payload, 404, request, path);
            }
            else
            {
                response.StatusCode = 404;
                WriteErrorResponse(response, 404, "Not Found", request);
            }
        }
        catch (Exception ex)
        {
            if (responseContext?.IsFlushed == true)
            {
                Console.Error.WriteLine("HttpServer request error after response was sent");
                Console.Error.WriteLine(RuntimeDiagnostics.FormatExceptionForConsole(ex, _interpreter));
            }
            else
            {
                HandleError(response, ex, correlationId, request);
            }
        }
        finally
        {
            // Only close if not SSE (SSE connections are kept open)
            // SSE connections will be closed when client disconnects or explicitly closed
            if (!isSSERequest)
            {
                WebRuntimeHelpers.TryCloseHttpListenerResponse(response);
            }
        }
    }
    
    private async Task HandleDynamicRouteAsync(Route route, Dictionary<string, string> pathParams, 
        Dictionary<string, string> queryParams, RuntimeValue? requestBody, HttpListenerResponse response, HttpListenerRequest request, bool isSSERequest,
        RequestContextInstance requestContext, ResponseContextInstance responseContext, string correlationId)
    {
        try
        {
            // Check if this is an AIPAGE route that needs AI generation
            bool isAiPage = false;
            string? aiDescription = null;
            lock (_aiCacheLock)
            {
                if (_aiPageDescriptions.TryGetValue(route.PathPattern, out var desc))
                {
                    isAiPage = true;
                    aiDescription = desc;
                }
            }
            
            // If AIPAGE and not cached, generate HTML
            if (isAiPage && aiDescription != null)
            {
                lock (_aiCacheLock)
                {
                    if (!_aiGeneratedCache.TryGetValue(route.PathPattern, out _))
                    {
                        // Generate HTML using AI
                        var generatedHtml = GenerateAIPage(aiDescription);
                        if (generatedHtml != null)
                        {
                            _aiGeneratedCache[route.PathPattern] = generatedHtml;
                        }
                    }
                }
            }
            
            // Bind parameters
            var continueRoutePipeline = await ExecuteRouteMiddlewareChainAsync(route, requestContext, responseContext);
            if (!continueRoutePipeline)
            {
                if (responseContext.IsCommitted || responseContext.HasStatusOverride)
                {
                    CommitAndApplyResponse(requestContext, responseContext, response, request);
                }
                return;
            }

            // Rate-limit after auth middleware so verifiedSub* keys see JWT subjects.
            if (!ValidateRateLimit(requestContext, response, request, correlationId))
            {
                return;
            }

            if (!ValidateRouteInput(route, pathParams, queryParams, requestBody, correlationId, response, request, request.Url?.AbsolutePath ?? route.PathPattern))
            {
                return;
            }

            var functionArgs = BindParameters(route, pathParams, queryParams, requestBody, requestContext, responseContext);
            
            RuntimeValue result;
            
            if (_interpreter == null)
            {
                // Transpiled code: call static method directly via reflection
                result = await CallTranspiledRouteFunctionAsync(route, functionArgs);
            }
            else
            {
                // Interpreted code: use interpreter
                // Create isolated interpreter for this request
                var requestInterpreter = _interpreter.CreateExecutionInterpreter();
                
                var requestFunction = ResolveFunction(requestInterpreter, route.FunctionName);
                
                if (requestFunction == null)
                {
                    var payload = WebRuntimeHelpers.CreateErrorRuntimeValue(
                        500,
                        "RouteFunctionNotFound",
                        $"Function '{route.FunctionName}' not found",
                        correlationId);
                    WriteErrorResult(response, payload, 500, request, request.Url?.AbsolutePath ?? route.PathPattern);
                    return;
                }
                
                // Call the function using the isolated interpreter
                result = await CallRouteFunctionAsync(requestFunction, functionArgs, requestInterpreter);
            }
            
            // For AIPAGE routes, if function returns empty/null, use cached generated HTML
            if (isAiPage && aiDescription != null)
            {
                // Check if result is empty or null
                bool useCached = false;
                if (result.Type == ValueType.Null)
                {
                    useCached = true;
                }
                else if (result.Type == ValueType.String && string.IsNullOrWhiteSpace(result.AsString()))
                {
                    useCached = true;
                }
                
                if (useCached)
                {
                    var cacheKey = route.PathPattern; // Use pattern as cache key
                    lock (_aiCacheLock)
                    {
                        if (_aiGeneratedCache.TryGetValue(cacheKey, out var cachedHtml))
                        {
                            result = RuntimeValue.String(cachedHtml);
                        }
                    }
                }
            }

            if (responseContext.IsFlushed)
            {
                return;
            }

            if (responseContext.IsCommitted || responseContext.HasStatusOverride)
            {
                CommitAndApplyResponse(requestContext, responseContext, response, request);
                return;
            }
            
            // Check if this is an SSE request and result indicates SSE mode
            if (isSSERequest && result.Type == ValueType.Object)
            {
                var obj = result.AsObject();
                if (obj is JsonObject jsonObj)
                {
                    var sseValue = jsonObj.Get("sse", null);
                    if (sseValue != null && sseValue.Type == ValueType.Boolean && sseValue.AsBoolean())
                    {
                        SessionRuntime.CommitSession(requestContext, responseContext, IsRequestSecure(request));
                        if (responseContext.HasHeaders)
                        {
                            responseContext.ApplyHeadersTo(response, _pathBase);
                        }

                        var subscribedChannels = ParseSseChannels(queryParams);

                        // HTTPS front-end: authorize via loopback, then stream from Kestrel.
                        var delegateSse = string.Equals(
                            request.Headers[SseDelegateHeaderName],
                            "1",
                            StringComparison.Ordinal);
                        if (delegateSse)
                        {
                            response.StatusCode = 200;
                            response.ContentType = "application/json; charset=utf-8";
                            response.Headers.Add(SseReadyHeaderName, "1");
                            if (subscribedChannels.Count > 0)
                            {
                                response.Headers.Add(
                                    SseChannelsHeaderName,
                                    string.Join(",", subscribedChannels));
                            }
                            response.Headers.Add("Cache-Control", "no-cache");
                            var readyBytes = Encoding.UTF8.GetBytes("{\"sse\":true}");
                            response.ContentLength64 = readyBytes.Length;
                            response.OutputStream.Write(readyBytes, 0, readyBytes.Length);
                            response.Close();
                            return;
                        }

                        // Direct HttpListener SSE (plain HTTP).
                        response.StatusCode = 200;
                        response.ContentType = "text/event-stream";
                        response.Headers.Add("Cache-Control", "no-cache");
                        response.Headers.Add("Connection", "keep-alive");
                        response.SendChunked = true;

                        var connectionId = $"sse_{Interlocked.Increment(ref _sseConnectionCounter)}_{DateTime.UtcNow.Ticks}";
                        var listenerConnection = new SseConnection(response, subscribedChannels);
                        lock (_sseConnectionsLock)
                        {
                            _sseConnections[connectionId] = listenerConnection;
                        }

                        var channelsJson = subscribedChannels.Count == 0
                            ? "[]"
                            : "[" + string.Join(",", subscribedChannels.Select(c => JsonSerializer.Serialize(c))) + "]";
                        var initMessage = $"data: {{\"type\":\"connected\",\"connectionId\":\"{connectionId}\",\"channels\":{channelsJson}}}\n\n";
                        var initBytes = Encoding.UTF8.GetBytes(initMessage);
                        if (!listenerConnection.TryWrite(initBytes))
                        {
                            lock (_sseConnectionsLock)
                            {
                                _sseConnections.Remove(connectionId);
                            }
                            return;
                        }

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                while (true)
                                {
                                    await Task.Delay(30000).ConfigureAwait(false);
                                    SseConnection? connection;
                                    lock (_sseConnectionsLock)
                                    {
                                        if (!_sseConnections.TryGetValue(connectionId, out connection))
                                            break;
                                    }

                                    var heartbeat = "data: {\"type\":\"heartbeat\"}\n\n";
                                    var heartbeatBytes = Encoding.UTF8.GetBytes(heartbeat);
                                    if (!connection.TryWrite(heartbeatBytes))
                                    {
                                        lock (_sseConnectionsLock)
                                        {
                                            _sseConnections.Remove(connectionId);
                                        }
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                // Client disconnected
                            }
                            finally
                            {
                                lock (_sseConnectionsLock)
                                {
                                    _sseConnections.Remove(connectionId);
                                }
                                try
                                {
                                    response.Close();
                                }
                                catch
                                {
                                    // Ignore errors on close
                                }
                            }
                        });

                        return; // Don't close response here
                    }
                }
            }
            
            // Serialize and send HTML response
            SerializeHtmlResponse(response, result, request, request.Url?.AbsolutePath ?? route.PathPattern, requestContext, responseContext);
        }
        catch (Exception ex)
        {
            if (responseContext.IsFlushed)
            {
                Console.Error.WriteLine("HttpServer handler error after response was sent");
                Console.Error.WriteLine(RuntimeDiagnostics.FormatExceptionForConsole(ex, _interpreter));
            }
            else
            {
                HandleError(response, ex, correlationId, request);
            }
        }
        finally
        {
            // Only close if not SSE
            if (!isSSERequest)
            {
                WebRuntimeHelpers.TryCloseHttpListenerResponse(response);
            }
        }
    }
}
