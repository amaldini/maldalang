// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Keeps the IDE AI language pack (embedded from docs/llm) aligned with the on-disk pack.
/// </summary>
public class IdeLanguagePackEmbedGuardTests
{
    private static string LlmDir => PlanningPaths.ResolveRepoPath("docs", "llm");

    private static IReadOnlyList<string> OnDiskRelativePaths()
    {
        var root = LlmDir;
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [Fact]
    public void EveryDocsLlmFile_IsEmbeddedWithStableLogicalName()
    {
        var onDisk = OnDiskRelativePaths();
        Assert.NotEmpty(onDisk);

        var embedded = MALDALanguageContextService.EnumeratePackRelativePaths();
        var missing = onDisk.Where(path => !embedded.Contains(path, StringComparer.Ordinal)).ToList();
        var extra = embedded.Where(path => !onDisk.Contains(path, StringComparer.Ordinal)).ToList();

        Assert.True(
            missing.Count == 0,
            "Embedded language pack is missing docs/llm files: " + string.Join(", ", missing));
        Assert.True(
            extra.Count == 0,
            "Embedded language pack has unexpected files not on disk under docs/llm: " + string.Join(", ", extra));
    }

    [Fact]
    public void MaterializeLanguagePack_WritesCoreFilesAndLiveDecorators()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "IdeLlmPack_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var service = new MALDALanguageContextService();
            var llmDir = service.MaterializeLanguagePack(tempDir);

            Assert.Equal(Path.Combine(tempDir, "llm"), llmDir);
            Assert.True(File.Exists(Path.Combine(llmDir, "malda-syntax.md")));
            Assert.True(File.Exists(Path.Combine(llmDir, "malda-gotchas.md")));
            Assert.True(File.Exists(Path.Combine(llmDir, "malda-builtins.tsv")));
            Assert.True(File.Exists(Path.Combine(llmDir, "DECORATORS.md")));
            Assert.True(File.Exists(Path.Combine(llmDir, "INDEX.md")));

            var decorators = File.ReadAllText(Path.Combine(llmDir, "DECORATORS.md"));
            Assert.Contains("@GET", decorators, StringComparison.Ordinal);
            Assert.Contains("@Tool", decorators, StringComparison.Ordinal);

            var syntaxOnDisk = File.ReadAllText(Path.Combine(LlmDir, "malda-syntax.md"));
            var syntaxMaterialized = File.ReadAllText(Path.Combine(llmDir, "malda-syntax.md"));
            Assert.Equal(syntaxOnDisk, syntaxMaterialized);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    [Fact]
    public void GetInlineBootContext_IncludesSyntaxAndGotchas()
    {
        var boot = new MALDALanguageContextService().GetInlineBootContext();
        Assert.Contains("malda-syntax.md", boot, StringComparison.Ordinal);
        Assert.Contains("malda-gotchas.md", boot, StringComparison.Ordinal);
        Assert.Contains(File.ReadAllText(Path.Combine(LlmDir, "malda-syntax.md")).Substring(0, 40), boot);
    }
}
