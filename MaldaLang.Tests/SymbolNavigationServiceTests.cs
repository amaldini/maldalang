// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class SymbolNavigationServiceTests
{
    private readonly SymbolNavigationService _service = new();

    [Fact]
    public void GetDocumentSymbols_ReturnsTopLevelAndWorkflowChildren()
    {
        const string source = """
workflow OrderFlow(orderId) {
    step validate = doWork(orderId);
    approval managerApproval = approval("manager") timeout 1000;
}

function doWork(orderId) {
    return orderId;
}

function helper() {
    return 1;
}
""";

        var symbols = _service.GetDocumentSymbols(source, "order.malda");

        Assert.Equal(3, symbols.Count);
        Assert.Equal("OrderFlow", symbols[0].Name);
        Assert.Equal(SymbolItemKind.Workflow, symbols[0].Kind);
        Assert.Equal(0, symbols[0].Span.Line);
        Assert.Contains(symbols[0].Children, child => child.Name == "validate" && child.Kind == SymbolItemKind.Step);
        Assert.Contains(symbols[0].Children, child => child.Name == "managerApproval" && child.Kind == SymbolItemKind.Event);
        Assert.Equal("doWork", symbols[1].Name);
        Assert.Equal(SymbolItemKind.Function, symbols[1].Kind);
        Assert.Equal(5, symbols[1].Span.Line);
        Assert.Equal("helper", symbols[2].Name);
        Assert.Equal(SymbolItemKind.Function, symbols[2].Kind);
        Assert.Equal(9, symbols[2].Span.Line);
    }

    [Fact]
    public void GetDefinition_FunctionUsage_ReturnsDeclarationLocation()
    {
        const string source = """
function foo() {
    return 1;
}

var result = foo();
""";

        var definition = _service.GetDefinition(source, 4, 14, "test.malda");

        Assert.NotNull(definition);
        Assert.Equal("foo", definition!.Name);
        Assert.Equal(0, definition.Span.Line);
        Assert.Equal(9, definition.Span.Column);
    }

    [Fact]
    public void GetDocumentSymbols_ClassMembers_UseDeclarationNameLocations()
    {
        const string source = """
class Person {
    function Person(name) {
        this.name = name;
    }

    function greet() {
        print("hi");
    }
}

prompt summarize(text) {
    system "Summarize";
}
""";

        var symbols = _service.GetDocumentSymbols(source, "members.malda");

        var person = Assert.Single(symbols, symbol => symbol.Name == "Person");
        Assert.Equal(0, person.Span.Line);

        var constructor = Assert.Single(person.Children, child => child.Name == "Person");
        Assert.Equal(1, constructor.Span.Line);

        var greet = Assert.Single(person.Children, child => child.Name == "greet");
        Assert.Equal(5, greet.Span.Line);

        var summarize = Assert.Single(symbols, symbol => symbol.Name == "summarize");
        Assert.Equal(10, summarize.Span.Line);
    }

    [Fact]
    public void GetReferences_ReturnsDeclarationAndUsage()
    {
        const string source = """
function foo() {
    return 1;
}

var first = foo();
var second = foo();
""";

        var references = _service.GetReferences(source, 0, 10, "test.malda");

        Assert.Equal(3, references.Count);
        Assert.All(references, reference => Assert.Equal("foo", reference.Name));
    }

    [Fact]
    public void Rename_ValidSymbol_ReturnsEditsForEveryReference()
    {
        const string source = """
function foo() {
    return 1;
}

var result = foo();
""";

        var edits = _service.Rename(source, 0, 10, "bar", "test.malda");

        Assert.NotNull(edits);
        Assert.Equal(2, edits!.Count);
        Assert.All(edits, edit => Assert.Equal("bar", edit.NewText));
    }

    [Fact]
    public void Rename_InvalidIdentifier_ReturnsNull()
    {
        const string source = "function foo() { return 1; }";

        var edits = _service.Rename(source, 0, 10, "123bad", "test.malda");

        Assert.Null(edits);
    }

    [Fact]
    public void Rename_IncludesIdentifierInsideInterpolatedString()
    {
        const string source = """
var name = "world";
print($"hello {name}");
""";

        var edits = _service.Rename(source, 0, 4, "title", "test.malda");

        Assert.NotNull(edits);
        Assert.Equal(2, edits!.Count);
        Assert.All(edits, edit => Assert.Equal("title", edit.NewText));
        Assert.All(edits, edit => Assert.Equal("name", Slice(source, edit.Span)));
        Assert.Contains(edits, edit => edit.Span.Line == 1);
    }

    [Fact]
    public void Rename_FromInterpolatedStringUsage_RenamesDeclaration()
    {
        const string source = """
var name = "world";
print($"hello {name}");
""";

        var interpolationColumn = source.Replace("\r\n", "\n").Split('\n')[1].IndexOf("name", StringComparison.Ordinal);
        var edits = _service.Rename(source, 1, interpolationColumn, "title", "test.malda");

        Assert.NotNull(edits);
        Assert.Equal(2, edits!.Count);
        Assert.Contains(edits, edit => edit.Span.Line == 0 && Slice(source, edit.Span) == "name");
        Assert.Contains(edits, edit => edit.Span.Line == 1 && Slice(source, edit.Span) == "name");
    }

    [Fact]
    public void PrepareRename_IdentifierInsideInterpolatedString_ReturnsTarget()
    {
        const string source = """
var name = "world";
print($"hello { name }");
""";

        var interpolationColumn = source.Replace("\r\n", "\n").Split('\n')[1].IndexOf("name", StringComparison.Ordinal);
        var target = _service.PrepareRename(source, 1, interpolationColumn, "test.malda");

        Assert.NotNull(target);
        Assert.Equal("name", target!.Name);
        Assert.Equal(1, target.Span.Line);
        Assert.Equal("name", Slice(source, target.Span));
    }

    [Fact]
    public void GetReferences_IncludesInterpolatedStringAndExpressionUsages()
    {
        const string source = """
var name = "world";
print($"hello {name} and {name.ToUpper()}");
print(name);
""";

        var references = _service.GetReferences(source, 0, 4, "test.malda");

        Assert.Equal(4, references.Count);
        Assert.All(references, reference => Assert.Equal("name", Slice(source, reference.Span)));
        Assert.Equal(2, references.Count(reference => reference.Span.Line == 1));
    }

    [Fact]
    public void Rename_DoesNotRewritePlainStringThatLooksLikeInterpolation()
    {
        const string source = """
var name = "world";
print("hello {name}");
""";

        var edits = _service.Rename(source, 0, 4, "title", "test.malda");

        Assert.NotNull(edits);
        Assert.Single(edits!);
        Assert.Equal(0, edits[0].Span.Line);
        Assert.Equal("name", Slice(source, edits[0].Span));
    }

    [Fact]
    public void Rename_IncludesIdentifierInsideTripleQuotedInterpolatedString()
    {
        const string source = """"
var name = "world";
var greeting = $"""
Hello {name}
""";
"""";

        var edits = _service.Rename(source, 0, 4, "title", "test.malda");

        Assert.NotNull(edits);
        Assert.Equal(2, edits!.Count);
        Assert.Contains(edits, edit => edit.Span.Line == 2 && Slice(source, edit.Span) == "name");
    }

    [Fact]
    public void RenameWorkspaceSymbol_IncludesInterpolatedStringUsage()
    {
        const string librarySource = """
function greet() {
    return "hi";
}
""";
        const string mainSource = """
print($"msg {greet()}");
""";

        var documents = new[]
        {
            new WorkspaceDocumentInfo { SourceKey = "lib.malda", Text = librarySource },
            new WorkspaceDocumentInfo { SourceKey = "main.malda", Text = mainSource }
        };

        var edits = _service.RenameWorkspaceSymbol(documents, librarySource, 0, 9, "welcome", "lib.malda");

        Assert.NotNull(edits);
        Assert.Equal(2, edits!.Count);
        Assert.Contains(edits, edit => edit.SourceKey == "lib.malda" && Slice(librarySource, edit.Span) == "greet");
        Assert.Contains(edits, edit => edit.SourceKey == "main.malda" && Slice(mainSource, edit.Span) == "greet");
    }

    [Fact]
    public void GetWorkspaceDefinition_CrossFileFunctionUsage_ReturnsExternalDeclaration()
    {
        const string librarySource = """
function sharedHelper() {
    return 1;
}
""";
        const string mainSource = """
var result = sharedHelper();
""";

        var documents = new[]
        {
            new WorkspaceDocumentInfo { SourceKey = "lib.malda", Text = librarySource },
            new WorkspaceDocumentInfo { SourceKey = "main.malda", Text = mainSource }
        };

        var definition = _service.GetWorkspaceDefinition(documents, mainSource, 0, 14, "main.malda");

        Assert.NotNull(definition);
        Assert.Equal("sharedHelper", definition!.Name);
        Assert.Equal("lib.malda", definition.SourceKey);
        Assert.Equal(0, definition.Span.Line);
    }

    [Fact]
    public void RenameWorkspaceSymbol_TopLevelFunction_ReturnsEditsAcrossDocuments()
    {
        const string librarySource = """
function sharedHelper() {
    return 1;
}
""";
        const string mainSource = """
var first = sharedHelper();
var second = sharedHelper();
""";

        var documents = new[]
        {
            new WorkspaceDocumentInfo { SourceKey = "lib.malda", Text = librarySource },
            new WorkspaceDocumentInfo { SourceKey = "main.malda", Text = mainSource }
        };

        var edits = _service.RenameWorkspaceSymbol(documents, librarySource, 0, 10, "renamedHelper", "lib.malda");

        Assert.NotNull(edits);
        Assert.Equal(3, edits!.Count);
        Assert.Contains(edits, edit => edit.SourceKey == "lib.malda");
        Assert.Equal(2, edits.Count(edit => edit.SourceKey == "main.malda"));
        Assert.All(edits, edit => Assert.Equal("renamedHelper", edit.NewText));
    }

    private static string Slice(string source, TextSpanInfo span)
    {
        var line = source.Replace("\r\n", "\n").Split('\n')[span.Line];
        return line.Substring(span.Column, span.Length);
    }
}
