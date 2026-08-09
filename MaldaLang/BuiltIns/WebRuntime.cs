// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;

public class WebMiddlewareRegistration
{
    public FunctionValue? Function { get; }
    public string? FunctionName { get; }
    public IReadOnlyList<string> ExceptPaths { get; }

    public WebMiddlewareRegistration(FunctionValue function, IReadOnlyList<string>? exceptPaths = null)
    {
        Function = function;
        ExceptPaths = exceptPaths ?? Array.Empty<string>();
    }

    public WebMiddlewareRegistration(string functionName, IReadOnlyList<string>? exceptPaths = null)
    {
        FunctionName = functionName;
        ExceptPaths = exceptPaths ?? Array.Empty<string>();
    }

    public bool ShouldSkipPath(string path)
    {
        if (ExceptPaths.Count == 0)
        {
            return false;
        }

        var normalizedPath = string.IsNullOrEmpty(path) ? "/" : path;
        foreach (var pattern in ExceptPaths)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (PathMatchesExceptPattern(normalizedPath, pattern.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PathMatchesExceptPattern(string path, string pattern)
    {
        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);
    }
}

public class MiddlewareNextCallbackInstance : ObjectInstance
{
    private readonly Action _onNext;

    public MiddlewareNextCallbackInstance(Action onNext) : base(null)
    {
        _onNext = onNext;
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "invoke")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = "invoke"
            };
            return RuntimeValue.Function(wrapper);
        }

        throw new Exception($"Undefined property '{name}' on middleware next callback.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        if (methodName != "invoke")
        {
            throw new Exception($"Unknown method: {methodName}");
        }

        if (args.Count != 0)
        {
            throw new Exception("next() expects 0 arguments");
        }

        _onNext();
        return RuntimeValue.Null();
    }
}

public class RequestContextInstance : ObjectInstance
{
    private Dictionary<string, string> _pathParams;
    private readonly Dictionary<string, string> _query;
    private readonly Dictionary<string, string> _headers;
    private readonly Dictionary<string, string> _cookies;
    private readonly Dictionary<string, RuntimeValue> _locals = new(StringComparer.Ordinal);
    private readonly RequestAuthContextInstance _authContext;
    private RequestSessionContextInstance _sessionContext;

    public string Method { get; }
    public string Path { get; }
    public string PathBase { get; }
    public string CorrelationId { get; }
    public string RemoteIp { get; }
    public string QueryString { get; }
    public string Url { get; }
    public string Scheme { get; }
    public string Host { get; }
    public string ContentType { get; }
    public RuntimeValue Body { get; }
    public RequestAuthContextInstance AuthContext => _authContext;
    public RequestSessionContextInstance Session => _sessionContext;

    public RequestContextInstance(
        string method,
        string path,
        Dictionary<string, string> query,
        Dictionary<string, string> headers,
        Dictionary<string, string> cookies,
        RuntimeValue? body,
        Dictionary<string, string>? pathParams = null,
        string? correlationId = null,
        string? remoteIp = null,
        string? queryString = null,
        string? url = null,
        string? scheme = null,
        string? host = null,
        string? contentType = null,
        string? pathBase = null,
        SessionOptions? sessionOptions = null) : base(null)
    {
        Method = method;
        Path = path;
        PathBase = pathBase ?? string.Empty;
        CorrelationId = correlationId ?? string.Empty;
        RemoteIp = remoteIp ?? string.Empty;
        QueryString = queryString ?? string.Empty;
        Url = url ?? path;
        Scheme = scheme ?? string.Empty;
        Host = host ?? string.Empty;
        ContentType = contentType ?? string.Empty;
        _query = query;
        _headers = headers;
        _cookies = cookies;
        Body = body ?? RuntimeValue.Null();
        _pathParams = pathParams ?? new Dictionary<string, string>();
        var verifiedSub = WebRuntimeHelpers.ResolveVerifiedSubjectFromHeaders(_headers);
        _authContext = new RequestAuthContextInstance(verifiedSub, _headers, _cookies);
        _sessionContext = SessionRuntime.CreateSessionContext(_cookies, sessionOptions);
    }

    public void AttachSession(RequestSessionContextInstance session)
    {
        _sessionContext = session;
    }

