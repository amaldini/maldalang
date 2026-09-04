// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.IO;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class CheckMaldaToolTests : TestBase
{
    private readonly string _testDirectory;

    public CheckMaldaToolTests()
    {
        _testDirectory = CreateTempDirectory("CheckMaldaTool_");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            SafeDeleteDirectory(_testDirectory);
        base.Dispose(disposing);
    }

    [Fact]
    public void InlineValidSource_OkTrueErrorCountZero()
    {
        var result = BuiltInFunctions.CheckMaldaSource("var x = 1;");
        AssertCheckShape(result);
        var obj = result.AsObject();
        Assert.True(obj.Get("ok", null)!.AsBoolean());
        Assert.False(obj.Get("executed", null)!.AsBoolean());
        Assert.Equal(0, obj.Get("errorCount", null)!.AsInteger());
        Assert.Equal("<eval>", obj.Get("file", null)?.AsString());
        Assert.Empty(obj.Get("diagnostics", null)!.AsArray());
    }

    [Fact]
    public void InlineSyntaxError_OkFalseWithLineAndColumn()
    {
        var result = BuiltInFunctions.CheckMaldaSource("function (");
        AssertCheckShape(result);
        var obj = result.AsObject();
        Assert.False(obj.Get("ok", null)!.AsBoolean());
        Assert.True(obj.Get("errorCount", null)!.AsInteger() >= 1);
        var first = obj.Get("diagnostics", null)!.AsArray()[0].AsObject();
        Assert.Equal("error", first.Get("severity", null)?.AsString());
        Assert.True(first.Get("line", null)!.AsInteger() >= 1);
        Assert.True(first.Get("column", null)!.AsInteger() >= 1);
        Assert.False(string.IsNullOrEmpty(first.Get("message", null)?.AsString()));
    }

    [Fact]
    public void TypeMismatch_DefaultMode_IsError()
    {
        var result = BuiltInFunctions.CheckMaldaSource("var n: int = \"abc\";");
        AssertCheckShape(result);
        var obj = result.AsObject();
        Assert.False(obj.Get("ok", null)!.AsBoolean());
        Assert.True(obj.Get("errorCount", null)!.AsInteger() >= 1);
        Assert.Contains(
            obj.Get("diagnostics", null)!.AsArray(),
            d => d.AsObject().Get("severity", null)?.AsString() == "error");
    }

    [Fact]
    public void TypeMismatch_LenientMode_IsWarningAndOk()
    {
        var result = BuiltInFunctions.CheckMaldaSource("var n: int = \"abc\";", "lenient");
        AssertCheckShape(result);
        var obj = result.AsObject();
        Assert.True(obj.Get("ok", null)!.AsBoolean());
        Assert.Equal(0, obj.Get("errorCount", null)!.AsInteger());
        Assert.True(obj.Get("warningCount", null)!.AsInteger() >= 1);
        Assert.Contains(
            obj.Get("diagnostics", null)!.AsArray(),
            d => d.AsObject().Get("severity", null)?.AsString() == "warning");
    }

    [Fact]
    public void FilePathInsideWorkingDirectory_ReadsAndChecks()
    {
        var path = Path.Combine(_testDirectory, "ok.malda");
        File.WriteAllText(path, "var x = 1;");
        var result = ExecuteTool(
            BuiltInTools.CreateCheckMaldaTool(_testDirectory),
            Args(("sourceOrFilePath", "ok.malda")));
        AssertCheckShape(result);
        var obj = result.AsObject();
        Assert.True(obj.Get("ok", null)!.AsBoolean());
        Assert.Equal(0, obj.Get("errorCount", null)!.AsInteger());
        Assert.Contains("ok.malda", obj.Get("file", null)?.AsString() ?? "");
    }

    [Fact]
    public void PathOutsideWorkingDirectory_ErrorNotCrash()
    {
        var root = CreateTempDirectory("CheckMaldaTool_jail_");
        try
        {
            var workDir = Path.Combine(root, "work");
            var outsideDir = Path.Combine(root, "outside");
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(outsideDir);
            var victim = Path.Combine(outsideDir, "secret.malda");
            File.WriteAllText(victim, "var x = 1;");

            var result = ExecuteTool(
                BuiltInTools.CreateCheckMaldaTool(workDir),
                Args(("sourceOrFilePath", victim)));
            Assert.False(IsOkTrue(result));
            Assert.Contains("outside", ToolErrorText(result), System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void ToolExecute_InlineValid_ReturnsOkTrue()
    {
        var toolVal = BuiltInTools.CreateCheckMaldaTool();
        var tool = Assert.IsType<ToolInstance>(toolVal.AsObject());
        Assert.Equal("check_malda", tool.Name);
        var result = tool.Execute(Args(("sourceOrFilePath", "var x = 1;")));
        AssertCheckShape(result);
        Assert.True(result.AsObject().Get("ok", null)!.AsBoolean());
        Assert.DoesNotContain("Tool execution validated", result.ToString());
    }

    [Fact]
    public void Builtin_CallBuiltIn_MatchesHelper()
    {
        var viaBuiltin = BuiltInFunctions.CallBuiltIn(
            "checkMalda",
            new List<RuntimeValue> { RuntimeValue.String("var x = 1;") },
            null);
        AssertCheckShape(viaBuiltin);
        Assert.True(viaBuiltin.AsObject().Get("ok", null)!.AsBoolean());
    }

    private static RuntimeValue ExecuteTool(RuntimeValue toolValue, RuntimeValue arguments)
    {
        var tool = Assert.IsType<ToolInstance>(toolValue.AsObject());
        return tool.Execute(arguments);
    }

    private static RuntimeValue Args(params (string Name, string Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (name, value) in fields)
            obj.Set(name, RuntimeValue.String(value));
        return RuntimeValue.Object(obj);
    }

    private static void AssertCheckShape(RuntimeValue result)
    {
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        Assert.NotNull(obj.Get("ok", null));
        Assert.NotNull(obj.Get("executed", null));
        Assert.NotNull(obj.Get("errorCount", null));
        Assert.NotNull(obj.Get("warningCount", null));
        Assert.NotNull(obj.Get("infoCount", null));
        Assert.NotNull(obj.Get("diagnostics", null));
        Assert.Equal(ValueType.Array, obj.Get("diagnostics", null)!.Type);
        Assert.False(obj.Get("executed", null)!.AsBoolean());
    }

    private static bool IsOkTrue(RuntimeValue result)
    {
        if (result.Type != ValueType.Object)
            return false;
        var ok = result.AsObject().Get("ok", null);
        return ok != null && ok.Type == ValueType.Boolean && ok.AsBoolean();
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
