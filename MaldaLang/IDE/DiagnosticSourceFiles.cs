// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System;
using System.Collections.Generic;
using System.IO;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST;

/// <summary>
/// Source-file attribution for <see cref="Diagnostic"/>s. <c>include</c> splices the statements of
/// another file into the program; their nodes keep their own <see cref="Node.SourceFile"/> and
/// lines, so a diagnostic raised on one must name that file rather than the analyzed document.
/// </summary>
public static class DiagnosticSourceFiles
{
    /// <summary>
    /// Sets <see cref="Diagnostic.SourceFile"/> from <paramref name="node"/> on every diagnostic added
    /// since <paramref name="fromIndex"/> that does not name a file yet. Validators call it around the
    /// work they do for one declaration when the position they report is not itself a node.
    /// </summary>
    public static void Stamp(List<Diagnostic> diagnostics, int fromIndex, Node? node)
    {
        var file = node?.SourceFile;
        if (string.IsNullOrWhiteSpace(file))
            return;

        for (var i = Math.Max(0, fromIndex); i < diagnostics.Count; i++)
            diagnostics[i].SourceFile ??= file;
    }

    /// <summary>
    /// Clears <see cref="Diagnostic.SourceFile"/> when it names the analyzed document itself, so that
    /// a non-null value always means "another (included) file". Auto-fixes on a diagnostic from
    /// another file are dropped: they would edit the analyzed document's text.
    /// </summary>
    public static void Normalize(List<Diagnostic> diagnostics, string? documentFile)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.SourceFile == null)
                continue;

            if (string.IsNullOrWhiteSpace(diagnostic.SourceFile) || IsSameFile(diagnostic.SourceFile, documentFile))
            {
                diagnostic.SourceFile = null;
                continue;
            }

            diagnostic.AutoFix = null;
        }
    }

    /// <summary>True when the diagnostic belongs to the analyzed document (not to an included file).</summary>
    public static bool IsInDocument(Diagnostic diagnostic) => diagnostic.SourceFile == null;

    public static bool IsSameFile(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A path for CLI output: relative to <paramref name="baseDirectory"/> (default: the current
    /// directory) when the file is below it, otherwise the full path.
    /// </summary>
    public static string ToDisplayPath(string path, string? baseDirectory = null)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(baseDirectory ?? System.Environment.CurrentDirectory);
            var relative = Path.GetRelativePath(root, full);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                return relative.Replace('\\', '/');
            return full;
        }
        catch
        {
            return path;
        }
    }
}
