// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Multi-channel 2D convolution. Tensors are CHW, or NCHW when the caller passes a batch.
/// A batch of one stays NCHW. Padding <c>same</c> uses <c>ceil(input / stride)</c> on each axis.
/// </summary>
public sealed class Conv2DInstance : ObjectInstance
{
    private readonly RuntimeValue _weights;
    private readonly RuntimeValue _bias;
    private readonly int _inChannels;
    private readonly int _outChannels;
    private readonly int _kernelSize;
    private readonly int _stride;
    private readonly string _padding;
    private double[,,,]? _input;
    private bool _inputBatched;
    private double[,,,]? _dWeights;
    private double[]? _dBias;

    public Conv2DInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 3 || args.Count > 6)
            throw new RuntimeException("Conv2D() expects 3 to 6 arguments: (inChannels, outChannels, kernelSize, stride?, padding?, scale?)");

        _inChannels = DenseInstance.RequirePositiveInt("Conv2D", args[0], "inChannels");
        _outChannels = DenseInstance.RequirePositiveInt("Conv2D", args[1], "outChannels");
        _kernelSize = DenseInstance.RequirePositiveInt("Conv2D", args[2], "kernelSize");
        _stride = args.Count >= 4 ? DenseInstance.RequirePositiveInt("Conv2D", args[3], "stride") : 1;
        _padding = "valid";
        if (args.Count >= 5)
        {
            if (args[4].Type != ValueType.String)
                throw new RuntimeException("Conv2D() padding must be a string");
            _padding = args[4].AsString();
            if (_padding is not ("valid" or "same"))
                throw new RuntimeException("Conv2D() padding must be \"valid\" or \"same\"");
        }

        var scale = args.Count == 6
            ? DenseInstance.RequireFinite("Conv2D", args[5], "scale")
            : 1.0 / Math.Sqrt(_inChannels * _kernelSize * _kernelSize);
        if (scale < 0)
            throw new RuntimeException("Conv2D() scale must be >= 0");

        var weightsList = new List<RuntimeValue>(_inChannels);
        for (var ic = 0; ic < _inChannels; ic++)
        {
            var outChannelsList = new List<RuntimeValue>(_outChannels);
            for (var oc = 0; oc < _outChannels; oc++)
                outChannelsList.Add(NeuralLayers.Matrix(_kernelSize, _kernelSize, scale));
            weightsList.Add(RuntimeValue.Array(outChannelsList));
        }

        _weights = RuntimeValue.Array(weightsList);
        _bias = NeuralLayers.Vector(_outChannels, scale);
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "inChannels")
            return RuntimeValue.Integer(_inChannels);
        if (name == "outChannels")
            return RuntimeValue.Integer(_outChannels);
        if (name == "kernelSize")
            return RuntimeValue.Integer(_kernelSize);
        if (name == "stride")
            return RuntimeValue.Integer(_stride);
        if (name == "padding")
            return RuntimeValue.String(_padding);
        if (name == "weights")
            return _weights;
        if (name == "bias")
            return _bias;
        if (name is "forward" or "backward" or "sgd")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Conv2D.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Conv2D.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Conv2D.forward() expects 1 argument: (input)");
        var input = NeuralLayers.ReadVolume("Conv2D.forward", args[0]);
        if (input.Channels != _inChannels)
            throw new RuntimeException($"Conv2D.forward() expected {_inChannels} input channels, got {input.Channels}");
        var (outH, outW, padTop, padLeft) = Geometry(input.Height, input.Width);
        if (outH < 1 || outW < 1)
            throw new RuntimeException("Conv2D.forward() input is smaller than the kernel");

        var weights = ReadWeights();
        var bias = NeuralLayers.ReadVector("Conv2D.forward", _bias, "bias");
        var output = new double[input.Batch, _outChannels, outH, outW];
        for (var b = 0; b < input.Batch; b++)
        {
            for (var oc = 0; oc < _outChannels; oc++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var sum = bias[oc];
                        var ihStart = oh * _stride - padTop;
                        var iwStart = ow * _stride - padLeft;
                        for (var ic = 0; ic < _inChannels; ic++)
                        {
                            for (var kh = 0; kh < _kernelSize; kh++)
                            {
                                for (var kw = 0; kw < _kernelSize; kw++)
                                {
                                    var ih = ihStart + kh;
                                    var iw = iwStart + kw;
                                    if (ih >= 0 && ih < input.Height && iw >= 0 && iw < input.Width)
                                        sum += input.Data[b, ic, ih, iw] * weights[ic, oc, kh, kw];
                                }
                            }
                        }

                        output[b, oc, oh, ow] = sum;
                    }
                }
            }
        }

        _input = input.Data;
        _inputBatched = input.Batched;
        return NeuralLayers.WriteVolume(output, input.Batched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Conv2D.backward() expects 1 argument: (upstream)");
        if (_input == null)
            throw new RuntimeException("Conv2D.backward() requires forward() first");
        var upstream = NeuralLayers.ReadVolume("Conv2D.backward", args[0]);
        var inH = _input.GetLength(2);
        var inW = _input.GetLength(3);
        var (outH, outW, padTop, padLeft) = Geometry(inH, inW);
        if (upstream.Batch != _input.GetLength(0) || upstream.Channels != _outChannels || upstream.Height != outH || upstream.Width != outW)
            throw new RuntimeException("Conv2D.backward() upstream must match the forward output");

        var weights = ReadWeights();
        var dInput = new double[_input.GetLength(0), _inChannels, inH, inW];
        var dWeights = new double[_inChannels, _outChannels, _kernelSize, _kernelSize];
        var dBias = new double[_outChannels];
        for (var b = 0; b < upstream.Batch; b++)
        {
            for (var oc = 0; oc < _outChannels; oc++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var grad = upstream.Data[b, oc, oh, ow];
                        dBias[oc] += grad;
                        var ihStart = oh * _stride - padTop;
                        var iwStart = ow * _stride - padLeft;
                        for (var ic = 0; ic < _inChannels; ic++)
                        {
                            for (var kh = 0; kh < _kernelSize; kh++)
                            {
                                for (var kw = 0; kw < _kernelSize; kw++)
                                {
                                    var ih = ihStart + kh;
                                    var iw = iwStart + kw;
                                    if (ih >= 0 && ih < inH && iw >= 0 && iw < inW)
                                    {
                                        dWeights[ic, oc, kh, kw] += grad * _input[b, ic, ih, iw];
                                        dInput[b, ic, ih, iw] += grad * weights[ic, oc, kh, kw];
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        _dWeights = dWeights;
        _dBias = dBias;
        return NeuralLayers.WriteVolume(dInput, _inputBatched);
    }

    private RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Conv2D.sgd() expects 1 argument: (lr)");
        if (_dWeights == null || _dBias == null)
            throw new RuntimeException("Conv2D.sgd() requires backward() first");
        var lr = DenseInstance.RequireFinite("Conv2D.sgd", args[0], "lr");
        ApplyWeights(_dWeights, lr);
        NeuralLayers.ApplyVector(_bias, _dBias, lr);
        return RuntimeValue.Null();
    }

    private (int outH, int outW, int padTop, int padLeft) Geometry(int inH, int inW)
    {
        if (_padding == "same")
        {
            var outH = (inH + _stride - 1) / _stride;
            var outW = (inW + _stride - 1) / _stride;
            var totalH = Math.Max(0, (outH - 1) * _stride + _kernelSize - inH);
            var totalW = Math.Max(0, (outW - 1) * _stride + _kernelSize - inW);
            return (outH, outW, totalH / 2, totalW / 2);
        }

        return ((inH - _kernelSize) / _stride + 1, (inW - _kernelSize) / _stride + 1, 0, 0);
    }

    private double[,,,] ReadWeights()
    {
        var weights = new double[_inChannels, _outChannels, _kernelSize, _kernelSize];
        var wArr = _weights.AsArray();
        for (var ic = 0; ic < _inChannels; ic++)
        {
            var ocArr = wArr[ic].AsArray();
            for (var oc = 0; oc < _outChannels; oc++)
            {
                var kernel = NeuralLayers.ReadMatrix("Conv2D", ocArr[oc], "weights");
                for (var kh = 0; kh < _kernelSize; kh++)
                {
                    for (var kw = 0; kw < _kernelSize; kw++)
                        weights[ic, oc, kh, kw] = kernel[kh][kw];
                }
            }
        }

        return weights;
    }

    private void ApplyWeights(double[,,,] grad, double lr)
    {
        var wArr = _weights.AsArray();
        for (var ic = 0; ic < _inChannels; ic++)
        {
            var ocArr = wArr[ic].AsArray();
            for (var oc = 0; oc < _outChannels; oc++)
            {
                var kernelArr = ocArr[oc].AsArray();
                for (var kh = 0; kh < _kernelSize; kh++)
                {
                    var rowArr = kernelArr[kh].AsArray();
                    for (var kw = 0; kw < _kernelSize; kw++)
                    {
                        var current = DenseInstance.AsNumber(rowArr[kw]);
                        rowArr[kw] = RuntimeValue.Float(current - lr * grad[ic, oc, kh, kw]);
                    }
                }
            }
        }
    }
}

/// <summary>2D max pooling. <c>backward</c> takes only the upstream gradient.</summary>
public sealed class MaxPool2DInstance : ObjectInstance
{
    private readonly int _poolSize;
    private readonly int _stride;
    private int[,,,]? _maxIndices;
    private int _batch;
    private int _channels;
    private int _inH;
    private int _inW;
    private bool _batched;

    public MaxPool2DInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new RuntimeException("MaxPool2D() expects 1 or 2 arguments: (poolSize, stride?)");
        _poolSize = DenseInstance.RequirePositiveInt("MaxPool2D", args[0], "poolSize");
        _stride = args.Count == 2 ? DenseInstance.RequirePositiveInt("MaxPool2D", args[1], "stride") : _poolSize;
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "poolSize")
            return RuntimeValue.Integer(_poolSize);
        if (name == "stride")
            return RuntimeValue.Integer(_stride);
        if (name is "forward" or "backward")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on MaxPool2D.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on MaxPool2D.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("MaxPool2D.forward() expects 1 argument: (input)");
        var input = NeuralLayers.ReadVolume("MaxPool2D.forward", args[0]);
        var outH = (input.Height - _poolSize) / _stride + 1;
        var outW = (input.Width - _poolSize) / _stride + 1;
        if (outH < 1 || outW < 1)
            throw new RuntimeException("MaxPool2D.forward() input is smaller than the pool");

        var output = new double[input.Batch, input.Channels, outH, outW];
        var indices = new int[input.Batch, input.Channels, outH, outW];
        for (var b = 0; b < input.Batch; b++)
        {
            for (var c = 0; c < input.Channels; c++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var maxVal = double.NegativeInfinity;
                        var maxIdx = 0;
                        var ihStart = oh * _stride;
                        var iwStart = ow * _stride;
                        for (var ph = 0; ph < _poolSize; ph++)
                        {
                            for (var pw = 0; pw < _poolSize; pw++)
                            {
                                var val = input.Data[b, c, ihStart + ph, iwStart + pw];
                                if (val > maxVal)
                                {
                                    maxVal = val;
                                    maxIdx = ph * _poolSize + pw;
                                }
                            }
                        }

                        output[b, c, oh, ow] = maxVal;
                        indices[b, c, oh, ow] = maxIdx;
                    }
                }
            }
        }

        _maxIndices = indices;
        _batch = input.Batch;
        _channels = input.Channels;
        _inH = input.Height;
        _inW = input.Width;
        _batched = input.Batched;
        return NeuralLayers.WriteVolume(output, input.Batched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("MaxPool2D.backward() expects 1 argument: (upstream)");
        if (_maxIndices == null)
            throw new RuntimeException("MaxPool2D.backward() requires forward() first");
        var upstream = NeuralLayers.ReadVolume("MaxPool2D.backward", args[0]);
        var outH = (_inH - _poolSize) / _stride + 1;
        var outW = (_inW - _poolSize) / _stride + 1;
        if (upstream.Batch != _batch || upstream.Channels != _channels || upstream.Height != outH || upstream.Width != outW)
            throw new RuntimeException("MaxPool2D.backward() upstream must match the forward output");

        var dInput = new double[_batch, _channels, _inH, _inW];
        for (var b = 0; b < _batch; b++)
        {
            for (var c = 0; c < _channels; c++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var maxIdx = _maxIndices[b, c, oh, ow];
                        var ph = maxIdx / _poolSize;
                        var pw = maxIdx % _poolSize;
                        dInput[b, c, oh * _stride + ph, ow * _stride + pw] += upstream.Data[b, c, oh, ow];
                    }
                }
            }
        }

        return NeuralLayers.WriteVolume(dInput, _batched);
    }
}

