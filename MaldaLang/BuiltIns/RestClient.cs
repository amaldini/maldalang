// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public class RestClientInstance : ObjectInstance
{
    private readonly HttpClient _httpClient;
    private string _baseUrl;
    private int _timeout;
    private readonly Dictionary<string, string> _defaultHeaders;
    private string? _authType;
    private string? _authCredentials;
    
    public RestClientInstance(string? baseUrl = null, int? timeout = null) : base(null)
    {
        _baseUrl = baseUrl ?? "";
        _timeout = timeout ?? 30000; // Default 30 seconds
        _defaultHeaders = new Dictionary<string, string>();
        
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMilliseconds(_timeout);
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "baseUrl")
            return RuntimeValue.String(_baseUrl);
        if (name == "timeout")
            return RuntimeValue.Integer(_timeout);
        
        // Handle method access
        if (name == "get" || name == "post" || name == "put" || name == "delete" || name == "patch" ||
            name == "setHeader" || name == "setAuth" || name == "setTimeout" || name == "setBaseUrl")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on RestClient.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "get":
                return GetRequest(args);
            case "post":
                return PostRequest(args);
            case "put":
                return PutRequest(args);
            case "delete":
                return DeleteRequest(args);
            case "patch":
                return PatchRequest(args);
            case "setHeader":
                if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
                    throw new Exception("setHeader() expects 2 string arguments: (name, value)");
                _defaultHeaders[args[0].AsString()] = args[1].AsString();
                return RuntimeValue.Null();
            case "setAuth":
                if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
                    throw new Exception("setAuth() expects 2 string arguments: (type, credentials)");
                _authType = args[0].AsString();
                _authCredentials = args[1].AsString();
                return RuntimeValue.Null();
            case "setTimeout":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("setTimeout() expects 1 integer argument (milliseconds)");
                _timeout = args[0].AsInteger();
                _httpClient.Timeout = TimeSpan.FromMilliseconds(_timeout);
                return RuntimeValue.Null();
            case "setBaseUrl":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setBaseUrl() expects 1 string argument");
                _baseUrl = args[0].AsString();
                return RuntimeValue.Null();
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    private RuntimeValue GetRequest(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("get() expects at least 1 string argument: (url, headers?, queryParams?)");
        
        var url = args[0].AsString();
        var headers = args.Count > 1 && args[1].Type == ValueType.Object ? args[1] : null;
        var queryParams = args.Count > 2 && args[2].Type == ValueType.Object ? args[2] : null;
        
        return MakeRequest(HttpMethod.Get, url, null, headers, queryParams);
    }
    
    private RuntimeValue PostRequest(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("post() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var url = args[0].AsString();
        var body = args.Count > 1 ? args[1] : null;
        var headers = args.Count > 2 && args[2].Type == ValueType.Object ? args[2] : null;
        var queryParams = args.Count > 3 && args[3].Type == ValueType.Object ? args[3] : null;
        
        return MakeRequest(HttpMethod.Post, url, body, headers, queryParams);
    }
    
    private RuntimeValue PutRequest(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("put() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var url = args[0].AsString();
        var body = args.Count > 1 ? args[1] : null;
        var headers = args.Count > 2 && args[2].Type == ValueType.Object ? args[2] : null;
        var queryParams = args.Count > 3 && args[3].Type == ValueType.Object ? args[3] : null;
        
        return MakeRequest(HttpMethod.Put, url, body, headers, queryParams);
    }
    
    private RuntimeValue DeleteRequest(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("delete() expects at least 1 string argument: (url, headers?, queryParams?)");
        
        var url = args[0].AsString();
        var headers = args.Count > 1 && args[1].Type == ValueType.Object ? args[1] : null;
        var queryParams = args.Count > 2 && args[2].Type == ValueType.Object ? args[2] : null;
        
        return MakeRequest(HttpMethod.Delete, url, null, headers, queryParams);
    }
    
    private RuntimeValue PatchRequest(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("patch() expects at least 1 string argument: (url, body?, headers?, queryParams?)");
        
        var url = args[0].AsString();
        var body = args.Count > 1 ? args[1] : null;
        var headers = args.Count > 2 && args[2].Type == ValueType.Object ? args[2] : null;
        var queryParams = args.Count > 3 && args[3].Type == ValueType.Object ? args[3] : null;
        
        return MakeRequest(new HttpMethod("PATCH"), url, body, headers, queryParams);
    }
    
    private RuntimeValue MakeRequest(HttpMethod method, string url, RuntimeValue? body, RuntimeValue? headers, RuntimeValue? queryParams)
    {
        try
        {
            // Build full URL
            var fullUrl = BuildUrl(url, queryParams);
            
            // Create request
            var request = new HttpRequestMessage(method, fullUrl);
            
            // Add default headers
            foreach (var header in _defaultHeaders)
            {
                request.Headers.Add(header.Key, header.Value);
            }
            
            // Add authentication
            if (!string.IsNullOrEmpty(_authType) && !string.IsNullOrEmpty(_authCredentials))
            {
                if (_authType.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authCredentials);
                }
                else if (_authType.Equals("Basic", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = Encoding.UTF8.GetBytes(_authCredentials);
                    var base64 = Convert.ToBase64String(bytes);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64);
                }
            }
            
            // Add custom headers
            if (headers != null && headers.Type == ValueType.Object)
            {
                var headersObj = headers.AsObject();
                var allKeys = headersObj.GetAllKeys();
                foreach (var key in allKeys)
                {
                    var value = headersObj.Get(key, null);
                    if (value.Type == ValueType.String)
                    {
                        try
                        {
                            request.Headers.Add(key, value.AsString());
                        }
                        catch
                        {
                            // Some headers need to be set via Content property
                            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                // Will be set when we set content
                            }
                        }
                    }
                }
            }
            
            // Add body for POST, PUT, PATCH
            if (body != null && (method == HttpMethod.Post || method == HttpMethod.Put || method.Method == "PATCH"))
            {
                string bodyJson;
                if (body.Type == ValueType.String)
                {
                    bodyJson = body.AsString();
                }
                else if (body.Type == ValueType.Object)
                {
                    bodyJson = SerializeObject(body.AsObject());
                }
                else
                {
                    bodyJson = JsonSerializer.Serialize(ConvertToJsonValue(body));
                }
                
                var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                
                // Override Content-Type if specified in headers
                if (headers != null && headers.Type == ValueType.Object)
                {
                    var headersObj = headers.AsObject();
                    var contentType = headersObj.Get("Content-Type", null);
                    if (contentType.Type == ValueType.String)
                    {
                        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType.AsString());
                    }
                }
                
                request.Content = content;
            }
            
            // Make synchronous request
            var response = _httpClient.Send(request);
            var responseContent = response.Content.ReadAsStringAsync().Result;
            
            // Parse response
            return BuildResponse(response, responseContent);
        }
        catch (Exception ex)
        {
            return BuildErrorResponse(ex);
        }
    }
    
    private string BuildUrl(string url, RuntimeValue? queryParams)
    {
        var fullUrl = url;
        
        // Prepend base URL if url is relative
        if (!string.IsNullOrEmpty(_baseUrl) && !url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            var baseUrl = _baseUrl.TrimEnd('/');
            var path = url.StartsWith("/") ? url : "/" + url;
            fullUrl = baseUrl + path;
        }
        
        // Add query parameters
        if (queryParams != null && queryParams.Type == ValueType.Object)
        {
            var queryObj = queryParams.AsObject();
            var allKeys = queryObj.GetAllKeys();
            var queryParts = new List<string>();
            
            foreach (var key in allKeys)
            {
                var value = queryObj.Get(key, null);
                var valueStr = value.Type == ValueType.String ? value.AsString() : 
                              value.Type == ValueType.Integer ? value.AsInteger().ToString() :
                              value.Type == ValueType.Float ? value.AsFloat().ToString() :
                              value.Type == ValueType.Boolean ? value.AsBoolean().ToString().ToLower() :
                              value.AsString();
                queryParts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(valueStr)}");
            }
            
            if (queryParts.Count > 0)
            {
                var separator = fullUrl.Contains("?") ? "&" : "?";
                fullUrl += separator + string.Join("&", queryParts);
            }
        }
        
        return fullUrl;
    }
    
    private RuntimeValue BuildResponse(HttpResponseMessage response, string responseContent)
    {
        var responseObj = new JsonObject();
        
        // Status
        responseObj.Set("status", RuntimeValue.Integer((int)response.StatusCode));
        responseObj.Set("statusText", RuntimeValue.String(response.ReasonPhrase ?? ""));
        responseObj.Set("ok", RuntimeValue.Boolean(response.IsSuccessStatusCode));
        
        // Headers
        var headersObj = new JsonObject();
        foreach (var header in response.Headers)
        {
            headersObj.Set(header.Key, RuntimeValue.String(string.Join(", ", header.Value)));
        }
        if (response.Content.Headers != null)
        {
            foreach (var header in response.Content.Headers)
            {
                headersObj.Set(header.Key, RuntimeValue.String(string.Join(", ", header.Value)));
            }
        }
        responseObj.Set("headers", RuntimeValue.Object(headersObj));
        
        // Body - try to parse as JSON, otherwise return as string
        RuntimeValue bodyValue;
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType.Contains("json") && !string.IsNullOrEmpty(responseContent))
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(responseContent);
                bodyValue = ParseJsonElement(jsonDoc.RootElement);
            }
            catch
            {
                bodyValue = RuntimeValue.String(responseContent);
            }
        }
        else
        {
            bodyValue = RuntimeValue.String(responseContent);
        }
        responseObj.Set("body", bodyValue);
        
        return RuntimeValue.Object(responseObj);
    }
    
    private RuntimeValue BuildErrorResponse(Exception ex)
    {
        var errorObj = new JsonObject();
        errorObj.Set("error", RuntimeValue.String(ex.Message));
        errorObj.Set("ok", RuntimeValue.Boolean(false));
        
        // Try to extract status code from HttpRequestException
        if (ex is HttpRequestException httpEx && httpEx.Data.Contains("StatusCode"))
        {
            var statusCode = httpEx.Data["StatusCode"];
            if (statusCode is int status)
            {
                errorObj.Set("status", RuntimeValue.Integer(status));
            }
        }
        else
        {
            errorObj.Set("status", RuntimeValue.Integer(0));
        }
        
        return RuntimeValue.Object(errorObj);
    }
    
    private RuntimeValue ParseJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ParseJsonObject(element),
            JsonValueKind.Array => ParseJsonArray(element),
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
    
    private RuntimeValue ParseJsonObject(JsonElement element)
    {
        var obj = new JsonObject();
        foreach (var prop in element.EnumerateObject())
        {
            obj.Set(prop.Name, ParseJsonElement(prop.Value));
        }
        return RuntimeValue.Object(obj);
    }
    
    private RuntimeValue ParseJsonArray(JsonElement element)
    {
        var list = new List<RuntimeValue>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ParseJsonElement(item));
        }
        return RuntimeValue.Array(list);
    }
    
    private string SerializeObject(ObjectInstance obj)
    {
        var dict = new Dictionary<string, object?>();
        var allKeys = obj.GetAllKeys();
        foreach (var key in allKeys)
        {
            var value = obj.Get(key, null);
            dict[key] = ConvertToJsonValue(value);
        }
        return JsonSerializer.Serialize(dict);
    }
    
    private object? ConvertToJsonValue(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.String => value.AsString(),
            ValueType.Integer => value.AsInteger(),
            ValueType.Float => value.AsFloat(),
            ValueType.Boolean => value.AsBoolean(),
            ValueType.Null => null,
            ValueType.Object => ConvertObjectToDict(value.AsObject()),
            ValueType.Array => value.AsArray().Select(ConvertToJsonValue).ToList(),
            _ => value.AsString()
        };
    }
    
    private Dictionary<string, object?> ConvertObjectToDict(ObjectInstance obj)
    {
        var dict = new Dictionary<string, object?>();
        var allKeys = obj.GetAllKeys();
        foreach (var key in allKeys)
        {
            var value = obj.Get(key, null);
            dict[key] = ConvertToJsonValue(value);
        }
        return dict;
    }
    
    private string? GetStringProperty(ObjectInstance obj, string name)
    {
        try
        {
            var value = obj.Get(name, null);
            return value.Type == ValueType.String ? value.AsString() : null;
        }
        catch
        {
            return null;
        }
    }
}
