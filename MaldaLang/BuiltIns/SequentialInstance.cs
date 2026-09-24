// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// A fixed stack of <see cref="DenseInstance"/> layers. <c>fit</c> is online SGD.
/// It is not an autograd tape and it does not implement Adam.
/// </summary>
public sealed class SequentialInstance : ObjectInstance
{
    private readonly List<DenseInstance> _layers;

    public SequentialInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 1)
            throw new RuntimeException("Sequential() expects 1 argument: (layers)");
        _layers = ReadLayers(args[0]);
    }

    private SequentialInstance(List<DenseInstance> layers) : base(null)
    {
        _layers = layers;
    }

    public static SequentialInstance FromSpecs(RuntimeValue specs)
    {
        if (specs.Type != ValueType.Array)
            throw new RuntimeException("sequential() expects an array of [in, out, activation?, scale?]");
        var rows = specs.AsArray();
        if (rows.Count == 0)
            throw new RuntimeException("sequential() expects at least one layer");

        var layers = new List<DenseInstance>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Type != ValueType.Array)
                throw new RuntimeException("sequential() expects an array of [in, out, activation?, scale?]");
            layers.Add(new DenseInstance(row.AsArray()));
        }

        return new SequentialInstance(layers);
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "layers")
        {
            var list = new List<RuntimeValue>(_layers.Count);
            foreach (var layer in _layers)
                list.Add(RuntimeValue.Object(layer));
            return RuntimeValue.Array(list);
        }

        if (name is "forward" or "backward" or "sgd" or "fit")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }

        throw new RuntimeException($"Undefined property '{name}' on Sequential.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            "fit" => Fit(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Sequential.")
        };
    }

    public RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Sequential.forward() expects 1 argument: (x)");
        return Forward(args[0]);
    }

    public RuntimeValue Forward(RuntimeValue input)
    {
        var current = input;
        foreach (var layer in _layers)
            current = layer.Forward(current);
        return current;
    }

    public RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Sequential.backward() expects 1 argument: (upstream)");
        var upstream = args[0];
        for (var i = _layers.Count - 1; i >= 0; i--)
            upstream = _layers[i].Backward(new List<RuntimeValue> { upstream });
        return upstream;
    }

    public RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Sequential.sgd() expects 1 argument: (lr)");
        var learningRate = DenseInstance.RequireFinite("Sequential.sgd", args[0], "lr");
        foreach (var layer in _layers)
            layer.Sgd(learningRate);
        return RuntimeValue.Null();
    }

    public RuntimeValue Fit(List<RuntimeValue> args)
    {
        if (args.Count < 4 || args.Count > 5)
            throw new RuntimeException("Sequential.fit() expects 4 or 5 arguments: (inputs, targets, epochs, lr, loss?)");
        if (args[0].Type != ValueType.Array || args[1].Type != ValueType.Array)
            throw new RuntimeException("Sequential.fit() expects inputs and targets to be arrays");

        var inputs = args[0].AsArray();
        var targets = args[1].AsArray();
        if (inputs.Count == 0 || inputs.Count != targets.Count)
            throw new RuntimeException("Sequential.fit() inputs and targets must be non-empty and the same length");

        var epochs = DenseInstance.RequirePositiveInt("Sequential.fit", args[2], "epochs");
        var learningRate = DenseInstance.RequireFinite("Sequential.fit", args[3], "lr");
        var loss = "mse";
        if (args.Count == 5)
        {
            if (args[4].Type != ValueType.String)
                throw new RuntimeException("Sequential.fit() loss must be a string");
            loss = args[4].AsString();
        }

        if (loss is not ("mse" or "crossEntropy"))
            throw new RuntimeException("Sequential.fit() loss must be \"mse\" or \"crossEntropy\"");

        var mean = 0.0;
        for (var epoch = 0; epoch < epochs; epoch++)
        {
            var total = 0.0;
            for (var sample = 0; sample < inputs.Count; sample++)
            {
                var output = Forward(inputs[sample]);
                RuntimeValue upstream;
                if (loss == "mse")
                {
                    var target = AsTargetVector(targets[sample], output);
                    total += NnStdLib.Call("mse", new List<RuntimeValue> { output, target }).AsFloat();
                    upstream = NnStdLib.MseGrad(new List<RuntimeValue> { output, target });
                }
                else
                {
                    total += NnStdLib.Call("crossEntropyFromLogits", new List<RuntimeValue> { output, targets[sample] }).AsFloat();
                    upstream = NnStdLib.SoftmaxGrad(new List<RuntimeValue> { output, targets[sample] });
                }

                Backward(new List<RuntimeValue> { upstream });
                Sgd(new List<RuntimeValue> { RuntimeValue.Float(learningRate) });
            }

            mean = total / inputs.Count;
        }

        return RuntimeValue.Float(mean);
    }

    private static List<DenseInstance> ReadLayers(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("Sequential() expects an array of Dense layers");
        var items = value.AsArray();
        if (items.Count == 0)
            throw new RuntimeException("Sequential() expects at least one Dense layer");

        var layers = new List<DenseInstance>(items.Count);
        foreach (var item in items)
        {
            if (item.Type != ValueType.Object || item.AsObject() is not DenseInstance dense)
                throw new RuntimeException("Sequential() expects an array of Dense layers");
            layers.Add(dense);
        }

        return layers;
    }

    private static RuntimeValue AsTargetVector(RuntimeValue target, RuntimeValue output)
    {
        if (target.Type is ValueType.Integer or ValueType.Float)
        {
            if (output.AsArray().Count != 1)
                throw new RuntimeException("Sequential.fit() scalar targets require one output");
            return RuntimeValue.Array(new List<RuntimeValue> { target });
        }

        if (target.Type == ValueType.Array && !DenseInstance.IsMatrix(target))
            return target;
        throw new RuntimeException("Sequential.fit() mse targets must be a number or a numeric vector");
    }
}
