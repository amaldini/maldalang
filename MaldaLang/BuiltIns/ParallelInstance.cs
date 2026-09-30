// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Fan-out of layers that already have <c>forward</c> and <c>backward</c>.
/// <c>add</c> sums same-shaped outputs. <c>concat</c> joins vectors, or joins maps on the channel axis.
/// </summary>
public sealed class ParallelInstance : ObjectInstance
{
    private readonly List<ObjectInstance> _branches;
    private readonly string _merge;
    private bool _ready;
    private int _rank;
    private int _batch;
    private int[] _sizes = [];

    public ParallelInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 2)
            throw new RuntimeException("Parallel() expects 2 arguments: (branches, merge)");
        _branches = ReadBranches(args[0]);
        if (args[1].Type != ValueType.String)
            throw new RuntimeException("Parallel() merge must be a string");
        _merge = args[1].AsString();
        if (_merge is not ("add" or "concat"))
            throw new RuntimeException("Parallel() merge must be \"add\" or \"concat\"");
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "branches")
        {
            var list = new List<RuntimeValue>(_branches.Count);
            foreach (var branch in _branches)
                list.Add(RuntimeValue.Object(branch));
            return RuntimeValue.Array(list);
        }

        if (name == "merge")
            return RuntimeValue.String(_merge);

        if (name is "forward" or "backward" or "sgd")
            return NeuralLayers.Method(this, name);

        throw new RuntimeException($"Undefined property '{name}' on Parallel.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Parallel.")
        };
    }

    public RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Parallel.forward() expects 1 argument: (x)");
        var outputs = new List<RuntimeValue>(_branches.Count);
        foreach (var branch in _branches)
            outputs.Add(SequentialInstance.CallLayerMethod(branch, "forward", new List<RuntimeValue> { args[0] }, "Parallel"));

        if (_merge == "add")
        {
            var acc = outputs[0];
            for (var i = 1; i < outputs.Count; i++)
                acc = Add("Parallel.forward", acc, outputs[i]);
            _ready = true;
            _rank = 0;
            return acc;
        }

        var first = ShapeOf("Parallel.forward", outputs[0]);
        _rank = first.Length;
        _batch = _rank == 4 ? first[0] : 0;
        _sizes = new int[outputs.Count];
        for (var i = 0; i < outputs.Count; i++)
        {
            var shape = i == 0 ? first : ShapeOf("Parallel.forward", outputs[i]);
            _sizes[i] = ConcatSize("Parallel.forward", first, shape);
        }

        _ready = true;
        return Concat(outputs, first);
    }

    public RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Parallel.backward() expects 1 argument: (upstream)");
        if (!_ready)
            throw new RuntimeException("Parallel.backward() requires forward() first");

        var upstream = args[0];
        var grads = new List<RuntimeValue>(_branches.Count);
        if (_merge == "add")
        {
            for (var i = 0; i < _branches.Count; i++)
            {
                var slice = NeuralLayers.CloneNumbers(upstream);
                grads.Add(SequentialInstance.CallLayerMethod(_branches[i], "backward", new List<RuntimeValue> { slice }, "Parallel"));
            }
        }
        else
        {
            RequireConcatUpstream(upstream);
            for (var i = 0; i < _branches.Count; i++)
            {
                var slice = Slice(upstream, i);
                grads.Add(SequentialInstance.CallLayerMethod(_branches[i], "backward", new List<RuntimeValue> { slice }, "Parallel"));
            }
        }

        var sum = grads[0];
        for (var i = 1; i < grads.Count; i++)
            sum = Add("Parallel.backward", sum, grads[i]);
        return sum;
    }

    public RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Parallel.sgd() expects 1 argument: (lr)");
        var learningRate = DenseInstance.RequireFinite("Parallel.sgd", args[0], "lr");
        foreach (var branch in _branches)
        {
            if (SequentialInstance.HasMethod(branch, "sgd"))
                SequentialInstance.CallLayerMethod(branch, "sgd", new List<RuntimeValue> { RuntimeValue.Float(learningRate) }, "Parallel");
        }

        return RuntimeValue.Null();
    }

    private void RequireConcatUpstream(RuntimeValue upstream)
    {
        var total = 0;
        foreach (var size in _sizes)
            total += size;
        if (_rank is 1 or 3)
        {
            if (upstream.Type != ValueType.Array || upstream.AsArray().Count != total)
                throw new RuntimeException("Parallel.backward() upstream length must match the forward output");
            return;
        }

        if (upstream.Type != ValueType.Array)
            throw new RuntimeException("Parallel.backward() upstream length must match the forward output");
        var batches = upstream.AsArray();
        if (batches.Count != _batch)
            throw new RuntimeException("Parallel.backward() upstream length must match the forward output");
        foreach (var batch in batches)
        {
            if (batch.Type != ValueType.Array || batch.AsArray().Count != total)
                throw new RuntimeException("Parallel.backward() upstream length must match the forward output");
        }
    }

    private RuntimeValue Slice(RuntimeValue upstream, int branchIndex)
    {
        var start = 0;
        for (var i = 0; i < branchIndex; i++)
            start += _sizes[i];
        var count = _sizes[branchIndex];
        if (_rank is 1 or 3)
        {
            var all = upstream.AsArray();
            var slice = new List<RuntimeValue>(count);
            for (var i = 0; i < count; i++)
                slice.Add(all[start + i]);
            return NeuralLayers.CloneNumbers(RuntimeValue.Array(slice));
        }

        var batches = upstream.AsArray();
        var result = new List<RuntimeValue>(batches.Count);
        foreach (var batch in batches)
        {
            var channels = batch.AsArray();
            var slice = new List<RuntimeValue>(count);
            for (var c = 0; c < count; c++)
                slice.Add(channels[start + c]);
            result.Add(RuntimeValue.Array(slice));
        }

        return NeuralLayers.CloneNumbers(RuntimeValue.Array(result));
    }

    private static int ConcatSize(string name, int[] first, int[] shape)
    {
        if (shape.Length != first.Length)
            throw new RuntimeException($"{name}() concat expects a vector or a CHW or NCHW map");
        if (first.Length == 1)
            return shape[0];
        if (first.Length == 3)
        {
            if (shape[1] != first[1] || shape[2] != first[2])
                throw new RuntimeException($"{name}() concat maps must share height and width");
            return shape[0];
        }

        if (first.Length == 4)
        {
            if (shape[0] != first[0] || shape[2] != first[2] || shape[3] != first[3])
                throw new RuntimeException($"{name}() concat maps must share batch, height, and width");
            return shape[1];
        }

        throw new RuntimeException($"{name}() concat expects a vector or a CHW or NCHW map");
    }

    private static RuntimeValue Concat(List<RuntimeValue> outputs, int[] first)
    {
        if (first.Length == 1)
        {
            var joined = new List<RuntimeValue>();
            foreach (var output in outputs)
            {
                foreach (var item in output.AsArray())
                    joined.Add(item);
            }

            return RuntimeValue.Array(joined);
        }

        if (first.Length == 3)
        {
            var channels = new List<RuntimeValue>();
            foreach (var output in outputs)
            {
                foreach (var channel in output.AsArray())
                    channels.Add(NeuralLayers.CloneNumbers(channel));
            }

            return RuntimeValue.Array(channels);
        }

        var batch = first[0];
        var batches = new List<RuntimeValue>(batch);
        for (var b = 0; b < batch; b++)
        {
            var channels = new List<RuntimeValue>();
            foreach (var output in outputs)
            {
                foreach (var channel in output.AsArray()[b].AsArray())
                    channels.Add(NeuralLayers.CloneNumbers(channel));
            }

            batches.Add(RuntimeValue.Array(channels));
        }

        return RuntimeValue.Array(batches);
    }

    private static RuntimeValue Add(string name, RuntimeValue left, RuntimeValue right)
    {
        var leftArray = left.Type == ValueType.Array;
        var rightArray = right.Type == ValueType.Array;
        if (leftArray || rightArray)
        {
            if (!leftArray || !rightArray)
                throw new RuntimeException($"{name}() shapes must match");
            var a = left.AsArray();
            var b = right.AsArray();
            if (a.Count != b.Count)
                throw new RuntimeException($"{name}() shapes must match");
            var sum = new List<RuntimeValue>(a.Count);
            for (var i = 0; i < a.Count; i++)
                sum.Add(Add(name, a[i], b[i]));
            return RuntimeValue.Array(sum);
        }

        if (left.Type is not (ValueType.Integer or ValueType.Float) || right.Type is not (ValueType.Integer or ValueType.Float))
            throw new RuntimeException($"{name}() shapes must match");
        return RuntimeValue.Float(DenseInstance.AsNumber(left) + DenseInstance.AsNumber(right));
    }

    private static int[] ShapeOf(string name, RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"{name}() output must be an array");
        var dims = new List<int>();
        var spine = value;
        while (spine.Type == ValueType.Array)
        {
            var items = spine.AsArray();
            if (items.Count == 0)
                throw new RuntimeException($"{name}() output must be non-empty");
            dims.Add(items.Count);
            spine = items[0];
        }

        if (spine.Type is not (ValueType.Integer or ValueType.Float))
            throw new RuntimeException($"{name}() output must be numeric");
        VerifyShape(name, value, dims, 0);
        return dims.ToArray();
    }

    private static void VerifyShape(string name, RuntimeValue value, List<int> dims, int depth)
    {
        if (depth == dims.Count)
        {
            if (value.Type is not (ValueType.Integer or ValueType.Float))
                throw new RuntimeException($"{name}() output must be rectangular");
            return;
        }

        if (value.Type != ValueType.Array || value.AsArray().Count != dims[depth])
            throw new RuntimeException($"{name}() output must be rectangular");
        foreach (var item in value.AsArray())
            VerifyShape(name, item, dims, depth + 1);
    }

    private static List<ObjectInstance> ReadBranches(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("Parallel() expects an array of layer instances");
        var items = value.AsArray();
        if (items.Count < 2)
            throw new RuntimeException("Parallel() expects at least two branches");

        var branches = new List<ObjectInstance>(items.Count);
        foreach (var item in items)
        {
            if (item.Type != ValueType.Object)
                throw new RuntimeException("Parallel() expects an array of layer instances");
            var layer = item.AsObject();
            if (!SequentialInstance.HasMethod(layer, "forward"))
                throw new RuntimeException("Parallel() branch must have a forward() method");
            branches.Add(layer);
        }

        return branches;
    }
}

/// <summary>Returns its input unchanged. The skip branch of an <c>add</c> <see cref="ParallelInstance"/>.</summary>
public sealed class IdentityInstance : ObjectInstance
{
    public IdentityInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 0)
            throw new RuntimeException("Identity() expects no arguments");
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name is "forward" or "backward")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Identity.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Identity.")
        };
    }

    private static RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Identity.forward() expects 1 argument: (x)");
        return args[0];
    }

    private static RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Identity.backward() expects 1 argument: (upstream)");
        return args[0];
    }
}
