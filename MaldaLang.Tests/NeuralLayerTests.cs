// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

public class NeuralLayerTests : TestBase
{
    private const string Probe = """
        var conv = new Conv(2, 0.0);
        conv.kernel[0][0] = 1.0;
        conv.kernel[0][1] = 0.0;
        conv.kernel[1][0] = 0.0;
        conv.kernel[1][1] = 1.0;
        var feat = conv.forward([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0], [7.0, 8.0, 9.0]]);
        var dImage = conv.backward([[1.0, 1.0], [1.0, 1.0]]);
        conv.sgd(1.0);
        print(int(feat[0][0]));
        print(int(feat[1][1]));
        print(int(dImage[0][0]));
        print(int(conv.kernel[0][0]));
        print(int(conv.kernel[1][1]));

        var emb = new Embedding(2, 1, 0.0);
        emb.table[0][0] = 3.0;
        emb.table[1][0] = 9.0;
        print(int(emb.forward(0)[0]));
        emb.backward([1.0]);
        emb.sgd(0.5);
        print(int(emb.table[0][0] * 10));
        print(int(emb.table[1][0]));

        var rnn = new Rnn(1, 1, "linear", 0.0);
        rnn.weightsXh[0][0] = 1.0;
        rnn.weightsHh[0][0] = 0.0;
        rnn.bias[0] = 0.0;
        var hs = rnn.forward([[2.0], [3.0]]);
        print(int(hs[0][0]));
        print(int(hs[1][0]));
        rnn.backward([[1.0], [0.0]]);
        rnn.sgd(0.5);
        print(int(rnn.weightsXh[0][0]));

        var ln = new LayerNorm(2);
        ln.beta[0] = 5.0;
        ln.beta[1] = 5.0;
        var y = ln.forward([1.0, 3.0]);
        print(int((y[0] + y[1]) * 10));
        ln.backward([1.0, 0.0]);
        ln.sgd(1.0);
        print(int(ln.gamma[0] * 1000));
        print(int(ln.beta[0]));

        var head = new Attention(2, 1, 1.0);
        head.query[0][0] = 0.0;
        head.query[1][0] = 0.0;
        head.key[0][0] = 0.0;
        head.key[1][0] = 0.0;
        var plain = head.forward([[2.0], [6.0]]);
        var causal = head.forward([[2.0], [6.0]], [[1.0, 0.0], [0.0, 1.0]]);
        print(int(plain[0][0]));
        print(int(plain[1][0]));
        print(int(causal[0][0]));
        print(int(causal[1][0]));
        print(int(head.probs[0][1] * 1000));
        """;

    [Fact]
    public void Layers_MatchTheLocalGradients()
    {
        var output = RunProgram(Probe).Replace("\r", "").Trim().Split('\n');
        Assert.Equal(new[]
        {
            "6", "14", "1", "-11", "-27",
            "3", "25", "9",
            "2", "3", "0",
            "100", "1999", "4",
            "4", "4", "2", "6", "0"
        }, output);
    }

    [Fact]
    public void Layers_MatchInTranspiledMode()
    {
        var interpreted = RunProgram(Probe).Replace("\r", "");
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(Probe);
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted, transpiled.StdOut.Replace("\r", ""));
    }

    [Fact]
    public void Layers_MatchJavaScript()
    {
        InterpretJsPair.AssertSameFromSource(Probe, "neural layers");
    }
}
