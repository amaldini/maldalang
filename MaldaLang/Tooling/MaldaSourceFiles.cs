// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Tooling;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// The <c>*.malda</c> files under a directory, as <c>malda lint</c> and <c>malda fmt</c> see them:
/// recursive, sorted, skipping build output and dot-folders.
/// </summary>
internal static class MaldaSourceFiles
{
    /// <summary>Build output and dependency folders that never hold sources.</summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "artifacts"
    };

    /// <summary>One line for usage text, so both commands describe the walk the same way.</summary>
    public const string UsageNote =
        "Directories are searched recursively for *.malda (bin/, obj/, node_modules/, artifacts/ and dot-folders are skipped).";

    public static bool IsSkippedDirectory(string name) =>
        // Dot-folders (.git, .vs, .malda-play staging copies) are tooling output, not sources.
        SkippedDirectories.Contains(name) || name.StartsWith('.');

    public static List<string> Enumerate(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var found = new List<string>();
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            found.AddRange(Directory.EnumerateFiles(dir, "*.malda"));
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!IsSkippedDirectory(Path.GetFileName(sub)))
                    pending.Push(sub);
            }
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }
}
