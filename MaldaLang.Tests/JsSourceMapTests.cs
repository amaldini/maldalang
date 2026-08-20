// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Compiler;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class JsSourceMapTests : TestBase
{
    [Fact]
    public void Parse_RoundTripsPrintlnLine_ToGeneratedJavaScript()
    {
        const string source = """
            function tick() {
                var score = 41;
                println(score);
            }
            tick();
            """;

        var compiler = new Compiler.Compiler();
        var result = compiler.TranspileToJavaScriptWithSourceMapFromSource(
            source,
            sourceFilePath: "game.malda",
            generatedFileName: "game.js");

        Assert.False(string.IsNullOrWhiteSpace(result.SourceMapJson));
        var map = JsSourceMap.Parse(result.SourceMapJson!);
        Assert.Equal("game.js", map.FileName);
        Assert.Contains("game.malda", map.SourceName, StringComparison.Ordinal);

        var printlnLine = LineNumber(source, "println(score);");
        var generatedLine = map.ToGeneratedLine(printlnLine);
        Assert.True(generatedLine.HasValue, "println line should appear in the source map");

        var jsLines = result.JavaScript.Replace("\r\n", "\n").Split('\n');
        Assert.InRange(generatedLine!.Value, 1, jsLines.Length);
        Assert.Contains("println", jsLines[generatedLine.Value - 1], StringComparison.Ordinal);

        Assert.Equal(printlnLine, map.ToOriginalLine(generatedLine.Value));
    }

    [Fact]
    public void ToGeneratedLineOrNext_SkipsCommentLine()
    {
        const string source = """
            var x = 1;
            // pause here
            println(x);
            """;

        var compiler = new Compiler.Compiler();
        var result = compiler.TranspileToJavaScriptWithSourceMapFromSource(source, "main.malda", "main.js");
        var map = JsSourceMap.Parse(result.SourceMapJson!);

        var commentLine = LineNumber(source, "// pause here");
        var printlnLine = LineNumber(source, "println(x);");
        var generated = map.ToGeneratedLineOrNext(commentLine);
        Assert.Equal(map.ToGeneratedLine(printlnLine), generated);
    }

    [Fact]
    public void Map_MarksUnmappedTrailingLineUnverified()
    {
        const string source = """
            println("ok");
            """;

        var compiler = new Compiler.Compiler();
        var result = compiler.TranspileToJavaScriptWithSourceMapFromSource(source, "main.malda", "main.js");
        var map = JsSourceMap.Parse(result.SourceMapJson!);

        var mapped = JsDebugBreakpointMapper.Map(
            map,
            new[]
            {
                (Line: 1, Condition: "x > 0", Enabled: true),
                (Line: 99, Condition: (string?)null, Enabled: true),
                (Line: 1, Condition: (string?)null, Enabled: false)
            });

        Assert.Equal(2, mapped.Count);
        Assert.True(mapped[0].Verified);
        Assert.Equal("x > 0", mapped[0].Condition);
        Assert.False(mapped[1].Verified);
        Assert.Equal(0, mapped[1].GeneratedLine);
    }

    [Fact]
    public void Parse_MapsMaldanoidRenderGame()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "Web", "js", "maldanoid.malda");
        Assert.True(File.Exists(path), path);
        var source = File.ReadAllText(path).Replace("\r\n", "\n");
        var compiler = new Compiler.Compiler();
        var result = compiler.TranspileToJavaScriptWithSourceMapFromSource(source, path, "maldanoid.js");
        var map = JsSourceMap.Parse(result.SourceMapJson!);
        var renderLine = LineNumber(source, "function renderGame()");
        var generated = map.ToGeneratedLineOrNext(renderLine);
        Assert.True(generated.HasValue);
        var jsLines = result.JavaScript.Replace("\r\n", "\n").Split('\n');
        Assert.Contains("renderGame", jsLines[generated.Value - 1], StringComparison.Ordinal);
    }

    private static int LineNumber(string source, string fragment)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(fragment, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        throw new InvalidOperationException($"Fragment '{fragment}' not found.");
    }
}

public class JsBrowserApiDetectorTests
{
    [Fact]
    public void UsesBrowserHost_DetectsDomGameAndThree()
    {
        Assert.True(JsBrowserApiDetector.UsesBrowserHost("var root = dom.query(\"#app\");"));
        Assert.True(JsBrowserApiDetector.UsesBrowserHost("game.clear();"));
        Assert.True(JsBrowserApiDetector.UsesBrowserHost("three.createRenderer();"));
        Assert.True(JsBrowserApiDetector.UsesBrowserHost("@client()\nfunction draw() {}"));
        Assert.True(JsBrowserApiDetector.UsesBrowserHost("@javascript()\nfunction draw() {}"));
    }

    [Fact]
    public void UsesBrowserHost_IgnoresInterpreterProgramsAndComments()
    {
        Assert.False(JsBrowserApiDetector.UsesBrowserHost("println(\"hello\");"));
        Assert.False(JsBrowserApiDetector.UsesBrowserHost("var game = 1;\nprint(game);"));
        Assert.False(JsBrowserApiDetector.UsesBrowserHost("// game.clear()\nprint(1);"));
    }

    [Fact]
    public void UsesBrowserHost_DetectsMaldanoidExample()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "Web", "js", "maldanoid.malda");
        Assert.True(File.Exists(path), path);
        Assert.True(JsBrowserApiDetector.UsesBrowserHost(File.ReadAllText(path)));
    }
}
