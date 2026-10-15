// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInCreateWebSearchTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createWebSearchTool", args, 0, 0);
        return BuiltInTools.CreateWebSearchTool();
    }

    private static RuntimeValue BuiltInCreateWebFetchTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createWebFetchTool", args, 0, 1, "httpCap?");
        CapabilityToken? httpCap = null;
        if (args.Count > 0 && args[0].Type != ValueType.Null)
        {
            httpCap = CapStdLib.RequireToken(args[0], CapabilityToken.KindHttpGet, "createWebFetchTool");
        }
        return BuiltInTools.CreateWebFetchTool(httpCap);
    }

    private static RuntimeValue BuiltInWebFetch(List<RuntimeValue> args)
    {
        BuiltInArity.Require("webFetch", args, 1, 4, "urlOrToken, pathOrMaxBytes?, maxBytes?, timeoutMs?");
        string? pathOrUrl = null;
        var rest = 1;
        if (CapStdLib.TryGetToken(args[0], out _))
        {
            if (args.Count > 1 && args[1].Type == ValueType.String)
            {
                pathOrUrl = args[1].AsString();
                rest = 2;
            }
            else if (args.Count > 1 && args[1].Type == ValueType.Null)
            {
                rest = 2;
            }
        }
        else if (args[0].Type != ValueType.String)
        {
            throw new Exception("webFetch() url must be a string or an httpGet capability token");
        }

        var url = CapStdLib.ResolveWebFetchUrl(args[0], pathOrUrl, "webFetch");
        var obj = new JsonObject();
        obj.Set("url", RuntimeValue.String(url));
        if (args.Count > rest)
            obj.Set("maxBytes", args[rest]);
        if (args.Count > rest + 1)
            obj.Set("timeoutMs", args[rest + 1]);
        return ExecuteWebFetch(RuntimeValue.Object(obj));
    }

    public const int WebFetchDefaultMaxBytes = 100_000;
    public const int WebFetchHardCapMaxBytes = 500_000;
    public const int WebFetchDefaultTimeoutMs = 15_000;
    public const int WebFetchHardCapTimeoutMs = 60_000;

    /// <summary>
    /// Fetch an HTTP(S) URL via <see cref="RestClientInstance"/> (same path as <c>httpGet</c>)
    /// and reshape the result for the <c>web_fetch</c> agent tool.
    /// </summary>
    public static RuntimeValue ExecuteWebFetch(RuntimeValue arguments)
    {
        string requestedUrl = "";
        try
        {
            if (arguments.Type != ValueType.Object)
                return WebFetchResult(false, 0, "", "", false, "url parameter required");

            var argsObj = arguments.AsObject();
            var urlVal = argsObj.Get("url", null);
            if (urlVal == null || urlVal.Type != ValueType.String)
                return WebFetchResult(false, 0, "", "", false, "url parameter required");

            requestedUrl = urlVal.AsString() ?? "";
            if (string.IsNullOrWhiteSpace(requestedUrl))
                return WebFetchResult(false, 0, requestedUrl, "", false, "url cannot be empty");

            if (!TryValidateWebFetchUrl(requestedUrl, out var urlError))
                return WebFetchResult(false, 0, requestedUrl, "", false, urlError);

            var maxBytes = ClampWebFetchInt(
                TryGetOptionalInt(argsObj, "maxBytes"),
                WebFetchDefaultMaxBytes,
                1,
                WebFetchHardCapMaxBytes);
            var timeoutMs = ClampWebFetchInt(
                TryGetOptionalInt(argsObj, "timeoutMs"),
                WebFetchDefaultTimeoutMs,
                1,
                WebFetchHardCapTimeoutMs);

            var client = new RestClientInstance();
            client.CallMethod("setTimeout", new List<RuntimeValue> { RuntimeValue.Integer(timeoutMs) });
            var httpResult = client.CallMethod("get", new List<RuntimeValue> { RuntimeValue.String(requestedUrl) });

            return ReshapeHttpGetToWebFetch(httpResult, requestedUrl, maxBytes);
        }
        catch (Exception ex)
        {
            return WebFetchResult(false, 0, requestedUrl, "", false, ex.Message);
        }
    }

    private static RuntimeValue ReshapeHttpGetToWebFetch(RuntimeValue httpResult, string requestedUrl, int maxBytes)
    {
        if (httpResult.Type != ValueType.Object)
        {
            var fallback = httpResult.Type == ValueType.String ? httpResult.AsString() : httpResult.ToString();
            return WebFetchResult(false, 0, requestedUrl, "", false, fallback);
        }

        var httpObj = httpResult.AsObject();
        var okVal = httpObj.Get("ok", null);
        var ok = okVal != null && okVal.Type == ValueType.Boolean && okVal.AsBoolean();

        var statusVal = httpObj.Get("status", null);
        var status = statusVal != null && statusVal.Type == ValueType.Integer
            ? statusVal.AsInteger()
            : 0;

        var bodyVal = httpObj.Get("body", null);
        var content = BodyToWebFetchContent(bodyVal);

        var truncated = content.Length > maxBytes;
        if (truncated)
            content = content.Substring(0, maxBytes);

        string? error = null;
        var errorVal = httpObj.Get("error", null);
        if (errorVal != null && errorVal.Type == ValueType.String)
        {
            var errText = errorVal.AsString();
            if (!string.IsNullOrEmpty(errText))
                error = errText;
        }

        return WebFetchResult(ok, status, requestedUrl, content, truncated, error);
    }

    private static string BodyToWebFetchContent(RuntimeValue? bodyVal)
    {
        if (bodyVal == null || bodyVal.Type == ValueType.Null)
            return "";
        if (bodyVal.Type == ValueType.String)
            return bodyVal.AsString() ?? "";
        if (bodyVal.Type == ValueType.Object || bodyVal.Type == ValueType.Array)
            return SerializeToJson(bodyVal);
        return SerializeToJson(bodyVal);
    }

    private static bool TryValidateWebFetchUrl(string url, out string error)
    {
        error = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "url must be an absolute http:// or https:// URL";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = $"url scheme '{uri.Scheme}' is not allowed; use http:// or https://";
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            error = "url is missing a host";
            return false;
        }

        return true;
    }

    private static int? TryGetOptionalInt(ObjectInstance obj, string name)
    {
        try
        {
            var val = obj.Get(name, null);
            if (val == null || val.Type == ValueType.Null)
                return null;
            if (val.Type == ValueType.Integer)
                return val.AsInteger();
            if (val.Type == ValueType.Float)
                return (int)val.AsFloat();
        }
        catch
        {
            // missing or non-numeric optional
        }

        return null;
    }

    private static int ClampWebFetchInt(int? value, int defaultValue, int min, int max)
    {
        var n = value ?? defaultValue;
        if (n < min)
            return defaultValue;
        return n > max ? max : n;
    }

    private static RuntimeValue WebFetchResult(
        bool ok,
        int status,
        string url,
        string content,
        bool truncated,
        string? error)
    {
        var obj = new JsonObject();
        obj.Set("ok", RuntimeValue.Boolean(ok));
        obj.Set("status", RuntimeValue.Integer(status));
        obj.Set("url", RuntimeValue.String(url ?? ""));
        obj.Set("content", RuntimeValue.String(content ?? ""));
        obj.Set("truncated", RuntimeValue.Boolean(truncated));
        if (!string.IsNullOrEmpty(error))
            obj.Set("error", RuntimeValue.String(error));
        return RuntimeValue.Object(obj);
    }
}
