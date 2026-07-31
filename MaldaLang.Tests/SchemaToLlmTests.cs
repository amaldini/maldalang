// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class SchemaToLlmTests
{
    [Fact]
    public void TypedPromptSchemaResolver_ResolvesRegisteredSchemaDeclaration()
    {
        SchemaRegistry.ClearForTesting();
        SchemaRegistry.Register(new SchemaDeclaration("ToolInput", new List<SchemaField>
        {
            new("name", "string", required: true)
        }));

        var ok = TypedPromptSchemaResolver.TryResolve("ToolInput", interpreter: null, out var schema, out var error);

        Assert.True(ok, error);
        Assert.Equal(ValueType.Object, schema.Type);
        var obj = schema.AsObject() as JsonObject;
        Assert.NotNull(obj);
        Assert.Equal("object", obj!.Get("type").AsString());
    }

    [Fact]
    public void LLMClient_BuildRequestBody_IncludesResponseFormat()
    {
        var innerSchema = new JsonObject();
        innerSchema.Set("type", RuntimeValue.String("object"));

        var responseFormat = TypedPromptValidator.BuildResponseFormat(RuntimeValue.Object(innerSchema));
        var client = new LLMClientInstance
        {
            Model = "gpt-4o-mini",
            ApiUrl = "https://example.test/v1/chat/completions"
        };

        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String("user"));
        msg.Set("content", RuntimeValue.String("hi"));
        var messages = RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(msg) });

        var body = client.BuildRequestBody(messages, tools: null, responseFormat);

        Assert.True(body.ContainsKey("response_format"));
        var json = JsonSerializer.Serialize(body["response_format"]);
        Assert.Contains("json_schema", json);
        Assert.Contains("typed_prompt_response", json);
    }

    [Fact]
    public void Transpiler_EmitsSchemaRegistration_AndResponseFormat_ForSchemaReturnType()
    {
        var source = """
            schema ToolInput {
                name: string;
            }

            prompt getToolInput(raw) -> ToolInput {
                user "Validate: " + raw;
            }

            var result = await getToolInput("alice");
            print(result);
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_schema_to_llm_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "schema_prompt.malda");
        var generatedPath = Path.Combine(tempDir, "GeneratedProgram.cs");
        File.WriteAllText(sourcePath, source);

        try
        {
            var compiler = new Compiler.Compiler();
            var csharpResult = compiler.CompileToCSharp(sourcePath, generatedPath);
            Assert.True(csharpResult.Success, csharpResult.ErrorMessage ?? "Transpile failed.");

            var generated = File.ReadAllText(generatedPath);
            Assert.Contains("SchemaRegistry.RegisterCompiled(\"ToolInput\"", generated);
            Assert.Contains("__responseFormatSchema", generated);
            Assert.Contains("__resolvedSchema", generated);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
