// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class Phase72ResourceTests : TestBase
{
    private static void AssertParses(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        _ = parser.Parse();
        Assert.Empty(parser.Errors);
    }

    [Fact]
    public void UsingResource_CallsDisposeAfterBody()
    {
        var source = """
            class Resource {
                function dispose() {
                    print("disposed");
                }
            }
            using r = new Resource() {
                print("body");
            }
            """;
        Assert.Equal("body\ndisposed", RunProgram(source).Trim());
    }

    [Fact]
    public void Defer_RunsInLifoOrderOnScopeExit()
    {
        var source = """
            function run() {
                defer { print("second"); }
                defer { print("first"); }
                print("body");
            }
            run();
            """;
        Assert.Equal("body\nfirst\nsecond", RunProgram(source).Trim());
    }

    [Fact]
    public void Defer_RunsBeforeFunctionReturn()
    {
        var source = """
            function run() {
                defer { print("defer"); }
                print("body");
                return;
            }
            run();
            """;
        Assert.Equal("body\ndefer", RunProgram(source).Trim());
    }

    [Fact]
    public void Using_WithDeferInside_RunsDeferBeforeDispose()
    {
        var source = """
            class Resource {
                function dispose() {
                    print("dispose");
                }
            }
            using r = new Resource() {
                defer { print("defer"); }
                print("body");
            }
            """;
        Assert.Equal("body\ndefer\ndispose", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_UsingAndDefer_MatchInterpreter()
    {
        var source = """
            class Resource {
                function dispose() {
                    print("dispose");
                }
            }
            using r = new Resource() {
                defer { print("defer"); }
                print("body");
            }
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void Parser_AcceptsUsingResourceAndDefer()
    {
        AssertParses("""
            class Log {
                function dispose() { }
            }
            using f = new Log() {
                defer { print("cleanup"); }
                print("work");
            }
            """);
    }
}
