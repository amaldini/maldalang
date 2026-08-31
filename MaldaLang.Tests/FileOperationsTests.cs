// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.IO;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class FileOperationsTests : TestBase
{
    private readonly string _testDirectory;

    public FileOperationsTests()
    {
        _testDirectory = CreateTempDirectory("FileOperationsTests_");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            SafeDeleteDirectory(_testDirectory);
        base.Dispose(disposing);
    }

    private string WriteTestFile(string name, string content)
    {
        var path = Path.Combine(_testDirectory, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReplaceInFile_ExactMatch_ReplacesOnce()
    {
        var path = WriteTestFile("exact.txt", "alpha beta gamma");

        var ok = FileOperations.ReplaceInFile(path, "beta", "BETA", 3);

        Assert.True(ok);
        Assert.Equal("alpha BETA gamma", File.ReadAllText(path));
    }

    [Fact]
    public void ReplaceInFile_MultipleExactMatches_ReturnsFalse()
    {
        var path = WriteTestFile("multi.txt", "foo x bar\nfoo x bar\n");

        var ok = FileOperations.ReplaceInFile(path, "x", "Y", 3);

        Assert.False(ok);
        Assert.Equal("foo x bar\nfoo x bar\n", File.ReadAllText(path));
    }

    [Fact]
    public void ReplaceInFile_FuzzyWhitespace_SingleMatch_Works()
    {
        var path = WriteTestFile("fuzzy.txt", "if  (x)  {\r\n    return  1;\r\n}");

        var ok = FileOperations.ReplaceInFile(path, "if (x) {\n    return 1;\n}", "if (x) {\n    return 2;\n}", 3);

        Assert.True(ok);
        Assert.Contains("return 2", File.ReadAllText(path));
        Assert.DoesNotContain("return  1", File.ReadAllText(path));
    }

    [Fact]
    public void ReplaceInFile_FuzzyMultipleMatchesWithSameContext_ReturnsFalse()
    {
        var path = WriteTestFile("fuzzy-multi.txt", "foo x bar\nfoo x bar\n");

        var ok = FileOperations.ReplaceInFile(path, "foo  x  bar", "replaced", 3);

        Assert.False(ok);
        Assert.Equal("foo x bar\nfoo x bar\n", File.ReadAllText(path));
    }

    [Fact]
    public void ReplaceInFile_ContextLines_DisambiguatesFuzzyMatch()
    {
        var content = "dup x here\n" +
                      "dup x here\n" +
                      "uniq x here\n";
        var path = WriteTestFile("context.txt", content);

        var ok = FileOperations.ReplaceInFile(path, "x", "X", 1);

        Assert.True(ok);
        var result = File.ReadAllText(path);
        Assert.Equal("dup x here\n" +
                     "dup x here\n" +
                     "uniq X here\n", result);
    }

    [Fact]
    public void ReplaceInFile_NoMatch_ReturnsFalse()
    {
        var path = WriteTestFile("missing.txt", "hello world");

        var ok = FileOperations.ReplaceInFile(path, "not-found", "nope", 3);

        Assert.False(ok);
        Assert.Equal("hello world", File.ReadAllText(path));
    }

    [Fact]
    public void ReplaceInFile_EmptyOldText_ReturnsFalse()
    {
        var path = WriteTestFile("empty-old.txt", "hello");

        var ok = FileOperations.ReplaceInFile(path, "", "x", 3);

        Assert.False(ok);
        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void EditFile_AllApplied_SuccessTrue()
    {
        var path = WriteTestFile("edit-all.txt", "a=1\nb=2\n");

        var result = FileOperations.EditFile(path, new List<FileOperations.FileEdit>
        {
            new() { OldText = "a=1", NewText = "a=10" },
            new() { OldText = "b=2", NewText = "b=20" }
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Applied);
        Assert.Equal("a=10\nb=20\n", File.ReadAllText(path));
    }

    [Fact]
    public void EditFile_PartialApplied_RollsBackAndReportsFailure()
    {
        var path = WriteTestFile("edit-partial.txt", "a=1\nb=2\n");

        var result = FileOperations.EditFile(path, new List<FileOperations.FileEdit>
        {
            new() { OldText = "a=1", NewText = "a=10" },
            new() { OldText = "missing", NewText = "nope" }
        });

        Assert.False(result.Success);
        Assert.Equal(0, result.Applied);
        Assert.Equal(2, result.FailedEditIndex);
        Assert.Equal(2, result.TotalEdits);
        Assert.NotNull(result.Error);
        Assert.Contains("Edit 2/2 failed", result.Error);
        Assert.Equal("a=1\nb=2\n", File.ReadAllText(path));
    }

    [Fact]
    public void EditFile_FailureOnFirstEdit_LeavesFileUnchanged()
    {
        var path = WriteTestFile("edit-first-fail.txt", "unchanged\n");

        var result = FileOperations.EditFile(path, new List<FileOperations.FileEdit>
        {
            new() { OldText = "not-here", NewText = "nope" },
            new() { OldText = "unchanged", NewText = "changed" }
        });

        Assert.False(result.Success);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.FailedEditIndex);
        Assert.Equal("unchanged\n", File.ReadAllText(path));
    }

    [Fact]
    public void EditFile_EmptyEdits_SuccessTrueAppliedZero()
    {
        var path = WriteTestFile("edit-empty.txt", "unchanged");

        var result = FileOperations.EditFile(path, new List<FileOperations.FileEdit>());

        Assert.True(result.Success);
        Assert.Equal(0, result.Applied);
        Assert.Equal("unchanged", File.ReadAllText(path));
    }

    [Fact]
    public void IsPathAllowed_BlocksPrefixBypassSiblingDirectory()
    {
        var workdir = Path.Combine(_testDirectory, "snake-demo");
        Directory.CreateDirectory(workdir);

        var sibling = Path.Combine(_testDirectory, "snake-demo-evil");
        Directory.CreateDirectory(sibling);
        var evilFile = Path.Combine(sibling, "secret.txt");
        File.WriteAllText(evilFile, "nope");

        var tool = new ToolInstance();
        tool.WorkingDirectory = workdir;

        Assert.True(tool.IsPathAllowed("local.txt"));
        Assert.False(tool.IsPathAllowed(evilFile));
        Assert.False(tool.IsPathAllowed("../snake-demo-evil/secret.txt"));
    }

    [Fact]
    public void NormalizePathForWorkingDirectory_ConvertsAbsolutePathUnderWorkdir()
    {
        var workdir = Path.Combine(_testDirectory, "ralph-work");
        Directory.CreateDirectory(workdir);
        var file = Path.Combine(workdir, "PRD.md");
        File.WriteAllText(file, "checklist");

        var tool = new ToolInstance { WorkingDirectory = workdir };

        Assert.Equal("PRD.md", tool.NormalizePathForWorkingDirectory(file));
        Assert.Equal("PRD.md", tool.NormalizePathForWorkingDirectory("PRD.md"));
    }

    [Fact]
    public void NormalizePathForWorkingDirectory_RejectsPathOutsideWorkdir()
    {
        var workdir = Path.Combine(_testDirectory, "ralph-work");
        Directory.CreateDirectory(workdir);
        var outside = Path.Combine(_testDirectory, "other-repo", "snake.html");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "<html></html>");

        var tool = new ToolInstance { WorkingDirectory = workdir };

        Assert.Null(tool.NormalizePathForWorkingDirectory(outside));
    }

    [Fact]
    public void IsPathAllowed_AllowsCurrentDirectoryDot()
    {
        var workdir = Path.Combine(_testDirectory, "ralph-work");
        Directory.CreateDirectory(workdir);

        var tool = new ToolInstance { WorkingDirectory = workdir };

        Assert.True(tool.IsPathAllowed("."));
        Assert.Equal(".", tool.NormalizePathForWorkingDirectory("."));
    }

    [Fact]
    public void IsPathUnder_AllowsDescendantAndBlocksTraversal()
    {
        var brain = Path.Combine(_testDirectory, "secondbrain");
        var notes = Path.Combine(brain, "notes");
        Directory.CreateDirectory(notes);
        var note = Path.Combine(notes, "ok.md");
        File.WriteAllText(note, "body");

        var outside = Path.Combine(_testDirectory, "secret.txt");
        File.WriteAllText(outside, "nope");

        Assert.True(BuiltInFunctions.IsPathUnderRoot(brain, note));
        Assert.True(BuiltInFunctions.IsPathUnderRoot(brain, Path.Combine(brain, "notes", "..", "notes", "ok.md")));
        Assert.False(BuiltInFunctions.IsPathUnderRoot(brain, outside));
        Assert.False(BuiltInFunctions.IsPathUnderRoot(brain, Path.Combine(brain, "..", "secret.txt")));

        var sibling = Path.Combine(_testDirectory, "secondbrain-evil");
        Directory.CreateDirectory(sibling);
        Assert.False(BuiltInFunctions.IsPathUnderRoot(brain, Path.Combine(sibling, "x.md")));

        var viaBuiltin = BuiltInFunctions.CallBuiltIn(
            "isPathUnder",
            new List<RuntimeValue>
            {
                RuntimeValue.String(brain),
                RuntimeValue.String(Path.Combine(brain, "..", "secret.txt"))
            },
            null);
        Assert.False(viaBuiltin.AsBoolean());
    }

    [Fact]
    public void CopyFile_OverwritesDestination()
    {
        var src = WriteTestFile("copy-src.bin", "hello-bytes");
        var dest = Path.Combine(_testDirectory, "copy-dest.bin");
        File.WriteAllText(dest, "old");

        var ok = BuiltInFunctions.CallBuiltIn(
            "copyFile",
            new List<RuntimeValue>
            {
                RuntimeValue.String(src),
                RuntimeValue.String(dest)
            },
            null);
        Assert.True(ok.AsBoolean());
        Assert.Equal("hello-bytes", File.ReadAllText(dest));
    }

    [Fact]
    public void CopyFile_MissingSource_ReturnsFalse()
    {
        var dest = Path.Combine(_testDirectory, "copy-missing-dest.bin");
        var ok = BuiltInFunctions.CallBuiltIn(
            "copyFile",
            new List<RuntimeValue>
            {
                RuntimeValue.String(Path.Combine(_testDirectory, "no-such-src.bin")),
                RuntimeValue.String(dest)
            },
            null);
        Assert.False(ok.AsBoolean());
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void CopyFile_RejectsEmbedDestination()
    {
        var src = WriteTestFile("copy-embed-src.txt", "x");
        var ex = Assert.Throws<Exception>(() => BuiltInFunctions.CallBuiltIn(
            "copyFile",
            new List<RuntimeValue>
            {
                RuntimeValue.String(src),
                RuntimeValue.String("embed:fixture/hello.txt")
            },
            null));
        Assert.Contains("cannot write", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopyFile_RejectsEmbedSource()
    {
        var dest = Path.Combine(_testDirectory, "from-embed.txt");
        var ex = Assert.Throws<Exception>(() => BuiltInFunctions.CallBuiltIn(
            "copyFile",
            new List<RuntimeValue>
            {
                RuntimeValue.String("embed:fixture/hello.txt"),
                RuntimeValue.String(dest)
            },
            null));
        Assert.Contains("cannot read from embedded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
