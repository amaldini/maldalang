// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guards that keep ReferenceManual content aligned with the code it documents:
/// reserved words versus the lexer, built-in coverage versus the registry,
/// internal links, section numbering, navigation/TOC fallbacks, contiguous categories,
/// and the shipping version stamped in chapter headers.
/// </summary>
public class ReferenceManualContentGuardTests
{
    private static string ManualDir => PlanningPaths.ResolveRepoPath("ReferenceManual");

    private static IEnumerable<string> ManualPages =>
        Directory.EnumerateFiles(ManualDir, "*.html").OrderBy(p => p, StringComparer.Ordinal);

    /// <summary>
    /// Built-ins that exist in the registry but are deliberately absent from the manual.
    /// Add a name here only together with the reason it should stay undocumented.
    /// </summary>
    private static readonly HashSet<string> UndocumentedBuiltInAllowList = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void ReservedWordLists_CoverEveryLexerKeyword()
    {
        var keywords = LexerKeywords();
        Assert.NotEmpty(keywords);

        foreach (var page in new[] { "02-lexical-structure.html", "23-appendix.html" })
        {
            var listed = ReservedWordsListedIn(page);
            var missing = keywords.Except(listed, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var extra = listed.Except(keywords, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.True(
                missing.Count == 0,
                $"{page} omits lexer keywords: {string.Join(", ", missing)}");
            Assert.True(
                extra.Count == 0,
                $"{page} lists words that are not lexer keywords: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void EveryRegistryBuiltIn_IsMentionedSomewhereInTheManual()
    {
        var builtIns = RegistryBuiltInNames();
        Assert.True(builtIns.Count > 250, $"Expected the registry parse to find the full built-in set, got {builtIns.Count}.");

        var manualText = string.Concat(ManualPages.Select(File.ReadAllText));
        var mentioned = new HashSet<string>(
            Regex.Matches(manualText, @"[A-Za-z_][A-Za-z0-9_]*").Select(m => m.Value),
            StringComparer.Ordinal);

        // The Web UI built-ins are registered flat (uiButton) but documented under the
        // namespaced spelling users actually write (ui.button).
        var namespacedUi = new HashSet<string>(
            Regex.Matches(manualText, @"\bui\.(?<member>[A-Za-z][A-Za-z0-9_]*)").Select(m => m.Groups["member"].Value),
            StringComparer.Ordinal);

        var missing = builtIns
            .Where(name => !mentioned.Contains(name))
            .Where(name => !IsDocumentedAsUiMember(name, namespacedUi))
            .Where(name => !UndocumentedBuiltInAllowList.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Built-ins registered in BuiltInRegistry but never named in the manual: {string.Join(", ", missing)}");
    }

    [Fact]
    public void InternalLinks_ResolveToExistingPages()
    {
        var broken = new List<string>();

        foreach (var path in ManualPages)
        {
            var html = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(html, @"href=""(?<target>[^""#]+\.html)(#[^""]*)?"""))
            {
                var target = match.Groups["target"].Value;
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    continue;

                var resolved = Path.GetFullPath(Path.Combine(ManualDir, target));
                if (!File.Exists(resolved))
                    broken.Add($"{Path.GetFileName(path)} -> {target}");
            }
        }

        Assert.True(broken.Count == 0, $"Broken internal links: {string.Join("; ", broken)}");
    }

    [Fact]
    public void SectionNumbers_AreUniqueWithinEachChapter()
    {
        var duplicates = new List<string>();

        foreach (var path in ManualPages)
        {
            var html = File.ReadAllText(path);
            var numbers = Regex.Matches(html, @"<h[23][^>]*>\s*(?<number>\d+(?:\.\d+)+)\s")
                .Select(m => m.Groups["number"].Value)
                .ToList();

            foreach (var group in numbers.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1))
                duplicates.Add($"{Path.GetFileName(path)} reuses section {group.Key} ({group.Count()} times)");
        }

        Assert.True(duplicates.Count == 0, string.Join("; ", duplicates));
    }

    [Fact]
    public void WebUiChapter_NamesEveryRegisteredControlType()
    {
        var specSource = File.ReadAllText(
            PlanningPaths.ResolveRepoPath("MaldaLang", "Runtime", "UI", "UiControlSpecRegistry.cs"));
        var types = Regex.Matches(specSource, @"\[""(?<type>[A-Za-z][A-Za-z0-9]*)""\]")
            .Select(m => m.Groups["type"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
        Assert.True(types.Count > 20, $"Expected a full control catalog, got {types.Count}.");

        var chapter = File.ReadAllText(Path.Combine(ManualDir, "16-web-ui.html"));
        var missing = types.Where(type => !chapter.Contains($"ui.{type}", StringComparison.Ordinal)).ToList();
        Assert.True(
            missing.Count == 0,
            "16-web-ui.html must name every UiControlSpecRegistry type as ui.<type>: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void NavigationFallback_MatchesChaptersJson()
    {
        var expected = ChaptersJsonEntries();
        var actual = NavigationFallbackEntries();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            "FALLBACK_NAV_ITEMS in navigation.js is out of sync with chapters.json.\n" +
            $"chapters.json: {string.Join(" | ", expected)}\n" +
            $"navigation.js: {string.Join(" | ", actual)}");
    }

    [Fact]
    public void IndexTocFallback_MatchesChaptersJson()
    {
        var expected = ChaptersJsonEntries().Where(e => !e.StartsWith("index.html::", StringComparison.Ordinal)).ToList();
        var actual = IndexTocFallbackEntries();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            "FALLBACK_TOC_CHAPTERS in index-toc.js is out of sync with chapters.json.\n" +
            $"chapters.json: {string.Join(" | ", expected)}\n" +
            $"index-toc.js: {string.Join(" | ", actual)}");
    }

    [Fact]
    public void ChapterCategories_AreContiguousInReadingOrder()
    {
        var numbered = NumberedChapters().ToList();
        Assert.NotEmpty(numbered);

        foreach (var group in numbered.GroupBy(ch => ch.Category, StringComparer.Ordinal))
        {
            var nums = group.Select(ch => ch.Number).ToList();
            Assert.True(
                nums.SequenceEqual(Enumerable.Range(nums[0], nums.Count)),
                $"Category '{group.Key}' is not a contiguous number range in chapters.json: {string.Join(", ", nums)}");
        }
    }

    [Fact]
    public void NavAndTocCategoryOrder_ListsEveryChapterCategory()
    {
        var expected = NumberedChapters()
            .Select(ch => ch.Category)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var navOrder = JsStringArray(Path.Combine(ManualDir, "navigation.js"), "NAV_CATEGORY_ORDER");
        var tocOrder = JsStringArray(Path.Combine(ManualDir, "index-toc.js"), "TOC_CATEGORY_ORDER");

        Assert.Equal(expected, navOrder);
        Assert.Equal(expected, tocOrder);
    }

    [Fact]
    public void ChapterMastheads_MatchCliVersion()
    {
        var csproj = File.ReadAllText(PlanningPaths.ResolveRepoPath("MaldaLang", "MaldaLang.csproj"));
        var versionMatch = Regex.Match(csproj, @"<Version>\s*(?<ver>[^<]+?)\s*</Version>");
        Assert.True(versionMatch.Success, "MaldaLang.csproj must contain <Version>x.y.z</Version>.");
        var version = versionMatch.Groups["ver"].Value.Trim();
        Assert.False(string.IsNullOrWhiteSpace(version));

        var masthead = new Regex(
            @"The AI-First Programming Language - Version (?<ver>[\d.]+)",
            RegexOptions.CultureInvariant);
        var mismatched = new List<string>();
        var stamped = 0;
        foreach (var path in ManualPages)
        {
            var html = File.ReadAllText(path);
            foreach (Match match in masthead.Matches(html))
            {
                stamped++;
                if (!string.Equals(match.Groups["ver"].Value, version, StringComparison.Ordinal))
                    mismatched.Add($"{Path.GetFileName(path)} stamps {match.Groups["ver"].Value}");
            }
        }

        Assert.True(stamped >= 30, $"Expected chapter mastheads on most manual pages, found {stamped}.");
        Assert.True(
            mismatched.Count == 0,
            "Chapter headers must stamp the CLI <Version> (" + version + "): " + string.Join(", ", mismatched));

        var index = File.ReadAllText(Path.Combine(ManualDir, "index.html"));
        Assert.Contains($"MALDA <strong>{version}</strong>", index, StringComparison.Ordinal);

        var tools = File.ReadAllText(Path.Combine(ManualDir, "25-tools.html"));
        Assert.Matches(
            new Regex(@"Interpreter\r?\nVersion " + Regex.Escape(version) + @"\r?\n", RegexOptions.CultureInvariant),
            tools);

        var script = File.ReadAllText(PlanningPaths.ResolveRepoPath("scripts", "build-reference-manual-book.ps1"));
        Assert.Contains("MaldaLang.csproj", script, StringComparison.Ordinal);
        Assert.Contains("$manualVersion", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Version 0.1 &middot;", script, StringComparison.Ordinal);
    }

    private static bool IsDocumentedAsUiMember(string registryName, HashSet<string> namespacedUiMembers)
    {
        if (!registryName.StartsWith("ui", StringComparison.Ordinal) || registryName.Length <= 2)
            return false;

        var suffix = registryName[2..];
        var member = char.ToLowerInvariant(suffix[0]) + suffix[1..];
        return namespacedUiMembers.Contains(member);
    }

    private static HashSet<string> LexerKeywords()
    {
        var source = File.ReadAllText(PlanningPaths.ResolveRepoPath("MaldaLang", "Lexer.cs"));
        var start = source.IndexOf("Keywords = new", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate the Keywords map in Lexer.cs.");

        var end = source.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not locate the end of the Keywords map in Lexer.cs.");

        var body = source[start..end];
        return new HashSet<string>(
            Regex.Matches(body, @"\{\s*""(?<word>[A-Za-z_][A-Za-z0-9_]*)""\s*,").Select(m => m.Groups["word"].Value),
            StringComparer.Ordinal);
    }

    private static HashSet<string> ReservedWordsListedIn(string page)
    {
        var html = File.ReadAllText(Path.Combine(ManualDir, page));
        var marker = page == "23-appendix.html" ? "Appendix A: Reserved Words" : "Complete Keyword List";
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Could not find the reserved words section in {page}.");

        var blockStart = html.IndexOf("<pre><code>", markerIndex, StringComparison.Ordinal);
        var blockEnd = html.IndexOf("</code></pre>", blockStart, StringComparison.Ordinal);
        Assert.True(blockStart >= 0 && blockEnd > blockStart, $"Could not find the reserved words block in {page}.");

        var block = html[blockStart..blockEnd];
        var plain = Regex.Replace(block, "<[^>]+>", string.Empty);

        return new HashSet<string>(
            plain.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => Regex.IsMatch(part, @"^[A-Za-z_][A-Za-z0-9_]*$")),
            StringComparer.Ordinal);
    }

    private static HashSet<string> RegistryBuiltInNames()
    {
        var source = File.ReadAllText(PlanningPaths.ResolveRepoPath("MaldaLang", "BuiltIns", "BuiltInRegistry.cs"));
        var start = source.IndexOf("public static BuiltInDescriptor? GetDescriptor", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate GetDescriptor in BuiltInRegistry.cs.");

        var end = source.IndexOf("public static WorkflowBuiltInBehavior", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not locate the end of GetDescriptor in BuiltInRegistry.cs.");

        var body = source[start..end];
        return new HashSet<string>(
            Regex.Matches(body, @"""(?<name>[A-Za-z_][A-Za-z0-9_]*)""").Select(m => m.Groups["name"].Value),
            StringComparer.Ordinal);
    }

    private static List<string> ChaptersJsonEntries()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ManualDir, "chapters.json")));
        var entries = new List<string>();
        var number = 0;

        foreach (var chapter in doc.RootElement.GetProperty("chapters").EnumerateArray())
        {
            var file = chapter.GetProperty("file").GetString()!;
            var title = chapter.GetProperty("title").GetString()!;
            var isHome = chapter.TryGetProperty("isHome", out var home) && home.GetBoolean();

            if (isHome)
            {
                entries.Add($"{file}::{title}");
                continue;
            }

            number++;
            entries.Add($"{file}::{number}. {title}");
        }

        return entries;
    }

    private static List<string> NavigationFallbackEntries()
    {
        var source = File.ReadAllText(Path.Combine(ManualDir, "navigation.js"));
        var start = source.IndexOf("FALLBACK_NAV_ITEMS = [", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate FALLBACK_NAV_ITEMS in navigation.js.");

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not locate the end of FALLBACK_NAV_ITEMS in navigation.js.");

        var body = source[start..end];
        return Regex.Matches(body, @"href:\s*""(?<file>[^""]+)""\s*,\s*text:\s*""(?<text>[^""]+)""")
            .Select(m => $"{m.Groups["file"].Value}::{m.Groups["text"].Value}")
            .ToList();
    }

    private static List<string> IndexTocFallbackEntries()
    {
        var source = File.ReadAllText(Path.Combine(ManualDir, "index-toc.js"));
        var start = source.IndexOf("FALLBACK_TOC_CHAPTERS = [", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate FALLBACK_TOC_CHAPTERS in index-toc.js.");

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not locate the end of FALLBACK_TOC_CHAPTERS in index-toc.js.");

        var body = source[start..end];
        return Regex.Matches(
                body,
                @"file:\s*""(?<file>[^""]+)""\s*,\s*title:\s*""(?<title>[^""]+)""\s*,\s*num:\s*""(?<num>[^""]+)""")
            .Select(m => $"{m.Groups["file"].Value}::{m.Groups["num"].Value}. {m.Groups["title"].Value}")
            .ToList();
    }

    private static IEnumerable<(int Number, string File, string Category)> NumberedChapters()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ManualDir, "chapters.json")));
        var number = 0;
        foreach (var chapter in doc.RootElement.GetProperty("chapters").EnumerateArray())
        {
            if (chapter.TryGetProperty("isHome", out var home) && home.GetBoolean())
                continue;

            number++;
            var file = chapter.GetProperty("file").GetString()!;
            var category = chapter.TryGetProperty("category", out var cat) ? cat.GetString() ?? "Reference" : "Reference";
            yield return (number, file, category);
        }
    }

    private static List<string> JsStringArray(string path, string constName)
    {
        var source = File.ReadAllText(path);
        var start = source.IndexOf($"{constName} = [", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not locate {constName} in {Path.GetFileName(path)}.");

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not locate the end of {constName} in {Path.GetFileName(path)}.");

        return Regex.Matches(source[start..end], @"'(?<value>[^']+)'")
            .Select(m => m.Groups["value"].Value)
            .ToList();
    }
}
