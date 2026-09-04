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
public class GetSymbolsGetParseErrorsTests : TestBase
{
    private readonly string _testDirectory;

    public GetSymbolsGetParseErrorsTests()
    {
        _testDirectory = CreateTempDirectory("GetSymbolsGetParseErrors_");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            SafeDeleteDirectory(_testDirectory);
        base.Dispose(disposing);
    }

    [Fact]
    public void GetSymbols_WithValidSource_ReturnsClassesFunctionsActorsPromptsAndParseErrors()
    {
        var source = @"
            function foo(x) { return x + 1; }
            class Bar { }
            actor Baz { }
            prompt greet(name) { }
        ";
        var result = BuiltInFunctions.CallBuiltIn("getSymbols", new List<RuntimeValue> { RuntimeValue.String(source) }, null);
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        var classes = obj.Get("classes", null);
        var functions = obj.Get("functions", null);
        var actors = obj.Get("actors", null);
        var prompts = obj.Get("prompts", null);
        var parseErrors = obj.Get("parseErrors", null);
        Assert.NotNull(classes); Assert.Equal(ValueType.Array, classes.Type);
        Assert.NotNull(functions); Assert.Equal(ValueType.Array, functions.Type);
        Assert.NotNull(actors); Assert.Equal(ValueType.Array, actors.Type);
        Assert.NotNull(prompts); Assert.Equal(ValueType.Array, prompts.Type);
        Assert.NotNull(parseErrors); Assert.Equal(ValueType.Array, parseErrors.Type);
        Assert.Equal(1, classes.AsArray().Count);
        Assert.Equal(1, functions.AsArray().Count);
        Assert.Equal(1, actors.AsArray().Count);
        Assert.Equal(1, prompts.AsArray().Count);
        Assert.Equal(0, parseErrors.AsArray().Count);
        var promptObj = prompts.AsArray()[0].AsObject();
        Assert.Equal("greet", promptObj.Get("name", null)?.AsString());
        Assert.True(promptObj.Get("signature", null)?.AsString()?.Contains("prompt greet") == true);
    }

    [Fact]
    public void GetSymbols_WithFilePath_ReadsFileAndReturnsSymbols()
    {
        var path = Path.Combine(_testDirectory, "script.malda");
        var source = "function f() { return 1; }\nclass C { }";
        File.WriteAllText(path, source);
        var result = BuiltInFunctions.CallBuiltIn("getSymbols", new List<RuntimeValue> { RuntimeValue.String(path) }, null);
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        Assert.Equal(1, obj.Get("classes", null)!.AsArray().Count);
        Assert.Equal(1, obj.Get("functions", null)!.AsArray().Count);
        Assert.Equal(0, obj.Get("parseErrors", null)!.AsArray().Count);
    }

    [Fact]
    public void GetSymbols_WithPathTraversal_ReturnsParseErrorsWithSafeMessage()
    {
        var result = BuiltInFunctions.CallBuiltIn("getSymbols", new List<RuntimeValue> { RuntimeValue.String("../foo.malda") }, null);
        Assert.Equal(ValueType.Object, result.Type);
        var parseErrors = result.AsObject().Get("parseErrors", null);
        Assert.NotNull(parseErrors);
        Assert.Equal(ValueType.Array, parseErrors.Type);
        Assert.Equal(1, parseErrors.AsArray().Count);
        Assert.Contains("suspicious", parseErrors.AsArray()[0].AsObject().Get("message", null)?.AsString() ?? "");
    }

    [Fact]
    public void GetSymbols_WithSourceContainingParseErrors_ReturnsBestEffortSymbolsAndNonEmptyParseErrors()
    {
        var source = "function ok() { }\nvar x = ;"; // parse error: empty rhs
        var result = BuiltInFunctions.CallBuiltIn("getSymbols", new List<RuntimeValue> { RuntimeValue.String(source) }, null);
        Assert.Equal(ValueType.Object, result.Type);
        var obj = result.AsObject();
        var parseErrors = obj.Get("parseErrors", null);
        Assert.NotNull(parseErrors);
        Assert.True(parseErrors.AsArray().Count >= 1);
    }

    [Fact]
    public void GetSymbols_WithPromptInSource_IncludesPromptInPromptsArray()
    {
        var source = "prompt planTask(task, docs) -> Plan { }";
        var result = BuiltInFunctions.CallBuiltIn("getSymbols", new List<RuntimeValue> { RuntimeValue.String(source) }, null);
        var prompts = result.AsObject().Get("prompts", null);
        Assert.NotNull(prompts);
        Assert.Equal(1, prompts.AsArray().Count);
        var p = prompts.AsArray()[0].AsObject();
        Assert.Equal("planTask", p.Get("name", null)?.AsString());
        Assert.Equal(2, p.Get("parameters", null)?.AsArray().Count ?? 0);
        Assert.True(p.Get("signature", null)?.AsString()?.Contains("-> Plan") == true);
    }

