// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class Phase7ExpressivenessTests : TestBase
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
    public void ListComprehension_WithFilter_Evaluates()
    {
        var source = """
            var evens = [x * 2 for x in range(10) if x % 2 == 0];
            print(join(evens, ","));
            """;
        Assert.Equal("0,4,8,12,16", RunProgram(source).Trim());
    }

    [Fact]
    public void ListComprehension_WithoutFilter_Evaluates()
    {
        var source = """
            var doubled = [n * 2 for n in [1, 2, 3]];
            print(join(doubled, ","));
            """;
        Assert.Equal("2,4,6", RunProgram(source).Trim());
    }

    [Fact]
    public void Pipe_ArrayFilter_Chains()
    {
        var source = """
            var data = [1, 2, 3, 4, 5];
            var evens = data |> filter((x) => x % 2 == 0);
            print(join(evens, ","));
            """;
        Assert.Equal("2,4", RunProgram(source).Trim());
    }

    [Fact]
    public void Pipe_ArraySort_Chains()
    {
        var source = """
            var data = [3, 1, 2];
            var sorted = data |> sort();
            print(join(sorted, ","));
            """;
        Assert.Equal("1,2,3", RunProgram(source).Trim());
    }

    [Fact]
    public void Pipe_UserFunction_PrependsLeftArgument()
    {
        var source = """
            function suffix(text, ending) {
                return text + ending;
            }
            var result = "hi" |> suffix("!");
            print(result);
            """;
        Assert.Equal("hi!", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_ListComprehension_MatchesInterpreter()
    {
        var source = """
            var evens = [x * 2 for x in range(6) if x % 2 == 0];
            print(join(evens, ","));
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void Transpiled_Pipe_MatchesInterpreter()
    {
        var source = """
            var data = [3, 1, 2];
            var sorted = data |> sort();
            print(join(sorted, ","));
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void Parser_AcceptsPipeAndComprehensionSyntax()
    {
        AssertParses("""
            var a = [x for x in range(3)];
            var b = range(5) |> filter((n) => n > 1) |> sort();
            """);
    }
}
