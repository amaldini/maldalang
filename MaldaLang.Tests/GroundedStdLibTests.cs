// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class GroundedStdLibTests : TestBase
{
    [Fact]
    public void Wrap_ExposesValueCitationsAndSourced()
    {
        var source = """
            var g = grounded.wrap("the sky is blue", [
                { "source": "wiki", "id": "p1", "span": "12-40" }
            ]);
            print(g.value);
            print(g.sourced);
            print(g.citations.length);
            print(g.citations[0].source);
            print(g.citations[0].id);
            print(g.citations[0].span);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("the sky is blue", lines[0]);
        Assert.Equal("true", lines[1]);
        Assert.Equal("1", lines[2]);
        Assert.Equal("wiki", lines[3]);
        Assert.Equal("p1", lines[4]);
        Assert.Equal("12-40", lines[5]);
    }

    [Fact]
    public void Wrap_WithoutCitations_IsUnsourced()
    {
        var source = """
            var g = grounded.wrap("bare");
            print(g.value);
            print(g.sourced);
            print(g.citations.length);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("bare", lines[0]);
        Assert.Equal("false", lines[1]);
        Assert.Equal("0", lines[2]);
    }

    [Fact]
    public void Wrap_NormalizesStringCitation()
    {
        var source = """
            var g = grounded.wrap(42, "notes.md");
            print(g.value);
            print(g.sourced);
            print(g.citations[0].source);
            print(g.citations[0].id == null);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("42", lines[0]);
        Assert.Equal("true", lines[1]);
        Assert.Equal("notes.md", lines[2]);
        Assert.Equal("true", lines[3]);
    }

    [Fact]
    public void Wrap_IsNotAFlatAlias()
    {
        var source = """
            var threw = false;
            try {
                wrap("x", []);
            } catch (e) {
                threw = true;
            }
            print(threw);
            print(grounded != null);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void GraphMemoryAsk_ReturnsCitationsOnWrapper()
    {
        var source = """
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember("Alice prefers dark mode", "", { "source": "notes.md" });
            memory.remember("Alice works as a software engineer");
            var g = memory.ask("What are Alice's preferences?", 5, { "minScore": 0 });
            print(g.sourced);
            print(g.citations.length >= 1);
            print(g.value.length >= 1);
            print(g.citations[0].source != null);
            print(g.citations[0].id != null);
            var hits = memory.query("What are Alice's preferences?", 5, { "minScore": 0 });
            print(hits.length >= 1);
            print(typeOf(hits) == "array");
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("true", lines[1]);
        Assert.Equal("true", lines[2]);
        Assert.Equal("true", lines[3]);
        Assert.Equal("true", lines[4]);
        Assert.Equal("true", lines[5]);
        Assert.Equal("true", lines[6]);
    }

    [Fact]
    public void GraphMemoryQuery_GroundedOption_WrapsHits()
    {
        var source = """
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember("Unique grounded token ZZGRND42", "notes.md#chunk-2", { "filePath": "notes.md" });
            var g = memory.query("ZZGRND42", 3, { "minScore": 0, "grounded": true });
            print(g.sourced);
            print(g.citations[0].source);
            print(g.citations[0].span);
            print(g.value[0].fact == "Unique grounded token ZZGRND42");
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("notes.md", lines[1]);
        Assert.Equal("#chunk-2", lines[2]);
        Assert.Equal("true", lines[3]);
    }

    [Fact]
    public void Wrap_TranspileAgreesWithInterpreter()
    {
        var source = """
            var g = grounded.wrap("payload", [{ "source": "a", "id": "1" }]);
            print(g.value);
            print(g.sourced);
            print(g.citations[0].source);
            """;
        var interpreted = RunProgram(source).Replace("\r\n", "\n").Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Replace("\r\n", "\n").Trim();
        Assert.Equal(interpreted, transpiled);
        Assert.Contains("payload", transpiled);
        Assert.Contains("true", transpiled);
        Assert.Contains("a", transpiled);
    }

    [Fact]
    public void GraphMemoryAsk_TranspileAgreesWithInterpreter()
    {
        var source = """
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember("Transpile grounded fact ZZASK99");
            var g = memory.ask("ZZASK99", 3, { "minScore": 0 });
            print(g.sourced);
            print(g.citations.length >= 1);
            print(g.value.length >= 1);
            """;
        var interpreted = RunProgram(source).Replace("\r\n", "\n").Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Replace("\r\n", "\n").Trim();
        Assert.Equal(interpreted, transpiled);
        Assert.Contains("true", transpiled);
    }
}
