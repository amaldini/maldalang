// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RalphPrdValidationHintsTests : TestBase
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string PrdValidationModules(string workDir) =>
        $@"
include ""{RepoRoot().Replace("\\", "/")}/Examples/RalphWiggum/ralph/00-env.malda"";
include ""{RepoRoot().Replace("\\", "/")}/Examples/RalphWiggum/ralph/02-prd.malda"";
include ""{RepoRoot().Replace("\\", "/")}/Examples/RalphWiggum/ralph/03-validation.malda"";
var workDir = ""{workDir.Replace("\\", "/")}"";
";

    [Fact]
    public void GetCurrentPrdFileHints_ParsesFilesAndVerifyLines()
    {
        var tempDir = CreateTempDirectory("ralph_prd_hint_");
        try
        {
            var prd = Path.Combine(tempDir, "PRD.md");
            File.WriteAllText(prd,
                "- [TODO] [P0] **F1 — Demo** (depends: none)\n" +
                "  - Files: snake.html, lib/helper.js\n" +
                "  - Verify: config.json\n" +
                "  - Acceptance: works\n");
            var source = PrdValidationModules(tempDir) + @"
var hints = getCurrentPrdFileHints(pathJoin(workDir, ""PRD.md""), """");
print(string(length(hints)) + ""|"" + hints[0] + ""|"" + hints[1] + ""|"" + hints[2]);
";
            var output = RunProgram(source);
            Assert.Contains("3|snake.html|lib/helper.js|config.json", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void MergeValidationRelPaths_IncludesPrdPathAndValidateAlways()
    {
        var tempDir = CreateTempDirectory("ralph_prd_hint_");
        using var env = new RalphIncrementalValidationTests.ValidateEnvScope("changed", "never", "none");
        Environment.SetEnvironmentVariable("MALDA_RALPH_VALIDATE_ALWAYS", "always-check.json");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "good.json"), "{\"a\":1}");
            File.WriteAllText(Path.Combine(tempDir, "always-check.json"), "{\"b\":2}");
            var sub = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "bad.html"), "<html></html></html>");

            var source = PrdValidationModules(tempDir) + @"
var merged = mergeValidationRelPaths([], [""good.json""], ""PRD.md"");
var r = validateWorkdirWithContext(workDir, ""PRD.md"", merged, """", []);
print(string(r.ok) + ""|"" + string(r.fileCount));
";
            var output = RunProgram(source);
            Assert.Contains("true|2", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_RALPH_VALIDATE_ALWAYS", null);
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdirWithContext_ChangedScopeUsesPrdHintsWithoutGitChanges()
    {
        var tempDir = CreateTempDirectory("ralph_prd_hint_");
        using var env = new RalphIncrementalValidationTests.ValidateEnvScope("changed", "never", "none");
        try
        {
            var prd = Path.Combine(tempDir, "PRD.md");
            File.WriteAllText(prd,
                "- [TODO] [P0] **F1** (depends: none)\n" +
                "  - Verify: good.json\n");
            File.WriteAllText(Path.Combine(tempDir, "good.json"), "{\"ok\":true}");
            var sub = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "bad.html"), "<html></html></html>");

            var source = PrdValidationModules(tempDir) + @"
var hints = getCurrentPrdFileHints(pathJoin(workDir, ""PRD.md""), """");
var merged = mergeValidationRelPaths([], hints, ""PRD.md"");
var r = validateWorkdirWithContext(workDir, ""PRD.md"", merged, """", []);
print(string(r.ok) + ""|"" + string(r.fileCount));
";
            var output = RunProgram(source);
            Assert.Contains("true|1", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
