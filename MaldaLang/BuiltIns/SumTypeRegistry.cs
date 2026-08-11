// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Registers sum-type declarations for typed prompt return types (<c>prompt p() -&gt; Intent</c>).
/// </summary>
public static class SumTypeRegistry
{
    public sealed class SumTypeDefinition
    {
        public SumTypeDefinition(string typeName, IReadOnlyList<VariantConstructor> constructors)
        {
            TypeName = typeName;
            Constructors = constructors;
        }

        public string TypeName { get; }
        public IReadOnlyList<VariantConstructor> Constructors { get; }
    }

    private static readonly Dictionary<string, SumTypeDefinition> Definitions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RuntimeValue> Schemas = new(StringComparer.Ordinal);

    public static void ClearForTesting()
    {
        Definitions.Clear();
        Schemas.Clear();
    }

    public static void Register(TypeDeclaration decl)
    {
        if (SchemaRegistry.IsRegistered(decl.TypeName))
        {
            throw new Exception(
                $"Name '{decl.TypeName}' is already registered as a schema; cannot also declare a sum type.");
        }

        if (ApiRegistry.IsRegistered(decl.TypeName))
        {
            throw new Exception(
                $"Name '{decl.TypeName}' is already registered as an api; cannot also declare a sum type.");
        }

        var def = new SumTypeDefinition(decl.TypeName, decl.Constructors.ToList());
        Definitions[decl.TypeName] = def;
        Schemas[decl.TypeName] = BuildSchema(def);
    }

    /// <summary>Transpiled programs register pre-built sum-type schemas at startup.</summary>
    public static void RegisterCompiled(string name, RuntimeValue schema)
    {
        if (SchemaRegistry.IsRegistered(name))
        {
            throw new Exception(
                $"Name '{name}' is already registered as a schema; cannot also register a sum type.");
        }

        if (ApiRegistry.IsRegistered(name))
        {
            throw new Exception(
                $"Name '{name}' is already registered as an api; cannot also register a sum type.");
        }

        Definitions[name] = ReconstructDefinition(name, schema);
        Schemas[name] = schema;
    }

    private static SumTypeDefinition ReconstructDefinition(string typeName, RuntimeValue schema)
    {
        var constructors = new List<VariantConstructor>();
        if (schema.Type == ValueType.Object && schema.AsObject() is JsonObject root)
        {
            var oneOf = root.Get("oneOf");
            if (oneOf.Type == ValueType.Array)
            {
                foreach (var armVal in oneOf.AsArray())
                {
                    if (armVal.Type != ValueType.Object || armVal.AsObject() is not JsonObject arm)
                        continue;
                    var propsVal = arm.Get("properties");
                    if (propsVal.Type != ValueType.Object || propsVal.AsObject() is not JsonObject props)
                        continue;

                    var tagName = "";
                    var tagProp = props.Get("tag");
                    if (tagProp.Type == ValueType.Object && tagProp.AsObject() is JsonObject tagSchema)
                    {
                        var constVal = tagSchema.Get("const");
                        if (constVal.Type == ValueType.String)
                            tagName = constVal.AsString();
                    }

                    var paramNames = new List<string>();
                    foreach (var key in props.GetAllKeys())
                    {
                        if (string.Equals(key, "tag", StringComparison.Ordinal))
                            continue;
                        paramNames.Add(key);
                    }

                    if (!string.IsNullOrEmpty(tagName))
                        constructors.Add(new VariantConstructor(tagName, paramNames));
                }
            }
        }

        return new SumTypeDefinition(typeName, constructors);
    }

    public static bool IsRegistered(string name) => Definitions.ContainsKey(name);

    public static bool TryResolve(string name, out RuntimeValue schema)
    {
        if (Schemas.TryGetValue(name, out schema!))
            return true;

        schema = RuntimeValue.Null();
        return false;
    }

    public static bool TryGetDefinition(string name, out SumTypeDefinition definition)
    {
        if (Definitions.TryGetValue(name, out definition!))
            return true;

        definition = null!;
        return false;
    }

    public static RuntimeValue BuildSchema(TypeDeclaration decl) =>
        BuildSchema(new SumTypeDefinition(decl.TypeName, decl.Constructors));

    public static RuntimeValue BuildSchema(SumTypeDefinition def)
    {
        var oneOf = new List<RuntimeValue>();
        foreach (var ctor in def.Constructors)
            oneOf.Add(RuntimeValue.Object(BuildConstructorArm(ctor)));

        var root = new JsonObject();
        root.Set("x-malda-kind", RuntimeValue.String("sum"));
        root.Set("x-malda-sum-type", RuntimeValue.String(def.TypeName));
        root.Set("oneOf", RuntimeValue.Array(oneOf));
        return RuntimeValue.Object(root);
    }

    private static JsonObject BuildConstructorArm(VariantConstructor ctor)
    {
        var properties = new JsonObject();
        var tagSchema = new JsonObject();
        tagSchema.Set("type", RuntimeValue.String("string"));
        tagSchema.Set("const", RuntimeValue.String(ctor.Name));
        properties.Set("tag", RuntimeValue.Object(tagSchema));

        var required = new List<RuntimeValue> { RuntimeValue.String("tag") };
        foreach (var param in ctor.ParameterNames)
        {
            properties.Set(param, RuntimeValue.Object(MakePermissiveValueSchema()));
            required.Add(RuntimeValue.String(param));
        }

        var arm = new JsonObject();
        arm.Set("type", RuntimeValue.String("object"));
        arm.Set("properties", RuntimeValue.Object(properties));
        arm.Set("required", RuntimeValue.Array(required));
        arm.Set("additionalProperties", RuntimeValue.Boolean(false));
        return arm;
    }

    /// <summary>Payload params are name-only in the language; accept any JSON value.</summary>
    private static JsonObject MakePermissiveValueSchema()
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("string"),
            RuntimeValue.String("number"),
            RuntimeValue.String("integer"),
            RuntimeValue.String("boolean"),
            RuntimeValue.String("object"),
            RuntimeValue.String("array"),
            RuntimeValue.String("null")
        }));
        return schema;
    }
}
