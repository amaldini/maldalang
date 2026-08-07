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

public class CachedFile
{
    public byte[] Content { get; set; }
    public string ContentType { get; set; }
    public DateTime LastModified { get; set; }
    
    public CachedFile(byte[] content, string contentType, DateTime lastModified)
    {
        Content = content;
        ContentType = contentType;
        LastModified = lastModified;
    }
}

public class HttpServerInstance : ObjectInstance
{
    private static readonly List<HttpServerInstance> _instances = new();
    private static readonly object _instancesLock = new object();
    
    // Store pending transpiled routes that were registered before instances were created
    private static readonly List<PendingRoute> _pendingRoutes = new();
    private static readonly object _pendingRoutesLock = new object();
    
    // Store pending AIPAGE descriptions for transpiled code
    private static readonly Dictionary<string, string> _pendingAiPageDescriptions = new();
    private static readonly object _pendingAiPageDescriptionsLock = new object();
    
    private class PendingRoute
    {
        public string Method { get; }
        public string Path { get; }
        public string FunctionName { get; }
        public List<string> ParamNames { get; }
        public List<Decorator>? ParamDecorators { get; }
        public RouteMetadata Metadata { get; }
        
        public PendingRoute(
            string method,
            string path,
            string functionName,
            List<string> paramNames,
            List<Decorator>? paramDecorators,
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
    private string _webDirectory;
    private bool _isRunning = false;
    
    public int Port => _port;
    public string WebDirectory => _webDirectory;
    public bool IsRunning => _isRunning;
    private Thread? _serverThread;
    private RouteRegistry _routeRegistry;
    private Interpreter? _interpreter;
    private Dictionary<string, CachedFile> _staticFileCache = new();
    private readonly object _cacheLock = new object();
    
    // Store AI-generated HTML cache and route metadata
    private Dictionary<string, string> _aiGeneratedCache = new(); // path -> generated HTML
    private Dictionary<string, string> _aiPageDescriptions = new(); // path -> description
    private readonly object _aiCacheLock = new object();
    
    // Store setHTML content for root path
    private string? _setHtmlContent = null;
    
    private sealed class SseConnection
    {
        public HttpListenerResponse Response { get; }
        public HashSet<string> Channels { get; }

        public SseConnection(HttpListenerResponse response, HashSet<string> channels)
        {
            Response = response;
            Channels = channels;
        }
    }

    // Store active SSE connections (connection ID -> connection metadata)
    private static readonly Dictionary<string, SseConnection> _sseConnections = new();
    private static readonly object _sseConnectionsLock = new object();
    private static int _sseConnectionCounter = 0;

    private sealed class ComponentStateEntry
    {
        public Dictionary<string, RuntimeValue> Values { get; }
        public DateTime LastAccessUtc { get; set; }

        public ComponentStateEntry()
        {
            Values = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
            LastAccessUtc = DateTime.UtcNow;
        }
    }

    // Phase B guardrails for component state safety.
    // Defaults are intentionally conservative and additive.
    private static int _componentStateMaxComponents = 512;
    private static int _componentStateMaxKeysPerComponent = 128;
    private static TimeSpan _componentStateTtl = TimeSpan.FromMinutes(30);

    // Phase A/Phase B component state store: componentId -> state entry
    private static readonly Dictionary<string, ComponentStateEntry> _componentStateStore = new();
    private static readonly object _componentStateLock = new object();

    // Global middleware pipeline (use(req, res, next)).
    private readonly WebMiddlewareChain _middlewareChain = new();
    private RateLimiter? _rateLimiter;
    private string _rateLimitKeyStrategy = "ipOrToken";
    private bool _csrfEnabled;
    private string _csrfSecret = string.Empty;
    private string _csrfCookieName = WebRuntimeHelpers.DefaultCsrfCookieName;
    private string _csrfHeaderName = WebRuntimeHelpers.DefaultCsrfHeaderName;
    private bool _trustProxy;
    private string _trustedProxyHeader = "X-Forwarded-For";
    private int _trustedProxyHopIndex;
    private bool _rateLimitHeadersEnabled;
    private bool _rateLimitRemainingEnabled = true;
    private int _rateLimitLimit = 60;
    private int _rateLimitWindowSeconds = 60;
    private readonly string _pathBase;
    
    public HttpServerInstance(int port, string? webDirectory = null, Interpreter? interpreter = null, string? pathBase = null) : base(null)
    {
        _port = port;
        _interpreter = interpreter; // Allow null for transpiled code
        _routeRegistry = new RouteRegistry();
        
        // Path base for reverse-proxy deployments (e.g. /schoolprep). Read from MALDA_PATH_BASE env if not passed.
        _pathBase = NormalizePathBase(pathBase ?? System.Environment.GetEnvironmentVariable("MALDA_PATH_BASE") ?? string.Empty);
        
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
        
        // Register this instance for transpiled code route registration
        lock (_instancesLock)
        {
            _instances.Add(this);
        }
        
        // Resolve web directory
        if (string.IsNullOrEmpty(webDirectory))
        {
            var webDirectoryFromEnv = System.Environment.GetEnvironmentVariable("MALDA_WEB_DIRECTORY");
            _webDirectory = string.IsNullOrWhiteSpace(webDirectoryFromEnv)
                ? Path.Combine(Directory.GetCurrentDirectory(), "web")
                : Path.GetFullPath(webDirectoryFromEnv);
        }
        else
        {
            _webDirectory = Path.IsPathRooted(webDirectory) 
                ? webDirectory 
                : Path.GetFullPath(webDirectory);
        }
    }

    private static string NormalizePathBase(string value)
    {
        var s = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (!s.StartsWith("/", StringComparison.Ordinal)) s = "/" + s;
        if (s.Length > 1 && s.EndsWith("/", StringComparison.Ordinal)) s = s[..^1];
        return s;
    }

    // Transpiled MALDA top-level vars are object-typed; allow constructor coercion.
    public HttpServerInstance(object? port, string? webDirectory = null, Interpreter? interpreter = null, string? pathBase = null)
        : this(CoercePort(port), webDirectory, interpreter, pathBase)
    {
    }

    // Overload for transpiled code where pathBase may come from getEnv() (RuntimeValue).
    public HttpServerInstance(object? port, object? webDirectory, Interpreter? interpreter, object? pathBase)
        : this(CoercePort(port), CoerceWebDirectory(webDirectory), interpreter, CoercePathBase(pathBase))
    {
    }

    private static string? CoerceWebDirectory(object? value)
    {
        if (value == null) return null;
        if (value is RuntimeValue rv)
            return rv.Type == ValueType.Null ? null : rv.AsString();
        return value.ToString();
    }

    private static string? CoercePathBase(object? value)
    {
        if (value == null) return null;
        if (value is RuntimeValue rv)
        {
            if (rv.Type == ValueType.Null) return null;
            var s = rv.AsString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        var str = value.ToString();
        return string.IsNullOrWhiteSpace(str) ? null : str.Trim();
    }

    private static int CoercePort(object? value)
    {
        if (value is RuntimeValue runtimeValue)
        {
            return runtimeValue.Type switch
            {
                ValueType.Integer => runtimeValue.AsInteger(),
                ValueType.Float => (int)runtimeValue.AsFloat(),
                ValueType.String when int.TryParse(runtimeValue.AsString(), out var parsed) => parsed,
                _ => 8080
            };
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 8080
        };
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "port")
            return RuntimeValue.Integer(_port);
        if (name == "isRunning")
            return RuntimeValue.Boolean(_isRunning);
        if (name == "webDirectory")
            return RuntimeValue.String(_webDirectory);
        if (name == "pathBase")
            return RuntimeValue.String(_pathBase);
        
        // Handle method access
        if (name == "start" || name == "stop" || name == "clearCache" || name == "getRoutes" || name == "setHTML" || name == "broadcastSSE" || name == "use" ||
            name == "setRateLimit" || name == "disableRateLimit" || name == "enableCsrf" || name == "disableCsrf" ||
            name == "configureTrustedProxy" || name == "setRateLimitHeaders")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on HttpServer.");
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
            
            case "clearCache":
                if (args.Count != 0)
                    throw new Exception("clearCache() expects 0 arguments");
                ClearCache();
                return RuntimeValue.Null();
            
            case "getRoutes":
                if (args.Count != 0)
                    throw new Exception("getRoutes() expects 0 arguments");
                return GetRoutes();
            
            case "setHTML":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setHTML() expects 1 string argument");
                SetHTML(args[0].AsString());
                return RuntimeValue.Null();
            
            case "broadcastSSE":
                if ((args.Count != 1 && args.Count != 2) || args[0].Type != ValueType.String || (args.Count == 2 && args[1].Type != ValueType.String))
                    throw new Exception("broadcastSSE() expects data string and optional channel string");
                var channel = args.Count == 2 ? args[1].AsString() : null;
                BroadcastSSEMessage(args[0].AsString(), channel);
                return RuntimeValue.Null();

            case "use":
                if (args.Count != 1 || (args[0].Type != ValueType.Function && args[0].Type != ValueType.String))
                    throw new Exception("use() expects 1 function or function-name string argument");
                RegisterMiddleware(args[0]);
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

    private void RegisterMiddleware(RuntimeValue middlewareValue)
    {
        if (middlewareValue.Type == ValueType.Function)
        {
            _middlewareChain.Add(middlewareValue.AsFunction());
            return;
        }

        _middlewareChain.Add(middlewareValue.AsString());
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
    
    public void Start()
    {
        if (_isRunning)
            throw new Exception("HttpServer is already running");
        
        try
        {
            // Scan for @PAGE decorated functions
            ScanForRoutes();
            
            // Validate route conflicts
            _routeRegistry.ValidateRouteConflicts();
            
            // Print registered routes
            var routesSummary = _routeRegistry.GetRoutesSummary();
            Console.WriteLine(routesSummary);
            
            // Load static files into cache
            LoadStaticFiles();
            
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _isRunning = true;
            
            // Use Task.Run to start async request handling
            _ = Task.Run(async () => await HandleRequestsAsync());
        }
        catch (Exception ex)
        {
            _isRunning = false;
            throw new Exception($"Failed to start HttpServer: {ex.Message}");
        }
    }
    
    public void Stop()
    {
        if (!_isRunning)
            return;
        
        _isRunning = false;
        _listener?.Stop();
        _listener?.Close();
        _listener = null;
    }
    
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _staticFileCache.Clear();
        }
        // Reload static files
        LoadStaticFiles();
    }
    
    public RuntimeValue GetRoutes()
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
    
    private void LoadStaticFiles()
    {
        lock (_cacheLock)
        {
            _staticFileCache.Clear();
            
            if (!Directory.Exists(_webDirectory))
            {
                // Web directory doesn't exist - that's okay, just no static files
                return;
            }
            
            // Recursively load all files from web directory
            LoadDirectory(_webDirectory, _webDirectory);
        }
    }
    
    private void LoadDirectory(string directory, string baseDirectory)
    {
        try
        {
            // Load files in current directory
            foreach (var file in Directory.GetFiles(directory))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var relativePath = Path.GetRelativePath(baseDirectory, file).Replace('\\', '/');
                    
                    // Normalize path to start with /
                    if (!relativePath.StartsWith("/"))
                        relativePath = "/" + relativePath;
                    
                    var content = File.ReadAllBytes(file);
                    var contentType = GetContentType(file);
                    
                    _staticFileCache[relativePath] = new CachedFile(content, contentType, fileInfo.LastWriteTime);
                }
                catch
                {
                    // Skip files that can't be read
                    continue;
                }
            }
            
            // Recursively load subdirectories
            foreach (var subdir in Directory.GetDirectories(directory))
            {
                LoadDirectory(subdir, baseDirectory);
            }
        }
        catch
        {
            // Skip directories that can't be accessed
        }
    }
    
