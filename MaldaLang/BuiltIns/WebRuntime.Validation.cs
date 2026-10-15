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

public class RouteValidationError
{
    public string Location { get; }
    public string Field { get; }
    public string Message { get; }

    public RouteValidationError(string location, string field, string message)
    {
        Location = location;
        Field = field;
        Message = message;
    }
}

public static partial class WebRuntimeHelpers
{
    private sealed class ValidationRule
    {
        public string TypeName { get; set; } = "string";
        public bool Required { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public string? Pattern { get; set; }
    }

    public static bool ValidateRequest(
        RuntimeValue schema,
        Dictionary<string, string> pathParams,
        Dictionary<string, string> queryParams,
        RuntimeValue? requestBody,
        out List<RouteValidationError> errors)
    {
        errors = new List<RouteValidationError>();

        if (schema.Type == ValueType.Null)
        {
            return true;
        }

        var normalizedSchema = NormalizeSchema(schema);
        if (normalizedSchema.Type != ValueType.Object || normalizedSchema.AsObject() is not JsonObject schemaObj)
        {
            throw new Exception("Validation schema must be a JSON object or a JSON string.");
        }

        ValidateSection("path", schemaObj, pathParams, requestBody, errors);
        ValidateSection("query", schemaObj, queryParams, requestBody, errors);
        ValidateBodySection(schemaObj, requestBody, errors);

        return errors.Count == 0;
    }

    private static RuntimeValue NormalizeSchema(RuntimeValue schema)
    {
        if (schema.Type == ValueType.String)
        {
            var text = schema.AsString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return RuntimeValue.Null();
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                return JsonElementToRuntimeValue(doc.RootElement);
            }
            catch
            {
                throw new Exception("Validation schema string must be valid JSON.");
            }
        }

        return schema;
    }

    private static void ValidateSection(
        string sectionName,
        JsonObject schemaObj,
        Dictionary<string, string> source,
        RuntimeValue? requestBody,
        List<RouteValidationError> errors)
    {
        var sectionValue = schemaObj.Get(sectionName, null);
        if (sectionValue.Type == ValueType.Null)
        {
            return;
        }

        if (sectionValue.Type != ValueType.Object || sectionValue.AsObject() is not JsonObject sectionObj)
        {
            throw new Exception($"Validation schema section '{sectionName}' must be an object.");
        }

        foreach (var kvp in sectionObj.GetProperties())
        {
            var field = kvp.Key;
            var rule = ParseRule(kvp.Value, sectionName, field);

            source.TryGetValue(field, out var rawTextValue);
            var hasValue = !string.IsNullOrEmpty(rawTextValue);

            if (!hasValue)
            {
                if (rule.Required)
                {
                    errors.Add(new RouteValidationError(sectionName, field, "Field is required."));
                }
                continue;
            }

            ValidateScalarValue(sectionName, field, RuntimeValue.String(rawTextValue!), rule, errors);
        }
    }

    private static void ValidateBodySection(JsonObject schemaObj, RuntimeValue? requestBody, List<RouteValidationError> errors)
    {
        var sectionValue = schemaObj.Get("body", null);
        if (sectionValue.Type == ValueType.Null)
        {
            return;
        }

        if (sectionValue.Type != ValueType.Object || sectionValue.AsObject() is not JsonObject sectionObj)
        {
            throw new Exception("Validation schema section 'body' must be an object.");
        }

        JsonObject? bodyObject = null;
        if (requestBody != null && requestBody.Type == ValueType.Object)
        {
            bodyObject = requestBody.AsObject() as JsonObject;
        }

        foreach (var kvp in sectionObj.GetProperties())
        {
            var field = kvp.Key;
            var rule = ParseRule(kvp.Value, "body", field);

            RuntimeValue value = RuntimeValue.Null();
            if (bodyObject != null)
            {
                value = bodyObject.Get(field, null);
            }

            if (value.Type == ValueType.Null)
            {
                if (rule.Required)
                {
                    errors.Add(new RouteValidationError("body", field, "Field is required."));
                }
                continue;
            }

            ValidateScalarValue("body", field, value, rule, errors);
        }
    }

    private static ValidationRule ParseRule(RuntimeValue ruleValue, string location, string field)
    {
        if (ruleValue.Type == ValueType.String)
        {
            return ParseRuleString(ruleValue.AsString());
        }

        if (ruleValue.Type == ValueType.Object && ruleValue.AsObject() is JsonObject ruleObj)
        {
            var rule = new ValidationRule();

            var typeValue = ruleObj.Get("type", null);
            if (typeValue.Type == ValueType.String)
            {
                rule.TypeName = typeValue.AsString();
            }

            var requiredValue = ruleObj.Get("required", null);
            if (requiredValue.Type == ValueType.Boolean)
            {
                rule.Required = requiredValue.AsBoolean();
            }

            rule.Min = RuntimeValueToDouble(ruleObj.Get("min", null));
            rule.Max = RuntimeValueToDouble(ruleObj.Get("max", null));
            rule.MinLength = RuntimeValueToInt(ruleObj.Get("minLength", null));
            rule.MaxLength = RuntimeValueToInt(ruleObj.Get("maxLength", null));

            var patternValue = ruleObj.Get("pattern", null);
            if (patternValue.Type == ValueType.String)
            {
                rule.Pattern = patternValue.AsString();
            }

            return rule;
        }

        throw new Exception($"Validation rule for {location}.{field} must be a string or object.");
    }

