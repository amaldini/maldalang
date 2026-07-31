using System;
using System.IO;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

public class TypedNumericBenchmarkSmokeTests
{
    [Fact]
    public void TypedNumericBenchmark_Compiles_WithTypedLevel2()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sourcePath = Path.Combine(repoRoot, "Examples", "Trading", "typed_vs_dynamic_float_profile.malda");
        Assert.True(File.Exists(sourcePath), $"Benchmark source not found: {sourcePath}");

        var tempRoot = Path.Combine(Path.GetTempPath(), "malda_typed_numeric_benchmark_smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var outputExe = Path.Combine(tempRoot, "typed_vs_dynamic_float_profile.exe");

        try
        {
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(
                sourcePath,
                outputExe,
                CompilationMode.TranspileToCSharp,
                includeLLamaSharp: false,
                includeUiHost: false,
                profilingOptions: null,
                typedTranspileLevel: 2);

            Assert.True(result.Success, $"Benchmark compile failed: {result.ErrorMessage}");
            Assert.True(File.Exists(outputExe), $"Output exe not found: {outputExe}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}

