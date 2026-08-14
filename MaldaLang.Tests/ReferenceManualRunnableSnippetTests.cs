// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Net;
using System.Text.RegularExpressions;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Executes the ReferenceManual code blocks that are marked as runnable, so documented
/// examples cannot silently stop working.
///
/// Mark a block by adding <c>data-run="true"</c> to its &lt;code&gt; element. Add
/// <c>data-expect="..."</c> to also assert the printed output, using <c>\n</c> to separate
/// lines. Blocks without the attribute are ignored, so pseudo-code and fragments are fine.
/// </summary>
[Collection("Sequential")]
public class ReferenceManualRunnableSnippetTests : TestBase
{
    private const int MinimumExpectedSnippets = 20;

    public static IEnumerable<object[]> RunnableSnippets =>
        DiscoverSnippets().Select(s => new object[] { s.Id });

    [Fact]
    public void Manual_MarksEnoughRunnableSnippets()
    {
        var count = DiscoverSnippets().Count;
        Assert.True(
            count >= MinimumExpectedSnippets,
            $"Only {count} runnable snippets are marked in the manual; expected at least {MinimumExpectedSnippets}. " +
            "Did a chapter lose its data-run attributes?");
    }

    [Fact]
    public void RunnableSnippets_HaveUniqueIdentifiers()
    {
        var ids = DiscoverSnippets().Select(s => s.Id).ToList();
        var duplicates = ids.GroupBy(id => id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate snippet ids: {string.Join(", ", duplicates)}");
    }

    [Theory]
    [MemberData(nameof(RunnableSnippets))]
    public async Task RunnableSnippet_ExecutesAndMatchesExpectedOutput(string id)
    {
        var snippet = DiscoverSnippets().Single(s => s.Id == id);

        string output;
        try
        {
            output = await RunProgramAsync(snippet.Source);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Snippet {id} failed to run: {ex.Message}\n---\n{snippet.Source}\n---");
            throw;
        }

        if (snippet.Expected == null)
            return;

        Assert.Equal(Normalize(snippet.Expected), Normalize(output));
    }

    private static string Normalize(string text) =>
        string.Join("\n", text.Replace("\r", string.Empty)
            .Split('\n')
            .Select(line => line.TrimEnd())
            .SkipWhile(string.IsNullOrEmpty)
            .Reverse()
            .SkipWhile(string.IsNullOrEmpty)
            .Reverse());

    private static List<Snippet> DiscoverSnippets()
    {
        var manualDir = PlanningPaths.ResolveRepoPath("ReferenceManual");
        var snippets = new List<Snippet>();

        foreach (var path in Directory.EnumerateFiles(manualDir, "*.html").OrderBy(p => p, StringComparer.Ordinal))
        {
            var html = File.ReadAllText(path);
            var page = Path.GetFileNameWithoutExtension(path);
            var index = 0;

            foreach (Match match in Regex.Matches(
                         html,
                         @"<code(?<attrs>[^>]*\bdata-run=""true""[^>]*)>(?<body>.*?)</code>",
                         RegexOptions.Singleline))
            {
                index++;
                var attrs = match.Groups["attrs"].Value;
                var expectAttr = Regex.Match(attrs, @"data-expect=""(?<value>[^""]*)""");

                snippets.Add(new Snippet(
                    Id: $"{page}#{index}",
                    Source: WebUtility.HtmlDecode(match.Groups["body"].Value),
                    Expected: expectAttr.Success
                        ? WebUtility.HtmlDecode(expectAttr.Groups["value"].Value).Replace("\\n", "\n")
                        : null));
            }
        }

        return snippets;
    }

    private sealed record Snippet(string Id, string Source, string? Expected);
}
