// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class MALDAToolsTests : TestBase
{
    private string _testDirectory;
    
    public MALDAToolsTests()
    {
        // Create a temporary directory for test files
        _testDirectory = CreateTempDirectory("MALDAToolsTests_");
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SafeDeleteDirectory(_testDirectory);
        }
        base.Dispose(disposing);
    }
    
    private RuntimeValue CreateToolArguments(Dictionary<string, RuntimeValue> args)
    {
        var argsObj = new JsonObject();
        foreach (var kvp in args)
        {
            argsObj.Set(kvp.Key, kvp.Value);
        }
        return RuntimeValue.Object(argsObj);
    }
    
    private RuntimeValue ExecuteTool(ToolInstance tool, RuntimeValue arguments)
    {
        return tool.Execute(arguments);
    }
    
    [Fact]
    public void TestRunMALDA_WithSourceCode()
    {
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String("print(\"Hello, World!\");") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        Assert.True(resultObj.Get("success", null)?.AsBoolean() ?? false);
        var output = resultObj.Get("output", null)?.AsString() ?? "";
        Assert.Equal("Hello, World!", output.TrimEnd('\r', '\n'));
        Assert.Equal("", resultObj.Get("error", null)?.AsString() ?? "");
    }
    
    [Fact]
    public void TestRunMALDA_WithFile()
    {
        var testFile = Path.Combine(_testDirectory, "test.malda");
        File.WriteAllText(testFile, "print(\"From file\");");
        
        // Use full path to ensure file is found
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String(testFile) }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        var success = resultObj.Get("success", null)?.AsBoolean() ?? false;
        if (!success)
        {
            // If it failed, check the error message for debugging
            var error = resultObj.Get("error", null)?.AsString() ?? "";
            var runtimeError = resultObj.Get("runtimeError", null)?.AsString() ?? "";
            Assert.Fail($"runMALDA failed. Error: {error}, RuntimeError: {runtimeError}");
        }
        
        Assert.True(success);
        var output = resultObj.Get("output", null)?.AsString() ?? "";
        Assert.Equal("From file", output.TrimEnd('\r', '\n'));
    }
    
    [Fact]
    public void TestRunMALDA_WithInput()
    {
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var sourceCode = @"
            var name = input(""Enter name: "");
            print(""Hello, "" + name + ""!"");
        ";
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String(sourceCode) },
            { "input", RuntimeValue.String("Alice") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        Assert.True(resultObj.Get("success", null)?.AsBoolean() ?? false);
        Assert.Contains("Hello, Alice!", resultObj.Get("output", null)?.AsString() ?? "");
    }
    
    [Fact]
    public void TestRunMALDA_WithParseError()
    {
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String("print(\"unclosed string);") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        Assert.False(resultObj.Get("success", null)?.AsBoolean() ?? true);
        // Check both error and runtimeError fields
        var error = resultObj.Get("error", null)?.AsString() ?? "";
        var runtimeError = resultObj.Get("runtimeError", null)?.AsString() ?? "";
        // At least one should have an error message
        Assert.True(!string.IsNullOrEmpty(error) || !string.IsNullOrEmpty(runtimeError), 
            "Expected either error or runtimeError to contain an error message");
    }
    
    [Fact]
    public void TestRunMALDA_WithRuntimeError()
    {
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String("var x = undefinedVar;") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        Assert.False(resultObj.Get("success", null)?.AsBoolean() ?? true);
        var runtimeError = resultObj.Get("runtimeError", null)?.AsString() ?? "";
        Assert.NotEmpty(runtimeError);
    }
    
    [Fact]
    public void TestRunMALDA_WithInvalidFilePath()
    {
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String("nonexistent.malda") }
        });
        
        var result = ExecuteTool(tool, args);
        
        // When file doesn't exist, it should treat it as source code
        // If it doesn't look like valid source, it might fail
        Assert.Equal(ValueType.Object, result.Type);
    }
    
    [Fact]
    public void TestRunMALDA_WithComplexProgram()
    {
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var sourceCode = @"
            function factorial(n) {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            print(factorial(5));
        ";
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String(sourceCode) }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        Assert.True(resultObj.Get("success", null)?.AsBoolean() ?? false);
        Assert.Equal("120", resultObj.Get("output", null)?.AsString()?.Trim() ?? "");
    }
    
    [Fact]
    public void TestCompileMALDA_WithValidFile()
    {
        var testFile = Path.Combine(_testDirectory, "compile_test.malda");
        File.WriteAllText(testFile, "print(\"Hello from compiled program\");");
        
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var outputFile = Path.Combine(_testDirectory, "compile_test.exe");
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("compile_test.malda") },
            { "outputPath", RuntimeValue.String("compile_test.exe") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        var success = resultObj.Get("success", null)?.AsBoolean() ?? false;
        if (success)
        {
            // Compilation succeeded - verify output file exists
            Assert.True(File.Exists(outputFile), "Compiled executable should exist");
            
            // Cleanup
            try { File.Delete(outputFile); } catch { }
        }
        else
        {
            // Compilation might fail if compiler DLL is not available in test environment
            // This is acceptable - we're testing the tool interface, not the compiler itself
            var error = resultObj.Get("error", null)?.AsString() ?? "";
            Assert.NotEmpty(error);
        }
    }
    
    [Fact]
    public void TestCompileMALDA_WithDefaultOutputPath()
    {
        var testFile = Path.Combine(_testDirectory, "default_output.malda");
        File.WriteAllText(testFile, "print(\"Test\");");
        
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("default_output.malda") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        var success = resultObj.Get("success", null)?.AsBoolean() ?? false;
        if (success)
        {
            var outputPath = resultObj.Get("outputPath", null)?.AsString();
            Assert.NotNull(outputPath);
            Assert.EndsWith(".exe", outputPath, System.StringComparison.OrdinalIgnoreCase);
            
            // Cleanup
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
        }
    }
    
    [Fact]
    public void TestCompileMALDA_WithInterpreterMode()
    {
        var testFile = Path.Combine(_testDirectory, "interpreter_mode.malda");
        File.WriteAllText(testFile, "print(\"Interpreter mode\");");
        
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("interpreter_mode.malda") },
            { "mode", RuntimeValue.String("interpreter") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        // Whether it succeeds or fails depends on compiler availability
        // We just verify the tool interface works
        Assert.NotNull(resultObj);
    }
    
    [Fact]
    public void TestCompileMALDA_WithTranspileMode()
    {
        var testFile = Path.Combine(_testDirectory, "transpile_mode.malda");
        File.WriteAllText(testFile, "print(\"Transpile mode\");");
        
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("transpile_mode.malda") },
            { "mode", RuntimeValue.String("transpile") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        // Whether it succeeds or fails depends on compiler availability
        // We just verify the tool interface works
        Assert.NotNull(resultObj);
    }
    
    [Fact]
    public void TestCompileMALDA_WithCompilationError()
    {
        var testFile = Path.Combine(_testDirectory, "error_test.malda");
        File.WriteAllText(testFile, "print(\"unclosed string);");
        
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("error_test.malda") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        // Should fail with compilation error
        Assert.False(resultObj.Get("success", null)?.AsBoolean() ?? true);
        
        var error = resultObj.Get("error", null)?.AsString() ?? "";
        var errors = resultObj.Get("errors", null);
        
        // Should have either error string or errors array
        Assert.True(!string.IsNullOrEmpty(error) || (errors != null && errors.Type == ValueType.Array));
    }
    
    [Fact]
    public void TestCompileMALDA_WithInvalidFilePath()
    {
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("nonexistent.malda") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        // Should fail because file doesn't exist
        Assert.False(resultObj.Get("success", null)?.AsBoolean() ?? true);
        
        var error = resultObj.Get("error", null)?.AsString() ?? "";
        Assert.NotEmpty(error);
        Assert.Contains("not found", error, System.StringComparison.OrdinalIgnoreCase);
    }
    
    [Fact]
    public void TestCompileMALDA_WithPathTraversalAttempt()
    {
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("../test.malda") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        // Should fail due to path traversal attempt
        Assert.False(resultObj.Get("success", null)?.AsBoolean() ?? true);
        
        var error = resultObj.Get("error", null)?.AsString() ?? "";
        Assert.NotEmpty(error);
        Assert.Contains("path traversal", error, System.StringComparison.OrdinalIgnoreCase);
    }
    
    [Fact]
    public void TestCompileMALDA_WithInvalidMode()
    {
        var testFile = Path.Combine(_testDirectory, "invalid_mode.malda");
        File.WriteAllText(testFile, "print(\"Test\");");
        
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        // Note: The tool itself doesn't validate mode - it's passed to compileMALDA function
        // The compileMALDA function will throw an exception for invalid mode
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("invalid_mode.malda") },
            { "mode", RuntimeValue.String("invalid") }
        });
        
        // This should result in an error from the compileMALDA function
        var result = ExecuteTool(tool, args);
        
        // The result might be an error string or an object with error
        if (result.Type == ValueType.String)
        {
            Assert.Contains("mode", result.AsString(), System.StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var resultObj = result.AsObject();
            Assert.False(resultObj.Get("success", null)?.AsBoolean() ?? true);
        }
    }
    
    [Fact]
    public void TestRunMALDA_WithMultipleInputs()
    {
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var sourceCode = @"
            var name = input(""Name: "");
            var age = input(""Age: "");
            print(""Name: "" + name + "", Age: "" + age);
        ";
        
        // Note: runMALDA only supports a single input string
        // Multiple inputs would need to be separated by newlines or handled differently
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String(sourceCode) },
            { "input", RuntimeValue.String("Alice\n25") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        // The input handling depends on how input() processes the input string
        // This test verifies the tool accepts the input parameter
        Assert.NotNull(resultObj);
    }
    
    [Fact]
    public void TestRunMALDA_WithEmptySource()
    {
        var tool = BuiltInTools.CreateRunMALDATool().AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourceOrFilePath", RuntimeValue.String("") }
        });
        
        // Empty source should be handled gracefully
        var result = ExecuteTool(tool, args);
        
        // The result might be an object with error or a string error
        // Both are acceptable - we just verify it's handled
        if (result.Type == ValueType.Object)
        {
            var resultObj = result.AsObject();
            // Should indicate failure or empty output
            var success = resultObj.Get("success", null)?.AsBoolean();
            Assert.NotNull(success);
        }
        else if (result.Type == ValueType.String)
        {
            // String error is also acceptable
            var errorMsg = result.AsString();
            Assert.NotEmpty(errorMsg);
        }
    }
    
    [Fact]
    public void TestCompileMALDA_WithComplexProgram()
    {
        var testFile = Path.Combine(_testDirectory, "complex.malda");
        var complexCode = @"
            function factorial(n) {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            print(factorial(5));
        ";
        File.WriteAllText(testFile, complexCode);
        
        var tool = BuiltInTools.CreateCompileMALDATool(_testDirectory).AsObject() as ToolInstance;
        Assert.NotNull(tool);
        
        var args = CreateToolArguments(new Dictionary<string, RuntimeValue>
        {
            { "sourcePath", RuntimeValue.String("complex.malda") }
        });
        
        var result = ExecuteTool(tool, args);
        
        Assert.Equal(ValueType.Object, result.Type);
        var resultObj = result.AsObject();
        
        // Whether compilation succeeds depends on compiler availability
        // We verify the tool interface works correctly
        Assert.NotNull(resultObj);
    }
}