/// <summary>2D average pooling.</summary>
public sealed class AvgPool2DInstance : ObjectInstance
{
    private readonly int _poolSize;
    private readonly int _stride;
    private int _batch;
    private int _channels;
    private int _inH;
    private int _inW;
    private bool _batched;
    private bool _seen;

    public AvgPool2DInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new RuntimeException("AvgPool2D() expects 1 or 2 arguments: (poolSize, stride?)");
        _poolSize = DenseInstance.RequirePositiveInt("AvgPool2D", args[0], "poolSize");
        _stride = args.Count == 2 ? DenseInstance.RequirePositiveInt("AvgPool2D", args[1], "stride") : _poolSize;
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "poolSize")
            return RuntimeValue.Integer(_poolSize);
        if (name == "stride")
            return RuntimeValue.Integer(_stride);
        if (name is "forward" or "backward")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on AvgPool2D.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on AvgPool2D.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("AvgPool2D.forward() expects 1 argument: (input)");
        var input = NeuralLayers.ReadVolume("AvgPool2D.forward", args[0]);
        var outH = (input.Height - _poolSize) / _stride + 1;
        var outW = (input.Width - _poolSize) / _stride + 1;
        if (outH < 1 || outW < 1)
            throw new RuntimeException("AvgPool2D.forward() input is smaller than the pool");

        var output = new double[input.Batch, input.Channels, outH, outW];
        var poolArea = _poolSize * _poolSize;
        for (var b = 0; b < input.Batch; b++)
        {
            for (var c = 0; c < input.Channels; c++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var sum = 0.0;
                        var ihStart = oh * _stride;
                        var iwStart = ow * _stride;
                        for (var ph = 0; ph < _poolSize; ph++)
                        {
                            for (var pw = 0; pw < _poolSize; pw++)
                                sum += input.Data[b, c, ihStart + ph, iwStart + pw];
                        }

                        output[b, c, oh, ow] = sum / poolArea;
                    }
                }
            }
        }

        _batch = input.Batch;
        _channels = input.Channels;
        _inH = input.Height;
        _inW = input.Width;
        _batched = input.Batched;
        _seen = true;
        return NeuralLayers.WriteVolume(output, input.Batched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("AvgPool2D.backward() expects 1 argument: (upstream)");
        if (!_seen)
            throw new RuntimeException("AvgPool2D.backward() requires forward() first");
        var upstream = NeuralLayers.ReadVolume("AvgPool2D.backward", args[0]);
        var outH = (_inH - _poolSize) / _stride + 1;
        var outW = (_inW - _poolSize) / _stride + 1;
        if (upstream.Batch != _batch || upstream.Channels != _channels || upstream.Height != outH || upstream.Width != outW)
            throw new RuntimeException("AvgPool2D.backward() upstream must match the forward output");

        var dInput = new double[_batch, _channels, _inH, _inW];
        var poolArea = _poolSize * _poolSize;
        for (var b = 0; b < _batch; b++)
        {
            for (var c = 0; c < _channels; c++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var grad = upstream.Data[b, c, oh, ow] / poolArea;
                        var ihStart = oh * _stride;
                        var iwStart = ow * _stride;
                        for (var ph = 0; ph < _poolSize; ph++)
                        {
                            for (var pw = 0; pw < _poolSize; pw++)
                                dInput[b, c, ihStart + ph, iwStart + pw] += grad;
                        }
                    }
                }
            }
        }

        return NeuralLayers.WriteVolume(dInput, _batched);
    }
}

