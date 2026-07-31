// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

public class NumericCoercionTests : TestBase
{
    [Fact]
    public void Repeat_AcceptsWholeValuedFloatFromFloor()
    {
        var output = RunProgram("""
            print(str.repeat("*", math.floor(7 / 2)));
            """);
        Assert.Equal("***", output.Trim());
    }

    [Fact]
    public void Repeat_RejectsFractionalFloat()
    {
        var ex = Assert.ThrowsAny<Exception>(() => RunProgram("""
            print(str.repeat("*", 2.7));
            """));
        Assert.Contains("repeat() expects (string, integer)", ex.Message);
    }

    [Fact]
    public void ArrayIndex_AcceptsWholeValuedFloat()
    {
        var output = RunProgram("""
            var a = ["a", "b", "c"];
            print(a[math.floor(1.9)]);
            """);
        Assert.Equal("b", output.Trim());
    }

    [Fact]
    public void TryAsInteger_AcceptsIntegerAndWholeFloatOnly()
    {
        Assert.True(NumericCoercion.TryAsInteger(RuntimeValue.Integer(3), out var fromInt));
        Assert.Equal(3, fromInt);

        Assert.True(NumericCoercion.TryAsInteger(RuntimeValue.Float(3.0), out var fromFloat));
        Assert.Equal(3, fromFloat);

        Assert.False(NumericCoercion.TryAsInteger(RuntimeValue.Float(2.7), out _));
        Assert.False(NumericCoercion.TryAsInteger(RuntimeValue.String("3"), out _));
    }
}
