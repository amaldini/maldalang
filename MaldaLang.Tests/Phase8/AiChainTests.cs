// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Named AI pipelines are ordinary functions that use <c>|&gt;</c>.
/// </summary>
public class AiChainTests : TestBase
{
    [Fact]
    public void Function_ExpressionBody_PipesRetriever()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            function buildContext(question, retriever) {
                return question |> retriever.get |> formatRetrievedDocs;
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);
            vdb.add("GraphMemory stores semantic facts");
            var retriever = vdb.asRetriever({ topK: 1 });

            var context = buildContext("GraphMemory", retriever);
            print(context);
            """;
        var output = RunProgram(source).Trim();
        Assert.Contains("[source:", output);
        Assert.Contains("GraphMemory", output);
    }

    [Fact]
    public void Function_ReturnBody_ParsesJson()
    {
        var source = """
            schema Answer {
                text: string;
            }

            function parseAnswer(jsonText) {
                return jsonText |> parseJson("Answer");
            }

            var parsed = parseAnswer("{\"text\":\"from-function\"}");
            print(parsed.text);
            """;
        Assert.Equal("from-function", RunProgram(source).Trim());
    }

    [Fact]
    public void Function_ClosureOverGlobals()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);
            vdb.add("VectorDB cosine search");
            var retriever = vdb.asRetriever({ topK: 1 });

            function ragHits(question) {
                return question |> retriever.get;
            }

            var hits = ragHits("VectorDB");
            print(length(hits));
            print(hits[0].content);
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("1", output[0]);
        Assert.Contains("VectorDB", output[1]);
    }

    [Fact]
    public void Function_AwaitOnPipeInsideFunction()
    {
        var source = """
            schema Answer { text: string; }
            function parseOnly(jsonText) {
                return await (jsonText |> parseJson("Answer"));
            }
            var parsed = parseOnly("{\"text\":\"awaited\"}");
            print(parsed.text);
            """;
        Assert.Equal("awaited", RunProgram(source).Trim());
    }

    [Fact]
    public void Function_WithPromptAdapter()
    {
        var source = """
            prompt answerPrompt(question, context) {
                user: "Q:{question} C:{context}"
            }

            function toPrompt(question, context) {
                return context |> (ctx) => answerPrompt(question, ctx);
            }

            var p = toPrompt("hi", "docs");
            print(p.user);
            """;
        Assert.Equal("Q:hi C:docs", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_FunctionExpressionBody_MatchesInterpreter()
    {
        var source = """
            prompt greet(name) {
                user: "Hello, {name}"
            }

            function toPrompt(name) {
                return name |> greet;
            }

            var p = toPrompt("Alice");
            print(p.user);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
        Assert.Equal("Hello, Alice", interpreted);
    }

    [Fact]
    public void Function_NamedLocals_ReferencePreviousBinding()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            function buildContext(question, retriever) {
                var hits = question |> retriever.get;
                var text = formatRetrievedDocs(hits);
                return text;
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);
            vdb.add("GraphMemory semantic facts");
            var retriever = vdb.asRetriever({ topK: 1 });

            print(buildContext("GraphMemory", retriever));
            """;
        var output = RunProgram(source).Trim();
        Assert.Contains("[source:", output);
        Assert.Contains("GraphMemory", output);
    }

    [Fact]
    public void Function_ConditionalBranch_ReturnsFallbackWhenEmpty()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            function pickContext(question, retriever, fallback) {
                var hits = question |> retriever.get;
                if (length(hits) > 0) {
                    return formatRetrievedDocs(hits);
                }
                return fallback;
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);
            var retriever = vdb.asRetriever({ topK: 1 });

            print(pickContext("missing topic", retriever, "none"));
            """;
        Assert.Equal("none", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_FunctionNamedLocals_MatchesInterpreter()
    {
        var source = """
            prompt greet(name) {
                user: "Hi {name}"
            }

            function toPrompt(name) {
                var p = name |> greet;
                return p;
            }

            var p = toPrompt("Bob");
            print(p.user);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
        Assert.Equal("Hi Bob", interpreted);
    }

    [Fact]
    public void Transpiled_FunctionReturnBody_MatchesInterpreter()
    {
        var source = """
            schema Answer { text: string; }
            function parseAnswer(jsonText) {
                return jsonText |> parseJson("Answer");
            }
            var parsed = parseAnswer("{\"text\":\"x\"}");
            print(parsed.text);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
        Assert.Equal("x", interpreted);
    }
}
