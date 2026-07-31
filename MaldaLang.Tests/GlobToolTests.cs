// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

public class GlobToolTests
{
    private static RuntimeValue CallGlob(
        string pattern,
        string dirPath = ".",
        int maxResults = GlobHelper.DefaultMaxResults,
        bool includeDirectories = false,
        string excludeDirs = "",
        string workingDirectory = "")
    {
        return BuiltInFunctions.CallBuiltIn(
            "glob",
            new List<RuntimeValue>
            {
                RuntimeValue.String(pattern),
                RuntimeValue.String(dirPath),
                RuntimeValue.Integer(maxResults),
                RuntimeValue.Boolean(includeDirectories),
                RuntimeValue.String(excludeDirs),
                RuntimeValue.String(workingDirectory)
            },
            null);
    }

    private static (List<string> Paths, int Count, bool Truncated) ParseGlobResult(RuntimeValue result)
    {
        var obj = result.AsObject();
        var count = obj.Get("count", null)!.AsInteger();
        var truncated = obj.Get("truncated", null)!.AsBoolean();
        var items = obj.Get("items", null)!.AsArray();
        var paths = new List<string>();
        foreach (var item in items)
        {
            paths.Add(item.AsObject().Get("path", null)!.AsString().Replace('\\', '/'));
        }
        return (paths, count, truncated);
    }

    [Fact]
    public void Glob_MatchesTxtFilesRecursively()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "malda-glob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var sub = Path.Combine(tempRoot, "src");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(tempRoot, "a.txt"), "a");
        File.WriteAllText(Path.Combine(sub, "b.txt"), "b");
        File.WriteAllText(Path.Combine(tempRoot, "skip.cs"), "// cs");

        try
        {
            var (paths, count, truncated) = ParseGlobResult(CallGlob("**/*.txt", tempRoot));
            Assert.False(truncated);
            Assert.Equal(2, count);
            Assert.Contains("a.txt", paths);
            Assert.Contains("src/b.txt", paths);
            Assert.DoesNotContain("skip.cs", paths);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Glob_ExcludesBinAndGitByDefault()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "malda-glob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "bin"));
        Directory.CreateDirectory(Path.Combine(tempRoot, ".git"));
        File.WriteAllText(Path.Combine(tempRoot, "ok.txt"), "ok");
        File.WriteAllText(Path.Combine(tempRoot, "bin", "hidden.txt"), "hidden");
        File.WriteAllText(Path.Combine(tempRoot, ".git", "config.txt"), "git");

        try
        {
            var (paths, _, _) = ParseGlobResult(CallGlob("**/*.txt", tempRoot));
            Assert.Single(paths);
            Assert.Contains("ok.txt", paths);
            Assert.DoesNotContain(paths, p => p.Contains("bin/"));
            Assert.DoesNotContain(paths, p => p.Contains(".git/"));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Glob_TruncatesWhenMaxResultsExceeded()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "malda-glob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        for (var i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(tempRoot, $"f{i}.txt"), "x");

        try
        {
            var (_, count, truncated) = ParseGlobResult(CallGlob("*.txt", tempRoot, maxResults: 2));
            Assert.True(truncated);
            Assert.Equal(2, count);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Glob_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), "malda-glob-missing-" + Guid.NewGuid().ToString("N"));
        var (paths, count, truncated) = ParseGlobResult(CallGlob("**/*", missing));
        Assert.Empty(paths);
        Assert.Equal(0, count);
        Assert.False(truncated);
    }

    [Fact]
    public void Glob_ReturnsPathsRelativeToWorkingDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "malda-glob-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "proj");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "app.txt"), "app");

        try
        {
            var (paths, count, _) = ParseGlobResult(CallGlob("**/*.txt", ".", workingDirectory: projectDir));
            Assert.Equal(1, count);
            Assert.Single(paths);
            Assert.Equal("app.txt", paths[0]);
            Assert.DoesNotContain(":", paths[0]);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
