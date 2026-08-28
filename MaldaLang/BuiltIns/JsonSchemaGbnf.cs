// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Globalization;
using System.Text;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Compiles a MALDA JSON Schema (schema / sum-type / <c>program(Api)</c>) into
/// llama.cpp GBNF so in-process GGUF sampling cannot emit tokens outside the
/// typed-prompt contract. Mode B tool rounds stay unconstrained.
/// </summary>
public static class JsonSchemaGbnf
{
    public const string RootRule = "root";

    /// <summary>
    /// True when the completion is a typed JSON object (Mode A / Mode C extract),
    /// not a tool-calling round.
    /// </summary>
    public static bool ShouldConstrain(RuntimeValue? tools)
    {
        if (tools == null || tools.Type == ValueType.Null)
            return true;
        if (tools.Type != ValueType.Array)
            return false;
        return tools.AsArray().Count == 0;
    }

    public static bool TryFromResponseFormat(RuntimeValue? responseFormat, out string gbnf, out string error)
    {
        gbnf = "";
        error = "";
        if (!TryUnwrapSchema(responseFormat, out var schema, out error))
            return false;
        return TryFromSchema(schema, out gbnf, out error);
    }

    public static bool TryFromSchema(RuntimeValue schema, out string gbnf, out string error)
    {
        gbnf = "";
        error = "";
        try
        {
            var compiler = new Compiler();
            var rule = compiler.Compile(schema);
            gbnf = compiler.Render(rule);
            return !string.IsNullOrWhiteSpace(gbnf);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryUnwrapSchema(RuntimeValue? responseFormat, out RuntimeValue schema, out string error)
    {
        schema = RuntimeValue.Null();
        error = "";
        if (responseFormat == null || responseFormat.Type != ValueType.Object)
        {
            error = "response_format is missing.";
            return false;
        }

        if (responseFormat.AsObject() is not JsonObject obj)
        {
            error = "response_format is not a JSON object.";
            return false;
        }

        var typeVal = obj.Get("type");
        if (typeVal.Type == ValueType.String &&
            string.Equals(typeVal.AsString(), "json_schema", StringComparison.Ordinal))
        {
            var wrapper = obj.Get("json_schema");
            if (wrapper.Type == ValueType.Object && wrapper.AsObject() is JsonObject jsonSchema)
            {
                var inner = jsonSchema.Get("schema");
                if (inner.Type == ValueType.Object)
                {
                    schema = inner;
                    return true;
                }
            }

            error = "response_format.json_schema.schema is missing.";
            return false;
        }

        if (obj.Get("oneOf").Type == ValueType.Array ||
            obj.Get("anyOf").Type == ValueType.Array ||
            obj.Get("enum").Type == ValueType.Array ||
            obj.Get("const").Type != ValueType.Null ||
            obj.Get("type").Type != ValueType.Null ||
            obj.Get("properties").Type == ValueType.Object)
        {
            schema = responseFormat;
            return true;
        }

        error = "Not a JSON Schema or OpenAI response_format wrapper.";
        return false;
    }

    private sealed class Compiler
    {
        private readonly StringBuilder _rules = new();
        private int _nextId;
        private bool _emittedPrimitives;
        private bool _emittedJsonValue;

        public string Compile(RuntimeValue schema)
        {
            EmitPrimitives();
            return CompileNode(schema);
        }

        public string Render(string rootValueRule)
        {
            var sb = new StringBuilder();
            sb.Append(RootRule).Append(" ::= ").Append(rootValueRule).Append(" ws\n");
            sb.Append(_rules);
            return sb.ToString();
        }

        private string CompileNode(RuntimeValue schema)
        {
            if (schema.Type != ValueType.Object || schema.AsObject() is not JsonObject obj)
                return CompileJsonValue();

            var oneOf = FirstNonEmptyUnion(obj);
            if (oneOf != null)
            {
                var parts = new List<string>();
                foreach (var arm in oneOf)
                    parts.Add(CompileNode(arm));
                if (parts.Count == 0)
                    return CompileJsonValue();
                if (parts.Count == 1)
                    return parts[0];
                var rule = NewRule();
                _rules.Append(rule).Append(" ::= ").Append(string.Join(" | ", parts)).Append('\n');
                return rule;
            }

            if (obj.Get("enum").Type == ValueType.Array)
            {
                var lits = new List<string>();
                foreach (var item in obj.Get("enum").AsArray())
                {
                    var lit = CompileConst(item);
                    if (lit != null)
                        lits.Add(lit);
                }

                if (lits.Count == 0)
                    return CompileJsonValue();
                var rule = NewRule();
                _rules.Append(rule).Append(" ::= ").Append(string.Join(" | ", lits)).Append('\n');
                return rule;
            }

            var constVal = obj.Get("const");
            if (constVal.Type != ValueType.Null && constVal.Type != ValueType.Function)
            {
                var lit = CompileConst(constVal);
                if (lit != null)
                    return lit;
            }

            var types = ReadTypes(obj);
            if (types.Count == 0)
            {
                if (obj.Get("properties").Type == ValueType.Object)
                    return CompileObject(obj);
                return CompileJsonValue();
            }

            if (types.Count > 1)
            {
                var parts = new List<string>();
                foreach (var t in types)
                    parts.Add(CompileTypeName(t, obj));
                var rule = NewRule();
                _rules.Append(rule).Append(" ::= ").Append(string.Join(" | ", parts)).Append('\n');
                return rule;
            }

            return CompileTypeName(types[0], obj);
        }

        private string CompileTypeName(string typeName, JsonObject obj)
        {
            return typeName switch
            {
                "object" => CompileObject(obj),
                "array" => CompileArray(obj),
                "string" => "string",
                "integer" => "integer",
                "number" => "number",
                "boolean" => "boolean",
                "null" => "null",
                _ => CompileJsonValue()
            };
        }

        private string CompileObject(JsonObject obj)
        {
            var propertiesVal = obj.Get("properties");
            JsonObject? properties = propertiesVal.Type == ValueType.Object
                ? propertiesVal.AsObject() as JsonObject
                : null;

            if (properties == null)
                return CompileJsonValue();

            var keys = properties.GetAllKeys().ToList();
            if (keys.Count == 0)
            {
                var empty = NewRule();
                _rules.Append(empty).Append(" ::= \"{\" ws \"}\"\n");
                return empty;
            }

            var required = new HashSet<string>(StringComparer.Ordinal);
            var requiredVal = obj.Get("required");
            if (requiredVal.Type == ValueType.Array)
            {
                foreach (var item in requiredVal.AsArray())
                {
                    if (item.Type == ValueType.String)
                        required.Add(item.AsString());
                }
            }

            var requiredKeys = keys.Where(required.Contains).ToList();
            var optionalKeys = keys.Where(k => !required.Contains(k)).ToList();

            var body = new StringBuilder();
            body.Append("\"{\" ws ");

            var first = true;
            foreach (var key in requiredKeys)
            {
                if (!first)
                    body.Append(" \",\" ws ");
                first = false;
                AppendProperty(body, key, properties.Get(key));
            }

            if (optionalKeys.Count > 0)
            {
                if (requiredKeys.Count == 0)
                {
                    body.Append('(');
                    AppendProperty(body, optionalKeys[0], properties.Get(optionalKeys[0]));
                    for (var i = 1; i < optionalKeys.Count; i++)
                    {
                        body.Append(" (\",\" ws ");
                        AppendProperty(body, optionalKeys[i], properties.Get(optionalKeys[i]));
                    }

                    for (var i = 0; i < optionalKeys.Count; i++)
                        body.Append(")?");
                }
                else
                {
                    foreach (var key in optionalKeys)
                    {
                        body.Append(" (\",\" ws ");
                        AppendProperty(body, key, properties.Get(key));
                        body.Append(")?");
                    }
                }
            }

            body.Append(" ws \"}\"");
            var rule = NewRule();
            _rules.Append(rule).Append(" ::= ").Append(body).Append('\n');
            return rule;
        }

        private void AppendProperty(StringBuilder body, string key, RuntimeValue schema)
        {
            body.Append(GbnfLiteral("\"" + EscapeJsonString(key) + "\""));
            body.Append(" ws \":\" ws ");
            body.Append(CompileNode(schema));
        }

        private string CompileArray(JsonObject obj)
        {
            var items = obj.Get("items");
            var itemRule = items.Type == ValueType.Object || items.Type == ValueType.Array
                ? CompileNode(items.Type == ValueType.Array && items.AsArray().Count > 0 ? items.AsArray()[0] : items)
                : CompileJsonValue();
            var rule = NewRule();
            _rules.Append(rule)
                .Append(" ::= \"[\" ws (")
                .Append(itemRule)
                .Append(" (ws \",\" ws ")
                .Append(itemRule)
                .Append(")*)? ws \"]\"\n");
            return rule;
        }

        private string CompileJsonValue()
        {
            if (_emittedJsonValue)
                return "json-value";
            _emittedJsonValue = true;
            _rules.Append("json-value ::= object | array | string | number | boolean | null\n");
            _rules.Append("object ::= \"{\" ws (string ws \":\" ws json-value (ws \",\" ws string ws \":\" ws json-value)*)? ws \"}\"\n");
            _rules.Append("array ::= \"[\" ws (json-value (ws \",\" ws json-value)*)? ws \"]\"\n");
            return "json-value";
        }

        private static string? CompileConst(RuntimeValue value)
        {
            return value.Type switch
            {
                ValueType.String => GbnfLiteral("\"" + EscapeJsonString(value.AsString()) + "\""),
                ValueType.Integer => GbnfLiteral(value.AsInteger().ToString(CultureInfo.InvariantCulture)),
                ValueType.Float => GbnfLiteral(value.AsFloat().ToString("G", CultureInfo.InvariantCulture)),
                ValueType.Boolean => GbnfLiteral(value.AsBoolean() ? "true" : "false"),
                ValueType.Null => GbnfLiteral("null"),
                _ => null
            };
        }

        private static List<string> ReadTypes(JsonObject obj)
        {
            var typeVal = obj.Get("type");
            var types = new List<string>();
            if (typeVal.Type == ValueType.String)
            {
                types.Add(typeVal.AsString());
            }
            else if (typeVal.Type == ValueType.Array)
            {
                foreach (var item in typeVal.AsArray())
                {
                    if (item.Type == ValueType.String)
                        types.Add(item.AsString());
                }
            }

            return types;
        }

        private static List<RuntimeValue>? FirstNonEmptyUnion(JsonObject obj)
        {
            foreach (var key in new[] { "oneOf", "anyOf" })
            {
                var val = obj.Get(key);
                if (val.Type == ValueType.Array && val.AsArray().Count > 0)
                    return val.AsArray();
            }

            return null;
        }

        private void EmitPrimitives()
        {
            if (_emittedPrimitives)
                return;
            _emittedPrimitives = true;
            // No newlines in ws: LlamaCppClient AntiPrompts include "\n\n".
            _rules.Append("ws ::= [ \\t]*\n");
            _rules.Append("string ::= \"\\\"\" ([^\"\\\\] | \"\\\\\" [\"\\\\/bfnrt])* \"\\\"\"\n");
            _rules.Append("integer ::= \"-\"? ([0-9] | [1-9] [0-9]*)\n");
            _rules.Append("number ::= integer (\".\" [0-9]+)? ([eE] [-+]? [0-9]+)?\n");
            _rules.Append("boolean ::= \"true\" | \"false\"\n");
            _rules.Append("null ::= \"null\"\n");
        }

        private string NewRule()
        {
            _nextId++;
            return "s" + _nextId.ToString(CultureInfo.InvariantCulture);
        }

        private static string GbnfLiteral(string text)
        {
            var sb = new StringBuilder(text.Length + 2);
            sb.Append('"');
            foreach (var ch in text)
            {
                if (ch is '"' or '\\')
                    sb.Append('\\');
                sb.Append(ch);
            }

            sb.Append('"');
            return sb.ToString();
        }

        private static string EscapeJsonString(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                sb.Append(ch switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => ch.ToString()
                });
            }

            return sb.ToString();
        }
    }
}
