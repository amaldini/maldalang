// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using System.Text.RegularExpressions;

namespace MaldaLang.Compiler;

/// <summary>
/// Leaves the optional areas of <c>malda-js-runtime.js</c> (<c>game.*</c>, <c>three.*</c>) out of
/// the copy written beside a program that does not use them. Areas sit between
/// <c>// @malda-runtime-area NAME</c> and <c>// @malda-runtime-area-end NAME</c> lines around one
/// member of the runtime object; a dropped area becomes <c>NAME: missingRuntimeArea("NAME")</c>,
/// which names the area when something reaches it anyway.
/// </summary>
public static class JsRuntimeAreas
{
    /// <summary>The optional areas, in file order.</summary>
    public static readonly IReadOnlyList<string> All = new[] { "game", "three" };

    /// <summary>First line of a trimmed runtime: the areas it keeps.</summary>
    private const string HeaderPrefix = "// malda-js-runtime areas:";

    private static readonly Regex AreaStart = new(@"^\s*// @malda-runtime-area (\w+)\s*$", RegexOptions.Compiled);

    /// <summary>The optional areas a generated program calls (<c>mlRuntime.game.…</c>).</summary>
    public static IReadOnlySet<string> UsedBy(string generatedJavaScript) =>
        All.Where(area => generatedJavaScript.Contains("mlRuntime." + area, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The areas a runtime file keeps: those named in its header, or every area for a full runtime
    /// (no header). Null when the file does not exist.
    /// </summary>
    public static IReadOnlySet<string>? KeptBy(string runtimePath)
    {
        if (!File.Exists(runtimePath))
            return null;
        using var reader = new StreamReader(runtimePath);
        var first = reader.ReadLine()?.TrimStart('﻿') ?? string.Empty;
        if (!first.StartsWith(HeaderPrefix, StringComparison.Ordinal))
            return All.ToHashSet(StringComparer.Ordinal);
        return first[HeaderPrefix.Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(area => All.Contains(area))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The runtime text with only <paramref name="keep"/> among the optional areas. Keeping every
    /// area returns <paramref name="fullRuntime"/> unchanged.
    /// </summary>
    public static string Trim(string fullRuntime, IReadOnlySet<string> keep)
    {
        if (All.All(keep.Contains))
            return fullRuntime;

        fullRuntime = fullRuntime.TrimStart('﻿');
        var newline = fullRuntime.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = fullRuntime.Split('\n');
        var output = new StringBuilder(fullRuntime.Length);
        var kept = All.Where(keep.Contains).ToList();
        output.Append(HeaderPrefix).Append(' ').Append(kept.Count == 0 ? "none" : string.Join(", ", kept)).Append(newline);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var start = AreaStart.Match(line);
            if (!start.Success || keep.Contains(start.Groups[1].Value))
            {
                output.Append(lines[i]);
                if (i < lines.Length - 1)
                    output.Append('\n');
                continue;
            }

            var area = start.Groups[1].Value;
            var end = Array.FindIndex(lines, i + 1, candidate => candidate.TrimEnd('\r').Trim() == "// @malda-runtime-area-end " + area);
            if (end < 0)
                throw new InvalidOperationException($"malda-js-runtime.js: area '{area}' has no end marker.");

            // The member keeps its trailing comma, if the area's last line had one.
            var lastCode = lines[end - 1].TrimEnd('\r').TrimEnd();
            var indent = line[..(line.Length - line.TrimStart().Length)];
            output.Append($"{indent}{area}: missingRuntimeArea(\"{area}\"){(lastCode.EndsWith(',') ? "," : string.Empty)}");
            output.Append(newline);
            i = end;
        }

        return output.ToString();
    }

    /// <summary>
    /// Writes the runtime beside a program. Areas already kept by a runtime in that folder stay, so
    /// several programs compiled into one folder keep working whatever the order.
    /// </summary>
    public static void WriteBeside(string outputDirectory, string fullRuntime, string generatedJavaScript)
    {
        var runtimePath = Path.Combine(outputDirectory, "malda-js-runtime.js");
        var keep = new HashSet<string>(UsedBy(generatedJavaScript), StringComparer.Ordinal);
        var existing = KeptBy(runtimePath);
        if (existing != null)
            keep.UnionWith(existing);
        File.WriteAllText(runtimePath, Trim(fullRuntime, keep), Encoding.UTF8);
    }
}
