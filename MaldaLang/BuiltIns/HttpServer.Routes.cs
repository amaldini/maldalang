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
    private void ScanForRoutes()
    {
        if (_interpreter == null)
        {
            // For transpiled code, routes are registered via reflection at runtime
            // This will be handled by the transpiled code's RegisterDecoratedFunctions method
            return;
        }
        
        // Scan for @PAGE decorated functions (they handle GET requests)
        var pageFunctions = _interpreter.GetDecoratedFunctions("PAGE");
        
        foreach (var (function, functionName) in pageFunctions)
        {
            if (function.Declaration == null)
                continue;
            
            // Get the path from the decorator arguments
            var decorator = function.Decorators?.FirstOrDefault(d => d.Name == "PAGE");
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
            
            // Register as GET route (PAGE decorator implies GET for HTML pages)
            _routeRegistry.RegisterRoute("GET", effectivePath, function, functionName, paramNames, paramDecorators, routeMetadata);
        }

        // Scan for @COMPONENT decorated functions (Phase A server component entry points).
        // If no path is specified, expose at /components/{functionName}
        var componentFunctions = _interpreter.GetDecoratedFunctions("COMPONENT");

        foreach (var (function, functionName) in componentFunctions)
        {
            if (function.Declaration == null)
                continue;

            var decorator = function.Decorators?.FirstOrDefault(d => d.Name == "COMPONENT");
            if (decorator == null)
                continue;

            string path = $"/components/{functionName}";
            if (decorator.Arguments != null && decorator.Arguments.Count > 0 && decorator.Arguments[0] != null)
            {
                try
                {
                    var pathValue = EvaluateDecoratorArgument(decorator.Arguments[0]);
                    if (pathValue.Type == ValueType.String && !string.IsNullOrWhiteSpace(pathValue.AsString()))
                    {
                        path = pathValue.AsString();
                    }
                }
                catch
                {
                    // Keep default path for invalid decorator arguments.
                }
            }

            var routeMetadata = BuildRouteMetadata(function.Decorators);
            var effectivePath = WebRuntimeHelpers.ComposeRoutePath(
                path,
                routeMetadata.GroupPrefix,
                routeMetadata.VersionPrefix);
            var paramNames = function.Declaration.Parameters;
            var paramDecorators = function.ParameterDecorators;
            _routeRegistry.RegisterRoute("GET", effectivePath, function, functionName, paramNames, paramDecorators, routeMetadata);
        }
        
        // Scan for @AIPAGE decorated functions
        var aiPageFunctions = _interpreter.GetDecoratedFunctions("AIPAGE");
        
        foreach (var (function, functionName) in aiPageFunctions)
        {
            if (function.Declaration == null)
                continue;
            
            var decorator = function.Decorators?.FirstOrDefault(d => d.Name == "AIPAGE");
            if (decorator == null || decorator.Arguments == null || decorator.Arguments.Count < 2)
                continue;
            
            var pathExpr = decorator.Arguments[0];
            var descExpr = decorator.Arguments[1];
            if (pathExpr == null || descExpr == null)
                continue;
            
            RuntimeValue pathValue, descValue;
            try
            {
                pathValue = EvaluateDecoratorArgument(pathExpr);
                descValue = EvaluateDecoratorArgument(descExpr);
            }
            catch (Exception)
            {
                continue;
            }
            
            if (pathValue.Type != ValueType.String || descValue.Type != ValueType.String)
                continue;
            
            var path = pathValue.AsString();
            var routeMetadata = BuildRouteMetadata(function.Decorators);
            var effectivePath = WebRuntimeHelpers.ComposeRoutePath(
                path,
                routeMetadata.GroupPrefix,
                routeMetadata.VersionPrefix);
            var description = descValue.AsString();
            var paramNames = function.Declaration.Parameters;
            var paramDecorators = function.ParameterDecorators;
            
            // Store description for AI generation
            lock (_aiCacheLock)
            {
                _aiPageDescriptions[effectivePath] = description;
            }
            
            // Register as GET route with special flag (we'll handle AI generation in HandleDynamicRouteAsync)
            _routeRegistry.RegisterRoute("GET", effectivePath, function, functionName, paramNames, paramDecorators, routeMetadata);
        }
        
        // Scan for @POST decorated functions
        var postFunctions = _interpreter.GetDecoratedFunctions("POST");
        
        foreach (var (function, functionName) in postFunctions)
        {
            if (function.Declaration == null)
                continue;
            
            var decorator = function.Decorators?.FirstOrDefault(d => d.Name == "POST");
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
            
            // Register as POST route
            _routeRegistry.RegisterRoute("POST", effectivePath, function, functionName, paramNames, paramDecorators, routeMetadata);
        }

        // Scan for @ACTION decorated functions (Phase A fragment/form actions).
        // If no path is provided, default to /components/{functionName}/action
        var actionFunctions = _interpreter.GetDecoratedFunctions("ACTION");

        foreach (var (function, functionName) in actionFunctions)
        {
            if (function.Declaration == null)
                continue;

            var decorator = function.Decorators?.FirstOrDefault(d => d.Name == "ACTION");
            if (decorator == null)
                continue;

            string path = $"/components/{functionName}/action";
            if (decorator.Arguments != null && decorator.Arguments.Count > 0 && decorator.Arguments[0] != null)
            {
                try
                {
                    var pathValue = EvaluateDecoratorArgument(decorator.Arguments[0]);
                    if (pathValue.Type == ValueType.String && !string.IsNullOrWhiteSpace(pathValue.AsString()))
                    {
                        path = pathValue.AsString();
                    }
                }
                catch
                {
                    // Keep default path for invalid decorator arguments.
                }
            }

            var routeMetadata = BuildRouteMetadata(function.Decorators);
            var effectivePath = WebRuntimeHelpers.ComposeRoutePath(
                path,
                routeMetadata.GroupPrefix,
                routeMetadata.VersionPrefix);
            var paramNames = function.Declaration.Parameters;
            var paramDecorators = function.ParameterDecorators;
            _routeRegistry.RegisterRoute("POST", effectivePath, function, functionName, paramNames, paramDecorators, routeMetadata);
        }
        
        // Scan for @GET decorated functions
        var getFunctions = _interpreter.GetDecoratedFunctions("GET");
        
        foreach (var (function, functionName) in getFunctions)
        {
            if (function.Declaration == null)
                continue;
            
            var decorator = function.Decorators?.FirstOrDefault(d => d.Name == "GET");
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
            
            // Register as GET route
            _routeRegistry.RegisterRoute("GET", effectivePath, function, functionName, paramNames, paramDecorators, routeMetadata);
        }

        // Scan for @LIVE decorated functions (Phase A live stream endpoints).
        // If no path is provided, default to /components/{functionName}/live
        var liveFunctions = _interpreter.GetDecoratedFunctions("LIVE");

        foreach (var (function, functionName) in liveFunctions)
        {
            if (function.Declaration == null)
                continue;

            var decorator = function.Decorators?.FirstOrDefault(d => d.Name == "LIVE");
            if (decorator == null)
                continue;

            string path = $"/components/{functionName}/live";
            if (decorator.Arguments != null && decorator.Arguments.Count > 0 && decorator.Arguments[0] != null)
            {
                try
                {
                    var pathValue = EvaluateDecoratorArgument(decorator.Arguments[0]);
                    if (pathValue.Type == ValueType.String && !string.IsNullOrWhiteSpace(pathValue.AsString()))
                    {
                        path = pathValue.AsString();
                    }
                }
                catch
                {
                    // Keep default path for invalid decorator arguments.
                }
            }

            var routeMetadata = BuildRouteMetadata(function.Decorators);
            var effectivePath = WebRuntimeHelpers.ComposeRoutePath(
                path,
                routeMetadata.GroupPrefix,
                routeMetadata.VersionPrefix);
            var paramNames = function.Declaration.Parameters;
            var paramDecorators = function.ParameterDecorators;
            _routeRegistry.RegisterRoute("GET", effectivePath, function, functionName, paramNames, paramDecorators, routeMetadata);
        }
    }
    
    private RuntimeValue EvaluateDecoratorArgument(Expression expr)
    {
        if (_interpreter == null)
            throw new Exception("Decorator evaluation requires interpreter.");

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

    private RuntimeValue? GetDecoratorArgument(Decorator decorator, int index)
    {
        if (index >= decorator.Arguments.Count)
            return null;

        return EvaluateDecoratorArgument(decorator.Arguments[index]);
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
        HttpListenerResponse response,
        HttpListenerRequest request,
        string path)
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

        var payload = WebRuntimeHelpers.CreateErrorRuntimeValue(
            400,
            "ValidationError",
            BuildValidationMessage(errors),
            correlationId,
            errors);
        WriteErrorResult(response, payload, 400, request, path);
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

    /// <summary>
    /// Static method to register transpiled routes on all HttpServer instances.
    /// Called by transpiled code's RegisterDecoratedFunctions method.
    /// Routes are applied to existing instances immediately, and stored for future instances.
    /// </summary>
    public static void RegisterTranspiledRoute(
        string method,
        string path,
        string functionName,
        List<string> paramNames,
        List<Decorator>? paramDecorators,
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

    /// <summary>Drops static pending routes so tests that register conflicts cannot poison later servers.</summary>
    public static void ClearPendingRoutesForTesting()
    {
        lock (_pendingRoutesLock)
        {
            _pendingRoutes.Clear();
        }
    }
}
