// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using Xunit;

namespace MaldaLang.Tests;

public class StrictCompileTests
{
    [Theory]
    [InlineData("TranspileToCSharp", false, false, true)]
    [InlineData("TranspileToDll", false, false, true)]
    [InlineData("TranspileToCSharp", false, true, false)]
    [InlineData("TranspileToCSharp", true, false, true)]
    [InlineData("Interpreter", false, false, false)]
    [InlineData("Interpreter", true, false, true)]
    [InlineData("JavaScript", false, false, false)]
    public void ShouldAnalyze_FollowsTranspileDefaultAndFlags(
        string mode, bool strict, bool lenient, bool expected)
    {
        Assert.Equal(expected, CompileStrictTypesGate.ShouldAnalyze(mode, strict, lenient));
    }

    [Fact]
    public void TryGetRejection_HintMismatch_RefusesEmit()
    {
        var rejected = CompileStrictTypesGate.TryGetRejection(
            "var n: int = \"abc\";\n",
            "strict_compile.malda",
            out var errorText);

        Assert.True(rejected);
        Assert.Contains("int", errorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetRejection_MatchingHint_AllowsEmit()
    {
        var rejected = CompileStrictTypesGate.TryGetRejection(
            "var n: int = 1;\nio.print(n);\n",
            "strict_compile_ok.malda",
            out _);

        Assert.False(rejected);
    }
}
