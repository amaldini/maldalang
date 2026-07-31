// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Interpreter;

namespace MaldaLang.Tests;

/// <summary>
/// Extended pattern-matching tests. Core Tier 0 coverage lives in
/// <c>conformance/tier0/cases/match-*.malda</c> (see <c>Tier0MaldaConformanceTests</c>).
/// </summary>
[Collection("Sequential")]
public class PatternMatchingTests : TestBase
{
    [Fact]
    public void TestLiteralPatternMatching()
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
        
        var output = RunProgram(source);
        Assert.Equal("matched 42", output);
    }
    
    [Fact]
    public void TestLiteralPatternMatching_String()
    {
        var source = @"
            var msg = ""hello"";
            var result = match msg {
                case ""hello"": ""greeting"";
                case ""goodbye"": ""farewell"";
                default: ""unknown"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("greeting", output);
    }
    
    [Fact]
    public void TestLiteralPatternMatching_Boolean()
    {
        var source = @"
            var flag = true;
            var result = match flag {
                case true: ""yes"";
                case false: ""no"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("yes", output);
    }
    
    [Fact]
    public void TestLiteralPatternMatching_Null()
    {
        var source = @"
            var x = null;
            var result = match x {
                case null: ""is null"";
                default: ""not null"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("is null", output);
    }
    
    [Fact]
    public void TestIdentifierPattern_Binding()
    {
        var source = @"
            var x = 42;
            var result = match x {
                case y: y + 10;
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("52", output);
    }
    
    [Fact]
    public void TestWildcardPattern()
    {
        var source = @"
            var x = 42;
            var result = match x {
                case 10: ""ten"";
                case _: ""other"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("other", output);
    }
    
    [Fact]
    public void TestArrayPattern_Simple()
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
        
        var output = RunProgram(source);
        Assert.Equal("exact match", output);
    }
    
    [Fact]
    public void TestArrayPattern_WithBinding()
    {
        var source = @"
            var arr = [10, 20];
            var result = match arr {
                case [x, y]: x + y;
                default: 0;
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("30", output);
    }
    
    [Fact]
    public void TestArrayPattern_WithRest()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var result = match arr {
                case [first, second, ...rest]: first + second + length(rest);
                default: 0;
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("6", output); // 1 + 2 + 3 (length of rest)
    }
    
    [Fact]
    public void TestArrayPattern_LengthMismatch()
    {
        var source = @"
            var arr = [1, 2];
            var result = match arr {
                case [x, y, z]: ""three"";
                case [x, y]: ""two"";
                default: ""other"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("two", output);
    }
    
    [Fact]
    public void TestObjectPattern_Simple()
    {
        var source = @"
            var obj = { type: ""Start"", value: 42 };
            var result = match obj {
                case { type: ""Start"", value: v }: v;
                case { type: ""Stop"" }: ""stopped"";
                default: ""unknown"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("42", output);
    }
    
    [Fact]
    public void TestObjectPattern_Shorthand()
    {
        var source = @"
            var obj = { name: ""Alice"", age: 30 };
            var result = match obj {
                case { name, age }: name + "" is "" + age;
                default: ""unknown"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("Alice is 30", output);
    }
    
    [Fact]
    public void TestObjectPattern_Nested()
    {
        var source = @"
            var obj = { user: { name: ""Bob"", age: 25 } };
            var result = match obj {
                case { user: { name, age } }: name + "" is "" + age;
                default: ""unknown"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("Bob is 25", output);
    }
    
    [Fact]
    public void TestObjectPattern_MissingProperty()
    {
        var source = @"
            var obj = { name: ""Alice"" };
            var result = match obj {
                case { name, age }: ""has age"";
                case { name }: ""no age"";
                default: ""unknown"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("no age", output);
    }
    
    [Fact]
    public void TestMatchExpression_DefaultCase()
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
        
        var output = RunProgram(source);
        Assert.Equal("other", output);
    }
    
    [Fact]
    public void TestMatchExpression_NoDefault_NoMatch()
    {
        var source = @"
            var x = 99;
            try {
                var result = match x {
                    case 1: ""one"";
                    case 2: ""two"";
                };
                print(""should not reach here"");
            } catch (e) {
                print(""error caught"");
            }
        ";
        
        var output = RunProgram(source);
        Assert.Equal("error caught", output);
    }
    
    [Fact]
    public void TestMatchExpression_StatementBody()
    {
        var source = @"
            var x = 42;
            match x {
                case 42: print(""matched 42"");
                default: print(""no match"");
            }
        ";
        
        var output = RunProgram(source);
        Assert.Equal("matched 42", output);
    }
    
    [Fact]
    public void TestMatchExpression_MultipleCases()
    {
        var source = @"
            var x = 2;
            var result = match x {
                case 1: ""first"";
                case 2: ""second"";
                case 3: ""third"";
                default: ""other"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("second", output);
    }
    
    [Fact]
    public void TestMatchExpression_FirstMatchWins()
    {
        var source = @"
            var x = 42;
            var result = match x {
                case y: ""bound to y"";
                case 42: ""matched 42"";
                default: ""default"";
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("bound to y", output); // Identifier pattern matches first
    }
    
    [Fact]
    public void TestMatchExpression_NestedPatterns()
    {
        // Nested patterns: array of objects, match first element's type and bind value
        var source = @"
            var data = [{ type: ""A"", value: 1 }, { type: ""B"", value: 2 }];
            var result = match data {
                case [{ type: ""A"", value: v }, ...rest]: v;
                default: 0;
            };
            print(result);
        ";
        
        var output = RunProgram(source);
        Assert.Equal("1", output);
    }

    [Fact]
    public void TestMatchExpression_ArrayPatternOnly()
    {
        // Simpler: array pattern without nested object, to isolate array+rest matching
        var source = @"
            var data = [1, 2, 3];
            var result = match data {
                case [v, ...rest]: v;
                default: 0;
            };
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("1", output);
    }

    [Fact]
    public void TestSumType_TypeDeclaration_And_Constructors()
    {
        var source = @"
            type Result = Ok(value) | Err(message);
            var r = Ok(42);
            print(r);
        ";
        var output = RunProgram(source);
        Assert.Contains("Ok(42)", output);
    }

    [Fact]
    public void TestSumType_Match_Ok_And_Err()
    {
        var source = @"
            type Result = Ok(value) | Err(message);
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
        var output = RunProgram(source);
        Assert.Equal("ok: 5", output);
    }

    [Fact]
    public void TestSumType_Match_Err_Branch()
    {
        var source = @"
            type Result = Ok(value) | Err(message);
            function divide(a, b) {
                if (b == 0) return Err(""divide by zero"");
                return Ok(a / b);
            }
            var r = divide(10, 0);
            var result = match r {
                case Ok(v): ""ok: "" + v;
                case Err(msg): ""error: "" + msg;
            };
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("error: divide by zero", output);
    }

    [Fact]
    public void TestSumType_ZeroArgConstructor()
    {
        var source = @"
            type Option = Some(x) | None();
            var n = None();
            var result = match n {
                case Some(v): ""some "" + v;
                case None(): ""none"";
            };
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("none", output);
    }

    [Fact]
    public void TestMatchExpression_BlockBody_LastExpressionWins()
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
        var output = RunProgram(source);
        Assert.Equal("side effect\nresult", output);
    }

    [Fact]
    public void TestMatchExpression_BlockBody_LastStatementNotExpression()
    {
        var source = @"
            var x = 42;
            var result = match x {
                case 42: {
                    print(""only side effect"");
                }
                default: ""no match"";
            };
            print(result == null ? ""null"" : ""not null"");
        ";
        var output = RunProgram(source);
        Assert.Equal("only side effect\nnull", output);
    }

    [Fact]
    public void TestMatchExpression_DefaultBlock_LastExpressionWins()
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
        var output = RunProgram(source);
        Assert.Equal("fallback\nother", output);
    }
}
