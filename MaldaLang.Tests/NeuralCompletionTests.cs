// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class NeuralCompletionTests
{
    private readonly LanguageService _service = new();

    [Fact]
    public void GetCompletions_OffersNeuralHostClasses()
    {
        var completions = _service.GetCompletions("var layer = new ", 0, "var layer = new ".Length);

        Assert.Contains(completions, item => item.Label == "Dense" && item.InsertText == "new Dense()");
        Assert.Contains(completions, item => item.Label == "Sequential");
        Assert.Contains(completions, item => item.Label == "Conv");
        Assert.Contains(completions, item => item.Label == "Embedding");
        Assert.Contains(completions, item => item.Label == "Rnn");
        Assert.Contains(completions, item => item.Label == "LayerNorm");
        Assert.Contains(completions, item => item.Label == "Attention");
        Assert.Contains(completions, item => item.Label == "OnnxModel");
    }

    [Fact]
    public void GetCompletions_NnDot_OffersSignedMethods()
    {
        var source = "nn.";
        var completions = _service.GetCompletions(source, 0, source.Length);
        var labels = completions.Select(item => item.Label).ToHashSet(StringComparer.Ordinal);

        Assert.True(StdLibNamespaces.NnMethodNames.IsSubsetOf(labels));
        var dense = completions.Single(item => item.Label == "dense");
        Assert.Contains("weights", dense.Detail ?? "");
        Assert.Contains("bias?", dense.Detail ?? "");
        var sequential = completions.Single(item => item.Label == "sequential");
        Assert.Contains("layers", sequential.Detail ?? "");
    }

    [Fact]
    public void GetCompletions_DenseInstance_OffersForwardAndWeights()
    {
        var source = "var layer = new Dense(2, 1);\nlayer.";
        var completions = _service.GetCompletions(source, 1, "layer.".Length);

        Assert.Contains(completions, item => item.Label == "forward" && item.Detail.Contains("x"));
        Assert.Contains(completions, item => item.Label == "backward");
        Assert.Contains(completions, item => item.Label == "sgd");
        Assert.Contains(completions, item => item.Label == "weights");
        Assert.Contains(completions, item => item.Label == "bias");
    }

    [Fact]
    public void GetCompletions_SequentialFromNn_OffersFit()
    {
        var source = "var net = nn.sequential([[2, 1, \"relu\"]]);\nnet.";
        var completions = _service.GetCompletions(source, 1, "net.".Length);

        Assert.Contains(completions, item => item.Label == "fit" && item.Detail.Contains("epochs"));
        Assert.Contains(completions, item => item.Label == "forward");
    }

    [Fact]
    public void GetSignatureHelp_NnDense_ShowsWeights()
    {
        var source = "nn.dense(";
        var help = _service.GetSignatureHelp(source, 0, source.Length);

        Assert.NotNull(help);
        Assert.Equal(new[] { "x", "weights", "bias?", "activation?" }, help!.Parameters);
        Assert.Contains("dense", help.SignatureLabel);
    }

    [Fact]
    public void GetSignatureHelp_NewDense_ShowsConstructor()
    {
        var source = "new Dense(";
        var help = _service.GetSignatureHelp(source, 0, source.Length);

        Assert.NotNull(help);
        Assert.Equal(new[] { "inFeatures", "outFeatures", "activation?", "scale?" }, help!.Parameters);
    }

    [Fact]
    public void GetSignatureHelp_LayerForward_ShowsInput()
    {
        var source = "var layer = new Dense(2, 1);\nlayer.forward(";
        var help = _service.GetSignatureHelp(source, 1, "layer.forward(".Length);

        Assert.NotNull(help);
        Assert.Equal(new[] { "x" }, help!.Parameters);
    }

    [Fact]
    public void GetHover_NnRelu_ShowsSignature()
    {
        var source = "var y = nn.relu([1.0]);";
        var hover = _service.GetHoverInformation(source, 0, source.IndexOf("relu"));

        Assert.NotNull(hover);
        Assert.Contains("nn.relu(x)", hover);
    }

    [Fact]
    public void GetHover_DenseClass_ShowsConstructor()
    {
        var source = "var layer = new Dense(2, 1);";
        var hover = _service.GetHoverInformation(source, 0, source.IndexOf("Dense"));

        Assert.NotNull(hover);
        Assert.Contains("inFeatures", hover);
    }

    [Fact]
    public void GetHover_UserClassNamedDense_StaysTheUserClass()
    {
        var source = "class Dense {\n\tvar value;\n}";
        var hover = _service.GetHoverInformation(source, 0, source.IndexOf("Dense"));

        Assert.NotNull(hover);
        Assert.Contains("class Dense", hover);
        Assert.DoesNotContain("inFeatures", hover);
    }
}
