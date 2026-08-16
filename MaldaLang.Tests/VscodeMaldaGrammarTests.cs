// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Keeps the VS Code TextMate grammar keyword list aligned with <c>Lexer.cs</c>.
/// </summary>
public class VscodeMaldaGrammarTests
{
    [Fact]
    public void PackageJson_RegistersMaldaTextMateGrammar()
    {
        var packagePath = PlanningPaths.ResolveRepoPath("vscode-malda", "package.json");
        using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
        var grammars = document.RootElement.GetProperty("contributes").GetProperty("grammars");
        Assert.Contains(grammars.EnumerateArray(), grammar =>
            grammar.GetProperty("language").GetString() == "malda" &&
            grammar.GetProperty("scopeName").GetString() == "source.malda" &&
            grammar.GetProperty("path").GetString() == "./syntaxes/malda.tmLanguage.json");
    }

    [Fact]
    public void TextMateGrammar_ContainsEveryLexerKeyword()
    {
        var grammarPath = PlanningPaths.ResolveRepoPath("vscode-malda", "syntaxes", "malda.tmLanguage.json");
        var grammar = File.ReadAllText(grammarPath);
        var keywords = LexerKeywords();
        var missing = keywords
            .Where(keyword => !Regex.IsMatch(grammar, $@"\b{Regex.Escape(keyword)}\b"))
            .OrderBy(keyword => keyword, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "vscode-malda/syntaxes/malda.tmLanguage.json is missing lexer keywords: " +
            string.Join(", ", missing));
    }

    private static HashSet<string> LexerKeywords()
    {
        var source = File.ReadAllText(PlanningPaths.ResolveRepoPath("MaldaLang", "Lexer.cs"));
        var start = source.IndexOf("Keywords = new", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate the Keywords map in Lexer.cs.");

        var end = source.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not locate the end of the Keywords map in Lexer.cs.");

        var body = source[start..end];
        return new HashSet<string>(
            Regex.Matches(body, @"\{\s*""(?<word>[A-Za-z_][A-Za-z0-9_]*)""\s*,").Select(m => m.Groups["word"].Value),
            StringComparer.Ordinal);
    }
}
