// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class Phase75DictComprehensionTests : TestBase
{
    private const string UsersSetup = """
        var users = [
            dict { "name": "alice", "score": 10 },
            dict { "name": "bob", "score": 20 }
        ];
        """;

    [Fact]
    public void DictComprehension_DictKeyword_BuildsMap()
    {
        var source = UsersSetup + """
            var byName = dict { u.name: u.score for u in users };
            print(byName["alice"]);
            print(byName["bob"]);
            """;
        Assert.Equal("10\n20", RunProgram(source).Trim());
    }

    [Fact]
    public void DictComprehension_BareBraceSyntax_BuildsMap()
    {
        var source = UsersSetup + """
            var byName = { u.name: u.score for u in users };
            print(byName["alice"]);
            """;
        Assert.Equal("10", RunProgram(source).Trim());
    }

    [Fact]
    public void DictComprehension_WithFilter_SkipsItems()
    {
        var source = UsersSetup + """
            var high = dict { u.name: u.score for u in users if u.score > 15 };
            print(length(high.keys()));
            print(high["bob"]);
            """;
        Assert.Equal("1\n20", RunProgram(source).Trim());
    }

    [Fact]
    public void Transpiled_DictComprehension_MatchesInterpreter()
    {
        var source = UsersSetup + """
            var byName = dict { u.name: u.score for u in users };
            print(byName["alice"]);
            print(byName["bob"]);
            """;
        var interpreted = RunProgram(source).Trim();
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Trim();
        Assert.Equal(interpreted, transpiled);
    }

    [Fact]
    public void Parser_AcceptsDictComprehensionSyntax()
    {
        var lexer = new Lexer("""
            var m = dict { k: v for x in range(3) if x > 0 };
            var n = { a.name: a.value for a in items };
            """);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        Assert.Equal(2, statements.Count);
    }
}
