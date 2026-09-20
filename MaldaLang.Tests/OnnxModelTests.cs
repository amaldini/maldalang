// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class OnnxModelTests : TestBase
{
    [Fact]
    public void Constructor_RejectsMissingFile()
    {
        var ex = Assert.Throws<RuntimeException>(() =>
            new OnnxModelInstance(Path.Combine(Path.GetTempPath(), "malda-missing-onnx-" + Guid.NewGuid() + ".onnx")));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IdentityFixture_InspectsAndRuns()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "data", "identity.onnx");
        Assert.True(File.Exists(path), path);
        var output = RunProgram(
            "var model = new OnnxModel(" + ToMaldaString(path) + ");\n" +
            "print(model.inputs()[0].name);\n" +
            "print(model.outputs()[0].name);\n" +
            "var y = model.run({ \"X\": [[1.0, 2.0], [3.0, 4.0]] }).Y;\n" +
            "print(y[0][0]);\n" +
            "print(y[1][1]);\n").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("X", output[0].Trim());
        Assert.Equal("Y", output[1].Trim());
        Assert.Equal("1", output[2].Trim());
        Assert.Equal("4", output[3].Trim());
    }

    [Fact]
    public void IdentityFixture_MatchesInTranspiledMode()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "data", "identity.onnx");
        var source =
            "var y = new OnnxModel(" + ToMaldaString(path) + ").run({ \"X\": [[5.0, 6.0], [7.0, 8.0]] }).Y;\n" +
            "print(y[0][1]);\n" +
            "print(y[1][0]);\n";
        var interpreted = RunProgram(source);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.True(transpiled.ExitCode == 0, $"ExitCode={transpiled.ExitCode}\nStdErr={transpiled.StdErr}\nStdOut={transpiled.StdOut}");
        Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
    }

    [Fact]
    public void SourceRequiresOnnxRuntime_DetectsOnnxModel()
    {
        Assert.True(MaldaLang.Compiler.Compiler.SourceRequiresOnnxRuntime("var m = new OnnxModel(\"m.onnx\");"));
        Assert.False(MaldaLang.Compiler.Compiler.SourceRequiresOnnxRuntime("print(\"hello\");"));
    }

    static string ToMaldaString(string path) =>
        "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