    [Fact]
    public void GetSymbols_WithFunctionTypeHints_IncludesSignatureParameterTypesAndReturnType()
    {
        var source = "function add(x: int, y: int) -> int { return x + y; }";
        var result = BuiltInFunctions.CallBuiltIn("getSymbols", new List<RuntimeValue> { RuntimeValue.String(source) }, null);
        var functions = result.AsObject().Get("functions", null);
        Assert.NotNull(functions);
        Assert.Equal(1, functions.AsArray().Count);
        var f = functions.AsArray()[0].AsObject();
        Assert.Equal("add", f.Get("name", null)?.AsString());
        var sig = f.Get("signature", null)?.AsString();
        Assert.NotNull(sig);
        Assert.Contains("x: int", sig);
        Assert.Contains("y: int", sig);
        Assert.Contains("-> int", sig);
        var paramTypes = f.Get("parameterTypes", null);
        Assert.NotNull(paramTypes);
        Assert.Equal(ValueType.Array, paramTypes.Type);
        Assert.Equal(2, paramTypes.AsArray().Count);
        Assert.Equal("int", paramTypes.AsArray()[0].AsString());
        Assert.Equal("int", paramTypes.AsArray()[1].AsString());
        Assert.Equal("int", f.Get("returnType", null)?.AsString());
    }

    [Fact]
    public void GetParseErrors_WithValidSource_ReturnsEmptyParseErrors()
    {
        var source = "var x = 1; print(x);";
        var result = BuiltInFunctions.CallBuiltIn("getParseErrors", new List<RuntimeValue> { RuntimeValue.String(source) }, null);
        Assert.Equal(ValueType.Object, result.Type);
        var parseErrors = result.AsObject().Get("parseErrors", null);
        Assert.NotNull(parseErrors);
        Assert.Equal(0, parseErrors.AsArray().Count);
    }

    [Fact]
    public void GetParseErrors_WithInvalidSource_ReturnsNonEmptyParseErrors()
    {
        var source = "function f() { "; // missing }
        var result = BuiltInFunctions.CallBuiltIn("getParseErrors", new List<RuntimeValue> { RuntimeValue.String(source) }, null);
        Assert.Equal(ValueType.Object, result.Type);
        var parseErrors = result.AsObject().Get("parseErrors", null);
        Assert.NotNull(parseErrors);
        Assert.True(parseErrors.AsArray().Count >= 1);
        var first = parseErrors.AsArray()[0].AsObject();
        Assert.NotNull(first.Get("message", null)?.AsString());
    }

    [Fact]
    public void GetParseErrors_WithFilePath_ReadsFileAndReturnsParseErrors()
    {
        var path = Path.Combine(_testDirectory, "bad.malda");
        File.WriteAllText(path, "var x = ;");
        var result = BuiltInFunctions.CallBuiltIn("getParseErrors", new List<RuntimeValue> { RuntimeValue.String(path) }, null);
        var parseErrors = result.AsObject().Get("parseErrors", null);
        Assert.NotNull(parseErrors);
        Assert.True(parseErrors.AsArray().Count >= 1);
    }

    [Fact]
    public void GetParseErrors_WithPathTraversal_ReturnsSafeError()
    {
        var result = BuiltInFunctions.CallBuiltIn("getParseErrors", new List<RuntimeValue> { RuntimeValue.String("../../../etc.malda") }, null);
        var parseErrors = result.AsObject().Get("parseErrors", null);
        Assert.NotNull(parseErrors);
        Assert.Equal(1, parseErrors.AsArray().Count);
        Assert.Contains("suspicious", parseErrors.AsArray()[0].AsObject().Get("message", null)?.AsString() ?? "");
    }

    [Fact]
    public void CreateGetParseErrorsTool_InvokeWithSource_ReturnsParseErrorsShape()
    {
        var toolVal = BuiltInTools.CreateGetParseErrorsTool();
        var tool = toolVal.AsObject() as ToolInstance;
        Assert.NotNull(tool);
        Assert.Equal("get_parse_errors", tool.Name);
        var args = new JsonObject();
        args.Set("sourceOrFilePath", RuntimeValue.String("var x = 1;"));
        var result = tool.Execute(RuntimeValue.Object(args));
        Assert.Equal(ValueType.Object, result.Type);
        var parseErrors = result.AsObject().Get("parseErrors", null);
        Assert.NotNull(parseErrors);
        Assert.Empty(parseErrors.AsArray());
    }

    [Fact]
    public void Transpiled_GetSymbolsAndGetParseErrors_CompileAndRun()
    {
        var source = @"
            var sym = getSymbols(""function f() { }"");
            var hasClasses = sym[""classes""] != null;
            var errs = getParseErrors(""var x = 1;"");
            var hasParseErrorsKey = errs[""parseErrors""] != null;
            print(hasClasses ? ""ok"" : ""fail"");
            print(hasParseErrorsKey ? ""ok"" : ""fail"");
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Contains("ok", lines[0]);
        Assert.Contains("ok", lines[1]);
    }
}
