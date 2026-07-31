// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Net;
using System.Net.Sockets;
using System.Text;
using MaldaLang.BuiltIns;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class AiStreamFewShotTests : TestBase
{
    [Fact]
    public async Task RunPromptAsync_InvokesTranspiledOnTokenDelegate()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"content":"X"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);
        var seen = new StringBuilder();

        async Task<object> OnTok(object t)
        {
            seen.Append(RuntimeHelpers.CoerceToString(t));
            return null!;
        }

        try
        {
            var client = new LLMClientInstance { ApiUrl = apiUrl, ApiKey = "k", Model = "m" };
            var prompt = new PromptInstance(null, "go");
            var args = new List<RuntimeValue>
            {
                RuntimeValue.Object(prompt),
                RuntimeValue.Object(client),
                RuntimeHelpers.ToRuntimeValue(new Dictionary<string, object?> { { "onToken", (Func<object, Task<object>>)OnTok } })
            };

            var result = await AiPipelineHelpers.RunPromptAsync(args, null);
            Assert.Equal("X", result.AsString());
            Assert.Equal("X", seen.ToString());
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task RunPromptAsync_InvokesOnTokenFromMethodGroupDictionary()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"content":"Y"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);
        var seen = new StringBuilder();

        async Task<object> OnTok(object t)
        {
            seen.Append(RuntimeHelpers.CoerceToString(t));
            return null!;
        }

        try
        {
            var client = new LLMClientInstance { ApiUrl = apiUrl, ApiKey = "k", Model = "m" };
            var prompt = new PromptInstance(null, "go");
            var args = new List<RuntimeValue>
            {
                RuntimeValue.Object(prompt),
                RuntimeValue.Object(client),
                RuntimeHelpers.ToRuntimeValue(new Dictionary<string, object?> { { "onToken", OnTok } })
            };

            var result = await AiPipelineHelpers.RunPromptAsync(args, null);
            Assert.Equal("Y", result.AsString());
            Assert.Equal("Y", seen.ToString());
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task RunPromptAsync_InvokesOnReasoningFromTranspiledDelegate()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"reasoning":"Think"}}]}

            data: {"choices":[{"index":0,"delta":{"reasoning":" hard"}}]}

            data: {"choices":[{"index":0,"delta":{"content":"Done"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);
        var seen = new StringBuilder();

        async Task<object> OnReason(object t)
        {
            seen.Append(RuntimeHelpers.CoerceToString(t));
            return null!;
        }

        try
        {
            var client = new LLMClientInstance { ApiUrl = apiUrl, ApiKey = "k", Model = "m" };
            var prompt = new PromptInstance(null, "go");
            var args = new List<RuntimeValue>
            {
                RuntimeValue.Object(prompt),
                RuntimeValue.Object(client),
                RuntimeHelpers.ToRuntimeValue(new Dictionary<string, object?> { { "onReasoning", (Func<object, Task<object>>)OnReason } })
            };

            var result = await AiPipelineHelpers.RunPromptAsync(args, null);
            Assert.Equal("Done", result.AsString());
            Assert.Equal("Think hard", seen.ToString());
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task RunPromptAsync_InvokesOnTokenAndOnReasoningTogether()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"reasoning":"R1"}}]}

            data: {"choices":[{"index":0,"delta":{"content":"C1"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);
        var content = new StringBuilder();
        var reasoning = new StringBuilder();

        async Task<object> OnTok(object t)
        {
            content.Append(RuntimeHelpers.CoerceToString(t));
            return null!;
        }

        async Task<object> OnReason(object t)
        {
            reasoning.Append(RuntimeHelpers.CoerceToString(t));
            return null!;
        }

        try
        {
            var client = new LLMClientInstance { ApiUrl = apiUrl, ApiKey = "k", Model = "m" };
            var prompt = new PromptInstance(null, "go");
            var args = new List<RuntimeValue>
            {
                RuntimeValue.Object(prompt),
                RuntimeValue.Object(client),
                RuntimeHelpers.ToRuntimeValue(new Dictionary<string, object?>
                {
                    { "onToken", (Func<object, Task<object>>)OnTok },
                    { "onReasoning", (Func<object, Task<object>>)OnReason }
                })
            };

            var result = await AiPipelineHelpers.RunPromptAsync(args, null);
            Assert.Equal("C1", result.AsString());
            Assert.Equal("C1", content.ToString());
            Assert.Equal("R1", reasoning.ToString());
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public void ToRuntimeValue_DictionaryOnToken_IsFunctionWithDelegate()
    {
        Func<object, Task<object>> fn = async t => t;
        var rv = RuntimeHelpers.ToRuntimeValue(new Dictionary<string, object?> { ["onToken"] = fn });
        var dict = Assert.IsType<DictionaryInstance>(rv.AsObject());
        Assert.True(dict.TryGetEntry("onToken", out var tokenVal));
        Assert.Equal(ValueType.Function, tokenVal.Type);
        Assert.NotNull(tokenVal.AsFunction().TranspiledDelegate);
    }

    [Fact]
    public void ToRuntimeValue_DictionaryOnReasoning_IsFunctionWithDelegate()
    {
        Func<object, Task<object>> fn = async t => t;
        var rv = RuntimeHelpers.ToRuntimeValue(new Dictionary<string, object?> { ["onReasoning"] = fn });
        var dict = Assert.IsType<DictionaryInstance>(rv.AsObject());
        Assert.True(dict.TryGetEntry("onReasoning", out var reasoningVal));
        Assert.Equal(ValueType.Function, reasoningVal.Type);
        Assert.NotNull(reasoningVal.AsFunction().TranspiledDelegate);
    }

    [Fact]
    public void Prompt_FewShotExamples_ExposedOnInstance()
    {
        var source = """
            prompt classify(text) {
                system: "Classify sentiment."
                examples: [
                    { input: "I love it!", output: "positive" },
                    { input: "Terrible.", output: "negative" }
                ]
                user: "Classify: {text}"
            }

            var p = classify("ok");
            print(length(p.examples));
            print(p.examples[0].input);
            print(p.examples[1].output);
            print(p.user);
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("2", output[0]);
        Assert.Equal("I love it!", output[1]);
        Assert.Equal("negative", output[2]);
        Assert.Equal("Classify: ok", output[3]);
    }

    [Fact]
    public void Prompt_FewShotStatementSyntax_Parses()
    {
        var source = """
            prompt classify(text) {
                examples [
                    { input: "A", output: "1" }
                ];
                user "Q: {text}";
            }
            print(length(classify("x").examples));
            """;
        Assert.Equal("1", RunProgram(source).Trim());
    }

    [Fact]
    public void Prompt_FewShotInterpolatesParameters()
    {
        var source = """
            prompt tagged(label) {
                examples [
                    { input: "sample {label}", output: "seen" }
                ];
                user "Run {label}";
            }
            var p = tagged("demo");
            print(p.examples[0].input);
            """;
        Assert.Equal("sample demo", RunProgram(source).Trim());
    }

    [Fact]
    public void AgentThink_WithFewShot_AddsExampleMessagesBeforeUser()
    {
        var examples = new List<PromptExample>
        {
            new("Example question", "Example answer")
        };
        var prompt = new PromptInstance(null, "Final question", examples: examples);
        var client = new LLMClientInstance
        {
            ApiUrl = "https://example.invalid/v1/chat/completions",
            ApiKey = "test",
            Model = "test"
        };

        var agent = new AgentInstance();
        agent.Initialize("FewShotAgent", "tester", "Base instructions", client, null, null, null);

        try
        {
            agent.Think(RuntimeValue.Object(prompt));
        }
        catch
        {
            // Network call may fail; message assembly happens before the request.
        }

        var conversation = agent.GetConversation().AsObject() as ConversationInstance;
        Assert.NotNull(conversation);
        var messagesField = typeof(ConversationInstance).GetField("_messages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(messagesField);
        var messages = messagesField!.GetValue(conversation) as List<RuntimeValue>;
        Assert.NotNull(messages);
        Assert.True(messages!.Count >= 4);

        static (string role, string content) ReadMessage(RuntimeValue msg)
        {
            var obj = msg.AsObject();
            var role = obj.Get("role").AsString();
            var content = obj.Get("content").AsString();
            return (role, content);
        }

        var first = ReadMessage(messages[1]);
        var second = ReadMessage(messages[2]);
        var third = ReadMessage(messages[3]);
        Assert.Equal("user", first.role);
        Assert.Equal("Example question", first.content);
        Assert.Equal("assistant", second.role);
        Assert.Equal("Example answer", second.content);
        Assert.Equal("user", third.role);
        Assert.Equal("Final question", third.content);
    }

    [Fact]
    public async Task RunPrompt_OnToken_StreamsContentTokens()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"content":"Hel"}}]}

            data: {"choices":[{"index":0,"delta":{"content":"lo"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);

        try
        {
            var source = $$"""
                var client = new LLMClient("{{apiUrl}}", "test-key", "test-model");
                var seen = "";

                function onTok(token) {
                    seen = seen + token;
                }

                prompt greet() {
                    user: "Say hello"
                }

                var text = await (greet() |> runPrompt(client, { onToken: onTok }));
                print(seen);
                print(text);
                """;

            var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("Hello", output[0]);
            Assert.Equal("Hello", output[1]);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task RunPrompt_OnReasoning_StreamsReasoningTokens()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"reasoning":"Think"}}]}

            data: {"choices":[{"index":0,"delta":{"reasoning":" hard"}}]}

            data: {"choices":[{"index":0,"delta":{"content":"Done"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);

        try
        {
            var source = $$"""
                var client = new LLMClient("{{apiUrl}}", "test-key", "test-model");
                var seen = "";

                function onReason(token) {
                    seen = seen + token;
                }

                prompt greet() {
                    user: "Say hello"
                }

                var text = await (greet() |> runPrompt(client, { onReasoning: onReason }));
                print(seen);
                print(text);
                """;

            var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("Think hard", output[0]);
            Assert.Equal("Done", output[1]);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public void WithExamples_ReplacesStaticExamples()
    {
        var source = """
            prompt classify(text) {
                examples: [{ input: "A", output: "1" }]
                user: "Q: {text}"
            }

            var dynamic = [{ input: "B", output: "2" }];
            var p = classify("z") |> withExamples(dynamic);
            print(length(p.examples));
            print(p.examples[0].input);
            print(p.examples[0].output);
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("1", output[0]);
        Assert.Equal("B", output[1]);
        Assert.Equal("2", output[2]);
    }

    [Fact]
    public void WithExamples_MergeAppendsAfterStaticExamples()
    {
        var source = """
            prompt classify(text) {
                examples: [{ input: "A", output: "1" }]
                user: "Q: {text}"
            }

            var dynamic = [{ input: "B", output: "2" }];
            var p = withExamples(classify("z"), dynamic, { merge: true });
            print(length(p.examples));
            print(p.examples[0].input);
            print(p.examples[1].input);
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("2", output[0]);
        Assert.Equal("A", output[1]);
        Assert.Equal("B", output[2]);
    }

    [Fact]
    public void AgentThink_WithDynamicExamples_AddsExampleMessagesBeforeUser()
    {
        var exampleObj = new JsonObject();
        exampleObj.Set("input", RuntimeValue.String("Runtime question"));
        exampleObj.Set("output", RuntimeValue.String("Runtime answer"));

        var prompt = new PromptInstance(null, "Final question");
        var dynamicExamples = AiPipelineHelpers.WithExamples(
            new List<RuntimeValue>
            {
                RuntimeValue.Object(prompt),
                RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Object(exampleObj) })
            }).AsObject() as PromptInstance;

        Assert.NotNull(dynamicExamples);

        var client = new LLMClientInstance
        {
            ApiUrl = "https://example.invalid/v1/chat/completions",
            ApiKey = "test",
            Model = "test"
        };

        var agent = new AgentInstance();
        agent.Initialize("DynFewShotAgent", "tester", "Base instructions", client, null, null, null);

        try
        {
            agent.Think(RuntimeValue.Object(dynamicExamples));
        }
        catch
        {
            // Network call may fail; message assembly happens before the request.
        }

        var conversation = agent.GetConversation().AsObject() as ConversationInstance;
        Assert.NotNull(conversation);
        var messagesField = typeof(ConversationInstance).GetField("_messages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(messagesField);
        var messages = messagesField!.GetValue(conversation) as List<RuntimeValue>;
        Assert.NotNull(messages);
        Assert.True(messages!.Count >= 3);

        static (string role, string content) ReadMessage(RuntimeValue msg)
        {
            var obj = msg.AsObject();
            var role = obj.Get("role").AsString();
            var content = obj.Get("content").AsString();
            return (role, content);
        }

        var first = ReadMessage(messages[1]);
        var second = ReadMessage(messages[2]);
        var third = ReadMessage(messages[3]);
        Assert.Equal("user", first.role);
        Assert.Equal("Runtime question", first.content);
        Assert.Equal("assistant", second.role);
        Assert.Equal("Runtime answer", second.content);
        Assert.Equal("user", third.role);
        Assert.Equal("Final question", third.content);
    }

    [Fact]
    public void Transpiled_WithExamples_MatchesInterpreter()
    {
        var source = """
            prompt classify(text) {
                examples: [{ input: "A", output: "1" }]
                user: "Q: {text}"
            }

            var dynamic = [{ input: "B", output: "2" }];
            var p = classify("z") |> withExamples(dynamic);
            print(length(p.examples));
            print(p.examples[0].input);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void Transpiled_FewShotPrompt_MatchesInterpreter()
    {
        var source = """
            prompt classify(text) {
                examples: [{ input: "A", output: "1" }]
                user: "Q: {text}"
            }
            var p = classify("z");
            print(length(p.examples));
            print(p.user);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void Transpiled_RunPromptOnToken_AccumulatesStream()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"content":"Hel"}}]}

            data: {"choices":[{"index":0,"delta":{"content":"lo"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);

        try
        {
            var source = $$"""
                var client = new LLMClient("{{apiUrl}}", "test-key", "test-model");
                var seen = "";

                function onTok(token) {
                    seen = seen + token;
                }

                prompt greet() {
                    user: "Say hello"
                }

                var text = await (greet() |> runPrompt(client, { onToken: onTok }));
                print(seen);
                print(text);
                """;

            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("Hello", output[0]);
            Assert.Equal("Hello", output[1]);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public void Transpiled_RunPromptOnReasoning_AccumulatesStream()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"reasoning":"Think"}}]}

            data: {"choices":[{"index":0,"delta":{"reasoning":" hard"}}]}

            data: {"choices":[{"index":0,"delta":{"content":"Done"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);

        try
        {
            var source = $$"""
                var client = new LLMClient("{{apiUrl}}", "test-key", "test-model");
                var seen = "";

                function onReason(token) {
                    seen = seen + token;
                }

                prompt greet() {
                    user: "Say hello"
                }

                var text = await (greet() |> runPrompt(client, { onReasoning: onReason }));
                print(seen);
                print(text);
                """;

            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("Think hard", output[0]);
            Assert.Equal("Done", output[1]);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public void Transpiled_RunPromptOnToken_ReturnsResponseText()
    {
        var sse = """
            data: {"choices":[{"index":0,"delta":{"content":"Hi"}}]}

            data: [DONE]

            """;
        var (listener, apiUrl) = StartMockChatCompletionsServer(sse);

        try
        {
            var source = $$"""
                var client = new LLMClient("{{apiUrl}}", "test-key", "test-model");
                function onTok(token) { }
                prompt p() { user: "go" }
                var text = await (p() |> runPrompt(client, { onToken: onTok }));
                print(text);
                """;

            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
            Assert.Equal("Hi", output);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    private static (HttpListener listener, string apiUrl) StartMockChatCompletionsServer(string sseBody)
    {
        var port = GetFreeTcpPort();
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
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Response.ContentType = "text/event-stream";
                    var bytes = Encoding.UTF8.GetBytes(sseBody);
                    await context.Response.OutputStream.WriteAsync(bytes);
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

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
