// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Runtime.InteropServices;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guards the bound-edition cover slot: the SVG plate exists, book.css gives it
/// a zero-margin named page, and the build script inlines it as a data URI.
/// </summary>
public class ReferenceManualBookTests
{
    private static string ManualDir => PlanningPaths.ResolveRepoPath("ReferenceManual");

    private static string CoverPath => Path.Combine(ManualDir, "assets", "cover.svg");

    private static string BookCssPath => Path.Combine(ManualDir, "book.css");

    private static string ScriptPath =>
        PlanningPaths.ResolveRepoPath("scripts", "build-reference-manual-book.ps1");

    [Fact]
    public void CoverSvg_ExistsAndDeclaresAViewBox()
    {
        Assert.True(File.Exists(CoverPath), $"Missing cover plate: {CoverPath}");
        var svg = File.ReadAllText(CoverPath);
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 700 1000\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void BookCss_DeclaresZeroMarginCoverPage()
    {
        var css = File.ReadAllText(BookCssPath);
        Assert.Contains("@page cover", css, StringComparison.Ordinal);
        Assert.Contains("page: cover", css, StringComparison.Ordinal);
        Assert.Contains(".cover-plate", css, StringComparison.Ordinal);
        Assert.Contains(".cover-meta", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BookBuildScript_InlinesCoverAsDataUri()
    {
        var script = File.ReadAllText(ScriptPath);
        Assert.Contains("cover.svg", script, StringComparison.Ordinal);
        Assert.Contains("data:image/svg+xml;base64", script, StringComparison.Ordinal);
        Assert.Contains("@@COVER_SRC@@", script, StringComparison.Ordinal);
        Assert.Contains("@page cover", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BookBuildScript_EmitsInlinedCoverIntoTheBoundEdition()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "malda-book-cover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        try
        {
            var shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell" : "pwsh";
            var psi = new ProcessStartInfo
            {
                FileName = shell,
                ArgumentList =
                {
                    "-NoProfile",
                    "-ExecutionPolicy", "Bypass",
                    "-File", ScriptPath,
                    "-Trim", "6x9",
                    "-OutputDirectory", outputDir,
                    "-NoPagedJs",
                },
                WorkingDirectory = PlanningPaths.RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "build-reference-manual-book.ps1 timed out.");
            Assert.True(
                process.ExitCode == 0,
                $"build-reference-manual-book.ps1 failed (exit {process.ExitCode}).\nstdout:\n{stdout}\nstderr:\n{stderr}");

            var bookPath = Path.Combine(outputDir, "malda-reference-manual.html");
            Assert.True(File.Exists(bookPath), $"Expected bound edition at {bookPath}.\nstdout:\n{stdout}");
            var html = File.ReadAllText(bookPath);
            Assert.Contains("class=\"cover-plate\"", html, StringComparison.Ordinal);
            Assert.Contains("src=\"data:image/svg+xml;base64,", html, StringComparison.Ordinal);
            Assert.Contains("class=\"cover-meta\"", html, StringComparison.Ordinal);
            Assert.Contains("@page cover", html, StringComparison.Ordinal);
            Assert.Contains("--page-height: 9in", html, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"cover-rule\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("@@COVER_SRC@@", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }
}
