// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

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

var parsed = parseTool.execute({{ ""sourceOrFilePath"": ""function ok() {{ return 1; }}"" }});
print(""parseOk="" + string(length(parsed) == 0));
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
    public void TranspiledCustomTool_WithoutHandler_StillValidatesOnly()
    {
        var source = """
            var tool = new Tool(
                "echo_message",
                "Echoes a message",
                {
                    "type": "object",
                    "properties": { "message": { "type": "string" } },
                    "required": ["message"]
                }
            );
            print(tool.execute({ "message": "hi" }));
            """;
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Tool execution validated", result.StdOut);
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
