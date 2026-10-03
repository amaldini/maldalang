// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using System.Text.RegularExpressions;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Sung lines from scripts/showcase/song-prompt.md, aligned to playlist cues.
/// Dream lines stay whole. Catalog and manual lines split into sentences so
/// each example or page can show one line. The manual refrain is the
/// multi-agent page; a page with no sentence stays blank.
/// </summary>
public sealed class ShowcaseLyrics
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ShowcaseLyrics Empty { get; } = new();

    public string Intro { get; private init; } = "";
    public IReadOnlyList<string> CatalogLines { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> ManualLines { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<string> DreamLines { get; private init; } = Array.Empty<string>();

    public IReadOnlyList<string> CatalogSentences => Sentences(CatalogLines);
    public IReadOnlyList<string> ManualSentences => Sentences(ManualLines);

    public static ShowcaseLyrics Load(string directory)
    {
        var path = Path.Combine(directory, "song-prompt.md");
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return Empty;
        }
    }

    public static ShowcaseLyrics Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return new ShowcaseLyrics
        {
            Intro = First(SungLines(lines, "[Intro")),
            CatalogLines = SungLines(lines, "[Catalog"),
            ManualLines = SungLines(lines, "[Manual"),
            DreamLines = SungLines(lines, "[Dream")
        };
    }

    public string BrowseLyric(int index, int browseCount)
    {
        var sentences = CatalogSentences;
        var source = sentences.Count == browseCount ? sentences
            : CatalogLines.Count == browseCount ? CatalogLines
            : sentences;
        return At(source, index);
    }

    public string SceneLyric(int index) => At(DreamLines, index);

    public string ManualLyric(int index, IReadOnlyList<string> captions)
    {
        if (index < 0 || index >= captions.Count)
        {
            return "";
        }

        var sentences = ManualSentences;
        if (sentences.Count == captions.Count)
        {
            return sentences[index];
        }

        var refrainIndex = IndexOf(sentences, "Many agents");
        var refrainPage = IndexOf(captions, "Multi-agent");
        if (refrainIndex < 0 || refrainPage < 0 || sentences.Count != captions.Count - 1)
        {
            return At(sentences, index);
        }

        if (index == refrainPage)
        {
            return sentences[refrainIndex];
        }

        if (index < refrainPage)
        {
            return index < refrainIndex ? sentences[index] : "";
        }

        var after = refrainIndex + 1 + (index - refrainPage - 1);
        return At(sentences, after);
    }

    private static List<string> SungLines(string[] lines, string sectionPrefix)
    {
        var inSection = false;
        var lyrics = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith(sectionPrefix, StringComparison.Ordinal))
            {
                inSection = true;
                lyrics.Clear();
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                if (line.StartsWith("[Female", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                break;
            }

            if (line.Length == 0)
            {
                if (lyrics.Count > 0)
                {
                    break;
                }

                continue;
            }

            lyrics.Add(line);
        }

        return lyrics;
    }

    private static List<string> Sentences(IReadOnlyList<string> lines)
    {
        var sentences = new List<string>();
        foreach (var line in lines)
        {
            foreach (var part in SentenceSplit.Split(line))
            {
                var sentence = part.Trim();
                if (sentence.Length > 0)
                {
                    sentences.Add(sentence);
                }
            }
        }

        return sentences;
    }

    private static string First(IReadOnlyList<string> lines) => lines.Count == 0 ? "" : lines[0];

    private static string At(IReadOnlyList<string> lines, int index) =>
        index >= 0 && index < lines.Count ? lines[index] : "";

    private static int IndexOf(IReadOnlyList<string> lines, string fragment)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
