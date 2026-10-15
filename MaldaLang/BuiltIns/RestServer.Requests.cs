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
                        Console.Error.WriteLine("REST Server unhandled request error");
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
                Console.Error.WriteLine("REST Server accept loop error");
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
        
        try
        {
            // Handle CORS preflight
            if (request.HttpMethod == "OPTIONS" && _corsEnabled)
            {
                HandleCORS(response, request);
                response.StatusCode = 200;
                response.Close();
                return;
            }
            
            // Handle CORS for actual requests
            if (_corsEnabled)
            {
                HandleCORS(response, request);
            }
            
            var method = request.HttpMethod;
            var path = request.Url?.AbsolutePath ?? "/";
            var queryString = request.Url?.Query ?? "";
            var queryParams = _routeRegistry.ExtractQueryParams(queryString);
            
            // Handle Swagger/OpenAPI requests
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
                    response.Close();
                    return;
                }
            }
            
            // Match route
            if (!_routeRegistry.MatchRoute(method, path, out var route, out var pathParams))
            {
                response.StatusCode = 404;
                var notFound = WebRuntimeHelpers.CreateErrorRuntimeValue(
                    404,
                    "NotFound",
                    "Not Found",
                    correlationId);
                WriteJsonResponse(response, notFound);
                return;
            }

            // Parse request body if present
            RuntimeValue? requestBody = null;
            if (method == "POST" || method == "PUT" || method == "PATCH")
            {
                requestBody = await ParseRequestBodyAsync(request);
            }

            var requestContext = CreateRequestContext(request, pathParams, queryParams, requestBody, correlationId);
            var responseContext = new ResponseContextInstance();
            responseContext.BindListener(response, request, requestContext, pathBase: null, isSecure: request.IsSecureConnection);

            if (!ValidateCsrf(requestContext, responseContext, requestBody, request, response, correlationId))
            {
                return;
            }

            var continuePipeline = await ExecuteMiddlewareChainAsync(requestContext, responseContext);
            if (!continuePipeline)
            {
                // Middleware short-circuited the request.
                if (responseContext.IsCommitted || responseContext.HasStatusOverride)
                {
                    CommitAndApplyResponse(requestContext, responseContext, response, request);
                }
                return;
            }

            var continueRoutePipeline = await ExecuteRouteMiddlewareChainAsync(route!, requestContext, responseContext);
            if (!continueRoutePipeline)
            {
                if (responseContext.IsCommitted || responseContext.HasStatusOverride)
                {
                    CommitAndApplyResponse(requestContext, responseContext, response, request);
                }
                return;
            }

            // Rate-limit after auth middleware so verifiedSub* keys see JWT subjects.
            if (!ValidateRateLimit(requestContext, response, correlationId))
            {
                return;
            }

            if (!ValidateRouteInput(route!, pathParams, queryParams, requestBody, correlationId, response))
            {
                return;
            }
            
            // Bind parameters
            var functionArgs = BindParameters(route!, pathParams, queryParams, requestBody, request, requestContext, responseContext);
            
            RuntimeValue result;
            
            if (_interpreter == null)
            {
                // Transpiled code: call static method directly via reflection
                result = await CallTranspiledRouteFunctionAsync(route!, functionArgs);
            }
            else
            {
                // Interpreted code: use interpreter
                // Create isolated interpreter for this request
                var requestInterpreter = _interpreter.CreateExecutionInterpreter();
                
                // Look up the function by name in the new interpreter's context
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
                    return;
                }
                
                // Call the function using the isolated interpreter
                result = await CallRouteFunctionAsync(requestFunction, functionArgs, requestInterpreter);
            }
            
            // Response helpers can pre-commit output regardless of return value.
            if (responseContext.IsFlushed)
            {
                return;
            }

            if (responseContext.IsCommitted || responseContext.HasStatusOverride)
            {
                CommitAndApplyResponse(requestContext, responseContext, response, request);
                return;
            }

            // Serialize and send response
            SerializeResponse(response, result, requestContext, responseContext, request);
        }
        catch (Exception ex)
        {
            try
            {
                HandleError(response, ex, correlationId);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
        finally
        {
            WebRuntimeHelpers.TryCloseHttpListenerResponse(response);
        }
    }

    private async Task<RuntimeValue> ParseRequestBodyAsync(HttpListenerRequest request)
    {
        var contentType = request.ContentType ?? "";
        
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var bodyText = await reader.ReadToEndAsync();
        
        if (string.IsNullOrEmpty(bodyText))
            return RuntimeValue.Null();
        
        if (contentType.Contains("application/json"))
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(bodyText);
                return JsonToRuntimeValue(jsonDoc.RootElement);
            }
            catch
            {
                return RuntimeValue.String(bodyText);
            }
        }
        else if (contentType.Contains("application/x-www-form-urlencoded"))
        {
            var jsonObj = new JsonObject();
            var pairs = bodyText.Split('&');
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = Uri.UnescapeDataString(parts[0]);
                    var value = Uri.UnescapeDataString(parts[1]);
                    jsonObj.Set(key, RuntimeValue.String(value));
                }
            }
            return RuntimeValue.Object(jsonObj);
        }
        
        return RuntimeValue.String(bodyText);
    }
    
    private RuntimeValue JsonToRuntimeValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonObjectToRuntimeValue(element),
            JsonValueKind.Array => JsonArrayToRuntimeValue(element),
            JsonValueKind.String => RuntimeValue.String(element.GetString() ?? ""),
            JsonValueKind.Number => element.TryGetInt32(out var intVal) 
                ? RuntimeValue.Integer(intVal) 
                : RuntimeValue.Float(element.GetDouble()),
            JsonValueKind.True => RuntimeValue.Boolean(true),
            JsonValueKind.False => RuntimeValue.Boolean(false),
            JsonValueKind.Null => RuntimeValue.Null(),
            _ => RuntimeValue.Null()
        };
    }
    
    private RuntimeValue JsonObjectToRuntimeValue(JsonElement element)
    {
        var jsonObj = new JsonObject();
        foreach (var prop in element.EnumerateObject())
        {
            jsonObj.Set(prop.Name, JsonToRuntimeValue(prop.Value));
        }
        return RuntimeValue.Object(jsonObj);
    }
    
    private RuntimeValue JsonArrayToRuntimeValue(JsonElement element)
    {
        var list = new List<RuntimeValue>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(JsonToRuntimeValue(item));
        }
        return RuntimeValue.Array(list);
    }
}
