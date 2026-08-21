// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

public class ResultOptionStdLibTests : TestBase
{
    [Fact]
    public void Result_MapAndUnwrapOr_OnOk()
    {
        var source = """
            var r = result.ok(10);
            var doubled = result.map(r, (x) => x * 2);
            print(result.unwrapOr(doubled, 0));
            print(result.isOk(r));
            print(result.isErr(r));
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("20", lines[0]);
        Assert.Equal("true", lines[1]);
        Assert.Equal("false", lines[2]);
    }

    [Fact]
    public void Result_MapLeavesErr_UnwrapOrUsesDefault()
    {
        var source = """
            var r = result.err("bad");
            var mapped = result.map(r, (x) => x);
            print(result.isErr(mapped));
            print(result.unwrapOr(mapped, 99));
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("99", lines[1]);
    }

    [Fact]
    public void Option_MapAndUnwrapOr_OnSome()
    {
        var source = """
            var o = option.some(3);
            var next = option.map(o, (n) => n + 1);
            print(option.unwrapOr(next, 0));
            print(option.isNone(option.none()));
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("4", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void Result_AndThen_ChainsOk()
    {
        var source = """
            var r = result.ok(10);
            var doubled = result.andThen(r, (x) => result.ok(x * 2));
            print(result.unwrapOr(doubled, 0));
            print(result.isOk(doubled));
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("20", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void Result_AndThen_OkToErr()
    {
        var source = """
            var r = result.ok(10);
            var failed = result.andThen(r, (x) => result.err("no"));
            print(result.isErr(failed));
            print(result.unwrapOr(failed, 99));
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("99", lines[1]);
    }

    [Fact]
    public void Result_AndThen_ShortCircuitsErr()
    {
        var source = """
            var called = false;
            function mark(x) {
                called = true;
                return result.ok(x);
            }
            var mapped = result.andThen(result.err("bad"), mark);
            print(result.isErr(mapped));
            print(called);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
    }

    [Fact]
    public void Result_AndThen_PipeChains()
    {
        var source = """
            var chained = result.ok(10)
                |> result.andThen((x) => result.ok(x * 2))
                |> result.andThen((x) => result.ok(x + 1));
            print(result.unwrapOr(chained, 0));
            """;
        Assert.Equal("21", RunProgram(source).Trim());
    }

    [Fact]
    public void Result_AndThen_RejectsBarePayload()
    {
        var source = """
            result.andThen(result.ok(1), (x) => x + 1);
            """;
        var ex = Assert.ThrowsAny<Exception>(() => RunProgram(source));
        Assert.Contains("andThen()", ex.Message, StringComparison.Ordinal);
        Assert.Contains("result.map", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_AndThen_RejectsOptionFamily()
    {
        var source = """
            result.andThen(result.ok(1), (x) => option.some(x));
            """;
        var ex = Assert.ThrowsAny<Exception>(() => RunProgram(source));
        Assert.Contains("andThen()", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Some", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Option_AndThen_SomeToNone()
    {
        var source = """
            var dropped = option.andThen(option.some(3), (n) => option.none());
            print(option.isNone(dropped));
            print(option.unwrapOr(dropped, 0));
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("0", lines[1]);
    }

    [Fact]
    public void Option_AndThen_ShortCircuitsNone()
    {
        var source = """
            var called = false;
            function mark(n) {
                called = true;
                return option.some(n);
            }
            var mapped = option.andThen(option.none(), mark);
            print(option.isNone(mapped));
            print(called);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
    }

    [Fact]
    public void NullConditionalMember_ReturnsNullWithoutError()
    {
        var source = """
            var d = null;
            var x = d?.missing;
            print(x == null);
            """;
        Assert.Equal("true", RunProgram(source).Trim());
    }

    [Fact]
    public void NullConditionalIndex_OnDict_ReturnsNull()
    {
        var source = """
            var d = null;
            print(d?["key"] == null);
            """;
        Assert.Equal("true", RunProgram(source).Trim());
    }

    [Fact]
    public void NullConditional_ChainWhenPresent_ReturnsValue()
    {
        var source = """
            var d = dict { "a": 7 };
            print(d?.a);
            """;
        Assert.Equal("7", RunProgram(source).Trim());
    }
}
