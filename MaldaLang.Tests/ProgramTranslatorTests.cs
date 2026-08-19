// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MaldaLang.BuiltIns;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
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
    public async Task InterpretAsync_RerunSameApiProgram_DoesNotThrowAlreadyRegistered()
    {
        var source = """
            api Calc {
                function add(a, b);
            }

            function add(a, b) { return a + b; }

            var prog = parseJSON("{\"@api\":\"Calc\",\"steps\":[{\"call\":\"add\",\"args\":[1,2],\"as\":\"t0\"}],\"return\":\"$t0\"}");
            io.print(runProgram(prog));
            """;
        var statements = ParseStatements(source);

        RedirectConsole();
        try
        {
            await new Interpreter.Interpreter().InterpretAsync(statements);
            Assert.Equal("3", GetOutput());
        }
        finally
        {
            RestoreConsole();
        }

        // Second host run in the same process (Desktop/Web IDE) must not see the
        // leftover api registration from the first Interpreter instance.
        RedirectConsole();
        try
        {
            await new Interpreter.Interpreter().InterpretAsync(statements);
            Assert.Equal("3", GetOutput());
        }
        finally
        {
            RestoreConsole();
        }
    }

    [Fact]
    public void DuplicateApiInSameProgram_ThrowsAlreadyRegistered()
    {
        var source = """
            api Calc { function add(a, b); }
            api Calc { function add(a, b); }
            """;
        var ex = Assert.ThrowsAny<Exception>(() => RunProgram(source));
        Assert.Contains("already registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InterpretAsync_ImportDoesNotClearHostApi()
    {
        var tempDir = CreateTempDirectory("api_import_host_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "lib.malda"), "export var loaded = 1;\n");
            var mainPath = Path.Combine(tempDir, "main.malda");
            var source = """
                api Calc { function add(a, b); }
                function add(a, b) { return a + b; }
                import "lib.malda";
                var prog = parseJSON("{\"@api\":\"Calc\",\"steps\":[{\"call\":\"add\",\"args\":[1,2],\"as\":\"t0\"}],\"return\":\"$t0\"}");
                io.print(runProgram(prog));
                io.print(loaded);
                """;
            File.WriteAllText(mainPath, source);

            var statements = ParseStatements(source, mainPath);
            RedirectConsole();
            try
            {
                await new Interpreter.Interpreter(currentFile: mainPath).InterpretAsync(statements);
                var lines = GetOutput().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal("3", lines[0]);
                Assert.Equal("1", lines[1]);
            }
            finally
            {
                RestoreConsole();
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private static List<Statement> ParseStatements(string source, string? sourceFileName = null)
    {
        var lexer = new Lexer(source, sourceFileName);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens, sourceFileName);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }

    [Fact]
    public void RunProgram_Transpile_ComputesExpression()
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

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_program_translator_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "api_run.malda");
        var outputPath = Path.Combine(tempDir, "api_run.exe");
        File.WriteAllText(sourcePath, source);

        try
        {
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(
                sourcePath,
                outputPath,
                CompilationMode.TranspileToCSharp,
                includeLLamaSharp: false,
                includeUiHost: false);
            Assert.True(result.Success, result.ErrorMessage ?? "Transpile failed.");
            Assert.True(!string.IsNullOrEmpty(result.OutputPath) && File.Exists(result.OutputPath));

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = result.OutputPath!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };
            using var process = System.Diagnostics.Process.Start(psi);
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60000), "transpiled runProgram timed out");
            Assert.True(process.ExitCode == 0, $"exit={process.ExitCode}\nstdout={stdout}\nstderr={stderr}");
            Assert.Equal("20", stdout.Trim());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void JsTranspile_ApiDeclaration_ThrowsHostOnly()
    {
        var source = """
            api Calc { function add(a, b); }
            function add(a, b) { return a + b; }
            """;
        var compiler = new Compiler.Compiler();
        var ex = Assert.ThrowsAny<Exception>(() => compiler.TranspileToJavaScriptFromSource(source));
        Assert.Contains("api", ex.Message, StringComparison.OrdinalIgnoreCase);
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
            Assert.Contains("GetAwaiter().GetResult()", generated);
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
    public void ApiRegistry_ProgramArgSchema_DoesNotAllowBareObjects()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" })
        }));

        Assert.True(ApiRegistry.TryResolveProgramSchema("Calc", out var schema));
        var root = Assert.IsType<JsonObject>(schema.AsObject());
        var props = Assert.IsType<JsonObject>(root.Get("properties").AsObject());
        var steps = Assert.IsType<JsonObject>(props.Get("steps").AsObject());
        var stepItem = Assert.IsType<JsonObject>(steps.Get("items").AsObject());
        var stepProps = Assert.IsType<JsonObject>(stepItem.Get("properties").AsObject());
        var args = Assert.IsType<JsonObject>(stepProps.Get("args").AsObject());
        var items = Assert.IsType<JsonObject>(args.Get("items").AsObject());
        var types = items.Get("type").AsArray().Select(v => v.AsString()).ToList();
        Assert.DoesNotContain("object", types);
        Assert.Contains("number", types);
        Assert.Contains("string", types);
    }

    [Fact]
    public void FormatSchemaAppendix_Program_WarnsAgainstTypedWrappers()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" })
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("program(Calc)", null, out var schema, out _));
        var appendix = TypedPromptValidator.FormatSchemaAppendix("program(Calc)", schema);
        Assert.Contains("JSON numbers", appendix);
        Assert.Contains("never numeric strings", appendix);
        Assert.Contains("\"$alias\"", appendix);
        Assert.Contains("{\"type\":\"number\",\"value\":2}", appendix);
    }

    [Fact]
    public void ValidateReturnType_Program_FlattensNestedCalls()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" }),
            new("mul", new List<string> { "a", "b" })
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("program(Calc)", null, out var schema, out _));

        var nestedAdd = new JsonObject();
        nestedAdd.Set("call", RuntimeValue.String("add"));
        nestedAdd.Set("args", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Integer(2),
            RuntimeValue.Integer(3)
        }));

        var mul = new JsonObject();
        mul.Set("call", RuntimeValue.String("mul"));
        mul.Set("args", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Object(nestedAdd),
            RuntimeValue.Integer(4)
        }));
        mul.Set("as", RuntimeValue.String("result"));

        var json = new JsonObject();
        json.Set("steps", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(mul) }));
        json.Set("return", RuntimeValue.String("$result"));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(json),
            schema,
            out var validated,
            out var error);

        Assert.True(ok, error);
        var prog = Assert.IsType<ProgramInstance>(validated.AsObject());
        Assert.Equal(2, prog.Steps.Count);
        Assert.Equal("add", prog.Steps[0].Call);
        Assert.Equal("mul", prog.Steps[1].Call);
        Assert.Equal("$n0", prog.Steps[1].Args[0].AsString());
        Assert.Equal(4, prog.Steps[1].Args[1].AsInteger());
    }

    [Fact]
    public void ValidateReturnType_Program_CoercesNumericStringsAndTypeWrappers()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" })
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("program(Calc)", null, out var schema, out _));

        var wrapper = new JsonObject();
        wrapper.Set("type", RuntimeValue.String("number"));
        wrapper.Set("value", RuntimeValue.String("3"));

        var step = new JsonObject();
        step.Set("call", RuntimeValue.String("add"));
        step.Set("args", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("2"),
            RuntimeValue.Object(wrapper)
        }));
        step.Set("as", RuntimeValue.String("t0"));

        var json = new JsonObject();
        json.Set("@api", RuntimeValue.String("Calc"));
        json.Set("steps", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(step) }));
        json.Set("return", RuntimeValue.String("$t0"));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(json),
            schema,
            out var validated,
            out var error);

        Assert.True(ok, error);
        var prog = Assert.IsType<ProgramInstance>(validated.AsObject());
        Assert.Equal(ValueType.Integer, prog.Steps[0].Args[0].Type);
        Assert.Equal(2, prog.Steps[0].Args[0].AsInteger());
        Assert.Equal(ValueType.Integer, prog.Steps[0].Args[1].Type);
        Assert.Equal(3, prog.Steps[0].Args[1].AsInteger());
    }

    [Fact]
    public void ValidateReturnType_Program_AcceptsTypeChatShape()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" }),
            new("mul", new List<string> { "a", "b" })
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("program(Calc)", null, out var schema, out _));

        var add = new JsonObject();
        add.Set("@func", RuntimeValue.String("add"));
        add.Set("@args", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Integer(2),
            RuntimeValue.Integer(3)
        }));

        var mul = new JsonObject();
        mul.Set("@func", RuntimeValue.String("mul"));
        var ref0 = new JsonObject();
        ref0.Set("@ref", RuntimeValue.Integer(0));
        mul.Set("@args", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Object(ref0),
            RuntimeValue.Integer(4)
        }));

        var json = new JsonObject();
        json.Set("@steps", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Object(add),
            RuntimeValue.Object(mul)
        }));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(json),
            schema,
            out var validated,
            out var error);

        Assert.True(ok, error);
        var prog = Assert.IsType<ProgramInstance>(validated.AsObject());
        Assert.Equal("Calc", prog.ApiName);
        Assert.Equal(2, prog.Steps.Count);
        Assert.Equal("$t0", prog.Steps[1].Args[0].AsString());
        Assert.Equal("$t1", prog.ReturnValue.AsString());
    }

    [Fact]
    public void ValidateReturnType_Program_RejectsUnknownObjectArgs()
    {
        ApiRegistry.Register(new ApiDeclaration("Calc", new List<ApiMethodSignature>
        {
            new("add", new List<string> { "a", "b" })
        }));
        Assert.True(TypedPromptSchemaResolver.TryResolve("program(Calc)", null, out var schema, out _));

        var junk = new JsonObject();
        junk.Set("foo", RuntimeValue.Integer(1));

        var step = new JsonObject();
        step.Set("call", RuntimeValue.String("add"));
        step.Set("args", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Object(junk),
            RuntimeValue.Integer(3)
        }));
        step.Set("as", RuntimeValue.String("t0"));

        var json = new JsonObject();
        json.Set("@api", RuntimeValue.String("Calc"));
        json.Set("steps", RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(step) }));
        json.Set("return", RuntimeValue.String("$t0"));

        var ok = TypedPromptValidator.TryValidateReturnType(
            RuntimeValue.Object(json),
            schema,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("object", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunProgram_Interpreter_NestedCallsAndNumericStrings()
    {
        var source = """
            api Calc {
                function add(a, b);
                function mul(a, b);
            }

            function add(a, b) { return a + b; }
            function mul(a, b) { return a * b; }

            var nested = parseJSON("{\"@api\":\"Calc\",\"steps\":[{\"call\":\"mul\",\"args\":[{\"call\":\"add\",\"args\":[2,3]},4],\"as\":\"r\"}],\"return\":\"$r\"}");
            io.print(runProgram(nested));

            var strings = parseJSON("{\"@api\":\"Calc\",\"steps\":[{\"call\":\"add\",\"args\":[\"2\",\"3\"],\"as\":\"t0\"},{\"call\":\"mul\",\"args\":[\"$t0\",\"4\"],\"as\":\"r\"}],\"return\":\"$r\"}");
            io.print(runProgram(strings));

            var wrappers = parseJSON("{\"steps\":[{\"call\":\"add\",\"args\":[{\"type\":\"number\",\"value\":2},{\"type\":\"integer\",\"value\":\"3\"}],\"as\":\"t0\"},{\"call\":\"mul\",\"args\":[\"$t0\",4],\"as\":\"r\"}],\"return\":\"$r\"}");
            io.print(runProgram(wrappers));
            """;

        var lines = RunProgram(source).Replace("\r\n", "\n").Trim().Split('\n');
        Assert.Equal(new[] { "20", "20", "20" }, lines);
    }

    [Fact]
    public void RunProgram_Interpreter_UnderscoreApiMethods()
    {
        var source = """
            api Calc {
                function _add(a, b);
                function _mul(a, b);
            }

            function _add(a, b) { return a + b; }
            function _mul(a, b) { return a * b; }

            var prog = parseJSON("{\"@api\":\"Calc\",\"steps\":[{\"call\":\"_mul\",\"args\":[{\"call\":\"_add\",\"args\":[\"2\",\"3\"]},4],\"as\":\"r\"}],\"return\":\"$r\"}");
            io.print(runProgram(prog));
            """;

        Assert.Equal("20", RunProgram(source).Trim());
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
