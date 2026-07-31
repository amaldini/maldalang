// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using ValueType = MaldaLang.Interpreter.ValueType;

public static class TypedPromptSchemaResolver
{
    public static bool TryResolve(string returnType, Interpreter? interpreter, out RuntimeValue schema, out string error)
    {
        schema = RuntimeValue.Null();
        error = "";

        var normalized = (returnType ?? "").Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            error = "Return type is empty.";
            return false;
        }

        switch (normalized)
        {
            case "string":
            case "String":
                schema = MakePrimitiveSchema("string");
                return true;
            case "int":
            case "Int":
            case "integer":
            case "Integer":
                schema = MakePrimitiveSchema("integer");
                return true;
            case "float":
            case "Float":
            case "double":
            case "Double":
            case "number":
            case "Number":
                schema = MakePrimitiveSchema("number");
                return true;
            case "bool":
            case "Bool":
            case "boolean":
            case "Boolean":
                schema = MakePrimitiveSchema("boolean");
                return true;
            case "array":
            case "Array":
            case "list":
            case "List":
                schema = MakePrimitiveSchema("array");
                return true;
            case "object":
            case "Object":
            case "json":
            case "Json":
                schema = MakePrimitiveSchema("object");
                return true;
            case "Plan":
                schema = BuildPlanSchema();
                return true;
        }

        if (SchemaRegistry.TryResolve(normalized, out schema))
            return true;

        if (interpreter != null && interpreter._classes.TryGetValue(normalized, out var classDef))
        {
            schema = BuildClassObjectSchema(classDef);
            return true;
        }

        error = $"Unknown typed prompt return type '{normalized}'.";
        return false;
    }

    /// <summary>
    /// Builds a JSON schema RuntimeValue from a class declaration (AST).
    /// Used by the transpiler at compile time so custom class return types can be validated without an interpreter.
    /// </summary>
    public static RuntimeValue BuildSchemaFromClassDeclaration(ClassDeclaration decl, IReadOnlyDictionary<string, ClassDeclaration> allClasses)
    {
        var root = new JsonObject();
        root.Set("type", RuntimeValue.String("object"));
        var properties = new JsonObject();
        var required = new List<RuntimeValue>();
        AddClassDeclarationFields(decl, allClasses, properties, required);
        root.Set("properties", RuntimeValue.Object(properties));
        root.Set("required", RuntimeValue.Array(required));
        return RuntimeValue.Object(root);
    }

    private static void AddClassDeclarationFields(ClassDeclaration? decl, IReadOnlyDictionary<string, ClassDeclaration> allClasses, JsonObject properties, List<RuntimeValue> required)
    {
        if (decl == null)
            return;
        if (decl.Superclass != null && allClasses.TryGetValue(decl.Superclass, out var superDecl))
            AddClassDeclarationFields(superDecl, allClasses, properties, required);
        foreach (var member in decl.Members)
        {
            if (member.Type != MemberType.Field)
                continue;
            if (member.Access == AccessModifier.Private)
                continue;
            if (member.IsStatic)
                continue;
            if (properties.Get(member.Name).Type != ValueType.Null)
                continue;
            properties.Set(member.Name, RuntimeValue.Object(MakeTypeObject("object")));
            required.Add(RuntimeValue.String(member.Name));
        }
    }

    private static RuntimeValue MakePrimitiveSchema(string typeName)
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String(typeName));
        return RuntimeValue.Object(schema);
    }

    private static RuntimeValue BuildPlanSchema()
    {
        var stepItem = new JsonObject();
        stepItem.Set("type", RuntimeValue.String("object"));

        var stepProps = new JsonObject();
        stepProps.Set("id", RuntimeValue.Object(MakeTypeObject("string")));
        stepProps.Set("description", RuntimeValue.Object(MakeTypeObject("string")));

        var dependsOnSchema = new JsonObject();
        dependsOnSchema.Set("type", RuntimeValue.String("array"));
        dependsOnSchema.Set("items", RuntimeValue.Object(MakeTypeObject("string")));
        stepProps.Set("dependsOn", RuntimeValue.Object(dependsOnSchema));

        stepItem.Set("properties", RuntimeValue.Object(stepProps));
        stepItem.Set("required", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("id"),
            RuntimeValue.String("description")
        }));

        var stepsSchema = new JsonObject();
        stepsSchema.Set("type", RuntimeValue.String("array"));
        stepsSchema.Set("items", RuntimeValue.Object(stepItem));

        var root = new JsonObject();
        root.Set("type", RuntimeValue.String("object"));

        var rootProps = new JsonObject();
        rootProps.Set("steps", RuntimeValue.Object(stepsSchema));
        rootProps.Set("planId", RuntimeValue.Object(MakeTypeObject("string")));
        rootProps.Set("taskSummary", RuntimeValue.Object(MakeTypeObject("string")));
        root.Set("properties", RuntimeValue.Object(rootProps));
        root.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("steps") }));

        return RuntimeValue.Object(root);
    }

    private static RuntimeValue BuildClassObjectSchema(ClassDefinition classDef)
    {
        var root = new JsonObject();
        root.Set("type", RuntimeValue.String("object"));

        var properties = new JsonObject();
        var required = new List<RuntimeValue>();
        AddClassFields(classDef, properties, required);

        root.Set("properties", RuntimeValue.Object(properties));
        root.Set("required", RuntimeValue.Array(required));
        return RuntimeValue.Object(root);
    }

    private static void AddClassFields(ClassDefinition? classDef, JsonObject properties, List<RuntimeValue> required)
    {
        if (classDef == null)
            return;

        AddClassFields(classDef.Superclass, properties, required);

        foreach (var kv in classDef.Fields)
        {
            if (kv.Value.Type != MemberType.Field)
                continue;
            if (kv.Value.Access == AccessModifier.Private)
                continue;

            var fieldName = kv.Key;
            if (properties.Get(fieldName).Type != ValueType.Null)
                continue;

            properties.Set(fieldName, RuntimeValue.Object(MakeTypeObject("object")));
            required.Add(RuntimeValue.String(fieldName));
        }
    }

    private static JsonObject MakeTypeObject(string typeName)
    {
        var obj = new JsonObject();
        obj.Set("type", RuntimeValue.String(typeName));
        return obj;
    }
}
