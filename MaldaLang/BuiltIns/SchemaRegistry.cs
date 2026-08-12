// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Phase 6.2 / P0: resolves <c>schema</c> declarations to JSON-schema RuntimeValues for <c>validate()</c>.
/// Nested schema field types are expanded inline (forward references allowed).
/// </summary>
public static class SchemaRegistry
{
    private static readonly Dictionary<string, RuntimeValue> Schemas = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SchemaDeclaration> Declarations = new(StringComparer.Ordinal);

    public static void ClearForTesting()
    {
        Schemas.Clear();
        Declarations.Clear();
    }

    public static bool IsRegistered(string name) =>
        Declarations.ContainsKey(name) || Schemas.ContainsKey(name);

    public static void Register(SchemaDeclaration decl)
    {
        if (SumTypeRegistry.IsRegistered(decl.Name))
        {
            throw new Exception(
                $"Name '{decl.Name}' is already registered as a sum type; cannot also declare a schema.");
        }

        if (ApiRegistry.IsRegistered(decl.Name))
        {
            throw new Exception(
                $"Name '{decl.Name}' is already registered as an api; cannot also declare a schema.");
        }

        Declarations[decl.Name] = decl;
        // Invalidate cached expansions so nested refs rebuild with the new declaration.
        Schemas.Clear();
    }

    /// <summary>Transpiled programs register pre-built schema values at startup.</summary>
    public static void RegisterCompiled(string name, RuntimeValue schema)
    {
        if (SumTypeRegistry.IsRegistered(name))
        {
            throw new Exception(
                $"Name '{name}' is already registered as a sum type; cannot also register a schema.");
        }

        if (ApiRegistry.IsRegistered(name))
        {
            throw new Exception(
                $"Name '{name}' is already registered as an api; cannot also register a schema.");
        }

        Schemas[name] = schema;
    }

    public static bool TryResolve(string name, out RuntimeValue schema)
    {
        if (Schemas.TryGetValue(name, out schema!))
            return true;

        if (!Declarations.TryGetValue(name, out var decl))
        {
            schema = RuntimeValue.Null();
            return false;
        }

        schema = ExpandDeclaration(decl, Declarations, new HashSet<string>(StringComparer.Ordinal));
        Schemas[name] = schema;
        return true;
    }

    /// <summary>
    /// Builds a JSON-schema object for <paramref name="decl"/>, expanding nested schema
    /// references using sibling declarations when provided (transpile path).
    /// </summary>
    public static RuntimeValue BuildSchema(
        SchemaDeclaration decl,
        IReadOnlyDictionary<string, SchemaDeclaration>? knownSchemas = null)
    {
        var lookup = knownSchemas ?? Declarations;
        return ExpandDeclaration(decl, lookup, new HashSet<string>(StringComparer.Ordinal));
    }

    private static RuntimeValue ExpandDeclaration(
        SchemaDeclaration decl,
        IReadOnlyDictionary<string, SchemaDeclaration> lookup,
        HashSet<string> expanding)
    {
        if (!expanding.Add(decl.Name))
        {
            throw new Exception(
                $"Cyclic schema reference involving '{decl.Name}'.");
        }

        var properties = new JsonObject();
        var required = new List<RuntimeValue>();

        foreach (var field in decl.Fields)
        {
            var propertySchema = BuildFieldSchema(field.TypeName, lookup, expanding);
            properties.Set(field.Name, RuntimeValue.Object(propertySchema));
            if (field.Required)
                required.Add(RuntimeValue.String(field.Name));
        }

        expanding.Remove(decl.Name);

        var root = new JsonObject();
        root.Set("type", RuntimeValue.String("object"));
        root.Set("properties", RuntimeValue.Object(properties));
        if (required.Count > 0)
            root.Set("required", RuntimeValue.Array(required));
        return RuntimeValue.Object(root);
    }

    private static JsonObject BuildFieldSchema(
        string typeName,
        IReadOnlyDictionary<string, SchemaDeclaration> lookup,
        HashSet<string> expanding)
    {
        var trimmed = typeName.Trim();
        if (trimmed.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementType = trimmed[..^2].Trim();
            var arraySchema = new JsonObject();
            arraySchema.Set("type", RuntimeValue.String("array"));
            arraySchema.Set("items", RuntimeValue.Object(ResolveNamedType(elementType, lookup, expanding)));
            return arraySchema;
        }

        return ResolveNamedType(trimmed, lookup, expanding);
    }

    private static JsonObject ResolveNamedType(
        string typeName,
        IReadOnlyDictionary<string, SchemaDeclaration> lookup,
        HashSet<string> expanding)
    {
        if (TryNormalizePrimitive(typeName, out var jsonType))
        {
            var propertySchema = new JsonObject();
            propertySchema.Set("type", RuntimeValue.String(jsonType));
            return propertySchema;
        }

        if (lookup.TryGetValue(typeName, out var nestedDecl))
        {
            var expanded = ExpandDeclaration(nestedDecl, lookup, expanding);
            if (expanded.Type != ValueType.Object || expanded.AsObject() is not JsonObject nestedObj)
            {
                throw new Exception($"Schema '{typeName}' did not expand to an object schema.");
            }

            var copy = new JsonObject();
            foreach (var key in nestedObj.GetAllKeys())
                copy.Set(key, nestedObj.Get(key));
            return copy;
        }

        throw new Exception(
            $"Unknown schema field type '{typeName}'. Use a Tier-0 JSON type (string, int, float, bool, array, object) or a declared schema name.");
    }

    private static bool TryNormalizePrimitive(string typeName, out string jsonType)
    {
        jsonType = typeName.Trim().ToLowerInvariant() switch
        {
            "string" => "string",
            "int" or "integer" => "integer",
            "float" or "double" or "number" => "number",
            "bool" or "boolean" => "boolean",
            "array" or "list" => "array",
            "object" or "json" => "object",
            "null" => "null",
            _ => ""
        };
        return jsonType.Length > 0;
    }

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
