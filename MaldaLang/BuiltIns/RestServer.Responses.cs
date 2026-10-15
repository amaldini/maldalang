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

public partial class RestServerInstance
{
    private void SerializeResponse(
        HttpListenerResponse response,
        RuntimeValue result,
        RequestContextInstance? requestContext = null,
        ResponseContextInstance? pipelineResponse = null,
        HttpListenerRequest? request = null)
    {
        if (result.Type == ValueType.Object && result.AsObject() is ResponseContextInstance responseContext)
        {
            if (requestContext != null)
            {
                SessionRuntime.CommitSession(requestContext, responseContext, request?.IsSecureConnection ?? false);
            }
            responseContext.ApplyTo(response, request: request);
            return;
        }

        if (requestContext != null && pipelineResponse != null)
        {
            SessionRuntime.CommitSession(requestContext, pipelineResponse, request?.IsSecureConnection ?? false);
            if (pipelineResponse.HasHeaders)
            {
                pipelineResponse.ApplyHeadersTo(response);
            }
        }

        if (WebRuntimeHelpers.TryGetStandardErrorPayload(result, out _, out var errorStatusCode))
        {
            response.StatusCode = errorStatusCode;
            WriteJsonResponse(response, result);
            return;
        }

        // Check if result has a status property (status+data return)
        if (result.Type == ValueType.Object)
        {
            var obj = result.AsObject();
            if (obj is JsonObject jsonObj)
            {
                try
                {
                    var statusValue = jsonObj.Get("status", null);
                    if (statusValue != null && statusValue.Type == ValueType.Integer)
                    {
                        response.StatusCode = statusValue.AsInteger();
                        // Remove status from response body
                        var responseObj = new Dictionary<string, RuntimeValue>();
                        var props = jsonObj.GetProperties();
                        foreach (var kvp in props)
                        {
                            if (kvp.Key != "status")
                                responseObj[kvp.Key] = kvp.Value;
                        }
                        WriteJsonResponse(response, responseObj);
                        return;
                    }
                }
                catch
                {
                    // Not a status object, continue with normal serialization
                }
            }
        }
        
        // Simple return - serialize to JSON
        if (result.Type == ValueType.String)
        {
            // Return string as-is
            var bytes = Encoding.UTF8.GetBytes(result.AsString());
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.StatusCode = 200;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        else
        {
            // Serialize to JSON
            response.StatusCode = 200;
            WriteJsonResponse(response, result);
        }
    }
    
    private void WriteJsonResponse(HttpListenerResponse response, object data)
    {
        string json;
        if (data is RuntimeValue rv)
        {
            json = RuntimeValueToJson(rv);
        }
        else
        {
            json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
            { 
                WriteIndented = false 
            });
        }
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
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
        return "{}";
    }
    
    private string RuntimeArrayToJson(List<RuntimeValue> array)
    {
        var items = array.Select(RuntimeValueToJson).ToList();
        return "[" + string.Join(",", items) + "]";
    }

    private void HandleError(HttpListenerResponse response, Exception ex, string correlationId)
    {
        var normalized = RuntimeDiagnostics.PreserveContext(ex, _interpreter);
        if (normalized is WebRuntimeException webRuntimeException && webRuntimeException.StatusCode < 500)
        {
            Console.Error.WriteLine(
                $"REST Server [{correlationId}] {webRuntimeException.StatusCode} {webRuntimeException.ErrorCode}: {webRuntimeException.Message}");
        }
        else
        {
            Console.Error.WriteLine($"REST Server Error [{correlationId}]");
            Console.Error.WriteLine(RuntimeDiagnostics.FormatExceptionForConsole(ex, _interpreter));
        }

        var payload = WebRuntimeHelpers.CreateErrorFromException(
            normalized,
            correlationId,
            out var statusCode,
            WebRuntimeHelpers.ShouldIncludeDebugDiagnostics());
        response.StatusCode = statusCode;
        WriteJsonResponse(response, payload);
    }
}
