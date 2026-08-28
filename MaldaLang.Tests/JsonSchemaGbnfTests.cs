// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class JsonSchemaGbnfTests
{
    public JsonSchemaGbnfTests()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
    }

    [Fact]
    public void ObjectSchema_EmitsPropertyKeysAndStringRule()
    {
        SchemaRegistry.Register(new SchemaDeclaration("Card", new List<SchemaField>
        {
            new("name", "string", required: true),
            new("email", "string", required: true)
        }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Card", null, out var schema, out var resolveError), resolveError);
        Assert.True(JsonSchemaGbnf.TryFromSchema(schema, out var gbnf, out var error), error);

        Assert.Contains("root ::= ", gbnf, StringComparison.Ordinal);
        Assert.Contains("string ::=", gbnf, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\"", gbnf, StringComparison.Ordinal);
        Assert.Contains("\\\"email\\\"", gbnf, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", gbnf.Replace("\r", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void SumType_EmitsTagConsts()
    {
        SumTypeRegistry.Register(new TypeDeclaration(
            "Intent",
            new List<VariantConstructor>
            {
                new("Search", new List<string> { "query" }, new List<string?> { "string" }, null),
                new("Buy", new List<string> { "sku", "qty" }, new List<string?> { "string", "int" }, null),
                new("Help", new List<string>())
            }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Intent", null, out var schema, out var resolveError), resolveError);
        var responseFormat = TypedPromptValidator.BuildResponseFormat(schema);
        Assert.True(JsonSchemaGbnf.TryFromResponseFormat(responseFormat, out var gbnf, out var error), error);

        Assert.Contains("\\\"tag\\\"", gbnf, StringComparison.Ordinal);
        Assert.Contains("\\\"Search\\\"", gbnf, StringComparison.Ordinal);
        Assert.Contains("\\\"Buy\\\"", gbnf, StringComparison.Ordinal);
        Assert.Contains("\\\"Help\\\"", gbnf, StringComparison.Ordinal);
        Assert.Contains("integer ::=", gbnf, StringComparison.Ordinal);
        Assert.Contains(" | ", gbnf, StringComparison.Ordinal);
    }

    [Fact]
    public void ArrayField_EmitsItemRepetition()
    {
        SchemaRegistry.Register(new SchemaDeclaration("Bag", new List<SchemaField>
        {
            new("tags", "string[]", required: true)
        }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Bag", null, out var schema, out var resolveError), resolveError);
        Assert.True(JsonSchemaGbnf.TryFromSchema(schema, out var gbnf, out var error), error);
        Assert.Contains("\\\"tags\\\"", gbnf, StringComparison.Ordinal);
        Assert.Contains("\"[\"", gbnf, StringComparison.Ordinal);
        Assert.Contains("string", gbnf, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalProperties_AreSuffixOptional()
    {
        SchemaRegistry.Register(new SchemaDeclaration("Card", new List<SchemaField>
        {
            new("name", "string", required: true),
            new("nick", "string", required: false)
        }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("Card", null, out var schema, out var resolveError), resolveError);
        Assert.True(JsonSchemaGbnf.TryFromSchema(schema, out var gbnf, out var error), error);
        Assert.Contains("\\\"name\\\"", gbnf, StringComparison.Ordinal);
        Assert.Contains("\\\"nick\\\"", gbnf, StringComparison.Ordinal);
        Assert.Contains(")?", gbnf, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolsListed_DoesNotConstrain()
    {
        Assert.False(JsonSchemaGbnf.ShouldConstrain(RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("read_file")
        })));
        Assert.True(JsonSchemaGbnf.ShouldConstrain(null));
        Assert.True(JsonSchemaGbnf.ShouldConstrain(RuntimeValue.Array(new List<RuntimeValue>())));
    }

    [Fact]
    public void LlamaCppClient_BuildsGrammarForModeA()
    {
        SchemaRegistry.Register(new SchemaDeclaration("Card", new List<SchemaField>
        {
            new("name", "string", required: true)
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("Card", null, out var schema, out var resolveError), resolveError);
        var format = TypedPromptValidator.BuildResponseFormat(schema);

        Assert.True(LlamaCppClientInstance.TryGetConstrainedGrammar(format, tools: null, out var grammar));
        Assert.NotNull(grammar);
        Assert.Equal(JsonSchemaGbnf.RootRule, grammar!.Root);
        Assert.Contains("root ::=", grammar.Gbnf, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\"", grammar.Gbnf, StringComparison.Ordinal);
    }

    [Fact]
    public void LlamaCppClient_SkipsGrammarWhenToolsPresent()
    {
        SchemaRegistry.Register(new SchemaDeclaration("Card", new List<SchemaField>
        {
            new("name", "string", required: true)
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("Card", null, out var schema, out _));
        var format = TypedPromptValidator.BuildResponseFormat(schema);
        var tools = RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.String("read_file") });

        Assert.False(LlamaCppClientInstance.TryGetConstrainedGrammar(format, tools, out var grammar));
        Assert.Null(grammar);
    }

    [Fact]
    public void MissingResponseFormat_FailsUnwrap()
    {
        Assert.False(JsonSchemaGbnf.TryFromResponseFormat(null, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