/// <summary>Flatten a nested numeric tensor to one vector, and reshape the gradient on the way back.</summary>
public sealed class FlattenInstance : ObjectInstance
{
    private RuntimeValue? _template;

    public FlattenInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 0)
            throw new RuntimeException("Flatten() expects no arguments");
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name is "forward" or "backward")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Flatten.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Flatten.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Flatten.forward() expects 1 argument: (input)");
        _template = NeuralLayers.CloneNumbers(args[0]);
        var flat = new List<double>();
        Collect(args[0], flat);
        if (flat.Count == 0)
            throw new RuntimeException("Flatten.forward() input must contain a number");
        return NeuralLayers.ToVector(flat.ToArray());
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Flatten.backward() expects 1 argument: (upstream)");
        if (_template == null)
            throw new RuntimeException("Flatten.backward() requires forward() first");
        var upstream = NeuralLayers.ReadVector("Flatten.backward", args[0], "upstream");
        var index = 0;
        var restored = Scatter(upstream, _template, ref index);
        if (index != upstream.Length)
            throw new RuntimeException("Flatten.backward() upstream length must match the forward output");
        return restored;
    }

    private static void Collect(RuntimeValue value, List<double> into)
    {
        if (value.Type == ValueType.Array)
        {
            foreach (var item in value.AsArray())
                Collect(item, into);
            return;
        }

        if (value.Type is not (ValueType.Integer or ValueType.Float))
            throw new RuntimeException("Flatten.forward() input must be numeric");
        into.Add(DenseInstance.AsNumber(value));
    }

    private static RuntimeValue Scatter(double[] values, RuntimeValue template, ref int index)
    {
        if (template.Type != ValueType.Array)
        {
            if (index >= values.Length)
                throw new RuntimeException("Flatten.backward() upstream length must match the forward output");
            return RuntimeValue.Float(values[index++]);
        }

        var source = template.AsArray();
        var copy = new List<RuntimeValue>(source.Count);
        foreach (var item in source)
            copy.Add(Scatter(values, item, ref index));
        return RuntimeValue.Array(copy);
    }
}

