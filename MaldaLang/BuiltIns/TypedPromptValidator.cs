// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public static class TypedPromptValidator
{
    public static bool TryExtractJsonCandidate(string content, out string jsonCandidate, out string error)
    {
        jsonCandidate = "";
        error = "";

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "LLM returned empty content.";
            return false;
        }

        var trimmed = content.Trim();
        if (trimmed.Contains("```", StringComparison.Ordinal))
        {
            var jsonFenceStart = trimmed.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (jsonFenceStart >= 0)
            {
                var afterFence = trimmed.IndexOf('\n', jsonFenceStart);
                if (afterFence > 0)
                {
                    var jsonFenceEnd = trimmed.IndexOf("```", afterFence + 1, StringComparison.Ordinal);
                    if (jsonFenceEnd > afterFence)
                        trimmed = trimmed.Substring(afterFence + 1, jsonFenceEnd - afterFence - 1).Trim();
                }
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Could not extract JSON object from LLM response.";
            return false;
        }

        jsonCandidate = trimmed;
        return true;
    }

    public static bool TryParseJson(string json, out RuntimeValue parsed, out string error)
    {
        parsed = RuntimeValue.Null();
        error = "";

        try
        {
            parsed = BuiltInFunctions.CallBuiltIn(
                "parseJSON",
                new List<RuntimeValue> { RuntimeValue.String(json) },
                null!);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    public static bool TryValidateReturnType(RuntimeValue value, string returnType, Interpreter? interpreter, out string error)
    {
        error = "";
        if (!TypedPromptSchemaResolver.TryResolve(returnType, interpreter, out var schema, out var schemaError))
        {
            error = schemaError;
            return false;
        }

        if (!TryValidateAgainstSchema(value, schema, "$", out error))
            return false;

        return true;
    }

    /// <summary>
    /// Validates a value against a pre-resolved schema (e.g. from transpiled code where no interpreter is available).
    /// </summary>
    public static bool TryValidateReturnType(RuntimeValue value, RuntimeValue schema, out string error)
    {
        error = "";
        return TryValidateAgainstSchema(value, schema, "$", out error);
    }

    public static string BuildRepairInstruction(string returnType, string validationError)
    {
        return
            "Your previous response did not match the expected typed output.\n" +
            $"Expected return type: {returnType}\n" +
            $"Validation errors: {validationError}\n" +
            "Return ONLY valid JSON with no markdown or explanation.";
    }

    /// <summary>
    /// Wraps a resolved JSON schema into OpenAI response_format structure.
    /// The schema from TypedPromptSchemaResolver is { type, properties?, required? };
    /// OpenAI expects it nested under json_schema.schema.
    /// </summary>
    public static RuntimeValue BuildResponseFormat(RuntimeValue resolvedSchema)
    {
        var jsonSchema = new JsonObject();
        jsonSchema.Set("name", RuntimeValue.String("typed_prompt_response"));
        jsonSchema.Set("strict", RuntimeValue.Boolean(true));
        jsonSchema.Set("schema", resolvedSchema);

        var wrapper = new JsonObject();
        wrapper.Set("type", RuntimeValue.String("json_schema"));
        wrapper.Set("json_schema", RuntimeValue.Object(jsonSchema));
        return RuntimeValue.Object(wrapper);
    }

    private static bool TryValidateAgainstSchema(RuntimeValue value, RuntimeValue schema, string path, out string error)
    {
        error = "";
        if (schema.Type != ValueType.Object || schema.AsObject() is not JsonObject schemaObj)
        {
            error = "Invalid schema object.";
            return false;
        }

        var typeNameVal = schemaObj.Get("type");
        var typeName = typeNameVal.Type == ValueType.String ? typeNameVal.AsString() : "object";

        switch (typeName)
        {
            case "string":
                if (value.Type != ValueType.String)
                {
                    error = $"{path} must be string, got {value.Type}.";
                    return false;
                }
                return true;
            case "integer":
                if (value.Type != ValueType.Integer)
                {
                    error = $"{path} must be integer, got {value.Type}.";
                    return false;
                }
                return true;
            case "number":
                if (value.Type != ValueType.Integer && value.Type != ValueType.Float)
                {
                    error = $"{path} must be number, got {value.Type}.";
                    return false;
                }
                return true;
            case "boolean":
                if (value.Type != ValueType.Boolean)
                {
                    error = $"{path} must be boolean, got {value.Type}.";
                    return false;
                }
                return true;
            case "array":
                return ValidateArray(value, schemaObj, path, out error);
            case "object":
                return ValidateObject(value, schemaObj, path, out error);
            default:
                error = $"Unknown schema type '{typeName}' at {path}.";
                return false;
        }
    }

    private static bool ValidateArray(RuntimeValue value, JsonObject schemaObj, string path, out string error)
    {
        error = "";
        if (value.Type != ValueType.Array)
        {
            error = $"{path} must be array, got {value.Type}.";
            return false;
        }

        var itemsSchema = schemaObj.Get("items");
        if (itemsSchema.Type == ValueType.Null)
            return true;

        var items = value.AsArray();
        for (int i = 0; i < items.Count; i++)
        {
            if (!TryValidateAgainstSchema(items[i], itemsSchema, $"{path}[{i}]", out error))
                return false;
        }

        return true;
    }

    private static bool ValidateObject(RuntimeValue value, JsonObject schemaObj, string path, out string error)
    {
        error = "";
        if (value.Type != ValueType.Object)
        {
            error = $"{path} must be object, got {value.Type}.";
            return false;
        }

        var obj = value.AsObject();
        if (obj is JsonObject jsonObj)
            return ValidateObjectProperties(key => jsonObj.Get(key), schemaObj, path, out error);

        if (obj is DictionaryInstance dict)
            return ValidateObjectProperties(
                key => dict.TryGetEntry(key, out var entry) ? entry : RuntimeValue.Null(),
                schemaObj,
                path,
                out error);

        error = $"{path} must be a JSON or dictionary object.";
        return false;
    }

    private static bool ValidateObjectProperties(
        Func<string, RuntimeValue> getProperty,
        JsonObject schemaObj,
        string path,
        out string error)
    {
        error = "";

        var requiredVal = schemaObj.Get("required");
        if (requiredVal.Type == ValueType.Array)
        {
            foreach (var requiredFieldVal in requiredVal.AsArray())
            {
                if (requiredFieldVal.Type != ValueType.String)
                    continue;
                var requiredField = requiredFieldVal.AsString();
                var requiredValue = getProperty(requiredField);
                if (requiredValue.Type == ValueType.Null)
                {
                    error = $"{path}.{requiredField} is required.";
                    return false;
                }
            }
        }

        var propertiesVal = schemaObj.Get("properties");
        if (propertiesVal.Type != ValueType.Object || propertiesVal.AsObject() is not JsonObject propertiesObj)
            return true;

        foreach (var key in propertiesObj.GetAllKeys())
        {
            var propertySchema = propertiesObj.Get(key);
            var propertyValue = getProperty(key);

            if (propertyValue.Type == ValueType.Null)
                continue;

            if (!TryValidateAgainstSchema(propertyValue, propertySchema, $"{path}.{key}", out error))
                return false;
        }

        return true;
    }
}
