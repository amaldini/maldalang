// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class WorkspaceFileSetTests
{
    [Fact]
    public void GetDocumentsFor_SiblingMaldaFiles_IncludesDiskAndOpenBuffers()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("lib.malda", "function sharedHelper() {\n    return 1;\n}\n"),
            ("main.malda", "var result = sharedHelper();\n"));

        var files = new WorkspaceFileSet();
        var mainPath = workspace.GetPath("main.malda");
        files.SetOpenDocument(mainPath, File.ReadAllText(mainPath));

        var documents = files.GetDocumentsFor(mainPath);

        Assert.Contains(documents, document => document.SourceKey.EndsWith("lib.malda", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(documents, document => document.SourceKey.EndsWith("main.malda", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetDocumentsFor_OpenBufferOverridesDisk()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("main.malda", "var stale = 1;\n"));

        var files = new WorkspaceFileSet();
        var mainPath = workspace.GetPath("main.malda");
        files.SetOpenDocument(mainPath, "var fresh = 2;\n");

        var documents = files.GetDocumentsFor(mainPath);
        var main = Assert.Single(documents, document => document.SourceKey.EndsWith("main.malda", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("var fresh = 2;\n", main.Text);
    }

    [Fact]
    public void GetWorkspaceDefinition_UsesFileSetDocuments()
    {
        using var workspace = new TemporaryMaldaWorkspace(
            ("lib.malda", "function sharedHelper() {\n    return 1;\n}\n"),
            ("main.malda", "var result = sharedHelper();\n"));

        var files = new WorkspaceFileSet();
        var mainPath = workspace.GetPath("main.malda");
        var libPath = workspace.GetPath("lib.malda");
        files.SetOpenDocument(mainPath, File.ReadAllText(mainPath));

        var service = new SymbolNavigationService();
        var documents = files.GetDocumentsFor(mainPath);
        var mainText = File.ReadAllText(mainPath);
        var definition = service.GetWorkspaceDefinition(documents, mainText, 0, 14, mainPath);

        Assert.NotNull(definition);
        Assert.Equal("sharedHelper", definition!.Name);
        Assert.EndsWith("lib.malda", definition.SourceKey, StringComparison.OrdinalIgnoreCase);
    }
}

public class MaldaIndentFormatterTests
{
    [Fact]
    public void FormatDocument_IndentsBlockBody()
    {
        var source = "function f() {\nvar x = 1;\n}\n";
        var formatted = MaldaIndentFormatter.FormatDocument(source);
        Assert.Contains("    var x = 1;", formatted);
    }

    [Fact]
    public void ApplyEdits_ReplacesFromTheEnd()
    {
        var source = "abc def";
        var updated = MaldaIndentFormatter.ApplyEdits(source, new[]
        {
            new MaldaLang.IDE.Models.TextEditInfo
            {
                Span = new MaldaLang.IDE.Models.TextSpanInfo { Line = 0, Column = 0, Length = 3 },
                NewText = "xyz"
            },
            new MaldaLang.IDE.Models.TextEditInfo
            {
                Span = new MaldaLang.IDE.Models.TextSpanInfo { Line = 0, Column = 4, Length = 3 },
                NewText = "ghi"
            }
        });

        Assert.Equal("xyz ghi", updated);
    }
}

internal sealed class TemporaryMaldaWorkspace : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "malda-workspace-tests", Guid.NewGuid().ToString("N"));

    public TemporaryMaldaWorkspace(params (string RelativePath, string Text)[] files)
    {
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(Path.Combine(_rootPath, ".git"));
        foreach (var (relativePath, text) in files)
        {
            var fullPath = GetPath(relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, text);
        }
    }

    public string GetPath(string relativePath) => Path.Combine(_rootPath, relativePath);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test workspaces.
        }
    }
}
