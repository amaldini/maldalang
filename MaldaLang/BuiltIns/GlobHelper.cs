// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.IO;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

internal sealed record GlobMatchItem(string Name, string Type, string Path);

internal sealed record GlobMatchResult(IReadOnlyList<GlobMatchItem> Items, int Count, bool Truncated);

internal static class GlobHelper
{
    public const int DefaultMaxResults = 200;
    public const int HardMaxResults = 500;

    private static readonly HashSet<string> DefaultExcludeDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj"
    };

    public static GlobMatchResult Match(
        string rootDir,
        string pattern,
        int maxResults,
        bool includeDirectories,
        string? excludeDirs,
        string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains('\0'))
            return new GlobMatchResult(Array.Empty<GlobMatchItem>(), 0, false);

        var cap = maxResults <= 0 ? DefaultMaxResults : Math.Min(maxResults, HardMaxResults);

        var absoluteRoot = Path.GetFullPath(rootDir);
        if (!Directory.Exists(absoluteRoot))
            return new GlobMatchResult(Array.Empty<GlobMatchItem>(), 0, false);

        var excludes = new HashSet<string>(DefaultExcludeDirs, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(excludeDirs))
        {
            foreach (var part in excludeDirs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part != "")
                    excludes.Add(part);
            }
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern);
        foreach (var dir in excludes)
            matcher.AddExclude($"**/{dir}/**");

        var result = matcher.Execute(new SkippingReparseDirectoryInfo(new DirectoryInfo(absoluteRoot)));

        var candidates = new List<(string RelativePath, string Name, bool IsDirectory)>();

        foreach (var file in result.Files)
        {
            var rel = NormalizeRelativePath(file.Path);
            var name = Path.GetFileName(rel);
            if (!string.IsNullOrEmpty(name))
                candidates.Add((rel, name, false));
        }

        if (includeDirectories)
        {
            foreach (var dir in SafeDirectoryWalk.EnumerateDirectories(absoluteRoot))
            {
                if (IsUnderExcludedDirectory(dir, absoluteRoot, excludes))
                    continue;

                var rel = NormalizeRelativePath(Path.GetRelativePath(absoluteRoot, dir));
                var dirMatch = matcher.Match(rel);
                if (!dirMatch.HasMatches)
                {
                    dirMatch = matcher.Match(rel + "/");
                    if (!dirMatch.HasMatches)
                        continue;
                }

                var name = Path.GetFileName(rel.TrimEnd('/', '\\'));
                if (string.IsNullOrEmpty(name))
                    name = rel;
                if (!string.IsNullOrEmpty(rel))
                    candidates.Add((rel, name, true));
            }
        }

        candidates.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));

        var truncated = candidates.Count > cap;
        var take = truncated ? cap : candidates.Count;
        var items = new List<GlobMatchItem>(take);

        for (var i = 0; i < take; i++)
        {
            var c = candidates[i];
            var pathForResult = ToResultPath(absoluteRoot, c.RelativePath, workingDirectory);
            items.Add(new GlobMatchItem(c.Name, c.IsDirectory ? "directory" : "file", pathForResult));
        }

        return new GlobMatchResult(items, items.Count, truncated);
    }

    private static bool IsUnderExcludedDirectory(string absoluteDir, string absoluteRoot, HashSet<string> excludes)
    {
        var rel = Path.GetRelativePath(absoluteRoot, absoluteDir).Replace('\\', '/');
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (excludes.Contains(part))
                return true;
        }
        return false;
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/');

    private static string ToResultPath(string absoluteRoot, string relativePath, string? workingDirectory)
    {
        var normalizedRel = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return normalizedRel;

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(absoluteRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var baseDir = Path.GetFullPath(workingDirectory);
            return NormalizeRelativePath(Path.GetRelativePath(baseDir, fullPath));
        }
        catch
        {
            return normalizedRel;
        }
    }
}

/// <summary>
/// <see cref="DirectoryInfoWrapper"/> that does not yield directory junctions or
/// symbolic links, so <c>Matcher</c> cannot recurse through a cycle.
/// </summary>
internal sealed class SkippingReparseDirectoryInfo : DirectoryInfoBase
{
    private readonly DirectoryInfo _directory;

    public SkippingReparseDirectoryInfo(DirectoryInfo directory)
    {
        _directory = directory;
    }

    public override string Name => _directory.Name;

    public override string FullName => _directory.FullName;

    public override DirectoryInfoBase? ParentDirectory
    {
        get
        {
            var parent = _directory.Parent;
            return parent == null ? null : new SkippingReparseDirectoryInfo(parent);
        }
    }

    public override IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos()
    {
        List<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(_directory.FullName).ToList();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(entry);
            }
            catch (Exception)
            {
                continue;
            }

            var isDirectory = (attrs & FileAttributes.Directory) != 0;
            if (isDirectory && (attrs & FileAttributes.ReparsePoint) != 0)
                continue;

            if (isDirectory)
                yield return new SkippingReparseDirectoryInfo(new DirectoryInfo(entry));
            else
                yield return new FileInfoWrapper(new FileInfo(entry));
        }
    }

    public override DirectoryInfoBase GetDirectory(string path)
        => new SkippingReparseDirectoryInfo(new DirectoryInfo(Path.Combine(_directory.FullName, path)));

    public override FileInfoBase GetFile(string path)
        => new FileInfoWrapper(new FileInfo(Path.Combine(_directory.FullName, path)));
}
