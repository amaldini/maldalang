using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class VirtualDocumentSegmentationServiceTests
{
    [Fact]
    public void Segment_Recompose_RoundTripPreservesOriginalSource()
    {
        var source = """
            include "shared.malda";

            @GET("/api/health")
            function health() {
                return "ok";
            }

            @client()
            function render() {
                print("ui");
            }
            """;

        var service = new VirtualDocumentSegmentationService();
        var sections = service.Segment(source);
        var recomposed = service.Recompose(sections);

        Assert.Equal(source, recomposed);
        Assert.Single(sections);
    }

    [Fact]
    public void Segment_DoesNotCreateASectionForEveryTopLevelFunction()
    {
        var source = """
            function first() {
                return 1;
            }

            function second() {
                return 2;
            }
            """;

        var service = new VirtualDocumentSegmentationService();
        var sections = service.Segment(source);

        Assert.Single(sections);
        Assert.Equal(source, sections[0].Content);
    }

    [Fact]
    public void RecalculateLineSpans_UpdatesOffsetsAfterSectionEdit()
    {
        var source = """
            // @malda-section First
            class First {
                function value() {
                    return 1;
                }
            }

            // @malda-section Second
            class Second {
                function value() {
                    return 2;
                }
            }
            """;

        var service = new VirtualDocumentSegmentationService();
        var sections = service.Segment(source).ToList();
        Assert.True(sections.Count >= 2);

        sections[0].Content += "\n// extra line\n";
        service.RecalculateLineSpans(sections);

        Assert.True(sections[1].StartLine > sections[0].StartLine);
    }

    [Fact]
    public void Segment_UsesExplicitSectionSeparators()
    {
        var source = """
            include "shared.malda";

            // @malda-section API
            @GET("/api/health")
            function health() {
                return "ok";
            }

            // @malda-section UI
            @client()
            function render() {
                print("ui");
            }
            """;

        var service = new VirtualDocumentSegmentationService();
        var sections = service.Segment(source);
        var recomposed = service.Recompose(sections);

        Assert.Equal(source, recomposed);
        Assert.Equal(3, sections.Count);
        Assert.Equal("section 1", sections[0].Title);
        Assert.Equal("API", sections[1].Title);
        Assert.Equal("UI", sections[2].Title);
    }

    [Fact]
    public void RecomposePreservingClosedSections_KeepsSectionsThatAreNotOpen()
    {
        var source = """
            // @malda-section A
            function a() {
                return "old a";
            }

            // @malda-section B
            function b() {
                return "keep b";
            }

            // @malda-section C
            function c() {
                return "old c";
            }
            """;

        var service = new VirtualDocumentSegmentationService();
        var sections = service.Segment(source).ToList();
        sections[0].Content = sections[0].Content.Replace("old a", "new a", StringComparison.Ordinal);
        sections[2].Content = sections[2].Content.Replace("old c", "new c", StringComparison.Ordinal);

        var recomposed = service.RecomposePreservingClosedSections(new[] { sections[0], sections[2] }, source);

        Assert.Contains("new a", recomposed);
        Assert.Contains("keep b", recomposed);
        Assert.Contains("new c", recomposed);
        Assert.DoesNotContain("old a", recomposed);
        Assert.DoesNotContain("old c", recomposed);
    }

    [Fact]
    public void Segment_AddsSuffixesToDuplicateSectionTitles()
    {
        var source = """
            // @malda-section API
            function first() {
                return 1;
            }

            // @malda-section API
            function second() {
                return 2;
            }

            // @malda-section api
            function third() {
                return 3;
            }
            """;

        var service = new VirtualDocumentSegmentationService();
        var sections = service.Segment(source);

        Assert.Equal("API", sections[0].Title);
        Assert.Equal("API (2)", sections[1].Title);
        Assert.Equal("api (3)", sections[2].Title);
        Assert.Equal(source, service.Recompose(sections));
    }

    [Fact]
    public void Segment_IgnoresDecorativeDashCommentLines()
    {
        var source = """
            // ============================================================================
            // COMPREHENSIVE LAMBDA AND ARRAY EXAMPLES
            // ============================================================================

            // ----------------------------------------------------------------------------
            // 1. LAMBDA SYNTAX BASICS
            // ----------------------------------------------------------------------------

            var add = (a, b) => a + b;
            """;

        var service = new VirtualDocumentSegmentationService();
        var sections = service.Segment(source);

        Assert.Single(sections);
        Assert.Equal(source, sections[0].Content);
    }
}
