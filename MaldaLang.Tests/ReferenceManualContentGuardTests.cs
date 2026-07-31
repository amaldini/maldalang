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
/// internal links, section numbering, and the navigation fallback list.
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
}
