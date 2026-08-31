// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

public class TranspiledTypedPromptTests
{
    [Fact]
    public void Transpiler_GeneratesExecuteAsyncHelper_ForAwaitedPrompt()
    {
        var source = """
            prompt planTask(task) -> Plan {
                user "Task: " + task;
            }

            var result = await planTask("test");
            print(result);
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_transpiled_prompt_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "typed_prompt_test.malda");
        var outputPath = Path.Combine(tempDir, "typed_prompt_test.exe");
        File.WriteAllText(sourcePath, source);

        try
        {
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputPath, CompilationMode.TranspileToCSharp, includeLLamaSharp: false, includeUiHost: false);

            Assert.True(result.Success, result.ErrorMessage ?? "Compilation failed.");

            var generatedProgramPath = Path.Combine(tempDir, "GeneratedProgram.cs");
            if (!File.Exists(generatedProgramPath))
            {
                generatedProgramPath = Path.Combine(Directory.GetCurrentDirectory(), "Examples", "GeneratedProgram.cs");
            }
            Assert.True(File.Exists(generatedProgramPath), "Expected transpiler to emit GeneratedProgram.cs.");

            var generated = File.ReadAllText(generatedProgramPath);
            Assert.Contains("planTask__ExecuteAsync(", generated);
            Assert.Contains("await planTask__ExecuteAsync(", generated);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Transpiler_EmitsResolvedSchema_ForPromptWithCustomClassReturnType()
    {
        var source = """
            class TaskResult {
                var id;
                var title;
            }

            prompt getTask(name) -> TaskResult {
                user "Get task: " + name;
            }

            var result = await getTask("test");
            print(result);
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_transpiled_prompt_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "custom_class_prompt.malda");
        var generatedPath = Path.Combine(tempDir, "GeneratedProgram.cs");
        File.WriteAllText(sourcePath, source);

        try
        {
            var compiler = new Compiler.Compiler();
            var csharpResult = compiler.CompileToCSharp(sourcePath, generatedPath);
            Assert.True(csharpResult.Success, csharpResult.ErrorMessage ?? "Transpile to C# failed.");
            Assert.True(File.Exists(generatedPath), "Expected GeneratedProgram.cs to be written.");

            var generated = File.ReadAllText(generatedPath);
            Assert.Contains("__resolvedSchema", generated);
            Assert.Contains("TryValidateReturnType(__parsed, __resolvedSchema!, out __validated, out __validationError)", generated);
            Assert.Contains("type", generated);
            Assert.Contains("properties", generated);
            Assert.Contains("required", generated);
            Assert.Contains("id", generated);
            Assert.Contains("title", generated);
            // Schema-to-LLM: typed prompts should pass response_format to PromptInstance
            Assert.Contains("__responseFormatSchema", generated);
            Assert.Contains("ApplySchemaAppendix", generated);
            Assert.Contains("__responseFormatSchema, examples, __withinTimeoutMs, gather, __resourceBudget,", generated);
            Assert.Contains(", attachments));", generated);

            var boundedSource = """
                @within(1500)
                prompt bounded(name) -> TaskResult {
                    user "Get task: " + name;
                }
                """;
            var boundedPath = Path.Combine(tempDir, "bounded_prompt.malda");
            File.WriteAllText(boundedPath, boundedSource);
            var boundedGenPath = Path.Combine(tempDir, "BoundedProgram.cs");
            var boundedResult = compiler.CompileToCSharp(boundedPath, boundedGenPath);
            Assert.True(boundedResult.Success, boundedResult.ErrorMessage ?? "Bounded prompt transpile failed.");
            var boundedGenerated = File.ReadAllText(boundedGenPath);
            Assert.Contains("__withinTimeoutMs = 1500", boundedGenerated);
            Assert.Contains("__withinTimeoutMs, gather, __resourceBudget,", boundedGenerated);
            Assert.Contains(", attachments));", boundedGenerated);

            var budgetSource = """
                @budget(tokens: 4000, tools: 8)
                prompt bounded(name) -> TaskResult {
                    user "Get task: " + name;
                }
                """;
            var budgetPath = Path.Combine(tempDir, "budget_prompt.malda");
            File.WriteAllText(budgetPath, budgetSource);
            var budgetGenPath = Path.Combine(tempDir, "BudgetProgram.cs");
            var budgetResult = compiler.CompileToCSharp(budgetPath, budgetGenPath);
            Assert.True(budgetResult.Success, budgetResult.ErrorMessage ?? "Budget prompt transpile failed.");
            var budgetGenerated = File.ReadAllText(budgetGenPath);
            Assert.Contains("ResourceBudget(4000, 8, null)", budgetGenerated);
            Assert.Contains(", attachments));", budgetGenerated);

            var buildResult = compiler.Compile(sourcePath, Path.Combine(tempDir, "out.exe"), CompilationMode.TranspileToCSharp, includeLLamaSharp: false, includeUiHost: false);
            Assert.True(buildResult.Success, buildResult.ErrorMessage ?? "Full compilation failed.");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
