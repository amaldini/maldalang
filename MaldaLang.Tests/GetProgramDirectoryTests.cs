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
    public void SourceRequiresLLamaSharp_DetectsLlamaEmbedder()
    {
        Assert.True(MaldaCompiler.SourceRequiresLLamaSharp("var e = new LlamaEmbedder(\"m.gguf\");"));
        Assert.False(MaldaCompiler.SourceRequiresLLamaSharp("print(\"hello\");"));
    }
}
