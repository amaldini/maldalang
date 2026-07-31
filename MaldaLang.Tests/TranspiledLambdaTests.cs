// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspiledLambdaTests
{
    [Fact]
    public void Transpiled_NamedFunction_LastExpressionWins()
    {
        var source = @"
            function process(x) {
                print(""side effect"");
                x * 2;
            }
            var result = process(5);
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("side effect", result.StdOut);
        Assert.Contains("10", result.StdOut);
    }

    [Fact]
    public void Transpiled_Lambda_BlockBody_LastExpressionWins()
    {
        var source = @"
            var f = (x) => {
                print(""side effect"");
                x * 2;
            };
            var result = f(5);
            print(result);
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("side effect", result.StdOut);
        Assert.Contains("10", result.StdOut);
    }

}
