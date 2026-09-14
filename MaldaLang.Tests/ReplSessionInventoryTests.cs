// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Cli;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Formatter and command-parse coverage for the REPL <c>vars</c> catalog.
/// </summary>
public class ReplSessionInventoryTests : TestBase
{
    [Theory]
    [InlineData("vars", "")]
    [InlineData("VARS", "")]
    [InlineData("defs", "")]
    [InlineData("who", "")]
    [InlineData("vars functions", "functions")]
    [InlineData("defs classes", "classes")]
    [InlineData("who variables", "variables")]
    public void TryParseCommand_RecognizesVerbsAndFilters(string line, string expectedFilter)
    {
        Assert.True(ReplSessionInventory.TryParseCommand(line, out var filter));
        Assert.Equal(expectedFilter, filter);
    }

    [Theory]
    [InlineData("vars = 1")]
    [InlineData("who + 2")]
    [InlineData("print(1)")]
    [InlineData("")]
    public void TryParseCommand_RejectsAssignmentsAndOrdinaryCode(string line)
    {
        Assert.False(ReplSessionInventory.TryParseCommand(line, out _));
    }

    [Fact]
    public void Format_EmptySession_HidesHostGlobals()
    {
        var interpreter = new Interpreter.Interpreter();
        var host = ReplSessionInventory.SnapshotHostNames(interpreter);

        var text = ReplSessionInventory.Format(interpreter, host);

        Assert.Equal("(no user definitions)", text);
        Assert.True(host.Contains("math"));
        Assert.True(host.Contains("AgentError"));
    }

    [Fact]
    public void Format_GroupsVariablesFunctionsAndClasses()
    {
        var text = FormatAfter(@"
var x = 10;
const name = ""ada"";
function greet(who) -> string { return who; }
class Point(x, y) {
    function dist() { return this.x; }
}
");

        Assert.Contains("variables:", text);
        Assert.Contains("x = 10", text);
        Assert.Contains("const name = \"ada\"", text);
        Assert.Contains("functions:", text);
        Assert.Contains("greet(who) -> string", text);
        Assert.Contains("classes:", text);
        Assert.Contains("Point(x, y)", text);
        Assert.Contains("methods: dist", text);
        Assert.DoesNotContain("math", text);
    }

    [Fact]
    public void Format_ListsPromptsActorsAndWorkflows()
    {
        var text = FormatAfter(@"
prompt summarize(text) {
    user: ""Hello, {text}"";
}

actor Greeter {
    function Greeter(actorName) { }
    on greet() { }
}

function identity(value) {
    return value;
}

workflow SimpleStep(input) {
    step result = identity(input);
    return result;
}
");

        Assert.Contains("prompts:", text);
        Assert.Contains("summarize(text)", text);
        Assert.Contains("actors:", text);
        Assert.Contains("Greeter(actorName)", text);
        Assert.Contains("handlers: greet", text);
        Assert.Contains("workflows:", text);
        Assert.Contains("SimpleStep(input)", text);
    }

    [Fact]
    public void Format_FunctionsFilter_OmitsOtherKinds()
    {
        var text = FormatAfter("var x = 1;\nfunction ping() { return 1; }\n", ReplSessionInventory.Kind.Functions);

        Assert.Contains("ping()", text);
        Assert.DoesNotContain("variables:", text);
        Assert.DoesNotContain("x = 1", text);
    }

    [Fact]
    public void Format_EmptyFilterKind_PrintsKindSpecificEmpty()
    {
        var interpreter = new Interpreter.Interpreter();
        var host = ReplSessionInventory.SnapshotHostNames(interpreter);

        var text = ReplSessionInventory.Format(interpreter, host, ReplSessionInventory.Kind.Classes);

        Assert.Equal("(no classes)", text);
    }

    [Fact]
    public void TryParseKind_UnknownFilter_ReturnsError()
    {
        Assert.False(ReplSessionInventory.TryParseKind("widgets", out _, out var error));
        Assert.Contains("Unknown vars filter 'widgets'", error);
    }

    private static string FormatAfter(string source, ReplSessionInventory.Kind kind = ReplSessionInventory.Kind.All)
    {
        var interpreter = new Interpreter.Interpreter();
        var host = ReplSessionInventory.SnapshotHostNames(interpreter);
        var tokens = new Lexer(source).Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        return ReplSessionInventory.Format(interpreter, host, kind);
    }
}
