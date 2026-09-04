// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspilerRemainingBuiltInTests
{
    private static readonly string[] FormerInterpreterOnly =
    [
        "append", "pop", "shift",
        "editFile", "insertAtLine",
        "gitLog", "gitBranch", "gitCheckout", "gitPush", "gitPull",
        "createReadFileTool", "createWriteFileTool", "createReplaceInFileTool",
        "createListDirectoryTool", "createDeleteFileTool", "createCopyFileTool", "createEnsureDirTool",
        "createCheckMaldaTool",
        "createWebFetchTool",
        "createAskUserTool", "createGrepTool", "createGlobTool",
        "createInsertAtLineTool", "createEditFileTool",
        "createGitStatusTool", "createGitAddTool", "createGitCommitTool",
        "createGitLogTool", "createGitDiffTool", "createGitBranchTool",
        "createGitCheckoutTool", "createGitPushTool", "createGitPullTool",
        "setDefaultAgent", "generateUI", "uiGenerate",
        "loadAssembly", "getDotNetType", "dotnetNew"
    ];

    [Fact]
    public void FormerInterpreterOnlyBuiltIns_AreAllTranspilerSupported()
    {
        Assert.Equal(39, FormerInterpreterOnly.Length);
        foreach (var name in FormerInterpreterOnly)
        {
            var descriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor(name));
            Assert.Equal(BuiltInTranspilerStrategy.SupportedByTranspiler, descriptor.TranspilerStrategy);
            Assert.True(BuiltInRegistry.IsTranspilerBuiltIn(name), name);
        }
    }

    [Fact]
    public void TranspiledArrayMutators_AppendPopShift()
    {
        var source = @"
var xs = [1, 2];
xs.append(3);
print(string(length(xs)));
print(string(xs.pop()));
print(string(xs.shift()));
print(string(length(xs)));
";
        var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
        Assert.Contains("3", output);
        Assert.Contains("1", output);
    }

    [Fact]
    public void TranspiledInsertAtLine_ModifiesFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_tinsert_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var filePath = Path.Combine(tempDir, "sample.txt").Replace("\\", "/");
            File.WriteAllText(Path.Combine(tempDir, "sample.txt"), "line1\nline3\n");
            var source = $@"
var path = ""{filePath}"";
insertAtLine(path, 2, ""line2"");
print(readFile(path));
";
            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
            Assert.Contains("line2", output);
            Assert.Contains("line1", output);
            Assert.Contains("line3", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TranspiledCreateAskUserTool_ReturnsTool()
    {
        var source = @"
var tool = createAskUserTool();
print(string(tool != null));
print(string(tool.name));
";
        var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
        Assert.Contains("true", output);
        Assert.Contains("ask_user", output);
    }

    [Fact]
    public void TranspiledDotNetInterop_GetTypeAndNew()
    {
        var source = @"
var t = getDotNetType(""System.Text.StringBuilder"");
print(string(t != null));
var sb = dotnetNew(t);
print(string(sb != null));
";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("true", result.StdOut);
    }

    [Fact]
    public void TranspiledSetDefaultAgent_UsesSharedInterpreter()
    {
        TranspiledBuiltinRuntime.SetInterpreter(new MaldaLang.Interpreter.Interpreter(currentFile: "test-reset"));
        try
        {
            var source = @"
var agent = new Agent(""T"", ""tester"", ""You test."", new OpenRouterClient());
setDefaultAgent(agent);
print(""ok"");
";
            var result = TranspiledTestRunner.CompileAndRunFromSource(source);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("ok", result.StdOut);
        }
        finally
        {
            TranspiledBuiltinRuntime.SetInterpreter(new MaldaLang.Interpreter.Interpreter(currentFile: "test-reset"));
        }
    }
}
