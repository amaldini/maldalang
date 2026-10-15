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
    private RuntimeValue GetRoutes()
    {
        var routes = _routeRegistry.GetAllRoutes();
        var routesList = new List<RuntimeValue>();
        
        foreach (var route in routes)
        {
            var routeObj = new JsonObject();
            routeObj.Set("method", RuntimeValue.String(route.Method));
            routeObj.Set("path", RuntimeValue.String(route.PathPattern));
            routesList.Add(RuntimeValue.Object(routeObj));
        }
        
        return RuntimeValue.Array(routesList);
    }
    
    private void ScanForRoutes()
    {
        if (_interpreter == null)
        {
            // For transpiled code, routes are registered via reflection at runtime
            // This will be handled by the transpiled code's RegisterDecoratedFunctions method
            return;
        }
        
        var httpMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS" };
        
        foreach (var method in httpMethods)
        {
            var functions = _interpreter.GetDecoratedFunctions(method);
            
            foreach (var (function, functionName) in functions)
            {
                if (function.Declaration == null)
                    continue;
                
                // Get the path from the decorator arguments
                var decorator = function.Decorators?.FirstOrDefault(d => d.Name == method);
                if (decorator == null || decorator.Arguments == null || decorator.Arguments.Count == 0)
                    continue;
                
                var pathExpr = decorator.Arguments[0];
                if (pathExpr == null)
                    continue;
                
                RuntimeValue pathValue;
                try
                {
                    pathValue = EvaluateDecoratorArgument(pathExpr);
                }
                catch (Exception)
                {
                    continue;
                }
                
                if (pathValue.Type != ValueType.String)
                    continue;
                
                var path = pathValue.AsString();
                var routeMetadata = BuildRouteMetadata(function.Decorators);
                var effectivePath = WebRuntimeHelpers.ComposeRoutePath(
                    path,
                    routeMetadata.GroupPrefix,
                    routeMetadata.VersionPrefix);
                var paramNames = function.Declaration.Parameters;
                var paramDecorators = function.ParameterDecorators;
                
                _routeRegistry.RegisterRoute(method, effectivePath, function, functionName, paramNames, paramDecorators, routeMetadata);
            }
        }
    }
    
    private RuntimeValue EvaluateDecoratorArgument(Expression expr)
    {
        if (_interpreter == null)
            throw new Exception("Decorator evaluation requires interpreter.");

        // Evaluate against the interpreter so decorator metadata can include
        // literals, arrays, and object schemas.
        return _interpreter.EvaluateAsync(expr).GetAwaiter().GetResult();
    }

    private RouteMetadata BuildRouteMetadata(List<Decorator>? decorators)
    {
        if (decorators == null || decorators.Count == 0)
        {
            return new RouteMetadata();
        }

        string? groupPrefix = null;
        string? versionPrefix = null;
        var middlewareNames = new List<string>();
        RuntimeValue validationSchema = RuntimeValue.Null();

        foreach (var decorator in decorators)
        {
            if (decorator == null)
            {
                continue;
            }

            if (IsGroupDecorator(decorator.Name))
            {
                var arg = GetDecoratorArgument(decorator, 0);
                if (arg != null && arg.Type == ValueType.String)
                {
                    groupPrefix = arg.AsString();
                }
            }
            else if (IsVersionDecorator(decorator.Name))
            {
                var arg = GetDecoratorArgument(decorator, 0);
                if (arg != null && arg.Type == ValueType.String)
                {
                    versionPrefix = arg.AsString();
                }
            }
            else if (IsRouteMiddlewareDecorator(decorator.Name))
            {
                foreach (var argExpr in decorator.Arguments)
                {
                    var arg = EvaluateDecoratorArgument(argExpr);
                    if (arg.Type == ValueType.String)
                    {
                        var fnName = arg.AsString();
                        if (!string.IsNullOrWhiteSpace(fnName))
                        {
                            middlewareNames.Add(fnName);
                        }
                    }
                }
            }
            else if (IsValidationDecorator(decorator.Name))
            {
                var arg = GetDecoratorArgument(decorator, 0);
                if (arg != null)
                {
                    validationSchema = arg;
                }
            }
        }

        return new RouteMetadata(groupPrefix, versionPrefix, middlewareNames, validationSchema);
    }

    private static bool IsGroupDecorator(string decoratorName)
    {
        return decoratorName == "RouteGroup" || decoratorName == "Group" || decoratorName == "Prefix";
    }

    private static bool IsVersionDecorator(string decoratorName)
    {
        return decoratorName == "Version" || decoratorName == "ApiVersion";
    }

    private static bool IsRouteMiddlewareDecorator(string decoratorName)
    {
        return decoratorName == "Use" || decoratorName == "Middleware";
    }

    private static bool IsValidationDecorator(string decoratorName)
    {
        return decoratorName == "Validate";
    }

    private static string BuildValidationMessage(List<RouteValidationError> errors)
    {
        return WebRuntimeHelpers.BuildValidationFailureMessage(errors);
    }

    private bool ValidateRouteInput(
        Route route,
        Dictionary<string, string> pathParams,
        Dictionary<string, string> queryParams,
        RuntimeValue? requestBody,
        string correlationId,
        HttpListenerResponse response)
    {
        var schema = route.Metadata.ValidationSchema;
        if (schema.Type == ValueType.Null)
        {
            return true;
        }

        var hasSchema = schema.Type != ValueType.String || !string.IsNullOrWhiteSpace(schema.AsString());
        if (!hasSchema)
        {
            return true;
        }

        if (WebRuntimeHelpers.ValidateRequest(
                schema,
                pathParams,
                queryParams,
                requestBody,
                out var errors))
        {
            return true;
        }

        response.StatusCode = 400;
        var payload = WebRuntimeHelpers.CreateErrorRuntimeValue(
            400,
            "ValidationError",
            BuildValidationMessage(errors),
            correlationId,
            errors);
        WriteJsonResponse(response, payload);
        return false;
    }

    private async Task<bool> ExecuteRouteMiddlewareChainAsync(
        Route route,
        RequestContextInstance requestContext,
        ResponseContextInstance responseContext)
    {
        var middlewareNames = route.Metadata.MiddlewareFunctionNames;
        if (middlewareNames == null || middlewareNames.Count == 0)
        {
            return true;
        }

        var chain = new WebMiddlewareChain();
        foreach (var middlewareName in middlewareNames)
        {
            chain.Add(middlewareName);
        }

        return await chain.ExecuteAsync(
            requestContext,
            responseContext,
            async (registration, args) =>
            {
                if (string.IsNullOrEmpty(registration.FunctionName))
                {
                    throw new Exception("Route middleware registration must use function names.");
                }

                if (_interpreter != null)
                {
                    var middlewareInterpreter = _interpreter.CreateExecutionInterpreter();
                    var function = ResolveFunction(middlewareInterpreter, registration.FunctionName!);
                    if (function == null)
                    {
                        throw new Exception($"Route middleware function '{registration.FunctionName}' not found");
                    }
                    return await middlewareInterpreter.CallFunctionAsync(function, args);
                }

                return await CallTranspiledFunctionByNameAsync(registration.FunctionName!, args);
            });
    }

    private RuntimeValue? GetDecoratorArgument(Decorator decorator, int index)
    {
        if (index >= decorator.Arguments.Count)
            return null;
        
        return EvaluateDecoratorArgument(decorator.Arguments[index]);
    }

    /// <summary>
    /// Static method to register transpiled routes on all RestServer instances.
    /// Called by transpiled code's RegisterDecoratedFunctions method.
    /// Routes are applied to existing instances immediately, and stored for future instances.
    /// </summary>
    public static void RegisterTranspiledRoute(
        string method,
        string path,
        string functionName,
        List<string> paramNames,
        List<Parser.AST.Declarations.Decorator>? paramDecorators,
        string? groupPrefix = null,
        string? versionPrefix = null,
        List<string>? routeMiddlewareFunctions = null,
        string? validationSchema = null)
    {
        var metadata = new RouteMetadata(
            groupPrefix,
            versionPrefix,
            routeMiddlewareFunctions ?? new List<string>(),
            string.IsNullOrWhiteSpace(validationSchema) ? RuntimeValue.Null() : RuntimeValue.String(validationSchema));
        var effectivePath = WebRuntimeHelpers.ComposeRoutePath(path, metadata.GroupPrefix, metadata.VersionPrefix);

        // Store the route for future instances (lock order: _pendingRoutesLock first)
        lock (_pendingRoutesLock)
        {
            _pendingRoutes.Add(new PendingRoute(method, effectivePath, functionName, paramNames, paramDecorators, metadata));
        }
        
        // Apply to all existing instances (lock order: _instancesLock second)
        lock (_instancesLock)
        {
            foreach (var instance in _instances)
            {
                instance._routeRegistry.RegisterTranspiledRoute(method, effectivePath, functionName, paramNames, paramDecorators, metadata);
            }
        }
    }
}
