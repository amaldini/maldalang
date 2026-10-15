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

public partial class HttpServerInstance : ObjectInstance
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
    private string _host;
    private string _webDirectory;
    private bool _isRunning = false;
    private bool _httpsEnabled;
    private string _certPath = string.Empty;
    private string _certPassword = string.Empty;
    
    public int Port => _port;
    public string Host => _host;
    public string WebDirectory => _webDirectory;
    public bool IsRunning => _isRunning;
    public bool HttpsEnabled => _httpsEnabled;
    public string CertPath => _certPath;
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
        private readonly object _writeLock = new();
        private readonly Action<byte[]> _write;
        private readonly Action? _close;

        public HashSet<string> Channels { get; }

        public SseConnection(HttpListenerResponse response, HashSet<string> channels)
            : this(bytes =>
                {
                    response.OutputStream.Write(bytes, 0, bytes.Length);
                    response.OutputStream.Flush();
                }, channels, () =>
                {
                    try
                    {
                        response.Abort();
                    }
                    catch
                    {
                        try { response.Close(); }
                        catch { /* ignore */ }
                    }
                })
        {
        }

        public SseConnection(Action<byte[]> write, HashSet<string> channels, Action? close = null)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _close = close;
            Channels = channels;
        }

        public bool TryWrite(byte[] bytes)
        {
            lock (_writeLock)
            {
                try
                {
                    _write(bytes);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void Close()
        {
            try
            {
                _close?.Invoke();
            }
            catch
            {
                // Best-effort: client may already have dropped.
            }
        }
    }

    /// <summary>
    /// HTTPS proxy asks the loopback pipeline to authorize/register the LIVE route,
    /// then serves the long-lived event stream from Kestrel (avoids HttpClient buffering).
    /// </summary>
    internal const string SseDelegateHeaderName = "X-Malda-Sse-Delegate";
    internal const string SseReadyHeaderName = "X-Malda-Sse-Ready";
    internal const string SseChannelsHeaderName = "X-Malda-Sse-Channels";

    // Store active SSE connections (connection ID -> connection metadata)
    private static readonly Dictionary<string, SseConnection> _sseConnections = new();
    private static readonly object _sseConnectionsLock = new object();
    private static int _sseConnectionCounter = 0;
    private static int _consoleCancelHooked;
    private static readonly ConsoleCancelEventHandler ConsoleCancelHandler = OnConsoleCancel;

    private sealed class ComponentStateEntry
    {
        public Dictionary<string, RuntimeValue> Values { get; }
        public DateTime LastAccessUtc { get; set; }
        public bool Pinned { get; set; }

        public ComponentStateEntry()
        {
            Values = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
            LastAccessUtc = DateTime.UtcNow;
            Pinned = false;
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
    private SessionOptions? _sessionOptions;
    private RestServerInstance? _mountedRest;
    private bool _trustProxy;
    private string _trustedProxyHeader = "X-Forwarded-For";
    private int _trustedProxyHopIndex;
    private bool _rateLimitHeadersEnabled;
    private bool _rateLimitRemainingEnabled = true;
    private int _rateLimitLimit = 60;
    private int _rateLimitWindowSeconds = 60;
    private readonly string _pathBase;
    
    public HttpServerInstance(int port, string? webDirectory = null, Interpreter? interpreter = null, string? pathBase = null, string? host = null) : base(null)
    {
        _port = port;
        _interpreter = interpreter; // Allow null for transpiled code
        _routeRegistry = new RouteRegistry();
        
        // Path base for reverse-proxy deployments (e.g. /schoolprep). Read from MALDA_PATH_BASE env if not passed.
        _pathBase = NormalizePathBase(pathBase ?? System.Environment.GetEnvironmentVariable("MALDA_PATH_BASE") ?? string.Empty);

        // Bind host: explicit ctor arg, else MALDA_HTTP_HOST, else localhost (loopback-only).
        _host = ResolveHost(host);
        
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
    public HttpServerInstance(object? port, string? webDirectory = null, Interpreter? interpreter = null, string? pathBase = null, string? host = null)
        : this(CoercePort(port), webDirectory, interpreter, pathBase, host)
    {
    }

    // Overload for transpiled code where pathBase/host may come from getEnv() (RuntimeValue).
    public HttpServerInstance(object? port, object? webDirectory, Interpreter? interpreter, object? pathBase, object? host = null)
        : this(CoercePort(port), CoerceWebDirectory(webDirectory), interpreter, CoercePathBase(pathBase), CoerceHost(host))
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

    private static string? CoerceHost(object? value)
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

    /// <summary>
    /// Resolve bind host: explicit value, else MALDA_HTTP_HOST, else localhost.
    /// Normalizes "*" / "all" to "0.0.0.0" (HttpListener wildcard prefix).
    /// </summary>
    internal static string ResolveHost(string? host)
    {
        var raw = host;
        if (string.IsNullOrWhiteSpace(raw))
            raw = System.Environment.GetEnvironmentVariable("MALDA_HTTP_HOST");
        if (string.IsNullOrWhiteSpace(raw))
            return "localhost";
        return NormalizeHost(raw);
    }

    internal static string NormalizeHost(string host)
    {
        var h = (host ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(h))
            throw new Exception("HttpServer host cannot be empty");
        if (string.Equals(h, "*", StringComparison.Ordinal) ||
            string.Equals(h, "all", StringComparison.OrdinalIgnoreCase))
            return "0.0.0.0";
        return h;
    }

    /// <summary>HttpListener prefix for the given bind host and port (RestServer-compatible).</summary>
    internal static string BuildListenerPrefix(string host, int port)
    {
        var h = NormalizeHost(host);
        return h == "0.0.0.0" ? $"http://*:{port}/" : $"http://{h}:{port}/";
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
        if (name == "host")
            return RuntimeValue.String(_host);
        if (name == "https")
            return RuntimeValue.Boolean(_httpsEnabled);
        if (name == "certPath")
            return RuntimeValue.String(_certPath);
        if (name == "isRunning")
            return RuntimeValue.Boolean(_isRunning);
        if (name == "webDirectory")
            return RuntimeValue.String(_webDirectory);
        if (name == "pathBase")
            return RuntimeValue.String(_pathBase);
        
        // Handle method access
        if (name == "start" || name == "stop" || name == "clearCache" || name == "getRoutes" || name == "setHTML" || name == "broadcastSSE" || name == "use" ||
            name == "setRateLimit" || name == "disableRateLimit" || name == "enableCsrf" || name == "disableCsrf" ||
            name == "enableSession" || name == "disableSession" || name == "mount" ||
            name == "configureTrustedProxy" || name == "setRateLimitHeaders" || name == "setHost" ||
            name == "enableHttps" || name == "disableHttps")
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

            case "enableSession":
                _sessionOptions = SessionRuntime.ParseEnableSessionArgs(args);
                return RuntimeValue.Null();

            case "disableSession":
                if (args.Count != 0)
                    throw new Exception("disableSession() expects 0 arguments");
                _sessionOptions = null;
                return RuntimeValue.Null();

            case "mount":
                if (args.Count != 1 || args[0].Type != ValueType.Object ||
                    args[0].AsObject() is not RestServerInstance restServer)
                    throw new Exception("mount() expects a RestServer instance");
                if (_mountedRest != null && !ReferenceEquals(_mountedRest, restServer))
                    throw new Exception("HttpServer already has a mounted RestServer");
                restServer.AttachToHost(this);
                _mountedRest = restServer;
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

            case "setHost":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setHost() expects 1 string argument");
                SetHost(args[0].AsString());
                return RuntimeValue.Null();

            case "enableHttps":
                if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
                    throw new Exception("enableHttps() expects certPath string and optional password string");
                if (args.Count == 2 && args[1].Type != ValueType.String)
                    throw new Exception("enableHttps() password must be a string when provided");
                EnableHttps(args[0].AsString(), args.Count == 2 ? args[1].AsString() : string.Empty);
                return RuntimeValue.Null();

            case "disableHttps":
                if (args.Count != 0)
                    throw new Exception("disableHttps() expects 0 arguments");
                DisableHttps();
                return RuntimeValue.Null();
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }

    public void SetHost(string host)
    {
        if (_isRunning)
            throw new Exception("Cannot setHost() while HttpServer is running");
        _host = NormalizeHost(host);
    }

    public void EnableHttps(string certPath, string? password = null)
    {
        if (_isRunning)
            throw new Exception("Cannot enableHttps() while HttpServer is running");
        if (string.IsNullOrWhiteSpace(certPath))
            throw new Exception("enableHttps() certPath cannot be empty");
        _httpsEnabled = true;
        _certPath = certPath.Trim();
        _certPassword = password ?? string.Empty;
    }

    public void DisableHttps()
    {
        if (_isRunning)
            throw new Exception("Cannot disableHttps() while HttpServer is running");
        _httpsEnabled = false;
        _certPath = string.Empty;
        _certPassword = string.Empty;
    }
}