/// <summary>Per-channel batch normalization over the batch and the spatial axes.</summary>
public sealed class BatchNorm2DInstance : ObjectInstance
{
    private readonly RuntimeValue _gamma;
    private readonly RuntimeValue _beta;
    private readonly RuntimeValue _runningMean;
    private readonly RuntimeValue _runningVar;
    private readonly double _momentum;
    private readonly double _eps;
    private readonly int _numFeatures;
    private double[,,,]? _normalized;
    private double[]? _std;
    private double[]? _dGamma;
    private double[]? _dBeta;
    private bool _training = true;
    private bool _batched;

    public BatchNorm2DInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new RuntimeException("BatchNorm2D() expects 1 to 3 arguments: (numFeatures, momentum?, eps?)");
        _numFeatures = DenseInstance.RequirePositiveInt("BatchNorm2D", args[0], "numFeatures");
        _momentum = args.Count >= 2 ? DenseInstance.RequireFinite("BatchNorm2D", args[1], "momentum") : 0.1;
        _eps = args.Count == 3 ? DenseInstance.RequireFinite("BatchNorm2D", args[2], "eps") : 0.00001;
        _gamma = NeuralLayers.Filled(_numFeatures, 1.0);
        _beta = NeuralLayers.Filled(_numFeatures, 0.0);
        _runningMean = NeuralLayers.Filled(_numFeatures, 0.0);
        _runningVar = NeuralLayers.Filled(_numFeatures, 1.0);
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "numFeatures")
            return RuntimeValue.Integer(_numFeatures);
        if (name == "gamma")
            return _gamma;
        if (name == "beta")
            return _beta;
        if (name == "runningMean")
            return _runningMean;
        if (name == "runningVar")
            return _runningVar;
        if (name == "training")
            return RuntimeValue.Boolean(_training);
        if (name is "forward" or "backward" or "sgd" or "train" or "eval")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on BatchNorm2D.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            "train" => SetTraining(args, true),
            "eval" => SetTraining(args, false),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on BatchNorm2D.")
        };
    }

    private RuntimeValue SetTraining(List<RuntimeValue> args, bool training)
    {
        if (args.Count != 0)
            throw new RuntimeException(training
                ? "BatchNorm2D.train() expects no arguments"
                : "BatchNorm2D.eval() expects no arguments");
        _training = training;
        return RuntimeValue.Null();
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("BatchNorm2D.forward() expects 1 argument: (input)");
        var input = NeuralLayers.ReadVolume("BatchNorm2D.forward", args[0]);
        if (input.Channels != _numFeatures)
            throw new RuntimeException($"BatchNorm2D.forward() expected {_numFeatures} channels, got {input.Channels}");
        var gamma = NeuralLayers.ReadVector("BatchNorm2D.forward", _gamma, "gamma");
        var beta = NeuralLayers.ReadVector("BatchNorm2D.forward", _beta, "beta");
        var output = new double[input.Batch, input.Channels, input.Height, input.Width];
        _batched = input.Batched;

        if (_training)
        {
            var spatial = input.Batch * input.Height * input.Width;
            var mean = new double[input.Channels];
            var variance = new double[input.Channels];
            for (var c = 0; c < input.Channels; c++)
            {
                for (var b = 0; b < input.Batch; b++)
                {
                    for (var h = 0; h < input.Height; h++)
                    {
                        for (var w = 0; w < input.Width; w++)
                            mean[c] += input.Data[b, c, h, w];
                    }
                }

                mean[c] /= spatial;
            }

            for (var c = 0; c < input.Channels; c++)
            {
                for (var b = 0; b < input.Batch; b++)
                {
                    for (var h = 0; h < input.Height; h++)
                    {
                        for (var w = 0; w < input.Width; w++)
                        {
                            var diff = input.Data[b, c, h, w] - mean[c];
                            variance[c] += diff * diff;
                        }
                    }
                }

                variance[c] /= spatial;
            }

            var runningMean = NeuralLayers.ReadVector("BatchNorm2D", _runningMean, "runningMean");
            var runningVar = NeuralLayers.ReadVector("BatchNorm2D", _runningVar, "runningVar");
            for (var c = 0; c < input.Channels; c++)
            {
                runningMean[c] = (1 - _momentum) * runningMean[c] + _momentum * mean[c];
                runningVar[c] = (1 - _momentum) * runningVar[c] + _momentum * variance[c];
            }

            WriteOwnedVector(_runningMean, runningMean);
            WriteOwnedVector(_runningVar, runningVar);

            _std = new double[input.Channels];
            _normalized = new double[input.Batch, input.Channels, input.Height, input.Width];
            for (var c = 0; c < input.Channels; c++)
            {
                _std[c] = Math.Sqrt(variance[c] + _eps);
                for (var b = 0; b < input.Batch; b++)
                {
                    for (var h = 0; h < input.Height; h++)
                    {
                        for (var w = 0; w < input.Width; w++)
                        {
                            _normalized[b, c, h, w] = (input.Data[b, c, h, w] - mean[c]) / _std[c];
                            output[b, c, h, w] = gamma[c] * _normalized[b, c, h, w] + beta[c];
                        }
                    }
                }
            }
        }
        else
        {
            var runningMean = NeuralLayers.ReadVector("BatchNorm2D", _runningMean, "runningMean");
            var runningVar = NeuralLayers.ReadVector("BatchNorm2D", _runningVar, "runningVar");
            for (var c = 0; c < input.Channels; c++)
            {
                var std = Math.Sqrt(runningVar[c] + _eps);
                for (var b = 0; b < input.Batch; b++)
                {
                    for (var h = 0; h < input.Height; h++)
                    {
                        for (var w = 0; w < input.Width; w++)
                            output[b, c, h, w] = gamma[c] * ((input.Data[b, c, h, w] - runningMean[c]) / std) + beta[c];
                    }
                }
            }

            _normalized = null;
            _std = null;
        }

        return NeuralLayers.WriteVolume(output, input.Batched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("BatchNorm2D.backward() expects 1 argument: (upstream)");
        if (_normalized == null || _std == null)
            throw new RuntimeException("BatchNorm2D.backward() requires forward() first in training mode");
        var upstream = NeuralLayers.ReadVolume("BatchNorm2D.backward", args[0]);
        var batch = _normalized.GetLength(0);
        var channels = _normalized.GetLength(1);
        var height = _normalized.GetLength(2);
        var width = _normalized.GetLength(3);
        if (upstream.Batch != batch || upstream.Channels != channels || upstream.Height != height || upstream.Width != width)
            throw new RuntimeException("BatchNorm2D.backward() upstream must match the forward output");

        var gamma = NeuralLayers.ReadVector("BatchNorm2D", _gamma, "gamma");
        var dGamma = new double[channels];
        var dBeta = new double[channels];
        var dInput = new double[batch, channels, height, width];
        var spatial = batch * height * width;
        for (var c = 0; c < channels; c++)
        {
            var meanDnorm = 0.0;
            var meanDnormNorm = 0.0;
            for (var b = 0; b < batch; b++)
            {
                for (var h = 0; h < height; h++)
                {
                    for (var w = 0; w < width; w++)
                    {
                        var grad = upstream.Data[b, c, h, w];
                        dBeta[c] += grad;
                        dGamma[c] += grad * _normalized[b, c, h, w];
                        var dnorm = grad * gamma[c];
                        meanDnorm += dnorm;
                        meanDnormNorm += dnorm * _normalized[b, c, h, w];
                    }
                }
            }

            meanDnorm /= spatial;
            meanDnormNorm /= spatial;
            for (var b = 0; b < batch; b++)
            {
                for (var h = 0; h < height; h++)
                {
                    for (var w = 0; w < width; w++)
                    {
                        var dnorm = upstream.Data[b, c, h, w] * gamma[c];
                        dInput[b, c, h, w] = (dnorm - meanDnorm - _normalized[b, c, h, w] * meanDnormNorm) / _std[c];
                    }
                }
            }
        }

        _dGamma = dGamma;
        _dBeta = dBeta;
        return NeuralLayers.WriteVolume(dInput, _batched);
    }

    private RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("BatchNorm2D.sgd() expects 1 argument: (lr)");
        if (_dGamma == null || _dBeta == null)
            throw new RuntimeException("BatchNorm2D.sgd() requires backward() first");
        var lr = DenseInstance.RequireFinite("BatchNorm2D.sgd", args[0], "lr");
        NeuralLayers.ApplyVector(_gamma, _dGamma, lr);
        NeuralLayers.ApplyVector(_beta, _dBeta, lr);
        return RuntimeValue.Null();
    }

    private static void WriteOwnedVector(RuntimeValue values, double[] data)
    {
        var arr = values.AsArray();
        for (var i = 0; i < data.Length; i++)
            arr[i] = RuntimeValue.Float(data[i]);
    }
}

