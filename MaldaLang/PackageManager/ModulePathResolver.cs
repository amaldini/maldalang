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

        var baseDirectory = !string.IsNullOrWhiteSpace(sourceFileName)
            ? Path.GetDirectoryName(Path.GetFullPath(sourceFileName))
            : Environment.CurrentDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = Environment.CurrentDirectory;

        return Path.IsPathRooted(modulePath)
            ? Path.GetFullPath(modulePath)
            : Path.GetFullPath(Path.Combine(baseDirectory, modulePath));
    }
}
