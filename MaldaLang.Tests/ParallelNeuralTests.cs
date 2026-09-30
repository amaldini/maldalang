// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ParallelNeuralTests : TestBase
{
    private const string AddAndConcat = """
        var left = new Dense(1, 1, "linear", 0.0);
        left.weights[0][0] = 2.0;
        left.bias[0] = 0.0;
        var right = new Dense(1, 1, "linear", 0.0);
        right.weights[0][0] = 3.0;
        right.bias[0] = 0.0;
        var added = new Parallel([left, right], "add");
        print(int(added.forward([4.0])[0]));
        added.backward([1.0]);
        added.sgd(1.0);
        print(int(left.weights[0][0]));
        print(int(right.weights[0][0]));
        var skip = new Dense(1, 1, "linear", 0.0);
        skip.weights[0][0] = 0.5;
        skip.bias[0] = 0.0;
        var residual = new Parallel([skip, new Identity()], "add");
        print(int(residual.forward([4.0])[0]));
        var catLeft = new Dense(1, 1, "linear", 0.0);
        catLeft.weights[0][0] = 2.0;
        catLeft.bias[0] = 0.0;
        var catRight = new Dense(1, 1, "linear", 0.0);
        catRight.weights[0][0] = 3.0;
        catRight.bias[0] = 0.0;
        var wide = new Parallel([catLeft, catRight], "concat");
        var joined = wide.forward([4.0]);
        print(int(joined[0]));
        print(int(joined[1]));
        wide.backward([1.0, 10.0]);
        wide.sgd(1.0);
        print(int(catLeft.weights[0][0]));
        print(int(catRight.weights[0][0]));
        """;

    [Fact]
    public void ParallelAddAndConcat_UpdateBothBranches()
    {
        var output = RunProgram(AddAndConcat).Replace("\r", "").Trim();
        Assert.Equal("20\n-2\n-1\n6\n8\n12\n-2\n-37", output);
    }

    [Fact]
    public void ParallelConcat_StacksChannels()
    {
        var output = RunProgram("""
            var picture = [[[1.0, 2.0], [3.0, 4.0]]];
            var stacked = new Parallel([new Identity(), new Identity()], "concat");
            var channels = stacked.forward(picture);
            var upstream = [
                [[1.0, 0.0], [0.0, 0.0]],
                [[0.0, 0.0], [0.0, 10.0]]
            ];
            var dInput = stacked.backward(upstream);
            var batch = [[
                [[1.0, 2.0], [3.0, 4.0]]
            ]];
            var wide = new Parallel([new Identity(), new Identity()], "concat");
            var nchw = wide.forward(batch);
            print(channels.length);
            print(int(channels[1][1][1]));
            print(int(dInput[0][0][0]));
            print(int(dInput[0][1][1]));
            print(nchw.length);
            print(nchw[0].length);
            print(int(nchw[0][1][0][1]));
            """).Replace("\r", "").Trim();

        Assert.Equal("2\n4\n1\n10\n1\n2\n2", output);
    }

    [Fact]
    public void Parallel_MatchesInTranspiledMode()
    {
        var interpreted = RunProgram(AddAndConcat);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(AddAndConcat);
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
    }

    [Fact]
    public void Parallel_MatchesJavaScript()
    {
        InterpretJsPair.AssertSameFromSource(AddAndConcat, "parallel add and concat");
    }
}