/// <summary>Element dropout on a CHW or NCHW map. <c>eval</c> leaves the map unchanged.</summary>
public sealed class Dropout2DInstance : ObjectInstance
{
    private readonly double _p;
    private bool[,,,]? _mask;
    private bool _batched;
    private bool _seen;
    private bool _training = true;

    public Dropout2DInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 1)
            throw new RuntimeException("Dropout2D() expects 1 argument: (p)");
        _p = DenseInstance.RequireFinite("Dropout2D", args[0], "p");
        if (_p < 0 || _p >= 1)
            throw new RuntimeException("Dropout2D() p must be in [0, 1)");
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "p")
            return RuntimeValue.Float(_p);
        if (name == "training")
            return RuntimeValue.Boolean(_training);
        if (name is "forward" or "backward" or "train" or "eval")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Dropout2D.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "train" => SetTraining(args, true),
            "eval" => SetTraining(args, false),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Dropout2D.")
        };
    }

    private RuntimeValue SetTraining(List<RuntimeValue> args, bool training)
    {
        if (args.Count != 0)
            throw new RuntimeException(training
                ? "Dropout2D.train() expects no arguments"
                : "Dropout2D.eval() expects no arguments");
        _training = training;
        return RuntimeValue.Null();
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Dropout2D.forward() expects 1 argument: (input)");
        var input = NeuralLayers.ReadVolume("Dropout2D.forward", args[0]);
        var output = new double[input.Batch, input.Channels, input.Height, input.Width];
        _batched = input.Batched;
        _seen = true;
        if (_training && _p > 0)
        {
            _mask = new bool[input.Batch, input.Channels, input.Height, input.Width];
            var scale = 1.0 / (1.0 - _p);
            for (var b = 0; b < input.Batch; b++)
            {
                for (var c = 0; c < input.Channels; c++)
                {
                    for (var h = 0; h < input.Height; h++)
                    {
                        for (var w = 0; w < input.Width; w++)
                        {
                            var rand = BuiltInFunctions.CallBuiltIn("randomFloat", new List<RuntimeValue>
                            {
                                RuntimeValue.Float(0.0),
                                RuntimeValue.Float(1.0)
                            }, null).AsFloat();
                            _mask[b, c, h, w] = rand >= _p;
                            output[b, c, h, w] = _mask[b, c, h, w] ? input.Data[b, c, h, w] * scale : 0.0;
                        }
                    }
                }
            }
        }
        else
        {
            _mask = null;
            for (var b = 0; b < input.Batch; b++)
            {
                for (var c = 0; c < input.Channels; c++)
                {
                    for (var h = 0; h < input.Height; h++)
                    {
                        for (var w = 0; w < input.Width; w++)
                            output[b, c, h, w] = input.Data[b, c, h, w];
                    }
                }
            }
        }

        return NeuralLayers.WriteVolume(output, input.Batched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Dropout2D.backward() expects 1 argument: (upstream)");
        if (!_seen)
            throw new RuntimeException("Dropout2D.backward() requires forward() first");
        var upstream = NeuralLayers.ReadVolume("Dropout2D.backward", args[0]);
        var dInput = new double[upstream.Batch, upstream.Channels, upstream.Height, upstream.Width];
        if (_mask != null)
        {
            if (_mask.GetLength(0) != upstream.Batch || _mask.GetLength(1) != upstream.Channels
                || _mask.GetLength(2) != upstream.Height || _mask.GetLength(3) != upstream.Width)
                throw new RuntimeException("Dropout2D.backward() upstream must match the forward output");
            var scale = 1.0 / (1.0 - _p);
            for (var b = 0; b < upstream.Batch; b++)
            {
                for (var c = 0; c < upstream.Channels; c++)
                {
                    for (var h = 0; h < upstream.Height; h++)
                    {
                        for (var w = 0; w < upstream.Width; w++)
                            dInput[b, c, h, w] = _mask[b, c, h, w] ? upstream.Data[b, c, h, w] * scale : 0.0;
                    }
                }
            }
        }
        else
        {
            for (var b = 0; b < upstream.Batch; b++)
            {
                for (var c = 0; c < upstream.Channels; c++)
                {
                    for (var h = 0; h < upstream.Height; h++)
                    {
                        for (var w = 0; w < upstream.Width; w++)
                            dInput[b, c, h, w] = upstream.Data[b, c, h, w];
                    }
                }
            }
        }

        return NeuralLayers.WriteVolume(dInput, _batched);
    }
}

