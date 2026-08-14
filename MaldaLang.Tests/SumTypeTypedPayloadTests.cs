// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class SumTypeTypedPayloadTests : TestBase
{
    public SumTypeTypedPayloadTests()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
    }

    [Fact]
    public void Parser_NameOnlyConstructors_StillAccepted()
    {
        var typeDecl = ParseSingleType("type Intent = Search(query) | Buy(sku, qty) | Help();");
        Assert.Equal(3, typeDecl.Constructors.Count);
        Assert.Equal(new[] { "query" }, typeDecl.Constructors[0].ParameterNames);
        Assert.Null(typeDecl.Constructors[0].ParameterTypeAt(0));
        Assert.Equal(new[] { "sku", "qty" }, typeDecl.Constructors[1].ParameterNames);
        Assert.Null(typeDecl.Constructors[1].ParameterTypeAt(0));
        Assert.Null(typeDecl.Constructors[1].ParameterTypeAt(1));
        Assert.Empty(typeDecl.Constructors[2].ParameterNames);
    }

    [Fact]
    public void Parser_TypedPayloads_AndMixedArms()
    {
        var typeDecl = ParseSingleType(
            "type Intent = Search(query: string) | Buy(sku: string, qty: int) | Help();");
        var search = typeDecl.Constructors[0];
        Assert.Equal("string", search.ParameterTypeAt(0));
        Assert.True(search.ParameterRequiredAt(0));

        var buy = typeDecl.Constructors[1];
        Assert.Equal("string", buy.ParameterTypeAt(0));
        Assert.Equal("int", buy.ParameterTypeAt(1));

        Assert.Empty(typeDecl.Constructors[2].ParameterNames);
    }

    [Fact]
    public void Parser_OptionalAndArrayPayloadTypes()
    {
        var typeDecl = ParseSingleType(
            "type Packet = Note(text: string?, tags: string[]);");
        var note = Assert.Single(typeDecl.Constructors);
        Assert.Equal("text", note.ParameterNames[0]);
        Assert.Equal("string", note.ParameterTypeAt(0));
        Assert.False(note.ParameterRequiredAt(0));
        Assert.Equal("tags", note.ParameterNames[1]);
        Assert.Equal("string[]", note.ParameterTypeAt(1));
        Assert.True(note.ParameterRequiredAt(1));
    }

    [Fact]
    public void Parser_PromptParametersStayNameOnly()
    {
        var source = """
            prompt greet(name: string) {
                user: "hi";
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        _ = parser.Parse();
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void Validate_TypedPayload_RejectsWrongJsonType()
    {
        var source = """
            type Intent = Search(query: string) | Buy(sku: string, qty: int);
            var bad = dict { "tag": "Buy", "sku": "SKU-9", "qty": "x" };
            print(validate("Intent", bad).ok);
            var good = dict { "tag": "Buy", "sku": "SKU-9", "qty": 2 };
            print(validate("Intent", good).ok);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("false", lines[0].Trim());
        Assert.Equal("true", lines[1].Trim());
    }

    [Fact]
    public void Validate_OptionalPayload_MayBeOmitted()
    {
        var source = """
            type Packet = Note(text: string?, tags: string[]);
            var missingText = dict { "tag": "Note", "tags": ["a"] };
            print(validate("Packet", missingText).ok);
            var badTags = dict { "tag": "Note", "tags": [1] };
            print(validate("Packet", badTags).ok);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("false", lines[1].Trim());
    }

    [Fact]
    public void Validate_NameOnlyArm_StaysPermissive()
    {
        var source = """
            type Intent = Search(query) | Buy(sku: string, qty: int);
            var untyped = dict { "tag": "Search", "query": 99 };
            print(validate("Intent", untyped).ok);
            """;
        var result = RunProgram(source);
        Assert.Contains("true", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NestedSchemaNameOnPayload()
    {
        var source = """
            schema Address {
                city: string;
            }
            type Intent = Visit(addr: Address) | Stay();
            var good = dict {
                "tag": "Visit",
                "addr": dict { "city": "Rome" }
            };
            var bad = dict {
                "tag": "Visit",
                "addr": dict { "city": 1 }
            };
            print(validate("Intent", good).ok);
            print(validate("Intent", bad).ok);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("false", lines[1].Trim());
    }

    [Fact]
    public void Validate_UnknownPayloadType_ThrowsOnResolve()
    {
        var source = """
            type Intent = Buy(sku: NotAType);
            print(validate("Intent", dict { "tag": "Buy", "sku": "x" }).ok);
            """;
        var ex = Assert.ThrowsAny<Exception>(() => RunProgram(source));
        Assert.Contains("Unknown schema field type", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NotAType", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RecursiveSumType_DoesNotThrow()
    {
        var source = """
            type Node = Branch(left: Node, right: Node) | Leaf(value: int);
            var leaf = dict { "tag": "Leaf", "value": 1 };
            print(validate("Node", leaf).ok);
            var badLeaf = dict { "tag": "Leaf", "value": "x" };
            print(validate("Node", badLeaf).ok);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("false", lines[1].Trim());
    }

    [Fact]
    public void SchemaEmit_TypedFields_AreJsonTypes()
    {
        var typeDecl = ParseSingleType(
            "type Intent = Search(query: string) | Buy(sku: string, qty: int);");
        SumTypeRegistry.Register(typeDecl);
        Assert.True(SumTypeRegistry.TryResolve("Intent", out var schema));
        var buy = FindArm(schema, "Buy");
        var props = buy.Get("properties").AsObject() as JsonObject;
        Assert.NotNull(props);
        var skuType = (props!.Get("sku").AsObject() as JsonObject)!.Get("type").AsString();
        var qtyType = (props.Get("qty").AsObject() as JsonObject)!.Get("type").AsString();
        Assert.Equal("string", skuType);
        Assert.Equal("integer", qtyType);
    }

    [Fact]
    public void SchemaEmit_UntypedField_StaysPermissive()
    {
        var typeDecl = ParseSingleType("type Intent = Search(query);");
        SumTypeRegistry.Register(typeDecl);
        Assert.True(SumTypeRegistry.TryResolve("Intent", out var schema));
        var search = FindArm(schema, "Search");
        var props = search.Get("properties").AsObject() as JsonObject;
        Assert.NotNull(props);
        var querySchema = props!.Get("query").AsObject() as JsonObject;
        Assert.NotNull(querySchema);
        Assert.Equal(ValueType.Array, querySchema!.Get("type").Type);
    }

    [Fact]
    public void Transpiled_TypedPayloadValidate_Works()
    {
        var source = """
            type Intent = Search(query: string) | Buy(sku: string, qty: int);
            var bad = dict { "tag": "Buy", "sku": "SKU-9", "qty": "x" };
            print(validate("Intent", bad).ok);
            var good = dict { "tag": "Buy", "sku": "SKU-9", "qty": 2 };
            print(validate("Intent", good).ok);
            """;
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("false", lines[0].Trim());
        Assert.Equal("true", lines[1].Trim());
    }

    [Fact]
    public void Transpiled_NestedSchemaPayload_Works()
    {
        var source = """
            schema Address {
                city: string;
            }
            type Intent = Visit(addr: Address) | Stay();
            var good = dict {
                "tag": "Visit",
                "addr": dict { "city": "Oslo" }
            };
            print(validate("Intent", good).ok);
            """;
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("true", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    private static TypeDeclaration ParseSingleType(string source)
    {
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return Assert.IsType<TypeDeclaration>(Assert.Single(statements));
    }

    private static JsonObject FindArm(RuntimeValue schema, string tag)
    {
        var root = Assert.IsType<JsonObject>(schema.AsObject());
        foreach (var armVal in root.Get("oneOf").AsArray())
        {
            var arm = Assert.IsType<JsonObject>(armVal.AsObject());
            var props = Assert.IsType<JsonObject>(arm.Get("properties").AsObject());
            var tagSchema = Assert.IsType<JsonObject>(props.Get("tag").AsObject());
            if (tagSchema.Get("const").AsString() == tag)
                return arm;
        }

        throw new InvalidOperationException($"No oneOf arm with tag '{tag}'.");
    }
}
