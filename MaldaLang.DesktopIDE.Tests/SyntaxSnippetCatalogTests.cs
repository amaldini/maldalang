// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class SyntaxSnippetCatalogTests
{
    [Fact]
    public void CreateDefault_HasUniqueNonEmptyIds()
    {
        var snippets = SyntaxSnippetCatalog.CreateDefault();
        Assert.NotEmpty(snippets);

        var duplicates = snippets
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicates.Count == 0, "Duplicate snippet ids: " + string.Join(", ", duplicates));

        foreach (var snippet in snippets)
        {
            Assert.False(string.IsNullOrWhiteSpace(snippet.Id), "Snippet id is empty");
            Assert.False(string.IsNullOrWhiteSpace(snippet.Category), snippet.Id);
            Assert.False(string.IsNullOrWhiteSpace(snippet.Label), snippet.Id);
            Assert.False(string.IsNullOrWhiteSpace(snippet.Description), snippet.Id);
            Assert.False(string.IsNullOrWhiteSpace(snippet.TemplateText), snippet.Id);
            Assert.DoesNotContain(SyntaxSnippetCatalog.CaretMarker, snippet.Preview, StringComparison.Ordinal);
            Assert.Contains(SyntaxSnippetCatalog.CaretMarker, snippet.TemplateText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CreateDefault_CoversLandedLanguageConstructs()
    {
        var ids = SyntaxSnippetCatalog.CreateDefault()
            .Select(s => s.Id)
            .ToHashSet(StringComparer.Ordinal);

        string[] required =
        [
            "schema",
            "type-sum",
            "validate",
            "api",
            "workflow",
            "workflow-approval",
            "import",
            "import-selective",
            "export",
            "prompt",
            "prompt-structured",
            "prompt-tools",
            "prompt-gather",
            "within-budget",
            "get-route",
            "post-route",
            "page-route",
            "component",
            "ui-tree",
            "client",
            "shader",
            "game-loop",
            "graph",
            "dict",
            "class-extends",
            "class-primary",
            "property",
            "try-finally",
            "throw",
            "defer",
            "const",
            "match-guard",
            "match-value",
            "null-safe",
            "tool",
            "pure-effects",
            "cap-read",
            "async-await",
            "input"
        ];

        var missing = required.Where(id => !ids.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "Syntax Helper is missing snippets: " + string.Join(", ", missing));
    }

    [Fact]
    public void CreateDefault_TemplatesParseAsMalda()
    {
        var failures = new List<string>();

        foreach (var snippet in SyntaxSnippetCatalog.CreateDefault())
        {
            var source = snippet.TemplateText.Replace(SyntaxSnippetCatalog.CaretMarker, "", StringComparison.Ordinal);
            if (snippet.Id == "include")
            {
                continue;
            }

            if (TryParse(source, out var error))
            {
                continue;
            }

            if (snippet.Id is "break" or "continue")
            {
                var loopWrapped = "function __snippet() {\n\twhile (true) {\n" + source + "\n\t}\n}\n";
                if (TryParse(loopWrapped, out error))
                {
                    continue;
                }
            }
            else
            {
                var wrapped = "function __snippet() {\n" + source + "\n}\n";
                if (TryParse(wrapped, out error))
                {
                    continue;
                }
            }

            failures.Add($"{snippet.Id}: {error}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool TryParse(string source, out string error)
    {
        var lexer = new Lexer(source, "snippet.malda");
        var tokens = lexer.Tokenize();
        var parser = new MaldaLang.Parser.Parser(tokens, "snippet.malda");
        parser.Parse();
        if (parser.Errors.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        error = string.Join("; ", parser.Errors.Select(e => e.Message));
        return false;
    }
}
