// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Keeps per-file licence notices consistent with the dual MIT OR Apache-2.0 offer at
/// the repository root. A file asserting "All rights reserved" claims the opposite of
/// that grant; the mismatch is what automated licence scanners flag when a project is
/// evaluated as a dependency. A file offering only one of the two licences quietly
/// breaks the dual offer for everything downstream of it.
/// </summary>
public class LicenseHeaderGuardTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private const string DualIdentifier = "SPDX-License-Identifier: MIT OR Apache-2.0";

    private static readonly string[] ScannedExtensions =
    {
        ".cs", ".html", ".css", ".js", ".ps1", ".malda", ".md",
        ".json", ".yml", ".yaml", ".xml", ".csproj", ".razor", ".sln"
    };

    private static readonly string[] SkippedDirectories =
    {
        ".git", ".vs", "bin", "obj", "artifacts", "node_modules", "packages", "TestResults"
    };

    /// <summary>
    /// Files whose subject matter is licensing itself, so they legitimately quote the
    /// phrase, plus vendored third-party assets that carry their own upstream notices.
    /// </summary>
    private static readonly string[] AllowedPaths =
    {
        "AGENTS.md",
        "CONTRIBUTING.md",
        "THIRD-PARTY-NOTICES.md",
        // This file spells out the phrase it forbids, in its own failure messages.
        Path.Combine("MaldaLang.Tests", "LicenseHeaderGuardTests.cs"),
        Path.Combine("MaldaLang.IDE", "wwwroot", "bootstrap")
    };

    [Fact]
    public void NoSourceFile_ClaimsAllRightsReserved()
    {
        var offenders = EnumerateScannableFiles()
            .Where(path => File.ReadAllText(path)
                .Contains("all rights reserved", StringComparison.OrdinalIgnoreCase))
            .Select(Relative)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These files claim \"All rights reserved\", which contradicts the licences at the "
            + $"repository root. Replace the line with \"{DualIdentifier}\":"
            + System.Environment.NewLine
            + string.Join(System.Environment.NewLine, offenders.Select(p => "  " + p)));
    }

    [Fact]
    public void EveryCopyrightedCsFile_CarriesTheSpdxIdentifier()
    {
        var offenders = new List<string>();

        foreach (var path in EnumerateScannableFiles()
                     .Where(p => Path.GetExtension(p).Equals(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            // The header, when present, occupies the first lines of the file.
            var head = File.ReadLines(path).Take(6).ToList();
            var hasCopyright = head.Any(line => line.Contains("Copyright", StringComparison.Ordinal));
            if (!hasCopyright)
            {
                // No header at all is acceptable: the root licences cover the repository.
                continue;
            }

            var hasSpdx = head.Any(line => line.Contains(DualIdentifier, StringComparison.Ordinal));
            if (!hasSpdx)
            {
                offenders.Add(Relative(path));
            }
        }

        offenders.Sort(StringComparer.Ordinal);

        Assert.True(
            offenders.Count == 0,
            "These C# files declare a copyright without the matching SPDX identifier. "
            + $"Add \"// SPDX-License-Identifier: {DualIdentifier}\" under the copyright line. "
            + "A bare \"MIT\" is no longer enough: it would withdraw the Apache-2.0 option "
            + "for everything that depends on the file."
            + System.Environment.NewLine
            + string.Join(System.Environment.NewLine, offenders.Select(p => "  " + p)));
    }

    [Fact]
    public void RootLicenses_OfferBothMitAndApache()
    {
        var mit = Path.Combine(RepoRoot, "LICENSE-MIT");
        var apache = Path.Combine(RepoRoot, "LICENSE-APACHE");

        Assert.True(File.Exists(mit), $"Missing {mit}");
        Assert.True(File.Exists(apache), $"Missing {apache}");

        var mitText = File.ReadAllText(mit);
        Assert.Contains("MIT License", mitText, StringComparison.Ordinal);
        Assert.DoesNotContain("all rights reserved", mitText, StringComparison.OrdinalIgnoreCase);

        var apacheText = File.ReadAllText(apache);
        Assert.Contains("Apache License", apacheText, StringComparison.Ordinal);
        Assert.Contains("Version 2.0, January 2004", apacheText, StringComparison.Ordinal);

        // Section 4(d) binds redistributors only when a file named exactly NOTICE
        // exists. Adding one would impose that duty on everyone downstream.
        Assert.False(
            File.Exists(Path.Combine(RepoRoot, "NOTICE")),
            "A file named NOTICE triggers Apache-2.0 section 4(d) for every redistributor. "
            + "Put third-party information in THIRD-PARTY-NOTICES.md instead.");

        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        Assert.Contains("LICENSE-MIT", readme, StringComparison.Ordinal);
        Assert.Contains("LICENSE-APACHE", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The runtime exception is what lets users ship programs compiled with MALDA
    /// without attributing the runtime the transpilers inject into their output.
    /// Losing it silently would quietly change the terms for every downstream user.
    /// </summary>
    [Fact]
    public void RuntimeLibraryException_IsPresentAndReferenced()
    {
        var exceptionPath = Path.Combine(RepoRoot, "LICENSE-RUNTIME-EXCEPTION");
        Assert.True(File.Exists(exceptionPath), $"Missing {exceptionPath}");

        var text = File.ReadAllText(exceptionPath);
        Assert.Contains("MIT License", text, StringComparison.Ordinal);
        Assert.Contains("Apache License 2.0", text, StringComparison.Ordinal);
        Assert.Contains("Runtime Material", text, StringComparison.Ordinal);

        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        Assert.Contains("LICENSE-RUNTIME-EXCEPTION", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks the tree pruning skipped directories as it descends, rather than
    /// enumerating everything and filtering afterwards: build output and
    /// <c>.git</c> dwarf the source tree and would dominate the run.
    /// </summary>
    private static IEnumerable<string> EnumerateScannableFiles()
    {
        var pending = new Stack<string>();
        pending.Push(RepoRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(child);
                if (!SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!ScannedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;
                if (IsAllowed(file))
                    continue;

                yield return file;
            }
        }
    }

    private static bool IsAllowed(string path)
    {
        var relative = Relative(path);
        return AllowedPaths.Any(allowed =>
            relative.Equals(allowed, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(RepoRoot, path);
}
