// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

public class GitAddPathTests
{
    [Fact]
    public void GitAdd_AcceptsRepoRootRelativePath_FromNestedWorkdir()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "malda-gitadd-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "Examples", "RalphWiggum", "snake-demo");
        Directory.CreateDirectory(projectDir);

        try
        {
            RunGit(tempRoot, "init");
            RunGit(tempRoot, "config", "user.email", "test@example.com");
            RunGit(tempRoot, "config", "user.name", "Test");

            var targetFile = Path.Combine(projectDir, "snake.html");
            File.WriteAllText(targetFile, "<html></html>");
            RunGit(tempRoot, "add", ".");
            RunGit(tempRoot, "commit", "-m", "init");

            File.WriteAllText(targetFile, "<html><body>snake</body></html>");

            var repoRootPath = "Examples/RalphWiggum/snake-demo";
            var result = BuiltInFunctions.CallBuiltIn(
                "gitAdd",
                new List<RuntimeValue>
                {
                    RuntimeValue.String(projectDir),
                    RuntimeValue.String(repoRootPath)
                },
                null);

            var message = result.AsString();
            Assert.StartsWith("Success", message);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GitAdd_AcceptsBasename_FromNestedWorkdir()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "malda-gitadd-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "Examples", "RalphWiggum", "snake-demo");
        Directory.CreateDirectory(projectDir);

        try
        {
            RunGit(tempRoot, "init");
            RunGit(tempRoot, "config", "user.email", "test@example.com");
            RunGit(tempRoot, "config", "user.name", "Test");

            var targetFile = Path.Combine(projectDir, "PRD.md");
            File.WriteAllText(targetFile, "# PRD");
            RunGit(tempRoot, "add", ".");
            RunGit(tempRoot, "commit", "-m", "init");

            File.WriteAllText(targetFile, "# PRD updated");

            var result = BuiltInFunctions.CallBuiltIn(
                "gitAdd",
                new List<RuntimeValue>
                {
                    RuntimeValue.String(projectDir),
                    RuntimeValue.String("PRD.md")
                },
                null);

            Assert.StartsWith("Success", result.AsString());
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Git on Windows may still hold handles under .git briefly after commands.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }
    }
}
