// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Resolves optional-pack SDK files from a repo workspace (packages/malda-*) without install.
/// </summary>
public static class WorkspacePackageResolver
{
    public static string? TryResolveModulePath(string packageName, string? subModule = null)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return null;

        if (!string.IsNullOrEmpty(subModule))
            return TryResolveSubModulePath(packageName, subModule);

        foreach (var packagesRoot in GetPackagesRoots())
        {
            var packageDir = Path.Combine(packagesRoot, packageName);
            if (!Directory.Exists(packageDir))
                continue;

            foreach (var candidate in GetMainEntryCandidates(packageName))
            {
                var fullPath = Path.Combine(packageDir, candidate);
                if (File.Exists(fullPath))
                    return Path.GetFullPath(fullPath);
            }

            var libDir = Path.Combine(packageDir, "lib");
            if (Directory.Exists(libDir))
            {
                foreach (var file in Directory.GetFiles(libDir, "*.malda"))
                    return Path.GetFullPath(file);
            }
        }

        return null;
    }

    private static string? TryResolveSubModulePath(string packageName, string subModule)
    {
        foreach (var packagesRoot in GetPackagesRoots())
        {
            var packageDir = Path.Combine(packagesRoot, packageName);
            if (!Directory.Exists(packageDir))
                continue;

            var libPath = Path.Combine(packageDir, "lib", $"{subModule}.malda");
            if (File.Exists(libPath))
                return Path.GetFullPath(libPath);

            var nested = Path.Combine(packageDir, $"{subModule}.malda");
            if (File.Exists(nested))
                return Path.GetFullPath(nested);
        }

        return null;
    }

    private static IEnumerable<string> GetMainEntryCandidates(string packageName)
    {
        if (packageName.StartsWith("malda-", StringComparison.Ordinal))
        {
            var shortName = packageName.Substring("malda-".Length);
            yield return $"{shortName}.malda";
        }

        yield return "index.malda";
        yield return "main.malda";
        yield return $"{packageName}.malda";
    }

    public static IEnumerable<string> GetPackagesRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();

        void TryAdd(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full) && seen.Add(full))
                roots.Add(full);
        }

        TryAdd(Environment.GetEnvironmentVariable("MALDA_PACKAGES_DIR"));

        var sdkRoot = Environment.GetEnvironmentVariable("MALDA_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(sdkRoot))
        {
            var packagesUnderSdk = Path.Combine(sdkRoot, "packages");
            TryAdd(Directory.Exists(packagesUnderSdk) ? packagesUnderSdk : sdkRoot);
        }

        foreach (var discovered in DiscoverPackagesDirsWalkUp(Directory.GetCurrentDirectory()))
            TryAdd(discovered);

        return roots;
    }

    /// <summary>
    /// Lists package folders under workspace roots that resolve to a .malda entry.
    /// </summary>
    public static IReadOnlyList<(string Name, string EntryPath)> ListWorkspacePackages()
    {
        var results = new List<(string Name, string EntryPath)>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetPackagesRoots())
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || !seenNames.Add(name))
                    continue;

                var entry = TryResolveModulePath(name);
                if (entry != null)
                    results.Add((name, entry));
            }
        }

        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private static IEnumerable<string> DiscoverPackagesDirsWalkUp(string startDirectory)
    {
        var current = Path.GetFullPath(startDirectory);
        for (var depth = 0; depth < 12 && !string.IsNullOrEmpty(current); depth++)
        {
            var packagesDir = Path.Combine(current, "packages");
            if (Directory.Exists(packagesDir))
                yield return Path.GetFullPath(packagesDir);

            var parent = Directory.GetParent(current)?.FullName;
            if (parent == null || parent == current)
                break;
            current = parent;
        }
    }
}
