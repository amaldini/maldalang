// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Desktop IDE and LSP share these services. Completions, hover, outline, and
/// Go to Definition must see members appended by a later <c>class Name</c>.
/// </summary>
public class ClassAugmentationIdeTests
{
    private const string AugmentedSource = """
        class Point(x, y);
        class Point {
            function total() {
                return this.x + this.y;
            }
        }
        var p = new Point(3, 4);
        print(p.total());
        """;

    private readonly LanguageService _language = new();
    private readonly SymbolNavigationService _navigation = new();

    [Fact]
    public void Completions_AfterInstanceDot_IncludeOriginalFieldsAndAddedMethod()
    {
        var source = AugmentedSource.Replace("print(p.total());", "print(p.);");
        var (line, column) = PositionAfter(source, "p.");

        var completions = _language.GetCompletions(source, line, column);

        Assert.Contains(completions, c => c.Label == "x" && c.Kind == "property");
        Assert.Contains(completions, c => c.Label == "y" && c.Kind == "property");
        Assert.Contains(completions, c => c.Label == "total" && c.Kind == "method");
    }

    [Fact]
    public void Completions_ThisDotInLaterClass_IncludePrimaryFieldsAndAddedMethod()
    {
        var source = """
            class Point(x, y);
            class Point {
                function total() {
                    return this.x + this.y;
                }
            }
            """;
        var (line, column) = PositionAfter(source, "return this.");

        var completions = _language.GetCompletions(source, line, column);

        Assert.Contains(completions, c => c.Label == "x");
        Assert.Contains(completions, c => c.Label == "y");
        Assert.Contains(completions, c => c.Label == "total");
    }

    [Fact]
    public void Completions_TypeHint_StillOffersAugmentedClass()
    {
        var source = """
            class Point(x, y);
            class Point {
                function total() {
                    return this.x + this.y;
                }
            }
            var q: 
            """;
        var (line, column) = PositionAfter(source, "var q: ");

        var completions = _language.GetCompletions(source, line, column);

        Assert.Contains(completions, c => c.Kind == "type" && c.Label == "Point");
    }

    [Fact]
    public void Hover_ClassName_IsClass()
    {
        var (line, column) = PositionAfter(AugmentedSource, "new ");
        var hover = _language.GetHoverInformation(AugmentedSource, line, column);
        Assert.Equal("class Point", hover);
    }

    [Fact]
    public void Hover_AddedMethodAtCallSite_ShowsSignature()
    {
        var (line, column) = PositionAfter(AugmentedSource, "print(p.t");
        var hover = _language.GetHoverInformation(AugmentedSource, line, column);
        Assert.Equal("function total()", hover);
    }

    [Fact]
    public void Outline_SingleClass_ListsAddedMethodAtLaterLine()
    {
        var symbols = _navigation.GetDocumentSymbols(AugmentedSource, "point.malda");

        var point = Assert.Single(symbols, s => s.Name == "Point");
        Assert.Equal(0, point.Span.Line);
        Assert.Contains(point.Children, c => c.Name == "x");
        Assert.Contains(point.Children, c => c.Name == "y");
        var total = Assert.Single(point.Children, c => c.Name == "total");
        Assert.Equal(2, total.Span.Line);
    }

    [Fact]
    public void GoToDefinition_AddedMethodCall_JumpsToLaterDeclaration()
    {
        var (line, column) = PositionAfter(AugmentedSource, "print(p.");
        var definition = _navigation.GetDefinition(AugmentedSource, line, column, "point.malda");

        Assert.NotNull(definition);
        Assert.Equal("total", definition!.Name);
        Assert.Equal(2, definition.Span.Line);
    }

    [Fact]
    public void Diagnostics_ValidAugmentation_HasNoErrors()
    {
        var diagnostics = _language.GetDiagnostics(AugmentedSource, "point.malda");
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    private static (int Line, int Column) PositionAfter(string source, string marker)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var index = lines[i].IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
                return (i, index + marker.Length);
        }

        throw new InvalidOperationException($"Marker '{marker}' not found.");
    }
}
