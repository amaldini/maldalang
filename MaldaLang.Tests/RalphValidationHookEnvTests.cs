// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RalphValidationHookEnvTests : TestBase
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string ValidationModules(string workDir) =>
        $@"
include ""{RepoRoot().Replace("\\", "/")}/Examples/RalphWiggum/ralph/00-env.malda"";
include ""{RepoRoot().Replace("\\", "/")}/Examples/RalphWiggum/ralph/03-validation.malda"";
var workDir = ""{workDir.Replace("\\", "/")}"";
";

    [Fact]
    public void RunCustomValidationHook_PassesRalphChangedFilesToBatHook()
    {
        var tempDir = CreateTempDirectory("ralph_hook_env_");
        try
        {
            var hookBat = Path.Combine(tempDir, ".ralph-validate.bat");
            File.WriteAllText(hookBat,
                "@echo off\r\n" +
                "echo RALPH_CHANGED_FILES=%RALPH_CHANGED_FILES% > ralph-hook-env.out\r\n" +
                "exit /b 0\r\n");

            var source = ValidationModules(tempDir) + @"
var env = buildRalphValidationHookEnv(workDir, [""a.malda"", ""PRD.md""], [""a.malda""]);
var errors = runCustomValidationHook(workDir, true, [""a.malda"", ""PRD.md""], [""a.malda""]);
print(string(length(errors)) + ""|"" + readFile(pathJoin(workDir, ""ralph-hook-env.out"")));
";
            var output = RunProgram(source);
            Assert.Contains("0|RALPH_CHANGED_FILES=a.malda", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
