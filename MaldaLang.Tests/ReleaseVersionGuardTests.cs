// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Keeps the shippable version label aligned: CLI/Desktop csproj &lt;Version&gt;,
/// the latest <c>docs/releases/v*.md</c>, and (on tag pushes) the Git tag used by
/// <c>scripts/build-oss-dist.ps1</c> for zip names.
/// </summary>
public class ReleaseVersionGuardTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static readonly Regex CsprojVersion = new(
        @"<Version>\s*([^<]+?)\s*</Version>",
        RegexOptions.CultureInvariant);

    private static readonly Regex ReleaseDocName = new(
        @"^v(?<ver>\d+\.\d+\.\d+)\.md$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string ReadCsprojVersion(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath);
        Assert.True(File.Exists(path), "missing " + relativePath);
        var text = File.ReadAllText(path);
        var match = CsprojVersion.Match(text);
        Assert.True(match.Success, relativePath + " must contain <Version>x.y.z</Version>");
        return match.Groups[1].Value.Trim();
    }

    private static string LatestReleaseDocVersion()
    {
        var dir = Path.Combine(RepoRoot, "docs", "releases");
        Assert.True(Directory.Exists(dir), "missing docs/releases");

        var versions = Directory.EnumerateFiles(dir, "v*.md")
            .Select(Path.GetFileName)
            .Select(name => ReleaseDocName.Match(name ?? ""))
            .Where(m => m.Success)
            .Select(m => Version.Parse(m.Groups["ver"].Value))
            .ToList();

        Assert.NotEmpty(versions);
        return versions.Max()!.ToString();
    }

    [Fact]
    public void CliAndDesktopIde_ShareTheSameVersion()
    {
        var cli = ReadCsprojVersion(Path.Combine("MaldaLang", "MaldaLang.csproj"));
        var desktop = ReadCsprojVersion(Path.Combine("MaldaLang.DesktopIDE", "MaldaLang.DesktopIDE.csproj"));
        Assert.Equal(cli, desktop);
    }

    [Fact]
    public void CsprojVersion_MatchesLatestReleaseNotes()
    {
        var cli = ReadCsprojVersion(Path.Combine("MaldaLang", "MaldaLang.csproj"));
        var latestNotes = LatestReleaseDocVersion();
        Assert.True(
            string.Equals(cli, latestNotes, StringComparison.Ordinal),
            $"MaldaLang.csproj <Version> is '{cli}' but latest docs/releases is v{latestNotes}.md. " +
            "Bump <Version> in MaldaLang.csproj and MaldaLang.DesktopIDE.csproj when cutting a release " +
            "(build-oss-dist.ps1 reads the CLI csproj for zip names).");
    }
}
