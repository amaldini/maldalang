// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.RegularExpressions;

namespace MaldaLang.DesktopIDE.Services;

public sealed class VirtualDocumentSection
{
    public required string SectionId { get; init; }
    public required string Title { get; set; }
    public required int Order { get; init; }
    public required int StartLine { get; set; }
    public required int EndLine { get; set; }
    public required string Content { get; set; }
}

public sealed class VirtualDocumentSegmentationService
{
    private static readonly Regex SectionSeparatorRegex = new(
        @"^//\s*@malda-section\b(?<tagTitle>.*)$",
        RegexOptions.Compiled);

    public IReadOnlyList<VirtualDocumentSection> Segment(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return new[]
            {
                new VirtualDocumentSection
                {
                    SectionId = "sec_001",
                    Title = "main",
                    Order = 0,
                    StartLine = 0,
                    EndLine = 0,
                    Content = string.Empty
                }
            };
        }

        var lines = SplitLines(source);
        var starts = FindSectionStarts(lines);
        if (starts.Count == 0 || starts[0] != 0)
        {
            starts.Insert(0, 0);
        }

        var sections = new List<VirtualDocumentSection>();
        for (int i = 0; i < starts.Count; i++)
        {
            var startLine = starts[i];
            var endLine = i + 1 < starts.Count ? starts[i + 1] : lines.Count;
            var content = string.Concat(lines.Skip(startLine).Take(endLine - startLine));
            sections.Add(new VirtualDocumentSection
            {
                SectionId = $"sec_{i + 1:D3}",
                Order = i,
                StartLine = startLine,
                EndLine = Math.Max(startLine, endLine - 1),
                Content = content,
                Title = BuildSectionTitle(lines, startLine, endLine, i)
            });
        }

        ApplyDuplicateTitleSuffixes(sections);
        return sections;
    }

    public string Recompose(IEnumerable<VirtualDocumentSection> sections)
    {
        var ordered = sections.OrderBy(section => section.Order).ToList();
        return string.Concat(ordered.Select(section => section.Content));
    }

    public string RecomposePreservingClosedSections(IEnumerable<VirtualDocumentSection> openSections, string existingSource)
    {
        var open = openSections.OrderBy(section => section.Order).ToList();
        if (open.Count == 0)
        {
            return existingSource;
        }

        if (string.IsNullOrEmpty(existingSource))
        {
            return Recompose(open);
        }

        var existing = Segment(existingSource).ToList();
        var openById = open.ToDictionary(section => section.SectionId, StringComparer.OrdinalIgnoreCase);
        var replacedAny = false;
        for (var i = 0; i < existing.Count; i++)
        {
            if (!openById.TryGetValue(existing[i].SectionId, out var replacement))
            {
                continue;
            }

            existing[i].Content = replacement.Content;
            existing[i].Title = replacement.Title;
            replacedAny = true;
        }

        return replacedAny ? Recompose(existing) : Recompose(open);
    }

    public void RecalculateLineSpans(IList<VirtualDocumentSection> sections)
    {
        var ordered = sections.OrderBy(section => section.Order).ToList();
        var line = 0;
        foreach (var section in ordered)
        {
            section.StartLine = line;
            var count = CountLines(section.Content);
            section.EndLine = line + Math.Max(0, count - 1);
            line += count;
        }
    }

    private static List<int> FindSectionStarts(IReadOnlyList<string> lines)
    {
        var starts = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (!IsSectionSeparator(lines[i]))
            {
                continue;
            }

            if (!starts.Contains(i))
            {
                starts.Add(i);
            }
        }

        starts.Sort();
        return starts;
    }

    private static bool IsSectionSeparator(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Length > 0 && char.IsWhiteSpace(line[0]))
        {
            return false;
        }

        return SectionSeparatorRegex.IsMatch(line.TrimEnd());
    }

    private static string BuildSectionTitle(IReadOnlyList<string> lines, int startLine, int endLine, int index)
    {
        for (int i = startLine; i < endLine; i++)
        {
            var trimmed = lines[i].Trim();
            var separatorMatch = SectionSeparatorRegex.Match(trimmed);
            if (separatorMatch.Success)
            {
                var title = separatorMatch.Groups["tagTitle"].Value.Trim();
                return string.IsNullOrWhiteSpace(title) ? $"section {index + 1}" : title;
            }

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("@", StringComparison.Ordinal))
            {
                continue;
            }

            break;
        }

        return $"section {index + 1}";
    }

    private static void ApplyDuplicateTitleSuffixes(IList<VirtualDocumentSection> sections)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            var title = string.IsNullOrWhiteSpace(section.Title) ? $"section {section.Order + 1}" : section.Title;
            counts.TryGetValue(title, out var count);
            count++;
            counts[title] = count;

            section.Title = count == 1 ? title : $"{title} ({count})";
        }
    }

    private static List<string> SplitLines(string source)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '\n')
            {
                continue;
            }

            lines.Add(source[start..(i + 1)]);
            start = i + 1;
        }

        if (start < source.Length)
        {
            lines.Add(source[start..]);
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        return lines;
    }

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 1;
        }

        var lines = 1;
        foreach (var ch in content)
        {
            if (ch == '\n')
            {
                lines++;
            }
        }

        return lines;
    }
}
