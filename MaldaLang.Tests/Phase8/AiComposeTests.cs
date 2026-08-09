// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

public class AiComposeTests : TestBase
{
    [Fact]
    public void ComposePipe_SyncFunctions_LeftToRight()
    {
        var source = """
            function doubleIt(x) {
                return x * 2;
            }

            function addPrefix(text) {
                return "val:" + string(text);
            }

            var pipeline = composePipe(doubleIt, addPrefix);
            print(pipeline(5));
            """;
        Assert.Equal("val:10", RunProgram(source).Trim());
    }

    [Fact]
    public void ComposePipe_WithFunctionReference()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);
            vdb.add("GraphMemory stores semantic facts");
            var retriever = vdb.asRetriever({ topK: 1 });

            function fetchHits(question) {
                return question |> retriever.get;
            }

            var pipeline = composePipe(fetchHits, formatRetrievedDocs);
            var context = pipeline("GraphMemory");
            print(context);
            """;
        var output = RunProgram(source).Trim();
        Assert.Contains("[source:", output);
        Assert.Contains("GraphMemory", output);
    }

    [Fact]
    public void ComposePipe_PipeFriendly()
    {
        var source = """
            function tag(text) {
                return "[" + text + "]";
            }

            var pipeline = composePipe(tag, upper);
            print("hello" |> pipeline);
            """;
        Assert.Equal("[HELLO]", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_ComposePipe_MatchesInterpreter()
    {
        var source = """
            function doubleIt(x) {
                return x * 2;
            }

            function addPrefix(text) {
                return "p:" + string(text);
            }

            var pipeline = composePipe(doubleIt, addPrefix);
            print(pipeline(3));
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
        Assert.Equal("p:6", interpreted);
    }

    [Fact]
    public void ParallelRun_TwoSyncBranches_DifferentTypes()
    {
        var source = """
            function classifyIntent(q) {
                return "intent:" + q;
            }

            function wordCount(q) {
                return length(q);
            }

            var result = parallelRun("hello world", {
                tags: classifyIntent,
                count: wordCount
            });

            print(result.tags);
            print(string(result.count));
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("intent:hello world", output[0]);
        Assert.Equal("11", output[1]);
    }

    [Fact]
    public void ParallelRun_Merge_Format_Integration()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);
            vdb.add({ content: "GraphMemory semantic graph", metadata: { source: "memory.md" } });
            vdb.add({ content: "VectorDB cosine search", metadata: { source: "vectordb.md" } });

            var retrieverA = vdb.asRetriever({ topK: 1 });
            var retrieverB = vdb.asRetriever({ topK: 1 });

            function fetchA(q) { return q |> retrieverA.get; }
            function fetchB(q) { return q |> retrieverB.get; }

            var branches = parallelRun("GraphMemory", {
                docsA: fetchA,
                docsB: fetchB
            });

            var merged = mergeRetrievedDocs(branches.docsA, branches.docsB);
            var text = formatRetrievedDocs(merged);
            print(text);
            """;
        var output = RunProgram(source).Trim();
        Assert.Contains("GraphMemory", output);
        Assert.Contains("[source:", output);
    }

    [Fact]
    public void ComposePipe_InsideFunctionBody()
    {
        var source = """
            function wrap(text) {
                return "<<" + text + ">>";
            }

            function tagged(question) {
                var pipeline = composePipe(wrap, upper);
                return pipeline(question);
            }

            print(tagged("hi"));
            """;
        Assert.Equal("<<HI>>", RunProgram(source).Trim());
    }

    [Fact]
    public void MergeRetrievedDocs_DedupesBySourceAndChunk()
    {
        var source = """
            var a = [{ content: "alpha", metadata: { source: "a.md", chunk: 0 } }];
            var b = [{ content: "alpha", metadata: { source: "a.md", chunk: 0 } }];
            var merged = mergeRetrievedDocs(a, b);
            print(length(merged));
            """;
        Assert.Equal("1", RunProgram(source).Trim());
    }
}
