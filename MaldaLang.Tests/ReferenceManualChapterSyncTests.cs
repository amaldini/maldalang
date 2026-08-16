using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace MaldaLang.Tests;

public class ReferenceManualChapterSyncTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void ReferenceManual_ChapterTitlesMatchChaptersJson()
    {
        var manualDir = Path.Combine(RepoRoot, "ReferenceManual");
        var configPath = Path.Combine(manualDir, "chapters.json");
        Assert.True(File.Exists(configPath), $"Missing {configPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var num = 0;
        foreach (var chapter in doc.RootElement.GetProperty("chapters").EnumerateArray())
        {
            if (chapter.TryGetProperty("isHome", out var isHome) && isHome.GetBoolean())
                continue;

            num++;
            var file = chapter.GetProperty("file").GetString()!;
            var title = chapter.GetProperty("title").GetString()!;
            var expectedLabel = $"{num}. {title}";
            var path = Path.Combine(manualDir, file);
            var html = File.ReadAllText(path);

            Assert.Matches(new Regex($"<title>{Regex.Escape(expectedLabel)} - MALDA Reference Manual</title>"), html);
            Assert.Contains($"<h1>{expectedLabel}</h1>", html);
            Assert.Contains($"<span>/</span> <span>{expectedLabel}</span>", html);
            // The masthead carries the trademark symbol; the <title> deliberately does
            // not, so the labels above stay comparable with chapters.json.
            Assert.Contains("<h1>MALDA&trade; Reference Manual</h1>", html);
        }

        Assert.Equal(35, num);
    }

    [Fact]
    public void ItalianManual_ChapterTitlesMatchChaptersJson()
    {
        var manualDir = Path.Combine(RepoRoot, "ReferenceManual", "it");
        var configPath = Path.Combine(manualDir, "chapters.json");
        Assert.True(File.Exists(configPath), $"Missing {configPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var num = 0;
        foreach (var chapter in doc.RootElement.GetProperty("chapters").EnumerateArray())
        {
            if (chapter.TryGetProperty("isHome", out var isHome) && isHome.GetBoolean())
                continue;

            num++;
            var file = chapter.GetProperty("file").GetString()!;
            var title = chapter.GetProperty("title").GetString()!;
            var expectedLabel = $"{num}. {title}";
            var path = Path.Combine(manualDir, file);
            var html = File.ReadAllText(path);

            Assert.Matches(new Regex($"<title>{Regex.Escape(expectedLabel)} - Manuale di riferimento MALDA</title>"), html);
            Assert.Contains($"<h1>{expectedLabel}</h1>", html);
            Assert.Contains($"<span>/</span> <span>{expectedLabel}</span>", html);
            Assert.Contains("<h1>Manuale di riferimento MALDA&trade;</h1>", html);
        }

        Assert.Equal(35, num);
    }
}
