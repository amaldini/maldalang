// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        SumTypeRegistry.ClearForTesting();
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
    public void TypedPromptSchemaResolver_ResolvesSumType_WithOneOfAndMaldaKind()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
        SumTypeRegistry.Register(new TypeDeclaration(
            "Intent",
            new List<VariantConstructor>
            {
                new("Search", new List<string> { "query" }),
                new("Buy", new List<string> { "sku", "qty" })
            }));

        var ok = TypedPromptSchemaResolver.TryResolve("Intent", interpreter: null, out var schema, out var error);
        Assert.True(ok, error);
        var obj = schema.AsObject() as JsonObject;
        Assert.NotNull(obj);
        Assert.Equal("sum", obj!.Get("x-malda-kind").AsString());
        Assert.Equal(ValueType.Array, obj.Get("oneOf").Type);
        Assert.Equal(2, obj.Get("oneOf").AsArray().Count);

        var responseFormat = TypedPromptValidator.BuildResponseFormat(schema);
        var wrapper = responseFormat.AsObject() as JsonObject;
        Assert.NotNull(wrapper);
        Assert.Equal("json_schema", wrapper!.Get("type").AsString());
        var jsonSchema = wrapper.Get("json_schema").AsObject() as JsonObject;
        Assert.NotNull(jsonSchema);
        var nested = jsonSchema!.Get("schema").AsObject() as JsonObject;
        Assert.NotNull(nested);
        Assert.Equal("sum", nested!.Get("x-malda-kind").AsString());
    }

    [Fact]
    public void SchemaAndSumType_SameName_RegisterThrows()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
        SchemaRegistry.Register(new SchemaDeclaration("Foo", new List<SchemaField>
        {
            new("x", "string", required: true)
        }));

        var ex = Assert.Throws<Exception>(() =>
            SumTypeRegistry.Register(new TypeDeclaration(
                "Foo",
                new List<VariantConstructor> { new("A", new List<string>()) })));
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
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
            Assert.Contains("ApplySchemaAppendix", generated);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Transpiler_EmitsSumTypeRegistration_AndResponseFormat_ForSumReturnType()
    {
        var source = """
            type Intent = Search(query) | Buy(sku, qty) | Help();

            prompt parseUtterance(text) -> Intent {
                user "Utterance: " + text;
            }

            var result = await parseUtterance("help");
            print(result);
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_schema_to_llm_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "sum_prompt.malda");
        var generatedPath = Path.Combine(tempDir, "GeneratedProgram.cs");
        File.WriteAllText(sourcePath, source);

        try
        {
            var compiler = new Compiler.Compiler();
            var csharpResult = compiler.CompileToCSharp(sourcePath, generatedPath);
            Assert.True(csharpResult.Success, csharpResult.ErrorMessage ?? "Transpile failed.");

            var generated = File.ReadAllText(generatedPath);
            Assert.Contains("SumTypeRegistry.RegisterCompiled(\"Intent\"", generated);
            Assert.Contains("x-malda-kind", generated);
            Assert.Contains("__responseFormatSchema", generated);
            Assert.Contains("ApplySchemaAppendix", generated);
            Assert.Contains("TryValidateReturnType(__parsed, __resolvedSchema!, out __validated, out __validationError)", generated);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void LooksLikeResponseFormatRejectionText_DetectsKnownPhrases()
    {
        Assert.True(LLMClientInstance.LooksLikeResponseFormatRejectionText(
            "Error: API request failed with status BadRequest. Response: response_format not supported"));
        Assert.True(LLMClientInstance.LooksLikeResponseFormatRejectionText(
            "unsupported structured outputs for this model"));
        Assert.False(LLMClientInstance.LooksLikeResponseFormatRejectionText(
            "Error: API request failed with status 500. Response: internal error"));
    }

    [Fact]
    public void Chat_RetriesOnceWithoutResponseFormat_WhenBackendRejectsIt()
    {
        var previousStream = System.Environment.GetEnvironmentVariable("MALDA_AGENT_LLM_STREAM");
        System.Environment.SetEnvironmentVariable("MALDA_AGENT_LLM_STREAM", "0");
        // Reset cached streaming flag via reflection (set by first IsLlmStreamingEnabled call).
        var flag = typeof(LLMClientInstance).GetField(
            "_llmStreamingEnabled",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        flag?.SetValue(null, null);

        var requestBodies = new List<string>();
        var (listener, apiUrl) = StartResponseFormatFallbackServer(requestBodies);
        try
        {
            var innerSchema = new JsonObject();
            innerSchema.Set("type", RuntimeValue.String("object"));
            var responseFormat = TypedPromptValidator.BuildResponseFormat(RuntimeValue.Object(innerSchema));

            var client = new LLMClientInstance
            {
                ApiUrl = apiUrl,
                ApiKey = "test",
                Model = "test-model"
            };

            var msg = new JsonObject();
            msg.Set("role", RuntimeValue.String("user"));
            msg.Set("content", RuntimeValue.String("hi"));
            var messages = RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(msg) });

            var result = client.Chat(messages, tools: null, responseFormat);
            Assert.Equal(ValueType.Object, result.Type);
            Assert.Equal("ok-without-format", result.AsObject().Get("content").AsString());

            Assert.Equal(2, requestBodies.Count);
            Assert.Contains("response_format", requestBodies[0], StringComparison.Ordinal);
            Assert.DoesNotContain("response_format", requestBodies[1], StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
            listener.Close();
            System.Environment.SetEnvironmentVariable("MALDA_AGENT_LLM_STREAM", previousStream);
            flag?.SetValue(null, null);
        }
    }

    private static (HttpListener listener, string apiUrl) StartResponseFormatFallbackServer(
        List<string> requestBodies)
    {
        var portListener = new TcpListener(IPAddress.Loopback, 0);
        portListener.Start();
        var port = ((IPEndPoint)portListener.LocalEndpoint).Port;
        portListener.Stop();

        var prefix = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    string body;
                    using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    {
                        body = await reader.ReadToEndAsync();
                    }
                    requestBodies.Add(body);

                    if (body.Contains("response_format", StringComparison.Ordinal))
                    {
                        var err = Encoding.UTF8.GetBytes(
                            "{\"error\":{\"message\":\"response_format is not supported for this model\"}}");
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        context.Response.ContentType = "application/json";
                        await context.Response.OutputStream.WriteAsync(err);
                    }
                    else
                    {
                        var ok = Encoding.UTF8.GetBytes(
                            "{\"choices\":[{\"message\":{\"content\":\"ok-without-format\"}}]}");
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.ContentType = "application/json";
                        await context.Response.OutputStream.WriteAsync(ok);
                    }

                    context.Response.OutputStream.Close();
                }
            }
            catch
            {
                // Listener stopped.
            }
        });

        return (listener, prefix.TrimEnd('/') + "/v1/chat/completions");
    }
}
