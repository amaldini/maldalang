// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class NullCoalesceTests : TestBase
{
    [Fact]
    public void NullCoalesce_KeepsNonNullValues()
    {
        var output = RunProgram("""
            print(0 ?? 1);
            print(false ?? true);
            print("hi" ?? "x");
            print(null ?? "fallback");
            print((null ?? null) ?? "z");
            """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("0", output[0].Trim());
        Assert.Equal("false", output[1].Trim());
        Assert.Equal("hi", output[2].Trim());
        Assert.Equal("fallback", output[3].Trim());
        Assert.Equal("z", output[4].Trim());
    }

    [Fact]
    public void NullCoalesce_CombinesWithNullConditional()
    {
        var output = RunProgram("""
            var user = null;
            print(user?.name ?? "guest");
            print("|" + str.trimText(user?.name) + "|");
            var obj = { "name": "Ada" };
            print(obj?.name ?? "guest");
            """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("guest", output[0].Trim());
        Assert.Equal("||", output[1].Trim());
        Assert.Equal("Ada", output[2].Trim());
    }

    [Fact]
    public void NullCoalesce_ShortCircuitsRightSide()
    {
        var output = RunProgram("""
            var hits = 0;
            function bump() {
                hits = hits + 1;
                return "B";
            }
            print("A" ?? bump());
            print(hits);
            print(null ?? bump());
            print(hits);
            """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("A", output[0].Trim());
        Assert.Equal("0", output[1].Trim());
        Assert.Equal("B", output[2].Trim());
        Assert.Equal("1", output[3].Trim());
    }

    [Fact]
    public void NullCoalesce_MatchesInTranspiledMode()
    {
        var source = """
            print(null ?? "ok");
            print(0 ?? 9);
            var u = null;
            print(u?.x ?? "g");
            """;
        var interpreted = RunProgram(source);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
    }
}
