using System;
using System.IO;
using System.Text;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspiledUiHostEmbeddingTests
{
    [Fact]
    public void Transpiled_AutoIncludesUiHost_WhenUiFrameworkIsUsed()
    {
        var source = @"
            var tree = ui.column({""componentId"": ""Root""}, [ui.text({""value"": ""ok""})]);
            ui.mount(tree, ""session-auto"");
        ";

        var generatedCode = CompileAndReadGeneratedProgram(source, includeUiHost: false);
        Assert.Contains("EmbeddedUiHostRuntime", generatedCode);
    }

    [Fact]
    public void Transpiled_ForcedIncludesUiHost_WhenFlagIsEnabled()
    {
        var source = @"
            print(""plain transpiled script"");
        ";

        var generatedCode = CompileAndReadGeneratedProgram(source, includeUiHost: true);
        Assert.Contains("EmbeddedUiHostRuntime", generatedCode);
    }

    [Fact]
    public void Transpiled_DoesNotIncludeUiHost_ByDefaultWithoutUiFramework()
    {
        var source = @"
            print(""plain transpiled script"");
        ";

        var generatedCode = CompileAndReadGeneratedProgram(source, includeUiHost: false);
        Assert.DoesNotContain("EmbeddedUiHostRuntime", generatedCode);
    }

    private static string CompileAndReadGeneratedProgram(string source, bool includeUiHost)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_uihost_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "program.malda");
        var outputPath = Path.Combine(tempDir, "program.exe");

        try
        {
            File.WriteAllText(sourcePath, source, Encoding.UTF8);

            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(
                sourcePath,
                outputPath,
                CompilationMode.TranspileToCSharp,
                includeLLamaSharp: false,
                includeUiHost: includeUiHost);

            Assert.True(result.Success, result.ErrorMessage);
            var generatedProgramPath = Path.Combine(Directory.GetCurrentDirectory(), "Examples", "GeneratedProgram.cs");
            Assert.True(File.Exists(generatedProgramPath), $"Generated transpiled code not found at {generatedProgramPath}");
            return File.ReadAllText(generatedProgramPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests.
            }
        }
    }
}
