// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class PropertyDeclarationTests : TestBase
{
    private static (List<MaldaLang.Parser.AST.Statements.Statement> statements, MaldaLang.Parser.Parser parser) Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new MaldaLang.Parser.Parser(tokens);
        var statements = parser.Parse();
        return (statements, parser);
    }

    [Fact]
    public void Lexer_RecognizesPropertyKeyword()
    {
        var lexer = new Lexer("property check(x) { return x; } propertyValue;");
        var tokens = lexer.Tokenize();

        Assert.Equal(TokenType.Property, tokens[0].Type);
        Assert.Equal(TokenType.Identifier, tokens[^3].Type);
        Assert.Equal("propertyValue", tokens[^3].Lexeme);
    }

    [Fact]
    public void Parser_ParsesPropertyDeclaration_WithOptionalParameters()
    {
        var source = @"
property noParams {
    var x = 1;
}

property hasParams(a, b) {
    return a;
}
";
        var (statements, parser) = Parse(source);

        Assert.Empty(parser.Errors);
        Assert.Equal(2, statements.Count);

        var noParams = Assert.IsType<PropertyDeclaration>(statements[0]);
        Assert.Equal("noParams", noParams.Name);
        Assert.Empty(noParams.Parameters);

        var hasParams = Assert.IsType<PropertyDeclaration>(statements[1]);
        Assert.Equal("hasParams", hasParams.Name);
        Assert.Equal(new[] { "a", "b" }, hasParams.Parameters);
    }

    [Fact]
    public void Parser_ReportsError_ForInvalidPropertyDeclaration()
    {
        var source = "property missingBody() return 1;";
        var (_, parser) = Parse(source);

        Assert.NotEmpty(parser.Errors);
        Assert.Contains(parser.Errors, e => e.Message.Contains("Expect '{' before property body."));
    }

    [Fact]
    public void Parser_ParsesDecoratedPropertyDeclaration_WithCapabilityMetadata()
    {
        var source = """
@requires("core", "actors")
@targets("interpreter", "csharp", "js")
property decoratedProperty(x) {
    return x == x;
}
""";

        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);

        var property = Assert.IsType<PropertyDeclaration>(Assert.Single(statements));
        Assert.Equal("decoratedProperty", property.Name);
        Assert.Equal(new[] { "core", "actors" }, property.GetRequiredCapabilities());
        Assert.Equal(new[] { "interpreter", "csharp", "js" }, property.GetTargetModes());
    }

    [Fact]
    public void Interpreter_RegistersProperties_AsDeclarationLevelStatements()
    {
        var source = @"
property onlyDecl {
    print(""should-not-run"");
}

print(""ok"");
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);

        var interpreter = new MaldaLang.Interpreter.Interpreter();
        interpreter.InterpretAsync(statements).GetAwaiter().GetResult();

        var propertiesField = typeof(MaldaLang.Interpreter.Interpreter).GetField("_properties", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(propertiesField);

        var properties = Assert.IsType<Dictionary<string, PropertyDeclaration>>(propertiesField!.GetValue(interpreter));
        Assert.True(properties.ContainsKey("onlyDecl"));
    }

    [Fact]
    public async Task Interpreter_ThrowsOnDuplicatePropertyNames()
    {
        var source = @"
property duplicate {
    return 1;
}

property duplicate {
    return 2;
}
";
        var (statements, parser) = Parse(source);
        Assert.Empty(parser.Errors);

        var interpreter = new MaldaLang.Interpreter.Interpreter();
        var ex = await Assert.ThrowsAsync<RuntimeException>(() => interpreter.InterpretAsync(statements));
        Assert.Contains("already defined", ex.Message);
        Assert.Contains("duplicate", ex.Message);
    }
}
