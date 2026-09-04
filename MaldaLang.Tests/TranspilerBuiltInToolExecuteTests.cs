// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspilerBuiltInToolExecuteTests
{
    [Fact]
    public void TranspiledFileTools_Execute_ReadWriteListGrepParse()
    {
        var tempDir = CreateTempDirectory("malda_ttools_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            File.WriteAllText(Path.Combine(tempDir, "note.txt"), "hello world");
            File.WriteAllText(Path.Combine(tempDir, "src", "app.malda"), "function hi() {\n    print(\"hi\");\n}\n");

            var workDir = tempDir.Replace("\\", "/");
            var source = $@"
var workDir = ""{workDir}"";
var readTool = createReadFileTool(workDir);
var writeTool = createWriteFileTool(workDir);
var listTool = createListDirectoryTool(workDir);
var grepTool = createGrepTool(workDir);
var parseTool = createGetParseErrorsTool(workDir);

var text = readTool.execute({{ ""filePath"": ""note.txt"" }});
print(""read="" + string(text));

writeTool.execute({{ ""filePath"": ""out.txt"", ""content"": ""saved"" }});
var listed = listTool.execute({{ ""dirPath"": ""."" }});
print(""listed="" + string(length(listed)));

var hits = grepTool.execute({{ ""pattern"": ""hello"", ""filePath"": ""note.txt"" }});
print(""grep="" + string(length(hits)));

var parsed = parseTool.execute({{ ""sourceOrFilePath"": ""var x = 1;"" }});
print(""parseOk="" + string(length(parsed.parseErrors) == 0));
";
            var result = TranspiledTestRunner.CompileAndRunFromSource(source);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("read=hello world", result.StdOut);
            Assert.Contains("listed=", result.StdOut);
            Assert.Contains("grep=1", result.StdOut);
            Assert.Contains("parseOk=true", result.StdOut);
            Assert.DoesNotContain("Tool execution validated", result.StdOut);
            Assert.Equal("saved", File.ReadAllText(Path.Combine(tempDir, "out.txt")));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledReplaceAndInsert_Execute_ModifyFile()
    {
        var tempDir = CreateTempDirectory("malda_tedit_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "sample.txt"), "line1\nline3\n");
            var workDir = tempDir.Replace("\\", "/");
            var source = $@"
var workDir = ""{workDir}"";
var insertTool = createInsertAtLineTool(workDir);
var replaceTool = createReplaceInFileTool(workDir);
insertTool.execute({{ ""filePath"": ""sample.txt"", ""lineNumber"": 2, ""content"": ""line2"" }});
replaceTool.execute({{ ""filePath"": ""sample.txt"", ""oldText"": ""line3"", ""newText"": ""LINE3"" }});
var readTool = createReadFileTool(workDir);
print(readTool.execute({{ ""filePath"": ""sample.txt"" }}));
";
            var result = TranspiledTestRunner.CompileAndRunFromSource(source);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("line2", result.StdOut);
            Assert.Contains("LINE3", result.StdOut);
            Assert.DoesNotContain("Tool execution validated", result.StdOut);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledSubmitPlanAndGetSymbols_Execute()
    {
        var source = """
            var planTool = createSubmitPlanTool();
            var plan = planTool.execute({
                "steps": [
                    { "id": "s1", "description": "one" }
                ]
            });
            print("accepted=" + string(plan.accepted));
            print("steps=" + string(plan.stepCount));

            var symbolsTool = createGetSymbolsTool();
            var symbols = symbolsTool.execute({
                "filePathOrSource": "function greet(name) { return name; }"
            });
            print("fns=" + string(length(symbols.functions)));
            print("fn0=" + string(symbols.functions[0].name));
            """;
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("accepted=true", result.StdOut);
        Assert.Contains("steps=1", result.StdOut);
        Assert.Contains("fns=1", result.StdOut);
        Assert.Contains("fn0=greet", result.StdOut);
        Assert.DoesNotContain("Tool execution validated", result.StdOut);
    }

    [Fact]
    public void FactoryTools_Execute_DoesNotReturnStub()
    {
        var empty = RuntimeValue.Object(new JsonObject());
        RuntimeValue[] tools =
        [
            BuiltInTools.CreateReadFileTool(),
            BuiltInTools.CreateWriteFileTool(),
            BuiltInTools.CreateReplaceInFileTool(),
            BuiltInTools.CreateListDirectoryTool(),
            BuiltInTools.CreateDeleteFileTool(),
            BuiltInTools.CreateCopyFileTool(),
            BuiltInTools.CreateEnsureDirTool(),
            BuiltInTools.CreateAskUserTool(),
            BuiltInTools.CreateWebSearchTool(),
            BuiltInTools.CreateWebFetchTool(),
            BuiltInTools.CreateGrepTool(),
            BuiltInTools.CreateGlobTool(),
            BuiltInTools.CreateInsertAtLineTool(),
            BuiltInTools.CreateEditFileTool(),
            BuiltInTools.CreateGitStatusTool(),
            BuiltInTools.CreateGitAddTool(),
            BuiltInTools.CreateGitCommitTool(),
            BuiltInTools.CreateGitLogTool(),
            BuiltInTools.CreateGitDiffTool(),
            BuiltInTools.CreateGitBranchTool(),
            BuiltInTools.CreateGitCheckoutTool(),
            BuiltInTools.CreateGitPushTool(),
            BuiltInTools.CreateGitPullTool(),
            BuiltInTools.CreateRunCommandTool(),
            BuiltInTools.CreateRunMALDATool(),
            BuiltInTools.CreateCompileMALDATool(),
            BuiltInTools.CreateGetSymbolsTool(),
            BuiltInTools.CreateGetParseErrorsTool(),
            BuiltInTools.CreateCheckMaldaTool(),
            BuiltInTools.CreateSubmitPlanTool(),
            BuiltInTools.CreateCreateMcpAgentScriptTool(),
        ];

        foreach (var toolVal in tools)
        {
            var tool = Assert.IsType<ToolInstance>(toolVal.AsObject());
            var result = tool.Execute(empty);
            Assert.NotEqual("Tool execution validated", result.ToString());
            Assert.DoesNotContain("Tool execution validated", result.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TranspiledEditFileRunMaldaGitAndMcp_Execute()
    {
        var tempDir = CreateTempDirectory("malda_trest_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "sample.txt"), "alpha beta");
            var workDir = tempDir.Replace("\\", "/");
            var scriptPath = Path.Combine(tempDir, "reviewer.malda").Replace("\\", "/");
            InitGitRepo(tempDir);

            var source = $@"
var workDir = ""{workDir}"";
var editTool = createEditFileTool(workDir);
var edited = editTool.execute({{
    ""filePath"": ""sample.txt"",
    ""edits"": [{{ ""oldText"": ""beta"", ""newText"": ""gamma"" }}]
}});
print(""editOk="" + string(edited.success));
print(""editN="" + string(edited.applied));

var runTool = createRunMALDATool();
var ran = runTool.execute({{ ""sourceOrFilePath"": ""print(1 + 1);"" }});
print(""runOk="" + string(ran.success));
print(""runOut="" + string(ran.output));

var gitTool = createGitStatusTool(workDir);
var status = gitTool.execute({{ ""repoPath"": workDir }});
print(""untracked="" + string(length(status.untracked) > 0));

var mcpTool = createCreateMcpAgentScriptTool(workDir);
var mcp = mcpTool.execute({{
    ""agentName"": ""Reviewer"",
    ""agentRole"": ""Code reviewer"",
    ""agentInstructions"": ""Review diffs."",
    ""tools"": [{{ ""name"": ""summarize"", ""description"": ""Summarize a file"" }}],
    ""outputPath"": ""reviewer.malda""
}});
print(""mcpOk="" + string(mcp.success));
";
            var result = TranspiledTestRunner.CompileAndRunFromSource(source);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("editOk=true", result.StdOut);
            Assert.Contains("editN=1", result.StdOut);
            Assert.Contains("runOk=true", result.StdOut);
            Assert.Contains("runOut=2", result.StdOut);
            Assert.Contains("untracked=true", result.StdOut);
            Assert.Contains("mcpOk=true", result.StdOut);
            Assert.DoesNotContain("Tool execution validated", result.StdOut);
            Assert.Contains("gamma", File.ReadAllText(Path.Combine(tempDir, "sample.txt")));
            Assert.True(File.Exists(scriptPath));
            Assert.Contains("@MCPTool", File.ReadAllText(scriptPath));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private static void InitGitRepo(string directory)
    {
        RunGit(directory, "init");
        RunGit(directory, "config", "user.email", "test@example.com");
        RunGit(directory, "config", "user.name", "Test");
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures on CI/Windows file locks
        }
    }
}
