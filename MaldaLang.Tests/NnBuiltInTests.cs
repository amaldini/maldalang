// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class NnBuiltInTests : TestBase
{
    private const string DenseSource = """
        print(int(nn.relu(-2.0) + nn.relu(1.0)));
        print(int(nn.leakyRelu(-2.0) * 100));
        print(int(nn.elu(-1.0) * 1000));
        print(int(nn.dGelu(0.0) * 10));
        print(int(nn.dSilu(0.0) * 10));
        print(int(nn.softplus(0.0) * 1000));
        print(int(nn.dSigmoid(0.0) * 100));
        print(int(nn.mseGrad(2.0, 0.5) * 10));
        print(int(nn.softmaxGrad([0.0, 0.0], 0)[0] * 10));
        var layer = nn.dense([1.0, 2.0], [[0.5, -1.0], [1.0, 0.0]], [0.25, -0.5], "relu");
        print(int(layer.pre[0] * 100));
        print(int(layer.out[0] * 100));
        print(int(layer.out[1] * 10));
        var back = nn.denseBackward([1.0, 2.0], [[0.5, -1.0], [1.0, 0.0]], [1.0, 1.0], "relu", layer.pre);
        print(int(back.dInput[0] * 10));
        print(int(back.dInput[1] * 10));
        print(int(back.dWeights[0][0] * 10));
        print(int(back.dWeights[1][0] * 10));
        print(int(back.dBias[1] * 10));
        var batch = nn.dense([[1.0, 0.0], [0.0, 1.0]], [[1.0, 2.0], [3.0, 4.0]], [1.0, 0.0]);
        print(int(batch.out[1][0]));
        print(int(batch.out[1][1]));
        var g = nn.mseGrad([[1.0, 2.0], [3.0, 4.0]], [[1.0, 0.0], [0.0, 4.0]]);
        print(int(g[0][1]));
        print(int(g[1][0]));
        """;

    [Fact]
    public void ActivationsDenseAndGradients_MatchHandValues()
    {
        var output = RunProgram(DenseSource).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            new[]
            {
                "1", "-2", "-632", "5", "5", "693", "25", "15", "-5",
                "275", "275", "0",
                "5", "10", "10", "20", "0",
                "4", "4",
                "2", "3"
            },
            output.Select(line => line.Trim()).ToArray());
    }

    [Fact]
    public void Dense_MatchesInTranspiledMode()
    {
        var interpreted = RunProgram(DenseSource);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(DenseSource);
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
    }

    [Fact]
    public void Dense_RejectsUnknownActivationAndMissingPre()
    {
        var unknown = Assert.Throws<RuntimeException>(() =>
            NnStdLib.Call("dense", new List<RuntimeValue>
            {
                RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Float(1.0) }),
                RuntimeValue.Array(new List<RuntimeValue>
                {
                    RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Float(1.0) })
                }),
                RuntimeValue.String("swish")
            }));
        Assert.Contains("unknown activation", unknown.Message, StringComparison.Ordinal);

        var missingPre = Assert.Throws<RuntimeException>(() =>
            NnStdLib.Call("denseBackward", new List<RuntimeValue>
            {
                RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Float(1.0) }),
                RuntimeValue.Array(new List<RuntimeValue>
                {
                    RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Float(1.0) })
                }),
                RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Float(1.0) }),
                RuntimeValue.String("relu")
            }));
        Assert.Contains("pre is required", missingPre.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsRuntime_MatchesDenseNumbers()
    {
        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = Path.Combine(Path.GetTempPath(), "malda_nn_js_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scriptPath = Path.Combine(root, "nn-check.js");
        File.WriteAllText(scriptPath, """
            const ml = require(process.argv[2]);
            const layer = ml.nn.dense([1, 2], [[0.5, -1], [1, 0]], [0.25, -0.5], "relu");
            if (layer.out[0] !== 2.75 || layer.out[1] !== 0) {
              throw new Error("forward " + layer.out.join(","));
            }
            const back = ml.nn.denseBackward([1, 2], [[0.5, -1], [1, 0]], [1, 1], "relu", layer.pre);
            if (back.dInput[0] !== 0.5 || back.dInput[1] !== 1 || back.dWeights[1][0] !== 2 || back.dBias[1] !== 0) {
              throw new Error("backward");
            }
            if (Math.abs(ml.nn.dGelu(0) - 0.5) > 1e-12) throw new Error("dgelu");
            if (Math.abs(ml.nn.softplus(0) - Math.LN2) > 1e-12) throw new Error("softplus");
            const grad = ml.nn.softmaxGrad([0, 0], 0);
            if (Math.abs(grad[0] + 0.5) > 1e-12 || Math.abs(grad[1] - 0.5) > 1e-12) throw new Error("softmaxGrad");
            const batch = ml.nn.dense([[1, 0], [0, 1]], [[1, 2], [3, 4]], [1, 0]);
            if (batch.out[1][0] !== 4 || batch.out[1][1] !== 4) throw new Error("batch");
            process.stdout.write("ok\n");
            """);

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);
            using var process = System.Diagnostics.Process.Start(startInfo);
            Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stderr + stdout);
            Assert.Contains("ok", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void JsTranspiler_EmitsNnModuleCalls()
    {
        var compiler = new MaldaLang.Compiler.Compiler();
        var js = compiler.TranspileToJavaScriptFromSource("""
            var layer = nn.dense([1.0], [[2.0]], [0.0], "relu");
            print(layer.out[0]);
            """);
            Assert.Contains("mlRuntime.nn.dense(", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsRuntime_RandnArgmaxAndSoftmaxTrainingStep()
    {
        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = Path.Combine(Path.GetTempPath(), "malda_math_js_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scriptPath = Path.Combine(root, "math-check.js");
        File.WriteAllText(scriptPath, """
            const ml = require(process.argv[2]);
            if (typeof ml.math.randn !== "function") throw new Error("randn missing");
            if (typeof ml.math.argmax !== "function") throw new Error("argmax missing");
            ml.math.seed(3);
            const first = ml.math.randn(0.25);
            const second = ml.math.randn(0.25, 1);
            if (!Number.isFinite(first) || !Number.isFinite(second)) throw new Error("randn not finite");
            ml.math.seed(3);
            if (ml.math.randn(0.25) !== first) throw new Error("randn ignores seed");
            if (ml.math.argmax([0.1, 0.7, 0.2]) !== 1) throw new Error("argmax");
            if (ml.math.argmax([2, 2, 1]) !== 0) throw new Error("argmax tie");
            if (ml.math.argmin([0.1, 0.7, 0.2]) !== 0) throw new Error("argmin");
            if (Math.abs(ml.math.rsqrt(4) - 0.5) > 1e-12) throw new Error("rsqrt");
            if (Math.abs(ml.math.logSumExp([0, 0]) - Math.log(2)) > 1e-12) throw new Error("logSumExp");
            const probs = ml.math.softmax([1, 2, 3]);
            const sum = probs[0] + probs[1] + probs[2];
            if (Math.abs(sum - 1) > 1e-9) throw new Error("softmax");
            if (ml.math.randomChoiceWeighted([0, 1, 0]) !== 1) throw new Error("randomChoiceWeighted");

            ml.math.seed(3);
            const points = [];
            const labels = [];
            const centers = [[-1.5, -1.2], [1.6, -0.8], [0.1, 1.7]];
            for (let c = 0; c < 3; c++) {
              for (let n = 0; n < 12; n++) {
                points.push([centers[c][0] + ml.math.randn(0.25), centers[c][1] + ml.math.randn(0.25)]);
                labels.push(c);
              }
            }
            const w = [
              [ml.math.randn(0.3), ml.math.randn(0.3), ml.math.randn(0.3)],
              [ml.math.randn(0.3), ml.math.randn(0.3), ml.math.randn(0.3)],
              [ml.math.randn(0.3), ml.math.randn(0.3), ml.math.randn(0.3)]
            ];
            const x = [points[0][0], points[0][1], 1];
            const logits = ml.math.matmul(x, w);
            const cls = ml.math.argmax(ml.nn.softmax(logits));
            if (cls !== labels[0] && (cls < 0 || cls > 2)) throw new Error("class " + cls);
            let threw = false;
            try { ml.math.randn(1, 0, 0); } catch (e) { threw = true; }
            if (!threw) throw new Error("randn arity");
            process.stdout.write("ok\n");
            """);

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);
            using var process = System.Diagnostics.Process.Start(startInfo);
            Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stderr + stdout);
            Assert.Contains("ok", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
