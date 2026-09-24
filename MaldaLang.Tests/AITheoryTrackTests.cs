// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class AITheoryTrackTests : TestBase
{
    [Fact]
    public void Catalog_ListsOfflineStudentExamples()
    {
        var examples = ExampleProgramsService.GetExamples()
            .Where(example => example.Category == "AI_Theory")
            .ToList();

        Assert.Equal(23, examples.Count);
        Assert.All(examples, example =>
        {
            Assert.Equal("student", example.Track);
            Assert.True(ExampleProgramsService.IsOfflineFriendly(example));
        });

        Assert.Equal(9, ExampleProgramsService.GetCategoryOrder("AI_Theory"));
    }

    [Fact]
    public void Catalog_NextChain_StartsAtSarsaCliff()
    {
        var sarsa = ExampleProgramsService.GetExampleByRelativePath("AI_Theory/sarsa_cliff.malda");
        Assert.NotNull(sarsa);
        Assert.Equal("AI_Theory/chain_rule.malda", sarsa!.Next);
        Assert.Contains("Algorithms/qlearn_grid.malda", sarsa.Prerequisites);

        var chain = ExampleProgramsService.GetExampleByRelativePath("AI_Theory/chain_rule.malda");
        Assert.NotNull(chain);
        Assert.Equal("AI_Theory/perceptron.malda", chain!.Next);
        Assert.Contains("AI_Theory/sarsa_cliff.malda", chain.Prerequisites);

        var perceptron = ExampleProgramsService.GetExampleByRelativePath("AI_Theory/perceptron.malda");
        Assert.NotNull(perceptron);
        Assert.Equal("AI_Theory/xor_neural_net.malda", perceptron!.Next);

        var xor = ExampleProgramsService.GetExampleByRelativePath("AI_Theory/xor_neural_net.malda");
        Assert.NotNull(xor);
        Assert.Contains("AI_Theory/perceptron.malda", xor!.Prerequisites);
        Assert.Equal("AI_Theory/softmax_classifier.malda", xor.Next);
    }

    [Fact]
    public async Task SarsaCliff_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "sarsa_cliff.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("q-learning hugs the cliff", output);
        Assert.Contains("sarsa stays inland", output);
        Assert.Contains("sarsa cliff ok", output);
    }

    [Fact]
    public async Task ChainRule_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "chain_rule.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("Numeric:", output);
        Assert.Contains("Analytic:", output);
        Assert.Contains("chain rule ok", output);
    }

    [Fact]
    public async Task Perceptron_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "perceptron.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("perceptron ok", output);
    }

    [Fact]
    public async Task SoftmaxClassifier_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "softmax_classifier.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("softmax classifier ok", output);
    }

    [Fact]
    public async Task Embedding2d_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "embedding_2d.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("embedding 2d ok", output);
    }

    [Fact]
    public async Task OnnxInspect_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "onnx_inspect.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("onnx inspect ok", output);
    }

    [Fact]
    public async Task NnDense_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "nn_dense.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("nn dense ok", output);
    }

    [Fact]
    public async Task MnistDigits_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "mnist_digits.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("Accuracy: 10/10", output);
        Assert.Contains("mnist digits ok", output);
    }

    [Fact]
    public async Task GradcheckDense_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "gradcheck_dense.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("gradcheck dense ok", output);
    }

    [Fact]
    public async Task InitScale_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "init_scale.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("init scale ok", output);
    }

    [Fact]
    public async Task GlyphHoldout_TrainBeatsHoldout()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "glyph_holdout.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("Train accuracy: 10/10", output);
        Assert.Contains("Holdout accuracy:", output);
        Assert.DoesNotContain("Holdout accuracy: 10/10", output);
        Assert.Contains("glyph holdout ok", output);
    }

    [Fact]
    public async Task MomentumValley_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "momentum_valley.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("momentum valley ok", output);
    }

    [Fact]
    public async Task RnnDelay_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "rnn_delay.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("rnn delay ok", output);
    }

    [Fact]
    public async Task ConvStroke_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "conv_stroke.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("conv stroke ok", output);
    }

    [Fact]
    public async Task ResidualDropout_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "residual_dropout.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("residual dropout ok", output);
    }

    [Fact]
    public async Task EmbedRow_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "embed_row.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("embed row ok", output);
    }

    [Fact]
    public async Task NextChar_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "next_char.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("next char ok", output);
    }

    [Fact]
    public async Task LayerNorm_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "layer_norm.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("layer norm ok", output);
    }

    [Fact]
    public async Task AttentionStep_PrintsOkLine()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "AI_Theory", "attention_step.malda");
        var output = await CaptureInterpretAsync(File.ReadAllText(path), path);
        Assert.Contains("attention step ok", output);
    }
}
