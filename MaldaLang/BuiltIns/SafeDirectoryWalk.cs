// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.IO;

/// <summary>
/// Recursive directory walks that do not enter directory junctions or symbolic links.
/// <see cref="SearchOption.AllDirectories"/> follows reparse points, so a folder linked
/// to itself (or a Windows profile link such as <c>Application Data</c>) walks until
/// the path cannot be resolved. Callers then surface that entire path as an error.
/// </summary>
internal static class SafeDirectoryWalk
{
    public const int MaxErrorMessageLength = 240;

    public static string ShortMessage(string? message)
    {
        if (string.IsNullOrEmpty(message) || message.Length <= MaxErrorMessageLength)
            return message ?? "";
        return string.Concat(message.AsSpan(0, MaxErrorMessageLength), "...");
    }

    public static IEnumerable<string> EnumerateFiles(string root, string searchPattern = "*")
    {
        var fullRoot = Path.GetFullPath(root);
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if (!seen.Add(dir))
                continue;

            var isRoot = string.Equals(dir, fullRoot, StringComparison.OrdinalIgnoreCase);
            List<string> files;
            List<string> subdirs;
            try
            {
                files = Directory.EnumerateFiles(dir, searchPattern, SearchOption.TopDirectoryOnly).ToList();
                subdirs = Directory.EnumerateDirectories(dir).ToList();
            }
            catch (Exception) when (!isRoot)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            foreach (var sub in subdirs)
            {
                if (IsDirectoryReparsePoint(sub))
                    continue;
                pending.Push(sub);
            }
        }
    }

    /// <summary>
    /// Subdirectories of <paramref name="root"/>. A directory reparse point is yielded
    /// once and is not entered.
    /// </summary>
    public static IEnumerable<string> EnumerateDirectories(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var pending = new Stack<string>();
        pending.Push(fullRoot);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if (!seen.Add(dir))
                continue;

            var isRoot = string.Equals(dir, fullRoot, StringComparison.OrdinalIgnoreCase);
            List<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir).ToList();
            }
            catch (Exception) when (!isRoot)
            {
                continue;
            }

            foreach (var sub in subdirs)
            {
                yield return sub;
                if (IsDirectoryReparsePoint(sub))
                    continue;
                pending.Push(sub);
            }
        }
    }

    public static bool IsDirectoryReparsePoint(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.Directory) != 0
                && (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
