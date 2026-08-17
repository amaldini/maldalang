// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using System;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspiledPatternMatchingTests
{
    [Fact]
    public void Transpiled_LiteralPatternMatching()
    {
        var source = @"
            var x = 42;
            var result = match x {
                case 42: ""matched 42"";
                case 10: ""matched 10"";
                default: ""no match"";
            };
            print(result);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("matched 42", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_IdentifierPattern_Binding()
    {
        var source = @"
            var x = 42;
            var result = match x {
                case y: y + 10;
            };
            print(result);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("52", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_ArrayPattern_Simple()
    {
        var source = @"
            var arr = [1, 2, 3];
            var result = match arr {
                case [1, 2, 3]: ""exact match"";
                case [x, y, z]: ""three elements"";
                default: ""no match"";
            };
            print(result);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("exact match", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_ArrayPattern_WithBinding()
    {
        var source = @"
            var arr = [10, 20];
            var result = match arr {
                case [x, y]: x + y;
                default: 0;
            };
            print(result);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("30", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_ObjectPattern_Simple()
    {
        var source = @"
            var obj = { name: ""Alice"", age: 30 };
            var result = match obj {
                case { name, age }: name + "" is "" + age;
                default: ""unknown"";
            };
            print(result);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Alice is 30", result.StdOut);
    }
    
    [Fact]
    public void Transpiled_MatchExpression_DefaultCase()
    {
        var source = @"
            var x = 99;
            var result = match x {
                case 1: ""one"";
                case 2: ""two"";
                default: ""other"";
            };
            print(result);
        ";
        
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("other", result.StdOut);
    }

    [Fact]
    public void Transpiled_SumType_Result_Ok_Err()
    {
        var source = @"
            type Result = Ok(value) | Err(errMsg);
            function divide(a, b) {
                if (b == 0) return Err(""divide by zero"");
                return Ok(a / b);
            }
            var r = divide(10, 2);
            var result = match r {
                case Ok(v): ""ok: "" + v;
                case Err(msg): ""error: "" + msg;
            };
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ok: 5", result.StdOut);
    }

    [Fact]
    public void Transpiled_SumType_Err_Branch()
    {
        var source = @"
            type Result = Ok(value) | Err(errMsg);
            function divide(a, b) {
                if (b == 0) return Err(""divide by zero"");
                return Ok(a / b);
            }
            var r = divide(10, 0);
            var result = match r {
                case Ok(v): ""ok"";
                case Err(msg): ""error: "" + msg;
            };
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("error: divide by zero", result.StdOut);
    }

    [Fact]
    public void Transpiled_MatchExpression_BlockBody_LastExpressionWins()
    {
        var source = @"
            var x = 42;
            var result = match x {
                case 42: {
                    print(""side effect"");
                    ""result"";
                }
                default: ""no match"";
            };
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("side effect", result.StdOut);
        Assert.Contains("result", result.StdOut);
    }

    [Fact]
    public void Transpiled_MatchExpression_DefaultBlock_LastExpressionWins()
    {
        var source = @"
            var x = 99;
            var result = match x {
                case 1: ""one"";
                default: {
                    print(""fallback"");
                    ""other"";
                }
            };
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("fallback", result.StdOut);
        Assert.Contains("other", result.StdOut);
    }

    [Fact]
    public void Transpiled_MatchGuard_BindsThenFallsThrough()
    {
        var source = @"
            var n = 3;
            var result = match n {
                case x if x > 10: ""big"";
                case x: ""small"";
            };
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("small", result.StdOut);
    }

    [Fact]
    public void Transpiled_MatchGuard_TakesArmWhenTrue()
    {
        var source = @"
            var n = 20;
            var result = match n {
                case x if x > 10: ""big"";
                case x: ""small"";
            };
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("big", result.StdOut);
    }

    [Fact]
    public void Transpiled_MatchGuard_VariantPayload()
    {
        var source = @"
            type Result = Ok(value) | Err(errMsg);
            var r = Ok(-1);
            var result = match r {
                case Ok(v) if v >= 0: ""ok"";
                case Ok(v): ""negative"";
                case Err(msg): msg;
            };
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("negative", result.StdOut);
    }

    [Fact]
    public void Transpiled_MatchGuard_ObjectBinding()
    {
        var source = @"
            var obj = { lo: 5, hi: 2 };
            var result = match obj {
                case { lo, hi } if lo <= hi: ""ordered"";
                case { lo, hi }: ""swapped"";
            };
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("swapped", result.StdOut);
    }
}
