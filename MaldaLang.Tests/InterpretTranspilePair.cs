// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// DT7 / ship-contract helper: same <c>.malda</c>, same exit class, same normalized
/// stdout on interpret and C# transpile when both succeed. JS is out of scope.
/// Mixed success/failure is a contract failure. Both-nonzero is error identity
/// (optional shared token in interpret exception or transpile stderr).
/// </summary>
public static class InterpretTranspilePair
{
    public static string Normalize(string text) => text.Replace("\r", "").Trim();

    public static void AssertSameFromSource(string source, string label, int typedTranspileLevel = 1)
    {
        var interpreted = TestBase.CaptureInterpretOutcomeAsync(source).GetAwaiter().GetResult();
        TranspiledTestRunner.RunResult compiled;
        try
        {
            compiled = TranspiledTestRunner.CompileAndRunFromSource(
                source,
                includeUiHost: false,
                environmentVariables: null,
                commandLineArgs: null,
                profilingOptions: null,
                typedTranspileLevel: typedTranspileLevel);
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
    public static void AssertSameFromFile(string sourcePath, string label, int typedTranspileLevel = 1)
    {
        Assert.True(File.Exists(sourcePath), $"Missing pair source: {sourcePath}");
        var source = File.ReadAllText(sourcePath);
        var interpreted = TestBase.CaptureInterpretOutcomeAsync(source, sourcePath).GetAwaiter().GetResult();

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_pair", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyMaldaSiblings(Path.GetDirectoryName(sourcePath)!, tempDir);
            var tempSource = Path.Combine(tempDir, Path.GetFileName(sourcePath));
            TranspiledTestRunner.RunResult compiled;
            try
            {
                compiled = TranspiledTestRunner.CompileAndRunFromFile(
                    tempSource,
                    includeUiHost: false,
                    environmentVariables: null,
                    commandLineArgs: null,
                    profilingOptions: null,
                    typedTranspileLevel: typedTranspileLevel);
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

    /// <summary>
    /// Both backends must fail (nonzero exit). When <paramref name="token"/> is set,
    /// it must appear in the interpret exception or transpile stderr.
    /// </summary>
    public static void AssertSameFailureFromSource(string source, string label, string? token = null)
    {
        var interpreted = TestBase.CaptureInterpretOutcomeAsync(source).GetAwaiter().GetResult();
        TranspiledTestRunner.RunResult compiled;
        try
        {
            compiled = TranspiledTestRunner.CompileAndRunFromSource(source);
        }
        catch (Exception ex)
        {
            throw new Exception($"{label}: transpile failed.{Environment.NewLine}{ex.Message}", ex);
        }

        Assert.True(
            interpreted.ExitCode != 0 && compiled.ExitCode != 0,
            $"{label}: expected both to fail. interpret exit {interpreted.ExitCode} ({interpreted.Exception?.Message}); transpile exit {compiled.ExitCode}.{FormatStdErr(compiled)}");

        if (!string.IsNullOrEmpty(token))
        {
            var haystack = (interpreted.Exception?.Message ?? "") + "\n" + compiled.StdErr;
            Assert.True(
                haystack.Contains(token, StringComparison.Ordinal),
                $"{label}: expected error token '{token}' in interpret exception or transpile stderr.{Environment.NewLine}{haystack}");
        }
    }

    private static void AssertPair(TestBase.InterpretOutcome interpreted, TranspiledTestRunner.RunResult compiled, string label)
    {
        if (interpreted.ExitCode == 0 && compiled.ExitCode == 0)
        {
            Assert.Equal(Normalize(interpreted.StdOut), Normalize(compiled.StdOut));
            return;
        }

        if (interpreted.ExitCode != 0 && compiled.ExitCode != 0)
            return;

        Assert.True(
            false,
            $"{label}: interpret exit {interpreted.ExitCode} vs transpile exit {compiled.ExitCode}.{Environment.NewLine}"
            + $"interpret error: {interpreted.Exception?.Message}{FormatStdErr(compiled)}");
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