    private static ValidationRule ParseRuleString(string ruleText)
    {
        var rule = new ValidationRule();
        var parts = ruleText.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (part.Equals("required", StringComparison.OrdinalIgnoreCase))
            {
                rule.Required = true;
                continue;
            }

            if (part.StartsWith("min=", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(part["min=".Length..], out var min))
                {
                    rule.Min = min;
                }
                continue;
            }

            if (part.StartsWith("max=", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(part["max=".Length..], out var max))
                {
                    rule.Max = max;
                }
                continue;
            }

            if (part.StartsWith("minLength=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part["minLength=".Length..], out var minLength))
                {
                    rule.MinLength = minLength;
                }
                continue;
            }

            if (part.StartsWith("maxLength=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part["maxLength=".Length..], out var maxLength))
                {
                    rule.MaxLength = maxLength;
                }
                continue;
            }

            if (part.StartsWith("pattern=", StringComparison.OrdinalIgnoreCase))
            {
                rule.Pattern = part["pattern=".Length..];
                continue;
            }

            rule.TypeName = part;
        }

        return rule;
    }

    private static void ValidateScalarValue(string location, string field, RuntimeValue value, ValidationRule rule, List<RouteValidationError> errors)
    {
        var typeName = rule.TypeName.Trim().ToLowerInvariant();
        if (typeName.Length == 0)
        {
            typeName = "string";
        }

        if (!TryValidateType(value, typeName, out var numericValue, out var stringValue))
        {
            errors.Add(new RouteValidationError(location, field, $"Expected type '{rule.TypeName}'."));
            return;
        }

        if (numericValue.HasValue)
        {
            if (rule.Min.HasValue && numericValue.Value < rule.Min.Value)
            {
                errors.Add(new RouteValidationError(location, field, $"Value must be >= {rule.Min.Value}."));
            }
            if (rule.Max.HasValue && numericValue.Value > rule.Max.Value)
            {
                errors.Add(new RouteValidationError(location, field, $"Value must be <= {rule.Max.Value}."));
            }
        }

        if (stringValue != null)
        {
            if (rule.MinLength.HasValue && stringValue.Length < rule.MinLength.Value)
            {
                errors.Add(new RouteValidationError(location, field, $"Length must be >= {rule.MinLength.Value}."));
            }
            if (rule.MaxLength.HasValue && stringValue.Length > rule.MaxLength.Value)
            {
                errors.Add(new RouteValidationError(location, field, $"Length must be <= {rule.MaxLength.Value}."));
            }
            if (!string.IsNullOrEmpty(rule.Pattern) && !Regex.IsMatch(stringValue, rule.Pattern))
            {
                errors.Add(new RouteValidationError(location, field, "Value does not match required pattern."));
            }
        }
    }

    private static bool TryValidateType(RuntimeValue value, string typeName, out double? numericValue, out string? stringValue)
    {
        numericValue = null;
        stringValue = null;

        switch (typeName)
        {
            case "string":
                if (value.Type == ValueType.String)
                {
                    stringValue = value.AsString();
                    return true;
                }
                return false;
            case "int":
            case "integer":
                if (TryCoerceToInt(value, out var intValue))
                {
                    numericValue = intValue;
                    return true;
                }
                return false;
            case "float":
            case "double":
            case "number":
                if (TryCoerceToDouble(value, out var floatValue))
                {
                    numericValue = floatValue;
                    return true;
                }
                return false;
            case "bool":
            case "boolean":
                if (value.Type == ValueType.Boolean)
                {
                    return true;
                }
                if (value.Type == ValueType.String)
                {
                    var s = value.AsString();
                    return s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                           s.Equals("false", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            case "object":
                return value.Type == ValueType.Object;
            case "array":
                return value.Type == ValueType.Array;
            default:
                // Unknown type behaves as string for backward compatibility.
                if (value.Type == ValueType.String)
                {
                    stringValue = value.AsString();
                    return true;
                }
                return false;
        }
    }

    private static bool TryCoerceToInt(RuntimeValue value, out int intValue)
    {
        intValue = 0;
        if (value.Type == ValueType.Integer)
        {
            intValue = value.AsInteger();
            return true;
        }
        if (value.Type == ValueType.String)
        {
            return int.TryParse(value.AsString(), out intValue);
        }
        return false;
    }

    private static bool TryCoerceToDouble(RuntimeValue value, out double doubleValue)
    {
        doubleValue = 0;
        if (value.Type == ValueType.Integer)
        {
            doubleValue = value.AsInteger();
            return true;
        }
        if (value.Type == ValueType.Float)
        {
            doubleValue = value.AsFloat();
            return true;
        }
        if (value.Type == ValueType.String)
        {
            return double.TryParse(value.AsString(), out doubleValue);
        }
        return false;
    }

    private static double? RuntimeValueToDouble(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger(),
            ValueType.Float => value.AsFloat(),
            _ => null
        };
    }

    private static int? RuntimeValueToInt(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger(),
            _ => null
        };
    }
}
