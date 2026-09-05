// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guards the Reference Manual search index and glossary: aliases such as
/// <c>cap</c> must resolve, English/Italian term ids stay aligned, and the
/// sidebar fallback matches <c>glossary.json</c>.
/// </summary>
public class ReferenceManualSearchTests
{
    private static readonly string[] SkipHeadingTitles =
    [
        "see also",
        "example",
        "examples",
        "constructor",
        "methods",
        "syntax",
        "behavior",
        "complete example",
        "use cases",
    ];

    private static string ManualDir => PlanningPaths.ResolveRepoPath("ReferenceManual");

    private static string ItalianDir => Path.Combine(ManualDir, "it");

    [Fact]
    public void GlossaryJson_IncludesCapabilityTokenAliases()
    {
        var cap = LoadGlossary(Path.Combine(ManualDir, "glossary.json"))
            .Single(term => term.Id == "capability-tokens");

        Assert.Equal("13-built-in-functions.html#capability-tokens", cap.Href);
        Assert.Contains("12-input-output.html#capability-tokens", cap.Also);
        foreach (var alias in new[] { "cap", "cap.fileRead", "cap.fileWrite", "cap.confine", "capability" })
            Assert.Contains(alias, cap.Aliases);
    }

    [Fact]
    public void GlossaryJson_HrefTargetsExist()
    {
        var broken = new List<string>();
        foreach (var locale in new[] { ManualDir, ItalianDir })
        {
            foreach (var term in LoadGlossary(Path.Combine(locale, "glossary.json")))
            {
                foreach (var href in term.AllHrefs)
                    broken.AddRange(BrokenHref(locale, term.Id, href));
            }
        }

        Assert.True(broken.Count == 0, string.Join("; ", broken));
    }