/// <summary>Average each channel down to one number.</summary>
public sealed class GlobalAvgPool2DInstance : ObjectInstance
{
    private int _batch;
    private int _channels;
    private int _height;
    private int _width;
    private bool _batched;
    private bool _seen;

    public GlobalAvgPool2DInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 0)
            throw new RuntimeException("GlobalAvgPool2D() expects no arguments");
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name is "forward" or "backward")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on GlobalAvgPool2D.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on GlobalAvgPool2D.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("GlobalAvgPool2D.forward() expects 1 argument: (input)");
        var input = NeuralLayers.ReadVolume("GlobalAvgPool2D.forward", args[0]);
        var output = new double[input.Batch, input.Channels];
        var spatial = input.Height * input.Width;
        for (var b = 0; b < input.Batch; b++)
        {
            for (var c = 0; c < input.Channels; c++)
            {
                var sum = 0.0;
                for (var h = 0; h < input.Height; h++)
                {
                    for (var w = 0; w < input.Width; w++)
                        sum += input.Data[b, c, h, w];
                }

                output[b, c] = sum / spatial;
            }
        }

        _batch = input.Batch;
        _channels = input.Channels;
        _height = input.Height;
        _width = input.Width;
        _batched = input.Batched;
        _seen = true;
        return WriteChannels(output, input.Batched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("GlobalAvgPool2D.backward() expects 1 argument: (upstream)");
        if (!_seen)
            throw new RuntimeException("GlobalAvgPool2D.backward() requires forward() first");
        var upstream = ReadChannels("GlobalAvgPool2D.backward", args[0]);
        if (upstream.GetLength(0) != _batch || upstream.GetLength(1) != _channels)
            throw new RuntimeException("GlobalAvgPool2D.backward() upstream must match the forward output");
        var dInput = new double[_batch, _channels, _height, _width];
        var grad = 1.0 / (_height * _width);
        for (var b = 0; b < _batch; b++)
        {
            for (var c = 0; c < _channels; c++)
            {
                var value = upstream[b, c] * grad;
                for (var h = 0; h < _height; h++)
                {
                    for (var w = 0; w < _width; w++)
                        dInput[b, c, h, w] = value;
                }
            }
        }

        return NeuralLayers.WriteVolume(dInput, _batched);
    }

    internal static RuntimeValue WriteChannels(double[,] values, bool batched)
    {
        var batch = values.GetLength(0);
        var channels = values.GetLength(1);
        if (!batched)
        {
            var row = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
                row.Add(RuntimeValue.Float(values[0, c]));
            return RuntimeValue.Array(row);
        }

        var rows = new List<RuntimeValue>(batch);
        for (var b = 0; b < batch; b++)
        {
            var row = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
                row.Add(RuntimeValue.Float(values[b, c]));
            rows.Add(RuntimeValue.Array(row));
        }

        return RuntimeValue.Array(rows);
    }

    internal static double[,] ReadChannels(string name, RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"{name} upstream must be an array");
        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException($"{name} upstream must be non-empty");
        if (arr[0].Type == ValueType.Array)
        {
            var channels = arr[0].AsArray().Count;
            var tensor = new double[arr.Count, channels];
            for (var b = 0; b < arr.Count; b++)
            {
                var row = NeuralLayers.ReadVector(name, arr[b], "upstream");
                if (row.Length != channels)
                    throw new RuntimeException($"{name} upstream rows must have the same length");
                for (var c = 0; c < channels; c++)
                    tensor[b, c] = row[c];
            }

            return tensor;
        }

        var vector = NeuralLayers.ReadVector(name, value, "upstream");
        var single = new double[1, vector.Length];
        for (var c = 0; c < vector.Length; c++)
            single[0, c] = vector[c];
        return single;
    }
}

