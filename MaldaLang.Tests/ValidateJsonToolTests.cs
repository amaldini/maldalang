// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ValidateJsonToolTests : TestBase
{
    [Fact]
    public void InterpretSnippet_ValidValue_OkTrue()
    {
        var output = RunProgram(@"
schema User { name: string; }
var tool = createValidateJsonTool();
var result = tool.execute({ ""schema"": ""User"", ""value"": dict { ""name"": ""Ada"" } });
print(""ok="" + string(result.ok));
");
        Assert.Contains("ok=true", output);
    }

    [Fact]
    public void InterpretSnippet_InvalidValue_OkFalseWithError()
    {
        var output = RunProgram(@"
schema User { name: string; }
var tool = createValidateJsonTool();
var result = tool.execute({ ""schema"": ""User"", ""value"": dict { ""name"": 1 } });
print(""ok="" + string(result.ok));
print(""err="" + string(result.error));
");
        Assert.Contains("ok=false", output);
        Assert.Contains("err=", output);
        Assert.DoesNotContain("err=\n", output);
        Assert.DoesNotContain("err=\r", output);
    }

    [Fact]
    public void ToolExecute_AfterInterpretingSchema_ReturnsOkTrue()
    {
        Interpret("schema User { name: string; }");
        var toolVal = BuiltInTools.CreateValidateJsonTool();
        var tool = Assert.IsType<ToolInstance>(toolVal.AsObject());
        Assert.Equal("validate_json", tool.Name);

        var value = new JsonObject();
        value.Set("name", RuntimeValue.String("Ada"));
        var result = tool.Execute(Args(("schema", RuntimeValue.String("User")), ("value", RuntimeValue.Object(value))));
        Assert.Equal(ValueType.Object, result.Type);
        Assert.True(result.AsObject().Get("ok", null)!.AsBoolean());
        Assert.DoesNotContain("Tool execution validated", result.ToString());
    }

    [Fact]
    public void MissingParams_OkFalse()
    {
        var tool = Assert.IsType<ToolInstance>(BuiltInTools.CreateValidateJsonTool().AsObject());
        var empty = tool.Execute(RuntimeValue.Object(new JsonObject()));
        Assert.False(empty.AsObject().Get("ok", null)!.AsBoolean());
        Assert.Contains("schema", empty.AsObject().Get("error", null)!.AsString(), StringComparison.OrdinalIgnoreCase);

        var noValue = new JsonObject();
        noValue.Set("schema", RuntimeValue.String("User"));
        var missingValue = tool.Execute(RuntimeValue.Object(noValue));
        Assert.False(missingValue.AsObject().Get("ok", null)!.AsBoolean());
        Assert.Contains("value", missingValue.AsObject().Get("error", null)!.AsString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CallBuiltIn_Validate_AfterSchema_MatchesTool()
    {
        Interpret("schema User { name: string; }");
        var value = new JsonObject();
        value.Set("name", RuntimeValue.String("Ada"));
        var viaBuiltin = BuiltInFunctions.CallBuiltIn(
            "validate",
            new List<RuntimeValue> { RuntimeValue.String("User"), RuntimeValue.Object(value) },
            null);
        Assert.True(viaBuiltin.AsObject().Get("ok", null)!.AsBoolean());
    }

    private static void Interpret(string source)
    {
        SchemaRegistry.ClearForTesting();
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        if (parser.Errors.Count > 0)
            throw parser.Errors[0];
        var interpreter = new Interpreter.Interpreter();
        interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
    }

    private static RuntimeValue Args(params (string Name, RuntimeValue Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (name, value) in fields)
            obj.Set(name, value);
        return RuntimeValue.Object(obj);
    }
}
