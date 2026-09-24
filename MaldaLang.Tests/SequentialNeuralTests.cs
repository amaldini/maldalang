// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class SequentialNeuralTests : TestBase
{
    [Fact]
    public void DenseForward_UsesAssignedWeights()
    {
        var output = RunProgram("""
            var layer = new Dense(1, 1, "linear", 0.0);
            layer.weights[0][0] = 2.0;
            layer.bias[0] = 1.0;
            print(int(layer.forward([3.0])[0]));
            """).Trim();

        Assert.Equal("7", output);
    }

    [Fact]
    public void DenseBackward_MatchesDenseBackwardBuiltin()
    {
        var output = RunProgram("""
            var layer = new Dense(2, 1, "relu", 0.0);
            layer.weights[0][0] = 0.5;
            layer.weights[1][0] = 1.0;
            layer.bias[0] = 0.25;
            var y = layer.forward([1.0, 2.0]);
            var dLayer = layer.backward([1.0]);
            var back = nn.denseBackward([1.0, 2.0], layer.weights, [1.0], "relu", [2.75]);
            print(int(y[0] * 100));
            print(int(dLayer[0] * 100));
            print(int(dLayer[1] * 100));
            print(int(back.dInput[0] * 100));
            print(int(back.dInput[1] * 100));
            print(int(back.dWeights[0][0] * 100));
            """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(new[] { "275", "50", "100", "50", "100", "100" }, output.Select(line => line.Trim()).ToArray());
    }

    [Fact]
    public void SequentialFit_LearnsXor()
    {
        var output = RunProgram("""
            math.seed(42);
            var net = new Sequential([
                new Dense(2, 4, "sigmoid", 1.0),
                new Dense(4, 1, "sigmoid", 1.0)
            ]);
            var inputs = [[0.0, 0.0], [0.0, 1.0], [1.0, 0.0], [1.0, 1.0]];
            var targets = [0.0, 1.0, 1.0, 0.0];
            net.fit(inputs, targets, 800, 0.8);
            var correct = 0;
            for (var s = 0; s < inputs.length; s = s + 1) {
                var y = net.forward(inputs[s])[0];
                var predicted = 0;
                if (y >= 0.5) {
                    predicted = 1;
                }
                if (predicted == int(targets[s])) {
                    correct = correct + 1;
                }
            }
            print(correct);
            """).Trim();

        Assert.Equal("4", output);
    }

    [Fact]
    public void SequentialFit_CrossEntropySeparatesTwoPoints()
    {
        var output = RunProgram("""
            math.seed(1);
            var net = nn.sequential([[2, 2, "linear", 0.1]]);
            var inputs = [[0.0, 0.0], [0.0, 1.0], [1.0, 0.0], [1.0, 1.0]];
            var targets = [0, 0, 1, 1];
            net.fit(inputs, targets, 80, 0.8, "crossEntropy");
            var correct = 0;
            for (var s = 0; s < inputs.length; s = s + 1) {
                var logits = net.forward(inputs[s]);
                var predicted = 0;
                if (logits[1] > logits[0]) {
                    predicted = 1;
                }
                if (predicted == targets[s]) {
                    correct = correct + 1;
                }
            }
            print(correct);
            """).Trim();

        Assert.Equal("4", output);
    }

    [Fact]
    public void SequentialXor_MatchesInTranspiledMode()
    {
        const string source = """
            math.seed(42);
            var net = nn.sequential([
                [2, 4, "sigmoid", 1.0],
                [4, 1, "sigmoid", 1.0]
            ]);
            var inputs = [[0.0, 0.0], [0.0, 1.0], [1.0, 0.0], [1.0, 1.0]];
            var targets = [0.0, 1.0, 1.0, 0.0];
            net.fit(inputs, targets, 40, 0.8);
            var y = net.forward([1.0, 0.0]);
            print(int(y[0] * 1000));
            """;
        var interpreted = RunProgram(source);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
    }

    [Fact]
    public void DenseForward_MatchesJavaScript()
    {
        InterpretJsPair.AssertSameFromSource("""
            var layer = new Dense(1, 1, "linear", 0.0);
            layer.weights[0][0] = 2.0;
            layer.bias[0] = 1.0;
            print(int(layer.forward([3.0])[0]));
            """, "dense forward");
    }
}
