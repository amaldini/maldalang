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

public partial class RestServerInstance : ObjectInstance
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
}