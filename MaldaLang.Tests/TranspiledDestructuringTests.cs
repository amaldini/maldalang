// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspiledDestructuringTests
{
    [Fact]
    public void Transpiled_ArrayDestructuring_Simple()
    {
        var source = @"
            var arr = [10, 20];
            var [x, y] = arr;
            print(x + y);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("30", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_ArrayDestructuring_ThreeElements()
    {
        var source = @"
            var arr = [1, 2, 3];
            var [a, b, c] = arr;
            print(a + b + c);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("6", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_ObjectDestructuring_Simple()
    {
        var source = @"
            var obj = { name: ""Alice"", age: 30 };
            var { name, age } = obj;
            print(name);
            print(age);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Alice", result.StdOut);
        Assert.Contains("30", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_DestructuringAssignment_Array()
    {
        var source = @"
            var x = 0;
            var y = 0;
            var arr = [10, 20];
            [x, y] = arr;
            print(x + y);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("30", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_DestructuringAssignment_Object()
    {
        var source = @"
            var name = """";
            var age = 0;
            var obj = { name: ""David"", age: 40 };
            { name, age } = obj;
            print(name);
            print(age);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("David", result.StdOut);
        Assert.Contains("40", result.StdOut);
    }
}
