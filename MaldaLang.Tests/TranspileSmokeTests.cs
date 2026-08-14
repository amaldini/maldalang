// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Compiler;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// B2 curated product Examples that must pass <c>compile --mode transpile</c> (compile-only).
/// </summary>
public class TranspileSmokeTests
{
    [Theory]
    [InlineData("Examples/Basics/schema_validate.malda")]
    [InlineData("Examples/Agents/phase6_pure_validate.malda")]
    [InlineData("Examples/Workflows/simple_step.malda")]
    [InlineData("Examples/Workflows/determinism_helpers.malda")]
    [InlineData("Examples/Web/job_queue_basic.malda")]
    [InlineData("Examples/Prompts/prompt_tools_then_structured.malda")]
    [InlineData("Examples/Prompts/prompt_budget.malda")]
    public void Example_TranspileToCSharp_Succeeds(string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sourcePath = PlanningPaths.ResolveRepoFile(parts);
        Assert.True(File.Exists(sourcePath), $"Missing smoke example: {sourcePath}");

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_transpile_smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var outputPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(sourcePath) + ".exe");

        try
        {
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(
                sourcePath,
                outputPath,
                CompilationMode.TranspileToCSharp,
                includeLLamaSharp: false,
                includeUiHost: false);

            if (!result.Success)
            {
                var errorDir = Path.GetDirectoryName(outputPath) ?? tempDir;
                var buildErrorsPath = Path.Combine(errorDir, "build_errors.txt");
                var generatedPath = Path.Combine(errorDir, "GeneratedProgram.cs");
                var details = result.ErrorMessage ?? "Compilation failed.";
                if (File.Exists(buildErrorsPath))
                    details += Environment.NewLine + "build_errors.txt: " + Path.GetFullPath(buildErrorsPath);
                if (File.Exists(generatedPath))
                    details += Environment.NewLine + "GeneratedProgram.cs: " + Path.GetFullPath(generatedPath);
                Assert.Fail($"Transpile smoke failed for {relativePath}: {details}");
            }

            Assert.True(
                !string.IsNullOrEmpty(result.OutputPath) && File.Exists(result.OutputPath),
                $"Transpile reported success but output missing for {relativePath}.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
