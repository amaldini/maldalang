using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Phase 2.2: ReferenceManual/22-grammar.html documents parser surface (anchor strings).
/// </summary>
public class ReferenceManualGrammarCoverageTests
{
    private static string GrammarHtml =>
        File.ReadAllText(PlanningPaths.ResolveRepoPath("ReferenceManual", "22-grammar.html"));

    public static IEnumerable<object[]> RequiredProductionAnchors =>
        new[]
        {
            "WorkflowDecl",
            "ActorDecl",
            "TypeDecl",
            "VariantPattern",
            "DictLiteral",
            "GraphLiteral",
            "async",
            "await",
            "TryStmt",
            "SendStmt",
            "spawn",
            "receive",
            "ForeachStmt",
            "DecoratedFunctionDecl",
            "awaitSignal",
            "ImportStmt",
            "ExportableDecl",
        }.Select(anchor => new object[] { anchor });

    [Theory]
    [MemberData(nameof(RequiredProductionAnchors))]
    public void GrammarChapter_DocumentsParserConstruct(string anchor)
    {
        var html = GrammarHtml;
        Assert.Contains(anchor, html, StringComparison.Ordinal);
    }

    [Fact]
    public void GrammarChapter_LinksLanguageSpec()
    {
        Assert.Contains("malda-language-1.0.md", GrammarHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void GrammarChapter_NoPartialGrammarWarning()
    {
        Assert.DoesNotContain("Partial grammar:", GrammarHtml, StringComparison.Ordinal);
    }
}
