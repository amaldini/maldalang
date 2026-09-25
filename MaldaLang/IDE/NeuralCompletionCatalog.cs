// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.IDE.Models;

/// <summary>
/// Completion, signature, and hover surface for <c>nn.*</c> and the neural host classes.
/// Names stay aligned with <see cref="StdLibNamespaces.NnMethodNames"/> and
/// <see cref="TypeHintNameIndex.HostClassNames"/>.
/// </summary>
internal static class NeuralCompletionCatalog
{
    public const string ModuleHover =
        "nn — activations, local derivatives, dense forward/backward, and sequential. No flat aliases: write nn.relu, not relu.";

    public static readonly (string Name, string Detail, string InsertText)[] HostClasses =
    {
        ("Dense", "new Dense(inFeatures, outFeatures, activation?, scale?) — one dense layer", "new Dense()"),
        ("Sequential", "new Sequential(layers) — stack of Dense; fit is online SGD", "new Sequential()"),
        ("Conv", "new Conv(size, scale?) — one square kernel, valid convolution", "new Conv()"),
        ("Embedding", "new Embedding(rows, dim, scale?) — table lookup", "new Embedding()"),
        ("Rnn", "new Rnn(inputSize, hiddenSize, activation?, scale?) — one recurrent step", "new Rnn()"),
        ("LayerNorm", "new LayerNorm(features) — learned scale and shift", "new LayerNorm()"),
        ("Attention", "new Attention(length, dim, scale?) — one attention head", "new Attention()"),
        ("OnnxModel", "new OnnxModel(path) — host-only ONNX inspect and forward", "new OnnxModel()")
    };

    private static readonly Dictionary<string, string[]> NnParameters = new(StringComparer.Ordinal)
    {
        ["relu"] = ["x"],
        ["sigmoid"] = ["x"],
        ["tanh"] = ["x"],
        ["mse"] = ["pred", "target"],
        ["softmax"] = ["logits", "temperature?"],
        ["crossEntropyFromLogits"] = ["logits", "targetIndex"],
        ["leakyRelu"] = ["x", "alpha?"],
        ["elu"] = ["x", "alpha?"],
        ["gelu"] = ["x"],
        ["silu"] = ["x"],
        ["softplus"] = ["x"],
        ["dRelu"] = ["x"],
        ["dLeakyRelu"] = ["x", "alpha?"],
        ["dElu"] = ["x", "alpha?"],
        ["dGelu"] = ["x"],
        ["dSilu"] = ["x"],
        ["dSoftplus"] = ["x"],
        ["dSigmoid"] = ["x"],
        ["dTanh"] = ["x"],
        ["dense"] = ["x", "weights", "bias?", "activation?"],
        ["denseBackward"] = ["x", "weights", "upstream", "activation?", "pre?"],
        ["mseGrad"] = ["pred", "target"],
        ["softmaxGrad"] = ["logits", "target"],
        ["sequential"] = ["layers"]
    };

    private static readonly Dictionary<string, string> NnNotes = new(StringComparer.Ordinal)
    {
        ["dense"] = "returns { pre, out }; weights are [in, out]",
        ["denseBackward"] = "returns { dInput, dWeights, dBias }; pass forward pre unless activation is linear",
        ["mse"] = "mean squared error",
        ["mseGrad"] = "elementwise pred - target",
        ["softmaxGrad"] = "softmax(logits) minus a class index or a same-length vector",
        ["crossEntropyFromLogits"] = "classification loss from logits and a class index",
        ["sequential"] = "rows are [in, out, activation?, scale?]; returns Sequential"
    };

