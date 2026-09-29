// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

public class CnnLayerTests : TestBase
{
    private const string Probe = """
        var conv = new Conv2D(1, 1, 2, 1, "valid", 0.0);
        conv.weights[0][0][0][0] = 1.0;
        conv.weights[0][0][0][1] = 0.0;
        conv.weights[0][0][1][0] = 0.0;
        conv.weights[0][0][1][1] = 1.0;
        conv.bias[0] = 0.0;
        var image = [[1.0, 2.0, 3.0], [4.0, 5.0, 6.0], [7.0, 8.0, 9.0]];
        var y = conv.forward([image]);
        print(y.length);
        print(y[0].length);
        print(int(y[0][0][0]));
        print(int(y[0][1][1]));
        var yb = conv.forward([[image]]);
        print(yb.length);
        print(yb[0].length);
        print(int(yb[0][0][0][0]));

        var same = new Conv2D(1, 1, 3, 1, "same", 0.0);
        same.weights[0][0][1][1] = 1.0;
        var img4 = [[
            [1.0, 2.0, 3.0, 4.0],
            [5.0, 6.0, 7.0, 8.0],
            [9.0, 10.0, 11.0, 12.0],
            [13.0, 14.0, 15.0, 16.0]
        ]];
        var os = same.forward(img4);
        print(os[0].length);
        print(int(os[0][0][0]));
        print(int(os[0][1][1]));

        var even = new Conv2D(1, 1, 2, 1, "same", 0.0);
        even.weights[0][0][0][0] = 1.0;
        var ones = [[
            [1.0, 1.0, 1.0, 1.0],
            [1.0, 1.0, 1.0, 1.0],
            [1.0, 1.0, 1.0, 1.0],
            [1.0, 1.0, 1.0, 1.0]
        ]];
        var oe = even.forward(ones);
        print(oe[0].length);
        print(oe[0][0].length);
        print(int(oe[0][0][0]));
        print(int(oe[0][3][3]));

        var mix = new Conv2D(2, 1, 1, 1, "valid", 0.0);
        mix.weights[0][0][0][0] = 1.0;
        mix.weights[1][0][0][0] = 2.0;
        mix.bias[0] = 3.0;
        var ym = mix.forward([[[4.0]], [[5.0]]]);
        print(int(ym[0][0][0]));

        var back = new Conv2D(1, 1, 2, 1, "valid", 0.0);
        back.weights[0][0][0][0] = 1.0;
        var yback = back.forward([[[1.0, 0.0], [0.0, 0.0]]]);
        print(int(yback[0][0][0]));
        var db = back.backward([[[2.0]]]);
        print(int(db[0][0][0]));
        print(int(db[0][0][1]));
        back.sgd(0.5);
        print(int(back.weights[0][0][0][0]));
        print(int(back.bias[0]));

        var pool = new MaxPool2D(2);
        var mp = pool.forward([[[1.0, 3.0], [2.0, 4.0]]]);
        print(int(mp[0][0][0]));
        var md = pool.backward([[[8.0]]]);
        print(int(md[0][1][1]));
        print(int(md[0][0][0]));

        var block = new Sequential([new MaxPool2D(2)]);
        var by = block.forward([[[1.0, 3.0], [2.0, 4.0]]]);
        print(int(by[0][0][0]));
        var bd = block.backward([[[8.0]]]);
        print(int(bd[0][1][1]));

        var avg = new AvgPool2D(2);
        var av = avg.forward([[[1.0, 3.0], [5.0, 7.0]]]);
        print(int(av[0][0][0]));
        var ad = avg.backward([[[8.0]]]);
        print(int(ad[0][0][0]));
        print(int(ad[0][1][1]));

        var flat = new Flatten();
        var fv = flat.forward([[[1.0, 2.0], [3.0, 4.0]]]);
        print(fv.length);
        print(int(fv[0]));
        print(int(fv[3]));
        var fb = flat.backward([10.0, 20.0, 30.0, 40.0]);
        print(int(fb[0][1][1]));

        var ga = new GlobalAvgPool2D();
        var gy = ga.forward([[[1.0, 3.0], [5.0, 7.0]]]);
        print(gy.length);
        print(int(gy[0]));
        var gd = ga.backward([8.0]);
        print(int(gd[0][0][0]));
        print(int(gd[0][1][1]));
        var gyb = ga.forward([
            [[[2.0, 2.0], [2.0, 2.0]]],
            [[[8.0, 8.0], [8.0, 8.0]]]
        ]);
        print(gyb.length);
        print(int(gyb[0][0]));
        print(int(gyb[1][0]));

        var gm = new GlobalMaxPool2D();
        var my = gm.forward([[[1.0, 9.0], [3.0, 4.0]]]);
        print(int(my[0]));
        var gmd = gm.backward([6.0]);
        print(int(gmd[0][0][1]));
        print(int(gmd[0][0][0]));

        var act = new Activation("relu");
        var ay = act.forward([-2.0, 3.0]);
        print(int(ay[0]));
        print(int(ay[1]));
        var ady = act.backward([1.0, 1.0]);
        print(int(ady[0]));
        print(int(ady[1]));
        var actNet = new Sequential([new Activation("relu")]);
        var az = actNet.forward([[[-1.0, 2.0]]]);
        print(int(az[0][0][0]));
        print(int(az[0][0][1]));

        var dense = new Dense(2, 1, "linear", 0.0);
        dense.weights[0][0] = 1.0;
        dense.weights[1][0] = 0.0;
        dense.bias[0] = 0.0;
        var seq = new Sequential([new Flatten(), dense]);
        var sy = seq.forward([[[2.0, 4.0]]]);
        print(int(sy[0]));
        seq.backward([1.0]);
        seq.sgd(0.5);
        print(int(dense.weights[0][0]));
        print(int(dense.weights[1][0]));
        print(int(dense.bias[0] * 2));

        var bn = new BatchNorm2D(1, 0.5, 0.00001);
        bn.beta[0] = 7.0;
        var bny = bn.forward([[[4.0, 4.0], [4.0, 4.0]]]);
        print(int(bny[0][0][0]));
        print(int(bny[0][1][1]));
        print(int(bn.runningMean[0]));
        print(int(bn.runningVar[0] * 2));
        bn.backward([[[1.0, 1.0], [1.0, 1.0]]]);
        bn.sgd(0.5);
        print(int(bn.beta[0]));

        var drop = new Dropout2D(0.0);
        var dy = drop.forward([[[1.0, 2.0], [3.0, 4.0]]]);
        print(int(dy[0][0][0]));
        print(int(dy[0][1][1]));
        var dd = drop.backward([[[9.0, 8.0], [7.0, 6.0]]]);
        print(int(dd[0][0][1]));
        var drop2 = new Dropout2D(0.5);
        drop2.eval();
        var dy2 = drop2.forward([[[1.0, 2.0], [3.0, 4.0]]]);
        print(int(dy2[0][0][1]));
        print(drop2.training);
        """;

    private static readonly string[] Expected =
    {
        "1", "2", "6", "14",
        "1", "1", "6",
        "4", "1", "6",
        "4", "4", "1", "1",
        "17",
        "1", "2", "0", "0", "-1",
        "4", "8", "0",
        "4", "8",
        "4", "2", "2",
        "4", "1", "4", "40",
        "1", "4", "2", "2",
        "2", "2", "8",
        "9", "6", "0",
        "0", "3", "0", "1",
        "0", "2",
        "2", "0", "-2", "-1",
        "7", "7", "2", "1", "5",
        "1", "4", "8",
        "2", "false"
    };

    [Fact]
    public void Layers_MatchTheLocalGradients()
    {
        var output = RunProgram(Probe).Replace("\r", "").Trim().Split('\n');
        Assert.Equal(Expected, output);
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
        InterpretJsPair.AssertSameFromSource(Probe, "cnn layers");
    }
}
