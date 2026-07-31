namespace MaldaLang.Testing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class TestDiscovery
{
    public IReadOnlyList<string> Discover(string rootPath, string? filter = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = Directory.GetCurrentDirectory();
        }

        var absoluteRoot = Path.GetFullPath(rootPath);
        if (File.Exists(absoluteRoot))
        {
            return IsDiscoverableFile(absoluteRoot)
                ? new List<string> { absoluteRoot }
                : Array.Empty<string>();
        }

        if (!Directory.Exists(absoluteRoot))
        {
            return Array.Empty<string>();
        }

        var discovered = Directory
            .EnumerateFiles(absoluteRoot, "*.malda", SearchOption.AllDirectories)
            .Where(IsDiscoverableFile)
            .OrderBy(NormalizePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            discovered = discovered
                .Where(path => NormalizePath(path).Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return discovered;
    }

    private static bool IsDiscoverableFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".test.malda", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".spec.malda", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = NormalizePath(filePath);
        return normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/__tests__/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
