// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// <c>state()</c> / <c>load(state)</c> for every neural layer. A state is a plain object:
/// <c>type</c>, the named parameter tensors (deep copies), and <c>layers</c> / <c>branches</c>
/// for containers. <c>load</c> checks the whole state before it writes, then copies the numbers
/// into the live parameter arrays, so the architecture comes from the constructor, not the state.
/// </summary>
internal static class NeuralState
{
    internal static RuntimeValue State(ObjectInstance layer, List<RuntimeValue> args)
    {
        var type = TypeName(layer);
        if (args.Count != 0)
            throw new RuntimeException($"{type}.state() expects no arguments");
        return State(layer);
    }

    internal static RuntimeValue Load(ObjectInstance layer, List<RuntimeValue> args)
    {
        var type = TypeName(layer);
        if (args.Count != 1)
            throw new RuntimeException($"{type}.load() expects 1 argument: (state)");
        Apply(type, layer, args[0], "state", write: false);
        Apply(type, layer, args[0], "state", write: true);
        return RuntimeValue.Null();
    }

    private static RuntimeValue State(ObjectInstance layer)
    {
        var dict = new DictionaryInstance();
        dict.SetEntry("type", RuntimeValue.String(TypeName(layer)));
        foreach (var name in ParameterNames(layer))
            dict.SetEntry(name, Copy(layer.Get(name)));

        var children = Children(layer, out var key);
        if (children != null)
        {
            var list = new List<RuntimeValue>(children.Count);
            foreach (var child in children)
                list.Add(State(child));
            dict.SetEntry(key, RuntimeValue.Array(list));
        }

        return RuntimeValue.Object(dict);
    }

    private static void Apply(string owner, ObjectInstance layer, RuntimeValue state, string path, bool write)
    {
        var type = TypeName(layer);
        if (state.Type != ValueType.Object)
            throw new RuntimeException($"{owner}.load() {path} must be an object");
        var dict = state.AsObject();
        if (!TryField(dict, "type", out var tag) || tag.Type != ValueType.String)
            throw new RuntimeException($"{owner}.load() {path}.type must be a string");
        if (tag.AsString() != type)
            throw new RuntimeException($"{owner}.load() {path} is a {tag.AsString()} state, expected {type}");

        foreach (var name in ParameterNames(layer))
        {
            if (!TryField(dict, name, out var value))
                throw new RuntimeException($"{owner}.load() {path}.{name} is missing");
            Copy(owner, layer.Get(name), value, $"{path}.{name}", write);
        }

        var children = Children(layer, out var key);
        if (children == null)
            return;
        if (!TryField(dict, key, out var items) || items.Type != ValueType.Array)
            throw new RuntimeException($"{owner}.load() {path}.{key} must be an array");
        var rows = items.AsArray();
        if (rows.Count != children.Count)
            throw new RuntimeException($"{owner}.load() {path}.{key} has {rows.Count} entries, expected {children.Count}");
        for (var i = 0; i < children.Count; i++)
            Apply(owner, children[i], rows[i], $"{path}.{key}[{i}]", write);
    }

    private static void Copy(string owner, RuntimeValue live, RuntimeValue saved, string path, bool write)
    {
        var target = live.AsArray();
        if (saved.Type != ValueType.Array || saved.AsArray().Count != target.Count)
            throw new RuntimeException($"{owner}.load() {path} must have {target.Count} entries");
        var source = saved.AsArray();
        for (var i = 0; i < target.Count; i++)
        {
            if (target[i].Type == ValueType.Array)
            {
                Copy(owner, target[i], source[i], $"{path}[{i}]", write);
                continue;
            }

            if (source[i].Type is not (ValueType.Integer or ValueType.Float))
                throw new RuntimeException($"{owner}.load() {path}[{i}] must be a number");
            var number = DenseInstance.AsNumber(source[i]);
            if (double.IsNaN(number) || double.IsInfinity(number))
                throw new RuntimeException($"{owner}.load() {path}[{i}] must be finite");
            if (write)
                target[i] = RuntimeValue.Float(number);
        }
    }

    // parseJSON gives a JsonObject and a literal gives a dictionary; both answer TryGet.
    private static bool TryField(ObjectInstance state, string name, out RuntimeValue value)
    {
        if (state.TryGet(name, out var found) && found != null && found.Type is not (ValueType.Null or ValueType.Function))
        {
            value = found;
            return true;
        }

        value = RuntimeValue.Null();
        return false;
    }

    private static RuntimeValue Copy(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            return RuntimeValue.Float(DenseInstance.AsNumber(value));
        var items = value.AsArray();
        var copy = new List<RuntimeValue>(items.Count);
        foreach (var item in items)
            copy.Add(Copy(item));
        return RuntimeValue.Array(copy);
    }

    private static List<ObjectInstance>? Children(ObjectInstance layer, out string key)
    {
        key = layer is ParallelInstance ? "branches" : "layers";
        if (layer is not (SequentialInstance or ParallelInstance))
            return null;
        var children = new List<ObjectInstance>();
        foreach (var item in layer.Get(key).AsArray())
            children.Add(item.AsObject());
        return children;
    }

    private static string[] ParameterNames(ObjectInstance layer) => layer switch
    {
        DenseInstance or Conv2DInstance => ["weights", "bias"],
        ConvInstance => ["kernel"],
        EmbeddingInstance => ["table"],
        RnnInstance => ["weightsXh", "weightsHh", "bias"],
        LayerNormInstance => ["gamma", "beta"],
        AttentionInstance => ["query", "key"],
        BatchNorm2DInstance => ["gamma", "beta", "runningMean", "runningVar"],
        _ => []
    };

    private static string TypeName(ObjectInstance layer) => layer switch
    {
        DenseInstance => "Dense",
        SequentialInstance => "Sequential",
        ParallelInstance => "Parallel",
        IdentityInstance => "Identity",
        ConvInstance => "Conv",
        EmbeddingInstance => "Embedding",
        RnnInstance => "Rnn",
        LayerNormInstance => "LayerNorm",
        AttentionInstance => "Attention",
        Conv2DInstance => "Conv2D",
        MaxPool2DInstance => "MaxPool2D",
        AvgPool2DInstance => "AvgPool2D",
        FlattenInstance => "Flatten",
        BatchNorm2DInstance => "BatchNorm2D",
        Dropout2DInstance => "Dropout2D",
        GlobalAvgPool2DInstance => "GlobalAvgPool2D",
        GlobalMaxPool2DInstance => "GlobalMaxPool2D",
        ActivationInstance => "Activation",
        _ => throw new RuntimeException($"state() unsupported layer type: {layer.GetType().Name}")
    };
}
