// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class MathNeuralBuiltInTests : TestBase
{
    [Fact]
    public void DotMatMulTransposeActivationsAndMse_Work()
    {
        var output = RunProgram("""
            print(math.dot([1.0, 2.0], [3.0, 4.0]));
            var p = math.matmul([[1.0, 2.0], [3.0, 4.0]], [[1.0, 0.0], [0.0, 1.0]]);
            print(p[1][1]);
            var v = math.matmul([[1.0, 2.0], [3.0, 4.0]], [1.0, 0.0]);
            print(v[1]);
            var r = math.matmul([1.0, 0.0], [[1.0, 2.0], [3.0, 4.0]]);
            print(r[1]);
            var t = math.transpose([[1.0, 2.0], [3.0, 4.0]]);
            print(t[0][1]);
            print(nn.relu(-2.0));
            print(nn.relu([ -1.0, 3.0 ])[1]);
            print(int(math.sigmoid(0.0) * 10));
            print(int(math.tanh(0.0) * 10));
            print(int(nn.mse([1.0, 3.0], [1.0, 1.0]) * 10));
            """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("11", output[0].Trim());
        Assert.Equal("4", output[1].Trim());
        Assert.Equal("3", output[2].Trim());
        Assert.Equal("2", output[3].Trim());
        Assert.Equal("3", output[4].Trim());
        Assert.Equal("0", output[5].Trim());
        Assert.Equal("3", output[6].Trim());
        Assert.Equal("5", output[7].Trim());
        Assert.Equal("0", output[8].Trim());
        Assert.Equal("20", output[9].Trim());
    }

    [Fact]
    public void MatMul_MatchesInTranspiledMode()
    {
        const string source = """
            var y = math.matmul([[1.0, 2.0]], [[3.0], [4.0]]);
            print(y[0][0]);
            print(math.dot([2.0, 0.0], [5.0, 9.0]));
            print(nn.mse(2.0, 0.0));
            """;
        var interpreted = RunProgram(source);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
    }

    [Fact]
    public void MatMul_RejectsInnerDimensionMismatch()
    {
        var ex = Assert.Throws<RuntimeException>(() =>
            BuiltInFunctions.CallBuiltIn("matmul", new List<RuntimeValue>
            {
                RuntimeValue.Array(new List<RuntimeValue>
                {
                    RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Float(1.0) })
                }),
                RuntimeValue.Array(new List<RuntimeValue>
                {
                    RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Float(1.0), RuntimeValue.Float(2.0) }),
                    RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Float(3.0), RuntimeValue.Float(4.0) })
                })
            }, null));
        Assert.Contains("inner dimensions must match", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReluAndMse_AreOnlyOnNn()
    {
        var ex = Assert.Throws<RuntimeException>(() => RunProgram("print(math.relu(1.0));"));
        Assert.Contains("relu", ex.Message, StringComparison.Ordinal);

        var output = RunProgram("""
            print(nn.relu(-1.0));
            print(int(nn.mse(2.0, 0.0)));
            """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("0", output[0].Trim());
        Assert.Equal("4", output[1].Trim());
    }

    [Fact]
    public void Sigmoid_SaturatesAtClamp()
    {
        var hi = BuiltInFunctions.CallBuiltIn("sigmoid", new List<RuntimeValue> { RuntimeValue.Float(50.0) }, null);
        var lo = BuiltInFunctions.CallBuiltIn("sigmoid", new List<RuntimeValue> { RuntimeValue.Float(-50.0) }, null);
        Assert.Equal(ValueType.Float, hi.Type);
        Assert.Equal(1.0, hi.AsFloat());
        Assert.Equal(0.0, lo.AsFloat());
    }
}
