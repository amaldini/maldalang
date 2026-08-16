// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guards the Italian Reference Manual tree in <c>ReferenceManual/it/</c>.
/// English remains canonical; these tests keep the translation aligned on
/// file set, code samples, asset paths, and recorded English snapshots.
/// </summary>
public class ReferenceManualItalianTests
{
    private static string ManualDir => PlanningPaths.ResolveRepoPath("ReferenceManual");

    private static string ItalianDir => Path.Combine(ManualDir, "it");

    [Fact]
    public void ItalianTree_MirrorsEveryEnglishChapterFile()
    {
        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(ManualDir, "chapters.json")));
        using var italian = JsonDocument.Parse(File.ReadAllText(Path.Combine(ItalianDir, "chapters.json")));

        var enFiles = english.RootElement.GetProperty("chapters")
            .EnumerateArray()
            .Select(ch => ch.GetProperty("file").GetString()!)
            .ToList();
        var itFiles = italian.RootElement.GetProperty("chapters")
            .EnumerateArray()
            .Select(ch => ch.GetProperty("file").GetString()!)
            .ToList();

        Assert.Equal(enFiles, itFiles);

        foreach (var file in enFiles)
        {
            Assert.True(File.Exists(Path.Combine(ItalianDir, file)), $"Missing Italian page it/{file}");
        }
    }

    [Fact]
    public void ItalianChaptersJson_KeepsEnglishCategoryKeys()
    {
        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(ManualDir, "chapters.json")));
        using var italian = JsonDocument.Parse(File.ReadAllText(Path.Combine(ItalianDir, "chapters.json")));

        var enCats = english.RootElement.GetProperty("chapters")
            .EnumerateArray()
            .Where(ch => !(ch.TryGetProperty("isHome", out var home) && home.GetBoolean()))
            .Select(ch => ch.GetProperty("category").GetString()!)
            .ToList();
        var itCats = italian.RootElement.GetProperty("chapters")
            .EnumerateArray()
            .Where(ch => !(ch.TryGetProperty("isHome", out var home) && home.GetBoolean()))
            .Select(ch => ch.GetProperty("category").GetString()!)
            .ToList();

        Assert.Equal(enCats, itCats);
    }

    [Fact]
    public void ItalianPages_UseItalianLangAndSharedAssets()
    {
        var broken = new List<string>();

        foreach (var path in Directory.EnumerateFiles(ItalianDir, "*.html", SearchOption.TopDirectoryOnly))
        {
            var html = File.ReadAllText(path);
            var name = Path.GetFileName(path);

            if (!html.Contains("<html lang=\"it\">", StringComparison.Ordinal))
                broken.Add($"{name}: missing html lang=it");
            if (!html.Contains("href=\"../styles.css\"", StringComparison.Ordinal))
                broken.Add($"{name}: missing ../styles.css");
            if (!html.Contains("href=\"../syntax.css\"", StringComparison.Ordinal))
                broken.Add($"{name}: missing ../syntax.css");
            if (!html.Contains("href=\"../print.css\"", StringComparison.Ordinal))
                broken.Add($"{name}: missing ../print.css");
            if (!html.Contains("src=\"../malda-highlight.js\"", StringComparison.Ordinal))
                broken.Add($"{name}: missing ../malda-highlight.js");
            if (!html.Contains("src=\"../navigation.js\"", StringComparison.Ordinal))
                broken.Add($"{name}: missing ../navigation.js");
            if (html.Contains("href=\"../docs/", StringComparison.Ordinal))
                broken.Add($"{name}: docs link is still one level up; use ../../docs/");
            if (Regex.IsMatch(html, @"href=""(styles|syntax|print)\.css"""))
                broken.Add($"{name}: asset href is not prefixed with ../");
        }

        Assert.True(broken.Count == 0, string.Join("; ", broken));
    }

    [Fact]
    public void ItalianPages_CodeBlocksMatchEnglish()
    {
        var mismatches = new List<string>();

        foreach (var enPath in Directory.EnumerateFiles(ManualDir, "*.html", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(enPath);
            var itPath = Path.Combine(ItalianDir, name);
            if (!File.Exists(itPath))
                continue;

            var enBlocks = CodeBlocks(File.ReadAllText(enPath));
            var itBlocks = CodeBlocks(File.ReadAllText(itPath));

            if (enBlocks.Count != itBlocks.Count)
            {
                mismatches.Add($"{name}: English has {enBlocks.Count} code blocks, Italian has {itBlocks.Count}");
                continue;
            }

            for (var i = 0; i < enBlocks.Count; i++)
            {
                if (!string.Equals(enBlocks[i].Attrs, itBlocks[i].Attrs, StringComparison.Ordinal))
                    mismatches.Add($"{name} block {i + 1}: code tag attributes differ");
                if (!string.Equals(enBlocks[i].Body, itBlocks[i].Body, StringComparison.Ordinal))
                    mismatches.Add($"{name} block {i + 1}: code body differs from English (do not translate listings)");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void ItalianPages_InternalHtmlLinksResolve()
    {
        var broken = new List<string>();

        foreach (var path in Directory.EnumerateFiles(ItalianDir, "*.html", SearchOption.TopDirectoryOnly))
        {
            var html = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(html, @"href=""(?<target>[^""#]+\.html)(#[^""]*)?"""))
            {
                var target = match.Groups["target"].Value;
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    continue;

                var resolved = Path.GetFullPath(Path.Combine(ItalianDir, target));
                if (!File.Exists(resolved))
                    broken.Add($"{Path.GetFileName(path)} -> {target}");
            }
        }

        Assert.True(broken.Count == 0, $"Broken Italian internal links: {string.Join("; ", broken)}");
    }

    [Fact]
    public void ItalianMastheads_MatchCliVersion()
    {
        var csproj = File.ReadAllText(PlanningPaths.ResolveRepoPath("MaldaLang", "MaldaLang.csproj"));
        var versionMatch = Regex.Match(csproj, @"<Version>\s*(?<ver>[^<]+?)\s*</Version>");
        Assert.True(versionMatch.Success, "MaldaLang.csproj must contain <Version>x.y.z</Version>.");
        var version = versionMatch.Groups["ver"].Value.Trim();

        var masthead = new Regex(
            @"Il linguaggio di programmazione AI-First - Versione (?<ver>[\d.]+)",
            RegexOptions.CultureInvariant);
        var mismatched = new List<string>();
        var stamped = 0;

        foreach (var path in Directory.EnumerateFiles(ItalianDir, "*.html", SearchOption.TopDirectoryOnly))
        {
            var html = File.ReadAllText(path);
            foreach (Match match in masthead.Matches(html))
            {
                stamped++;
                if (!string.Equals(match.Groups["ver"].Value, version, StringComparison.Ordinal))
                    mismatched.Add($"{Path.GetFileName(path)} stamps {match.Groups["ver"].Value}");
            }
        }

        Assert.True(stamped >= 30, $"Expected Italian mastheads on most pages, found {stamped}.");
        Assert.True(
            mismatched.Count == 0,
            "Italian chapter headers must stamp the CLI <Version> (" + version + "): " +
            string.Join(", ", mismatched));

        var index = File.ReadAllText(Path.Combine(ItalianDir, "index.html"));
        Assert.Contains($"MALDA <strong>{version}</strong>", index, StringComparison.Ordinal);
    }

    [Fact]
    public void ItalianNavigationFallback_MatchesChaptersJson()
    {
        var expected = ItalianChaptersJsonEntries();
        var actual = NavigationFallbackEntries("FALLBACK_NAV_ITEMS_IT = [");

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            "FALLBACK_NAV_ITEMS_IT in navigation.js is out of sync with it/chapters.json.\n" +
            $"chapters.json: {string.Join(" | ", expected)}\n" +
            $"navigation.js: {string.Join(" | ", actual)}");
    }

    [Fact]
    public void ItalianIndexTocFallback_MatchesChaptersJson()
    {
        var expected = ItalianChaptersJsonEntries()
            .Where(e => !e.StartsWith("index.html::", StringComparison.Ordinal))
            .ToList();
        var actual = IndexTocFallbackEntries();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            "FALLBACK_TOC_CHAPTERS_IT in index-toc.js is out of sync with it/chapters.json.\n" +
            $"chapters.json: {string.Join(" | ", expected)}\n" +
            $"index-toc.js: {string.Join(" | ", actual)}");
    }

    [Fact]
    public void ItalianStatus_MatchesEnglishFileHashes()
    {
        var statusPath = Path.Combine(ItalianDir, "STATUS.md");
        Assert.True(File.Exists(statusPath), $"Missing {statusPath}");

        var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     File.ReadAllText(statusPath),
                     @"^\|\s*(?<file>[A-Za-z0-9._-]+\.html)\s*\|\s*(?<hash>[0-9a-f]{64})\s*\|",
                     RegexOptions.Multiline))
        {
            recorded[match.Groups["file"].Value] = match.Groups["hash"].Value;
        }

        using var italian = JsonDocument.Parse(File.ReadAllText(Path.Combine(ItalianDir, "chapters.json")));
        var missing = new List<string>();
        var stale = new List<string>();

        foreach (var chapter in italian.RootElement.GetProperty("chapters").EnumerateArray())
        {
            var file = chapter.GetProperty("file").GetString()!;
            var enPath = Path.Combine(ManualDir, file);
            Assert.True(File.Exists(enPath), $"English source missing for {file}");
            var hash = Sha256Hex(enPath);

            if (!recorded.TryGetValue(file, out var recordedHash))
            {
                missing.Add(file);
                continue;
            }

            if (!string.Equals(recordedHash, hash, StringComparison.OrdinalIgnoreCase))
                stale.Add($"{file} (STATUS {recordedHash}, English {hash})");
        }

        Assert.True(missing.Count == 0, "STATUS.md is missing rows for: " + string.Join(", ", missing));
        Assert.True(
            stale.Count == 0,
            "Italian STATUS.md is stale versus English sources. Re-translate the chapter and run " +
            "scripts/sync-reference-manual-it-status.py. Drift: " + string.Join("; ", stale));
    }

    private static List<(string Attrs, string Body)> CodeBlocks(string html)
    {
        return Regex.Matches(
                html,
                @"<pre><code(?<attrs>[^>]*)>(?<body>.*?)</code></pre>",
                RegexOptions.Singleline)
            .Select(m => (
                m.Groups["attrs"].Value,
                WebUtility.HtmlDecode(m.Groups["body"].Value).Replace("\r\n", "\n")))
            .ToList();
    }

    private static List<string> ItalianChaptersJsonEntries()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ItalianDir, "chapters.json")));
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

    private static List<string> NavigationFallbackEntries(string marker)
    {
        var source = File.ReadAllText(Path.Combine(ManualDir, "navigation.js"));
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not locate {marker} in navigation.js.");

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not locate the end of {marker} in navigation.js.");

        var body = source[start..end];
        return Regex.Matches(body, @"href:\s*""(?<file>[^""]+)""\s*,\s*text:\s*""(?<text>[^""]+)""")
            .Select(m => $"{m.Groups["file"].Value}::{m.Groups["text"].Value}")
            .ToList();
    }

    private static List<string> IndexTocFallbackEntries()
    {
        var source = File.ReadAllText(Path.Combine(ManualDir, "index-toc.js"));
        var start = source.IndexOf("FALLBACK_TOC_CHAPTERS_IT = [", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate FALLBACK_TOC_CHAPTERS_IT in index-toc.js.");

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not locate the end of FALLBACK_TOC_CHAPTERS_IT in index-toc.js.");

        var body = source[start..end];
        return Regex.Matches(
                body,
                @"file:\s*""(?<file>[^""]+)""\s*,\s*title:\s*""(?<title>[^""]+)""\s*,\s*num:\s*""(?<num>[^""]+)""")
            .Select(m => $"{m.Groups["file"].Value}::{m.Groups["num"].Value}. {m.Groups["title"].Value}")
            .ToList();
    }

    private static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            builder.Append(b.ToString("x2"));
        return builder.ToString();
    }
}