    [Fact]
    public void ItalianGlossary_SharesEnglishTermIdsAndHrefs()
    {
        var english = LoadGlossary(Path.Combine(ManualDir, "glossary.json"));
        var italian = LoadGlossary(Path.Combine(ItalianDir, "glossary.json"));

        Assert.Equal(
            english.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal),
            italian.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal));

        var itById = italian.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var drift = english
            .Where(term => term.Href != itById[term.Id].Href
                           || !term.Also.SequenceEqual(itById[term.Id].Also, StringComparer.Ordinal))
            .Select(term => term.Id)
            .ToList();

        Assert.True(drift.Count == 0, "Italian glossary hrefs drifted for: " + string.Join(", ", drift));
    }

    [Fact]
    public void NavigationFallbackGlossary_MatchesGlossaryJson()
    {
        var source = File.ReadAllText(Path.Combine(ManualDir, "navigation.js"));
        AssertEqualGlossary(LoadGlossary(Path.Combine(ManualDir, "glossary.json")), ParseFallback(source, "FALLBACK_GLOSSARY_EN"));
        AssertEqualGlossary(LoadGlossary(Path.Combine(ItalianDir, "glossary.json")), ParseFallback(source, "FALLBACK_GLOSSARY_IT"));
    }

    [Fact]
    public void HeadingsJson_MatchesNumberedChapterHeadings()
    {
        AssertEqualHeadings(
            Path.Combine(ManualDir, "headings.json"),
            ExtractHeadings(ManualDir));
        AssertEqualHeadings(
            Path.Combine(ItalianDir, "headings.json"),
            ExtractHeadings(ItalianDir));
    }

    [Fact]
    public void GlossaryPages_ExistInBothLocales()
    {
        Assert.True(File.Exists(Path.Combine(ManualDir, "glossary.html")));
        Assert.True(File.Exists(Path.Combine(ItalianDir, "glossary.html")));

        var en = File.ReadAllText(Path.Combine(ManualDir, "glossary.html"));
        var it = File.ReadAllText(Path.Combine(ItalianDir, "glossary.html"));
        Assert.Contains("id=\"glossary-list\"", en, StringComparison.Ordinal);
        Assert.Contains("id=\"glossary-list\"", it, StringComparison.Ordinal);
        Assert.Contains("13-built-in-functions.html#capability-tokens", en, StringComparison.Ordinal);
        Assert.Contains("13-built-in-functions.html#capability-tokens", it, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationJs_WiresSidebarSearch()
    {
        var source = File.ReadAllText(Path.Combine(ManualDir, "navigation.js"));
        Assert.Contains("initManualSearch", source, StringComparison.Ordinal);
        Assert.Contains("initGlossaryPage", source, StringComparison.Ordinal);
        Assert.Contains("manual-search-input", source, StringComparison.Ordinal);
        Assert.Contains("glossary.html", source, StringComparison.Ordinal);
        Assert.Contains("headings.json", source, StringComparison.Ordinal);
    }

    private static void AssertEqualGlossary(IReadOnlyList<GlossaryTerm> expected, IReadOnlyList<GlossaryTerm> actual)
    {
        Assert.Equal(expected.Select(t => t.Id), actual.Select(t => t.Id));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Href, actual[i].Href);
            Assert.Equal(expected[i].Aliases, actual[i].Aliases);
        }
    }

    private static void AssertEqualHeadings(string path, IReadOnlyList<Heading> expected)
    {
        Assert.True(File.Exists(path), $"Missing {path}. Run python3 scripts/sync-reference-manual-search-index.py");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var actual = doc.RootElement.EnumerateArray()
            .Select(el => new Heading(
                el.GetProperty("file").GetString()!,
                el.GetProperty("level").GetInt32(),
                el.GetProperty("id").GetString() ?? "",
                el.GetProperty("title").GetString()!))
            .ToList();

        Assert.Equal(expected, actual);
    }

    private static List<string> BrokenHref(string localeDir, string termId, string href)
    {
        var hash = href.IndexOf('#');
        var file = hash >= 0 ? href[..hash] : href;
        var fragment = hash >= 0 ? href[(hash + 1)..] : "";
        var path = Path.GetFullPath(Path.Combine(localeDir, file));
        if (!File.Exists(path))
            return [$"{termId} -> {href} (missing {file})"];

        if (string.IsNullOrEmpty(fragment))
            return [];

        var html = File.ReadAllText(path);
        return html.Contains($"id=\"{fragment}\"", StringComparison.Ordinal)
            ? []
            : [$"{termId} -> {href} (missing id {fragment} in {file})"];
    }

    private static List<GlossaryTerm> LoadGlossary(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("terms").EnumerateArray().Select(ParseTerm).ToList();
    }

    private static List<GlossaryTerm> ParseFallback(string source, string constName)
    {
        var start = source.IndexOf($"const {constName} = ", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not locate {constName} in navigation.js.");
        var arrayStart = source.IndexOf('[', start);
        var end = source.IndexOf("\n];", arrayStart, StringComparison.Ordinal);
        Assert.True(end > arrayStart, $"Could not locate the end of {constName}.");
        var json = source[arrayStart..(end + 2)];
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(ParseTerm).ToList();
    }

    private static GlossaryTerm ParseTerm(JsonElement el)
    {
        var also = el.TryGetProperty("also", out var alsoEl) && alsoEl.ValueKind == JsonValueKind.Array
            ? alsoEl.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [];
        var aliases = el.TryGetProperty("aliases", out var aliasEl) && aliasEl.ValueKind == JsonValueKind.Array
            ? aliasEl.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [];
        return new GlossaryTerm(
            el.GetProperty("id").GetString()!,
            el.GetProperty("href").GetString()!,
            aliases,
            also);
    }

    private static List<Heading> ExtractHeadings(string folder)
    {
        var headingRe = new Regex(@"<h([23])([^>]*)>(.*?)</h\1>", RegexOptions.Singleline);
        var idRe = new Regex(@"id=""([^""]+)""");
        var headings = new List<Heading>();

        foreach (var path in Directory.EnumerateFiles(folder, "*.html", SearchOption.TopDirectoryOnly)
                     .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^\d{2}-", RegexOptions.CultureInvariant))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var html = File.ReadAllText(path);
            foreach (Match match in headingRe.Matches(html))
            {
                var level = int.Parse(match.Groups[1].Value);
                var title = Regex.Replace(match.Groups[3].Value, "<[^>]+>", string.Empty);
                title = System.Net.WebUtility.HtmlDecode(Regex.Replace(title, @"\s+", " ").Trim());
                if (string.IsNullOrEmpty(title))
                    continue;

                var idMatch = idRe.Match(match.Groups[2].Value);
                var id = idMatch.Success ? idMatch.Groups[1].Value : "";
                var lowered = title.ToLowerInvariant();
                var numbered = Regex.IsMatch(title, @"^\d+\.");
                if (SkipHeadingTitles.Contains(lowered) && id.Length == 0 && !numbered)
                    continue;
                if (level == 3 && id.Length == 0 && !numbered)
                    continue;

                headings.Add(new Heading(Path.GetFileName(path), level, id, title));
            }
        }

        return headings;
    }

    private sealed record GlossaryTerm(string Id, string Href, IReadOnlyList<string> Aliases, IReadOnlyList<string> Also)
    {
        public IEnumerable<string> AllHrefs => Also.Prepend(Href);
    }

    private sealed record Heading(string File, int Level, string Id, string Title);
}
