// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace MaldaLang.Compiler;

/// <summary>
/// The last step of a JavaScript build (<c>--mode js</c> / <c>esm</c> / <c>pwa</c>): optional
/// minification (<see cref="MinifyJavaScript"/>, <c>--minify</c>) and the size report the CLI
/// prints, program + runtime (+ UI client), raw and gzipped.
/// </summary>
public partial class Compiler
{
    /// <summary>
    /// Size budget, in characters, of the runtime written beside a program that uses no optional area
    /// (<c>ui</c> / <c>game</c> / <c>three</c>). <c>JsBuildSizeTests</c> fails when the core grows past it:
    /// raise it on purpose, with the reason in the commit.
    /// </summary>
    public const int CoreRuntimeBudgetCharacters = 300_000;

    /// <summary><c>--minify</c>: minify the JavaScript files of the build (see <see cref="JsMinifier"/>).</summary>
    public bool MinifyJavaScript { get; set; }

    private string FinishJavaScriptBuild(string outputDirectory, string programPath, bool programHasSourceMap)
    {
        var runtimePath = Path.Combine(outputDirectory, "malda-js-runtime.js");
        var clientPath = Path.Combine(outputDirectory, JsUiClientFileName);
        var areas = JsRuntimeAreas.KeptBy(runtimePath) ?? new HashSet<string>();
        var files = new List<string> { programPath, runtimePath };
        if (File.Exists(clientPath))
            files.Add(clientPath);

        string? minifiedBy = null;
        if (MinifyJavaScript)
            minifiedBy = Minify(outputDirectory, programPath, programHasSourceMap, runtimePath, clientPath);

        return SizeReport(files, areas, minifiedBy);
    }

    private static string Minify(string outputDirectory, string programPath, bool programHasSourceMap, string runtimePath, string clientPath)
    {
        // The first line names the runtime areas kept (JsRuntimeAreas.KeptBy): minifiers drop comments.
        var runtimeHeader = File.ReadLines(runtimePath).FirstOrDefault()?.TrimStart('﻿');
        var keepHeader = runtimeHeader != null && runtimeHeader.StartsWith("// malda-js-runtime areas:", StringComparison.Ordinal);
        var runtimeFiles = File.Exists(clientPath) ? new[] { runtimePath, clientPath } : new[] { runtimePath };

        var esbuild = JsMinifier.FindEsbuild(outputDirectory);
        string tool;
        if (esbuild != null)
        {
            JsMinifier.MinifyWithEsbuild(esbuild, programPath, programHasSourceMap);
            foreach (var file in runtimeFiles)
                JsMinifier.MinifyWithEsbuild(esbuild, file, sourceMap: false);
            tool = "minified with esbuild";
        }
        else
        {
            foreach (var file in runtimeFiles)
                File.WriteAllText(file, JsMinifier.StripCommentsAndIndentation(File.ReadAllText(file)), new UTF8Encoding(false));
            tool = "esbuild not found (set MALDA_ESBUILD or npm install esbuild): runtime files without comments and indentation, program as written";
        }

        if (keepHeader)
        {
            var minified = File.ReadAllText(runtimePath);
            if (!minified.StartsWith(runtimeHeader!, StringComparison.Ordinal))
                File.WriteAllText(runtimePath, runtimeHeader + "\n" + minified, new UTF8Encoding(false));
        }

        return tool;
    }

    private static string SizeReport(IReadOnlyList<string> files, IReadOnlySet<string> areas, string? minifiedBy)
    {
        var report = new StringBuilder();
        report.AppendLine(minifiedBy == null ? "JavaScript size:" : $"JavaScript size ({minifiedBy}):");
        var width = files.Max(f => Path.GetFileName(f).Length);
        long total = 0;
        long totalGzip = 0;
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var gzip = GzipLength(bytes);
            total += bytes.Length;
            totalGzip += gzip;
            var note = Path.GetFileName(file) == "malda-js-runtime.js"
                ? "  areas: " + (areas.Count == 0 ? "none" : string.Join(", ", JsRuntimeAreas.All.Where(areas.Contains)))
                : string.Empty;
            report.AppendLine($"  {Path.GetFileName(file).PadRight(width)}  {Kilobytes(bytes.Length),9}  (gzip {Kilobytes(gzip)}){note}");
        }
        report.Append($"  {"total".PadRight(width)}  {Kilobytes(total),9}  (gzip {Kilobytes(totalGzip)})");
        return report.ToString();
    }

    private static long GzipLength(byte[] bytes)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);
        return buffer.Length;
    }

    private static string Kilobytes(long bytes) => (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
}
