// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// DT7 helper: same <c>.malda</c>, same exit, same normalized stdout on interpret and
/// C# transpile. JS is out of scope.
/// </summary>
public static class InterpretTranspilePair
{
    public static string Normalize(string text) => text.Replace("\r", "").Trim();

    public static void AssertSameFromSource(string source, string label)
    {
        var interpreted = TestBase.CaptureInterpretAsync(source).GetAwaiter().GetResult();
        TranspiledTestRunner.RunResult compiled;
        try
        {
            compiled = TranspiledTestRunner.CompileAndRunFromSource(source);
        }
        catch (Exception ex)
        {
            throw new Exception($"{label}: transpile failed.{Environment.NewLine}{ex.Message}", ex);
        }

        AssertPair(interpreted, compiled, label);
    }

    /// <summary>
    /// Interpret from the repo path (so <c>import</c> resolves). Transpile from a temp
    /// copy of sibling <c>.malda</c> files so Examples/ is not written with an <c>.exe</c>.
    /// </summary>
    public static void AssertSameFromFile(string sourcePath, string label)
    {
        Assert.True(File.Exists(sourcePath), $"Missing pair source: {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        var interpreted = TestBase.CaptureInterpretAsync(source, sourcePath).GetAwaiter().GetResult();

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_pair", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyMaldaSiblings(Path.GetDirectoryName(sourcePath)!, tempDir);
            var tempSource = Path.Combine(tempDir, Path.GetFileName(sourcePath));
            TranspiledTestRunner.RunResult compiled;
            try
            {
                compiled = TranspiledTestRunner.CompileAndRunFromFile(tempSource);
            }
            catch (Exception ex)
            {
                throw new Exception($"{label}: transpile failed.{FormatCompileHint(tempDir)}{Environment.NewLine}{ex.Message}", ex);
            }

            AssertPair(interpreted, compiled, label);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static void AssertPair(string interpreted, TranspiledTestRunner.RunResult compiled, string label)
    {
        Assert.True(compiled.ExitCode == 0, $"{label}: transpiled exit {compiled.ExitCode}.{FormatStdErr(compiled)}");
        Assert.Equal(Normalize(interpreted), Normalize(compiled.StdOut));
    }

    private static void CopyMaldaSiblings(string sourceDir, string destDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*.malda"))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
    }

    private static string FormatCompileHint(string errorDir)
    {
        var buildErrorsPath = Path.Combine(errorDir, "build_errors.txt");
        var generatedPath = Path.Combine(errorDir, "GeneratedProgram.cs");
        var hint = string.Empty;
        if (File.Exists(buildErrorsPath))
            hint += Environment.NewLine + "build_errors.txt: " + Path.GetFullPath(buildErrorsPath);
        if (File.Exists(generatedPath))
            hint += Environment.NewLine + "GeneratedProgram.cs: " + Path.GetFullPath(generatedPath);
        return hint;
    }

    private static string FormatStdErr(TranspiledTestRunner.RunResult compiled)
    {
        return string.IsNullOrEmpty(compiled.StdErr)
            ? string.Empty
            : Environment.NewLine + compiled.StdErr;
    }
}
