// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Interpreter;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class DestructuringTests : TestBase
{
    [Fact]
    public void TestArrayDestructuring_Simple()
    {
        var source = @"
            var arr = [10, 20];
            var [x, y] = arr;
            print(x + y);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("30", output);
    }
    
    [Fact]
    public void TestArrayDestructuring_ThreeElements()
    {
        var source = @"
            var arr = [1, 2, 3];
            var [a, b, c] = arr;
            print(a + b + c);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("6", output);
    }
    
    [Fact]
    public void TestArrayDestructuring_WithRest()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var [first, second, ...rest] = arr;
            print(first);
            print(second);
            print(length(rest));
        ";
        
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]); // rest has 3 elements
    }
    
    [Fact]
    public void TestArrayDestructuring_RestOnly()
    {
        var source = @"
            var arr = [1, 2, 3];
            var [...all] = arr;
            print(length(all));
        ";
        
        var output = RunProgram(source);
        Assert.Equal("3", output);
    }
    
    [Fact]
    public void TestArrayDestructuring_LengthMismatch()
    {
        var source = @"
            try {
                var arr = [1, 2];
                var [x, y, z] = arr;
                print(""should not reach here"");
            } catch (e) {
                print(""error caught"");
            }
        ";
        
        var output = RunProgram(source);
        Assert.Equal("error caught", output);
    }
    
    [Fact]
    public void TestObjectDestructuring_Simple()
    {
        var source = @"
            var obj = { name: ""Alice"", age: 30 };
            var { name, age } = obj;
            print(name);
            print(age);
        ";
        
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("Alice", lines[0]);
        Assert.Equal("30", lines[1]);
    }
    
    [Fact]
    public void TestObjectDestructuring_WithRenaming()
    {
        var source = @"
            var obj = { name: ""Bob"", age: 25 };
            var { name: userName, age: userAge } = obj;
            print(userName);
            print(userAge);
        ";
        
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("Bob", lines[0]);
        Assert.Equal("25", lines[1]);
    }
    
    [Fact]
    public void TestObjectDestructuring_Nested()
    {
        var source = @"
            var obj = { user: { name: ""Charlie"", age: 35 }, role: ""admin"" };
            var { user: { name, age }, role } = obj;
            print(name);
            print(age);
            print(role);
        ";
        
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("Charlie", lines[0]);
        Assert.Equal("35", lines[1]);
        Assert.Equal("admin", lines[2]);
    }
    
    [Fact]
    public void TestObjectDestructuring_MissingProperty()
    {
        var source = @"
            try {
                var obj = { name: ""Alice"" };
                var { name, age } = obj;
                print(""should not reach here"");
            } catch (e) {
                print(""error caught"");
            }
        ";
        
        var output = RunProgram(source);
        Assert.Equal("error caught", output);
    }
    
    [Fact]
    public void TestDestructuringAssignment_Array()
    {
        var source = @"
            var x = 0;
            var y = 0;
            var arr = [10, 20];
            [x, y] = arr;
            print(x + y);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("30", output);
    }
    
    [Fact]
    public void TestDestructuringAssignment_Object()
    {
        var source = @"
            var name = """";
            var age = 0;
            var obj = { name: ""David"", age: 40 };
            { name, age } = obj;
            print(name);
            print(age);
        ";
        
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("David", lines[0]);
        Assert.Equal("40", lines[1]);
    }
    
    [Fact]
    public void TestDestructuring_FromFunctionReturn()
    {
        var source = @"
            function getPoint() {
                return { x: 5, y: 10 };
            }
            var { x, y } = getPoint();
            print(x + y);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("15", output);
    }
    
    [Fact]
    public void TestDestructuring_ComplexNested()
    {
        var source = @"
            var data = {
                user: {
                    name: ""Eve"",
                    address: {
                        city: ""Paris"",
                        country: ""France""
                    }
                },
                score: 100
            };
            var { user: { name, address: { city } }, score } = data;
            print(name);
            print(city);
            print(score);
        ";
        
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("Eve", lines[0]);
        Assert.Equal("Paris", lines[1]);
        Assert.Equal("100", lines[2]);
    }
    
    [Fact]
    public void TestDestructuring_ArrayWithNestedPatterns()
    {
        var source = @"
            var data = [{ x: 1, y: 2 }, { x: 3, y: 4 }];
            var [{ x: x1, y: y1 }, { x: x2, y: y2 }] = data;
            print(x1 + y1 + x2 + y2);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("10", output);
    }
    
    [Fact]
    public void TestDestructuring_WildcardInArray()
    {
        var source = @"
            var arr = [1, 2, 3];
            var [first, _, third] = arr;
            print(first + third);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("4", output);
    }
}
