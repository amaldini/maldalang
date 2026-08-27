// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;
using MaldaCompiler = MaldaLang.Compiler.Compiler;

namespace MaldaLang.Tests;

public class GetProgramDirectoryTests : TestBase
{
    [Fact]
    public void GetProgramDirectory_UsesSourceScriptDirectory_WhenFileExists()
    {
        var tempDir = CreateTempDirectory("gpd_src_");
        var scriptPath = Path.Combine(tempDir, "probe.malda");
        File.WriteAllText(scriptPath, "print(getProgramDirectory());\n");

        var interpreter = new Interpreter.Interpreter(currentFile: scriptPath);
        var result = BuiltInFunctions.CallBuiltIn(
            "getProgramDirectory",
            new System.Collections.Generic.List<RuntimeValue>(),
            interpreter);

        Assert.Equal(ValueType.String, result.Type);
        Assert.Equal(
            Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(result.AsString()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            ignoreCase: true);
    }

    [Fact]
    public void GetProgramDirectory_IgnoresTranspiledPlaceholder_UsesProcessDirectory()
    {
        var interpreter = new Interpreter.Interpreter(currentFile: "transpiled");
        var result = BuiltInFunctions.CallBuiltIn(
            "getProgramDirectory",
            new System.Collections.Generic.List<RuntimeValue>(),
            interpreter);

        Assert.Equal(ValueType.String, result.Type);
        var expected = Path.GetDirectoryName(Path.GetFullPath(System.Environment.ProcessPath!))!;
        Assert.Equal(
            Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(result.AsString()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            ignoreCase: true);
    }

    [Fact]
    public void MaldaTest_PassesCurrentFile_SoGetProgramDirectoryIsTheTestFolder()
    {
        var tempDir = CreateTempDirectory("gpd_test_");
        try
        {
            var testPath = Path.Combine(tempDir, "probe.test.malda");
            File.WriteAllText(Path.Combine(tempDir, "marker.txt"), "ok");
            File.WriteAllText(
                testPath,
                "var marker = io.pathJoin(getProgramDirectory(), \"marker.txt\");\nassert(io.pathExists(marker), \"getProgramDirectory should be the test file folder\");\n");

            var output = new StringWriter();
            var error = new StringWriter();
            var code = new MaldaLang.Testing.TestCommandRunner().Run(new[] { testPath }, output, error);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void SourceRequiresLLamaSharp_DetectsLlamaEmbedder()
    {
        Assert.True(MaldaCompiler.SourceRequiresLLamaSharp("var e = new LlamaEmbedder(\"m.gguf\");"));
        Assert.False(MaldaCompiler.SourceRequiresLLamaSharp("print(\"hello\");"));
    }
}
