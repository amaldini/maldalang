// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Enhanced 2D convolution with multi-channel support, padding, stride, and bias.
/// </summary>
public sealed class Conv2DInstance : ObjectInstance
{
    private readonly RuntimeValue _weights; // [inChannels, outChannels, kernelH, kernelW]
    private readonly RuntimeValue _bias;    // [outChannels]
    private readonly int _inChannels;
    private readonly int _outChannels;
    private readonly int _kernelSize;
    private readonly int _stride;
    private readonly string _padding;
    private double[,,,]? _input;
    private double[,,,]? _dWeights;
    private double[]? _dBias;

    public Conv2DInstance(List<RuntimeValue> args) : base(null)
    {
        // Conv2D(inChannels, outChannels, kernelSize, stride?, padding?, scale?)
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

        // Initialize weights: [inChannels, outChannels, kernelH, kernelW]
        var weightsList = new List<RuntimeValue>(_inChannels);
        for (var ic = 0; ic < _inChannels; ic++)
        {
            var outChannelsList = new List<RuntimeValue>(_outChannels);
            for (var oc = 0; oc < _outChannels; oc++)
            {
                outChannelsList.Add(NeuralLayers.Matrix(_kernelSize, _kernelSize, scale));
            }
            weightsList.Add(RuntimeValue.Array(outChannelsList));
        }
        _weights = RuntimeValue.Array(weightsList);

        // Initialize bias: [outChannels]
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

        // Input shape: [batch, channels, height, width] or [channels, height, width]
        var input = Read4DTensor("Conv2D.forward", args[0], "input");
        var batch = input.GetLength(0);
        var inC = input.GetLength(1);
        var inH = input.GetLength(2);
        var inW = input.GetLength(3);

        if (inC != _inChannels)
            throw new RuntimeException($"Conv2D.forward() expected {_inChannels} input channels, got {inC}");

        // Calculate padding
        int padH = 0, padW = 0;
        if (_padding == "same")
        {
            padH = (_kernelSize - 1) / 2;
            padW = (_kernelSize - 1) / 2;
        }

        // Calculate output dimensions
        var outH = (inH + 2 * padH - _kernelSize) / _stride + 1;
        var outW = (inW + 2 * padW - _kernelSize) / _stride + 1;

        // Read weights and bias
        var weights = Read4DWeights();
        var bias = NeuralLayers.ReadVector("Conv2D.forward", _bias, "bias");

        // Perform convolution
        var output = new double[batch, _outChannels, outH, outW];
        for (var b = 0; b < batch; b++)
        {
            for (var oc = 0; oc < _outChannels; oc++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var sum = bias[oc];
                        var ihStart = oh * _stride - padH;
                        var iwStart = ow * _stride - padW;

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
                                        sum += input[b, ic, ih, iw] * weights[ic, oc, kh, kw];
                                    }
                                }
                            }
                        }
                        output[b, oc, oh, ow] = sum;
                    }
                }
            }
        }

        _input = input;
        return To4DTensor(output);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Conv2D.backward() expects 1 argument: (upstream)");
        if (_input == null)
            throw new RuntimeException("Conv2D.backward() requires forward() first");

        var upstream = Read4DTensor("Conv2D.backward", args[0], "upstream");
        var batch = _input.GetLength(0);
        var inH = _input.GetLength(2);
        var inW = _input.GetLength(3);
        var outH = upstream.GetLength(2);
        var outW = upstream.GetLength(3);

        int padH = 0, padW = 0;
        if (_padding == "same")
        {
            padH = (_kernelSize - 1) / 2;
            padW = (_kernelSize - 1) / 2;
        }

        var weights = Read4DWeights();
        var dInput = new double[batch, _inChannels, inH, inW];
        var dWeights = new double[_inChannels, _outChannels, _kernelSize, _kernelSize];
        var dBias = new double[_outChannels];

        // Compute gradients
        for (var b = 0; b < batch; b++)
        {
            for (var oc = 0; oc < _outChannels; oc++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var grad = upstream[b, oc, oh, ow];
                        dBias[oc] += grad;

                        var ihStart = oh * _stride - padH;
                        var iwStart = ow * _stride - padW;

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
        return To4DTensor(dInput);
    }

    private RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Conv2D.sgd() expects 1 argument: (lr)");
        if (_dWeights == null || _dBias == null)
            throw new RuntimeException("Conv2D.sgd() requires backward() first");

        var lr = DenseInstance.RequireFinite("Conv2D.sgd", args[0], "lr");
        Apply4DWeights(_dWeights, lr);
        NeuralLayers.ApplyVector(_bias, _dBias, lr);
        return RuntimeValue.Null();
    }

    private double[,,,] Read4DTensor(string name, RuntimeValue value, string which)
    {
        // Accept either [batch, channels, height, width] or [channels, height, width]
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"{name} {which} must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException($"{name} {which} must be non-empty");

        // Check if it's 3D (add batch dimension) or 4D
        if (arr[0].Type == ValueType.Array && arr[0].AsArray().Count > 0 && 
            arr[0].AsArray()[0].Type == ValueType.Array && arr[0].AsArray()[0].AsArray().Count > 0)
        {
            // Could be 3D or 4D
            var first = arr[0].AsArray()[0].AsArray()[0];
            if (first.Type == ValueType.Array)
            {
                // 4D: [batch, channels, height, width]
                return Parse4D(arr);
            }
            else
            {
                // 3D: [channels, height, width] - add batch dimension
                var tensor3d = Parse3D(arr);
                var batch = 1;
                var channels = tensor3d.GetLength(0);
                var height = tensor3d.GetLength(1);
                var width = tensor3d.GetLength(2);
                var tensor4d = new double[batch, channels, height, width];
                for (var c = 0; c < channels; c++)
                    for (var h = 0; h < height; h++)
                        for (var w = 0; w < width; w++)
                            tensor4d[0, c, h, w] = tensor3d[c, h, w];
                return tensor4d;
            }
        }

        throw new RuntimeException($"{name} {which} must be a 3D or 4D tensor");
    }

    private double[,,,] Parse4D(List<RuntimeValue> arr)
    {
        var batch = arr.Count;
        var channels = arr[0].AsArray().Count;
        var height = arr[0].AsArray()[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray()[0].AsArray().Count;

        var tensor = new double[batch, channels, height, width];
        for (var b = 0; b < batch; b++)
        {
            var batchArr = arr[b].AsArray();
            for (var c = 0; c < channels; c++)
            {
                var channelArr = batchArr[c].AsArray();
                for (var h = 0; h < height; h++)
                {
                    var rowArr = channelArr[h].AsArray();
                    for (var w = 0; w < width; w++)
                    {
                        tensor[b, c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                    }
                }
            }
        }
        return tensor;
    }

    private double[,,] Parse3D(List<RuntimeValue> arr)
    {
        var channels = arr.Count;
        var height = arr[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray().Count;

        var tensor = new double[channels, height, width];
        for (var c = 0; c < channels; c++)
        {
            var channelArr = arr[c].AsArray();
            for (var h = 0; h < height; h++)
            {
                var rowArr = channelArr[h].AsArray();
                for (var w = 0; w < width; w++)
                {
                    tensor[c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                }
            }
        }
        return tensor;
    }

    private RuntimeValue To4DTensor(double[,,,] tensor)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);

        var result = new List<RuntimeValue>(batch);
        for (var b = 0; b < batch; b++)
        {
            var batchList = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                var channelList = new List<RuntimeValue>(height);
                for (var h = 0; h < height; h++)
                {
                    var rowList = new List<RuntimeValue>(width);
                    for (var w = 0; w < width; w++)
                    {
                        rowList.Add(RuntimeValue.Float(tensor[b, c, h, w]));
                    }
                    channelList.Add(RuntimeValue.Array(rowList));
                }
                batchList.Add(RuntimeValue.Array(channelList));
            }
            result.Add(RuntimeValue.Array(batchList));
        }

        // If batch size is 1, return without batch dimension
        return batch == 1 ? result[0] : RuntimeValue.Array(result);
    }

    private double[,,,] Read4DWeights()
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
                    {
                        weights[ic, oc, kh, kw] = kernel[kh][kw];
                    }
                }
            }
        }
        return weights;
    }

    private void Apply4DWeights(double[,,,] grad, double lr)
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

