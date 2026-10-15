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

public partial class HttpServerInstance
{
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

    private void WriteJsonRuntimeValue(HttpListenerResponse response, RuntimeValue payload, HttpListenerRequest? request = null)
    {
        var body = RuntimeValueToJson(payload);
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = "application/json; charset=utf-8";
        WebRuntimeHelpers.WriteHttpListenerBody(response, bytes, request);
    }

    private void SerializeHtmlResponse(
        HttpListenerResponse response,
        RuntimeValue result,
        HttpListenerRequest request,
        string path,
        RequestContextInstance? requestContext = null,
        ResponseContextInstance? pipelineResponse = null)
    {
        if (result.Type == ValueType.Object && result.AsObject() is ResponseContextInstance responseContext)
        {
            if (requestContext != null)
            {
                SessionRuntime.CommitSession(requestContext, responseContext, IsRequestSecure(request));
            }
            responseContext.ApplyTo(response, _pathBase, request);
            return;
        }

        if (requestContext != null && pipelineResponse != null)
        {
            SessionRuntime.CommitSession(requestContext, pipelineResponse, IsRequestSecure(request));
            if (pipelineResponse.HasHeaders)
            {
                pipelineResponse.ApplyHeadersTo(response, _pathBase);
            }
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
        WebRuntimeHelpers.WriteHttpListenerBody(response, bytes, request);
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
    
    private bool TryServeStaticFile(string path, HttpListenerResponse response, HttpListenerRequest? request = null)
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
                response.StatusCode = 200;
                WebRuntimeHelpers.WriteHttpListenerBody(response, cachedFile.Content, request);
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
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        WebRuntimeHelpers.WriteHttpListenerBody(response, bytes, request);
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

    private void HandleError(HttpListenerResponse response, Exception ex, string correlationId, HttpListenerRequest request)
    {
        var normalized = RuntimeDiagnostics.PreserveContext(ex, _interpreter);
        if (normalized is WebRuntimeException webRuntimeException && webRuntimeException.StatusCode < 500)
        {
            // Expected client/auth failures — keep console readable.
            Console.Error.WriteLine(
                $"HttpServer [{correlationId}] {webRuntimeException.StatusCode} {webRuntimeException.ErrorCode}: {webRuntimeException.Message}");
        }
        else
        {
            Console.Error.WriteLine($"HttpServer Error [{correlationId}]");
            Console.Error.WriteLine(RuntimeDiagnostics.FormatExceptionForConsole(ex, _interpreter));
        }

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
}
