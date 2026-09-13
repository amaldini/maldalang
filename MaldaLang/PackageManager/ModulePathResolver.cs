// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.IO;

public static class ModulePathResolver
{
    public static string ResolveRelativeModulePath(string modulePath, string? sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
            throw new ArgumentException("Module path cannot be empty.", nameof(modulePath));

        var baseDirectory = GetBaseDirectory(sourceFileName);

        return Path.IsPathRooted(modulePath)
            ? Path.GetFullPath(modulePath)
            : Path.GetFullPath(Path.Combine(baseDirectory, modulePath));
    }

    /// <summary>
    /// Absolute directory a relative module path is resolved against: the host file's
    /// directory, or the process working directory when the host is virtual / unknown.
    /// </summary>
    public static string GetBaseDirectory(string? sourceFileName)
    {
        string? baseDirectory = null;
        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            try
            {
                baseDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFileName));
            }
            catch
            {
                // Virtual or malformed host key (e.g. "file.malda#section"); fall back to cwd.
                baseDirectory = null;
            }
        }

        return string.IsNullOrWhiteSpace(baseDirectory) ? Environment.CurrentDirectory : baseDirectory;
    }

    /// <summary>
    /// Non-throwing sibling of <see cref="ResolveRelativeModulePath"/> for tooling.
    /// Returns false (instead of throwing) when the path cannot be resolved.
    /// </summary>
    public static bool TryResolveRelativeModulePath(string? modulePath, string? sourceFileName, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(modulePath))
            return false;

        try
        {
            resolvedPath = ResolveRelativeModulePath(modulePath, sourceFileName);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
