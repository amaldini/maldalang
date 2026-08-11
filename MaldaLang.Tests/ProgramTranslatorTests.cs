// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaldaLang.BuiltIns;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class ProgramTranslatorTests : TestBase
{
    public ProgramTranslatorTests()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
        ApiRegistry.ClearForTesting();
    }

    [Fact]
    public void ApiRegistry_ResolvesProgramSchema_WithMaldaKind()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" }),
            new("mul", new List<string> { "a", "b" })
        }));

        Assert.True(TypedPromptSchemaResolver.TryResolve("program(Calc)", null, out var schema, out var error), error);
        var obj = schema.AsObject() as JsonObject;
        Assert.NotNull(obj);
        Assert.Equal("program", obj!.Get("x-malda-kind").AsString());
        Assert.Equal("Calc", obj.Get("x-malda-api").AsString());

        var appendix = TypedPromptValidator.FormatSchemaAppendix("program(Calc)", schema);
        Assert.Contains("add(a, b)", appendix);
        Assert.Contains("mul(a, b)", appendix);
    }

    [Fact]
    public void ValidateReturnType_Program_CoercesToProgramInstance()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" })
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("program(Calc)", null, out var schema, out _));

        var json = new JsonObject();
        json.Set("@api", RuntimeValue.String("Calc"));
        var step = new JsonObject();
        step.Set("call", RuntimeValue.String("add"));
        step.Set("args", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Integer(2),
            RuntimeValue.Integer(3)
        }));
        step.Set("as", RuntimeValue.String("t0"));
        json.Set("steps", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(step) }));
        json.Set("return", RuntimeValue.String("$t0"));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(json),
            schema,
            out var validated,
            out var error);

        Assert.True(ok, error);
        Assert.True(validated.AsObject() is ProgramInstance prog && prog.ApiName == "Calc");
    }

    [Fact]
    public void ValidateReturnType_Program_FailsOnUnknownCall()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" })
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("program(Calc)", null, out var schema, out _));

        var json = new JsonObject();
        json.Set("@api", RuntimeValue.String("Calc"));
        var step = new JsonObject();
        step.Set("call", RuntimeValue.String("div"));
        step.Set("args", RuntimeValue.Array(new List<RuntimeValue>()));
        step.Set("as", RuntimeValue.String("t0"));
        json.Set("steps", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(step) }));
        json.Set("return", RuntimeValue.String("$t0"));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(json),
            schema,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("div", error);
    }

    [Fact]
    public void ApiAndSchema_SameName_RegisterThrows()
    {
        SchemaRegistry.Register(new SchemaDeclaration("Foo", new List<SchemaField>
        {
            new("x", "string", required: true)
        }));

        var ex = Assert.Throws<Exception>(() =>
            ApiRegistry.Register(new ApiDeclaration("Foo", new List<ApiMethodSignature>
            {
                new("bar", new List<string>())
            })));
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunProgram_Interpreter_ComputesExpression()
    {
        var source = """
            api Calc {
                function add(a, b);
                function mul(a, b);
            }

            function add(a, b) { return a + b; }
            function mul(a, b) { return a * b; }

            var prog = parseJSON("{\"@api\":\"Calc\",\"steps\":[{\"call\":\"add\",\"args\":[2,3],\"as\":\"t0\"},{\"call\":\"mul\",\"args\":[\"$t0\",4],\"as\":\"r\"}],\"return\":\"$r\"}");
            io.print(runProgram(prog));
            """;

        var output = RunProgram(source).Trim();
        Assert.Equal("20", output);
    }

    [Fact]
    public void Transpiler_EmitsApiRegistration_AndProgramSchema()
    {
        var source = """
            api Calc {
                function add(a, b);
            }

            function add(a, b) { return a + b; }

            prompt solve(expr) -> program(Calc) {
                user expr;
            }

            var result = await solve("1+1");
            print(result);
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_program_translator_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "api_prompt.malda");
        var generatedPath = Path.Combine(tempDir, "GeneratedProgram.cs");
        File.WriteAllText(sourcePath, source);

        try
        {
            var compiler = new Compiler.Compiler();
            var csharpResult = compiler.CompileToCSharp(sourcePath, generatedPath);
            Assert.True(csharpResult.Success, csharpResult.ErrorMessage ?? "Transpile failed.");

            var generated = File.ReadAllText(generatedPath);
            Assert.Contains("ApiRegistry.RegisterCompiled(\"Calc\"", generated);
            Assert.Contains("BindImplementation(\"add\"", generated);
            Assert.Contains("x-malda-kind", generated);
            Assert.Contains("program(Calc)", generated);
            Assert.Contains("ApplySchemaAppendix", generated);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Parser_AcceptsProgramReturnType()
    {
        var source = """
            api Calc { function add(a, b); }
            prompt solve(expr) -> program(Calc) {
                user expr;
            }
            """;
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var prompt = Assert.IsType<PromptDeclaration>(
            Assert.Single(statements.OfType<PromptDeclaration>()));
        Assert.Equal("program(Calc)", prompt.ReturnType);
    }
}