/// <summary>The maximum of each channel.</summary>
public sealed class GlobalMaxPool2DInstance : ObjectInstance
{
    private int _batch;
    private int _channels;
    private int _height;
    private int _width;
    private bool _batched;
    private int[,]? _maxIndices;

    public GlobalMaxPool2DInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 0)
            throw new RuntimeException("GlobalMaxPool2D() expects no arguments");
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name is "forward" or "backward")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on GlobalMaxPool2D.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on GlobalMaxPool2D.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("GlobalMaxPool2D.forward() expects 1 argument: (input)");
        var input = NeuralLayers.ReadVolume("GlobalMaxPool2D.forward", args[0]);
        var output = new double[input.Batch, input.Channels];
        var indices = new int[input.Batch, input.Channels];
        for (var b = 0; b < input.Batch; b++)
        {
            for (var c = 0; c < input.Channels; c++)
            {
                var maxVal = double.NegativeInfinity;
                var maxIdx = 0;
                for (var h = 0; h < input.Height; h++)
                {
                    for (var w = 0; w < input.Width; w++)
                    {
                        var val = input.Data[b, c, h, w];
                        if (val > maxVal)
                        {
                            maxVal = val;
                            maxIdx = h * input.Width + w;
                        }
                    }
                }

                output[b, c] = maxVal;
                indices[b, c] = maxIdx;
            }
        }

        _batch = input.Batch;
        _channels = input.Channels;
        _height = input.Height;
        _width = input.Width;
        _batched = input.Batched;
        _maxIndices = indices;
        return GlobalAvgPool2DInstance.WriteChannels(output, input.Batched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("GlobalMaxPool2D.backward() expects 1 argument: (upstream)");
        if (_maxIndices == null)
            throw new RuntimeException("GlobalMaxPool2D.backward() requires forward() first");
        var upstream = GlobalAvgPool2DInstance.ReadChannels("GlobalMaxPool2D.backward", args[0]);
        if (upstream.GetLength(0) != _batch || upstream.GetLength(1) != _channels)
            throw new RuntimeException("GlobalMaxPool2D.backward() upstream must match the forward output");
        var dInput = new double[_batch, _channels, _height, _width];
        for (var b = 0; b < _batch; b++)
        {
            for (var c = 0; c < _channels; c++)
            {
                var idx = _maxIndices[b, c];
                dInput[b, c, idx / _width, idx % _width] = upstream[b, c];
            }
        }

        return NeuralLayers.WriteVolume(dInput, _batched);
    }
}

/// <summary>Elementwise activation. Works on a vector or on a nested tensor. No parameters.</summary>
public sealed class ActivationInstance : ObjectInstance
{
    private readonly string _activation;
    private RuntimeValue? _pre;

    public ActivationInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 1)
            throw new RuntimeException("Activation() expects 1 argument: (name)");
        _activation = NeuralLayers.RequireActivation("Activation", args[0]);
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "activation")
            return RuntimeValue.String(_activation);
        if (name is "forward" or "backward")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Activation.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Activation.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Activation.forward() expects 1 argument: (x)");
        _pre = NeuralLayers.CloneNumbers(args[0]);
        return NeuralLayers.ApplyActivation("Activation.forward", _activation, _pre, false);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Activation.backward() expects 1 argument: (upstream)");
        if (_pre == null)
            throw new RuntimeException("Activation.backward() requires forward() first");
        var deriv = NeuralLayers.ApplyActivation("Activation.backward", _activation, _pre, true);
        return NeuralLayers.ZipMultiply("Activation.backward", args[0], deriv);
    }
}
