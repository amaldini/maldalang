// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Cli;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Formatter and command-parse coverage for the REPL <c>source</c> dump.
/// </summary>
public class ReplSessionSourceTests
{
    [Theory]
    [InlineData("source", "")]
    [InlineData("SOURCE", "")]
    [InlineData("src", "")]
    [InlineData("source functions", "functions")]
    [InlineData("src add", "add")]
    public void TryParseCommand_RecognizesVerbsAndFilters(string line, string expectedFilter)
    {
        Assert.True(ReplSessionSource.TryParseCommand(line, out var filter));
        Assert.Equal(expectedFilter, filter);
    }

    [Theory]
    [InlineData("source = 1")]
    [InlineData("src + 2")]
    [InlineData("print(1)")]
    [InlineData("vars")]
    [InlineData("")]
    public void TryParseCommand_RejectsAssignmentsAndOrdinaryCode(string line)
    {
        Assert.False(ReplSessionSource.TryParseCommand(line, out _));
    }

    [Fact]
    public void Format_EmptySession_PrintsNoUserDefinitions()
    {
        var session = new ReplSessionSource();

        Assert.Equal("(no user definitions)", session.Format(null));
    }

    [Fact]
    public void Record_PrintsDefinitionsInSourceForm()
    {
        var session = new ReplSessionSource();
        session.Record("var x = 10;");
        session.Record(@"function add(a, b) {
    return a + b;
}");

        var text = session.Format(null);

        Assert.Contains("var x = 10;", text);
        Assert.Contains("function add(a, b) {", text);
        Assert.Contains("return a + b;", text);
        Assert.DoesNotContain("variables:", text);
    }

    [Fact]
    public void Record_SkipsPrintStatements()
    {
        var session = new ReplSessionSource();
        session.Record("print(1);");
        session.Record("var x = 2;");

        var text = session.Format(null);

        Assert.Equal("var x = 2;", text);
        Assert.DoesNotContain("print", text);
    }

    [Fact]
    public void Record_Redefinition_ReplacesFunctionSource()
    {
        var session = new ReplSessionSource();
        session.Record("function ping() { return 1; }");
        session.Record("function ping() { return 2; }");

        var text = session.Format(null);

        Assert.Contains("return 2;", text);
        Assert.DoesNotContain("return 1;", text);
    }

    [Fact]
    public void Record_ClassAugmentation_AppendsOriginalSnippets()
    {
        var session = new ReplSessionSource();
        session.Record("class Point(x, y);");
        session.Record("class Point function total() this.x + this.y;");

        var text = session.Format(null);

        Assert.Contains("class Point(x, y);", text);
        Assert.Contains("class Point function total() this.x + this.y;", text);
    }

    [Fact]
    public void Record_KeepsExportAndDecorators()
    {
        var session = new ReplSessionSource();
        session.Record("export function greet(who) { return who; }");
        session.Record("@GET(\"/ping\") function ping() { return 1; }");

        var text = session.Format("functions");

        Assert.Contains("export function greet(who) { return who; }", text);
        Assert.Contains("@GET(\"/ping\") function ping() { return 1; }", text);
    }

    [Fact]
    public void Format_KindFilter_OmitsOtherKinds()
    {
        var session = new ReplSessionSource();
        session.Record("var x = 1;");
        session.Record("function ping() { return 1; }");

        var text = session.Format("functions");

        Assert.Contains("function ping()", text);
        Assert.DoesNotContain("var x", text);
    }

    [Fact]
    public void Format_NameFilter_PrintsThatDefinition()
    {
        var session = new ReplSessionSource();
        session.Record("var x = 1;");
        session.Record("function ping() { return 1; }");

        var text = session.Format("ping");

        Assert.Contains("function ping() { return 1; }", text);
        Assert.DoesNotContain("var x", text);
    }

    [Fact]
    public void Format_UnknownName_PrintsKindSpecificEmpty()
    {
        var session = new ReplSessionSource();
        session.Record("var x = 1;");

        Assert.Equal("(no definition named 'missing')", session.Format("missing"));
        Assert.Equal("(no functions)", session.Format("functions"));
    }

    [Fact]
    public void Record_SameEntry_TwoDefinitions()
    {
        var session = new ReplSessionSource();
        session.Record("var x = 1; function ping() { return x; }");

        var text = session.Format(null);

        Assert.Contains("var x = 1;", text);
        Assert.Contains("function ping() { return x; }", text);
    }

    [Fact]
    public void Record_PromptActorAndWorkflow_KeepBodies()
    {
        var session = new ReplSessionSource();
        session.Record(@"
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

        var text = session.Format(null);

        Assert.Contains("prompt summarize(text) {", text);
        Assert.Contains("user: \"Hello, {text}\";", text);
        Assert.Contains("actor Greeter {", text);
        Assert.Contains("on greet() { }", text);
        Assert.Contains("workflow SimpleStep(input) {", text);
        Assert.Contains("step result = identity(input);", text);
    }
}