/// <summary>2D Max Pooling layer.</summary>
public sealed class MaxPool2DInstance : ObjectInstance
{
    private readonly int _poolSize;
    private readonly int _stride;
    private int[,,,]? _maxIndices;

    public MaxPool2DInstance(List<RuntimeValue> args) : base(null)
    {
        // MaxPool2D(poolSize, stride?)
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

        var (tensor, wasBatched) = ReadTensor(args[0]);
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var inH = tensor.GetLength(2);
        var inW = tensor.GetLength(3);

        var outH = (inH - _poolSize) / _stride + 1;
        var outW = (inW - _poolSize) / _stride + 1;

        var output = new double[batch, channels, outH, outW];
        _maxIndices = new int[batch, channels, outH, outW];

        for (var b = 0; b < batch; b++)
        {
            for (var c = 0; c < channels; c++)
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
                                var val = tensor[b, c, ihStart + ph, iwStart + pw];
                                if (val > maxVal)
                                {
                                    maxVal = val;
                                    maxIdx = ph * _poolSize + pw;
                                }
                            }
                        }

                        output[b, c, oh, ow] = maxVal;
                        _maxIndices[b, c, oh, ow] = maxIdx;
                    }
                }
            }
        }

        return ToTensor(output, wasBatched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new RuntimeException("MaxPool2D.backward() expects 2 arguments: (upstream, inputShape)");
        if (_maxIndices == null)
            throw new RuntimeException("MaxPool2D.backward() requires forward() first");

        var (upstream, wasBatched) = ReadTensor(args[0]);
        var inputShape = ReadShape(args[1]);
        var batch = upstream.GetLength(0);
        var channels = upstream.GetLength(1);
        var outH = upstream.GetLength(2);
        var outW = upstream.GetLength(3);
        var inH = inputShape[wasBatched ? 2 : 1];
        var inW = inputShape[wasBatched ? 3 : 2];

        var dInput = new double[batch, channels, inH, inW];

        for (var b = 0; b < batch; b++)
        {
            for (var c = 0; c < channels; c++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var grad = upstream[b, c, oh, ow];
                        var maxIdx = _maxIndices[b, c, oh, ow];
                        var ph = maxIdx / _poolSize;
                        var pw = maxIdx % _poolSize;
                        var ih = oh * _stride + ph;
                        var iw = ow * _stride + pw;
                        dInput[b, c, ih, iw] += grad;
                    }
                }
            }
        }

        return ToTensor(dInput, wasBatched);
    }

    private (double[,,,], bool) ReadTensor(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("MaxPool2D tensor must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException("MaxPool2D tensor must be non-empty");

        // Detect if it's 3D or 4D
        var wasBatched = Is4D(arr);
        if (!wasBatched)
        {
            // Add batch dimension
            var tensor3d = Parse3D(arr);
            var channels = tensor3d.GetLength(0);
            var height = tensor3d.GetLength(1);
            var width = tensor3d.GetLength(2);
            var tensor4d = new double[1, channels, height, width];
            for (var c = 0; c < channels; c++)
                for (var h = 0; h < height; h++)
                    for (var w = 0; w < width; w++)
                        tensor4d[0, c, h, w] = tensor3d[c, h, w];
            return (tensor4d, false);
        }
        else
        {
            return (Parse4D(arr), true);
        }
    }

    private RuntimeValue ToTensor(double[,,,] tensor, bool wasBatched)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);

        if (!wasBatched && batch == 1)
        {
            // Remove batch dimension
            var result = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                var channelList = new List<RuntimeValue>(height);
                for (var h = 0; h < height; h++)
                {
                    var rowList = new List<RuntimeValue>(width);
                    for (var w = 0; w < width; w++)
                    {
                        rowList.Add(RuntimeValue.Float(tensor[0, c, h, w]));
                    }
                    channelList.Add(RuntimeValue.Array(rowList));
                }
                result.Add(RuntimeValue.Array(channelList));
            }
            return RuntimeValue.Array(result);
        }
        else
        {
            var result = new List<RuntimeValue>(batch);
            for (var b = 0; b < batch; b++)
            {
                var batchList = new List<RuntimeValue>(channels);
                for (var c = 0; c < channels; c++)
                {
                    var channelList = new List<RuntimeValue>(height);
                    for (var h = 0; h < height; h++)
                    {
                        var rowList = new List<RuntimeValue>(width);
                        for (var w = 0; w < width; w++)
                        {
                            rowList.Add(RuntimeValue.Float(tensor[b, c, h, w]));
                        }
                        channelList.Add(RuntimeValue.Array(rowList));
                    }
                    batchList.Add(RuntimeValue.Array(channelList));
                }
                result.Add(RuntimeValue.Array(batchList));
            }
            return RuntimeValue.Array(result);
        }
    }

    private bool Is4D(List<RuntimeValue> arr)
    {
        if (arr[0].Type != ValueType.Array) return false;
        var level2 = arr[0].AsArray();
        if (level2.Count == 0 || level2[0].Type != ValueType.Array) return false;
        var level3 = level2[0].AsArray();
        if (level3.Count == 0 || level3[0].Type != ValueType.Array) return false;
        var level4 = level3[0].AsArray();
        return level4.Count > 0 && level4[0].Type == ValueType.Array;
    }

    private double[,,,] Parse4D(List<RuntimeValue> arr)
    {
        var batch = arr.Count;
        var channels = arr[0].AsArray().Count;
        var height = arr[0].AsArray()[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray()[0].AsArray().Count;

        var tensor = new double[batch, channels, height, width];
        for (var b = 0; b < batch; b++)
        {
            var batchArr = arr[b].AsArray();
            for (var c = 0; c < channels; c++)
            {
                var channelArr = batchArr[c].AsArray();
                for (var h = 0; h < height; h++)
                {
                    var rowArr = channelArr[h].AsArray();
                    for (var w = 0; w < width; w++)
                    {
                        tensor[b, c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                    }
                }
            }
        }
        return tensor;
    }

    private double[,,] Parse3D(List<RuntimeValue> arr)
    {
        var channels = arr.Count;
        var height = arr[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray().Count;

        var tensor = new double[channels, height, width];
        for (var c = 0; c < channels; c++)
        {
            var channelArr = arr[c].AsArray();
            for (var h = 0; h < height; h++)
            {
                var rowArr = channelArr[h].AsArray();
                for (var w = 0; w < width; w++)
                {
                    tensor[c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                }
            }
        }
        return tensor;
    }

    private int[] ReadShape(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("MaxPool2D.backward() inputShape must be an array");
        var arr = value.AsArray();
        var shape = new int[arr.Count];
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i].Type != ValueType.Integer)
                throw new RuntimeException("MaxPool2D.backward() inputShape must contain integers");
            shape[i] = arr[i].AsInteger();
        }
        return shape;
    }
}

