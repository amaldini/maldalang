// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Overlapping <c>async</c> user calls that <c>sleep</c> before the next <c>var</c>
/// binding must keep caller bindings and callee locals (spec §11.1 task isolation).
/// </summary>
[Collection("Sequential")]
public class AsyncTaskIsolationTests : TestBase
{
    [Fact]
    public void OverlappingUserSleep_BindsTasksOnCaller_AllSums()
    {
        var source = """
            function computeA() {
                sleep(20);
                return 1;
            }
            function computeB() {
                sleep(30);
                return 2;
            }
            var tA = async computeA();
            var tB = async computeB();
            var results = await all(tA, tB);
            print(results[0] + results[1]);
            """;
        Assert.Equal("3", RunProgram(source));
    }

    [Fact]
    public void OverlappingUserSleep_ArrayLiteral_AllSums()
    {
        var source = """
            function computeA() {
                sleep(15);
                return 1;
            }
            function computeB() {
                sleep(25);
                return 2;
            }
            var tasks = [async computeA(), async computeB()];
            var results = await all(tasks);
            print(results[0] + results[1]);
            """;
        Assert.Equal("3", RunProgram(source));
    }

    [Fact]
    public void UserSleep_LocalSurvivesAcrossAwait()
    {
        var source = """
            function compute() {
                var x = 41;
                sleep(10);
                return x + 1;
            }
            var t = async compute();
            print(await t);
            """;
        Assert.Equal("42", RunProgram(source));
    }

    [Fact]
    public void OverlappingUserSleep_LocalsDoNotLeakAcrossTasks()
    {
        var source = """
            function computeA() {
                var x = 1;
                sleep(20);
                return x;
            }
            function computeB() {
                var x = 2;
                sleep(30);
                return x;
            }
            var tA = async computeA();
            var tB = async computeB();
            var results = await all(tA, tB);
            print(results[0]);
            print(results[1]);
            """;
        Assert.Equal("1\n2", RunProgram(source));
    }

    [Fact]
    public void UserSleep_MethodThisSurvivesAcrossAwait()
    {
        var source = """
            class Box {
                public var n;
                function Box(n) {
                    this.n = n;
                }
                function get() {
                    sleep(10);
                    return this.n;
                }
            }
            var b = new Box(7);
            var t = async b.get();
            print(await t);
            """;
        Assert.Equal("7", RunProgram(source));
    }

    [Fact]
    public void UserSleep_DeferRunsOnCalleeCompletion()
    {
        var source = """
            function computeA() {
                defer { print("defer-a"); }
                sleep(10);
                print("body-a");
                return 1;
            }
            var t = async computeA();
            print("started");
            print(await t);
            """;
        Assert.Equal("started\nbody-a\ndefer-a\n1", RunProgram(source));
    }

    [Fact]
    public void BuiltinAsyncSleep_StillBindsOnCaller()
    {
        var source = """
            var tA = async sleep(10);
            var tB = async sleep(15);
            await all(tA, tB);
            print("ok");
            """;
        Assert.Equal("ok", RunProgram(source));
    }
}
