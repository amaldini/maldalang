// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class InstallationUpdateServiceTests
{
    [Theory]
    [InlineData("1.0.13", "v1.0.13")]
    [InlineData("v1.0.13", "v1.0.13")]
    [InlineData("V1.0.13", "v1.0.13")]
    [InlineData("", "")]
    public void NormalizeTag_AddsVPrefix(string input, string expected)
    {
        Assert.Equal(expected, InstallationUpdateService.NormalizeTag(input));
    }

    [Fact]
    public void CompareTags_OrdersSemanticVersions()
    {
        Assert.True(InstallationUpdateService.CompareTags("v1.0.12", "v1.0.13") < 0);
        Assert.Equal(0, InstallationUpdateService.CompareTags("1.0.13", "v1.0.13"));
        Assert.True(InstallationUpdateService.CompareTags("v1.0.14", "1.0.13") > 0);
    }

    [Fact]
    public void ParseReleaseJson_FindsWinX64Asset()
    {
        const string json = """
            {
              "tag_name": "v1.0.13",
              "html_url": "https://github.com/amaldini/maldalang/releases/tag/v1.0.13",
              "assets": [
                {
                  "name": "malda-1.0.13-linux-x64.zip",
                  "browser_download_url": "https://example.test/linux.zip",
                  "size": 10
                },
                {
                  "name": "malda-1.0.13-win-x64.zip",
                  "browser_download_url": "https://example.test/win.zip",
                  "size": 2048
                }
              ]
            }
            """;

        var release = InstallationUpdateService.ParseReleaseJson(json);
        var asset = InstallationUpdateService.FindWinX64Asset(release);

        Assert.Equal("v1.0.13", release.TagName);
        Assert.NotNull(asset);
        Assert.Equal("malda-1.0.13-win-x64.zip", asset.Name);
        Assert.Equal("https://example.test/win.zip", asset.BrowserDownloadUrl);
        Assert.Equal(2048, asset.Size);
    }

    [Fact]
    public void Locate_PrefersSourceTreeOverDistLayout()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "MaldaLang.sln"), "Microsoft Visual Studio Solution File");
            WriteDistMarkers(root);

            var location = InstallationUpdateService.Locate(Path.Combine(root, "bin", "desktop-ide"));

            Assert.Equal(InstallationKind.SourceTree, location.Kind);
            Assert.Equal(root, location.RootPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Locate_FindsDistributionRoot()
    {
        var root = CreateTempDir();
        try
        {
            WriteDistMarkers(root);

            var location = InstallationUpdateService.Locate(Path.Combine(root, "bin", "desktop-ide"));

            Assert.Equal(InstallationKind.Distribution, location.Kind);
            Assert.Equal(root, location.RootPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Evaluate_SourceTree_CannotUpdate()
    {
        var latest = InstallationUpdateService.ParseReleaseJson("""
            {"tag_name":"v1.0.14","html_url":"https://example.test","assets":[{"name":"malda-1.0.14-win-x64.zip","browser_download_url":"https://example.test/w.zip","size":1}]}
            """);

        var result = InstallationUpdateService.Evaluate(
            new InstallationLocation(InstallationKind.SourceTree, @"C:\src"),
            "v1.0.13",
            latest);

        Assert.Equal(UpdateAvailability.CannotUpdateHere, result.Availability);
        Assert.Contains("source checkout", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_DetectsUpdateAvailableAndUpToDate()
    {
        var latest = InstallationUpdateService.ParseReleaseJson("""
            {"tag_name":"v1.0.14","html_url":"https://example.test","assets":[{"name":"malda-1.0.14-win-x64.zip","browser_download_url":"https://example.test/w.zip","size":1}]}
            """);
        var dest = new InstallationLocation(InstallationKind.Distribution, @"C:\malda");

        var update = InstallationUpdateService.Evaluate(dest, "v1.0.13", latest);
        Assert.Equal(UpdateAvailability.UpdateAvailable, update.Availability);
        Assert.Equal("malda-1.0.14-win-x64.zip", update.WinX64Asset?.Name);

        var current = InstallationUpdateService.Evaluate(dest, "v1.0.14", latest);
        Assert.Equal(UpdateAvailability.UpToDate, current.Availability);

        var newer = InstallationUpdateService.Evaluate(dest, "v1.0.15", latest);
        Assert.Equal(UpdateAvailability.LocalNewer, newer.Availability);
    }

    [Fact]
    public void ReadInstalledTag_PrefersMarkerFile()
    {
        var root = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, InstallationUpdateService.MarkerFileName), "v1.0.9\n");
            Assert.Equal("v1.0.9", InstallationUpdateService.ReadInstalledTag(root, "1.0.13"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolveExtractedRoot_UnwrapsSingleFolderZip()
    {
        var extract = CreateTempDir();
        try
        {
            var inner = Path.Combine(extract, "malda-1.0.13-win-x64");
            WriteDistMarkers(inner);

            var resolved = InstallationUpdateService.ResolveExtractedRoot(extract);

            Assert.Equal(Path.GetFullPath(inner), resolved);
        }
        finally
        {
            TryDelete(extract);
        }
    }

    [Fact]
    public void ApplyExtractedRelease_ReplacesBinAndPreservesExtraFiles()
    {
        var destination = CreateTempDir();
        var payload = CreateTempDir();
        try
        {
            WriteDistMarkers(destination);
            File.WriteAllText(Path.Combine(destination, "bin", "desktop-ide", "MaldaLang.DesktopIDE.exe"), "old-exe");
            File.WriteAllText(Path.Combine(destination, "bin", "stale.dll"), "stale");
            File.WriteAllText(Path.Combine(destination, "run-custom.bat"), "keep-me");
            File.WriteAllText(Path.Combine(destination, "Examples", "mine.malda"), "let x = 1");

            WriteDistMarkers(payload);
            File.WriteAllText(Path.Combine(payload, "bin", "desktop-ide", "MaldaLang.DesktopIDE.exe"), "new-exe");
            File.WriteAllText(Path.Combine(payload, "Examples", "Basics", "hello.malda"), "print(\"hi\")");
            File.WriteAllText(Path.Combine(payload, "README.txt"), "release notes");

            InstallationUpdateService.ApplyExtractedRelease(payload, destination, "v1.0.14");

            Assert.Equal("new-exe", File.ReadAllText(Path.Combine(destination, "bin", "desktop-ide", "MaldaLang.DesktopIDE.exe")));
            Assert.False(File.Exists(Path.Combine(destination, "bin", "stale.dll")));
            Assert.Equal("keep-me", File.ReadAllText(Path.Combine(destination, "run-custom.bat")));
            Assert.Equal("let x = 1", File.ReadAllText(Path.Combine(destination, "Examples", "mine.malda")));
            Assert.Equal("print(\"hi\")", File.ReadAllText(Path.Combine(destination, "Examples", "Basics", "hello.malda")));
            Assert.Equal("v1.0.14", File.ReadAllText(Path.Combine(destination, InstallationUpdateService.MarkerFileName)).Trim());
        }
        finally
        {
            TryDelete(destination);
            TryDelete(payload);
        }
    }

    [Fact]
    public void ApplyExtractedRelease_RefusesSourceTree()
    {
        var destination = CreateTempDir();
        var payload = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(destination, "MaldaLang.sln"), "sln");
            WriteDistMarkers(payload);

            var error = Assert.Throws<InvalidOperationException>(() =>
                InstallationUpdateService.ApplyExtractedRelease(payload, destination, "v1.0.14"));

            Assert.Contains("source checkout", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(destination);
            TryDelete(payload);
        }
    }

    [Fact]
    public void TryParseApplyRequest_ReadsNamedFlags()
    {
        var parsed = InstallationUpdateService.TryParseApplyRequest(
            ["--apply-update", "--payload", @"C:\cache\extract", "--destination", @"C:\malda", "--tag", "v1.0.14", "--wait-pid", "42"],
            out var request,
            out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal(@"C:\cache\extract", request.PayloadRoot);
        Assert.Equal(@"C:\malda", request.Destination);
        Assert.Equal("v1.0.14", request.Tag);
        Assert.Equal(42, request.WaitPid);
    }

    [Fact]
    public void TryParseApplyRequest_IgnoresUnrelatedArgs()
    {
        var parsed = InstallationUpdateService.TryParseApplyRequest(["program.malda"], out var request, out var error);

        Assert.False(parsed);
        Assert.Null(request);
        Assert.Null(error);
    }

    [Fact]
    public void FormatBytes_UsesReadableUnits()
    {
        Assert.Equal("512 B", InstallationUpdateService.FormatBytes(512));
        Assert.Equal("2.0 KB", InstallationUpdateService.FormatBytes(2048));
        Assert.Equal("1.0 MB", InstallationUpdateService.FormatBytes(1024 * 1024));
    }

    [Fact]
    public void LatestReleaseApiUrl_PointsAtOfficialRepo()
    {
        Assert.Equal(
            "https://api.github.com/repos/amaldini/maldalang/releases/latest",
            InstallationUpdateService.LatestReleaseApiUrl());
    }

    private static void WriteDistMarkers(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "ReferenceManual"));
        Directory.CreateDirectory(Path.Combine(root, "bin", "desktop-ide"));
        Directory.CreateDirectory(Path.Combine(root, "bin", "malda"));
        Directory.CreateDirectory(Path.Combine(root, "Examples", "Basics"));
        File.WriteAllText(Path.Combine(root, "ReferenceManual", "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(root, "bin", "desktop-ide", "MaldaLang.DesktopIDE.exe"), "exe");
        File.WriteAllText(Path.Combine(root, "bin", "malda", "malda.exe"), "cli");
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "malda-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort for local temp leftovers.
        }
    }
}
