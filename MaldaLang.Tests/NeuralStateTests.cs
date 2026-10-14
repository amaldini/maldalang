// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

using MaldaLang.Tests.Planning;

namespace MaldaLang.Tests;

/// <summary>N15: <c>state()</c> / <c>load(state)</c> on every neural layer.</summary>
[Collection("Sequential")]
public class NeuralStateTests : TestBase
{
    // Each pair is built twice from different seeds; load must make the second answer like the first.
    private const string RoundTrip = """
        function flat(x) {
            if (typeOf(x) != "array") {
                return [x];
            }
            var out = [];
            for (var i = 0; i < x.length; i = i + 1) {
                var inner = flat(x[i]);
                for (var j = 0; j < inner.length; j = j + 1) {
                    out.append(inner[j]);
                }
            }
            return out;
        }

        function close(a, b) -> bool {
            var left = flat(a);
            var right = flat(b);
            if (left.length != right.length) {
                return false;
            }
            for (var i = 0; i < left.length; i = i + 1) {
                if (math.abs(left[i] - right[i]) > 0.000000001) {
                    return false;
                }
            }
            return true;
        }

        function cnn() {
            return new Sequential([
                new Conv2D(1, 2, 3, 1, "same"),
                new BatchNorm2D(2),
                new Activation("relu"),
                new Dropout2D(0.0),
                new Parallel([new Identity(), new Conv2D(2, 2, 1)], "add"),
                new MaxPool2D(2),
                new AvgPool2D(1),
                new Flatten(),
                new Dense(8, 3, "tanh")
            ]);
        }

        var picture = [[[0.1, 0.9, 0.2, 0.4], [0.5, 0.3, 0.8, 0.6], [0.7, 0.2, 0.1, 0.9], [0.3, 0.6, 0.4, 0.2]]];

        math.seed(1);
        var a = cnn();
        a.forward(picture);
        a.forward(picture);
        math.seed(2);
        var b = cnn();
        b.load(parseJSON(toJSON(a.state())));
        a.layers[1].eval();
        b.layers[1].eval();
        print("cnn " + string(close(a.forward(picture), b.forward(picture))));

        var pools = new Sequential([new GlobalAvgPool2D()]);
        var pooled = new Sequential([new GlobalMaxPool2D()]);
        pools.load(pools.state());
        pooled.load(pooled.state());
        print("pools " + pools.state()["layers"][0]["type"] + " " + pooled.state()["layers"][0]["type"]);

        math.seed(3);
        var conv = new Conv(2);
        var embed = new Embedding(4, 3);
        var rnn = new Rnn(2, 3);
        var norm = new LayerNorm(3);
        var head = new Attention(3, 2);
        math.seed(4);
        var conv2 = new Conv(2);
        var embed2 = new Embedding(4, 3);
        var rnn2 = new Rnn(2, 3);
        var norm2 = new LayerNorm(3);
        var head2 = new Attention(3, 2);
        norm.gamma[1] = 2.5;
        conv2.load(conv.state());
        embed2.load(embed.state());
        rnn2.load(rnn.state());
        norm2.load(norm.state());
        head2.load(head.state());
        var image = [[0.1, 0.2, 0.3], [0.4, 0.5, 0.6], [0.7, 0.8, 0.9]];
        var seq = [[0.5, -0.5], [0.25, 0.75]];
        var value = [[1.0, 0.0], [0.0, 1.0], [0.5, 0.5]];
        print("conv " + string(close(conv.forward(image), conv2.forward(image))));
        print("embedding " + string(close(embed.forward(2), embed2.forward(2))));
        print("rnn " + string(close(rnn.forward(seq), rnn2.forward(seq))));
        print("layerNorm " + string(close(norm.forward([1.0, 2.0, 4.0]), norm2.forward([1.0, 2.0, 4.0]))));
        print("attention " + string(close(head.forward(value), head2.forward(value))));

        var dense = new Dense(2, 1, "linear", 0.0);
        dense.weights[0][0] = 3.0;
        var saved = dense.state();
        dense.weights[0][0] = 5.0;
        print("copy " + string(int(saved["weights"][0][0])));
        dense.load(saved);
        print("restored " + string(int(dense.weights[0][0])));

        var wrong = new Sequential([new Dense(2, 1, "linear", 0.0), new Dense(1, 1, "linear", 0.0)]);
        var half = { type: "Sequential", layers: [saved, { type: "Conv", kernel: [[1.0]] }] };
        try {
            wrong.load(half);
        } catch (e) {
            print(string(e));
        }
        print("untouched " + string(int(wrong.layers[0].weights[0][0])));
        try {
            dense.load({ type: "Dense", weights: [[1.0], [2.0]] });
        } catch (e) {
            print(string(e));
        }
        try {
            dense.load({ type: "Dense", weights: [[1.0], ["x"]], bias: [0.0] });
        } catch (e) {
            print(string(e));
        }
        """;

    [Fact]
    public void StateAndLoad_RoundTripEveryLayer()
    {
        var lines = RunProgram(RoundTrip).Replace("\r", "").Trim().Split('\n');

        Assert.Equal(new[]
        {
            "cnn true",
            "pools GlobalAvgPool2D GlobalMaxPool2D",
            "conv true",
            "embedding true",
            "rnn true",
            "layerNorm true",
            "attention true",
            "copy 3",
            "restored 3",
            "Sequential.load() state.layers[1] is a Conv state, expected Dense",
            "untouched 0",
            "Dense.load() state.bias is missing",
            "Dense.load() state.weights[1][0] must be a number"
        }, lines.Select(line => line.Trim()).ToArray());
    }

    [Fact]
    public void StateAndLoad_MatchesInTranspiledMode()
    {
        var interpreted = RunProgram(RoundTrip);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(RoundTrip);
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
    }

    [Fact]
    public void StateAndLoad_MatchesJavaScript()
    {
        InterpretJsPair.AssertSameFromSource(RoundTrip, "neural state round trip");
    }

    [Fact]
    public void SaveLoadExample_PrintsOk()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "AI_Theory", "save_load_state.malda");
        var output = RunProgram(File.ReadAllText(path));
        Assert.Contains("save load state ok", output);
    }
}
