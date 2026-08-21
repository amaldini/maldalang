// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class WebPreviewHostBuilderTests
{
    [Fact]
    public void BuildHostUri_PrefixesAssetsRelativeToOpenSourceFile()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), "malda-preview-host-" + Guid.NewGuid().ToString("N"));
        var gamesDir = Path.Combine(repoRoot, "Examples", "Games");
        Directory.CreateDirectory(gamesDir);
        var hostPath = Path.Combine(repoRoot, "program.html");
        var scriptPath = Path.Combine(repoRoot, ".malda-preview", "Examples_Games_three_textured_malda.js");
        var sourcePath = Path.Combine(gamesDir, "three_textured.malda");
        File.WriteAllText(hostPath, "<html></html>");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(scriptPath, "void 0;");
        File.WriteAllText(sourcePath, "");

        try
        {
            var uri = WebPreviewHostBuilder.BuildHostUri(
                hostPath,
                repoRoot,
                scriptPath,
                "three_textured",
                sourcePath);

            Assert.Equal("https", uri.Scheme);
            Assert.Equal(WebPreviewHostBuilder.VirtualHostName, uri.Host);
            Assert.Equal("/program.html", uri.AbsolutePath);
            Assert.Contains("assets=Examples%2FGames%2F", uri.Query, StringComparison.Ordinal);
            Assert.Contains("script=.malda-preview%2FExamples_Games_three_textured_malda.js", uri.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("runtime=", uri.Query, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    [Fact]
    public void BuildHostUri_GeneratedHostResolvesRuntimeAndAssetsThroughRepoRoot()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), "malda-preview-gen-" + Guid.NewGuid().ToString("N"));
        var previewDir = Path.Combine(repoRoot, ".malda-preview");
        var gamesDir = Path.Combine(repoRoot, "Examples", "Games");
        Directory.CreateDirectory(previewDir);
        Directory.CreateDirectory(gamesDir);
        var hostPath = Path.Combine(previewDir, "program.html");
        var scriptPath = Path.Combine(previewDir, "demo.js");
        var sourcePath = Path.Combine(gamesDir, "demo.malda");
        File.WriteAllText(hostPath, "<html></html>");
        File.WriteAllText(scriptPath, "void 0;");
        File.WriteAllText(sourcePath, "");

        try
        {
            var uri = WebPreviewHostBuilder.BuildHostUri(
                hostPath,
                repoRoot,
                scriptPath,
                "demo",
                sourcePath);

            Assert.Equal("/.malda-preview/program.html", uri.AbsolutePath);
            Assert.Contains("assets=..%2FExamples%2FGames%2F", uri.Query, StringComparison.Ordinal);
            Assert.Contains("runtime=..%2FExamples%2FWeb%2Fwwwroot%2Fmalda-js-runtime.js", uri.Query, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(repoRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    [Fact]
    public void BuildAssetBase_SameDirectoryIsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "malda-preview-same-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var assetBase = WebPreviewHostBuilder.BuildAssetBase(
                dir,
                Path.Combine(dir, "app.malda"));
            Assert.Equal("", assetBase);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
