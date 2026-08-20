// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;

namespace MaldaLang.Compiler;

/// <summary>
/// VLQ source map (v3) produced by <see cref="JsTranspiler"/>. Lines are 1-based
/// at this API; the JSON payload stores 0-based VLQ deltas.
/// </summary>
public sealed class JsSourceMap
{
    private readonly Dictionary<int, int> _generatedToOriginal = new();
    private readonly Dictionary<int, int> _originalToGenerated = new();
    private readonly List<int> _originalLinesAscending = new();

    public string FileName { get; }
    public string SourceName { get; }

    private JsSourceMap(string fileName, string sourceName)
    {
        FileName = fileName;
        SourceName = sourceName;
    }

    public static JsSourceMap Parse(string sourceMapJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMapJson);
        using var document = JsonDocument.Parse(sourceMapJson);
        var root = document.RootElement;
        var fileName = root.TryGetProperty("file", out var fileElement)
            ? fileElement.GetString() ?? "program.js"
            : "program.js";
        var sourceName = "source.malda";
        if (root.TryGetProperty("sources", out var sources) &&
            sources.ValueKind == JsonValueKind.Array &&
            sources.GetArrayLength() > 0)
        {
            sourceName = sources[0].GetString() ?? sourceName;
        }

        var map = new JsSourceMap(fileName, sourceName);
        var mappings = root.TryGetProperty("mappings", out var mappingsElement)
            ? mappingsElement.GetString() ?? ""
            : "";
        map.LoadMappings(mappings);
        return map;
    }

    public int? ToOriginalLine(int generatedLine1Based)
    {
        return _generatedToOriginal.TryGetValue(generatedLine1Based, out var original)
            ? original
            : null;
    }

    public int? ToGeneratedLine(int originalLine1Based)
    {
        return _originalToGenerated.TryGetValue(originalLine1Based, out var generated)
            ? generated
            : null;
    }

    /// <summary>
    /// Maps a MALDA line to a generated JS line, or to the next mapped statement
    /// at or after that line (same rule as interpret-mode unverified breakpoints).
    /// </summary>
    public int? ToGeneratedLineOrNext(int originalLine1Based)
    {
        var exact = ToGeneratedLine(originalLine1Based);
        if (exact.HasValue)
        {
            return exact;
        }

        foreach (var original in _originalLinesAscending)
        {
            if (original >= originalLine1Based)
            {
                return _originalToGenerated[original];
            }
        }

        return null;
    }

    private void LoadMappings(string mappings)
    {
        var generatedLine = 1;
        var sourceIndex = 0;
        var sourceLine = 0;
        var sourceColumn = 0;
        var index = 0;

        while (index <= mappings.Length)
        {
            var atEnd = index == mappings.Length;
            var ch = atEnd ? ';' : mappings[index];
            if (ch == ';' || atEnd)
            {
                generatedLine++;
                if (atEnd)
                {
                    break;
                }

                index++;
                continue;
            }

            if (ch == ',')
            {
                index++;
                continue;
            }

            var generatedColumn = DecodeVlq(mappings, ref index);
            _ = generatedColumn;
            if (index < mappings.Length && mappings[index] != ';' && mappings[index] != ',')
            {
                sourceIndex += DecodeVlq(mappings, ref index);
                sourceLine += DecodeVlq(mappings, ref index);
                sourceColumn += DecodeVlq(mappings, ref index);
                if (index < mappings.Length && mappings[index] != ';' && mappings[index] != ',')
                {
                    _ = DecodeVlq(mappings, ref index);
                }
            }

            if (sourceIndex != 0)
            {
                continue;
            }

            var originalLine1Based = sourceLine + 1;
            if (!_generatedToOriginal.ContainsKey(generatedLine))
            {
                _generatedToOriginal[generatedLine] = originalLine1Based;
            }

            if (!_originalToGenerated.ContainsKey(originalLine1Based) ||
                generatedLine < _originalToGenerated[originalLine1Based])
            {
                _originalToGenerated[originalLine1Based] = generatedLine;
            }
        }

        _originalLinesAscending.AddRange(_originalToGenerated.Keys);
        _originalLinesAscending.Sort();
    }

    private static int DecodeVlq(string mappings, ref int index)
    {
        var result = 0;
        var shift = 0;
        while (index < mappings.Length)
        {
            var digit = FromBase64(mappings[index]);
            index++;
            if (digit < 0)
            {
                break;
            }

            result |= (digit & 31) << shift;
            shift += 5;
            if ((digit & 32) == 0)
            {
                break;
            }
        }

        var negative = (result & 1) != 0;
        var magnitude = result >> 1;
        return negative ? -magnitude : magnitude;
    }

    private static int FromBase64(char ch)
    {
        if (ch >= 'A' && ch <= 'Z')
        {
            return ch - 'A';
        }

        if (ch >= 'a' && ch <= 'z')
        {
            return ch - 'a' + 26;
        }

        if (ch >= '0' && ch <= '9')
        {
            return ch - '0' + 52;
        }

        if (ch == '+')
        {
            return 62;
        }

        if (ch == '/')
        {
            return 63;
        }

        return -1;
    }
}

public readonly record struct JsGeneratedBreakpoint(
    int OriginalLine,
    int GeneratedLine,
    string? Condition,
    bool Verified);

public static class JsDebugBreakpointMapper
{
    public static IReadOnlyList<JsGeneratedBreakpoint> Map(
        JsSourceMap sourceMap,
        IEnumerable<(int Line, string? Condition, bool Enabled)> originalBreakpoints)
    {
        ArgumentNullException.ThrowIfNull(sourceMap);
        ArgumentNullException.ThrowIfNull(originalBreakpoints);

        var mapped = new List<JsGeneratedBreakpoint>();
        foreach (var breakpoint in originalBreakpoints)
        {
            if (!breakpoint.Enabled)
            {
                continue;
            }

            var generated = sourceMap.ToGeneratedLineOrNext(breakpoint.Line);
            mapped.Add(new JsGeneratedBreakpoint(
                breakpoint.Line,
                generated ?? 0,
                string.IsNullOrWhiteSpace(breakpoint.Condition) ? null : breakpoint.Condition.Trim(),
                generated.HasValue));
        }

        return mapped;
    }
}
