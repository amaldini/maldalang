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

public class RestServerInstance : ObjectInstance
{
    private static readonly List<RestServerInstance> _instances = new();
    private static readonly object _instancesLock = new object();
    
    // Store pending transpiled routes that were registered before instances were created
    private static readonly List<PendingRoute> _pendingRoutes = new();
    private static readonly object _pendingRoutesLock = new object();
    
    private class PendingRoute
    {
        public string Method { get; }
        public string Path { get; }
        public string FunctionName { get; }
        public List<string> ParamNames { get; }
        public List<Parser.AST.Declarations.Decorator>? ParamDecorators { get; }
        public RouteMetadata Metadata { get; }
        
        public PendingRoute(
            string method,
            string path,
            string functionName,
            List<string> paramNames,
            List<Parser.AST.Declarations.Decorator>? paramDecorators,
            RouteMetadata? metadata = null)
        {
            Method = method;
            Path = path;
            FunctionName = functionName;
            ParamNames = paramNames;
            ParamDecorators = paramDecorators;
            Metadata = metadata ?? new RouteMetadata();
        }
    }
    
    private HttpListener? _listener;
    private int _port;
    private string _host;
    private bool _isRunning = false;
    private Thread? _serverThread;
    private RouteRegistry _routeRegistry;
    private Interpreter? _interpreter;
    private HttpServerInstance? _mountedHost;
    private bool _routesScanned;
    
    // CORS configuration
    private bool _corsEnabled = false;
    private string _corsOrigin = "*";
    private List<string> _corsMethods = new List<string> { "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS" };
    private List<string> _corsHeaders = new List<string> { "Content-Type", "Authorization" };
    
    // Swagger configuration
    private bool _swaggerEnabled = false;

    // Global middleware pipeline (use(req, res, next)).
    private readonly WebMiddlewareChain _middlewareChain = new();
    private RateLimiter? _rateLimiter;
    private string _rateLimitKeyStrategy = "ipOrToken";
    private bool _csrfEnabled;
    private string _csrfSecret = string.Empty;
    private string _csrfCookieName = WebRuntimeHelpers.DefaultCsrfCookieName;
    private string _csrfHeaderName = WebRuntimeHelpers.DefaultCsrfHeaderName;
    private SessionOptions? _sessionOptions;
    private bool _trustProxy;
    private string _trustedProxyHeader = "X-Forwarded-For";
    private int _trustedProxyHopIndex;
    private bool _rateLimitHeadersEnabled;
    private bool _rateLimitRemainingEnabled = true;
    private int _rateLimitLimit = 60;
    private int _rateLimitWindowSeconds = 60;
    
