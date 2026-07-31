// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

public class AiChainTests : TestBase
{
    [Fact]
    public void Chain_ExpressionBody_PipesRetriever()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            chain buildContext(question, retriever) {
                question |> retriever.get |> formatRetrievedDocs
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
    public void Chain_ReturnBody_ParsesJson()
    {
        var source = """
            schema Answer {
                text: string;
            }

            chain parseAnswer(jsonText) {
                return jsonText |> parseJson("Answer");
            }

            var parsed = parseAnswer("{\"text\":\"from-chain\"}");
            print(parsed.text);
            """;
        Assert.Equal("from-chain", RunProgram(source).Trim());
    }

    [Fact]
    public void Chain_ClosureOverGlobals()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);
            vdb.add("VectorDB cosine search");
            var retriever = vdb.asRetriever({ topK: 1 });

            chain ragHits(question) {
                question |> retriever.get
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
    public void Chain_AwaitOnPipeInsideChain()
    {
        var source = """
            schema Answer { text: string; }
            chain parseOnly(jsonText) {
                await (jsonText |> parseJson("Answer"))
            }
            var parsed = parseOnly("{\"text\":\"awaited\"}");
            print(parsed.text);
            """;
        Assert.Equal("awaited", RunProgram(source).Trim());
    }

    [Fact]
    public void Chain_WithPromptAdapter()
    {
        var source = """
            prompt answerPrompt(question, context) {
                user: "Q:{question} C:{context}"
            }

            chain toPrompt(question, context) {
                context |> (ctx) => answerPrompt(question, ctx)
            }

            var p = toPrompt("hi", "docs");
            print(p.user);
            """;
        Assert.Equal("Q:hi C:docs", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_ChainExpressionBody_MatchesInterpreter()
    {
        var source = """
            prompt greet(name) {
                user: "Hello, {name}"
            }

            chain toPrompt(name) {
                name |> greet
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
    public void Chain_NamedSteps_ReferencePreviousStep()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            chain buildContext(question, retriever) {
                step hits = question |> retriever.get;
                step text = formatRetrievedDocs(hits);
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
    public void Chain_ConditionalBranch_ReturnsFallbackWhenEmpty()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            chain pickContext(question, retriever, fallback) {
                step hits = question |> retriever.get;
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
    public void Transpiled_ChainNamedSteps_MatchesInterpreter()
    {
        var source = """
            prompt greet(name) {
                user: "Hi {name}"
            }

            chain toPrompt(name) {
                step p = name |> greet;
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
    public void Transpiled_ChainReturnBody_MatchesInterpreter()
    {
        var source = """
            schema Answer { text: string; }
            chain parseAnswer(jsonText) {
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
