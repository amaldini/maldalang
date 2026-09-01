// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Globalization;
using System.Text.Json;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Expressions;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Resolves the optional third argument of <c>@Tool</c> / <c>@MCPTool</c> to a JSON Schema
/// object: a registered schema or sum-type name, a JSON object string, or an already-built object.
/// </summary>
public static class ToolSchemaResolver
{
    public static bool LooksLikeJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = text.TrimStart();
        return trimmed.Length > 0 && trimmed[0] == '{';
    }

    /// <summary>
    /// Evaluates a decorator argument that must be a literal or a bare schema/sum-type name.
    /// </summary>
    public static RuntimeValue EvaluateNameOrLiteral(Expression expr, Interpreter? interpreter = null)
    {
        if (expr is IdentifierExpression identifier)
            return RuntimeValue.String(identifier.Name);

        if (expr is LiteralExpression literal)
        {
            if (interpreter != null)
                return interpreter.RuntimeValueFromLiteral(literal);
            return LiteralToRuntimeValue(literal);
        }

        throw new Exception(
            "Decorator argument must be a literal or a schema/sum-type name.");
    }

    /// <summary>
    /// Resolves a <c>@Tool</c> / <c>@MCPTool</c> schema argument to a JSON-schema object.
    /// Returns <c>null</c> when the argument is omitted/empty so callers can auto-generate.
    /// </summary>
    public static RuntimeValue? Resolve(RuntimeValue? argument)
    {
        if (argument == null || argument.Type == ValueType.Null)
            return null;

        if (argument.Type == ValueType.Object)
            return argument;

        if (argument.Type != ValueType.String)
        {
            throw new Exception(
                "@Tool / @MCPTool schema argument must be a registered schema or sum-type name, a JSON schema object string, or omitted.");
        }

        var text = argument.AsString().Trim();
        if (text.Length == 0)
            return null;

        if (LooksLikeJsonObject(text))
            return ParseJsonObject(text);

        try
        {
            return SchemaRegistry.ResolveSchemaArgument(RuntimeValue.String(text));
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"Unknown schema '{text}' for @Tool / @MCPTool. Declare a schema or sum type with that name, or pass a JSON schema object string. {ex.Message}");
        }
    }

    public static JsonElement ToJsonElement(RuntimeValue schema)
    {
        var clr = ToClr(schema);
        return JsonDocument.Parse(JsonSerializer.Serialize(clr)).RootElement.Clone();
    }

    /// <summary>
    /// Validates LLM/MCP tool arguments against an attached JSON Schema.
    /// Returns <c>true</c> when <paramref name="schema"/> is missing so omitted
    /// third arguments stay advertise-only (auto-generated string properties).
    /// </summary>
    public static bool TryValidateArgs(RuntimeValue? schema, RuntimeValue arguments, out string error)
    {
        error = "";
        if (schema == null || schema.Type != ValueType.Object)
            return true;

        if (arguments.Type != ValueType.Object)
        {
            error = "Tool arguments must be an object.";
            return false;
        }

        return TypedPromptValidator.TryValidateReturnType(arguments, schema, out error);
    }

    public static RuntimeValue EmptyArgsObject() => RuntimeValue.Object(new JsonObject());

    public static RuntimeValue CallResult(bool ok, RuntimeValue? data, string? error)
    {
        var result = new JsonObject();
        result.Set("ok", RuntimeValue.Boolean(ok));
        if (ok)
            result.Set("data", data ?? RuntimeValue.Null());
        else
            result.Set("error", RuntimeValue.String(error ?? "Tool arguments failed schema."));
        return RuntimeValue.Object(result);
    }

    public static string AgentError(string error) =>
        $"Error: tool arguments failed schema: {error}";

    public static RuntimeValue FromJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return RuntimeValue.Integer(intVal);
                if (element.TryGetDouble(out var doubleVal))
                    return RuntimeValue.Float(doubleVal);
                return RuntimeValue.Float(0);
            case JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            case JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            case JsonValueKind.Null:
                return RuntimeValue.Null();
            case JsonValueKind.Array:
                var array = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                    array.Add(FromJsonElement(item));
                return RuntimeValue.Array(array);
            case JsonValueKind.Object:
                var jsonObj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                    jsonObj.Set(prop.Name, FromJsonElement(prop.Value));
                return RuntimeValue.Object(jsonObj);
            default:
                return RuntimeValue.Null();
        }
    }

    private static RuntimeValue ParseJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new Exception(
                    "@Tool / @MCPTool JSON schema argument must be a JSON object.");
            }

            return FromJsonElement(doc.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            throw new Exception(
                $"@Tool / @MCPTool schema argument is not valid JSON: {ex.Message}");
        }
    }

    private static RuntimeValue LiteralToRuntimeValue(LiteralExpression literal)
    {
        if (literal.Value == null)
            return RuntimeValue.Null();
        return literal.Value switch
        {
            string s => RuntimeValue.String(s),
            bool b => RuntimeValue.Boolean(b),
            int i => RuntimeValue.Integer(i),
            long l => RuntimeValue.Integer((int)l),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            _ => RuntimeValue.String(Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? "")
        };
    }

    private static object? ToClr(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.String:
                return value.AsString();
            case ValueType.Integer:
                return value.AsInteger();
            case ValueType.Float:
                return value.AsFloat();
            case ValueType.Boolean:
                return value.AsBoolean();
            case ValueType.Null:
                return null;
            case ValueType.Array:
                return value.AsArray().Select(ToClr).ToList();
            case ValueType.Object:
                var dict = new Dictionary<string, object?>();
                var obj = value.AsObject();
                if (obj is JsonObject jsonObj)
                {
                    foreach (var kvp in jsonObj.GetProperties())
                        dict[kvp.Key] = ToClr(kvp.Value);
                }
                else
                {
                    foreach (var key in obj.GetAllKeys())
                        dict[key] = ToClr(obj.Get(key));
                }

                return dict;
            default:
                return value.ToString();
        }
    }
}
