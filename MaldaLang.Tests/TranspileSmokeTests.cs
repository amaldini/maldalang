// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Text;
using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Parser;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// B2 / DT3 curated product Examples that must pass <c>compile --mode transpile</c> (compile-only).
/// README showcases too large for CI (Second Brain, RalphWiggum) are documented n/a.
/// </summary>
public class TranspileSmokeTests
{
    [Theory]
    [InlineData("Examples/Basics/first_look.malda")]
    [InlineData("Examples/Basics/schema_validate.malda")]
    [InlineData("Examples/Basics/schema_sumtype_validate.malda")]
    [InlineData("Examples/Agents/phase6_pure_validate.malda")]
    [InlineData("Examples/Agents/agent_governance_golden.malda")]
    [InlineData("Examples/Workflows/simple_step.malda")]
    [InlineData("Examples/Workflows/determinism_helpers.malda")]
    [InlineData("Examples/Web/job_queue_basic.malda")]
    [InlineData("Examples/Prompts/prompt_tools_then_structured.malda")]
    [InlineData("Examples/Prompts/api_program_calc.malda")]
    [InlineData("Examples/Prompts/prompt_budget.malda")]
    [InlineData("Examples/Memory/grounded_ask.malda")]
    [InlineData("Examples/Tools/capability_tokens.malda")]
    [InlineData("Examples/Modules/selective_import.malda")]
    [InlineData("Examples/Modules/export_type_schema.malda")]
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

    [Fact]
    public void InterpretAndTranspile_Pair_SameStdout()
    {
        const string source = "io.print(\"pair-ok\");\n";
        string interpreted;
        lock (typeof(TranspileSmokeTests))
        {
            var original = Console.Out;
            var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                var parser = new Parser.Parser(tokens);
                var statements = parser.Parse();
                Assert.Empty(parser.Errors);
                var interp = new MaldaLang.Interpreter.Interpreter();
                interp.InterpretAsync(statements).GetAwaiter().GetResult();
                interpreted = writer.ToString();
            }
            finally
            {
                Console.SetOut(original);
            }
        }

        Assert.Contains("pair-ok", interpreted);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_transpile_pair", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "pair.malda");
        var outputPath = Path.Combine(tempDir, "pair.exe");
        File.WriteAllText(sourcePath, source, Encoding.UTF8);

        try
        {
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(
                sourcePath,
                outputPath,
                CompilationMode.TranspileToCSharp,
                includeLLamaSharp: false,
                includeUiHost: false);

            Assert.True(result.Success, result.ErrorMessage ?? "pair transpile failed");
            Assert.True(File.Exists(result.OutputPath), "pair transpile output missing");

            if (!OperatingSystem.IsWindows())
                return;

            var psi = new ProcessStartInfo(result.OutputPath!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            Assert.NotNull(process);
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                Assert.Fail("pair exe timed out");
            }

            var compiledOut = process.StandardOutput.ReadToEnd();
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("pair-ok", compiledOut);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