    private static readonly Dictionary<string, Member[]> Members = new(StringComparer.Ordinal)
    {
        ["Dense"] = Layer(
            Prop("inFeatures", "int"),
            Prop("outFeatures", "int"),
            Prop("activation", "string"),
            Prop("weights", "array [in, out]"),
            Prop("bias", "array"),
            Method("forward", "x"),
            Method("backward", "upstream"),
            Method("sgd", "lr")),
        ["Sequential"] = Layer(
            Prop("layers", "array of Dense"),
            Method("forward", "x"),
            Method("backward", "upstream"),
            Method("sgd", "lr"),
            Method("fit", "inputs", "targets", "epochs", "lr", "loss?")),
        ["Conv"] = Layer(
            Prop("size", "int"),
            Prop("kernel", "array"),
            Method("forward", "image"),
            Method("backward", "upstream"),
            Method("sgd", "lr")),
        ["Embedding"] = Layer(
            Prop("rows", "int"),
            Prop("dim", "int"),
            Prop("table", "array"),
            Method("forward", "index"),
            Method("backward", "upstream"),
            Method("sgd", "lr")),
        ["Rnn"] = Layer(
            Prop("inputSize", "int"),
            Prop("hiddenSize", "int"),
            Prop("activation", "string"),
            Prop("weightsXh", "array [input, hidden]"),
            Prop("weightsHh", "array [hidden, hidden]"),
            Prop("bias", "array"),
            Method("forward", "sequence"),
            Method("backward", "upstreams"),
            Method("sgd", "lr")),
        ["LayerNorm"] = Layer(
            Prop("features", "int"),
            Prop("gamma", "array"),
            Prop("beta", "array"),
            Method("forward", "x"),
            Method("backward", "upstream"),
            Method("sgd", "lr")),
        ["Attention"] = Layer(
            Prop("length", "int"),
            Prop("dim", "int"),
            Prop("scale", "float"),
            Prop("query", "array"),
            Prop("key", "array"),
            Prop("probs", "array — after forward"),
            Method("forward", "value", "mask?"),
            Method("backward", "upstream"),
            Method("sgd", "lr")),
        ["OnnxModel"] = Layer(
            Prop("modelPath", "string"),
            Method("inputs"),
            Method("outputs"),
            Method("run", "feeds"))
    };

    private static readonly Dictionary<string, string[]> Constructors = new(StringComparer.Ordinal)
    {
        ["Dense"] = ["inFeatures", "outFeatures", "activation?", "scale?"],
        ["Sequential"] = ["layers"],
        ["Conv"] = ["size", "scale?"],
        ["Embedding"] = ["rows", "dim", "scale?"],
        ["Rnn"] = ["inputSize", "hiddenSize", "activation?", "scale?"],
        ["LayerNorm"] = ["features"],
        ["Attention"] = ["length", "dim", "scale?"],
        ["OnnxModel"] = ["path"]
    };

    public static string NnMethodDetail(string method)
    {
        if (!NnParameters.TryGetValue(method, out var parameters))
            return $"nn.{method}()";

        var signature = $"nn.{method}({string.Join(", ", parameters)})";
        return NnNotes.TryGetValue(method, out var note) ? $"{signature} — {note}" : signature;
    }

    public static bool TryAddMembers(string typeName, List<CompletionItem> members)
    {
        if (!Members.TryGetValue(typeName, out var found))
            return false;

        members.AddRange(found.Select(member => member.ToCompletion()));
        return true;
    }

    public static List<string>? TryGetMemberParameters(string? receiverType, string methodName)
    {
        if (string.IsNullOrEmpty(receiverType))
            return null;

        if (receiverType == StdLibNamespaces.NnModule &&
            NnParameters.TryGetValue(methodName, out var nnParameters))
        {
            return nnParameters.ToList();
        }

        if (!Members.TryGetValue(receiverType, out var members))
            return null;

        var member = members.FirstOrDefault(item => item.Label == methodName && item.Parameters != null);
        return member?.Parameters?.ToList();
    }

    public static List<string>? TryGetConstructorParameters(string className)
    {
        return Constructors.TryGetValue(className, out var parameters)
            ? parameters.ToList()
            : null;
    }

    public static string? TryGetHover(string? receiverType, string memberName)
    {
        if (string.IsNullOrEmpty(receiverType))
            return null;

        if (receiverType == StdLibNamespaces.NnModule)
        {
            var detail = NnParameters.ContainsKey(memberName) ? NnMethodDetail(memberName) : null;
            return detail;
        }

        if (!Members.TryGetValue(receiverType, out var members))
            return null;

        var member = members.FirstOrDefault(item => item.Label == memberName);
        return member == null ? null : $"{receiverType}.{member.Detail}";
    }

    public static string? TryGetClassHover(string className)
    {
        foreach (var host in HostClasses)
        {
            if (host.Name == className)
                return host.Detail;
        }

        return null;
    }

    private static Member[] Layer(params Member[] members) => members;

    private static Member Prop(string name, string detail) =>
        new(name, "property", detail, name, null);

    private static Member Method(string name, params string[] parameters) =>
        new(name, "method", $"{name}({string.Join(", ", parameters)})", name + "()", parameters);

    private sealed record Member(string Label, string Kind, string Detail, string InsertText, string[]? Parameters)
    {
        public CompletionItem ToCompletion() => new()
        {
            Label = Label,
            Kind = Kind,
            Detail = Detail,
            InsertText = InsertText
        };
    }
}
