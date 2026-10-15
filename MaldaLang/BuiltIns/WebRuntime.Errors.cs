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

public static partial class WebRuntimeHelpers
{
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

    /// <summary>
    /// Converts a C# transpiler return value into a <see cref="RuntimeValue"/>.
    /// Object literals emit as <c>Dictionary&lt;string, object?&gt;</c>; RestServer and
    /// HttpServer both need that shape to serialize JSON object bodies.
    /// </summary>
    public static RuntimeValue ConvertTranspiledResultToRuntimeValue(object? value)
    {
        if (value == null)
        {
            return RuntimeValue.Null();
        }

        if (value is RuntimeValue runtimeValue)
        {
            return runtimeValue;
        }

        if (value is DBNull)
        {
            return RuntimeValue.Null();
        }

        if (value is int i) return RuntimeValue.Integer(i);
        if (value is long l) return RuntimeValue.Integer((int)l);
        if (value is short sh) return RuntimeValue.Integer(sh);
        if (value is byte bt) return RuntimeValue.Integer(bt);
        if (value is double d) return RuntimeValue.Float(d);
        if (value is float f) return RuntimeValue.Float(f);
        if (value is decimal dm) return RuntimeValue.Float((double)dm);
        if (value is string s) return RuntimeValue.String(s);
        if (value is bool b) return RuntimeValue.Boolean(b);
        if (value is ObjectInstance oi) return RuntimeValue.Object(oi);

        if (value is Dictionary<string, object?> nullableDict)
        {
            return RuntimeValue.Object(DictionaryToJsonObject(nullableDict));
        }

        if (value is Dictionary<string, object> dict)
        {
            var nullable = new Dictionary<string, object?>(dict.Count);
            foreach (var kvp in dict)
            {
                nullable[kvp.Key] = kvp.Value;
            }
            return RuntimeValue.Object(DictionaryToJsonObject(nullable));
        }

        if (value is List<object> list)
        {
            return RuntimeValue.Array(list.Select(ConvertTranspiledResultToRuntimeValue).ToList());
        }

        if (value is IDictionary dictionary)
        {
            var jsonObj = new JsonObject();
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = entry.Key == null ? "" : entry.Key.ToString() ?? "";
                jsonObj.Set(key, ConvertTranspiledResultToRuntimeValue(entry.Value));
            }
            return RuntimeValue.Object(jsonObj);
        }

        if (value is IEnumerable sequence && value is not string)
        {
            var items = new List<RuntimeValue>();
            foreach (var item in sequence)
            {
                items.Add(ConvertTranspiledResultToRuntimeValue(item));
            }
            return RuntimeValue.Array(items);
        }

        return RuntimeValue.Null();
    }

    private static JsonObject DictionaryToJsonObject(Dictionary<string, object?> source)
    {
        var jsonObj = new JsonObject();
        foreach (var kvp in source)
        {
            jsonObj.Set(kvp.Key, ConvertTranspiledResultToRuntimeValue(kvp.Value));
        }
        return jsonObj;
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
}
