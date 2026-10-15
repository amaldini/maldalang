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

public class ResponseContextInstance : ObjectInstance
{
    private RuntimeValue _body = RuntimeValue.Null();
    private bool _bodyIsBase64;
    private readonly List<string> _setCookieHeaders = new();
    private Action? _httpFlush;

    public int StatusCode { get; private set; } = 200;
    public string ContentType { get; private set; } = "application/json; charset=utf-8";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsCommitted { get; private set; }
    public bool IsFlushed { get; private set; }
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

        if (name is "flushed" or "isFlushed")
            return RuntimeValue.Boolean(IsFlushed);

        if (name is "status" or "json" or "text" or "html" or "redirect" or "header" or "cookie" or "clearCookie" or "send" or "sendBase64" or "setContentType" or "fragment" or "flush")
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
                EnsureNotFlushed();
                StatusCode = args[0].AsInteger();
                HasStatusOverride = true;
                return RuntimeValue.Object(this);

            case "json":
                if (args.Count != 1)
                    throw new Exception("json() expects 1 argument");
                EnsureNotFlushed();
                ContentType = "application/json; charset=utf-8";
                _body = args[0];
                _bodyIsBase64 = false;
                CommitAndFlush();
                return RuntimeValue.Object(this);

            case "text":
                if (args.Count != 1)
                    throw new Exception("text() expects 1 argument");
                EnsureNotFlushed();
                ContentType = "text/plain; charset=utf-8";
                _body = args[0].Type == ValueType.String ? args[0] : RuntimeValue.String(args[0].ToString());
                _bodyIsBase64 = false;
                CommitAndFlush();
                return RuntimeValue.Object(this);

            case "html":
                if (args.Count != 1)
                    throw new Exception("html() expects 1 argument");
                EnsureNotFlushed();
                ContentType = "text/html; charset=utf-8";
                _body = args[0].Type == ValueType.String ? args[0] : RuntimeValue.String(args[0].ToString());
                _bodyIsBase64 = false;
                CommitAndFlush();
                return RuntimeValue.Object(this);

            case "redirect":
                if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
                    throw new Exception("redirect() expects location string and optional status integer");
                var redirectStatus = args.Count == 2 && args[1].Type == ValueType.Integer
                    ? args[1].AsInteger()
                    : (int?)null;
                EnsureNotFlushed();
                StatusCode = WebRuntimeHelpers.NormalizeRedirectStatusCode(redirectStatus, "res.redirect()");
                HasStatusOverride = true;
                Headers["Location"] = args[0].AsString();
                ContentType = "text/html; charset=utf-8";
                _body = RuntimeValue.String(WebRuntimeHelpers.BuildRedirectHtml(args[0].AsString()));
                _bodyIsBase64 = false;
                CommitAndFlush();
                return RuntimeValue.Object(this);

            case "header":
                if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
                    throw new Exception("header() expects 2 string arguments");
                EnsureNotFlushed();
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
                EnsureNotFlushed();

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
                EnsureNotFlushed();

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
                EnsureNotFlushed();
                ContentType = args[0].AsString();
                return RuntimeValue.Object(this);

            case "send":
                if (args.Count != 1)
                    throw new Exception("send() expects 1 argument");
                EnsureNotFlushed();
                _body = args[0];
                _bodyIsBase64 = false;
                CommitAndFlush();
                return RuntimeValue.Object(this);

            case "sendBase64":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("sendBase64() expects 1 string argument");
                EnsureNotFlushed();
                try
                {
                    Convert.FromBase64String(args[0].AsString());
                }
                catch (FormatException)
                {
                    throw new Exception("sendBase64() expects a base64 string");
                }
                _body = args[0];
                _bodyIsBase64 = true;
                CommitAndFlush();
                return RuntimeValue.Object(this);

            case "fragment":
                if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
                    throw new Exception("fragment() expects targetId and html string");
                EnsureNotFlushed();
                Headers["X-Malda-Fragment"] = "true";
                Headers["X-Malda-Fragment-Target"] = args[0].AsString();
                ContentType = "text/html; charset=utf-8";
                _body = args[1];
                _bodyIsBase64 = false;
                CommitAndFlush();
                return RuntimeValue.Object(this);

            case "flush":
                if (args.Count != 0)
                    throw new Exception("flush() expects 0 arguments");
                if (IsFlushed)
                    return RuntimeValue.Object(this);
                CommitAndFlush();
                return RuntimeValue.Object(this);

            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }

    /// <summary>
    /// When bound, <c>res.json</c>/<c>send</c>/<c>html</c>/<c>fragment</c> write and close
    /// the HTTP response immediately so the handler can keep running.
    /// </summary>
    public void BindListener(
        HttpListenerResponse listener,
        HttpListenerRequest request,
        RequestContextInstance requestContext,
        string? pathBase,
        bool isSecure)
    {
        _httpFlush = () =>
        {
            if (IsFlushed)
                return;

            SessionRuntime.CommitSession(requestContext, this, isSecure);
            ApplyTo(listener, pathBase, request);
            try
            {
                listener.OutputStream.Flush();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException)
            {
            }

            WebRuntimeHelpers.TryCloseHttpListenerResponse(listener);
            IsFlushed = true;
        };
    }

    private void EnsureNotFlushed()
    {
        if (IsFlushed)
            throw new Exception("response already sent");
    }

    private void CommitAndFlush()
    {
        IsCommitted = true;
        _httpFlush?.Invoke();
    }

    public void AddSetCookieHeader(string cookieHeader)
    {
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            _setCookieHeaders.Add(cookieHeader);
        }
    }

    public void ApplyTo(HttpListenerResponse response, string? pathBase = null, HttpListenerRequest? request = null)
    {
        response.StatusCode = StatusCode;
        ApplyHeadersTo(response, pathBase);
        response.StatusCode = StatusCode;

        if (!HasBody)
        {
            response.ContentLength64 = 0;
            return;
        }

        var bytes = EncodeBodyBytes();
        response.ContentType = ContentType;
        WebRuntimeHelpers.WriteHttpListenerBody(response, bytes, request);
    }

    public void ApplyHeadersTo(HttpListenerResponse response, string? pathBase = null)
    {
        foreach (var header in Headers)
        {
            if (header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                WebRuntimeHelpers.ApplyRedirectLocation(response, header.Value, pathBase);
            }
            else if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                     header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                // Entity framing is derived from the encoded body (and HEAD / no-body statuses).
                continue;
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

    private byte[] EncodeBodyBytes()
    {
        if (_bodyIsBase64)
        {
            if (_body.Type != ValueType.String)
                throw new Exception("sendBase64() expects a base64 string");
            try
            {
                return Convert.FromBase64String(_body.AsString());
            }
            catch (FormatException)
            {
                throw new Exception("sendBase64() expects a base64 string");
            }
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

        return Encoding.UTF8.GetBytes(bodyText);
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
