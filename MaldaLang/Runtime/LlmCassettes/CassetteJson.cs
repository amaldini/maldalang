// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.LlmCassettes;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public static class CassetteJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal static JsonElement ToElement(RuntimeValue value)
    {
        using var doc = JsonDocument.Parse(ToJsonString(value));
        return doc.RootElement.Clone();
    }

    internal static string ToJsonString(RuntimeValue value) =>
        JsonSerializer.Serialize(ToClr(value), Options);

    internal static RuntimeValue FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => RuntimeValue.Null(),
        JsonValueKind.True => RuntimeValue.Boolean(true),
        JsonValueKind.False => RuntimeValue.Boolean(false),
        JsonValueKind.Number when element.TryGetInt32(out var i) => RuntimeValue.Integer(i),
        JsonValueKind.Number => RuntimeValue.Float(element.GetDouble()),
        JsonValueKind.String => RuntimeValue.String(element.GetString() ?? ""),
        JsonValueKind.Array => RuntimeValue.Array(element.EnumerateArray().Select(FromElement).ToList()),
        JsonValueKind.Object => ObjectFromElement(element),
        _ => RuntimeValue.Null()
    };

    private static RuntimeValue ObjectFromElement(JsonElement element)
    {
        var obj = new JsonObject();
        foreach (var prop in element.EnumerateObject())
            obj.Set(prop.Name, FromElement(prop.Value));
        return RuntimeValue.Object(obj);
    }

    private static object? ToClr(RuntimeValue value) => value.Type switch
    {
        ValueType.Null => null,
        ValueType.Boolean => value.AsBoolean(),
        ValueType.Integer => value.AsInteger(),
        ValueType.Float => value.AsFloat(),
        ValueType.String => Redactor.Redact(value.AsString()),
        ValueType.Array => value.AsArray().Select(ToClr).ToList(),
        ValueType.Object => ObjectToClr(value.AsObject()),
        ValueType.Variant => VariantToClr(value.AsVariant()),
        _ => value.ToString()
    };

    private static Dictionary<string, object?> ObjectToClr(ObjectInstance instance)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (instance is JsonObject json)
        {
            foreach (var key in json.GetAllKeys())
            {
                if (Redactor.IsSecretKey(key))
                    dict[key] = "[REDACTED]";
                else
                    dict[key] = ToClr(json.Get(key));
            }

            return dict;
        }

        dict["type"] = instance.GetType().Name;
        return dict;
    }

    private static Dictionary<string, object?> VariantToClr(VariantValue variant)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tag"] = variant.Tag,
            ["payload"] = variant.Payload.Select(ToClr).ToList()
        };
    }
}
