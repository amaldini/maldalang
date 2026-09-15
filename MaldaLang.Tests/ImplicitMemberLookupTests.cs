// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ImplicitMemberLookupTests : TestBase
{
    [Fact]
    public void MethodBareName_PrefersFieldOverOuterVar()
    {
        var output = RunProgram("""
            var x = 10;
            class Point(x, y) {
                function total() { return x + y; }
            }
            print(new Point(2, 3).total());
            """);
        Assert.Equal("5", output);
    }

    [Fact]
    public void ConstructorParameter_StillShadowsField()
    {
        var output = RunProgram("""
            var x = 10;
            class Box {
                public var x;
                function Box(x) {
                    this.x = x;
                }
                function get() { return x; }
            }
            print(new Box(2).get());
            """);
        Assert.Equal("2", output);
    }

    [Fact]
    public void MethodParameter_ShadowsField()
    {
        var output = RunProgram("""
            class Point(x, y) {
                function add(x) { return x + y; }
            }
            print(new Point(2, 3).add(10));
            """);
        Assert.Equal("13", output);
    }

    [Fact]
    public void MethodLocal_ShadowsField()
    {
        var output = RunProgram("""
            class Point(x, y) {
                function total() {
                    var x = 100;
                    return x + y;
                }
            }
            print(new Point(2, 3).total());
            """);
        Assert.Equal("103", output);
    }

    [Fact]
    public void BareNameWithoutOuterBinding_StillReadsField()
    {
        var output = RunProgram("""
            class Point(x, y) {
                function total() { return x + y; }
            }
            print(new Point(2, 3).total());
            """);
        Assert.Equal("5", output);
    }

    [Fact]
    public void MethodAssignment_WritesFieldNotOuterVar()
    {
        var output = RunProgram("""
            var x = 10;
            class Point(x, y) {
                function bump() {
                    x = x + 1;
                    return x;
                }
            }
            var p = new Point(2, 3);
            print(p.bump());
            print(p.x);
            print(x);
            """);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(new[] { "3", "3", "10" }, lines);
    }

    [Fact]
    public void MethodAssignment_FieldWinsOverOuterConst()
    {
        var output = RunProgram("""
            const x = 10;
            class Point(x, y) {
                function setX(n) {
                    x = n;
                    return x;
                }
            }
            print(new Point(2, 3).setX(8));
            print(x);
            """);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(new[] { "8", "10" }, lines);
    }

    [Fact]
    public void FreeFunctionCalledFromMethod_StillSeesGlobal()
    {
        var output = RunProgram("""
            var x = 10;
            function readX() { return x; }
            class Point(x, y) {
                function total() { return readX() + y; }
            }
            print(new Point(2, 3).total());
            """);
        Assert.Equal("13", output);
    }

    [Fact]
    public void StaticField_BareNameStillBeatsOuterVar()
    {
        var output = RunProgram("""
            var count = 99;
            class Counter {
                static var count = 0;
                static function increment() {
                    count = count + 1;
                }
                static function getCount() {
                    return count;
                }
            }
            Counter.increment();
            print(Counter.getCount());
            print(count);
            """);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(new[] { "1", "99" }, lines);
    }
}