/// <summary>2D Average Pooling layer.</summary>
public sealed class AvgPool2DInstance : ObjectInstance
{
    private readonly int _poolSize;
    private readonly int _stride;
    private (int, int, int, int)? _inputShape;

    public AvgPool2DInstance(List<RuntimeValue> args) : base(null)
    {
        // AvgPool2D(poolSize, stride?)
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

        var (tensor, wasBatched) = ReadTensor(args[0]);
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var inH = tensor.GetLength(2);
        var inW = tensor.GetLength(3);

        _inputShape = (batch, channels, inH, inW);

        var outH = (inH - _poolSize) / _stride + 1;
        var outW = (inW - _poolSize) / _stride + 1;

        var output = new double[batch, channels, outH, outW];
        var poolArea = _poolSize * _poolSize;

        for (var b = 0; b < batch; b++)
        {
            for (var c = 0; c < channels; c++)
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
                            {
                                sum += tensor[b, c, ihStart + ph, iwStart + pw];
                            }
                        }

                        output[b, c, oh, ow] = sum / poolArea;
                    }
                }
            }
        }

        return ToTensor(output, wasBatched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("AvgPool2D.backward() expects 1 argument: (upstream)");
        if (_inputShape == null)
            throw new RuntimeException("AvgPool2D.backward() requires forward() first");

        var (upstream, wasBatched) = ReadTensor(args[0]);
        var (batch, channels, inH, inW) = _inputShape.Value;
        var outH = upstream.GetLength(2);
        var outW = upstream.GetLength(3);

        var dInput = new double[batch, channels, inH, inW];
        var poolArea = _poolSize * _poolSize;

        for (var b = 0; b < batch; b++)
        {
            for (var c = 0; c < channels; c++)
            {
                for (var oh = 0; oh < outH; oh++)
                {
                    for (var ow = 0; ow < outW; ow++)
                    {
                        var grad = upstream[b, c, oh, ow] / poolArea;
                        var ihStart = oh * _stride;
                        var iwStart = ow * _stride;

                        for (var ph = 0; ph < _poolSize; ph++)
                        {
                            for (var pw = 0; pw < _poolSize; pw++)
                            {
                                dInput[b, c, ihStart + ph, iwStart + pw] += grad;
                            }
                        }
                    }
                }
            }
        }

        return ToTensor(dInput, wasBatched);
    }

    // Reuse helper methods from MaxPool2D
    private (double[,,,], bool) ReadTensor(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("AvgPool2D tensor must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException("AvgPool2D tensor must be non-empty");

        var wasBatched = Is4D(arr);
        if (!wasBatched)
        {
            var tensor3d = Parse3D(arr);
            var channels = tensor3d.GetLength(0);
            var height = tensor3d.GetLength(1);
            var width = tensor3d.GetLength(2);
            var tensor4d = new double[1, channels, height, width];
            for (var c = 0; c < channels; c++)
                for (var h = 0; h < height; h++)
                    for (var w = 0; w < width; w++)
                        tensor4d[0, c, h, w] = tensor3d[c, h, w];
            return (tensor4d, false);
        }
        else
        {
            return (Parse4D(arr), true);
        }
    }

    private RuntimeValue ToTensor(double[,,,] tensor, bool wasBatched)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);

        if (!wasBatched && batch == 1)
        {
            var result = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                var channelList = new List<RuntimeValue>(height);
                for (var h = 0; h < height; h++)
                {
                    var rowList = new List<RuntimeValue>(width);
                    for (var w = 0; w < width; w++)
                    {
                        rowList.Add(RuntimeValue.Float(tensor[0, c, h, w]));
                    }
                    channelList.Add(RuntimeValue.Array(rowList));
                }
                result.Add(RuntimeValue.Array(channelList));
            }
            return RuntimeValue.Array(result);
        }
        else
        {
            var result = new List<RuntimeValue>(batch);
            for (var b = 0; b < batch; b++)
            {
                var batchList = new List<RuntimeValue>(channels);
                for (var c = 0; c < channels; c++)
                {
                    var channelList = new List<RuntimeValue>(height);
                    for (var h = 0; h < height; h++)
                    {
                        var rowList = new List<RuntimeValue>(width);
                        for (var w = 0; w < width; w++)
                        {
                            rowList.Add(RuntimeValue.Float(tensor[b, c, h, w]));
                        }
                        channelList.Add(RuntimeValue.Array(rowList));
                    }
                    batchList.Add(RuntimeValue.Array(channelList));
                }
                result.Add(RuntimeValue.Array(batchList));
            }
            return RuntimeValue.Array(result);
        }
    }

    private bool Is4D(List<RuntimeValue> arr)
    {
        if (arr[0].Type != ValueType.Array) return false;
        var level2 = arr[0].AsArray();
        if (level2.Count == 0 || level2[0].Type != ValueType.Array) return false;
        var level3 = level2[0].AsArray();
        if (level3.Count == 0 || level3[0].Type != ValueType.Array) return false;
        var level4 = level3[0].AsArray();
        return level4.Count > 0 && level4[0].Type == ValueType.Array;
    }

    private double[,,,] Parse4D(List<RuntimeValue> arr)
    {
        var batch = arr.Count;
        var channels = arr[0].AsArray().Count;
        var height = arr[0].AsArray()[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray()[0].AsArray().Count;

        var tensor = new double[batch, channels, height, width];
        for (var b = 0; b < batch; b++)
        {
            var batchArr = arr[b].AsArray();
            for (var c = 0; c < channels; c++)
            {
                var channelArr = batchArr[c].AsArray();
                for (var h = 0; h < height; h++)
                {
                    var rowArr = channelArr[h].AsArray();
                    for (var w = 0; w < width; w++)
                    {
                        tensor[b, c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                    }
                }
            }
        }
        return tensor;
    }

    private double[,,] Parse3D(List<RuntimeValue> arr)
    {
        var channels = arr.Count;
        var height = arr[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray().Count;

        var tensor = new double[channels, height, width];
        for (var c = 0; c < channels; c++)
        {
            var channelArr = arr[c].AsArray();
            for (var h = 0; h < height; h++)
            {
                var rowArr = channelArr[h].AsArray();
                for (var w = 0; w < width; w++)
                {
                    tensor[c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                }
            }
        }
        return tensor;
    }
}

/// <summary>Flatten multi-dimensional input to 1D vector.</summary>
public sealed class FlattenInstance : ObjectInstance
{
    private int[]? _inputShape;

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

        var (flattened, shape) = FlattenTensor(args[0]);
        _inputShape = shape;
        return flattened;
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Flatten.backward() expects 1 argument: (upstream)");
        if (_inputShape == null)
            throw new RuntimeException("Flatten.backward() requires forward() first");

        return UnflattenTensor(args[0], _inputShape);
    }

    private (RuntimeValue, int[]) FlattenTensor(RuntimeValue value)
    {
        var shape = GetShape(value);
        var flat = new List<RuntimeValue>();
        FlattenRecursive(value, flat);
        return (RuntimeValue.Array(flat), shape);
    }

    private void FlattenRecursive(RuntimeValue value, List<RuntimeValue> result)
    {
        if (value.Type == ValueType.Array)
        {
            foreach (var item in value.AsArray())
            {
                FlattenRecursive(item, result);
            }
        }
        else
        {
            result.Add(value);
        }
    }

    private int[] GetShape(RuntimeValue value)
    {
        var shape = new List<int>();
        var current = value;
        while (current.Type == ValueType.Array && current.AsArray().Count > 0)
        {
            shape.Add(current.AsArray().Count);
            current = current.AsArray()[0];
        }
        return shape.ToArray();
    }

    private RuntimeValue UnflattenTensor(RuntimeValue flat, int[] shape)
    {
        if (flat.Type != ValueType.Array)
            throw new RuntimeException("Flatten.backward() upstream must be an array");

        var flatList = flat.AsArray();
        var index = 0;
        return UnflattenRecursive(flatList, shape, 0, ref index);
    }

    private RuntimeValue UnflattenRecursive(List<RuntimeValue> flatList, int[] shape, int depth, ref int index)
    {
        if (depth == shape.Length - 1)
        {
            var result = new List<RuntimeValue>(shape[depth]);
            for (var i = 0; i < shape[depth]; i++)
            {
                result.Add(flatList[index++]);
            }
            return RuntimeValue.Array(result);
        }
        else
        {
            var result = new List<RuntimeValue>(shape[depth]);
            for (var i = 0; i < shape[depth]; i++)
            {
                result.Add(UnflattenRecursive(flatList, shape, depth + 1, ref index));
            }
            return RuntimeValue.Array(result);
        }
    }
}

/// <summary>2D Batch Normalization layer.</summary>
public sealed class BatchNorm2DInstance : ObjectInstance
{
    private readonly RuntimeValue _gamma;
    private readonly RuntimeValue _beta;
    private readonly RuntimeValue _runningMean;
    private readonly RuntimeValue _runningVar;
    private readonly double _momentum;
    private readonly double _eps;
    private readonly int _numFeatures;
    private double[,,,]? _input;
    private double[,,,]? _normalized;
    private double[]? _std;
    private double[]? _dGamma;
    private double[]? _dBeta;
    private bool _training = true;

    public BatchNorm2DInstance(List<RuntimeValue> args) : base(null)
    {
        // BatchNorm2D(numFeatures, momentum?, eps?)
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
            "train" => Train(args),
            "eval" => Eval(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on BatchNorm2D.")
        };
    }

    private RuntimeValue Train(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new RuntimeException("BatchNorm2D.train() expects no arguments");
        _training = true;
        return RuntimeValue.Null();
    }

    private RuntimeValue Eval(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new RuntimeException("BatchNorm2D.eval() expects no arguments");
        _training = false;
        return RuntimeValue.Null();
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("BatchNorm2D.forward() expects 1 argument: (input)");

        var (input, wasBatched) = ReadTensor(args[0]);
        var batch = input.GetLength(0);
        var channels = input.GetLength(1);
        var height = input.GetLength(2);
        var width = input.GetLength(3);

        if (channels != _numFeatures)
            throw new RuntimeException($"BatchNorm2D.forward() expected {_numFeatures} channels, got {channels}");

        var gamma = NeuralLayers.ReadVector("BatchNorm2D.forward", _gamma, "gamma");
        var beta = NeuralLayers.ReadVector("BatchNorm2D.forward", _beta, "beta");
        var output = new double[batch, channels, height, width];

        if (_training)
        {
            // Calculate batch statistics
            var mean = new double[channels];
            var variance = new double[channels];
            var spatialSize = batch * height * width;

            for (var c = 0; c < channels; c++)
            {
                for (var b = 0; b < batch; b++)
                    for (var h = 0; h < height; h++)
                        for (var w = 0; w < width; w++)
                            mean[c] += input[b, c, h, w];
                mean[c] /= spatialSize;
            }

            for (var c = 0; c < channels; c++)
            {
                for (var b = 0; b < batch; b++)
                    for (var h = 0; h < height; h++)
                        for (var w = 0; w < width; w++)
                        {
                            var diff = input[b, c, h, w] - mean[c];
                            variance[c] += diff * diff;
                        }
                variance[c] /= spatialSize;
            }

            // Update running statistics
            var runningMean = NeuralLayers.ReadVector("BatchNorm2D", _runningMean, "runningMean");
            var runningVar = NeuralLayers.ReadVector("BatchNorm2D", _runningVar, "runningVar");
            for (var c = 0; c < channels; c++)
            {
                runningMean[c] = (1 - _momentum) * runningMean[c] + _momentum * mean[c];
                runningVar[c] = (1 - _momentum) * runningVar[c] + _momentum * variance[c];
            }
            UpdateVector(_runningMean, runningMean);
            UpdateVector(_runningVar, runningVar);

            // Normalize and transform
            _std = new double[channels];
            _normalized = new double[batch, channels, height, width];
            for (var c = 0; c < channels; c++)
            {
                _std[c] = Math.Sqrt(variance[c] + _eps);
                for (var b = 0; b < batch; b++)
                {
                    for (var h = 0; h < height; h++)
                    {
                        for (var w = 0; w < width; w++)
                        {
                            _normalized[b, c, h, w] = (input[b, c, h, w] - mean[c]) / _std[c];
                            output[b, c, h, w] = gamma[c] * _normalized[b, c, h, w] + beta[c];
                        }
                    }
                }
            }
            _input = input;
        }
        else
        {
            // Use running statistics
            var runningMean = NeuralLayers.ReadVector("BatchNorm2D", _runningMean, "runningMean");
            var runningVar = NeuralLayers.ReadVector("BatchNorm2D", _runningVar, "runningVar");
            for (var c = 0; c < channels; c++)
            {
                var std = Math.Sqrt(runningVar[c] + _eps);
                for (var b = 0; b < batch; b++)
                {
                    for (var h = 0; h < height; h++)
                    {
                        for (var w = 0; w < width; w++)
                        {
                            var normalized = (input[b, c, h, w] - runningMean[c]) / std;
                            output[b, c, h, w] = gamma[c] * normalized + beta[c];
                        }
                    }
                }
            }
        }

        return ToTensor(output, wasBatched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("BatchNorm2D.backward() expects 1 argument: (upstream)");
        if (_input == null || _normalized == null || _std == null)
            throw new RuntimeException("BatchNorm2D.backward() requires forward() first in training mode");

        var (upstream, wasBatched) = ReadTensor(args[0]);
        var batch = _input.GetLength(0);
        var channels = _input.GetLength(1);
        var height = _input.GetLength(2);
        var width = _input.GetLength(3);
        var spatialSize = batch * height * width;

        var gamma = NeuralLayers.ReadVector("BatchNorm2D", _gamma, "gamma");
        var dGamma = new double[channels];
        var dBeta = new double[channels];
        var dInput = new double[batch, channels, height, width];

        for (var c = 0; c < channels; c++)
        {
            for (var b = 0; b < batch; b++)
            {
                for (var h = 0; h < height; h++)
                {
                    for (var w = 0; w < width; w++)
                    {
                        var grad = upstream[b, c, h, w];
                        dBeta[c] += grad;
                        dGamma[c] += grad * _normalized[b, c, h, w];
                    }
                }
            }
        }

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
                        var dnorm = upstream[b, c, h, w] * gamma[c];
                        meanDnorm += dnorm;
                        meanDnormNorm += dnorm * _normalized[b, c, h, w];
                    }
                }
            }
            meanDnorm /= spatialSize;
            meanDnormNorm /= spatialSize;

            for (var b = 0; b < batch; b++)
            {
                for (var h = 0; h < height; h++)
                {
                    for (var w = 0; w < width; w++)
                    {
                        var dnorm = upstream[b, c, h, w] * gamma[c];
                        dInput[b, c, h, w] = (dnorm - meanDnorm - _normalized[b, c, h, w] * meanDnormNorm) / _std[c];
                    }
                }
            }
        }

        _dGamma = dGamma;
        _dBeta = dBeta;
        return ToTensor(dInput, wasBatched);
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

    private void UpdateVector(RuntimeValue values, double[] data)
    {
        var arr = values.AsArray();
        for (var i = 0; i < data.Length; i++)
            arr[i] = RuntimeValue.Float(data[i]);
    }

    private (double[,,,], bool) ReadTensor(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("BatchNorm2D tensor must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException("BatchNorm2D tensor must be non-empty");

        var wasBatched = Is4D(arr);
        if (!wasBatched)
        {
            var tensor3d = Parse3D(arr);
            var channels = tensor3d.GetLength(0);
            var height = tensor3d.GetLength(1);
            var width = tensor3d.GetLength(2);
            var tensor4d = new double[1, channels, height, width];
            for (var c = 0; c < channels; c++)
                for (var h = 0; h < height; h++)
                    for (var w = 0; w < width; w++)
                        tensor4d[0, c, h, w] = tensor3d[c, h, w];
            return (tensor4d, false);
        }
        else
        {
            return (Parse4D(arr), true);
        }
    }

    private RuntimeValue ToTensor(double[,,,] tensor, bool wasBatched)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);

        if (!wasBatched && batch == 1)
        {
            var result = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                var channelList = new List<RuntimeValue>(height);
                for (var h = 0; h < height; h++)
                {
                    var rowList = new List<RuntimeValue>(width);
                    for (var w = 0; w < width; w++)
                    {
                        rowList.Add(RuntimeValue.Float(tensor[0, c, h, w]));
                    }
                    channelList.Add(RuntimeValue.Array(rowList));
                }
                result.Add(RuntimeValue.Array(channelList));
            }
            return RuntimeValue.Array(result);
        }
        else
        {
            var result = new List<RuntimeValue>(batch);
            for (var b = 0; b < batch; b++)
            {
                var batchList = new List<RuntimeValue>(channels);
                for (var c = 0; c < channels; c++)
                {
                    var channelList = new List<RuntimeValue>(height);
                    for (var h = 0; h < height; h++)
                    {
                        var rowList = new List<RuntimeValue>(width);
                        for (var w = 0; w < width; w++)
                        {
                            rowList.Add(RuntimeValue.Float(tensor[b, c, h, w]));
                        }
                        channelList.Add(RuntimeValue.Array(rowList));
                    }
                    batchList.Add(RuntimeValue.Array(channelList));
                }
                result.Add(RuntimeValue.Array(batchList));
            }
            return RuntimeValue.Array(result);
        }
    }

    private bool Is4D(List<RuntimeValue> arr)
    {
        if (arr[0].Type != ValueType.Array) return false;
        var level2 = arr[0].AsArray();
        if (level2.Count == 0 || level2[0].Type != ValueType.Array) return false;
        var level3 = level2[0].AsArray();
        if (level3.Count == 0 || level3[0].Type != ValueType.Array) return false;
        var level4 = level3[0].AsArray();
        return level4.Count > 0 && level4[0].Type == ValueType.Array;
    }

    private double[,,,] Parse4D(List<RuntimeValue> arr)
    {
        var batch = arr.Count;
        var channels = arr[0].AsArray().Count;
        var height = arr[0].AsArray()[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray()[0].AsArray().Count;

        var tensor = new double[batch, channels, height, width];
        for (var b = 0; b < batch; b++)
        {
            var batchArr = arr[b].AsArray();
            for (var c = 0; c < channels; c++)
            {
                var channelArr = batchArr[c].AsArray();
                for (var h = 0; h < height; h++)
                {
                    var rowArr = channelArr[h].AsArray();
                    for (var w = 0; w < width; w++)
                    {
                        tensor[b, c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                    }
                }
            }
        }
        return tensor;
    }

    private double[,,] Parse3D(List<RuntimeValue> arr)
    {
        var channels = arr.Count;
        var height = arr[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray().Count;

        var tensor = new double[channels, height, width];
        for (var c = 0; c < channels; c++)
        {
            var channelArr = arr[c].AsArray();
            for (var h = 0; h < height; h++)
            {
                var rowArr = channelArr[h].AsArray();
                for (var w = 0; w < width; w++)
                {
                    tensor[c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                }
            }
        }
        return tensor;
    }
}

/// <summary>2D Dropout layer - randomly zeros elements during training.</summary>
public sealed class Dropout2DInstance : ObjectInstance
{
    private readonly double _p;
    private bool[,,,]? _mask;
    private bool _training = true;

    public Dropout2DInstance(List<RuntimeValue> args) : base(null)
    {
        // Dropout2D(p)
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
            "train" => Train(args),
            "eval" => Eval(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Dropout2D.")
        };
    }

    private RuntimeValue Train(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new RuntimeException("Dropout2D.train() expects no arguments");
        _training = true;
        return RuntimeValue.Null();
    }

    private RuntimeValue Eval(List<RuntimeValue> args)
    {
        if (args.Count != 0)
            throw new RuntimeException("Dropout2D.eval() expects no arguments");
        _training = false;
        return RuntimeValue.Null();
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Dropout2D.forward() expects 1 argument: (input)");

        var (input, wasBatched) = ReadTensor(args[0]);
        var batch = input.GetLength(0);
        var channels = input.GetLength(1);
        var height = input.GetLength(2);
        var width = input.GetLength(3);

        var output = new double[batch, channels, height, width];

        if (_training && _p > 0)
        {
            _mask = new bool[batch, channels, height, width];
            var scale = 1.0 / (1.0 - _p);

            for (var b = 0; b < batch; b++)
            {
                for (var c = 0; c < channels; c++)
                {
                    for (var h = 0; h < height; h++)
                    {
                        for (var w = 0; w < width; w++)
                        {
                            var rand = BuiltInFunctions.CallBuiltIn("randomFloat", new List<RuntimeValue> 
                            { 
                                RuntimeValue.Float(0.0), 
                                RuntimeValue.Float(1.0) 
                            }, null).AsFloat();
                            _mask[b, c, h, w] = rand >= _p;
                            output[b, c, h, w] = _mask[b, c, h, w] ? input[b, c, h, w] * scale : 0.0;
                        }
                    }
                }
            }
        }
        else
        {
            for (var b = 0; b < batch; b++)
                for (var c = 0; c < channels; c++)
                    for (var h = 0; h < height; h++)
                        for (var w = 0; w < width; w++)
                            output[b, c, h, w] = input[b, c, h, w];
        }

        return ToTensor(output, wasBatched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Dropout2D.backward() expects 1 argument: (upstream)");

        var (upstream, wasBatched) = ReadTensor(args[0]);
        var batch = upstream.GetLength(0);
        var channels = upstream.GetLength(1);
        var height = upstream.GetLength(2);
        var width = upstream.GetLength(3);

        var dInput = new double[batch, channels, height, width];

        if (_mask != null)
        {
            var scale = 1.0 / (1.0 - _p);
            for (var b = 0; b < batch; b++)
            {
                for (var c = 0; c < channels; c++)
                {
                    for (var h = 0; h < height; h++)
                    {
                        for (var w = 0; w < width; w++)
                        {
                            dInput[b, c, h, w] = _mask[b, c, h, w] ? upstream[b, c, h, w] * scale : 0.0;
                        }
                    }
                }
            }
        }
        else
        {
            for (var b = 0; b < batch; b++)
                for (var c = 0; c < channels; c++)
                    for (var h = 0; h < height; h++)
                        for (var w = 0; w < width; w++)
                            dInput[b, c, h, w] = upstream[b, c, h, w];
        }

        return ToTensor(dInput, wasBatched);
    }

    private (double[,,,], bool) ReadTensor(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("Dropout2D tensor must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException("Dropout2D tensor must be non-empty");

        var wasBatched = Is4D(arr);
        if (!wasBatched)
        {
            var tensor3d = Parse3D(arr);
            var channels = tensor3d.GetLength(0);
            var height = tensor3d.GetLength(1);
            var width = tensor3d.GetLength(2);
            var tensor4d = new double[1, channels, height, width];
            for (var c = 0; c < channels; c++)
                for (var h = 0; h < height; h++)
                    for (var w = 0; w < width; w++)
                        tensor4d[0, c, h, w] = tensor3d[c, h, w];
            return (tensor4d, false);
        }
        else
        {
            return (Parse4D(arr), true);
        }
    }

    private RuntimeValue ToTensor(double[,,,] tensor, bool wasBatched)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);

        if (!wasBatched && batch == 1)
        {
            var result = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                var channelList = new List<RuntimeValue>(height);
                for (var h = 0; h < height; h++)
                {
                    var rowList = new List<RuntimeValue>(width);
                    for (var w = 0; w < width; w++)
                    {
                        rowList.Add(RuntimeValue.Float(tensor[0, c, h, w]));
                    }
                    channelList.Add(RuntimeValue.Array(rowList));
                }
                result.Add(RuntimeValue.Array(channelList));
            }
            return RuntimeValue.Array(result);
        }
        else
        {
            var result = new List<RuntimeValue>(batch);
            for (var b = 0; b < batch; b++)
            {
                var batchList = new List<RuntimeValue>(channels);
                for (var c = 0; c < channels; c++)
                {
                    var channelList = new List<RuntimeValue>(height);
                    for (var h = 0; h < height; h++)
                    {
                        var rowList = new List<RuntimeValue>(width);
                        for (var w = 0; w < width; w++)
                        {
                            rowList.Add(RuntimeValue.Float(tensor[b, c, h, w]));
                        }
                        channelList.Add(RuntimeValue.Array(rowList));
                    }
                    batchList.Add(RuntimeValue.Array(channelList));
                }
                result.Add(RuntimeValue.Array(batchList));
            }
            return RuntimeValue.Array(result);
        }
    }

    private bool Is4D(List<RuntimeValue> arr)
    {
        if (arr[0].Type != ValueType.Array) return false;
        var level2 = arr[0].AsArray();
        if (level2.Count == 0 || level2[0].Type != ValueType.Array) return false;
        var level3 = level2[0].AsArray();
        if (level3.Count == 0 || level3[0].Type != ValueType.Array) return false;
        var level4 = level3[0].AsArray();
        return level4.Count > 0 && level4[0].Type == ValueType.Array;
    }

    private double[,,,] Parse4D(List<RuntimeValue> arr)
    {
        var batch = arr.Count;
        var channels = arr[0].AsArray().Count;
        var height = arr[0].AsArray()[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray()[0].AsArray().Count;

        var tensor = new double[batch, channels, height, width];
        for (var b = 0; b < batch; b++)
        {
            var batchArr = arr[b].AsArray();
            for (var c = 0; c < channels; c++)
            {
                var channelArr = batchArr[c].AsArray();
                for (var h = 0; h < height; h++)
                {
                    var rowArr = channelArr[h].AsArray();
                    for (var w = 0; w < width; w++)
                    {
                        tensor[b, c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                    }
                }
            }
        }
        return tensor;
    }

    private double[,,] Parse3D(List<RuntimeValue> arr)
    {
        var channels = arr.Count;
        var height = arr[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray().Count;

        var tensor = new double[channels, height, width];
        for (var c = 0; c < channels; c++)
        {
            var channelArr = arr[c].AsArray();
            for (var h = 0; h < height; h++)
            {
                var rowArr = channelArr[h].AsArray();
                for (var w = 0; w < width; w++)
                {
                    tensor[c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                }
            }
        }
        return tensor;
    }
}

/// <summary>Global Average Pooling 2D - averages each channel to a single value.</summary>
public sealed class GlobalAvgPool2DInstance : ObjectInstance
{
    private (int, int, int, int)? _inputShape;

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

        var (input, wasBatched) = ReadTensor(args[0]);
        var batch = input.GetLength(0);
        var channels = input.GetLength(1);
        var height = input.GetLength(2);
        var width = input.GetLength(3);

        _inputShape = (batch, channels, height, width);

        var output = new double[batch, channels];
        var spatialSize = height * width;

        for (var b = 0; b < batch; b++)
        {
            for (var c = 0; c < channels; c++)
            {
                var sum = 0.0;
                for (var h = 0; h < height; h++)
                {
                    for (var w = 0; w < width; w++)
                    {
                        sum += input[b, c, h, w];
                    }
                }
                output[b, c] = sum / spatialSize;
            }
        }

        return To2DTensor(output, wasBatched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("GlobalAvgPool2D.backward() expects 1 argument: (upstream)");
        if (_inputShape == null)
            throw new RuntimeException("GlobalAvgPool2D.backward() requires forward() first");

        var upstream = Read2DTensor(args[0]);
        var (batch, channels, height, width) = _inputShape.Value;
        var spatialSize = height * width;

        var dInput = new double[batch, channels, height, width];
        var grad = 1.0 / spatialSize;

        for (var b = 0; b < batch; b++)
        {
            for (var c = 0; c < channels; c++)
            {
                var upstreamGrad = upstream[b, c] * grad;
                for (var h = 0; h < height; h++)
                {
                    for (var w = 0; w < width; w++)
                    {
                        dInput[b, c, h, w] = upstreamGrad;
                    }
                }
            }
        }

        return To4DTensor(dInput);
    }

    private (double[,,,], bool) ReadTensor(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("GlobalAvgPool2D tensor must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException("GlobalAvgPool2D tensor must be non-empty");

        var wasBatched = Is4D(arr);
        if (!wasBatched)
        {
            var tensor3d = Parse3D(arr);
            var channels = tensor3d.GetLength(0);
            var height = tensor3d.GetLength(1);
            var width = tensor3d.GetLength(2);
            var tensor4d = new double[1, channels, height, width];
            for (var c = 0; c < channels; c++)
                for (var h = 0; h < height; h++)
                    for (var w = 0; w < width; w++)
                        tensor4d[0, c, h, w] = tensor3d[c, h, w];
            return (tensor4d, false);
        }
        else
        {
            return (Parse4D(arr), true);
        }
    }

    private double[,] Read2DTensor(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("GlobalAvgPool2D upstream must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException("GlobalAvgPool2D upstream must be non-empty");

        // Check if it's 1D (single batch) or 2D (batched)
        if (arr[0].Type == ValueType.Array)
        {
            // 2D
            var batch = arr.Count;
            var channels = arr[0].AsArray().Count;
            var tensor = new double[batch, channels];
            for (var b = 0; b < batch; b++)
            {
                var channelArr = arr[b].AsArray();
                for (var c = 0; c < channels; c++)
                {
                    tensor[b, c] = DenseInstance.AsNumber(channelArr[c]);
                }
            }
            return tensor;
        }
        else
        {
            // 1D - add batch dimension
            var channels = arr.Count;
            var tensor = new double[1, channels];
            for (var c = 0; c < channels; c++)
            {
                tensor[0, c] = DenseInstance.AsNumber(arr[c]);
            }
            return tensor;
        }
    }

    private RuntimeValue To2DTensor(double[,] tensor, bool wasBatched)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);

        if (!wasBatched && batch == 1)
        {
            // Return 1D array
            var result = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                result.Add(RuntimeValue.Float(tensor[0, c]));
            }
            return RuntimeValue.Array(result);
        }
        else
        {
            // Return 2D array
            var result = new List<RuntimeValue>(batch);
            for (var b = 0; b < batch; b++)
            {
                var batchList = new List<RuntimeValue>(channels);
                for (var c = 0; c < channels; c++)
                {
                    batchList.Add(RuntimeValue.Float(tensor[b, c]));
                }
                result.Add(RuntimeValue.Array(batchList));
            }
            return RuntimeValue.Array(result);
        }
    }

    private RuntimeValue To4DTensor(double[,,,] tensor)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);

        var result = new List<RuntimeValue>(batch);
        for (var b = 0; b < batch; b++)
        {
            var batchList = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                var channelList = new List<RuntimeValue>(height);
                for (var h = 0; h < height; h++)
                {
                    var rowList = new List<RuntimeValue>(width);
                    for (var w = 0; w < width; w++)
                    {
                        rowList.Add(RuntimeValue.Float(tensor[b, c, h, w]));
                    }
                    channelList.Add(RuntimeValue.Array(rowList));
                }
                batchList.Add(RuntimeValue.Array(channelList));
            }
            result.Add(RuntimeValue.Array(batchList));
        }

        return batch == 1 ? result[0] : RuntimeValue.Array(result);
    }

    private bool Is4D(List<RuntimeValue> arr)
    {
        if (arr[0].Type != ValueType.Array) return false;
        var level2 = arr[0].AsArray();
        if (level2.Count == 0 || level2[0].Type != ValueType.Array) return false;
        var level3 = level2[0].AsArray();
        if (level3.Count == 0 || level3[0].Type != ValueType.Array) return false;
        var level4 = level3[0].AsArray();
        return level4.Count > 0 && level4[0].Type == ValueType.Array;
    }

    private double[,,,] Parse4D(List<RuntimeValue> arr)
    {
        var batch = arr.Count;
        var channels = arr[0].AsArray().Count;
        var height = arr[0].AsArray()[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray()[0].AsArray().Count;

        var tensor = new double[batch, channels, height, width];
        for (var b = 0; b < batch; b++)
        {
            var batchArr = arr[b].AsArray();
            for (var c = 0; c < channels; c++)
            {
                var channelArr = batchArr[c].AsArray();
                for (var h = 0; h < height; h++)
                {
                    var rowArr = channelArr[h].AsArray();
                    for (var w = 0; w < width; w++)
                    {
                        tensor[b, c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                    }
                }
            }
        }
        return tensor;
    }

    private double[,,] Parse3D(List<RuntimeValue> arr)
    {
        var channels = arr.Count;
        var height = arr[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray().Count;

        var tensor = new double[channels, height, width];
        for (var c = 0; c < channels; c++)
        {
            var channelArr = arr[c].AsArray();
            for (var h = 0; h < height; h++)
            {
                var rowArr = channelArr[h].AsArray();
                for (var w = 0; w < width; w++)
                {
                    tensor[c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                }
            }
        }
        return tensor;
    }
}

/// <summary>Global Max Pooling 2D - takes max value from each channel.</summary>
public sealed class GlobalMaxPool2DInstance : ObjectInstance
{
    private (int, int, int, int)? _inputShape;
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

        var (input, wasBatched) = ReadTensor(args[0]);
        var batch = input.GetLength(0);
        var channels = input.GetLength(1);
        var height = input.GetLength(2);
        var width = input.GetLength(3);

        _inputShape = (batch, channels, height, width);
        _maxIndices = new int[batch, channels];

        var output = new double[batch, channels];

        for (var b = 0; b < batch; b++)
        {
            for (var c = 0; c < channels; c++)
            {
                var maxVal = double.NegativeInfinity;
                var maxIdx = 0;
                for (var h = 0; h < height; h++)
                {
                    for (var w = 0; w < width; w++)
                    {
                        var val = input[b, c, h, w];
                        if (val > maxVal)
                        {
                            maxVal = val;
                            maxIdx = h * width + w;
                        }
                    }
                }
                output[b, c] = maxVal;
                _maxIndices[b, c] = maxIdx;
            }
        }

        return To2DTensor(output, wasBatched);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("GlobalMaxPool2D.backward() expects 1 argument: (upstream)");
        if (_inputShape == null || _maxIndices == null)
            throw new RuntimeException("GlobalMaxPool2D.backward() requires forward() first");

        var upstream = Read2DTensor(args[0]);
        var (batch, channels, height, width) = _inputShape.Value;

        var dInput = new double[batch, channels, height, width];

        for (var b = 0; b < batch; b++)
        {
            for (var c = 0; c < channels; c++)
            {
                var idx = _maxIndices[b, c];
                var h = idx / width;
                var w = idx % width;
                dInput[b, c, h, w] = upstream[b, c];
            }
        }

        return To4DTensor(dInput);
    }

    // Reuse helper methods from GlobalAvgPool2D
    private (double[,,,], bool) ReadTensor(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("GlobalMaxPool2D tensor must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException("GlobalMaxPool2D tensor must be non-empty");

        var wasBatched = Is4D(arr);
        if (!wasBatched)
        {
            var tensor3d = Parse3D(arr);
            var channels = tensor3d.GetLength(0);
            var height = tensor3d.GetLength(1);
            var width = tensor3d.GetLength(2);
            var tensor4d = new double[1, channels, height, width];
            for (var c = 0; c < channels; c++)
                for (var h = 0; h < height; h++)
                    for (var w = 0; w < width; w++)
                        tensor4d[0, c, h, w] = tensor3d[c, h, w];
            return (tensor4d, false);
        }
        else
        {
            return (Parse4D(arr), true);
        }
    }

    private double[,] Read2DTensor(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException("GlobalMaxPool2D upstream must be an array");

        var arr = value.AsArray();
        if (arr.Count == 0)
            throw new RuntimeException("GlobalMaxPool2D upstream must be non-empty");

        if (arr[0].Type == ValueType.Array)
        {
            var batch = arr.Count;
            var channels = arr[0].AsArray().Count;
            var tensor = new double[batch, channels];
            for (var b = 0; b < batch; b++)
            {
                var channelArr = arr[b].AsArray();
                for (var c = 0; c < channels; c++)
                {
                    tensor[b, c] = DenseInstance.AsNumber(channelArr[c]);
                }
            }
            return tensor;
        }
        else
        {
            var channels = arr.Count;
            var tensor = new double[1, channels];
            for (var c = 0; c < channels; c++)
            {
                tensor[0, c] = DenseInstance.AsNumber(arr[c]);
            }
            return tensor;
        }
    }

    private RuntimeValue To2DTensor(double[,] tensor, bool wasBatched)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);

        if (!wasBatched && batch == 1)
        {
            var result = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                result.Add(RuntimeValue.Float(tensor[0, c]));
            }
            return RuntimeValue.Array(result);
        }
        else
        {
            var result = new List<RuntimeValue>(batch);
            for (var b = 0; b < batch; b++)
            {
                var batchList = new List<RuntimeValue>(channels);
                for (var c = 0; c < channels; c++)
                {
                    batchList.Add(RuntimeValue.Float(tensor[b, c]));
                }
                result.Add(RuntimeValue.Array(batchList));
            }
            return RuntimeValue.Array(result);
        }
    }

    private RuntimeValue To4DTensor(double[,,,] tensor)
    {
        var batch = tensor.GetLength(0);
        var channels = tensor.GetLength(1);
        var height = tensor.GetLength(2);
        var width = tensor.GetLength(3);

        var result = new List<RuntimeValue>(batch);
        for (var b = 0; b < batch; b++)
        {
            var batchList = new List<RuntimeValue>(channels);
            for (var c = 0; c < channels; c++)
            {
                var channelList = new List<RuntimeValue>(height);
                for (var h = 0; h < height; h++)
                {
                    var rowList = new List<RuntimeValue>(width);
                    for (var w = 0; w < width; w++)
                    {
                        rowList.Add(RuntimeValue.Float(tensor[b, c, h, w]));
                    }
                    channelList.Add(RuntimeValue.Array(rowList));
                }
                batchList.Add(RuntimeValue.Array(channelList));
            }
            result.Add(RuntimeValue.Array(batchList));
        }

        return batch == 1 ? result[0] : RuntimeValue.Array(result);
    }

    private bool Is4D(List<RuntimeValue> arr)
    {
        if (arr[0].Type != ValueType.Array) return false;
        var level2 = arr[0].AsArray();
        if (level2.Count == 0 || level2[0].Type != ValueType.Array) return false;
        var level3 = level2[0].AsArray();
        if (level3.Count == 0 || level3[0].Type != ValueType.Array) return false;
        var level4 = level3[0].AsArray();
        return level4.Count > 0 && level4[0].Type == ValueType.Array;
    }

    private double[,,,] Parse4D(List<RuntimeValue> arr)
    {
        var batch = arr.Count;
        var channels = arr[0].AsArray().Count;
        var height = arr[0].AsArray()[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray()[0].AsArray().Count;

        var tensor = new double[batch, channels, height, width];
        for (var b = 0; b < batch; b++)
        {
            var batchArr = arr[b].AsArray();
            for (var c = 0; c < channels; c++)
            {
                var channelArr = batchArr[c].AsArray();
                for (var h = 0; h < height; h++)
                {
                    var rowArr = channelArr[h].AsArray();
                    for (var w = 0; w < width; w++)
                    {
                        tensor[b, c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                    }
                }
            }
        }
        return tensor;
    }

    private double[,,] Parse3D(List<RuntimeValue> arr)
    {
        var channels = arr.Count;
        var height = arr[0].AsArray().Count;
        var width = arr[0].AsArray()[0].AsArray().Count;

        var tensor = new double[channels, height, width];
        for (var c = 0; c < channels; c++)
        {
            var channelArr = arr[c].AsArray();
            for (var h = 0; h < height; h++)
            {
                var rowArr = channelArr[h].AsArray();
                for (var w = 0; w < width; w++)
                {
                    tensor[c, h, w] = DenseInstance.AsNumber(rowArr[w]);
                }
            }
        }
        return tensor;
    }
}
