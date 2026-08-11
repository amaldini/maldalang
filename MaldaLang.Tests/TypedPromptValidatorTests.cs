// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using Xunit;

namespace MaldaLang.Tests;

public class TypedPromptValidatorTests
{
    [Fact]
    public void PromptInstance_WhenConstructedWithResponseFormatSchema_HasPropertySet()
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("object"));
        var responseFormat = TypedPromptValidator.BuildResponseFormat(RuntimeValue.Object(schema));

        var pi = new PromptInstance("sys", "user", null, null, null, null, responseFormat);

        Assert.NotNull(pi.ResponseFormatSchema);
        Assert.Equal(ValueType.Object, pi.ResponseFormatSchema!.Type);
    }
    [Fact]
    public void ValidateReturnType_Plan_SucceedsForValidStructure()
    {
        var step = new JsonObject();
        step.Set("id", RuntimeValue.String("1"));
        step.Set("description", RuntimeValue.String("Do work"));
        step.Set("dependsOn", RuntimeValue.Array(new List<RuntimeValue>()));

        var plan = new JsonObject();
        plan.Set("steps", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(step) }));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(plan),
            "Plan",
            interpreter: null,
            out var error);

        Assert.True(ok);
        Assert.True(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ValidateReturnType_Plan_FailsWhenStepsMissing()
    {
        var plan = new JsonObject();
        plan.Set("taskSummary", RuntimeValue.String("No steps provided"));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(plan),
            "Plan",
            interpreter: null,
            out var error);

        Assert.False(ok);
        Assert.Contains("$.steps", error);
    }

    [Fact]
    public void ExtractJsonCandidate_PullsJsonFromMarkdownFence()
    {
        var content = """
            Here is the output:
            ```json
            { "steps": [ { "id": "1", "description": "A" } ] }
            ```
            """;

        var ok = TypedPromptValidator.TryExtractJsonCandidate(content, out var json, out var error);
        Assert.True(ok);
        Assert.True(string.IsNullOrEmpty(error));
        Assert.Contains("\"steps\"", json);
    }

    [Fact]
    public void ValidateReturnType_WithPreResolvedSchema_SucceedsForValidObject()
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("object"));
        var props = new JsonObject();
        props.Set("name", RuntimeValue.Object(MakeTypeObj("string")));
        props.Set("count", RuntimeValue.Object(MakeTypeObj("integer")));
        schema.Set("properties", RuntimeValue.Object(props));
        schema.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("name"), RuntimeValue.String("count") }));

        var value = new JsonObject();
        value.Set("name", RuntimeValue.String("test"));
        value.Set("count", RuntimeValue.Integer(42));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(value),
            RuntimeValue.Object(schema),
            out var err);

        Assert.True(ok);
        Assert.True(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void ValidateReturnType_WithPreResolvedSchema_FailsWhenRequiredFieldMissing()
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("object"));
        var props = new JsonObject();
        props.Set("name", RuntimeValue.Object(MakeTypeObj("string")));
        schema.Set("properties", RuntimeValue.Object(props));
        schema.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("name") }));

        var value = new JsonObject();

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(value),
            RuntimeValue.Object(schema),
            out var err);

        Assert.False(ok);
        Assert.Contains("$.name", err);
    }

    [Fact]
    public void BuildResponseFormat_WrapsSchemaInOpenAIFormat()
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("object"));
        var props = new JsonObject();
        props.Set("name", RuntimeValue.Object(MakeTypeObj("string")));
        schema.Set("properties", RuntimeValue.Object(props));
        schema.Set("required", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("name") }));

        var result = TypedPromptValidator.BuildResponseFormat(RuntimeValue.Object(schema));

        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject() as JsonObject;
        Assert.NotNull(obj);

        var typeVal = obj!.Get("type");
        Assert.Equal(ValueType.String, typeVal.Type);
        Assert.Equal("json_schema", typeVal.AsString());

        var jsonSchemaVal = obj.Get("json_schema");
        Assert.Equal(ValueType.Object, jsonSchemaVal.Type);
        var jsonSchema = jsonSchemaVal.AsObject() as JsonObject;
        Assert.NotNull(jsonSchema);
        Assert.Equal("typed_prompt_response", jsonSchema!.Get("name").AsString());
        Assert.True(jsonSchema.Get("strict").AsBoolean());
        Assert.NotNull(jsonSchema.Get("schema"));
    }

    [Fact]
    public void FormatSchemaAppendix_ObjectSchema_ListsFields()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
        SchemaRegistry.Register(new MaldaLang.Parser.AST.Declarations.SchemaDeclaration(
            "Contact",
            new List<MaldaLang.Parser.AST.Declarations.SchemaField>
            {
                new("name", "string", required: true),
                new("email", "string", required: false)
            }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Contact", null, out var schema, out _));
        var text = TypedPromptValidator.FormatSchemaAppendix("Contact", schema);
        Assert.Contains("Return type: Contact", text);
        Assert.Contains("name: string", text);
        Assert.Contains("email?: string", text);
    }

    [Fact]
    public void ApplySchemaAppendix_IsIdempotent()
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("string"));
        var once = TypedPromptValidator.ApplySchemaAppendix("You are helpful.", "string", RuntimeValue.Object(schema));
        Assert.Contains(TypedPromptValidator.SchemaAppendixMarker, once);
        var twice = TypedPromptValidator.ApplySchemaAppendix(once, "string", RuntimeValue.Object(schema));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void ValidateReturnType_SumType_CoercesToVariant()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
        SumTypeRegistry.Register(new MaldaLang.Parser.AST.Declarations.TypeDeclaration(
            "Intent",
            new List<MaldaLang.Parser.AST.Declarations.VariantConstructor>
            {
                new("Search", new List<string> { "query" }),
                new("Buy", new List<string> { "sku", "qty" }),
                new("Help", new List<string>())
            }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Intent", null, out var schema, out var resolveError), resolveError);

        var buy = new JsonObject();
        buy.Set("tag", RuntimeValue.String("Buy"));
        buy.Set("sku", RuntimeValue.String("SKU-9"));
        buy.Set("qty", RuntimeValue.Integer(2));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(buy),
            schema,
            out var validated,
            out var error);

        Assert.True(ok, error);
        Assert.Equal(ValueType.Variant, validated.Type);
        var variant = validated.AsVariant();
        Assert.Equal("Buy", variant.Tag);
        Assert.Equal(2, variant.Payload.Count);
        Assert.Equal("SKU-9", variant.Payload[0].AsString());
        Assert.Equal(2, variant.Payload[1].AsInteger());
    }

    [Fact]
    public void ValidateReturnType_SumType_FailsOnUnknownTag()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
        SumTypeRegistry.Register(new MaldaLang.Parser.AST.Declarations.TypeDeclaration(
            "Intent",
            new List<MaldaLang.Parser.AST.Declarations.VariantConstructor>
            {
                new("Help", new List<string>())
            }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Intent", null, out var schema, out _));
        var bad = new JsonObject();
        bad.Set("tag", RuntimeValue.String("Nope"));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(bad),
            schema,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("Nope", error);
    }

    [Fact]
    public void ValidateReturnType_SumType_FailsWhenPayloadMissing()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
        SumTypeRegistry.Register(new MaldaLang.Parser.AST.Declarations.TypeDeclaration(
            "Intent",
            new List<MaldaLang.Parser.AST.Declarations.VariantConstructor>
            {
                new("Buy", new List<string> { "sku", "qty" })
            }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Intent", null, out var schema, out _));
        var incomplete = new JsonObject();
        incomplete.Set("tag", RuntimeValue.String("Buy"));
        incomplete.Set("sku", RuntimeValue.String("SKU-9"));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(incomplete),
            schema,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("qty", error);
    }

    [Fact]
    public void FormatSchemaAppendix_SumType_ListsConstructors()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
        SumTypeRegistry.Register(new MaldaLang.Parser.AST.Declarations.TypeDeclaration(
            "Intent",
            new List<MaldaLang.Parser.AST.Declarations.VariantConstructor>
            {
                new("Search", new List<string> { "query" }),
                new("Help", new List<string>())
            }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Intent", null, out var schema, out _));
        var text = TypedPromptValidator.FormatSchemaAppendix("Intent", schema);
        Assert.Contains("Search(query)", text);
        Assert.Contains("Help()", text);
        Assert.Contains("tag", text);
    }

    private static JsonObject MakeTypeObj(string typeName)
    {
        var o = new JsonObject();
        o.Set("type", RuntimeValue.String(typeName));
        return o;
    }
}
