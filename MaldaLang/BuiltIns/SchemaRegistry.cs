// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Phase 6.2: resolves <c>schema</c> declarations to JSON-schema RuntimeValues for <c>validate()</c>.
/// </summary>
public static class SchemaRegistry
{
    private static readonly Dictionary<string, RuntimeValue> Schemas = new(StringComparer.Ordinal);

    public static void ClearForTesting() => Schemas.Clear();

    public static void Register(SchemaDeclaration decl) =>
        Schemas[decl.Name] = BuildSchema(decl);

    /// <summary>Transpiled programs register pre-built schema values at startup.</summary>
    public static void RegisterCompiled(string name, RuntimeValue schema) =>
        Schemas[name] = schema;

    public static bool TryResolve(string name, out RuntimeValue schema)
    {
        if (Schemas.TryGetValue(name, out schema!))
            return true;

        schema = RuntimeValue.Null();
        return false;
    }

    public static RuntimeValue BuildSchema(SchemaDeclaration decl)
    {
        var properties = new JsonObject();
        var required = new List<RuntimeValue>();

        foreach (var field in decl.Fields)
        {
            var propertySchema = BuildFieldSchema(field.TypeName);
            properties.Set(field.Name, RuntimeValue.Object(propertySchema));
            if (field.Required)
                required.Add(RuntimeValue.String(field.Name));
        }

        var root = new JsonObject();
        root.Set("type", RuntimeValue.String("object"));
        root.Set("properties", RuntimeValue.Object(properties));
        if (required.Count > 0)
            root.Set("required", RuntimeValue.Array(required));
        return RuntimeValue.Object(root);
    }

    private static JsonObject BuildFieldSchema(string typeName)
    {
        var trimmed = typeName.Trim();
        if (trimmed.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementType = trimmed[..^2];
            var arraySchema = new JsonObject();
            arraySchema.Set("type", RuntimeValue.String("array"));
            var itemsSchema = new JsonObject();
            itemsSchema.Set("type", RuntimeValue.String(NormalizeType(elementType)));
            arraySchema.Set("items", RuntimeValue.Object(itemsSchema));
            return arraySchema;
        }

        var propertySchema = new JsonObject();
        propertySchema.Set("type", RuntimeValue.String(NormalizeType(trimmed)));
        return propertySchema;
    }

    private static string NormalizeType(string typeName) =>
        typeName.Trim().ToLowerInvariant() switch
        {
            "int" or "integer" => "integer",
            "float" or "double" or "number" => "number",
            "bool" or "boolean" => "boolean",
            "array" or "list" => "array",
            "object" or "json" => "object",
            _ => "string"
        };

    public static RuntimeValue ResolveSchemaArgument(RuntimeValue schemaArg)
    {
        if (schemaArg.Type == ValueType.String)
        {
            var name = schemaArg.AsString();
            if (TryResolve(name, out var resolved))
                return resolved;
            throw new Exception($"Unknown schema '{name}'.");
        }

        if (schemaArg.Type == ValueType.Object)
            return schemaArg;

        throw new Exception("validate() expects a schema object or registered schema name.");
    }
}