    public RestServerInstance(int port = 0, string? host = null, Interpreter? interpreter = null) : base(null)
    {
        if (port != 0 && (port < 1 || port > 65535))
            throw new Exception("RestServer() port must be 0 (deferred/mounted) or between 1 and 65535");

        _port = port;
        _host = host ?? "localhost";
        _interpreter = interpreter; // Allow null for transpiled code
        _routeRegistry = new RouteRegistry();
        
        // Register this instance for transpiled code route registration
        lock (_instancesLock)
        {
            _instances.Add(this);
        }
        
        // Apply any pending routes that were registered before this instance was created
        lock (_pendingRoutesLock)
        {
            foreach (var pendingRoute in _pendingRoutes)
            {
                _routeRegistry.RegisterTranspiledRoute(
                    pendingRoute.Method, 
                    pendingRoute.Path, 
                    pendingRoute.FunctionName, 
                    pendingRoute.ParamNames, 
                    pendingRoute.ParamDecorators,
                    pendingRoute.Metadata);
            }
        }
    }

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
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "port")
            return RuntimeValue.Integer(_port);
        if (name == "isRunning")
            return RuntimeValue.Boolean(_isRunning);
        
        // Handle method access
        if (name == "start" || name == "stop" || name == "enableCORS" || 
            name == "setCORSOrigin" || name == "setCORSMethods" || name == "setCORSHeaders" ||
            name == "getRoutes" || name == "enableSwagger" || name == "use" ||
            name == "setRateLimit" || name == "disableRateLimit" || name == "enableCsrf" || name == "disableCsrf" ||
            name == "enableSession" || name == "disableSession" ||
            name == "configureTrustedProxy" || name == "setRateLimitHeaders")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on RestServer.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "start":
                if (args.Count != 0)
                    throw new Exception("start() expects 0 arguments");
                Start();
                return RuntimeValue.Null();
            
            case "stop":
                if (args.Count != 0)
                    throw new Exception("stop() expects 0 arguments");
                Stop();
                return RuntimeValue.Null();
            
            case "enableCORS":
                if (args.Count != 1 || args[0].Type != ValueType.Boolean)
                    throw new Exception("enableCORS() expects 1 boolean argument");
                EnableCORS(args[0].AsBoolean());
                return RuntimeValue.Null();
            
            case "setCORSOrigin":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setCORSOrigin() expects 1 string argument");
                SetCORSOrigin(args[0].AsString());
                return RuntimeValue.Null();
            
            case "setCORSMethods":
                if (args.Count != 1 || args[0].Type != ValueType.Array)
                    throw new Exception("setCORSMethods() expects 1 array argument");
                var methods = args[0].AsArray().Select(v => v.AsString()).ToList();
                SetCORSMethods(methods);
                return RuntimeValue.Null();
            
            case "setCORSHeaders":
                if (args.Count != 1 || args[0].Type != ValueType.Array)
                    throw new Exception("setCORSHeaders() expects 1 array argument");
                var headers = args[0].AsArray().Select(v => v.AsString()).ToList();
                SetCORSHeaders(headers);
                return RuntimeValue.Null();
            
            case "getRoutes":
                if (args.Count != 0)
                    throw new Exception("getRoutes() expects 0 arguments");
                return GetRoutes();
            
            case "enableSwagger":
                if (args.Count != 1 || args[0].Type != ValueType.Boolean)
                    throw new Exception("enableSwagger() expects 1 boolean argument");
                EnableSwagger(args[0].AsBoolean());
                return RuntimeValue.Null();

            case "use":
                if (args.Count < 1 || args.Count > 2 ||
                    (args[0].Type != ValueType.Function && args[0].Type != ValueType.String))
                    throw new Exception("use() expects middleware function/name and optional options object");
                if (args.Count == 2 && args[1].Type != ValueType.Object)
                    throw new Exception("use() options must be an object when provided");
                RegisterMiddleware(args[0], args.Count == 2 ? args[1] : null);
                return RuntimeValue.Null();

            case "setRateLimit":
                ConfigureRateLimit(args);
                return RuntimeValue.Null();

            case "disableRateLimit":
                if (args.Count != 0)
                    throw new Exception("disableRateLimit() expects 0 arguments");
                _rateLimiter = null;
                return RuntimeValue.Null();

            case "enableCsrf":
                ConfigureCsrf(args);
                return RuntimeValue.Null();

            case "disableCsrf":
                if (args.Count != 0)
                    throw new Exception("disableCsrf() expects 0 arguments");
                _csrfEnabled = false;
                _csrfSecret = string.Empty;
                return RuntimeValue.Null();

            case "enableSession":
                _sessionOptions = SessionRuntime.ParseEnableSessionArgs(args);
                return RuntimeValue.Null();

            case "disableSession":
                if (args.Count != 0)
                    throw new Exception("disableSession() expects 0 arguments");
                _sessionOptions = null;
                return RuntimeValue.Null();

            case "configureTrustedProxy":
                ConfigureTrustedProxy(args);
                return RuntimeValue.Null();

            case "setRateLimitHeaders":
                ConfigureRateLimitHeaders(args);
                return RuntimeValue.Null();
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
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
        
        try
        {
            // Scan for routes before starting
            EnsureRoutesScanned();
            
            // Print registered routes
            var routesSummary = _routeRegistry.GetRoutesSummary();
            Console.WriteLine(routesSummary);
            
            _listener = new HttpListener();
            var prefix = _host == "0.0.0.0" ? $"http://*:{_port}/" : $"http://{_host}:{_port}/";
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _isRunning = true;
            
            // Use Task.Run to start async request handling
            _ = Task.Run(async () => await HandleRequestsAsync());
        }
        catch (Exception ex)
        {
            _isRunning = false;
            throw new Exception($"Failed to start RestServer: {ex.Message}");
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
    
    private void EnableCORS(bool enabled)
    {
        _corsEnabled = enabled;
    }
    
    private void SetCORSOrigin(string origin)
    {
        _corsOrigin = origin;
    }
    
    private void SetCORSMethods(List<string> methods)
    {
        _corsMethods = methods;
    }
    
    private void SetCORSHeaders(List<string> headers)
    {
        _corsHeaders = headers;
    }
    
    private void EnableSwagger(bool enabled)
    {
        _swaggerEnabled = enabled;
    }

    private void RegisterMiddleware(RuntimeValue middlewareValue, RuntimeValue? optionsValue)
    {
        var exceptPaths = ParseMiddlewareExceptPaths(optionsValue);
        if (middlewareValue.Type == ValueType.Function)
        {
            _middlewareChain.Add(middlewareValue.AsFunction(), exceptPaths);
            return;
        }

        _middlewareChain.Add(middlewareValue.AsString(), exceptPaths);
    }

    private static List<string>? ParseMiddlewareExceptPaths(RuntimeValue? optionsValue)
    {
        if (optionsValue == null)
        {
            return null;
        }

        if (optionsValue.Type != ValueType.Object)
        {
            throw new Exception("use() options must be an object when provided");
        }

        // Interpreter object literals are JsonObject; C# transpile emits DictionaryInstance.
        var exceptValue = optionsValue.AsObject().Get("except", null);
        if (exceptValue.Type == ValueType.Null)
        {
            return null;
        }

        if (exceptValue.Type != ValueType.Array)
        {
            throw new Exception("use() options.except must be an array of path strings");
        }

        var paths = new List<string>();
        foreach (var entry in exceptValue.AsArray())
        {
            if (entry.Type != ValueType.String || string.IsNullOrWhiteSpace(entry.AsString()))
            {
                throw new Exception("use() options.except must contain non-empty path strings");
            }

            paths.Add(entry.AsString().Trim());
        }

        return paths;
    }

    private void ConfigureRateLimit(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3)
            throw new Exception("setRateLimit() expects limit, windowSeconds, and optional keyStrategy");
        if (args[0].Type != ValueType.Integer || args[1].Type != ValueType.Integer)
            throw new Exception("setRateLimit() expects integer limit and integer windowSeconds");

        var limit = args[0].AsInteger();
        var windowSeconds = args[1].AsInteger();
        if (limit <= 0 || windowSeconds <= 0)
            throw new Exception("setRateLimit() limit and windowSeconds must be > 0");

        _rateLimitKeyStrategy = "ipOrToken";
        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.String)
                throw new Exception("setRateLimit() keyStrategy must be a string when provided");
            _rateLimitKeyStrategy = args[2].AsString();
        }

        _rateLimiter = _rateLimiter == null
            ? new RateLimiter(limit, TimeSpan.FromSeconds(windowSeconds))
            : _rateLimiter;
        _rateLimiter.SetRateLimit(limit, TimeSpan.FromSeconds(windowSeconds));
        _rateLimitLimit = limit;
        _rateLimitWindowSeconds = windowSeconds;
    }

    private void ConfigureTrustedProxy(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new Exception("configureTrustedProxy() expects enabled, optional headerName, optional hopIndex");
        if (args[0].Type != ValueType.Boolean)
            throw new Exception("configureTrustedProxy() enabled must be a boolean");

        _trustProxy = args[0].AsBoolean();
        _trustedProxyHeader = "X-Forwarded-For";
        _trustedProxyHopIndex = 0;

        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.String)
                throw new Exception("configureTrustedProxy() headerName must be a string");
            var headerName = args[1].AsString().Trim();
            if (!string.IsNullOrWhiteSpace(headerName))
            {
                _trustedProxyHeader = headerName;
            }
        }

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.Integer)
                throw new Exception("configureTrustedProxy() hopIndex must be an integer");
            var hopIndex = args[2].AsInteger();
            if (hopIndex < 0)
                throw new Exception("configureTrustedProxy() hopIndex must be >= 0");
            _trustedProxyHopIndex = hopIndex;
        }
    }

    private void ConfigureRateLimitHeaders(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("setRateLimitHeaders() expects enabled, and optional includeRemaining");
        if (args[0].Type != ValueType.Boolean)
            throw new Exception("setRateLimitHeaders() enabled must be a boolean");

        _rateLimitHeadersEnabled = args[0].AsBoolean();
        _rateLimitRemainingEnabled = true;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Boolean)
                throw new Exception("setRateLimitHeaders() includeRemaining must be a boolean");
            _rateLimitRemainingEnabled = args[1].AsBoolean();
        }
    }

    private void ConfigureCsrf(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new Exception("enableCsrf() expects secret, and optional cookie/header names");
        if (args[0].Type != ValueType.String)
            throw new Exception("enableCsrf() secret must be a string");

        var secret = args[0].AsString();
        if (string.IsNullOrWhiteSpace(secret))
            throw new Exception("enableCsrf() secret cannot be empty");

        _csrfSecret = secret;
        _csrfCookieName = WebRuntimeHelpers.DefaultCsrfCookieName;
        _csrfHeaderName = WebRuntimeHelpers.DefaultCsrfHeaderName;
        if (args.Count >= 2)
        {
            if (args[1].Type != ValueType.String)
                throw new Exception("enableCsrf() cookieName must be a string");
            _csrfCookieName = args[1].AsString();
        }

        if (args.Count == 3)
        {
            if (args[2].Type != ValueType.String)
                throw new Exception("enableCsrf() headerName must be a string");
            _csrfHeaderName = args[2].AsString();
        }

        _csrfEnabled = true;
    }
    
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
    
    private void HandleCORS(HttpListenerResponse response, HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        if (origin != null && (_corsOrigin == "*" || _corsOrigin == origin))
        {
            response.Headers.Add("Access-Control-Allow-Origin", origin);
        }
        else if (_corsOrigin != "*")
        {
            response.Headers.Add("Access-Control-Allow-Origin", _corsOrigin);
        }
        
        response.Headers.Add("Access-Control-Allow-Methods", string.Join(", ", _corsMethods));
        response.Headers.Add("Access-Control-Allow-Headers", string.Join(", ", _corsHeaders));
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
    
    private List<RuntimeValue> BindParameters(Route route, Dictionary<string, string> pathParams, 
        Dictionary<string, string> queryParams, RuntimeValue? requestBody, HttpListenerRequest request,
        RequestContextInstance requestContext, ResponseContextInstance responseContext)
    {
        var args = new List<RuntimeValue>();
        var paramNames = route.ParameterNames;
        var paramDecorators = route.ParameterDecorators;
        
        // Check if any parameter has decorators (opt-in explicit mode)
        // Parameter decorators are stored in order matching parameter positions
        bool useDecoratorMode = paramDecorators != null && paramDecorators.Count > 0;
        
        for (int i = 0; i < paramNames.Count; i++)
        {
            var paramName = paramNames[i];
            RuntimeValue? value = null;

            // Explicitly support request/response objects in both binding modes.
            if (paramName == "request" || paramName == "req")
            {
                args.Add(RuntimeValue.Object(requestContext));
                continue;
            }
            if (paramName == "response" || paramName == "res")
            {
                args.Add(RuntimeValue.Object(responseContext));
                continue;
            }
            if (paramName == "params" || paramName == "pathParams")
            {
                args.Add(requestContext.Get("params", null));
                continue;
            }
            if (paramName == "query" || paramName == "queryParams")
            {
                args.Add(requestContext.Get("query", null));
                continue;
            }
            if (paramName == "headers")
            {
                args.Add(requestContext.Get("headers", null));
                continue;
            }
            if (paramName == "cookies")
            {
                args.Add(requestContext.Get("cookies", null));
                continue;
            }
            
            if (useDecoratorMode)
            {
                // Decorator-based binding - decorators are stored in order
                Decorator? decorator = null;
                if (i < paramDecorators!.Count)
                {
                    decorator = paramDecorators[i];
                }
                
                if (decorator != null)
                {
                    if (decorator.Name == "PathParam")
                    {
                        var pathParamName = GetDecoratorArgument(decorator, 0)?.AsString() ?? paramName;
                        value = pathParams.ContainsKey(pathParamName) 
                            ? RuntimeValue.String(pathParams[pathParamName]) 
                            : RuntimeValue.Null();
                    }
                    else if (decorator.Name == "QueryParam")
                    {
                        var queryParamName = GetDecoratorArgument(decorator, 0)?.AsString() ?? paramName;
                        value = queryParams.ContainsKey(queryParamName) 
                            ? RuntimeValue.String(queryParams[queryParamName]) 
                            : RuntimeValue.Null();
                    }
                    else if (decorator.Name == "Body")
                    {
                        value = requestBody ?? RuntimeValue.Null();
                    }
                    else
                    {
                        // Unknown decorator, treat as no binding
                        value = RuntimeValue.Null();
                    }
                }
                else
                {
                    // No decorator for this parameter in explicit mode - error or null
                    value = RuntimeValue.Null();
                }
            }
            else
            {
                // Name-based binding (default)
                if (route.PathParameterNames.Contains(paramName))
                {
                    // Path parameter
                    value = pathParams.ContainsKey(paramName) 
                        ? RuntimeValue.String(pathParams[paramName]) 
                        : RuntimeValue.Null();
                }
                else if (paramName == "body")
                {
                    // Request body (special name)
                    value = requestBody ?? RuntimeValue.Null();
                }
                else
                {
                    // Query parameter
                    value = queryParams.ContainsKey(paramName) 
                        ? RuntimeValue.String(queryParams[paramName]) 
                        : RuntimeValue.Null();
                }
            }
            
            args.Add(value);
        }
        
        return args;
    }

    private RequestContextInstance CreateRequestContext(
        HttpListenerRequest request,
        Dictionary<string, string> pathParams,
        Dictionary<string, string> queryParams,
        RuntimeValue? requestBody,
        string correlationId,
        string? pathOverride = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in request.Headers.AllKeys)
        {
            if (key != null)
            {
                headers[key] = request.Headers[key] ?? string.Empty;
            }
        }

        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Cookie cookie in request.Cookies)
        {
            cookies[cookie.Name] = cookie.Value;
        }

        var remoteIp = ResolveClientIp(request);

        return new RequestContextInstance(
            request.HttpMethod,
            pathOverride ?? request.Url?.AbsolutePath ?? "/",
            queryParams,
            headers,
            cookies,
            requestBody,
            pathParams,
            correlationId,
            remoteIp,
            request.Url?.Query ?? string.Empty,
            request.Url?.ToString(),
            request.Url?.Scheme,
            request.UserHostName ?? request.Headers["Host"] ?? string.Empty,
            request.ContentType ?? string.Empty,
            sessionOptions: _sessionOptions);
    }

    private void CommitAndApplyResponse(
        RequestContextInstance requestContext,
        ResponseContextInstance responseContext,
        HttpListenerResponse response,
        HttpListenerRequest request)
    {
        if (responseContext.IsFlushed)
            return;

        SessionRuntime.CommitSession(requestContext, responseContext, request.IsSecureConnection);
        responseContext.ApplyTo(response, request: request);
    }

    private bool ValidateRateLimit(
        RequestContextInstance requestContext,
        HttpListenerResponse response,
        string correlationId)
    {
        if (_rateLimiter == null)
        {
            return true;
        }

        var key = ResolveRateLimitKey(requestContext);
        if (_rateLimiter.CheckRateLimit(key))
        {
            ApplyRateLimitHeaders(response, key, rateLimited: false);
            return true;
        }

        response.StatusCode = 429;
        ApplyRateLimitHeaders(response, key, rateLimited: true);
        var payload = WebRuntimeHelpers.CreateErrorRuntimeValue(
            429,
            "RateLimitExceeded",
            "Too many requests. Please try again later.",
            correlationId);
        WriteJsonResponse(response, payload);
        return false;
    }

    private void ApplyRateLimitHeaders(HttpListenerResponse response, string key, bool rateLimited)
    {
        if (!_rateLimitHeadersEnabled || _rateLimiter == null)
        {
            return;
        }

        response.Headers["X-RateLimit-Limit"] = _rateLimitLimit.ToString();

        if (_rateLimitRemainingEnabled)
        {
            var remaining = rateLimited ? 0 : _rateLimiter.GetRemainingRequests(key);
            response.Headers["X-RateLimit-Remaining"] = Math.Max(0, remaining).ToString();
        }

        var retryAfter = rateLimited ? _rateLimiter.GetRetryAfterSeconds(key) : 0;
        if (retryAfter <= 0 && rateLimited)
        {
            retryAfter = Math.Max(1, _rateLimitWindowSeconds);
        }
        if (retryAfter > 0)
        {
            response.Headers["Retry-After"] = retryAfter.ToString();
        }
    }

    private string ResolveClientIp(HttpListenerRequest request)
    {
        var remoteIp = request.RemoteEndPoint?.Address?.ToString() ?? string.Empty;
        if (!_trustProxy)
        {
            return remoteIp;
        }

        var rawForwarded = request.Headers[_trustedProxyHeader];
        if (string.IsNullOrWhiteSpace(rawForwarded))
        {
            return remoteIp;
        }

        var hops = rawForwarded
            .Split(',')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (hops.Count == 0)
        {
            return remoteIp;
        }

        var index = _trustedProxyHopIndex;
        if (index < 0 || index >= hops.Count)
        {
            index = 0;
        }

        return hops[index];
    }

    private string ResolveRateLimitKey(RequestContextInstance requestContext)
    {
        var headers = (JsonObject)requestContext.Get("headers", null).AsObject();
        var ip = requestContext.Get("ip", null).AsString();
        var hasAuthHeader = headers.Get("Authorization", null);
        var token = hasAuthHeader.Type == ValueType.String ? hasAuthHeader.AsString() : string.Empty;
        var userHeader = headers.Get("X-User-Id", null);
        var userId = userHeader.Type == ValueType.String ? userHeader.AsString() : string.Empty;
        var verifiedSub = requestContext.GetVerifiedSubject();

        return _rateLimitKeyStrategy switch
        {
            "user" => !string.IsNullOrWhiteSpace(userId) ? $"user:{userId}" : FallbackRateLimitKey(ip, token),
            "token" => !string.IsNullOrWhiteSpace(token) ? $"token:{token}" : FallbackRateLimitKey(ip, token),
            "ip" => !string.IsNullOrWhiteSpace(ip) ? $"ip:{ip}" : "global",
            "sub" => !string.IsNullOrWhiteSpace(verifiedSub) ? $"sub:{verifiedSub}" : FallbackRateLimitKey(ip, token),
            "verifiedSub" => !string.IsNullOrWhiteSpace(verifiedSub) ? $"sub:{verifiedSub}" : FallbackRateLimitKey(ip, token),
            "verifiedSubOrIp" => !string.IsNullOrWhiteSpace(verifiedSub) ? $"sub:{verifiedSub}" : FallbackRateLimitKey(ip, token),
            _ => FallbackRateLimitKey(ip, token)
        };
    }

    private static string FallbackRateLimitKey(string ip, string token)
    {
        if (!string.IsNullOrWhiteSpace(ip))
        {
            return $"ip:{ip}";
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            return $"token:{token}";
        }

        return "global";
    }

    private bool ValidateCsrf(
        RequestContextInstance requestContext,
        ResponseContextInstance responseContext,
        RuntimeValue? requestBody,
        HttpListenerRequest request,
        HttpListenerResponse response,
        string correlationId)
    {
        if (!_csrfEnabled)
        {
            return true;
        }

        var cookies = (JsonObject)requestContext.Get("cookies", null).AsObject();
        var cookieToken = cookies.Get(_csrfCookieName, null);
        var cookieTokenValue = cookieToken.Type == ValueType.String ? cookieToken.AsString() : string.Empty;

        if (WebRuntimeHelpers.RequiresCsrfValidation(requestContext.Method))
        {
            var requestToken = TryResolveCsrfRequestToken(requestContext, requestBody);
            var valid =
                !string.IsNullOrWhiteSpace(cookieTokenValue) &&
                !string.IsNullOrWhiteSpace(requestToken) &&
                cookieTokenValue == requestToken &&
                WebRuntimeHelpers.VerifyCsrfToken(requestToken, _csrfSecret);

            if (!valid)
            {
                response.StatusCode = 403;
                var payload = WebRuntimeHelpers.CreateErrorRuntimeValue(
                    403,
                    "CsrfValidationFailed",
                    "Invalid or missing CSRF token.",
                    correlationId);
                WriteJsonResponse(response, payload);
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(cookieTokenValue) || !WebRuntimeHelpers.VerifyCsrfToken(cookieTokenValue, _csrfSecret))
        {
            var csrfToken = WebRuntimeHelpers.GenerateCsrfToken(_csrfSecret);
            var cookieOptions = new JsonObject();
            cookieOptions.Set("secure", RuntimeValue.Boolean(request.IsSecureConnection));
            cookieOptions.Set("httpOnly", RuntimeValue.Boolean(false));
            responseContext.CallMethod(
                "cookie",
                new List<RuntimeValue>
                {
                    RuntimeValue.String(_csrfCookieName),
                    RuntimeValue.String(csrfToken),
                    RuntimeValue.Object(cookieOptions)
                });
        }

        return true;
    }

    private string TryResolveCsrfRequestToken(RequestContextInstance requestContext, RuntimeValue? requestBody)
    {
        var headers = (JsonObject)requestContext.Get("headers", null).AsObject();
        var headerToken = headers.Get(_csrfHeaderName, null);
        if (headerToken.Type == ValueType.String && !string.IsNullOrWhiteSpace(headerToken.AsString()))
        {
            return headerToken.AsString();
        }

        if (requestBody != null && requestBody.Type == ValueType.Object && requestBody.AsObject() is JsonObject bodyObj)
        {
            var csrf = bodyObj.Get("csrfToken", null);
            if (csrf.Type == ValueType.String && !string.IsNullOrWhiteSpace(csrf.AsString()))
            {
                return csrf.AsString();
            }

            var alt = bodyObj.Get("_csrf", null);
            if (alt.Type == ValueType.String && !string.IsNullOrWhiteSpace(alt.AsString()))
            {
                return alt.AsString();
            }
        }

        return string.Empty;
    }
    
    private RuntimeValue? GetDecoratorArgument(Decorator decorator, int index)
    {
        if (index >= decorator.Arguments.Count)
            return null;
        
        return EvaluateDecoratorArgument(decorator.Arguments[index]);
    }
    
    private async Task<RuntimeValue> CallRouteFunctionAsync(FunctionValue function, List<RuntimeValue> args, Interpreter interpreter)
    {
        // Use the provided interpreter to call the function
        try
        {
            return await interpreter.CallFunctionAsync(function, args);
        }
        catch (Exception ex)
        {
            // Check if it's a RuntimeException with status
            if (ex is WebRuntimeException webRuntimeException)
            {
                throw webRuntimeException;
            }

            if (ex is RuntimeException rex)
            {
                throw rex;
            }
            throw (RuntimeException)RuntimeDiagnostics.PreserveContext(ex, interpreter);
        }
    }

    private async Task<bool> ExecuteMiddlewareChainAsync(RequestContextInstance requestContext, ResponseContextInstance responseContext)
    {
        if (_middlewareChain.Count == 0)
        {
            return true;
        }

        return await _middlewareChain.ExecuteAsync(
            requestContext,
            responseContext,
            async (registration, args) =>
            {
                if (registration.Function != null && _interpreter != null)
                {
                    var middlewareInterpreter = _interpreter.CreateExecutionInterpreter();
                    return await middlewareInterpreter.CallFunctionAsync(registration.Function, args);
                }

                if (!string.IsNullOrEmpty(registration.FunctionName))
                {
                    if (_interpreter != null)
                    {
                        var middlewareInterpreter = _interpreter.CreateExecutionInterpreter();
                        var function = ResolveFunction(middlewareInterpreter, registration.FunctionName!);
                        if (function == null)
                        {
                            throw new Exception($"Middleware function '{registration.FunctionName}' not found");
                        }
                        return await middlewareInterpreter.CallFunctionAsync(function, args);
                    }

                    return await CallTranspiledFunctionByNameAsync(registration.FunctionName!, args);
                }

                throw new Exception("Invalid middleware registration");
            });
    }

    private FunctionValue? ResolveFunction(Interpreter interpreter, string functionName)
    {
        if (functionName.Contains("."))
        {
            var parts = functionName.Split('.', 2);
            var className = parts[0];
            var methodName = parts[1];

            if (interpreter._classes.TryGetValue(className, out var klass))
            {
                if (klass.Methods.TryGetValue(methodName, out var classMethod))
                {
                    return classMethod;
                }

                if (klass.StaticMethods.TryGetValue(methodName, out var staticMethod))
                {
                    return staticMethod;
                }
            }

            return null;
        }

        try
        {
            var funcValue = interpreter._globals.Get(functionName);
            if (funcValue.Type == ValueType.Function)
            {
                return funcValue.AsFunction();
            }
        }
        catch
        {
            // Function not found.
        }

        return null;
    }
    
    private async Task<RuntimeValue> CallTranspiledRouteFunctionAsync(Route route, List<RuntimeValue> functionArgs)
    {
        // Check if this is an LLMServer route (function names start with "llmServer")
        if (route.FunctionName.StartsWith("llmServer"))
        {
            return await CallLLMServerEndpointAsync(route, functionArgs);
        }
        
        // Call transpiled static method via reflection
        // The Program class is in the GeneratedCode namespace
        // The transpiled code is in the entry assembly (the executable), not the executing assembly
        // Use GetType() instead of GetTypes() to avoid loading all types and their dependencies
        // This prevents issues with missing optional dependencies like LLamaSharp
        Type? programType = null;
        
        // Try entry assembly first (where the transpiled code is)
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            programType = entryAssembly.GetType("GeneratedCode.Program");
        }
        
        // Fallback to executing assembly if not found
        if (programType == null)
        {
            var executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            programType = executingAssembly.GetType("GeneratedCode.Program");
        }
        
        // Last resort: search all loaded assemblies (but avoid GetTypes() to prevent loading dependencies)
        if (programType == null)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    programType = assembly.GetType("GeneratedCode.Program");
                    if (programType != null)
                        break;
                }
                catch
                {
                    // Skip assemblies that can't be queried (e.g., missing dependencies)
                    continue;
                }
            }
        }
        
        if (programType == null)
        {
            throw new Exception("GeneratedCode.Program class not found. Make sure the transpiled code is in the GeneratedCode namespace.");
        }
        
        return await CallTranspiledFunctionFromProgramTypeAsync(programType, route.FunctionName, functionArgs);
    }

    private async Task<RuntimeValue> CallTranspiledFunctionByNameAsync(string functionName, List<RuntimeValue> functionArgs)
    {
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            var entryProgramType = entryAssembly.GetType("GeneratedCode.Program");
            if (entryProgramType != null)
            {
                return await CallTranspiledFunctionFromProgramTypeAsync(entryProgramType, functionName, functionArgs);
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var programType = assembly.GetType("GeneratedCode.Program");
                if (programType != null)
                {
                    return await CallTranspiledFunctionFromProgramTypeAsync(programType, functionName, functionArgs);
                }
            }
            catch
            {
                // Ignore assemblies that cannot be queried.
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type == null)
                {
                    continue;
                }

                var method = type.GetMethod(functionName,
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    return await CallTranspiledFunctionFromProgramTypeAsync(type, functionName, functionArgs);
                }
            }
        }

        throw new Exception("GeneratedCode.Program class not found. Make sure the transpiled code is in the GeneratedCode namespace.");
    }

    private async Task<RuntimeValue> CallTranspiledFunctionFromProgramTypeAsync(Type programType, string functionName, List<RuntimeValue> functionArgs)
    {
        var method = programType.GetMethod(functionName,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        if (method == null)
        {
            throw new Exception($"Transpiled function '{functionName}' not found");
        }

        var args = functionArgs.Select(arg => arg.Type switch
        {
            ValueType.Integer => (object)arg.AsInteger(),
            ValueType.Float => (object)arg.AsFloat(),
            ValueType.String => (object)arg.AsString(),
            ValueType.Boolean => (object)arg.AsBoolean(),
            ValueType.Function => (object)arg.AsFunction(),
            ValueType.Object => (object)arg.AsObject(),
            ValueType.Array => arg.AsArray(),
            _ => null
        }).ToArray();

        var task = (Task<object>)method.Invoke(null, args)!;
        var result = await task;
        return WebRuntimeHelpers.ConvertTranspiledResultToRuntimeValue(result);
    }
    
    private async Task<RuntimeValue> CallLLMServerEndpointAsync(Route route, List<RuntimeValue> functionArgs)
    {
        // Find the LLMServerInstance for this route
        var llmServer = LLMServerInstance.GetInstanceForRoute(route.FunctionName);
        if (llmServer == null)
        {
            throw new Exception($"LLMServer instance not found for route '{route.FunctionName}'");
        }
        
        // Map function name to internal method name
        var methodName = route.FunctionName switch
        {
            "llmServerHealthCheck" => "_endpointHealth",
            "llmServerChatCompletions" => "_endpointChat",
            "llmServerComplete" => "_endpointComplete",
            "llmServerModelInfo" => "_endpointModelInfo",
            "llmServerUpdateSettings" => "_endpointUpdateSettings",
            _ => throw new Exception($"Unknown LLMServer endpoint: {route.FunctionName}")
        };
        
        // Call the instance method
        // Note: LLMServer endpoint methods are synchronous and return RuntimeValue directly
        var result = llmServer.CallMethod(methodName, functionArgs, null);
        
        // Return the result (already a RuntimeValue)
        return result;
    }
    
    private void SerializeResponse(
        HttpListenerResponse response,
        RuntimeValue result,
        RequestContextInstance? requestContext = null,
        ResponseContextInstance? pipelineResponse = null,
        HttpListenerRequest? request = null)
    {
        if (result.Type == ValueType.Object && result.AsObject() is ResponseContextInstance responseContext)
        {
            if (requestContext != null)
            {
                SessionRuntime.CommitSession(requestContext, responseContext, request?.IsSecureConnection ?? false);
            }
            responseContext.ApplyTo(response, request: request);
            return;
        }

        if (requestContext != null && pipelineResponse != null)
        {
            SessionRuntime.CommitSession(requestContext, pipelineResponse, request?.IsSecureConnection ?? false);
            if (pipelineResponse.HasHeaders)
            {
                pipelineResponse.ApplyHeadersTo(response);
            }
        }

        if (WebRuntimeHelpers.TryGetStandardErrorPayload(result, out _, out var errorStatusCode))
        {
            response.StatusCode = errorStatusCode;
            WriteJsonResponse(response, result);
            return;
        }

        // Check if result has a status property (status+data return)
        if (result.Type == ValueType.Object)
        {
            var obj = result.AsObject();
            if (obj is JsonObject jsonObj)
            {
                try
                {
                    var statusValue = jsonObj.Get("status", null);
                    if (statusValue != null && statusValue.Type == ValueType.Integer)
                    {
                        response.StatusCode = statusValue.AsInteger();
                        // Remove status from response body
                        var responseObj = new Dictionary<string, RuntimeValue>();
                        var props = jsonObj.GetProperties();
                        foreach (var kvp in props)
                        {
                            if (kvp.Key != "status")
                                responseObj[kvp.Key] = kvp.Value;
                        }
                        WriteJsonResponse(response, responseObj);
                        return;
                    }
                }
                catch
                {
                    // Not a status object, continue with normal serialization
                }
            }
        }
        
        // Simple return - serialize to JSON
        if (result.Type == ValueType.String)
        {
            // Return string as-is
            var bytes = Encoding.UTF8.GetBytes(result.AsString());
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.StatusCode = 200;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        else
        {
            // Serialize to JSON
            response.StatusCode = 200;
            WriteJsonResponse(response, result);
        }
    }
    
    private void WriteJsonResponse(HttpListenerResponse response, object data)
    {
        string json;
        if (data is RuntimeValue rv)
        {
            json = RuntimeValueToJson(rv);
        }
        else
        {
            json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
            { 
                WriteIndented = false 
            });
        }
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }
    
    private string RuntimeValueToJson(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Object => RuntimeObjectToJson(value.AsObject()),
            ValueType.Array => RuntimeArrayToJson(value.AsArray()),
            ValueType.String => JsonSerializer.Serialize(value.AsString()),
            ValueType.Integer => value.AsInteger().ToString(),
            ValueType.Float => value.AsFloat().ToString("G17"),
            ValueType.Boolean => value.AsBoolean() ? "true" : "false",
            ValueType.Null => "null",
            _ => "null"
        };
    }
    
    private string RuntimeObjectToJson(ObjectInstance obj)
    {
        if (obj is JsonObject jsonObj)
        {
            var props = jsonObj.GetProperties();
            var jsonProps = new List<string>();
            foreach (var kvp in props)
            {
                var key = JsonSerializer.Serialize(kvp.Key);
                var val = RuntimeValueToJson(kvp.Value);
                jsonProps.Add($"{key}:{val}");
            }
            return "{" + string.Join(",", jsonProps) + "}";
        }
        return "{}";
    }
    
    private string RuntimeArrayToJson(List<RuntimeValue> array)
    {
        var items = array.Select(RuntimeValueToJson).ToList();
        return "[" + string.Join(",", items) + "]";
    }
    
    private string GenerateSwaggerJson()
    {
        var routes = _routeRegistry.GetAllRoutes();
        var swagger = new Dictionary<string, object>
        {
            ["openapi"] = "3.0.0",
            ["info"] = new Dictionary<string, object>
            {
                ["title"] = "MaldaLang REST API",
                ["version"] = "1.0.0",
                ["description"] = "Auto-generated API documentation"
            },
            ["servers"] = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["url"] = $"http://{_host}:{_port}",
                    ["description"] = "API Server"
                }
            },
            ["paths"] = new Dictionary<string, object>()
        };
        
        var paths = (Dictionary<string, object>)swagger["paths"];
        
        // Group routes by path
        var routesByPath = routes.GroupBy(r => r.PathPattern);
        
        foreach (var pathGroup in routesByPath)
        {
            var pathPattern = pathGroup.Key;
            // Convert MaldaLang path pattern to OpenAPI path: /api/users/{id}
            var openApiPath = pathPattern;
            
            var pathItem = new Dictionary<string, object>();
            
            foreach (var route in pathGroup)
            {
                var method = route.Method.ToLower();
                var operation = new Dictionary<string, object>
                {
                    ["operationId"] = route.FunctionName,
                    ["summary"] = $"{route.Method} {pathPattern}",
                    ["tags"] = new List<string> { ExtractTagFromPath(pathPattern) }
                };
                
                // Parameters
                var parameters = new List<Dictionary<string, object>>();
                
                // Path parameters
                foreach (var pathParamName in route.PathParameterNames)
                {
                    var pathParamSchema = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    };
                    if (TryGetValidationRule(route, "path", pathParamName, out var pathRuleSchema, out _))
                    {
                        MergeOpenApiSchema(pathParamSchema, pathRuleSchema);
                    }

                    parameters.Add(new Dictionary<string, object>
                    {
                        ["name"] = pathParamName,
                        ["in"] = "path",
                        ["required"] = true,
                        ["schema"] = pathParamSchema
                    });
                }
                
                // Query parameters
                var hasBodyParam = false;
                foreach (var paramName in route.ParameterNames)
                {
                    // Skip path parameters (already added)
                    if (route.PathParameterNames.Contains(paramName))
                        continue;
                    
                    // Check if it's a body parameter
                    if (paramName == "body" || IsBodyParameter(route, paramName))
                    {
                        hasBodyParam = true;
                        continue;
                    }
                    
                    // It's a query parameter
                    var queryParamSchema = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    };
                    var required = false;
                    if (TryGetValidationRule(route, "query", paramName, out var queryRuleSchema, out var queryRequired))
                    {
                        MergeOpenApiSchema(queryParamSchema, queryRuleSchema);
                        required = queryRequired;
                    }

                    parameters.Add(new Dictionary<string, object>
                    {
                        ["name"] = paramName,
                        ["in"] = "query",
                        ["required"] = required,
                        ["schema"] = queryParamSchema
                    });
                }
                
                if (parameters.Count > 0)
                {
                    operation["parameters"] = parameters;
                }
                
                // Request body
                if (hasBodyParam && (method == "post" || method == "put" || method == "patch"))
                {
                    var bodySchema = BuildOpenApiBodySchema(route);
                    operation["requestBody"] = new Dictionary<string, object>
                    {
                        ["required"] = bodySchema.ContainsKey("required") || HasRequiredBodyFields(route),
                        ["content"] = new Dictionary<string, object>
                        {
                            ["application/json"] = new Dictionary<string, object>
                            {
                                ["schema"] = bodySchema
                            }
                        }
                    };
                }
                
                // Responses
                operation["responses"] = new Dictionary<string, object>
                {
                    ["200"] = new Dictionary<string, object>
                    {
                        ["description"] = "Successful response",
                        ["content"] = new Dictionary<string, object>
                        {
                            ["application/json"] = new Dictionary<string, object>
                            {
                                ["schema"] = new Dictionary<string, object>
                                {
                                    ["type"] = "object"
                                }
                            }
                        }
                    }
                };
                
                pathItem[method] = operation;
            }
            
            paths[openApiPath] = pathItem;
        }
        
        return JsonSerializer.Serialize(swagger, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }
    
    private string ExtractTagFromPath(string path)
    {
        // Extract a tag from the path, e.g., /api/users -> "users"
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return parts[1]; // e.g., "users" from "/api/users"
        }
        return "default";
    }
    
    private bool IsBodyParameter(Route route, string paramName)
    {
        if (route.ParameterDecorators == null || route.ParameterDecorators.Count == 0)
        {
            // Default mode: "body" is the body parameter
            return paramName == "body";
        }
        
        // Decorator mode: check if parameter has @Body decorator
        var paramIndex = route.ParameterNames.IndexOf(paramName);
        if (paramIndex >= 0 && paramIndex < route.ParameterDecorators.Count)
        {
            var decorator = route.ParameterDecorators[paramIndex];
            return decorator?.Name == "Body";
        }
        
        return false;
    }

    private Dictionary<string, object> BuildOpenApiBodySchema(Route route)
    {
        var bodySchema = new Dictionary<string, object>
        {
            ["type"] = "object"
        };

        var section = GetValidationSection(route, "body");
        if (section == null)
        {
            return bodySchema;
        }

        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var field in section.GetProperties())
        {
            var fieldSchema = new Dictionary<string, object>
            {
                ["type"] = "string"
            };
            if (TryGetValidationRule(route, "body", field.Key, out var ruleSchema, out var isRequired))
            {
                MergeOpenApiSchema(fieldSchema, ruleSchema);
                if (isRequired)
                {
                    required.Add(field.Key);
                }
            }
            properties[field.Key] = fieldSchema;
        }

        if (properties.Count > 0)
        {
            bodySchema["properties"] = properties;
        }
        if (required.Count > 0)
        {
            bodySchema["required"] = required;
        }

        return bodySchema;
    }

    private bool HasRequiredBodyFields(Route route)
    {
        var section = GetValidationSection(route, "body");
        if (section == null)
        {
            return false;
        }

        foreach (var field in section.GetProperties())
        {
            if (TryGetValidationRule(route, "body", field.Key, out _, out var isRequired) && isRequired)
            {
                return true;
            }
        }

        return false;
    }

    private JsonObject? GetValidationSection(Route route, string sectionName)
    {
        var schema = NormalizeValidationSchema(route);
        if (schema == null)
        {
            return null;
        }

        var section = schema.Get(sectionName, null);
        if (section.Type == ValueType.Object && section.AsObject() is JsonObject sectionObj)
        {
            return sectionObj;
        }

        return null;
    }

    private JsonObject? NormalizeValidationSchema(Route route)
    {
        var schema = route.Metadata.ValidationSchema;
        if (schema.Type == ValueType.Null)
        {
            return null;
        }

        RuntimeValue normalizedSchema = schema;
        if (schema.Type == ValueType.String)
        {
            var raw = schema.AsString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);
                normalizedSchema = JsonToRuntimeValue(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }

        if (normalizedSchema.Type == ValueType.Object && normalizedSchema.AsObject() is JsonObject schemaObj)
        {
            return schemaObj;
        }

        return null;
    }

    private bool TryGetValidationRule(
        Route route,
        string sectionName,
        string field,
        out Dictionary<string, object> openApiSchema,
        out bool required)
    {
        openApiSchema = new Dictionary<string, object>();
        required = false;

        var section = GetValidationSection(route, sectionName);
        if (section == null)
        {
            return false;
        }

        var ruleValue = section.Get(field, null);
        if (ruleValue.Type == ValueType.Null)
        {
            return false;
        }

        if (ruleValue.Type == ValueType.String)
        {
            ParseValidationRuleDsl(ruleValue.AsString(), openApiSchema, ref required);
            return true;
        }

        if (ruleValue.Type == ValueType.Object && ruleValue.AsObject() is JsonObject ruleObj)
        {
            ParseValidationRuleObject(ruleObj, openApiSchema, ref required);
            return true;
        }

        return false;
    }

    private static void ParseValidationRuleDsl(string rule, Dictionary<string, object> schema, ref bool required)
    {
        var parts = rule.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (part.Equals("required", StringComparison.OrdinalIgnoreCase))
            {
                required = true;
                continue;
            }

            if (part.StartsWith("min=", StringComparison.OrdinalIgnoreCase) && double.TryParse(part["min=".Length..], out var min))
            {
                schema["minimum"] = min;
                continue;
            }

            if (part.StartsWith("max=", StringComparison.OrdinalIgnoreCase) && double.TryParse(part["max=".Length..], out var max))
            {
                schema["maximum"] = max;
                continue;
            }

            if (part.StartsWith("minLength=", StringComparison.OrdinalIgnoreCase) && int.TryParse(part["minLength=".Length..], out var minLength))
            {
                schema["minLength"] = minLength;
                continue;
            }

            if (part.StartsWith("maxLength=", StringComparison.OrdinalIgnoreCase) && int.TryParse(part["maxLength=".Length..], out var maxLength))
            {
                schema["maxLength"] = maxLength;
                continue;
            }

            if (part.StartsWith("pattern=", StringComparison.OrdinalIgnoreCase))
            {
                schema["pattern"] = part["pattern=".Length..];
                continue;
            }

            schema["type"] = MapValidationTypeToOpenApiType(part);
        }
    }

    private static void ParseValidationRuleObject(JsonObject ruleObj, Dictionary<string, object> schema, ref bool required)
    {
        var type = ruleObj.Get("type", null);
        if (type.Type == ValueType.String)
        {
            schema["type"] = MapValidationTypeToOpenApiType(type.AsString());
        }

        var requiredValue = ruleObj.Get("required", null);
        if (requiredValue.Type == ValueType.Boolean)
        {
            required = requiredValue.AsBoolean();
        }

        var min = ruleObj.Get("min", null);
        if (min.Type == ValueType.Integer)
        {
            schema["minimum"] = min.AsInteger();
        }
        else if (min.Type == ValueType.Float)
        {
            schema["minimum"] = min.AsFloat();
        }

        var max = ruleObj.Get("max", null);
        if (max.Type == ValueType.Integer)
        {
            schema["maximum"] = max.AsInteger();
        }
        else if (max.Type == ValueType.Float)
        {
            schema["maximum"] = max.AsFloat();
        }

        var minLength = ruleObj.Get("minLength", null);
        if (minLength.Type == ValueType.Integer)
        {
            schema["minLength"] = minLength.AsInteger();
        }

        var maxLength = ruleObj.Get("maxLength", null);
        if (maxLength.Type == ValueType.Integer)
        {
            schema["maxLength"] = maxLength.AsInteger();
        }

        var pattern = ruleObj.Get("pattern", null);
        if (pattern.Type == ValueType.String)
        {
            schema["pattern"] = pattern.AsString();
        }
    }

    private static string MapValidationTypeToOpenApiType(string validationType)
    {
        var type = validationType.Trim().ToLowerInvariant();
        return type switch
        {
            "int" => "integer",
            "integer" => "integer",
            "float" => "number",
            "double" => "number",
            "number" => "number",
            "bool" => "boolean",
            "boolean" => "boolean",
            "object" => "object",
            "array" => "array",
            _ => "string"
        };
    }

    private static void MergeOpenApiSchema(Dictionary<string, object> target, Dictionary<string, object> source)
    {
        foreach (var kvp in source)
        {
            target[kvp.Key] = kvp.Value;
        }
    }
    
    private void HandleError(HttpListenerResponse response, Exception ex, string correlationId)
    {
        var normalized = RuntimeDiagnostics.PreserveContext(ex, _interpreter);
        if (normalized is WebRuntimeException webRuntimeException && webRuntimeException.StatusCode < 500)
        {
            Console.Error.WriteLine(
                $"REST Server [{correlationId}] {webRuntimeException.StatusCode} {webRuntimeException.ErrorCode}: {webRuntimeException.Message}");
        }
        else
        {
            Console.Error.WriteLine($"REST Server Error [{correlationId}]");
            Console.Error.WriteLine(RuntimeDiagnostics.FormatExceptionForConsole(ex, _interpreter));
        }

        var payload = WebRuntimeHelpers.CreateErrorFromException(
            normalized,
            correlationId,
            out var statusCode,
            WebRuntimeHelpers.ShouldIncludeDebugDiagnostics());
        response.StatusCode = statusCode;
        WriteJsonResponse(response, payload);
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