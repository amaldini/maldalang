// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;

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
