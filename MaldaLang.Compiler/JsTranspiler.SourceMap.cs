// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

public partial class JsTranspiler
{
    private void EmitLine(string text)
    {
        if (text.Length == 0)
        {
            _output.AppendLine();
            _generatedLine++;
            return;
        }

        if (_currentSourceLine.HasValue && _currentSourceLine.Value > 0)
        {
            _mappings.Add(new SourceMappingEntry(
                _generatedLine,
                GeneratedColumn: 0,
                SourceLine: _currentSourceLine.Value,
                SourceColumn: Math.Max(0, (_currentSourceColumn ?? 1) - 1)));
        }

        _output.Append(new string(' ', _indentLevel * 4));
        _output.AppendLine(text);
        _generatedLine++;
    }

    private void EmitLineWithSource(Node? node, string text)
    {
        WithSource(node, () => EmitLine(text));
    }

    private void WithSource(Node? node, Action action)
    {
        var previousLine = _currentSourceLine;
        var previousColumn = _currentSourceColumn;
        if (node != null && node.Line > 0)
        {
            _currentSourceLine = node.Line;
            _currentSourceColumn = node.Column <= 0 ? 1 : node.Column;
        }

        action();
        _currentSourceLine = previousLine;
        _currentSourceColumn = previousColumn;
    }

    private string? BuildSourceMapJson(string? sourceFilePath, string? sourceContent, string? generatedFileName)
    {
        var sourceName = string.IsNullOrWhiteSpace(sourceFilePath)
            ? "source.malda"
            : sourceFilePath.Replace('\\', '/');
        var fileName = string.IsNullOrWhiteSpace(generatedFileName) ? "program.js" : generatedFileName;

        var mapObject = new
        {
            version = 3,
            file = fileName,
            sources = new[] { sourceName },
            sourcesContent = sourceContent != null ? new[] { sourceContent } : Array.Empty<string>(),
            names = Array.Empty<string>(),
            mappings = BuildMappingsField()
        };

        return JsonSerializer.Serialize(mapObject);
    }

    private string BuildMappingsField()
    {
        var mappingsByLine = _mappings
            .GroupBy(m => m.GeneratedLine)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.GeneratedColumn).ToList());

        var lineCount = _generatedLine;
        var builder = new StringBuilder();
        var previousSourceIndex = 0;
        var previousSourceLine = 0;
        var previousSourceColumn = 0;

        for (int line = 1; line <= lineCount; line++)
        {
            if (line > 1)
            {
                builder.Append(';');
            }

            if (!mappingsByLine.TryGetValue(line, out var lineMappings))
            {
                continue;
            }

            var previousGeneratedColumn = 0;
            for (int i = 0; i < lineMappings.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                var mapping = lineMappings[i];
                builder.Append(EncodeVlq(mapping.GeneratedColumn - previousGeneratedColumn));
                previousGeneratedColumn = mapping.GeneratedColumn;

                // single source file only
                builder.Append(EncodeVlq(0 - previousSourceIndex));
                previousSourceIndex = 0;

                var zeroBasedSourceLine = mapping.SourceLine - 1;
                builder.Append(EncodeVlq(zeroBasedSourceLine - previousSourceLine));
                previousSourceLine = zeroBasedSourceLine;

                builder.Append(EncodeVlq(mapping.SourceColumn - previousSourceColumn));
                previousSourceColumn = mapping.SourceColumn;
            }
        }

        return builder.ToString();
    }

    private static string EncodeVlq(int value)
    {
        var vlq = ToVlqSigned(value);
        var output = new StringBuilder();

        do
        {
            var digit = vlq & 31;
            vlq >>= 5;
            if (vlq > 0)
            {
                digit |= 32;
            }

            output.Append(Base64Digit(digit));
        } while (vlq > 0);

        return output.ToString();
    }

    private static int ToVlqSigned(int value)
    {
        return value < 0 ? ((-value) << 1) + 1 : (value << 1);
    }

    private static char Base64Digit(int value)
    {
        const string digits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        return digits[value];
    }

    private readonly record struct SourceMappingEntry(
        int GeneratedLine,
        int GeneratedColumn,
        int SourceLine,
        int SourceColumn);
}
