// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TestMaldaToolTests : TestBase
{
    private readonly string _testDirectory;

    public TestMaldaToolTests()
    {
        _testDirectory = CreateTempDirectory("TestMaldaTool_");
        File.WriteAllText(Path.Combine(_testDirectory, "pass.test.malda"), "var x = 1; print(x);");
        File.WriteAllText(Path.Combine(_testDirectory, "fail.test.malda"), "error(\"boom\");");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            SafeDeleteDirectory(_testDirectory);
        base.Dispose(disposing);
    }

    [Fact]
    public void RunAll_PassAndFail_SuccessFalseExitCode1()
    {
        var result = ExecuteTool(BuiltInTools.CreateTestMaldaTool(_testDirectory), EmptyArgs());
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        Assert.False(obj.Get("success", null)!.AsBoolean());
        Assert.Equal(1, obj.Get("exitCode", null)!.AsInteger());
        Assert.False(string.IsNullOrEmpty(obj.Get("output", null)?.AsString()));
        var report = obj.Get("report", null);
        Assert.NotNull(report);
        Assert.Equal(ValueType.Object, report!.Type);
        Assert.Equal("ci", report.AsObject().Get("mode", null)?.AsString());
    }

    [Fact]
    public void ListOnly_SuccessTrue()
    {
        var args = new JsonObject();
        args.Set("listOnly", RuntimeValue.Boolean(true));
        var result = ExecuteTool(BuiltInTools.CreateTestMaldaTool(_testDirectory), RuntimeValue.Object(args));
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        Assert.True(obj.Get("success", null)!.AsBoolean());
        Assert.Equal(0, obj.Get("exitCode", null)!.AsInteger());
        var report = obj.Get("report", null);
        Assert.NotNull(report);
        Assert.Equal("list", report!.AsObject().Get("action", null)?.AsString());
        Assert.Equal(2, report.AsObject().Get("count", null)!.AsInteger());
    }

    [Fact]
    public void PathOutsideWorkingDirectory_Error()
    {
        var root = CreateTempDirectory("TestMaldaTool_jail_");
        try
        {
            var workDir = Path.Combine(root, "work");
            var outsideDir = Path.Combine(root, "outside");
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(outsideDir);
            var victim = Path.Combine(outsideDir, "secret.test.malda");
            File.WriteAllText(victim, "var x = 1;");

            var result = ExecuteTool(
                BuiltInTools.CreateTestMaldaTool(workDir),
                Args(("path", victim)));
            Assert.Contains("outside", ToolErrorText(result), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void ToolExecute_ListOnly_DoesNotReturnStub()
    {
        var toolVal = BuiltInTools.CreateTestMaldaTool(_testDirectory);
        var tool = Assert.IsType<ToolInstance>(toolVal.AsObject());
        Assert.Equal("test_malda", tool.Name);
        var args = new JsonObject();
        args.Set("listOnly", RuntimeValue.Boolean(true));
        var result = tool.Execute(RuntimeValue.Object(args));
        Assert.Equal(ValueType.Object, result.Type);
        Assert.True(result.AsObject().Get("success", null)!.AsBoolean());
        Assert.DoesNotContain("Tool execution validated", result.ToString());
    }

    private static RuntimeValue ExecuteTool(RuntimeValue toolValue, RuntimeValue arguments)
    {
        var tool = Assert.IsType<ToolInstance>(toolValue.AsObject());
        return tool.Execute(arguments);
    }

    private static RuntimeValue EmptyArgs() => RuntimeValue.Object(new JsonObject());

    private static RuntimeValue Args(params (string Name, string Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (name, value) in fields)
            obj.Set(name, RuntimeValue.String(value));
        return RuntimeValue.Object(obj);
    }

    private static string ToolErrorText(RuntimeValue result)
    {
        if (result.Type == ValueType.String)
            return result.AsString();
        if (result.Type == ValueType.Object)
        {
            var err = result.AsObject().Get("error", null);
            if (err != null && err.Type == ValueType.String)
                return err.AsString();
        }

        return result.ToString();
    }
}
