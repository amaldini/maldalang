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
    private static RuntimeValue BuiltInHttpGet(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpGet() expects at least 1 string argument: (url, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("get", args);
    }
    
    private static RuntimeValue BuiltInHttpPost(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpPost() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("post", args);
    }
    
    private static RuntimeValue BuiltInHttpPut(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpPut() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("put", args);
    }
    
    private static RuntimeValue BuiltInHttpDelete(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpDelete() expects at least 1 string argument: (url, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("delete", args);
    }
    
    private static RuntimeValue BuiltInHttpPatch(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("httpPatch() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var client = new RestClientInstance();
        return client.CallMethod("patch", args);
    }

    private static RuntimeValue BuiltInHttpBearerToken(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("httpBearerToken() expects 1 argument: (request)");

        if (!TryGetObjectPropertyValue(args[0], "headers", out var headersValue))
            return RuntimeValue.String("");

        var authHeader = GetCaseInsensitiveStringValue(headersValue, "Authorization");
        if (string.IsNullOrWhiteSpace(authHeader))
            return RuntimeValue.String("");

        var pieces = authHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length < 2 || !string.Equals(pieces[0], "bearer", StringComparison.OrdinalIgnoreCase))
            return RuntimeValue.String("");

        return RuntimeValue.String(pieces[1]);
    }

    private static RuntimeValue BuiltInHttpCookieToken(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[1].Type != ValueType.String)
            throw new Exception("httpCookieToken() expects request, cookieName, optional cookieSecret");

        var cookieName = args[1].AsString();
        if (string.IsNullOrWhiteSpace(cookieName))
            return RuntimeValue.String("");

        if (!TryGetObjectPropertyValue(args[0], "cookies", out var cookiesValue))
            return RuntimeValue.String("");

        var rawCookie = GetCaseInsensitiveStringValue(cookiesValue, cookieName);
        if (string.IsNullOrWhiteSpace(rawCookie))
            return RuntimeValue.String("");

        var decoded = BuiltInUrlDecode(new List<RuntimeValue> { RuntimeValue.String(rawCookie) });
        var decodedText = decoded.Type == ValueType.String ? decoded.AsString() : rawCookie;
        if (args.Count == 3 && args[2].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[2].AsString()))
        {
            var secureValue = BuiltInReadSecureCookie(new List<RuntimeValue>
            {
                RuntimeValue.String(decodedText),
                args[2]
            });

            if (secureValue.Type == ValueType.Null)
                return RuntimeValue.String("");

            return RuntimeValue.String(secureValue.ToString());
        }

        return RuntimeValue.String(decodedText);
    }

    private static RuntimeValue BuiltInHttpAuthToken(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("httpAuthToken() expects request and optional options object");

        var options = args.Count == 2 ? CoerceRuntimeObjectToJsonObject(args[1]) : null;
        var allowBearer = options == null || GetBooleanOptionFromObject(options, "allowBearer", true);
        var allowCookie = options == null || GetBooleanOptionFromObject(options, "allowCookie", true);
        var allowBody = options == null || GetBooleanOptionFromObject(options, "allowBody", true);
        var cookieName = options == null ? string.Empty : GetStringOptionFromObject(options, "cookieName", string.Empty);
        var cookieSecret = options == null ? string.Empty : GetStringOptionFromObject(options, "cookieSecret", string.Empty);
        var bodyKey = options == null ? "auth" : GetStringOptionFromObject(options, "bodyKey", "auth");

        if (allowBearer)
        {
            var token = BuiltInHttpBearerToken(new List<RuntimeValue> { args[0] });
            if (token.Type == ValueType.String && !string.IsNullOrWhiteSpace(token.AsString()))
                return token;
        }

        if (allowCookie && !string.IsNullOrWhiteSpace(cookieName))
        {
            var cookieArgs = new List<RuntimeValue> { args[0], RuntimeValue.String(cookieName) };
            if (!string.IsNullOrWhiteSpace(cookieSecret))
                cookieArgs.Add(RuntimeValue.String(cookieSecret));
            var token = BuiltInHttpCookieToken(cookieArgs);
            if (token.Type == ValueType.String && !string.IsNullOrWhiteSpace(token.AsString()))
                return token;
        }

        if (allowBody)
        {
            if (TryGetObjectPropertyValue(args[0], "body", out var bodyValue))
            {
                var bodyToken = GetCaseInsensitiveStringValue(bodyValue, bodyKey);
                if (!string.IsNullOrWhiteSpace(bodyToken))
                    return RuntimeValue.String(bodyToken);
            }            
        }

        return RuntimeValue.String("");
    }

    private static bool TryGetObjectPropertyValue(RuntimeValue source, string key, out RuntimeValue value)
    {
        value = RuntimeValue.Null();
        if (source.Type != ValueType.Object)
            return false;

        var obj = source.AsObject();
        if (obj is JsonObject jsonObj)
        {
            var direct = jsonObj.Get(key, null);
            if (direct.Type != ValueType.Null)
            {
                value = direct;
                return true;
            }

            foreach (var kvp in jsonObj.GetProperties())
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        if (obj is DictionaryInstance dict)
        {
            if (dict.TryGetEntry(key, out var direct))
            {
                value = direct;
                return true;
            }

            foreach (var kvp in dict.GetEntries())
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        try
        {
            var direct = obj.Get(key, null);
            if (direct.Type != ValueType.Null)
            {
                value = direct;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string GetCaseInsensitiveStringValue(RuntimeValue source, string key)
    {
        if (!TryGetObjectPropertyValue(source, key, out var value) || value.Type == ValueType.Null)
            return string.Empty;

        return value.Type == ValueType.String ? value.AsString() : value.ToString();
    }
    
    private static RuntimeValue BuiltInWebSearch(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("webSearch() expects at least 1 string argument: (query, apiKey?)");
        
        var query = args[0].AsString();
        string? apiKey = null;
        if (args.Count > 1 && args[1].Type == ValueType.String)
            apiKey = args[1].AsString();
        if (string.IsNullOrEmpty(apiKey))
            apiKey = System.Environment.GetEnvironmentVariable("BRAVE_SEARCH_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            var configVal = BuiltInGetMaldaConfig(new List<RuntimeValue>());
            if (configVal.Type == ValueType.Object)
            {
                var cfg = configVal.AsObject();
                try
                {
                    var tools = cfg.Get("tools", null);
                    if (tools?.Type == ValueType.Object)
                    {
                        var web = tools.AsObject().Get("web", null);
                        if (web?.Type == ValueType.Object)
                        {
                            var search = web.AsObject().Get("search", null);
                            if (search?.Type == ValueType.Object)
                            {
                                var keyVal = search.AsObject().Get("apiKey", null);
                                if (keyVal?.Type == ValueType.String && !string.IsNullOrWhiteSpace(keyVal.AsString()))
                                    apiKey = keyVal.AsString();
                            }
                        }
                    }
                }
                catch { }
            }
        }
        if (string.IsNullOrEmpty(apiKey))
            throw new Exception("webSearch() requires apiKey argument, BRAVE_SEARCH_API_KEY, or tools.web.search.apiKey in ~/.malda/config.json");
        
        var url = "https://api.search.brave.com/res/v1/web/search?q=" + Uri.EscapeDataString(query);
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Subscription-Token", apiKey);
            var response = httpClient.Send(request);
            var responseContent = response.Content.ReadAsStringAsync().Result;
            
            if (!response.IsSuccessStatusCode)
            {
                var errorObj = new JsonObject();
                errorObj.Set("ok", RuntimeValue.Boolean(false));
                errorObj.Set("error", RuntimeValue.String($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"));
                return RuntimeValue.Object(errorObj);
            }
            
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            
            var resultObj = new JsonObject();
            resultObj.Set("ok", RuntimeValue.Boolean(true));
            
            var resultsList = new List<RuntimeValue>();
            if (root.TryGetProperty("web", out var webEl) &&
                webEl.ValueKind == JsonValueKind.Object &&
                webEl.TryGetProperty("results", out var resultsEl) &&
                resultsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsEl.EnumerateArray())
                {
                    var itemObj = new JsonObject();
                    itemObj.Set("title", RuntimeValue.String(item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""));
                    itemObj.Set("url", RuntimeValue.String(item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : ""));
                    itemObj.Set("description", RuntimeValue.String(item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""));
                    resultsList.Add(RuntimeValue.Object(itemObj));
                }
            }
            resultObj.Set("results", RuntimeValue.Array(resultsList));
            
            var moreAvailable = false;
            if (root.TryGetProperty("query", out var queryEl) &&
                queryEl.ValueKind == JsonValueKind.Object &&
                queryEl.TryGetProperty("more_results_available", out var moreEl))
                moreAvailable = moreEl.ValueKind == JsonValueKind.True;
            resultObj.Set("moreResultsAvailable", RuntimeValue.Boolean(moreAvailable));
            
            return RuntimeValue.Object(resultObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("ok", RuntimeValue.Boolean(false));
            errorObj.Set("error", RuntimeValue.String(ex.Message));
            return RuntimeValue.Object(errorObj);
        }
    }
}
