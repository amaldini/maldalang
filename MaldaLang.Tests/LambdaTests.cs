// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class LambdaTests : TestBase
{
    [Fact]
    public void NamedFunction_LastExpressionWins()
    {
        var source = @"
            function process(x) {
                print(""side effect"");
                x * 2;
            }
            var result = process(5);
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("side effect\n10", output);
    }

    [Fact]
    public void Method_LastExpressionWins()
    {
        var source = @"
            class Calculator {
                function double(x) {
                    print(""doubling "" + x);
                    x * 2;
                }
            }
            var calc = new Calculator();
            var result = calc.double(7);
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("doubling 7\n14", output);
    }

    [Fact]
    public void Lambda_BlockBody_LastExpressionWins()
    {
        var source = @"
            var f = (x) => {
                print(""side effect"");
                x * 2;
            };
            var result = f(5);
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("side effect\n10", output);
    }

    [Fact]
    public void Lambda_BlockBody_LastStatementNotExpression_ReturnsNull()
    {
        var source = @"
            var f = (x) => {
                print(""only side effect"");
            };
            var result = f(5);
            print(result == null ? ""null"" : ""not null"");
        ";
        var output = RunProgram(source);
        Assert.Equal("only side effect\nnull", output);
    }

    [Fact]
    public void Lambda_BlockBody_LastExpressionWins_InMap()
    {
        var source = @"
            var nums = [1, 2, 3];
            var doubled = nums.map(x => {
                print(""processing "" + x);
                x * 2;
            });
            print(doubled);
        ";
        var output = RunProgram(source);
        Assert.Contains("processing 1", output);
        Assert.Contains("processing 2", output);
        Assert.Contains("processing 3", output);
        Assert.Contains("[2, 4, 6]", output);
    }
}