    private string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".html" => "text/html; charset=utf-8",
            ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".wasm" => "application/wasm",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".webp" => "image/webp",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".txt" => "text/plain; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            _ => "application/octet-stream"
        };
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
                _ = Task.Run(async () => await ProcessRequestAsync(context));
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
            catch (Exception)
            {
                // Ignore other errors and continue
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
            
            // Parse request body if present
            RuntimeValue? requestBody = null;
            if (method == "POST" || method == "PUT" || method == "PATCH")
            {
                requestBody = await ParseRequestBodyAsync(request);
            }

            var requestContext = CreateRequestContext(request, path, new Dictionary<string, string>(), queryParams, requestBody, correlationId);
            var responseContext = new ResponseContextInstance();

            if (!ValidateRateLimit(requestContext, response, request, correlationId))
            {
                return;
            }

            if (!ValidateCsrf(requestContext, responseContext, requestBody, request, response, correlationId))
            {
                return;
            }

            var continuePipeline = await ExecuteMiddlewareChainAsync(requestContext, responseContext);
            if (!continuePipeline)
            {
                if (responseContext.IsCommitted || responseContext.HasStatusOverride)
                {
                    responseContext.ApplyTo(response, _pathBase);
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
            
            // Only check static files for GET requests
            if (method == "GET")
            {
                if (TryServeStaticFile(path, response))
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
            HandleError(response, ex, correlationId, request);
        }
        finally
        {
            // Only close if not SSE (SSE connections are kept open)
            // SSE connections will be closed when client disconnects or explicitly closed
            if (!isSSERequest)
            {
                response.Close();
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
                    responseContext.ApplyTo(response, _pathBase);
                }
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

            if (responseContext.IsCommitted || responseContext.HasStatusOverride)
            {
                responseContext.ApplyTo(response, _pathBase);
                return;
            }

            if (responseContext.HasHeaders)
            {
                responseContext.ApplyHeadersTo(response, _pathBase);
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
                        // This is an SSE stream - set headers and keep connection open
                        response.StatusCode = 200;
                        response.ContentType = "text/event-stream";
                        response.Headers.Add("Cache-Control", "no-cache");
                        response.Headers.Add("Connection", "keep-alive");
                        
                        // Generate connection ID and store response with optional channel subscriptions.
                        var subscribedChannels = ParseSseChannels(queryParams);
                        var connectionId = $"sse_{Interlocked.Increment(ref _sseConnectionCounter)}_{DateTime.UtcNow.Ticks}";
                        lock (_sseConnectionsLock)
                        {
                            _sseConnections[connectionId] = new SseConnection(response, subscribedChannels);
                        }
                        
                        // Send initial connection message
                        var channels = subscribedChannels.Count == 0
                            ? "[]"
                            : "[" + string.Join(",", subscribedChannels.Select(c => JsonSerializer.Serialize(c))) + "]";
                        var initMessage = $"data: {{\"type\":\"connected\",\"connectionId\":\"{connectionId}\",\"channels\":{channels}}}\n\n";
                        var initBytes = Encoding.UTF8.GetBytes(initMessage);
                        response.OutputStream.Write(initBytes, 0, initBytes.Length);
                        response.OutputStream.Flush();
                        
                        // Don't close the connection - it will be closed when the client disconnects or explicitly closed
                        // The connection will be cleaned up when the client disconnects
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                while (response.OutputStream.CanWrite)
                                {
                                    await Task.Delay(30000); // 30 second heartbeat
                                    lock (_sseConnectionsLock)
                                    {
                                        if (!_sseConnections.ContainsKey(connectionId))
                                            break;
                                        try
                                        {
                                            var heartbeat = "data: {\"type\":\"heartbeat\"}\n\n";
                                            var heartbeatBytes = Encoding.UTF8.GetBytes(heartbeat);
                                            response.OutputStream.Write(heartbeatBytes, 0, heartbeatBytes.Length);
                                            response.OutputStream.Flush();
                                        }
                                        catch
                                        {
                                            _sseConnections.Remove(connectionId);
                                            break;
                                        }
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
            SerializeHtmlResponse(response, result, request, request.Url?.AbsolutePath ?? route.PathPattern);
        }
        catch (Exception ex)
        {
            HandleError(response, ex, correlationId, request);
        }
        finally
        {
            // Only close if not SSE
            if (!isSSERequest)
            {
                response.Close();
            }
        }
    }
    
    /// <summary>
    /// Writes an SSE message to a specific connection. Used by built-in functions.
    /// </summary>
    public static void WriteSSEMessage(string connectionId, string data)
    {
        lock (_sseConnectionsLock)
        {
            if (_sseConnections.TryGetValue(connectionId, out var connection))
            {
                try
                {
                    var message = $"data: {data}\n\n";
                    var bytes = Encoding.UTF8.GetBytes(message);
                    connection.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    connection.Response.OutputStream.Flush();
                }
                catch
                {
                    // Connection closed, remove it
                    _sseConnections.Remove(connectionId);
                }
            }
        }
    }
    
    /// <summary>
    /// Broadcasts an SSE message to all connected clients.
    /// </summary>
    public static void BroadcastSSEMessage(string data)
    {
        BroadcastSSEMessage(data, null);
    }

    /// <summary>
    /// Broadcasts an SSE message to all connected clients, optionally filtered by channel.
    /// </summary>
    public static void BroadcastSSEMessage(string data, string? channel)
    {
        lock (_sseConnectionsLock)
        {
            var toRemove = new List<string>();
            foreach (var kvp in _sseConnections)
            {
                if (!ConnectionMatchesChannel(kvp.Value, channel))
                {
                    continue;
                }

                try
                {
                    var message = $"data: {data}\n\n";
                    var bytes = Encoding.UTF8.GetBytes(message);
                    kvp.Value.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    kvp.Value.Response.OutputStream.Flush();
                }
                catch
                {
                    // Connection closed, mark for removal
                    toRemove.Add(kvp.Key);
                }
            }
            
            // Remove closed connections
            foreach (var id in toRemove)
            {
                _sseConnections.Remove(id);
            }
        }
    }

    private static bool ConnectionMatchesChannel(SseConnection connection, string? channel)
    {
        // Empty channel filter means broadcast to all.
        if (string.IsNullOrWhiteSpace(channel))
        {
            return true;
        }

        // Empty subscription list means wildcard subscription.
        if (connection.Channels.Count == 0)
        {
            return true;
        }

        return connection.Channels.Contains(channel);
    }

    private static HashSet<string> ParseSseChannels(Dictionary<string, string> queryParams)
    {
        var channels = new HashSet<string>(StringComparer.Ordinal);
        if (!queryParams.TryGetValue("channel", out var channelValue) || string.IsNullOrWhiteSpace(channelValue))
        {
            return channels;
        }

        var values = channelValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                channels.Add(value);
            }
        }

        return channels;
    }
    
    private async Task<RuntimeValue> CallTranspiledRouteFunctionAsync(Route route, List<RuntimeValue> functionArgs)
    {
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
        return ConvertTranspiledResultToRuntimeValue(result);
    }

    private RuntimeValue ConvertTranspiledResultToRuntimeValue(object? value)
    {
        if (value == null)
        {
            return RuntimeValue.Null();
        }

        if (value is RuntimeValue runtimeValue)
        {
            return runtimeValue;
        }

        if (value is DBNull)
        {
            return RuntimeValue.Null();
        }

        if (value is int i) return RuntimeValue.Integer(i);
        if (value is long l) return RuntimeValue.Integer((int)l);
        if (value is short sh) return RuntimeValue.Integer(sh);
        if (value is byte bt) return RuntimeValue.Integer(bt);
        if (value is double d) return RuntimeValue.Float(d);
        if (value is float f) return RuntimeValue.Float(f);
        if (value is decimal dm) return RuntimeValue.Float((double)dm);
        if (value is string s) return RuntimeValue.String(s);
        if (value is bool b) return RuntimeValue.Boolean(b);
        if (value is MaldaLang.Interpreter.ObjectInstance oi) return RuntimeValue.Object(oi);

        if (value is Dictionary<string, object?> nullableDict)
        {
            return RuntimeValue.Object(ToJsonObject(nullableDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }

        if (value is Dictionary<string, object> dict)
        {
            return RuntimeValue.Object(ToJsonObject(dict.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)));
        }

        if (value is List<object> list)
        {
            return RuntimeValue.Array(list.Select(ConvertTranspiledResultToRuntimeValue).ToList());
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            var jsonObj = new JsonObject();
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                var key = entry.Key == null ? "" : entry.Key.ToString() ?? "";
                jsonObj.Set(key, ConvertTranspiledResultToRuntimeValue(entry.Value));
            }
            return RuntimeValue.Object(jsonObj);
        }

        if (value is System.Collections.IEnumerable sequence && value is not string)
        {
            var items = new List<RuntimeValue>();
            foreach (var item in sequence)
            {
                items.Add(ConvertTranspiledResultToRuntimeValue(item));
            }
            return RuntimeValue.Array(items);
        }

        return RuntimeValue.Null();
    }

    private JsonObject ToJsonObject(Dictionary<string, object?> source)
    {
        var jsonObj = new JsonObject();
        foreach (var kvp in source)
        {
            jsonObj.Set(kvp.Key, ConvertTranspiledResultToRuntimeValue(kvp.Value));
        }
        return jsonObj;
    }
    
    private List<RuntimeValue> BindParameters(Route route, Dictionary<string, string> pathParams, 
        Dictionary<string, string> queryParams, RuntimeValue? requestBody,
        RequestContextInstance requestContext, ResponseContextInstance responseContext)
    {
        var args = new List<RuntimeValue>();
        var paramNames = route.ParameterNames;
        
        // Simple binding: path parameters by name, then query params, then body
        foreach (var paramName in paramNames)
        {
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

            if (route.PathParameterNames.Contains(paramName))
            {
                // Path parameter
                var value = pathParams.ContainsKey(paramName) 
                    ? RuntimeValue.String(pathParams[paramName]) 
                    : RuntimeValue.Null();
                args.Add(value);
            }
            else if (paramName == "queryParams" || paramName == "query")
            {
                // Special case: parameter named "queryParams" or "query" receives entire query params as object
                var jsonObj = new JsonObject();
                foreach (var kvp in queryParams)
                {
                    jsonObj.Set(kvp.Key, RuntimeValue.String(kvp.Value));
                }
                args.Add(RuntimeValue.Object(jsonObj));
            }
            else if (queryParams.ContainsKey(paramName))
            {
                // Query parameter
                args.Add(RuntimeValue.String(queryParams[paramName]));
            }
            else if (paramName == "body" && requestBody != null)
            {
                // Request body parameter (by convention, parameter named "body")
                args.Add(requestBody);
            }
            else
            {
                // No value provided
                args.Add(RuntimeValue.Null());
            }
        }
        
        return args;
    }

    private RequestContextInstance CreateRequestContext(
        HttpListenerRequest request,
        string path,
        Dictionary<string, string> pathParams,
        Dictionary<string, string> queryParams,
        RuntimeValue? requestBody,
        string correlationId)
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
            path,
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
            pathBase: _pathBase);
    }

    private bool ValidateRateLimit(
        RequestContextInstance requestContext,
        HttpListenerResponse response,
        HttpListenerRequest request,
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
        WriteJsonRuntimeValue(response, payload, request);
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

    private void WriteJsonRuntimeValue(HttpListenerResponse response, RuntimeValue payload, HttpListenerRequest? request = null)
    {
        var body = RuntimeValueToJson(payload);
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        WriteResponseBodyIfAllowed(response, bytes, request);
    }

    private string ResolveRateLimitKey(RequestContextInstance requestContext)
    {
        var headers = (JsonObject)requestContext.Get("headers", null).AsObject();
        var ip = requestContext.Get("ip", null).AsString();
        var authHeader = headers.Get("Authorization", null);
        var token = authHeader.Type == ValueType.String ? authHeader.AsString() : string.Empty;
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
                WriteJsonRuntimeValue(response, payload, request);

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
    
    private void SerializeHtmlResponse(HttpListenerResponse response, RuntimeValue result, HttpListenerRequest request, string path)
    {
        if (result.Type == ValueType.Object && result.AsObject() is ResponseContextInstance responseContext)
        {
            responseContext.ApplyTo(response, _pathBase);
            return;
        }

        if (WebRuntimeHelpers.TryGetStandardErrorPayload(result, out _, out var errorStatusCode))
        {
            WriteErrorResult(response, result, errorStatusCode, request, path);
            return;
        }

        // Check if result has a status property (status+data return).
        // Accept JsonObject and DictionaryInstance (transpile object literals).
        if (result.Type == ValueType.Object)
        {
            var obj = result.AsObject();
            if (obj is JsonObject or DictionaryInstance)
            {
                try
                {
                    var statusValue = obj.Get("status", null);
                    if (statusValue != null && statusValue.Type == ValueType.Integer)
                    {
                        var statusCode = statusValue.AsInteger();
                        response.StatusCode = statusCode;
                        
                        // Check for custom headers
                        var headersValue = obj.Get("headers", null);
                        if (headersValue != null && headersValue.Type == ValueType.Object)
                        {
                            var headersObj = headersValue.AsObject();
                            foreach (var key in headersObj.GetAllKeys())
                            {
                                var headerVal = headersObj.Get(key, null);
                                if (headerVal.Type == ValueType.String)
                                {
                                    if (key.Equals("Location", StringComparison.OrdinalIgnoreCase))
                                    {
                                        WebRuntimeHelpers.ApplyRedirectLocation(response, headerVal.AsString(), _pathBase);
                                    }
                                    else
                                    {
                                        response.Headers.Add(key, headerVal.AsString());
                                    }
                                }
                            }
                        }

                        // RedirectLocation can reset HttpListenerResponse to 302.
                        // Re-apply the explicit status so POST actions can return 303 See Other.
                        response.StatusCode = statusCode;
                        
                        // For redirects (3xx), send minimal body
                        if (statusCode >= 300 && statusCode < 400)
                        {
                            var bodyValue = obj.Get("body", null);
                            if (bodyValue != null && bodyValue.Type == ValueType.String && !string.IsNullOrEmpty(bodyValue.AsString()))
                            {
                                WriteHtmlResponse(response, bodyValue.AsString(), request);
                            }
                            else
                            {
                                var location = response.RedirectLocation ?? string.Empty;
                                WriteHtmlResponse(response, WebRuntimeHelpers.BuildRedirectHtml(location), request);
                            }
                            return;
                        }
                        
                        // Get body for non-redirect responses
                        var body = obj.Get("body", null);
                        if (body != null && body.Type == ValueType.String)
                        {
                            WriteHtmlResponse(response, body.AsString(), request);
                            return;
                        }
                    }
                }
                catch
                {
                    // Not a status object, continue with normal serialization
                }
            }
        }
        
        // Simple return - treat as HTML string
        if (result.Type == ValueType.String)
        {
            response.StatusCode = 200;
            WriteHtmlResponse(response, result.AsString(), request);
        }
        else
        {
            // For non-string results, convert to JSON and wrap in HTML
            response.StatusCode = 200;
            var json = RuntimeValueToJson(result);
            var escapedJson = System.Net.WebUtility.HtmlEncode(json);
            var html = $"<html><head><title>Data</title></head><body><pre>{escapedJson}</pre></body></html>";
            WriteHtmlResponse(response, html, request);
        }
    }
    
    private void WriteHtmlResponse(HttpListenerResponse response, string html, HttpListenerRequest? request = null)
    {
        // Full documents get the form AJAX helper so @ACTION + componentFragment
        // can patch a target without a classic navigation to the action URL.
        // Fragment bodies (no doctype/html shell) are left untouched.
        if (LooksLikeFullHtmlDocument(html))
        {
            html = InjectAjaxHelper(html);
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        WriteResponseBodyIfAllowed(response, bytes, request);
    }

    private static bool LooksLikeFullHtmlDocument(string html)
    {
        if (string.IsNullOrEmpty(html))
            return false;

        // Fast path: fragment payloads from componentFragment() are inner HTML only.
        return html.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
               || html.Contains("<html", StringComparison.OrdinalIgnoreCase);
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
        if (obj is DictionaryInstance dictObj)
        {
            var jsonProps = new List<string>();
            foreach (var kvp in dictObj.Entries)
            {
                var key = JsonSerializer.Serialize(kvp.Key);
                var val = RuntimeValueToJson(kvp.Value);
                jsonProps.Add($"{key}:{val}");
            }
            return "{" + string.Join(",", jsonProps) + "}";
        }
        var keys = obj.GetAllKeys();
        var fallbackProps = new List<string>();
        foreach (var keyName in keys)
        {
            var key = JsonSerializer.Serialize(keyName);
            var val = RuntimeValueToJson(obj.Get(keyName, null));
            fallbackProps.Add($"{key}:{val}");
        }
        if (fallbackProps.Count > 0)
        {
            return "{" + string.Join(",", fallbackProps) + "}";
        }
        return "{}";
    }
    
    private string RuntimeArrayToJson(List<RuntimeValue> array)
    {
        var items = array.Select(RuntimeValueToJson).ToList();
        return "[" + string.Join(",", items) + "]";
    }
    
    private bool TryServeStaticFile(string path, HttpListenerResponse response)
    {
        // Normalize path
        if (path == "/")
        {
            path = "/index.html";
        }
        
        lock (_cacheLock)
        {
            if (_staticFileCache.TryGetValue(path, out var cachedFile))
            {
                response.ContentType = cachedFile.ContentType;
                response.ContentLength64 = cachedFile.Content.Length;
                response.StatusCode = 200;
                response.OutputStream.Write(cachedFile.Content, 0, cachedFile.Content.Length);
                return true;
            }
        }
        
        return false;
    }
    
    private void WriteErrorResponse(HttpListenerResponse response, int statusCode, string message, HttpListenerRequest? request = null)
    {
        var escapedMessage = System.Net.WebUtility.HtmlEncode(message);
        var html = $"<html><head><title>{statusCode}</title></head><body><h1>{statusCode}</h1><p>{escapedMessage}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.StatusCode = statusCode;
        WriteResponseBodyIfAllowed(response, bytes, request);
    }

    private void WriteErrorResult(
        HttpListenerResponse response,
        RuntimeValue payload,
        int statusCode,
        HttpListenerRequest request,
        string path)
    {
        if (ShouldWriteJsonError(request, path, statusCode))
        {
            response.StatusCode = statusCode;
            WriteJsonRuntimeValue(response, payload, request);
            return;
        }

        response.StatusCode = statusCode;
        var message = statusCode >= 500
            ? "Internal server error"
            : WebRuntimeHelpers.TryGetErrorMessage(payload, out var extractedMessage)
                ? extractedMessage
                : "Request failed.";
        WriteErrorResponse(response, statusCode, message, request);
    }

    private static void WriteResponseBodyIfAllowed(HttpListenerResponse response, byte[] bytes, HttpListenerRequest? request = null)
    {
        if (request != null && string.Equals(request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        response.OutputStream.Write(bytes, 0, bytes.Length);
    }
    
    private void HandleError(HttpListenerResponse response, Exception ex, string correlationId, HttpListenerRequest request)
    {
        // Log error
        var normalized = RuntimeDiagnostics.PreserveContext(ex, _interpreter);
        Console.Error.WriteLine($"HttpServer Error [{correlationId}]");
        Console.Error.WriteLine(RuntimeDiagnostics.FormatForConsole(normalized, _interpreter));
        if (ex.StackTrace != null)
            Console.Error.WriteLine(ex.StackTrace);

        var path = request.Url?.AbsolutePath ?? "/";
        var payload = WebRuntimeHelpers.CreateErrorFromException(
            normalized,
            correlationId,
            out var statusCode,
            WebRuntimeHelpers.ShouldIncludeDebugDiagnostics());
        WriteErrorResult(response, payload, statusCode, request, path);
    }

    private static bool ShouldWriteJsonError(HttpListenerRequest request, string path, int statusCode)
    {
        return WantsJsonResponse(request, path) || statusCode == 401 || statusCode == 403 || statusCode == 429;
    }

    private static bool WantsJsonResponse(HttpListenerRequest request, string path)
    {
        var accept = request.Headers["Accept"] ?? string.Empty;
        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
               accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }
    
    private void SetHTML(string html)
    {
        _setHtmlContent = html;
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
                // If JSON parsing fails, try to parse as form-urlencoded as fallback
                // This handles cases where Content-Type is set incorrectly
                if (bodyText.Contains('&') && bodyText.Contains('='))
                {
                    return RuntimeValue.Object(ParseUrlEncodedBody(bodyText));
                }
                return RuntimeValue.String(bodyText);
            }
        }
        else if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = ParseMultipartFormData(bodyText, contentType);
            if (parsed != null)
            {
                return RuntimeValue.Object(parsed);
            }
            return RuntimeValue.String(bodyText);
        }
        else if (contentType.Contains("application/x-www-form-urlencoded") || 
                 (string.IsNullOrEmpty(contentType) && bodyText.Contains('&') && bodyText.Contains('=')))
        {
            // Handle form-urlencoded data, or data that looks like form-urlencoded even without Content-Type
            return RuntimeValue.Object(ParseUrlEncodedBody(bodyText));
        }
        
        // Default: return as string if we can't parse it
        return RuntimeValue.String(bodyText);
    }

    private static JsonObject ParseUrlEncodedBody(string bodyText)
    {
        var jsonObj = new JsonObject();
        var pairs = bodyText.Split('&');
        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair))
            {
                continue;
            }

            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = DecodeFormValue(parts[0]);
            var value = DecodeFormValue(parts[1]);
            jsonObj.Set(key, RuntimeValue.String(value));
        }

        return jsonObj;
    }

    private static string DecodeFormValue(string value)
    {
        // x-www-form-urlencoded uses '+' for spaces.
        return Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
    }

    private static JsonObject? ParseMultipartFormData(string bodyText, string contentType)
    {
        // Minimal parser for common browser multipart form submissions (fields first).
        // This intentionally focuses on key/value form fields and keeps files as metadata+text.
        var boundaryMatch = Regex.Match(contentType, "boundary=(?:\"(?<b>[^\"]+)\"|(?<b>[^;]+))", RegexOptions.IgnoreCase);
        if (!boundaryMatch.Success)
        {
            return null;
        }

        var boundary = boundaryMatch.Groups["b"].Value;
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return null;
        }

        var marker = "--" + boundary;
        var sections = bodyText.Split(marker, StringSplitOptions.RemoveEmptyEntries);
        var result = new JsonObject();

        foreach (var rawSection in sections)
        {
            var section = rawSection.Trim();
            if (section == "--")
            {
                continue;
            }

            // Strip final boundary marker suffix.
            if (section.EndsWith("--", StringComparison.Ordinal))
            {
                section = section.Substring(0, section.Length - 2).Trim();
            }

            var splitIndex = section.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var separatorLength = 4;
            if (splitIndex < 0)
            {
                splitIndex = section.IndexOf("\n\n", StringComparison.Ordinal);
                separatorLength = 2;
            }

            if (splitIndex < 0)
            {
                continue;
            }

            var headers = section.Substring(0, splitIndex);
            var content = section.Substring(splitIndex + separatorLength).TrimEnd('\r', '\n');
            var disposition = headers
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(h => h.StartsWith("Content-Disposition", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(disposition))
            {
                continue;
            }

            var nameMatch = Regex.Match(disposition, "name=(?:\"(?<n>[^\"]+)\"|(?<n>[^;\\r\\n]+))", RegexOptions.IgnoreCase);
            if (!nameMatch.Success)
            {
                continue;
            }

            var fieldName = nameMatch.Groups["n"].Value.Trim();
            var fileNameMatch = Regex.Match(disposition, "filename=(?:\"(?<f>[^\"]*)\"|(?<f>[^;\\r\\n]+))", RegexOptions.IgnoreCase);
            if (fileNameMatch.Success)
            {
                var fileObj = new JsonObject();
                fileObj.Set("fileName", RuntimeValue.String(fileNameMatch.Groups["f"].Value));
                fileObj.Set("content", RuntimeValue.String(content));
                result.Set(fieldName, RuntimeValue.Object(fileObj));
            }
            else
            {
                result.Set(fieldName, RuntimeValue.String(content));
            }
        }

        return result;
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
    
    private string GenerateAIPage(string description)
    {
        if (_interpreter == null)
        {
            return "<html><body><h1>Error</h1><p>AI page generation requires an interpreter instance.</p></body></html>";
        }
        
        try
        {
            // Try to find Agent in globals; otherwise create with default local LLM (auto-download from Hugging Face)
            AgentInstance? agent = null;
            var defaultLlama = DefaultLocalLlm.GetDefaultLocalClient();
            
            // Create agent if not found
            agent = new AgentInstance();
            agent.Initialize(
                "UIGenerator",
                "UI designer",
                "You create beautiful, functional HTML forms with modern CSS. Always include proper form submission using AJAX/fetch API. Use modern CSS styling. Include the AJAX helper script automatically.",
                null,
                defaultLlama,
                null,
                null
            );
            
            // Generate HTML using agent
            var response = agent.Think(RuntimeValue.String($"Create an HTML form: {description}"));
            
            // Extract HTML from response
            string html;
            if (response.Type == ValueType.Object)
            {
                var responseObj = response.AsObject();
                if (responseObj is JsonObject jsonObj)
                {
                    var contentValue = jsonObj.Get("content", null);
                    if (contentValue != null && contentValue.Type == ValueType.String)
                    {
                        html = contentValue.AsString();
                    }
                    else
                    {
                        html = response.ToString();
                    }
                }
                else
                {
                    html = response.ToString();
                }
            }
            else if (response.Type == ValueType.String)
            {
                html = response.AsString();
            }
            else
            {
                html = response.ToString();
            }
            
            // Extract HTML from markdown code blocks if present
            html = ExtractHTML(html);
            
            // Inject AJAX helper script
            html = InjectAjaxHelper(html);
            
            return html;
        }
        catch (Exception ex)
        {
            return $"<html><body><h1>Error Generating Page</h1><p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p></body></html>";
        }
    }
    
    private string ExtractHTML(string markdown)
    {
        // Extract HTML from markdown code blocks
        var htmlPattern = @"```html\s*(.*?)\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(
            markdown, 
            htmlPattern, 
            System.Text.RegularExpressions.RegexOptions.Singleline | 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        if (match.Success)
            return match.Groups[1].Value.Trim();
        
        // If no code block, check if it's already HTML
        if (markdown.Contains("<html") || markdown.Contains("<!DOCTYPE"))
            return markdown;
        
        // Return as-is if no HTML found
        return markdown;
    }
    
    private string InjectAjaxHelper(string html)
    {
        // Check if AJAX helper is already injected
        if (html.Contains("id=\"spl-ajax-helper\""))
            return html;
        
        // AJAX helper script
        var ajaxScript = @"
<script id=""spl-ajax-helper"">
(function() {
    'use strict';
    // MALDA AJAX Helper - automatically intercepts form submissions
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAjaxHelper);
    } else {
        initAjaxHelper();
    }
    
    function initAjaxHelper() {
        document.querySelectorAll('form').forEach(function(form) {
            if (form.dataset.splAjaxBound === '1') {
                return;
            }
            form.dataset.splAjaxBound = '1';
            form.addEventListener('submit', function(e) {
                e.preventDefault();
                
                var formData = new FormData(form);
                
                var action = form.action || '/submit';
                var submitButton = form.querySelector('button[type=""submit""], input[type=""submit""]');
                var originalText = submitButton ? submitButton.textContent || submitButton.value : '';
                
                if (submitButton) {
                    submitButton.disabled = true;
                    if (submitButton.tagName === 'BUTTON') {
                        submitButton.textContent = 'Submitting...';
                    } else {
                        submitButton.value = 'Submitting...';
                    }
                }
                
                fetch(action, {
                    method: 'POST',
                    body: formData
                })
                .then(function(response) {
                    // Check for redirect (3xx status codes)
                    if (response.status >= 300 && response.status < 400) {
                        var location = response.headers.get('Location') || response.url;
                        if (location) {
                            window.location.href = location;
                            return;
                        }
                    }
                    return response.text().then(function(html) {
                        return {
                            html: html,
                            isFragment: (response.headers.get('X-Malda-Fragment') || '').toLowerCase() === 'true',
                            targetId: response.headers.get('X-Malda-Fragment-Target') || ''
                        };
                    });
                })
                .then(function(result) {
                    if (result === undefined) return; // Redirect already handled
                    if (result.isFragment && result.targetId) {
                        var target = document.getElementById(result.targetId);
                        if (target) {
                            target.innerHTML = result.html;
                            initAjaxHelper();
                            return;
                        }
                    }
                    // Fallback to full-page replacement.
                    document.body.innerHTML = result.html;
                    initAjaxHelper();
                })
                .catch(function(error) {
                    alert('Error submitting form: ' + error.message);
                })
                .finally(function() {
                    if (submitButton) {
                        submitButton.disabled = false;
                        if (submitButton.tagName === 'BUTTON') {
                            submitButton.textContent = originalText;
                        } else {
                            submitButton.value = originalText;
                        }
                    }
                });
            });
        });
    }
})();
</script>";
        
        // Inject before closing </body> tag, or at the end if no body tag
        if (html.Contains("</body>"))
        {
            return html.Replace("</body>", ajaxScript + "\n</body>");
        }
        else if (html.Contains("</html>"))
        {
            return html.Replace("</html>", ajaxScript + "\n</html>");
        }
        else
        {
            // No closing tags, append at the end
            return html + ajaxScript;
        }
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

    public static void SetComponentState(string componentId, string key, RuntimeValue value)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            if (!_componentStateStore.TryGetValue(componentId, out var entry))
            {
                EnsureComponentCapacityLocked();
                entry = new ComponentStateEntry();
                _componentStateStore[componentId] = entry;
            }

            entry.LastAccessUtc = DateTime.UtcNow;
            if (!entry.Values.ContainsKey(key) && entry.Values.Count >= _componentStateMaxKeysPerComponent)
            {
                var oldestKey = entry.Values.Keys.FirstOrDefault();
                if (oldestKey != null)
                {
                    entry.Values.Remove(oldestKey);
                }
            }

            entry.Values[key] = SnapshotComponentStateValue(value);
        }
    }

    public static RuntimeValue GetComponentState(string componentId, string key)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            if (!_componentStateStore.TryGetValue(componentId, out var entry))
            {
                return RuntimeValue.Null();
            }

            entry.LastAccessUtc = DateTime.UtcNow;
            if (!entry.Values.TryGetValue(key, out var value))
            {
                return RuntimeValue.Null();
            }

            return SnapshotComponentStateValue(value);
        }
    }

    public static RuntimeValue GetComponentStateObject(string componentId)
    {
        var obj = new JsonObject();
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            if (_componentStateStore.TryGetValue(componentId, out var entry))
            {
                entry.LastAccessUtc = DateTime.UtcNow;
                foreach (var kvp in entry.Values)
                {
                    obj.Set(kvp.Key, SnapshotComponentStateValue(kvp.Value));
                }
            }
        }

        return RuntimeValue.Object(obj);
    }

    private static RuntimeValue SnapshotComponentStateValue(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Array => RuntimeValue.Array(value.AsArray().Select(SnapshotComponentStateValue).ToList()),
            ValueType.Object => RuntimeValue.Object(SnapshotComponentStateObject(value.AsObject())),
            _ => value
        };
    }

    private static ObjectInstance SnapshotComponentStateObject(ObjectInstance source)
    {
        ObjectInstance clone = source switch
        {
            JsonObject => new JsonObject(),
            DictionaryInstance => new DictionaryInstance(),
            _ => new ObjectInstance(source.Class)
        };

        foreach (var key in source.GetAllKeys())
        {
            try
            {
                clone.Set(key, SnapshotComponentStateValue(source.Get(key, null)));
            }
            catch
            {
                // Skip members that cannot be materialized as persistent state.
            }
        }

        NormalizeBoundedStateObject(clone);
        return clone;
    }

    private static void NormalizeBoundedStateObject(ObjectInstance stateObject)
    {
        var hasCount = HasStateObjectKey(stateObject, "count");
        var hasHead = HasStateObjectKey(stateObject, "head");
        var hasMaxItems = HasStateObjectKey(stateObject, "maxItems");
        var hasItems = HasStateObjectKey(stateObject, "items");
        if (!hasItems || (!hasCount && !hasHead && !hasMaxItems))
        {
            return;
        }

        var countValue = GetStateObjectValue(stateObject, "count");
        var headValue = GetStateObjectValue(stateObject, "head");
        var maxItemsValue = GetStateObjectValue(stateObject, "maxItems");
        var itemsValue = GetStateObjectValue(stateObject, "items");
        var items = itemsValue.Type == ValueType.Array ? itemsValue.AsArray() : new List<RuntimeValue>();
        if (hasItems && itemsValue.Type != ValueType.Array)
        {
            stateObject.Set("items", RuntimeValue.Array(items));
        }

        var itemsLength = items.Count;

        if (countValue.Type == ValueType.Integer)
        {
            var normalizedCount = Math.Clamp(countValue.AsInteger(), 0, itemsLength);
            if (normalizedCount != countValue.AsInteger())
            {
                stateObject.Set("count", RuntimeValue.Integer(normalizedCount));
            }
            countValue = RuntimeValue.Integer(normalizedCount);
        }

        var capacity = itemsLength;
        if (maxItemsValue.Type == ValueType.Integer)
        {
            var requestedCapacity = Math.Max(0, maxItemsValue.AsInteger());
            if (itemsLength > 0 && requestedCapacity > itemsLength)
            {
                requestedCapacity = itemsLength;
            }

            if (requestedCapacity != maxItemsValue.AsInteger())
            {
                stateObject.Set("maxItems", RuntimeValue.Integer(requestedCapacity));
            }

            capacity = requestedCapacity > 0 ? requestedCapacity : itemsLength;
        }

        if (headValue.Type == ValueType.Integer)
        {
            var moduloBase = capacity > 0 ? capacity : itemsLength;
            var normalizedHead = headValue.AsInteger();
            if (moduloBase <= 0)
            {
                normalizedHead = 0;
            }
            else
            {
                normalizedHead %= moduloBase;
                if (normalizedHead < 0)
                {
                    normalizedHead += moduloBase;
                }

                if (countValue.Type == ValueType.Integer && countValue.AsInteger() < moduloBase)
                {
                    normalizedHead = 0;
                }
            }

            if (normalizedHead != headValue.AsInteger())
            {
                stateObject.Set("head", RuntimeValue.Integer(normalizedHead));
            }
        }
    }

    private static RuntimeValue GetStateObjectValue(ObjectInstance stateObject, string key)
    {
        if (stateObject is JsonObject jsonObject)
        {
            return jsonObject.Get(key, null);
        }

        try
        {
            return stateObject.Get(key, null);
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }

    private static bool HasStateObjectKey(ObjectInstance stateObject, string key)
    {
        if (stateObject is JsonObject jsonObject)
        {
            return jsonObject.GetProperties().ContainsKey(key);
        }

        if (stateObject is DictionaryInstance dictionaryInstance)
        {
            return dictionaryInstance.Entries.ContainsKey(key);
        }

        return stateObject.GetAllKeys().Contains(key);
    }

    public static void ClearComponentState(string componentId)
    {
        lock (_componentStateLock)
        {
            CleanupExpiredComponentStateLocked();
            _componentStateStore.Remove(componentId);
        }
    }

    public static void ClearAllComponentState()
    {
        lock (_componentStateLock)
        {
            _componentStateStore.Clear();
        }
    }

    public static void ConfigureComponentStatePolicy(int maxComponents, int maxKeysPerComponent, int ttlMilliseconds)
    {
        if (maxComponents <= 0)
            throw new Exception("maxComponents must be > 0");
        if (maxKeysPerComponent <= 0)
            throw new Exception("maxKeysPerComponent must be > 0");
        if (ttlMilliseconds <= 0)
            throw new Exception("ttlMilliseconds must be > 0");

        lock (_componentStateLock)
        {
            _componentStateMaxComponents = maxComponents;
            _componentStateMaxKeysPerComponent = maxKeysPerComponent;
            _componentStateTtl = TimeSpan.FromMilliseconds(ttlMilliseconds);
            CleanupExpiredComponentStateLocked();
            EnsureComponentCapacityLocked();
        }
    }

    private static void CleanupExpiredComponentStateLocked()
    {
        if (_componentStateStore.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var expired = _componentStateStore
            .Where(kvp => (now - kvp.Value.LastAccessUtc) > _componentStateTtl)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in expired)
        {
            _componentStateStore.Remove(key);
        }
    }

    private static void EnsureComponentCapacityLocked()
    {
        while (_componentStateStore.Count >= _componentStateMaxComponents)
        {
            var oldest = _componentStateStore
                .OrderBy(kvp => kvp.Value.LastAccessUtc)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(oldest.Key))
            {
                break;
            }
            _componentStateStore.Remove(oldest.Key);
        }
    }
    
    /// <summary>
    /// Static method to register transpiled AIPAGE routes with descriptions.
    /// </summary>
    public static void RegisterTranspiledAIPage(
        string path,
        string functionName,
        List<string> paramNames,
        string description,
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

        // Store the description for future instances
        lock (_pendingAiPageDescriptionsLock)
        {
            _pendingAiPageDescriptions[effectivePath] = description;
        }
        
        // Store the route for future instances
        lock (_pendingRoutesLock)
        {
            _pendingRoutes.Add(new PendingRoute("GET", effectivePath, functionName, paramNames, null, metadata));
        }
        
        // Apply to all existing instances
        lock (_instancesLock)
        {
            foreach (var instance in _instances)
            {
                instance._routeRegistry.RegisterTranspiledRoute("GET", effectivePath, functionName, paramNames, null, metadata);
                lock (instance._aiCacheLock)
                {
                    instance._aiPageDescriptions[effectivePath] = description;
                }
            }
        }
    }
}