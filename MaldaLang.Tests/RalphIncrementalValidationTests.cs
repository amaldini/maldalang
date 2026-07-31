// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RalphIncrementalValidationTests : TestBase
{
    public sealed class ValidateEnvScope : IDisposable
    {
        private readonly string? _scope;
        private readonly string? _hook;
        private readonly string? _fallback;

        public ValidateEnvScope(string? scope = null, string? hook = null, string? fallback = null)
        {
            _scope = Environment.GetEnvironmentVariable("MALDA_RALPH_VALIDATE_SCOPE");
            _hook = Environment.GetEnvironmentVariable("MALDA_RALPH_VALIDATE_HOOK");
            _fallback = Environment.GetEnvironmentVariable("MALDA_RALPH_VALIDATE_FALLBACK");
            Environment.SetEnvironmentVariable("MALDA_RALPH_VALIDATE_SCOPE", scope);
            Environment.SetEnvironmentVariable("MALDA_RALPH_VALIDATE_HOOK", hook);
            Environment.SetEnvironmentVariable("MALDA_RALPH_VALIDATE_FALLBACK", fallback);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("MALDA_RALPH_VALIDATE_SCOPE", _scope);
            Environment.SetEnvironmentVariable("MALDA_RALPH_VALIDATE_HOOK", _hook);
            Environment.SetEnvironmentVariable("MALDA_RALPH_VALIDATE_FALLBACK", _fallback);
        }
    }

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string ValidationModules(string workDir) =>
        $@"
include ""{RepoRoot().Replace("\\", "/")}/Examples/RalphWiggum/ralph/00-env.malda"";
include ""{RepoRoot().Replace("\\", "/")}/Examples/RalphWiggum/ralph/03-validation.malda"";
var workDir = ""{workDir.Replace("\\", "/")}"";
";

    [Fact]
    public void ValidateWorkdirWithContext_ChangedScope_SkipsUnlistedNestedBadFile()
    {
        var tempDir = CreateTempDirectory("ralph_incr_");
        using var env = new ValidateEnvScope("changed", "never", "all");
        try
        {
            var sub = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "bad.html"), "<html></html></html>");
            File.WriteAllText(Path.Combine(tempDir, "good.json"), "{\"a\":1}");

            var source = ValidationModules(tempDir) + @"
var r = validateWorkdirWithContext(workDir, ""PRD.md"", [""good.json""], """", []);
print(string(r.ok) + ""|"" + string(r.fileCount) + ""|"" + r.scope);
";
            var output = RunProgram(source);
            Assert.Contains("true|1|changed", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdirWithContext_AllScope_StillFindsNestedBadFile()
    {
        var tempDir = CreateTempDirectory("ralph_incr_");
        using var env = new ValidateEnvScope("all", "never", "all");
        try
        {
            var sub = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "bad.html"), "<html></html></html>");

            var source = ValidationModules(tempDir) + @"
var r = validateWorkdir(workDir, ""PRD.md"");
print(string(r.ok) + ""|"" + r.scope);
";
            var output = RunProgram(source);
            Assert.Contains("false|all", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdirWithContext_ValidationFixPhase_ForcesChangedScope()
    {
        var tempDir = CreateTempDirectory("ralph_incr_");
        using var env = new ValidateEnvScope("all", "never", "all");
        try
        {
            var sub = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "bad.html"), "<html></html></html>");
            File.WriteAllText(Path.Combine(tempDir, "good.json"), "{\"a\":1}");

            var source = ValidationModules(tempDir) + @"
var r = validateWorkdirWithContext(workDir, ""PRD.md"", [""good.json""], ""validation-fix"", []);
print(string(r.ok) + ""|"" + string(r.fileCount) + ""|"" + r.scope);
";
            var output = RunProgram(source);
            Assert.Contains("true|1|changed", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdirWithContext_ChangedFallbackNone_SkipsWhenNoPaths()
    {
        var tempDir = CreateTempDirectory("ralph_incr_");
        using var env = new ValidateEnvScope("changed", "never", "none");
        try
        {
            var sub = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "bad.html"), "<html></html></html>");

            var source = ValidationModules(tempDir) + @"
var r = validateWorkdirWithContext(workDir, ""PRD.md"", [], """", []);
print(string(r.ok) + ""|"" + string(r.fileCount));
";
            var output = RunProgram(source);
            Assert.Contains("true|0", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdirWithContext_OnChangeHook_SkipsWhenNoChanges()
    {
        var tempDir = CreateTempDirectory("ralph_incr_");
        using var env = new ValidateEnvScope("all", "on_change", "all");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ".ralph-validate.bat"), "@echo off\r\nexit /b 1\r\n");

            var source = ValidationModules(tempDir) + @"
var r = validateWorkdirWithContext(workDir, ""PRD.md"", [], """", []);
print(string(r.ok) + ""|"" + string(r.hookRan));
";
            var output = RunProgram(source);
            Assert.Contains("true|false", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
