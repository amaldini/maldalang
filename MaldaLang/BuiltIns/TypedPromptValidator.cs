// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public static class TypedPromptValidator
{
    public const string SchemaAppendixMarker = "---\nMALDA_OUTPUT_SCHEMA\n";

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
        return TryValidateReturnType(value, returnType, interpreter, out _, out error);
    }

    public static bool TryValidateReturnType(
        RuntimeValue value,
        string returnType,
        Interpreter? interpreter,
        out RuntimeValue validated,
        out string error)
    {
        validated = value;
        error = "";
        if (!TypedPromptSchemaResolver.TryResolve(returnType, interpreter, out var schema, out var schemaError))
        {
            error = schemaError;
            return false;
        }

        return TryValidateReturnType(value, schema, out validated, out error);
    }

    /// <summary>
    /// Validates a value against a pre-resolved schema (e.g. from transpiled code where no interpreter is available).
    /// </summary>
    public static bool TryValidateReturnType(RuntimeValue value, RuntimeValue schema, out string error)
    {
        return TryValidateReturnType(value, schema, out _, out error);
    }

    public static bool TryValidateReturnType(
        RuntimeValue value,
        RuntimeValue schema,
        out RuntimeValue validated,
        out string error)
    {
        validated = value;
        error = "";

        if (IsSumSchema(schema))
            return TryValidateAndCoerceSum(value, schema, "$", out validated, out error);

        if (IsProgramSchema(schema))
            return TryValidateAndCoerceProgram(value, schema, out validated, out error);

        if (!TryValidateAgainstSchema(value, schema, "$", out error))
            return false;

        validated = value;
        return true;
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
    /// The schema from TypedPromptSchemaResolver is { type, properties?, required? } or a sum oneOf;
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

    public static bool IsSumSchema(RuntimeValue schema)
    {
        if (schema.Type != ValueType.Object || schema.AsObject() is not JsonObject obj)
            return false;
        var kind = obj.Get("x-malda-kind");
        return kind.Type == ValueType.String &&
               string.Equals(kind.AsString(), "sum", StringComparison.Ordinal);
    }

    public static bool IsProgramSchema(RuntimeValue schema)
    {
        if (schema.Type != ValueType.Object || schema.AsObject() is not JsonObject obj)
            return false;
        var kind = obj.Get("x-malda-kind");
        return kind.Type == ValueType.String &&
               string.Equals(kind.AsString(), "program", StringComparison.Ordinal);
    }

    public static string FormatSchemaAppendix(string returnType, RuntimeValue schema)
    {
        var sb = new StringBuilder();
        sb.Append("Return type: ");
        sb.Append(returnType);
        sb.AppendLine(".");

        if (IsProgramSchema(schema) && schema.AsObject() is JsonObject progObj)
        {
            var apiName = progObj.Get("x-malda-api");
            var api = apiName.Type == ValueType.String ? apiName.AsString() : "?";
            sb.AppendLine($"Program for api {api} — return JSON:");
            sb.AppendLine($"{{\"@api\":\"{api}\",\"steps\":[{{\"call\":\"<method>\",\"args\":[...],\"as\":\"t0\"}}],\"return\":\"$t0\"}}");
            sb.AppendLine("Allowed calls:");
            if (ApiRegistry.TryGet(api, out var def))
            {
                foreach (var method in def.Methods)
                {
                    sb.Append("- ");
                    sb.Append(method.Name);
                    sb.Append('(');
                    sb.Append(string.Join(", ", method.ParameterNames));
                    sb.AppendLine(")");
                }
            }

            return sb.ToString().TrimEnd();
        }

        if (IsSumSchema(schema) && schema.AsObject() is JsonObject sumObj)
        {
            sb.AppendLine("Sum type — return exactly ONE variant as JSON:");
            sb.AppendLine("Shape: {\"tag\":\"<Constructor>\", ...payload fields by name}");
            var oneOf = sumObj.Get("oneOf");
            if (oneOf.Type == ValueType.Array)
            {
                foreach (var armVal in oneOf.AsArray())
                {
                    if (armVal.Type != ValueType.Object || armVal.AsObject() is not JsonObject arm)
                        continue;
                    var propsVal = arm.Get("properties");
                    if (propsVal.Type != ValueType.Object || propsVal.AsObject() is not JsonObject props)
                        continue;
                    var tagProp = props.Get("tag");
                    var tagName = "?";
                    if (tagProp.Type == ValueType.Object && tagProp.AsObject() is JsonObject tagSchema)
                    {
                        var constVal = tagSchema.Get("const");
                        if (constVal.Type == ValueType.String)
                            tagName = constVal.AsString();
                    }

                    var requiredNames = new HashSet<string>(StringComparer.Ordinal);
                    var requiredVal = arm.Get("required");
                    if (requiredVal.Type == ValueType.Array)
                    {
                        foreach (var r in requiredVal.AsArray())
                        {
                            if (r.Type == ValueType.String)
                                requiredNames.Add(r.AsString());
                        }
                    }

                    var payloadBits = new List<string>();
                    foreach (var key in props.GetAllKeys())
                    {
                        if (string.Equals(key, "tag", StringComparison.Ordinal))
                            continue;
                        var described = DescribeSchemaType(props.Get(key));
                        var optional = !requiredNames.Contains(key);
                        if (described == "any")
                            payloadBits.Add(key);
                        else
                            payloadBits.Add(optional ? $"{key}: {described}?" : $"{key}: {described}");
                    }

                    sb.Append("- ");
                    sb.Append(tagName);
                    sb.Append('(');
                    sb.Append(string.Join(", ", payloadBits));
                    sb.AppendLine(")");
                }
            }

            return sb.ToString().TrimEnd();
        }

        if (schema.Type == ValueType.Object && schema.AsObject() is JsonObject obj)
        {
            var typeVal = obj.Get("type");
            if (typeVal.Type == ValueType.String && typeVal.AsString() != "object")
            {
                sb.Append("JSON type: ");
                sb.Append(typeVal.AsString());
                sb.Append('.');
                return sb.ToString();
            }

            sb.AppendLine("Object fields:");
            var propsVal = obj.Get("properties");
            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            var requiredVal = obj.Get("required");
            if (requiredVal.Type == ValueType.Array)
            {
                foreach (var r in requiredVal.AsArray())
                {
                    if (r.Type == ValueType.String)
                        requiredNames.Add(r.AsString());
                }
            }

            if (propsVal.Type == ValueType.Object && propsVal.AsObject() is JsonObject props)
            {
                foreach (var key in props.GetAllKeys())
                {
                    var fieldSchema = props.Get(key);
                    var fieldType = DescribeSchemaType(fieldSchema);
                    var optional = !requiredNames.Contains(key);
                    sb.Append("- ");
                    sb.Append(key);
                    sb.Append(optional ? "?: " : ": ");
                    sb.AppendLine(fieldType);
                }
            }
            else
            {
                sb.AppendLine("(no field list)");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string? ApplySchemaAppendix(string? system, string returnType, RuntimeValue schema)
    {
        var appendixBody = FormatSchemaAppendix(returnType, schema);
        var block = "\n\n" + SchemaAppendixMarker + appendixBody + "\n---";
        if (!string.IsNullOrEmpty(system) &&
            system.Contains(SchemaAppendixMarker, StringComparison.Ordinal))
        {
            return system;
        }

        return (system ?? "") + block;
    }

    private static string DescribeSchemaType(RuntimeValue fieldSchema)
    {
        if (fieldSchema.Type != ValueType.Object || fieldSchema.AsObject() is not JsonObject obj)
            return "any";

        var typeVal = obj.Get("type");
        if (typeVal.Type == ValueType.String)
        {
            var t = typeVal.AsString();
            if (t == "array")
            {
                var items = obj.Get("items");
                if (items.Type == ValueType.Object)
                    return DescribeSchemaType(items) + "[]";

                return "array";
            }

            if (t == "object")
            {
                var propsVal = obj.Get("properties");
                if (propsVal.Type == ValueType.Object && propsVal.AsObject() is JsonObject props)
                {
                    var parts = new List<string>();
                    foreach (var key in props.GetAllKeys())
                        parts.Add(key + ": " + DescribeSchemaType(props.Get(key)));
                    return "{ " + string.Join(", ", parts) + " }";
                }
            }

            return t;
        }

        if (typeVal.Type == ValueType.Array)
            return "any";

        return "any";
    }

    private static bool TryValidateAndCoerceProgram(
        RuntimeValue value,
        RuntimeValue schema,
        out RuntimeValue validated,
        out string error)
    {
        validated = RuntimeValue.Null();
        error = "";

        if (schema.Type != ValueType.Object || schema.AsObject() is not JsonObject schemaObj)
        {
            error = "Invalid program schema object.";
            return false;
        }

        if (!TryValidateAgainstSchema(value, schema, "$", out error))
            return false;

        if (value.Type != ValueType.Object || value.AsObject() is not JsonObject jsonObj)
        {
            error = "$. must be a JSON program object.";
            return false;
        }

        var expectedApi = schemaObj.Get("x-malda-api");
        var expectedApiName = expectedApi.Type == ValueType.String ? expectedApi.AsString() : "";
        var apiVal = jsonObj.Get("@api");
        if (apiVal.Type != ValueType.String)
        {
            error = "$.@api is required and must be a string.";
            return false;
        }

        var apiName = apiVal.AsString();
        if (!string.Equals(apiName, expectedApiName, StringComparison.Ordinal))
        {
            error = $"$.@api must be '{expectedApiName}', got '{apiName}'.";
            return false;
        }

        if (!ApiRegistry.TryGet(apiName, out var apiDef))
        {
            error = $"Unknown api '{apiName}'.";
            return false;
        }

        var stepsVal = jsonObj.Get("steps");
        if (stepsVal.Type != ValueType.Array)
        {
            error = "$.steps must be an array.";
            return false;
        }

        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var steps = new List<ProgramInstance.Step>();
        var stepList = stepsVal.AsArray();
        for (int i = 0; i < stepList.Count; i++)
        {
            var stepVal = stepList[i];
            if (stepVal.Type != ValueType.Object || stepVal.AsObject() is not JsonObject stepObj)
            {
                error = $"$.steps[{i}] must be an object.";
                return false;
            }

            var callVal = stepObj.Get("call");
            if (callVal.Type != ValueType.String)
            {
                error = $"$.steps[{i}].call must be a string.";
                return false;
            }

            var call = callVal.AsString();
            if (!apiDef.TryGetMethod(call, out var method))
            {
                error = $"$.steps[{i}].call '{call}' is not a method on api '{apiName}'.";
                return false;
            }

            var argsVal = stepObj.Get("args");
            if (argsVal.Type != ValueType.Array)
            {
                error = $"$.steps[{i}].args must be an array.";
                return false;
            }

            var args = argsVal.AsArray();
            if (args.Count != method.ParameterNames.Count)
            {
                error = $"$.steps[{i}].args length must be {method.ParameterNames.Count} for {call}, got {args.Count}.";
                return false;
            }

            for (int a = 0; a < args.Count; a++)
            {
                if (!TryValidateProgramArgRef(args[a], aliases, $"$.steps[{i}].args[{a}]", out error))
                    return false;
            }

            var asVal = stepObj.Get("as");
            if (asVal.Type != ValueType.String || string.IsNullOrWhiteSpace(asVal.AsString()))
            {
                error = $"$.steps[{i}].as must be a non-empty string.";
                return false;
            }

            var alias = asVal.AsString();
            if (!aliases.Add(alias))
            {
                error = $"$.steps[{i}].as '{alias}' is duplicated.";
                return false;
            }

            steps.Add(new ProgramInstance.Step(call, new List<RuntimeValue>(args), alias));
        }

        var returnVal = jsonObj.Get("return");
        if (!TryValidateProgramArgRef(returnVal, aliases, "$.return", out error))
            return false;

        validated = RuntimeValue.Object(new ProgramInstance(apiName, steps, returnVal));
        return true;
    }

    private static bool TryValidateProgramArgRef(
        RuntimeValue arg,
        HashSet<string> definedAliases,
        string path,
        out string error)
    {
        error = "";
        if (arg.Type == ValueType.String)
        {
            var s = arg.AsString();
            if (s.StartsWith("$", StringComparison.Ordinal))
            {
                var alias = s.Substring(1);
                if (string.IsNullOrEmpty(alias) || !definedAliases.Contains(alias))
                {
                    error = $"{path} references unknown step alias '{s}'.";
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryValidateAndCoerceSum(
        RuntimeValue value,
        RuntimeValue schema,
        string path,
        out RuntimeValue validated,
        out string error)
    {
        validated = RuntimeValue.Null();
        if (!TryMatchSumArm(value, schema, path, out var tag, out var getProperty, out var matchedArm, out error))
            return false;

        if (!TryValidateAgainstSchema(value, RuntimeValue.Object(matchedArm), path, out error))
            return false;

        var payload = new List<RuntimeValue>();
        var armProps = matchedArm.Get("properties");
        if (armProps.Type == ValueType.Object && armProps.AsObject() is JsonObject armPropsObj)
        {
            foreach (var key in armPropsObj.GetAllKeys())
            {
                if (string.Equals(key, "tag", StringComparison.Ordinal))
                    continue;
                payload.Add(getProperty(key));
            }
        }

        // Prefer declaration order from registry when available.
        if (schema.Type == ValueType.Object && schema.AsObject() is JsonObject schemaObj)
        {
            var typeNameVal = schemaObj.Get("x-malda-sum-type");
            if (typeNameVal.Type == ValueType.String &&
                SumTypeRegistry.TryGetDefinition(typeNameVal.AsString(), out var def))
            {
                var ctor = def.Constructors.FirstOrDefault(c =>
                    string.Equals(c.Name, tag, StringComparison.Ordinal));
                if (ctor != null)
                {
                    payload = new List<RuntimeValue>();
                    foreach (var param in ctor.ParameterNames)
                        payload.Add(getProperty(param));
                }
            }
        }

        validated = RuntimeValue.Variant(tag, payload);
        return true;
    }

    /// <summary>
    /// Validates a sum-type JSON/dict shape at <paramref name="path"/> without coercing to a variant.
    /// Used for nested <c>schema { field: Intent }</c> fields and <c>validate("Intent", dict)</c>.
    /// </summary>
    private static bool TryValidateSumShape(
        RuntimeValue value,
        RuntimeValue schema,
        string path,
        out string error)
    {
        if (!TryMatchSumArm(value, schema, path, out _, out _, out var matchedArm, out error))
            return false;
        return TryValidateAgainstSchema(value, RuntimeValue.Object(matchedArm), path, out error);
    }

    private static bool TryMatchSumArm(
        RuntimeValue value,
        RuntimeValue schema,
        string path,
        out string tag,
        out Func<string, RuntimeValue> getProperty,
        out JsonObject matchedArm,
        out string error)
    {
        tag = "";
        getProperty = static _ => RuntimeValue.Null();
        matchedArm = null!;
        error = "";

        if (schema.Type != ValueType.Object || schema.AsObject() is not JsonObject schemaObj)
        {
            error = "Invalid sum-type schema object.";
            return false;
        }

        if (value.Type != ValueType.Object || value.AsObject() is not ObjectInstance obj)
        {
            error = $"{path} must be a JSON object with a sum-type tag.";
            return false;
        }

        getProperty = key => obj.Get(key);
        var tagVal = getProperty("tag");
        if (tagVal.Type != ValueType.String)
        {
            error = $"{path}.tag is required and must be a string constructor name.";
            return false;
        }

        tag = tagVal.AsString();
        var oneOf = schemaObj.Get("oneOf");
        if (oneOf.Type != ValueType.Array || oneOf.AsArray().Count == 0)
        {
            error = "Sum-type schema has no oneOf arms.";
            return false;
        }

        JsonObject? found = null;
        var knownTags = new List<string>();
        foreach (var armVal in oneOf.AsArray())
        {
            if (armVal.Type != ValueType.Object || armVal.AsObject() is not JsonObject arm)
                continue;
            var propsVal = arm.Get("properties");
            if (propsVal.Type != ValueType.Object || propsVal.AsObject() is not JsonObject props)
                continue;
            var tagProp = props.Get("tag");
            if (tagProp.Type != ValueType.Object || tagProp.AsObject() is not JsonObject tagSchema)
                continue;
            var constVal = tagSchema.Get("const");
            if (constVal.Type != ValueType.String)
                continue;
            var armTag = constVal.AsString();
            knownTags.Add(armTag);
            if (string.Equals(armTag, tag, StringComparison.Ordinal))
                found = arm;
        }

        if (found == null)
        {
            error = $"{path}.tag '{tag}' is not a known constructor. Expected one of: {string.Join(", ", knownTags)}.";
            return false;
        }

        matchedArm = found;
        return true;
    }

    private static bool TryValidateAgainstSchema(RuntimeValue value, RuntimeValue schema, string path, out string error)
    {
        error = "";
        if (IsSumSchema(schema))
            return TryValidateSumShape(value, schema, path, out error);

        if (schema.Type != ValueType.Object || schema.AsObject() is not JsonObject schemaObj)
        {
            error = "Invalid schema object.";
            return false;
        }

        var constVal = schemaObj.Get("const");
        if (constVal.Type != ValueType.Null)
        {
            if (!ValuesEqualForConst(value, constVal))
            {
                error = $"{path} must equal {FormatConst(constVal)}.";
                return false;
            }
        }

        var typeNameVal = schemaObj.Get("type");
        if (typeNameVal.Type == ValueType.Array)
        {
            foreach (var alt in typeNameVal.AsArray())
            {
                if (alt.Type != ValueType.String)
                    continue;
                if (MatchesJsonType(value, alt.AsString()))
                    return true;
            }

            error = $"{path} does not match any allowed type.";
            return false;
        }

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
            case "null":
                if (value.Type != ValueType.Null)
                {
                    error = $"{path} must be null, got {value.Type}.";
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

    private static bool MatchesJsonType(RuntimeValue value, string typeName) =>
        typeName switch
        {
            "string" => value.Type == ValueType.String,
            "integer" => value.Type == ValueType.Integer,
            "number" => value.Type == ValueType.Integer || value.Type == ValueType.Float,
            "boolean" => value.Type == ValueType.Boolean,
            "null" => value.Type == ValueType.Null,
            "array" => value.Type == ValueType.Array,
            "object" => value.Type == ValueType.Object,
            _ => false
        };

    private static bool ValuesEqualForConst(RuntimeValue value, RuntimeValue constVal)
    {
        if (value.Type != constVal.Type)
            return false;
        return value.Type switch
        {
            ValueType.String => value.AsString() == constVal.AsString(),
            ValueType.Integer => value.AsInteger() == constVal.AsInteger(),
            ValueType.Float => Math.Abs(value.AsFloat() - constVal.AsFloat()) < double.Epsilon,
            ValueType.Boolean => value.AsBoolean() == constVal.AsBoolean(),
            ValueType.Null => true,
            _ => value.ToString() == constVal.ToString()
        };
    }

    private static string FormatConst(RuntimeValue constVal) =>
        constVal.Type == ValueType.String ? $"\"{constVal.AsString()}\"" : constVal.ToString();

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
            return ValidateObjectProperties(key => jsonObj.Get(key), jsonObj.GetAllKeys(), schemaObj, path, out error);

        if (obj is DictionaryInstance dict)
            return ValidateObjectProperties(
                key => dict.TryGetEntry(key, out var entry) ? entry : RuntimeValue.Null(),
                dict.Entries.Keys,
                schemaObj,
                path,
                out error);

        error = $"{path} must be a JSON or dictionary object.";
        return false;
    }

    private static bool ValidateObjectProperties(
        Func<string, RuntimeValue> getProperty,
        IEnumerable<string> presentKeys,
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
        JsonObject? propertiesObj = null;
        if (propertiesVal.Type == ValueType.Object && propertiesVal.AsObject() is JsonObject props)
            propertiesObj = props;

        var additional = schemaObj.Get("additionalProperties");
        if (additional.Type == ValueType.Boolean && !additional.AsBoolean() && propertiesObj != null)
        {
            var allowed = new HashSet<string>(propertiesObj.GetAllKeys(), StringComparer.Ordinal);
            foreach (var key in presentKeys)
            {
                if (!allowed.Contains(key))
                {
                    error = $"{path}.{key} is not allowed.";
                    return false;
                }
            }
        }

        if (propertiesObj == null)
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
