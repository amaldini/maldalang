// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class AiPipeChainTests : TestBase
{
    [Fact]
    public void Pipe_PromptPrependsLeftArgument()
    {
        var source = """
            prompt simple(text) {
                user: "Value: {text}"
            }

            var p = "hello" |> simple;
            print(p.user);
            """;
        Assert.Equal("Value: hello", RunProgram(source).Trim());
    }

    [Fact]
    public void Pipe_PromptWithExtraArgs()
    {
        var source = """
            prompt combine(prefix, text) {
                user: "{prefix}{text}"
            }

            var p = "!" |> (msg) => combine("Hi ", msg);
            print(p.user);
            """;
        Assert.Equal("Hi !", RunProgram(source).Trim());
    }

    [Fact]
    public void ParseJson_ValidatesSchema()
    {
        var source = """
            schema Answer {
                text: string;
                score: float;
            }

            var parsed = parseJson("{\"text\":\"ok\",\"score\":0.9}", "Answer");
            print(parsed.text);
            print(string(parsed.score));
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("ok", output[0]);
        Assert.Equal("0.9", output[1]);
    }

    [Fact]
    public void Pipe_ParseJsonAfterString()
    {
        var source = """
            schema Answer {
                text: string;
            }

            var parsed = "{\"text\":\"piped\"}" |> parseJson("Answer");
            print(parsed.text);
            """;
        Assert.Equal("piped", RunProgram(source).Trim());
    }

    [Fact]
    public void SplitDocuments_ChunksContent()
    {
        var source = """
            var docs = [{ content: "abcdefghij", metadata: { source: "a.txt" } }];
            var chunks = splitDocuments(docs, 4, 1);
            print(length(chunks));
            print(chunks[0].content);
            print(chunks[1].content);
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("3", output[0]);
        Assert.Equal("abcd", output[1]);
        Assert.Equal("defg", output[2]);
    }

    [Fact]
    public void FormatRetrievedDocs_JoinsDocuments()
    {
        var source = """
            var docs = [
                { content: "alpha", metadata: { source: "a.md" } },
                { content: "beta", metadata: { source: "b.md" } }
            ];
            var text = formatRetrievedDocs(docs);
            print(text);
            """;
        var output = RunProgram(source).Trim();
        Assert.Contains("[source: a.md]", output);
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
    }

    [Fact]
    public void Retriever_GetFromVectorDb()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);
            vdb.add("GraphMemory stores semantic facts");
            vdb.add("VectorDB supports cosine search");

            var retriever = vdb.asRetriever({ topK: 1 });
            var hits = "GraphMemory" |> retriever.get;
            print(length(hits));
            print(hits[0].content);
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("1", output[0]);
        Assert.Contains("GraphMemory", output[1]);
    }

    [Fact]
    public void Transpiled_PipePrompt_MatchesInterpreter()
    {
        var source = """
            prompt simple(text) {
                user: "Value: {text}"
            }
            var p = "hello" |> simple;
            print(p.user);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void SchemaBeforePrompt_WithStringArrayField_RegistersCallablePrompt()
    {
        var source = """
            schema Answer {
                text: string;
                sources: string[];
            }

            prompt answer(question) {
                user: "Q: {question}"
            }

            var p = "hello" |> answer;
            print(p.user);
            """;
        Assert.Equal("Q: hello", RunProgram(source).Trim());
    }

    [Fact]
    public void ParseJson_ValidatesStringArraySchema()
    {
        var source = """
            schema Answer {
                text: string;
                sources: string[];
            }

            var parsed = parseJson("{\"text\":\"ok\",\"sources\":[\"a.md\",\"b.md\"]}", "Answer");
            print(parsed.text);
            print(length(parsed.sources));
            print(parsed.sources[0]);
            """;
        var output = RunProgram(source).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("ok", output[0]);
        Assert.Equal("2", output[1]);
        Assert.Equal("a.md", output[2]);
    }

    [Fact]
    public void IndexInto_PreservesSourceMetadataInRetriever()
    {
        var source = """
            function embedText(text) {
                return embedBagOfWords(text, 8);
            }

            var vdb = new VectorDB(8, "single");
            vdb.init(embedText);

            var docs = [
                { content: "GraphMemory stores semantic facts", metadata: { source: "memory.md" } }
            ];
            indexInto(vdb, docs);

            var retriever = vdb.asRetriever({ topK: 1 });
            var hits = "GraphMemory" |> retriever.get;
            print(hits[0].metadata.source);
            """;
        Assert.Equal("memory.md", RunProgram(source).Trim());
    }

    [Fact]
    public void LoadDocuments_ReadsFilesWithMetadata()
    {
        var tempDir = CreateTempDirectory("load_docs_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "note.txt"), "Document body");

            var docs = AiPipelineHelpers.LoadDocuments(new List<RuntimeValue>
            {
                RuntimeValue.String("**/*.txt"),
                RuntimeValue.String(tempDir)
            });

            Assert.Equal(ValueType.Array, docs.Type);
            var items = docs.AsArray();
            Assert.Single(items);
            Assert.Equal(ValueType.Object, items[0].Type);
            var doc = Assert.IsType<DocumentInstance>(items[0].AsObject());
            Assert.Equal("Document body", doc.Content);
            Assert.Contains("note.txt", doc.GetMetadataString("source") ?? "");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Await_SyncPipeExpression_ReturnsResult()
    {
        var source = """
            schema Answer { text: string; }
            var parsed = await ("{\"text\":\"awaited\"}" |> parseJson("Answer"));
            print(parsed.text);
            """;
        Assert.Equal("awaited", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_FormatRetrievedDocsPipe_MatchesInterpreter()
    {
        var source = """
            var docs = [{ content: "alpha", metadata: { source: "a.md" } }];
            var text = docs |> formatRetrievedDocs;
            print(text);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
        Assert.Contains("[source: a.md]", interpreted);
    }

    [Fact]
    public void Transpiled_ParseJsonPipe_MatchesInterpreter()
    {
        var source = """
            schema Answer { text: string; }
            var parsed = "{\"text\":\"x\"}" |> parseJson("Answer");
            print(parsed.text);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void Transpiled_SchemaBeforePrompt_MatchesInterpreter()
    {
        var source = """
            schema Answer {
                text: string;
                sources: string[];
            }

            prompt answer(q) {
                user: "{q}"
            }

            var p = "test" |> answer;
            print(p.user);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }
}
