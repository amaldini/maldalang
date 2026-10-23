// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Scaffolding;
using MaldaLang.Testing;
using MaldaLang.Tests.Conformance.Tier0;
using Xunit;

namespace MaldaLang.Tests;

/// <summary><c>malda test --target js</c>: each test file compiles to JavaScript and runs in Node.</summary>
[Collection("Sequential")]
public class TestCommandJavaScriptTargetTests
{
    private static JavaScriptCompileResult Compile(string sourcePath, string outputPath)
    {
        var result = new Compiler.Compiler().CompileToJavaScript(sourcePath, outputPath);
        return new JavaScriptCompileResult(result.Success, result.OutputPath, result.ErrorMessage);
    }

    private static (int Code, string Output) RunTests(string directory, params string[] extraArgs)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var args = new[] { directory }.Concat(extraArgs).ToArray();
        var code = new TestCommandRunner(Compile).Run(args, output, error);
        return (code, output + error.ToString());
    }

    private static string NewTestDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "malda_test_js_target", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void PassingAndFailingFiles_ReportOnJavaScript_WithMaldaLocation()
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var directory = NewTestDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "ok.test.malda"), "var xs = [1, 2, 3];\nassert(xs.length == 3, \"length\");\n");
            File.WriteAllText(Path.Combine(directory, "bad.test.malda"), "function f() {\n    assert(1 == 2, \"one is not two\");\n}\nf();\n");

            var (code, output) = RunTests(directory, "--target", "js");

            Assert.Equal(1, code);
            Assert.Contains("PASS", output);
            Assert.Contains("one is not two", output);
            Assert.Contains("bad.test.malda:2:", output);
            Assert.Contains("total=2 passed=1 failed=1", output);
            // The program copy written beside each test is removed afterwards.
            Assert.Empty(Directory.GetFiles(directory, ".malda-test-*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ThrownValue_IsReportedLikeTheInterpreter()
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var directory = NewTestDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "throw.test.malda"),
                "function boom() {\n    throw \"late failure\";\n}\nboom();\n");

            var (code, output) = RunTests(directory, "--target", "js");

            Assert.Equal(1, code);
            Assert.Contains("late failure (throw.test.malda:2:", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PropertyTests_AreReportedAsInterpreterOnly()
    {
        var directory = NewTestDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "prop.test.malda"),
                "property stays(x) {\n    return x == x;\n}\n");

            var (code, output) = RunTests(directory, "--target", "js");

            Assert.Equal(1, code);
            Assert.Contains("interpreter only", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnknownTarget_IsAnError()
    {
        var (code, output) = RunTests(Path.GetTempPath(), "--target", "wasm");

        Assert.Equal(1, code);
        Assert.Contains("unsupported --target 'wasm'", output);
    }
}