    public void SetPathParams(Dictionary<string, string> pathParams)
    {
        _pathParams = pathParams;
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (_locals.TryGetValue(name, out var localValue))
        {
            return localValue;
        }

        return name switch
        {
            "method" => RuntimeValue.String(Method),
            "path" => RuntimeValue.String(Path),
            "pathBase" => RuntimeValue.String(PathBase),
            "url" => RuntimeValue.String(Url),
            "queryString" => RuntimeValue.String(QueryString),
            "scheme" => RuntimeValue.String(Scheme),
            "protocol" => RuntimeValue.String(Scheme),
            "host" => RuntimeValue.String(Host),
            "contentType" => RuntimeValue.String(ContentType),
            "correlationId" => RuntimeValue.String(CorrelationId),
            "ip" => RuntimeValue.String(RemoteIp),
            "remoteIp" => RuntimeValue.String(RemoteIp),
            "query" => RuntimeValue.Object(ToJsonObject(_query)),
            "queryParams" => RuntimeValue.Object(ToJsonObject(_query)),
            "headers" => RuntimeValue.Object(ToJsonObject(_headers)),
            "auth" => RuntimeValue.Object(_authContext),
            "authContext" => RuntimeValue.Object(_authContext),
            "session" => RuntimeValue.Object(_sessionContext),
            "verifiedSub" => _authContext.HasVerifiedSubject
                ? RuntimeValue.String(_authContext.VerifiedSubject)
                : RuntimeValue.Null(),
            "cookies" => RuntimeValue.Object(ToJsonObject(_cookies)),
            "params" => RuntimeValue.Object(ToJsonObject(_pathParams)),
            "pathParams" => RuntimeValue.Object(ToJsonObject(_pathParams)),
            "body" => Body,
            "header" or "queryParam" or "param" or "cookie" or
            "hasHeader" or "hasQueryParam" or "hasParam" or "hasCookie" or
            "validate" or "requireValid" => RuntimeValue.Function(new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            }),
            _ => throw new Exception($"Undefined property '{name}' on Request context.")
        };
    }

    public override bool TryGet(string name, out RuntimeValue? value, ClassDefinition? accessingClass = null)
    {
        if (_locals.TryGetValue(name, out var localValue))
        {
            value = localValue;
            return true;
        }

        try
        {
            value = Get(name, accessingClass);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    public override void Set(string name, RuntimeValue value)
    {
        if (IsReservedMemberName(name))
        {
            throw new Exception($"Cannot overwrite built-in request member '{name}'.");
        }

        _locals[name] = value;
    }

    public override IEnumerable<string> GetAllKeys()
    {
        foreach (var key in _locals.Keys)
        {
            yield return key;
        }

        foreach (var key in new[]
                 {
                     "method", "path", "url", "queryString", "scheme", "protocol", "host", "contentType",
                     "correlationId", "ip", "remoteIp", "query", "queryParams", "headers", "auth", "authContext",
                     "session", "verifiedSub", "cookies", "params", "pathParams", "body",
                     "header", "queryParam", "param", "cookie", "hasHeader", "hasQueryParam", "hasParam", "hasCookie",
                     "validate", "requireValid"
                 })
        {
            yield return key;
        }
    }

    public string GetVerifiedSubject()
    {
        return _authContext.VerifiedSubject;
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        return methodName switch
        {
            "header" => LookupValue(_headers, "header", args),
            "queryParam" => LookupValue(_query, "queryParam", args),
            "param" => LookupValue(_pathParams, "param", args),
            "cookie" => LookupValue(_cookies, "cookie", args),
            "hasHeader" => RuntimeValue.Boolean(ContainsKey(_headers, "hasHeader", args)),
            "hasQueryParam" => RuntimeValue.Boolean(ContainsKey(_query, "hasQueryParam", args)),
            "hasParam" => RuntimeValue.Boolean(ContainsKey(_pathParams, "hasParam", args)),
            "hasCookie" => RuntimeValue.Boolean(ContainsKey(_cookies, "hasCookie", args)),
            "validate" => ValidateAgainstSchema(args, throwOnFailure: false),
            "requireValid" => ValidateAgainstSchema(args, throwOnFailure: true),
            _ => throw new Exception($"Unknown method: {methodName}")
        };
    }

    private RuntimeValue ValidateAgainstSchema(List<RuntimeValue> args, bool throwOnFailure)
    {
        var methodName = throwOnFailure ? "requireValid" : "validate";
        if (args.Count != 1)
        {
            throw new Exception($"{methodName}() expects 1 schema argument");
        }

        if (WebRuntimeHelpers.ValidateRequest(args[0], _pathParams, _query, Body, out var errors))
        {
            return throwOnFailure
                ? RuntimeValue.Object(this)
                : WebRuntimeHelpers.CreateValidationResultRuntimeValue(errors);
        }

        if (throwOnFailure)
        {
            throw new WebRuntimeException(
                400,
                "ValidationError",
                WebRuntimeHelpers.BuildValidationFailureMessage(errors),
                errors);
        }

        return WebRuntimeHelpers.CreateValidationResultRuntimeValue(errors);
    }

    private static RuntimeValue LookupValue(
        Dictionary<string, string> source,
        string methodName,
        List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
        {
            throw new Exception($"{methodName}() expects key string and optional default value");
        }

        var key = args[0].AsString();
        if (source.TryGetValue(key, out var value))
        {
            return RuntimeValue.String(value);
        }

        return args.Count == 2 ? args[1] : RuntimeValue.Null();
    }

    private static bool ContainsKey(
        Dictionary<string, string> source,
        string methodName,
        List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
        {
            throw new Exception($"{methodName}() expects 1 string argument");
        }

        return source.ContainsKey(args[0].AsString());
    }

    private static bool IsReservedMemberName(string name)
    {
        return name is
            "method" or "path" or "url" or "queryString" or "scheme" or "protocol" or "host" or "contentType" or
            "correlationId" or "ip" or "remoteIp" or "query" or "queryParams" or "headers" or "auth" or
            "authContext" or "session" or "verifiedSub" or "cookies" or "params" or "pathParams" or "body" or
            "header" or "queryParam" or "param" or "cookie" or "hasHeader" or "hasQueryParam" or
            "hasParam" or "hasCookie" or "validate" or "requireValid";
    }

    private static JsonObject ToJsonObject(Dictionary<string, string> source)
    {
        var obj = new JsonObject();
        foreach (var kvp in source)
        {
            obj.Set(kvp.Key, RuntimeValue.String(kvp.Value));
        }
        return obj;
    }
}

public class RequestAuthContextInstance : ObjectInstance
{
    private readonly Dictionary<string, string> _headers;
    private readonly Dictionary<string, string> _cookies;

    public bool IsVerified { get; private set; }
    public string VerifiedSubject { get; private set; } = string.Empty;
    public bool HasVerifiedSubject => IsVerified && !string.IsNullOrWhiteSpace(VerifiedSubject);
    public RuntimeValue Claims { get; private set; } = RuntimeValue.Null();
    public string BearerToken { get; private set; } = string.Empty;

    public RequestAuthContextInstance(
        string verifiedSubject,
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? cookies = null) : base(null)
    {
        _headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _cookies = cookies ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(verifiedSubject))
        {
            IsVerified = true;
            VerifiedSubject = verifiedSubject.Trim();
        }
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "verified")
        {
            return RuntimeValue.Boolean(IsVerified);
        }

        if (name == "sub" || name == "subject")
        {
            return HasVerifiedSubject ? RuntimeValue.String(VerifiedSubject) : RuntimeValue.Null();
        }

        if (name == "claims")
        {
            return Claims;
        }

        if (name == "roles")
        {
            return RuntimeValue.Array(BuildClaimArray("role", "roles"));
        }

        if (name == "permissions")
        {
            return RuntimeValue.Array(BuildClaimArray("permission", "permissions", "scope", "scp"));
        }

        if (name is "token" or "bearerToken")
        {
            return string.IsNullOrWhiteSpace(BearerToken) ? RuntimeValue.Null() : RuntimeValue.String(BearerToken);
        }

        if (name is
            "setVerifiedSub" or "setAnonymous" or "clear" or
            "authenticateBearerJwt" or "authenticateCookieJwt" or
            "claim" or "hasClaim" or
            "hasRole" or "requireRole" or
            "hasPermission" or "requirePermission" or
            "requireVerified")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }

        throw new Exception($"Undefined property '{name}' on Request auth context.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "setVerifiedSub":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.setVerifiedSub() expects 1 string argument");
                }

                var sub = args[0].AsString().Trim();
                if (string.IsNullOrWhiteSpace(sub))
                {
                    throw new Exception("req.auth.setVerifiedSub() subject cannot be empty");
                }

                SetVerifiedPrincipal(sub, RuntimeValue.Null(), string.Empty);
                return RuntimeValue.Object(this);

            case "setAnonymous":
            case "clear":
                if (args.Count != 0)
                {
                    throw new Exception($"req.auth.{methodName}() expects 0 arguments");
                }
                ClearPrincipal();
                return RuntimeValue.Object(this);

            case "authenticateBearerJwt":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.authenticateBearerJwt() expects 1 string secret argument");
                }

                return AuthenticateBearerJwt(args[0].AsString());

            case "authenticateCookieJwt":
                if (args.Count < 2 || args.Count > 3 ||
                    args[0].Type != ValueType.String ||
                    args[1].Type != ValueType.String ||
                    (args.Count == 3 && args[2].Type != ValueType.String))
                {
                    throw new Exception("req.auth.authenticateCookieJwt() expects cookieName, jwtSecret, and optional cookieSecret");
                }

                return AuthenticateCookieJwt(
                    args[0].AsString(),
                    args[1].AsString(),
                    args.Count == 3 ? args[2].AsString() : string.Empty);

            case "claim":
                if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.claim() expects claimName and optional defaultValue");
                }

                return GetClaimOrDefault(
                    args[0].AsString(),
                    args.Count == 2 ? args[1] : RuntimeValue.Null());

            case "hasClaim":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.hasClaim() expects 1 string claimName argument");
                }

                return RuntimeValue.Boolean(HasClaim(args[0].AsString()));

            case "hasRole":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.hasRole() expects 1 string role argument");
                }

                return RuntimeValue.Boolean(HasRole(args[0].AsString()));

            case "requireRole":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.requireRole() expects 1 string role argument");
                }

                RequireVerified();
                var requiredRole = args[0].AsString();
                if (!HasRole(requiredRole))
                {
                    throw new WebRuntimeException(403, "Forbidden", $"Role '{requiredRole}' is required.");
                }

                return RuntimeValue.Object(this);

            case "hasPermission":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.hasPermission() expects 1 string permission argument");
                }

                return RuntimeValue.Boolean(HasPermission(args[0].AsString()));

            case "requirePermission":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                {
                    throw new Exception("req.auth.requirePermission() expects 1 string permission argument");
                }

                RequireVerified();
                var requiredPermission = args[0].AsString();
                if (!HasPermission(requiredPermission))
                {
                    throw new WebRuntimeException(403, "Forbidden", $"Permission '{requiredPermission}' is required.");
                }

                return RuntimeValue.Object(this);

            case "requireVerified":
                if (args.Count != 0)
                {
                    throw new Exception("req.auth.requireVerified() expects 0 arguments");
                }

                RequireVerified();
                return RuntimeValue.Object(this);

            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }

    public override IEnumerable<string> GetAllKeys()
    {
        foreach (var key in new[]
                 {
                     "verified", "sub", "subject", "claims", "roles", "permissions", "token", "bearerToken",
                     "setVerifiedSub", "setAnonymous", "clear",
                     "authenticateBearerJwt", "authenticateCookieJwt",
                     "claim", "hasClaim",
                     "hasRole", "requireRole",
                     "hasPermission", "requirePermission",
                     "requireVerified"
                 })
        {
            yield return key;
        }
    }

    private RuntimeValue AuthenticateBearerJwt(string secret)
    {
        if (!WebRuntimeHelpers.TryGetHeaderValue(_headers, "Authorization", out var rawAuthorization) ||
            string.IsNullOrWhiteSpace(rawAuthorization))
        {
            throw new WebRuntimeException(401, "MissingToken", "Missing bearer token.");
        }

        var headerValue = rawAuthorization.Trim();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new WebRuntimeException(401, "InvalidToken", "Invalid Authorization header format.");
        }

        var token = headerValue["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new WebRuntimeException(401, "MissingToken", "Missing bearer token.");
        }

        return AuthenticateJwtToken(token, secret);
    }

    private RuntimeValue AuthenticateCookieJwt(string cookieName, string jwtSecret, string cookieSecret)
    {
        if (string.IsNullOrWhiteSpace(cookieName))
        {
            throw new Exception("req.auth.authenticateCookieJwt() cookieName cannot be empty");
        }

        if (!TryGetDictionaryValue(_cookies, cookieName, out var rawCookie) || string.IsNullOrWhiteSpace(rawCookie))
        {
            throw new WebRuntimeException(401, "MissingToken", $"Missing auth cookie '{cookieName}'.");
        }

        var cookieToken = SafeUrlDecode(rawCookie.Trim());
        if (!string.IsNullOrWhiteSpace(cookieSecret))
        {
            if (!WebRuntimeHelpers.TryReadSecureCookieValue(cookieToken, cookieSecret, out cookieToken) ||
                string.IsNullOrWhiteSpace(cookieToken))
            {
                throw new WebRuntimeException(401, "InvalidToken", $"Invalid auth cookie '{cookieName}'.");
            }
        }

        return AuthenticateJwtToken(cookieToken, jwtSecret);
    }

    private RuntimeValue AuthenticateJwtToken(string token, string secret)
    {
        var claims = BuiltInFunctions.CallBuiltIn(
            "verifyJwt",
            new List<RuntimeValue>
            {
                RuntimeValue.String(token),
                RuntimeValue.String(secret)
            },
            null!);

        if (claims.Type != ValueType.Object || claims.AsObject() is not JsonObject claimsObject)
        {
            throw new WebRuntimeException(401, "InvalidToken", "Invalid token payload.");
        }

        var subject = claimsObject.Get("sub", null);
        if (subject.Type != ValueType.String || string.IsNullOrWhiteSpace(subject.AsString()))
        {
            throw new WebRuntimeException(401, "InvalidToken", "JWT subject claim is required.");
        }

        SetVerifiedPrincipal(subject.AsString().Trim(), claims, token);
        return claims;
    }

    private void RequireVerified()
    {
        if (!HasVerifiedSubject)
        {
            throw new WebRuntimeException(401, "Unauthorized", "Authentication required.");
        }
    }

    private RuntimeValue GetClaimOrDefault(string claimName, RuntimeValue defaultValue)
    {
        return TryGetClaimValue(claimName, out var claimValue) && claimValue.Type != ValueType.Null
            ? claimValue
            : defaultValue;
    }

    private bool HasClaim(string claimName)
    {
        return TryGetClaimValue(claimName, out var claimValue) && claimValue.Type != ValueType.Null;
    }

    private bool HasRole(string role)
    {
        return ClaimListContains(role, "role", "roles");
    }

    private bool HasPermission(string permission)
    {
        return ClaimListContains(permission, "permission", "permissions", "scope", "scp");
    }

    private bool ClaimListContains(string candidate, params string[] claimNames)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var value in EnumerateClaimStringValues(claimNames))
        {
            if (value.Equals(candidate.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private ArrayInstance BuildClaimArray(params string[] claimNames)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<RuntimeValue>();
        foreach (var value in EnumerateClaimStringValues(claimNames))
        {
            if (seen.Add(value))
            {
                values.Add(RuntimeValue.String(value));
            }
        }

        return new ArrayInstance(values);
    }

    private IEnumerable<string> EnumerateClaimStringValues(params string[] claimNames)
    {
        foreach (var claimName in claimNames)
        {
            if (!TryGetClaimValue(claimName, out var claimValue) || claimValue.Type == ValueType.Null)
            {
                continue;
            }

            foreach (var value in ExpandClaimValues(claimValue))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> ExpandClaimValues(RuntimeValue claimValue)
    {
        if (claimValue.Type == ValueType.String)
        {
            foreach (var part in claimValue.AsString().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return part;
            }

            yield break;
        }

        if (claimValue.Type != ValueType.Array)
        {
            yield break;
        }

        foreach (var item in claimValue.AsArray())
        {
            if (item.Type == ValueType.String && !string.IsNullOrWhiteSpace(item.AsString()))
            {
                yield return item.AsString();
            }
        }
    }

    private bool TryGetClaimValue(string claimName, out RuntimeValue claimValue)
    {
        claimValue = RuntimeValue.Null();
        if (string.IsNullOrWhiteSpace(claimName) ||
            Claims.Type != ValueType.Object ||
            Claims.AsObject() is not JsonObject claimsObject)
        {
            return false;
        }

        var direct = claimsObject.Get(claimName, null);
        if (direct.Type != ValueType.Null)
        {
            claimValue = direct;
            return true;
        }

        foreach (var kvp in claimsObject.GetProperties())
        {
            if (kvp.Key.Equals(claimName, StringComparison.OrdinalIgnoreCase))
            {
                claimValue = kvp.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDictionaryValue(Dictionary<string, string> source, string key, out string value)
    {
        if (source.TryGetValue(key, out value!))
        {
            return true;
        }

        foreach (var kvp in source)
        {
            if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string SafeUrlDecode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            return value;
        }
    }

    private void SetVerifiedPrincipal(string subject, RuntimeValue claims, string bearerToken)
    {
        IsVerified = true;
        VerifiedSubject = subject;
        Claims = claims;
        BearerToken = bearerToken;
    }

    private void ClearPrincipal()
    {
        IsVerified = false;
        VerifiedSubject = string.Empty;
        Claims = RuntimeValue.Null();
        BearerToken = string.Empty;
    }
}

public class ResponseContextInstance : ObjectInstance
{
    private RuntimeValue _body = RuntimeValue.Null();
    private readonly List<string> _setCookieHeaders = new();

    public int StatusCode { get; private set; } = 200;
    public string ContentType { get; private set; } = "application/json; charset=utf-8";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsCommitted { get; private set; }
    public RuntimeValue Body => _body;
    public bool HasBody => _body.Type != ValueType.Null;
    public bool HasStatusOverride { get; private set; }
    public bool HasHeaders => Headers.Count > 0 || _setCookieHeaders.Count > 0;

    public ResponseContextInstance() : base(null)
    {
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "statusCode")
            return RuntimeValue.Integer(StatusCode);

        if (name is "contentType" or "type")
            return RuntimeValue.String(ContentType);

        if (name == "headers")
            return RuntimeValue.Object(ToJsonObject(Headers));

        if (name == "body")
            return _body;

        if (name is "committed" or "isCommitted" or "sent")
            return RuntimeValue.Boolean(IsCommitted);

        if (name is "status" or "json" or "text" or "html" or "redirect" or "header" or "cookie" or "clearCookie" or "send" or "setContentType")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }

        throw new Exception($"Undefined property '{name}' on Response context.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "status":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("status() expects 1 integer argument");
                StatusCode = args[0].AsInteger();
                HasStatusOverride = true;
                return RuntimeValue.Object(this);

            case "json":
                if (args.Count != 1)
                    throw new Exception("json() expects 1 argument");
                ContentType = "application/json; charset=utf-8";
                _body = args[0];
                IsCommitted = true;
                return RuntimeValue.Object(this);

            case "text":
                if (args.Count != 1)
                    throw new Exception("text() expects 1 argument");
                ContentType = "text/plain; charset=utf-8";
                _body = args[0].Type == ValueType.String ? args[0] : RuntimeValue.String(args[0].ToString());
                IsCommitted = true;
                return RuntimeValue.Object(this);

            case "html":
                if (args.Count != 1)
                    throw new Exception("html() expects 1 argument");
                ContentType = "text/html; charset=utf-8";
                _body = args[0].Type == ValueType.String ? args[0] : RuntimeValue.String(args[0].ToString());
                IsCommitted = true;
                return RuntimeValue.Object(this);

            case "redirect":
                if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
                    throw new Exception("redirect() expects location string and optional status integer");
                var redirectStatus = args.Count == 2 && args[1].Type == ValueType.Integer
                    ? args[1].AsInteger()
                    : (int?)null;
                StatusCode = WebRuntimeHelpers.NormalizeRedirectStatusCode(redirectStatus, "res.redirect()");
                HasStatusOverride = true;
                Headers["Location"] = args[0].AsString();
                ContentType = "text/html; charset=utf-8";
                _body = RuntimeValue.String(WebRuntimeHelpers.BuildRedirectHtml(args[0].AsString()));
                IsCommitted = true;
                return RuntimeValue.Object(this);

            case "header":
                if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
                    throw new Exception("header() expects 2 string arguments");
                var headerName = args[0].AsString();
                var headerValue = args[1].AsString();
                if (headerName.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    _setCookieHeaders.Add(headerValue);
                }
                else
                {
                    Headers[headerName] = headerValue;
                }
                return RuntimeValue.Object(this);

            case "cookie":
                if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
                    throw new Exception("cookie() expects name, value, and optional options object");

                RuntimeValue cookieOptions = RuntimeValue.Null();
                if (args.Count == 3)
                {
                    cookieOptions = args[2];
                    if (cookieOptions.Type != ValueType.Null && cookieOptions.Type != ValueType.Object)
                        throw new Exception("cookie() options must be an object when provided");
                }

                var cookieHeader = WebRuntimeHelpers.CreateCookieHeader(
                    args[0].AsString(),
                    args[1].AsString(),
                    cookieOptions,
                    useSecureDefaults: true);
                _setCookieHeaders.Add(cookieHeader);
                return RuntimeValue.Object(this);

            case "clearCookie":
                if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
                    throw new Exception("clearCookie() expects name and optional options object");

                RuntimeValue clearCookieOptions = RuntimeValue.Object(new JsonObject());
                if (args.Count == 2)
                {
                    if (args[1].Type != ValueType.Object)
                        throw new Exception("clearCookie() options must be an object when provided");
                    clearCookieOptions = args[1];
                }

                var clearOptions = clearCookieOptions.AsObject() as JsonObject ?? new JsonObject();
                clearOptions.Set("maxAge", RuntimeValue.Integer(0));
                _setCookieHeaders.Add(WebRuntimeHelpers.CreateCookieHeader(
                    args[0].AsString(),
                    string.Empty,
                    RuntimeValue.Object(clearOptions),
                    useSecureDefaults: true));
                return RuntimeValue.Object(this);

            case "setContentType":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setContentType() expects 1 string argument");
                ContentType = args[0].AsString();
                return RuntimeValue.Object(this);

            case "send":
                if (args.Count != 1)
                    throw new Exception("send() expects 1 argument");
                _body = args[0];
                IsCommitted = true;
                return RuntimeValue.Object(this);

            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }

    public void AddSetCookieHeader(string cookieHeader)
    {
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            _setCookieHeaders.Add(cookieHeader);
        }
    }

    public void ApplyTo(HttpListenerResponse response, string? pathBase = null)
    {
        response.StatusCode = StatusCode;
        ApplyHeadersTo(response, pathBase);
        response.StatusCode = StatusCode;

        if (!HasBody)
        {
            response.ContentLength64 = 0;
            return;
        }

        string bodyText;
        if (ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            bodyText = RuntimeValueToJson(_body);
        }
        else if (_body.Type == ValueType.String)
        {
            bodyText = _body.AsString();
        }
        else
        {
            bodyText = _body.ToString();
        }

        var bytes = Encoding.UTF8.GetBytes(bodyText);
        response.ContentType = ContentType;
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    public void ApplyHeadersTo(HttpListenerResponse response, string? pathBase = null)
    {
        foreach (var header in Headers)
        {
            if (header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                WebRuntimeHelpers.ApplyRedirectLocation(response, header.Value, pathBase);
            }
            else
            {
                response.Headers[header.Key] = header.Value;
            }
        }

        foreach (var cookieHeader in _setCookieHeaders)
        {
            response.Headers.Add("Set-Cookie", cookieHeader);
        }
    }

    private static JsonObject ToJsonObject(Dictionary<string, string> source)
    {
        var obj = new JsonObject();
        foreach (var kvp in source)
        {
            obj.Set(kvp.Key, RuntimeValue.String(kvp.Value));
        }
        return obj;
    }

    private static string RuntimeValueToJson(RuntimeValue value)
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

    private static string RuntimeObjectToJson(ObjectInstance obj)
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

    private static string RuntimeArrayToJson(List<RuntimeValue> array)
    {
        var items = array.Select(RuntimeValueToJson).ToList();
        return "[" + string.Join(",", items) + "]";
    }
}

public class WebMiddlewareChain
{
    private readonly List<WebMiddlewareRegistration> _middlewares = new();

    public int Count => _middlewares.Count;

    public void Add(FunctionValue function, IReadOnlyList<string>? exceptPaths = null)
    {
        _middlewares.Add(new WebMiddlewareRegistration(function, exceptPaths));
    }

    public void Add(string functionName, IReadOnlyList<string>? exceptPaths = null)
    {
        _middlewares.Add(new WebMiddlewareRegistration(functionName, exceptPaths));
    }

    public async Task<bool> ExecuteAsync(
        RequestContextInstance request,
        ResponseContextInstance response,
        Func<WebMiddlewareRegistration, List<RuntimeValue>, Task<RuntimeValue>> invokeMiddleware)
    {
        for (var i = 0; i < _middlewares.Count; i++)
        {
            var registration = _middlewares[i];
            if (registration.ShouldSkipPath(request.Path))
            {
                continue;
            }

            var continuePipeline = false;
            var nextCallback = new MiddlewareNextCallbackInstance(() => continuePipeline = true);
            var args = new List<RuntimeValue>
            {
                RuntimeValue.Object(request),
                RuntimeValue.Object(response),
                RuntimeValue.Function(new FunctionValue(null, null, false, null)
                {
                    BuiltInInstance = nextCallback,
                    BuiltInMethod = "invoke"
                })
            };

            await invokeMiddleware(registration, args);

            if (!continuePipeline)
            {
                return false;
            }
        }

        return true;
    }
}

public class RouteValidationError
{
    public string Location { get; }
    public string Field { get; }
    public string Message { get; }

    public RouteValidationError(string location, string field, string message)
    {
        Location = location;
        Field = field;
        Message = message;
    }
}

/// <summary>
/// HTTP/auth failures thrown from web helpers (e.g. missing JWT cookie).
/// Extends <see cref="RuntimeException"/> so Malda <c>try/catch</c> can redirect
/// (ASK <c>askRequireAuth</c>) instead of always surfacing a raw 401.
/// </summary>
public class WebRuntimeException : RuntimeException
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public List<RouteValidationError>? Details { get; }

    public WebRuntimeException(
        int statusCode,
        string errorCode,
        string message,
        List<RouteValidationError>? details = null,
        Exception? innerException = null) : base(message, null, null, null, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details;
    }
}

public static class WebRuntimeHelpers
{
    private sealed class CookieOptions
    {
        public bool HttpOnly { get; set; } = true;
        public bool Secure { get; set; } = true;
        public string SameSite { get; set; } = "Lax";
        public string Path { get; set; } = "/";
        public string? Domain { get; set; }
        public int? MaxAge { get; set; }
    }

    private sealed class ValidationRule
    {
        public string TypeName { get; set; } = "string";
        public bool Required { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public string? Pattern { get; set; }
    }

    public static bool IsRelativeRedirect(string location)
    {
        if (string.IsNullOrEmpty(location)) return false;
        if (location.StartsWith("//", StringComparison.Ordinal)) return false; // protocol-relative
        if (location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>
    /// Prepends <paramref name="pathBase"/> to app-relative redirect URLs (e.g. <c>/login</c>).
    /// If <paramref name="location"/> already begins with <paramref name="pathBase"/> followed by <c>/</c> or end-of-string,
    /// returns <paramref name="location"/> unchanged so hosts are not doubled (e.g. <c>/schoolprep/schoolprep/login</c>).
    /// </summary>
    public static string ApplyPathBaseToRelativeRedirect(string location, string? pathBase)
    {
        if (string.IsNullOrEmpty(location)) return location;
        if (string.IsNullOrEmpty(pathBase) || !IsRelativeRedirect(location))
            return location;
        if (location.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
        {
            if (location.Length == pathBase.Length)
                return location;
            if (location.Length > pathBase.Length && location[pathBase.Length] == '/')
                return location;
        }

        return pathBase + (location.StartsWith("/", StringComparison.Ordinal) ? location : "/" + location);
    }

    public static int NormalizeRedirectStatusCode(int? statusCode, string callerName, int defaultStatusCode = 303)
    {
        var normalized = statusCode ?? defaultStatusCode;
        if (normalized < 300 || normalized >= 400)
        {
            throw new Exception($"{callerName} redirect status must be a 3xx code.");
        }

        return normalized;
    }

    public static JsonObject CreateRedirectResponse(string location, int? statusCode = null)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new Exception("redirect location cannot be empty.");
        }

        var response = new JsonObject();
        response.Set("status", RuntimeValue.Integer(NormalizeRedirectStatusCode(statusCode, "redirect()")));

        var headers = new JsonObject();
        headers.Set("Location", RuntimeValue.String(location));
        response.Set("headers", RuntimeValue.Object(headers));
        response.Set("body", RuntimeValue.String(BuildRedirectHtml(location)));
        return response;
    }

    public static void ApplyRedirectLocation(HttpListenerResponse response, string location, string? pathBase = null)
    {
        response.RedirectLocation = ApplyPathBaseToRelativeRedirect(location, pathBase);
    }

    public static string BuildRedirectHtml(string location)
    {
        var escapedLocation = WebUtility.HtmlEncode(location ?? string.Empty);
        return $"<html><head><title>Redirecting</title><meta http-equiv=\"refresh\" content=\"0;url={escapedLocation}\"></head><body><p>Redirecting to <a href=\"{escapedLocation}\">{escapedLocation}</a>...</p></body></html>";
    }

    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string DefaultCsrfHeaderName = "X-CSRF-Token";
    public const string DefaultCsrfCookieName = "csrf_token";
    public const string DefaultSessionCookieName = "malda_session";
    public const string AuthVerifiedHeader = "X-Malda-Auth-Verified";
    public const string AuthSubjectHeader = "X-Malda-Auth-Sub";
    public const string LegacyAuthVerifiedHeader = "X-Auth-Verified";
    public const string LegacyAuthSubjectHeader = "X-Auth-Sub";
    public const string LegacyVerifiedSubjectHeader = "X-Verified-Sub";

    public static string ResolveCorrelationId(HttpListenerRequest request)
    {
        var headerValue = request.Headers[CorrelationIdHeader];
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.Trim();
        }

        return Guid.NewGuid().ToString("N");
    }

    public static void ApplyCorrelationId(HttpListenerResponse response, string correlationId)
    {
        response.Headers[CorrelationIdHeader] = correlationId;
    }

    public static string GenerateCsrfToken(string secret, int ttlSeconds = 7200)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new Exception("CSRF secret cannot be empty.");
        }

        if (ttlSeconds <= 0)
        {
            throw new Exception("CSRF token TTL must be greater than 0 seconds.");
        }

        var random = new byte[32];
        RandomNumberGenerator.Fill(random);
        var nonce = Base64UrlEncode(random);
        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;
        var payload = $"{nonce}.{expiresAt}";
        var signature = ComputeHmac(payload, secret);
        return $"{payload}.{signature}";
    }

    public static bool VerifyCsrfToken(string token, string secret)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var expiresAt))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt)
        {
            return false;
        }

        var payload = $"{parts[0]}.{parts[1]}";
        var expectedSignature = ComputeHmac(payload, secret);
        return FixedTimeEquals(expectedSignature, parts[2]);
    }

    public static string CreateSecureCookieValue(string plainValue, string secret, int? maxAgeSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new Exception("Secure cookie secret cannot be empty.");
        }

        var encodedValue = Base64UrlEncode(Encoding.UTF8.GetBytes(plainValue ?? string.Empty));
        var expiresAt = maxAgeSeconds.HasValue
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + maxAgeSeconds.Value
            : 0;
        var payload = $"{encodedValue}.{expiresAt}";
        var signature = ComputeHmac(payload, secret);
        return $"{payload}.{signature}";
    }

    public static bool TryReadSecureCookieValue(string cookieValue, string secret, out string plainValue)
    {
        plainValue = string.Empty;
        if (string.IsNullOrWhiteSpace(cookieValue) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var parts = cookieValue.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var payload = $"{parts[0]}.{parts[1]}";
        var expectedSignature = ComputeHmac(payload, secret);
        if (!FixedTimeEquals(expectedSignature, parts[2]))
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var expiresAt))
        {
            return false;
        }

        if (expiresAt > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt)
        {
            return false;
        }

        try
        {
            var decoded = Base64UrlDecode(parts[0]);
            plainValue = Encoding.UTF8.GetString(decoded);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string CreateCookieHeader(string name, string value, RuntimeValue options, bool useSecureDefaults)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new Exception("Cookie name cannot be empty.");
        }

        var parsedOptions = ParseCookieOptions(options, useSecureDefaults);
        var builder = new StringBuilder();
        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(value ?? string.Empty));
        builder.Append("; Path=");
        builder.Append(parsedOptions.Path);
        builder.Append("; SameSite=");
        builder.Append(parsedOptions.SameSite);

        if (!string.IsNullOrWhiteSpace(parsedOptions.Domain))
        {
            builder.Append("; Domain=");
            builder.Append(parsedOptions.Domain);
        }

        if (parsedOptions.MaxAge.HasValue)
        {
            builder.Append("; Max-Age=");
            builder.Append(parsedOptions.MaxAge.Value);
        }

        if (parsedOptions.HttpOnly)
        {
            builder.Append("; HttpOnly");
        }

        if (parsedOptions.Secure)
        {
            builder.Append("; Secure");
        }

        return builder.ToString();
    }

    public static bool RequiresCsrfValidation(string method)
    {
        return method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
               method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
               method.Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
               method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetHeaderValue(Dictionary<string, string> headers, string name, out string value)
    {
        if (headers.TryGetValue(name, out value!))
        {
            return true;
        }

        foreach (var kvp in headers)
        {
            if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public static string ResolveVerifiedSubjectFromHeaders(Dictionary<string, string> headers)
    {
        if (!TryReadVerifiedFlag(headers))
        {
            return string.Empty;
        }

        if (TryGetHeaderValue(headers, AuthSubjectHeader, out var canonicalSub) && !string.IsNullOrWhiteSpace(canonicalSub))
        {
            return canonicalSub.Trim();
        }

        if (TryGetHeaderValue(headers, LegacyAuthSubjectHeader, out var legacySub) && !string.IsNullOrWhiteSpace(legacySub))
        {
            return legacySub.Trim();
        }

        if (TryGetHeaderValue(headers, LegacyVerifiedSubjectHeader, out var fallbackSub) && !string.IsNullOrWhiteSpace(fallbackSub))
        {
            return fallbackSub.Trim();
        }

        return string.Empty;
    }

    public static string ComposeRoutePath(string routePath, string? groupPrefix, string? versionPrefix)
    {
        var segments = new List<string>();

        var normalizedGroup = NormalizeRouteSegment(groupPrefix);
        if (!string.IsNullOrEmpty(normalizedGroup))
        {
            segments.Add(normalizedGroup);
        }

        var normalizedVersion = NormalizeRouteSegment(versionPrefix);
        if (!string.IsNullOrEmpty(normalizedVersion))
        {
            segments.Add(normalizedVersion);
        }

        var normalizedRoute = NormalizeRouteSegment(routePath);
        if (!string.IsNullOrEmpty(normalizedRoute))
        {
            segments.Add(normalizedRoute);
        }

        if (segments.Count == 0)
        {
            return "/";
        }

        return "/" + string.Join("/", segments);
    }

    public static RuntimeValue CreateErrorRuntimeValue(
        int status,
        string errorCode,
        string message,
        string correlationId,
        List<RouteValidationError>? details = null,
        RuntimeValue? diagnostics = null)
    {
        var payload = new JsonObject();
        payload.Set("status", RuntimeValue.Integer(status));
        payload.Set("error", RuntimeValue.String(errorCode));
        payload.Set("message", RuntimeValue.String(message));
        payload.Set("correlationId", RuntimeValue.String(correlationId));

        if (details != null && details.Count > 0)
        {
            payload.Set("details", CreateValidationDetailsRuntimeValue(details));
        }

        if (diagnostics != null)
        {
            payload.Set("diagnostics", diagnostics);
        }

        return RuntimeValue.Object(payload);
    }

    public static bool TryGetStandardErrorPayload(RuntimeValue value, out JsonObject? payload, out int statusCode)
    {
        payload = null;
        statusCode = 0;

        if (value.Type != ValueType.Object || value.AsObject() is not JsonObject jsonObj)
        {
            return false;
        }

        var statusValue = jsonObj.Get("status", null);
        var errorValue = jsonObj.Get("error", null);
        var messageValue = jsonObj.Get("message", null);
        if (statusValue.Type != ValueType.Integer ||
            errorValue.Type != ValueType.String ||
            messageValue.Type != ValueType.String ||
            string.IsNullOrWhiteSpace(errorValue.AsString()) ||
            string.IsNullOrWhiteSpace(messageValue.AsString()))
        {
            return false;
        }

        payload = jsonObj;
        statusCode = statusValue.AsInteger();
        return true;
    }

    public static bool TryGetErrorMessage(RuntimeValue value, out string message)
    {
        message = string.Empty;
        if (value.Type != ValueType.Object || value.AsObject() is not JsonObject jsonObj)
        {
            return false;
        }

        var messageValue = jsonObj.Get("message", null);
        if (messageValue.Type != ValueType.String || string.IsNullOrWhiteSpace(messageValue.AsString()))
        {
            return false;
        }

        message = messageValue.AsString();
        return true;
    }

    public static RuntimeValue CreateValidationResultRuntimeValue(List<RouteValidationError>? errors)
    {
        var normalizedErrors = errors ?? new List<RouteValidationError>();
        var payload = new JsonObject();
        payload.Set("ok", RuntimeValue.Boolean(normalizedErrors.Count == 0));
        payload.Set("errors", CreateValidationDetailsRuntimeValue(normalizedErrors));
        if (normalizedErrors.Count > 0)
        {
            payload.Set("message", RuntimeValue.String(BuildValidationFailureMessage(normalizedErrors)));
        }

        return RuntimeValue.Object(payload);
    }

    public static RuntimeValue CreateValidationDetailsRuntimeValue(List<RouteValidationError>? details)
    {
        var items = new List<RuntimeValue>();
        if (details != null)
        {
            foreach (var detail in details)
            {
                var item = new JsonObject();
                item.Set("location", RuntimeValue.String(detail.Location));
                item.Set("field", RuntimeValue.String(detail.Field));
                item.Set("message", RuntimeValue.String(detail.Message));
                items.Add(RuntimeValue.Object(item));
            }
        }

        return RuntimeValue.Array(items);
    }

    public static string BuildValidationFailureMessage(List<RouteValidationError> errors)
    {
        if (errors == null || errors.Count == 0)
        {
            return "Request validation failed.";
        }

        var sorted = errors
            .OrderBy(e => e.Location, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Field, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Message, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parts = sorted
            .Select(e => $"{e.Location}.{e.Field} {e.Message}")
            .ToList();

        return $"Request validation failed ({sorted.Count} issue(s)): " + string.Join("; ", parts);
    }

    public static RuntimeValue CreateErrorFromException(
        Exception ex,
        string correlationId,
        out int statusCode,
        bool includeDiagnostics = false)
    {
        var normalized = RuntimeDiagnostics.Unwrap(ex);
        var diagnostics = includeDiagnostics
            ? CreateDiagnosticsRuntimeValue(normalized)
            : null;

        if (normalized is WebRuntimeException webRuntimeException)
        {
            statusCode = webRuntimeException.StatusCode;
            return CreateErrorRuntimeValue(
                webRuntimeException.StatusCode,
                webRuntimeException.ErrorCode,
                webRuntimeException.Message,
                correlationId,
                webRuntimeException.Details,
                diagnostics);
        }

        if (normalized is UnauthorizedAccessException unauthorizedAccessException)
        {
            statusCode = 401;
            return CreateErrorRuntimeValue(
                401,
                "Unauthorized",
                unauthorizedAccessException.Message,
                correlationId,
                diagnostics: diagnostics);
        }

        statusCode = 500;
        return CreateErrorRuntimeValue(
            500,
            "InternalServerError",
            "Internal server error",
            correlationId,
            diagnostics: diagnostics);
    }

    public static bool ShouldIncludeDebugDiagnostics()
    {
        var value = System.Environment.GetEnvironmentVariable("MALDA_WEB_DEBUG_ERRORS");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeValue CreateDiagnosticsRuntimeValue(Exception ex)
    {
        var info = RuntimeDiagnostics.CreateDiagnosticInfo(ex);
        var payload = new JsonObject();
        payload.Set("type", RuntimeValue.String(info.ExceptionType));
        payload.Set("message", RuntimeValue.String(info.Message));

        if (!string.IsNullOrWhiteSpace(info.File))
        {
            payload.Set("file", RuntimeValue.String(info.File));
        }

        if (info.Line.HasValue)
        {
            payload.Set("line", RuntimeValue.Integer(info.Line.Value));
        }

        if (!string.IsNullOrWhiteSpace(info.SourceLine))
        {
            payload.Set("sourceLine", RuntimeValue.String(info.SourceLine));
        }

        return RuntimeValue.Object(payload);
    }

    public static bool ValidateRequest(
        RuntimeValue schema,
        Dictionary<string, string> pathParams,
        Dictionary<string, string> queryParams,
        RuntimeValue? requestBody,
        out List<RouteValidationError> errors)
    {
        errors = new List<RouteValidationError>();

        if (schema.Type == ValueType.Null)
        {
            return true;
        }

        var normalizedSchema = NormalizeSchema(schema);
        if (normalizedSchema.Type != ValueType.Object || normalizedSchema.AsObject() is not JsonObject schemaObj)
        {
            throw new Exception("Validation schema must be a JSON object or a JSON string.");
        }

        ValidateSection("path", schemaObj, pathParams, requestBody, errors);
        ValidateSection("query", schemaObj, queryParams, requestBody, errors);
        ValidateBodySection(schemaObj, requestBody, errors);

        return errors.Count == 0;
    }

    private static RuntimeValue NormalizeSchema(RuntimeValue schema)
    {
        if (schema.Type == ValueType.String)
        {
            var text = schema.AsString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return RuntimeValue.Null();
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                return JsonElementToRuntimeValue(doc.RootElement);
            }
            catch
            {
                throw new Exception("Validation schema string must be valid JSON.");
            }
        }

        return schema;
    }

    private static void ValidateSection(
        string sectionName,
        JsonObject schemaObj,
        Dictionary<string, string> source,
        RuntimeValue? requestBody,
        List<RouteValidationError> errors)
    {
        var sectionValue = schemaObj.Get(sectionName, null);
        if (sectionValue.Type == ValueType.Null)
        {
            return;
        }

        if (sectionValue.Type != ValueType.Object || sectionValue.AsObject() is not JsonObject sectionObj)
        {
            throw new Exception($"Validation schema section '{sectionName}' must be an object.");
        }

        foreach (var kvp in sectionObj.GetProperties())
        {
            var field = kvp.Key;
            var rule = ParseRule(kvp.Value, sectionName, field);

            source.TryGetValue(field, out var rawTextValue);
            var hasValue = !string.IsNullOrEmpty(rawTextValue);

            if (!hasValue)
            {
                if (rule.Required)
                {
                    errors.Add(new RouteValidationError(sectionName, field, "Field is required."));
                }
                continue;
            }

            ValidateScalarValue(sectionName, field, RuntimeValue.String(rawTextValue!), rule, errors);
        }
    }

    private static void ValidateBodySection(JsonObject schemaObj, RuntimeValue? requestBody, List<RouteValidationError> errors)
    {
        var sectionValue = schemaObj.Get("body", null);
        if (sectionValue.Type == ValueType.Null)
        {
            return;
        }

        if (sectionValue.Type != ValueType.Object || sectionValue.AsObject() is not JsonObject sectionObj)
        {
            throw new Exception("Validation schema section 'body' must be an object.");
        }

        JsonObject? bodyObject = null;
        if (requestBody != null && requestBody.Type == ValueType.Object)
        {
            bodyObject = requestBody.AsObject() as JsonObject;
        }

        foreach (var kvp in sectionObj.GetProperties())
        {
            var field = kvp.Key;
            var rule = ParseRule(kvp.Value, "body", field);

            RuntimeValue value = RuntimeValue.Null();
            if (bodyObject != null)
            {
                value = bodyObject.Get(field, null);
            }

            if (value.Type == ValueType.Null)
            {
                if (rule.Required)
                {
                    errors.Add(new RouteValidationError("body", field, "Field is required."));
                }
                continue;
            }

            ValidateScalarValue("body", field, value, rule, errors);
        }
    }

    private static ValidationRule ParseRule(RuntimeValue ruleValue, string location, string field)
    {
        if (ruleValue.Type == ValueType.String)
        {
            return ParseRuleString(ruleValue.AsString());
        }

        if (ruleValue.Type == ValueType.Object && ruleValue.AsObject() is JsonObject ruleObj)
        {
            var rule = new ValidationRule();

            var typeValue = ruleObj.Get("type", null);
            if (typeValue.Type == ValueType.String)
            {
                rule.TypeName = typeValue.AsString();
            }

            var requiredValue = ruleObj.Get("required", null);
            if (requiredValue.Type == ValueType.Boolean)
            {
                rule.Required = requiredValue.AsBoolean();
            }

            rule.Min = RuntimeValueToDouble(ruleObj.Get("min", null));
            rule.Max = RuntimeValueToDouble(ruleObj.Get("max", null));
            rule.MinLength = RuntimeValueToInt(ruleObj.Get("minLength", null));
            rule.MaxLength = RuntimeValueToInt(ruleObj.Get("maxLength", null));

            var patternValue = ruleObj.Get("pattern", null);
            if (patternValue.Type == ValueType.String)
            {
                rule.Pattern = patternValue.AsString();
            }

            return rule;
        }

        throw new Exception($"Validation rule for {location}.{field} must be a string or object.");
    }

    private static ValidationRule ParseRuleString(string ruleText)
    {
        var rule = new ValidationRule();
        var parts = ruleText.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (part.Equals("required", StringComparison.OrdinalIgnoreCase))
            {
                rule.Required = true;
                continue;
            }

            if (part.StartsWith("min=", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(part["min=".Length..], out var min))
                {
                    rule.Min = min;
                }
                continue;
            }

            if (part.StartsWith("max=", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(part["max=".Length..], out var max))
                {
                    rule.Max = max;
                }
                continue;
            }

            if (part.StartsWith("minLength=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part["minLength=".Length..], out var minLength))
                {
                    rule.MinLength = minLength;
                }
                continue;
            }

            if (part.StartsWith("maxLength=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part["maxLength=".Length..], out var maxLength))
                {
                    rule.MaxLength = maxLength;
                }
                continue;
            }

            if (part.StartsWith("pattern=", StringComparison.OrdinalIgnoreCase))
            {
                rule.Pattern = part["pattern=".Length..];
                continue;
            }

            rule.TypeName = part;
        }

        return rule;
    }

    private static void ValidateScalarValue(string location, string field, RuntimeValue value, ValidationRule rule, List<RouteValidationError> errors)
    {
        var typeName = rule.TypeName.Trim().ToLowerInvariant();
        if (typeName.Length == 0)
        {
            typeName = "string";
        }

        if (!TryValidateType(value, typeName, out var numericValue, out var stringValue))
        {
            errors.Add(new RouteValidationError(location, field, $"Expected type '{rule.TypeName}'."));
            return;
        }

        if (numericValue.HasValue)
        {
            if (rule.Min.HasValue && numericValue.Value < rule.Min.Value)
            {
                errors.Add(new RouteValidationError(location, field, $"Value must be >= {rule.Min.Value}."));
            }
            if (rule.Max.HasValue && numericValue.Value > rule.Max.Value)
            {
                errors.Add(new RouteValidationError(location, field, $"Value must be <= {rule.Max.Value}."));
            }
        }

        if (stringValue != null)
        {
            if (rule.MinLength.HasValue && stringValue.Length < rule.MinLength.Value)
            {
                errors.Add(new RouteValidationError(location, field, $"Length must be >= {rule.MinLength.Value}."));
            }
            if (rule.MaxLength.HasValue && stringValue.Length > rule.MaxLength.Value)
            {
                errors.Add(new RouteValidationError(location, field, $"Length must be <= {rule.MaxLength.Value}."));
            }
            if (!string.IsNullOrEmpty(rule.Pattern) && !Regex.IsMatch(stringValue, rule.Pattern))
            {
                errors.Add(new RouteValidationError(location, field, "Value does not match required pattern."));
            }
        }
    }

    private static bool TryValidateType(RuntimeValue value, string typeName, out double? numericValue, out string? stringValue)
    {
        numericValue = null;
        stringValue = null;

        switch (typeName)
        {
            case "string":
                if (value.Type == ValueType.String)
                {
                    stringValue = value.AsString();
                    return true;
                }
                return false;
            case "int":
            case "integer":
                if (TryCoerceToInt(value, out var intValue))
                {
                    numericValue = intValue;
                    return true;
                }
                return false;
            case "float":
            case "double":
            case "number":
                if (TryCoerceToDouble(value, out var floatValue))
                {
                    numericValue = floatValue;
                    return true;
                }
                return false;
            case "bool":
            case "boolean":
                if (value.Type == ValueType.Boolean)
                {
                    return true;
                }
                if (value.Type == ValueType.String)
                {
                    var s = value.AsString();
                    return s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                           s.Equals("false", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            case "object":
                return value.Type == ValueType.Object;
            case "array":
                return value.Type == ValueType.Array;
            default:
                // Unknown type behaves as string for backward compatibility.
                if (value.Type == ValueType.String)
                {
                    stringValue = value.AsString();
                    return true;
                }
                return false;
        }
    }

    private static bool TryCoerceToInt(RuntimeValue value, out int intValue)
    {
        intValue = 0;
        if (value.Type == ValueType.Integer)
        {
            intValue = value.AsInteger();
            return true;
        }
        if (value.Type == ValueType.String)
        {
            return int.TryParse(value.AsString(), out intValue);
        }
        return false;
    }

    private static bool TryCoerceToDouble(RuntimeValue value, out double doubleValue)
    {
        doubleValue = 0;
        if (value.Type == ValueType.Integer)
        {
            doubleValue = value.AsInteger();
            return true;
        }
        if (value.Type == ValueType.Float)
        {
            doubleValue = value.AsFloat();
            return true;
        }
        if (value.Type == ValueType.String)
        {
            return double.TryParse(value.AsString(), out doubleValue);
        }
        return false;
    }

    private static double? RuntimeValueToDouble(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger(),
            ValueType.Float => value.AsFloat(),
            _ => null
        };
    }

    private static int? RuntimeValueToInt(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger(),
            _ => null
        };
    }

    private static string NormalizeRouteSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        var trimmed = segment.Trim();
        trimmed = trimmed.Trim('/');
        return trimmed;
    }

    private static RuntimeValue JsonElementToRuntimeValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonObjectToRuntimeValue(element),
            JsonValueKind.Array => JsonArrayToRuntimeValue(element),
            JsonValueKind.String => RuntimeValue.String(element.GetString() ?? string.Empty),
            JsonValueKind.Number => element.TryGetInt32(out var intVal)
                ? RuntimeValue.Integer(intVal)
                : RuntimeValue.Float(element.GetDouble()),
            JsonValueKind.True => RuntimeValue.Boolean(true),
            JsonValueKind.False => RuntimeValue.Boolean(false),
            JsonValueKind.Null => RuntimeValue.Null(),
            _ => RuntimeValue.Null()
        };
    }

    private static RuntimeValue JsonObjectToRuntimeValue(JsonElement element)
    {
        var jsonObj = new JsonObject();
        foreach (var prop in element.EnumerateObject())
        {
            jsonObj.Set(prop.Name, JsonElementToRuntimeValue(prop.Value));
        }
        return RuntimeValue.Object(jsonObj);
    }

    private static RuntimeValue JsonArrayToRuntimeValue(JsonElement element)
    {
        var list = new List<RuntimeValue>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(JsonElementToRuntimeValue(item));
        }
        return RuntimeValue.Array(list);
    }

    private static CookieOptions ParseCookieOptions(RuntimeValue options, bool useSecureDefaults)
    {
        var parsed = new CookieOptions
        {
            HttpOnly = useSecureDefaults,
            Secure = useSecureDefaults,
            SameSite = useSecureDefaults ? "Lax" : "None",
            Path = "/"
        };

        if (options.Type != ValueType.Object || options.AsObject() is not JsonObject obj)
        {
            return parsed;
        }

        var httpOnly = obj.Get("httpOnly", null);
        if (httpOnly.Type == ValueType.Boolean)
        {
            parsed.HttpOnly = httpOnly.AsBoolean();
        }

        var secure = obj.Get("secure", null);
        if (secure.Type == ValueType.Boolean)
        {
            parsed.Secure = secure.AsBoolean();
        }

        var sameSite = obj.Get("sameSite", null);
        if (sameSite.Type == ValueType.String)
        {
            parsed.SameSite = NormalizeSameSite(sameSite.AsString());
        }

        var path = obj.Get("path", null);
        if (path.Type == ValueType.String && !string.IsNullOrWhiteSpace(path.AsString()))
        {
            parsed.Path = path.AsString();
        }

        var domain = obj.Get("domain", null);
        if (domain.Type == ValueType.String && !string.IsNullOrWhiteSpace(domain.AsString()))
        {
            parsed.Domain = domain.AsString();
        }

        var maxAge = obj.Get("maxAge", null);
        if (maxAge.Type == ValueType.Integer)
        {
            parsed.MaxAge = maxAge.AsInteger();
        }

        return parsed;
    }

    private static string NormalizeSameSite(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("strict", StringComparison.OrdinalIgnoreCase))
            return "Strict";
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "None";
        return "Lax";
    }

    private static string ComputeHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(signature);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        var mod4 = normalized.Length % 4;
        if (mod4 == 2) normalized += "==";
        else if (mod4 == 3) normalized += "=";
        else if (mod4 != 0) throw new FormatException("Invalid base64url input.");
        return Convert.FromBase64String(normalized);
    }

    private static bool TryReadVerifiedFlag(Dictionary<string, string> headers)
    {
        if (TryGetHeaderValue(headers, AuthVerifiedHeader, out var canonicalVerified))
        {
            return IsTrueHeaderValue(canonicalVerified);
        }

        if (TryGetHeaderValue(headers, LegacyAuthVerifiedHeader, out var legacyVerified))
        {
            return IsTrueHeaderValue(legacyVerified);
        }

        return false;
    }

    private static bool IsTrueHeaderValue(string raw)
    {
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}
