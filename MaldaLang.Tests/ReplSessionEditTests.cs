// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Cli;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Command-parse coverage for REPL <c>drop</c> / <c>replace</c>.
/// </summary>
public class ReplSessionEditTests
{
    [Theory]
    [InlineData("drop ping", ReplSessionEdit.DropAction, "ping")]
    [InlineData("UNDEF Point", ReplSessionEdit.DropAction, "Point")]
    [InlineData("replace add", ReplSessionEdit.ReplaceAction, "add")]
    [InlineData("edit total", ReplSessionEdit.ReplaceAction, "total")]
    [InlineData("drop", ReplSessionEdit.DropAction, "")]
    [InlineData("drop <lambda>", ReplSessionEdit.DropAction, "<lambda>")]
    [InlineData("drop add(a, b)", ReplSessionEdit.DropAction, "add")]
    [InlineData("drop <lambda>(a, b)", ReplSessionEdit.DropAction, "<lambda>")]
    [InlineData("replace add(a, b)", ReplSessionEdit.ReplaceAction, "add")]
    public void TryParseCommand_RecognizesVerbsAndNames(string line, string action, string name)
    {
        Assert.True(ReplSessionEdit.TryParseCommand(line, out var parsedAction, out var parsedName));
        Assert.Equal(action, parsedAction);
        Assert.Equal(name, parsedName);
    }

    [Theory]
    [InlineData("drop = 1")]
    [InlineData("replace + 2")]
    [InlineData("source ping")]
    [InlineData("print(1)")]
    [InlineData("")]
    public void TryParseCommand_RejectsAssignmentsAndOrdinaryCode(string line)
    {
        Assert.False(ReplSessionEdit.TryParseCommand(line, out _, out _));
    }

    [Fact]
    public void TryRemove_DropsRecordedLambdaSource()
    {
        var session = new ReplSessionSource();
        session.Record("var add = (a, b) => a + b;");

        var previous = session.TryRemove("add");

        Assert.Contains("var add = (a, b) => a + b;", previous);
        Assert.Equal("(no user definitions)", session.Format(null));
    }

    [Fact]
    public void TryRemove_DropsRecordedSource()
    {
        var session = new ReplSessionSource();
        session.Record("function ping() { return 1; }");
        session.Record("var x = 2;");

        var previous = session.TryRemove("ping");

        Assert.Contains("function ping() { return 1; }", previous);
        Assert.Equal("var x = 2;", session.Format(null));
        Assert.Null(session.TryRemove("ping"));
    }
}
