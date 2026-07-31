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

    private static JsonObject MakeTypeObj(string typeName)
    {
        var o = new JsonObject();
        o.Set("type", RuntimeValue.String(typeName));
        return o;
    }
}
