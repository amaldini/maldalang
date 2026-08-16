// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class TypeHintCompletionTests
{
    private readonly LanguageService _service = new();

    [Theory]
    [InlineData("var x: i", 0, 8, "i")]
    [InlineData("var count: ", 0, 11, "")]
    [InlineData("function add(x: in", 0, 18, "in")]
    [InlineData("function greet(name: str", 0, 24, "str")]
    [InlineData("function f() -> fl", 0, 18, "fl")]
    [InlineData("function f() => int", 0, 19, "int")]
    public void GetTypeHintPartialPrefix_DetectsContext(string source, int line, int column, string expectedPrefix)
    {
        var prefix = MaldaLang.IDE.TypeHintCompletions.GetTypeHintPartialPrefix(source, line, column);
        Assert.Equal(expectedPrefix, prefix);
    }

    [Theory]
    [InlineData("var x = 1;", 0, 5)]
    [InlineData("dict { \"a\": 1 };", 0, 12)]
    [InlineData("print(x: 1);", 0, 8)]
    public void GetTypeHintPartialPrefix_OutsideHintContext_ReturnsNull(string source, int line, int column)
    {
        Assert.Null(MaldaLang.IDE.TypeHintCompletions.GetTypeHintPartialPrefix(source, line, column));
    }

    [Fact]
    public void GetCompletions_AfterVarColon_OffersTypeHints()
    {
        var source = "var x: ";
        var completions = _service.GetCompletions(source, 0, source.Length);
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "int");
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "string");
        Assert.DoesNotContain(completions, c => c.Kind == "type" && c.Label == "print");
    }

    [Fact]
    public void GetCompletions_AfterVarColonWithPrefix_FiltersTypes()
    {
        var source = "var x: str";
        var completions = _service.GetCompletions(source, 0, source.Length);
        Assert.Contains(completions, c => c.Label == "string");
        Assert.DoesNotContain(completions, c => c.Kind == "type" && c.Label == "int");
    }

    [Fact]
    public void GetCompletions_AfterReturnArrow_OffersTypeHints()
    {
        var source = "function f() -> ";
        var completions = _service.GetCompletions(source, 0, source.Length);
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "void");
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "int");
    }

    [Fact]
    public void GetCompletions_ParameterTypeHint_OffersMatchingTypes()
    {
        var source = "function add(a: i";
        var completions = _service.GetCompletions(source, 0, source.Length);
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "int");
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "integer");
    }

    [Fact]
    public void GetCompletions_AfterVarColon_OffersDeclaredClass()
    {
        var source = """
            class Person {
                var name: string = "";
            }
            var p: 
            """;
        var line = 3;
        var column = "var p: ".Length;
        var completions = _service.GetCompletions(source, line, column);
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "Person");
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "int");
    }

    [Fact]
    public void GetCompletions_AfterVarColon_OffersHostClass()
    {
        var source = "var s: Rest";
        var completions = _service.GetCompletions(source, 0, source.Length);
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "RestServer");
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "RestClient");
    }

    [Fact]
    public void GetCompletions_PromptReturnArrow_OffersDeclaredSchema()
    {
        var source = """
            schema Person {
                name: string;
            }
            prompt greet() -> 
            """;
        var line = 3;
        var column = "prompt greet() -> ".Length;
        var completions = _service.GetCompletions(source, line, column);
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "Person");
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "string");
    }

    [Fact]
    public void GetCompletions_SchemaFieldColon_OffersTypeHints()
    {
        var source = """
            schema Person {
                name: 
            """;
        var line = 1;
        var column = "    name: ".Length;
        var completions = _service.GetCompletions(source, line, column);
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "string");
        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "int");
    }
}
