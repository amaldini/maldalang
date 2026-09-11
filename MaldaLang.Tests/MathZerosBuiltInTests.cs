// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class MathZerosBuiltInTests : TestBase
{
    [Fact]
    public void Zeros_BuildsIndependent1DAnd2DFloatArrays()
    {
        var output = RunProgram("""
            var v = math.zeros(3);
            print(v.length);
            print(v[0] == 0.0);
            print(v[2] == 0.0);
            var m = math.zeros(2, 3);
            print(m.length);
            print(m[0].length);
            m[0][0] = 1.0;
            print(m[1][0]);
            print(math.zeros(0).length);
            print(math.zeros(2, 0)[0].length);
            print(math.zeros(0, 4).length);
            print(math.zeros(3.0)[1] == 0.0);
            """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("3", output[0].Trim());
        Assert.Equal("true", output[1].Trim());
        Assert.Equal("true", output[2].Trim());
        Assert.Equal("2", output[3].Trim());
        Assert.Equal("3", output[4].Trim());
        Assert.Equal("0", output[5].Trim());
        Assert.Equal("0", output[6].Trim());
        Assert.Equal("0", output[7].Trim());
        Assert.Equal("0", output[8].Trim());
        Assert.Equal("true", output[9].Trim());
    }

    [Fact]
    public void Zeros_MatchesInTranspiledMode()
    {
        const string source = """
            var m = math.zeros(2, 2);
            m[0][0] = 5.0;
            print(m.length);
            print(m[0].length);
            print(m[1][0]);
            print(math.zeros(4).length);
            """;
        var interpreted = RunProgram(source);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
    }

    [Fact]
    public void Zeros_RejectsBadArityTypesAndNegatives()
    {
        var empty = Assert.Throws<RuntimeException>(() =>
            BuiltInFunctions.CallBuiltIn("zeros", new List<RuntimeValue>(), null));
        Assert.Contains("1-2 arguments: (n, cols?)", empty.Message, StringComparison.Ordinal);

        var tooMany = Assert.Throws<RuntimeException>(() =>
            BuiltInFunctions.CallBuiltIn("zeros", new List<RuntimeValue>
            {
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(1)
            }, null));
        Assert.Contains("1-2 arguments: (n, cols?)", tooMany.Message, StringComparison.Ordinal);

        var fractional = Assert.Throws<RuntimeException>(() =>
            BuiltInFunctions.CallBuiltIn("zeros", new List<RuntimeValue> { RuntimeValue.Float(1.5) }, null));
        Assert.Contains("integer dimensions", fractional.Message, StringComparison.Ordinal);

        var negative = Assert.Throws<RuntimeException>(() =>
            BuiltInFunctions.CallBuiltIn("zeros", new List<RuntimeValue> { RuntimeValue.Integer(-1) }, null));
        Assert.Contains(">= 0", negative.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Zeros_CellsAreFloats()
    {
        var vector = BuiltInFunctions.CallBuiltIn(
            "zeros",
            new List<RuntimeValue> { RuntimeValue.Integer(1) },
            null);
        Assert.Equal(ValueType.Array, vector.Type);
        Assert.Equal(ValueType.Float, vector.AsArray()[0].Type);

        var matrix = BuiltInFunctions.CallBuiltIn(
            "zeros",
            new List<RuntimeValue> { RuntimeValue.Integer(1), RuntimeValue.Integer(1) },
            null);
        Assert.Equal(ValueType.Float, matrix.AsArray()[0].AsArray()[0].Type);
    }
}
