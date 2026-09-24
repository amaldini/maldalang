// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Shared array helpers for the neural layer classes. Each class keeps its own
/// parameters and the last forward. <c>sgd</c> applies the stored gradients.
/// There is no tape.
/// </summary>
internal static class NeuralLayers
{
    internal const double LayerNormEps = 0.00001;
    internal const double AttentionMaskFill = -1.0e9;

    internal static RuntimeValue Method(ObjectInstance owner, string name)
    {
        var wrapper = new FunctionValue(null, null, false, null)
        {
            BuiltInInstance = owner,
            BuiltInMethod = name
        };
        return RuntimeValue.Function(wrapper);
    }

    internal static RuntimeValue Matrix(int rows, int cols, double scale)
    {
        var matrix = new List<RuntimeValue>(rows);
        for (var i = 0; i < rows; i++)
            matrix.Add(Vector(cols, scale));
        return RuntimeValue.Array(matrix);
    }

    internal static RuntimeValue Vector(int length, double scale)
    {
        var row = new List<RuntimeValue>(length);
        for (var i = 0; i < length; i++)
        {
            row.Add(BuiltInFunctions.CallBuiltIn("randomFloat", new List<RuntimeValue>
            {
                RuntimeValue.Float(-scale),
                RuntimeValue.Float(scale)
            }, null));
        }

        return RuntimeValue.Array(row);
    }

    internal static RuntimeValue Filled(int length, double value)
    {
        var row = new List<RuntimeValue>(length);
        for (var i = 0; i < length; i++)
            row.Add(RuntimeValue.Float(value));
        return RuntimeValue.Array(row);
    }

    internal static double[] ReadVector(string name, RuntimeValue value, string which)
    {
        if (value.Type != ValueType.Array || DenseInstance.IsMatrix(value))
            throw new RuntimeException($"{name}() {which} must be a numeric vector");
        var items = value.AsArray();
        if (items.Count == 0)
            throw new RuntimeException($"{name}() {which} must be a non-empty vector");
        var row = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
            row[i] = DenseInstance.AsNumber(items[i]);
        return row;
    }

    internal static double[][] ReadMatrix(string name, RuntimeValue value, string which)
    {
        if (!DenseInstance.IsMatrix(value))
            throw new RuntimeException($"{name}() {which} must be a numeric matrix");
        var rows = value.AsArray();
        var width = -1;
        var matrix = new double[rows.Count][];
        for (var r = 0; r < rows.Count; r++)
        {
            var row = ReadVector(name, rows[r], which);
            if (width < 0)
                width = row.Length;
            else if (row.Length != width)
                throw new RuntimeException($"{name}() {which} rows must have the same length");
            matrix[r] = row;
        }

        return matrix;
    }

    internal static RuntimeValue ToVector(double[] row)
    {
        var list = new List<RuntimeValue>(row.Length);
        foreach (var value in row)
            list.Add(RuntimeValue.Float(value));
        return RuntimeValue.Array(list);
    }

    internal static RuntimeValue ToMatrix(double[][] rows)
    {
        var list = new List<RuntimeValue>(rows.Length);
        foreach (var row in rows)
            list.Add(ToVector(row));
        return RuntimeValue.Array(list);
    }

    internal static void ApplyVector(RuntimeValue values, double[] grad, double learningRate)
    {
        var row = values.AsArray();
        for (var i = 0; i < row.Count; i++)
            row[i] = RuntimeValue.Float(DenseInstance.AsNumber(row[i]) - learningRate * grad[i]);
    }

    internal static void ApplyMatrix(RuntimeValue values, double[][] grad, double learningRate)
    {
        var rows = values.AsArray();
        for (var i = 0; i < rows.Count; i++)
            ApplyVector(rows[i], grad[i], learningRate);
    }

    internal static double[][] Zeros(int rows, int cols)
    {
        var matrix = new double[rows][];
        for (var i = 0; i < rows; i++)
            matrix[i] = new double[cols];
        return matrix;
    }

    internal static double[][] Matmul(double[][] left, double[][] right)
    {
        var rows = left.Length;
        var inner = left[0].Length;
        var cols = right[0].Length;
        var result = Zeros(rows, cols);
        for (var i = 0; i < rows; i++)
        {
            for (var k = 0; k < inner; k++)
            {
                var value = left[i][k];
                for (var j = 0; j < cols; j++)
                    result[i][j] += value * right[k][j];
            }
        }

        return result;
    }

    internal static double[][] Transpose(double[][] matrix)
    {
        var rows = matrix.Length;
        var cols = matrix[0].Length;
        var result = Zeros(cols, rows);
        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < cols; j++)
                result[j][i] = matrix[i][j];
        }

        return result;
    }

    internal static double[] Activate(string activation, double[] pre)
    {
        if (activation == "linear")
            return (double[])pre.Clone();
        return ReadVector("Rnn", NnStdLib.Call(activation, new List<RuntimeValue> { ToVector(pre) }), "activation");
    }

    internal static double[] ActivateDerivative(string activation, double[] pre)
    {
        if (activation == "linear")
        {
            var ones = new double[pre.Length];
            for (var i = 0; i < ones.Length; i++)
                ones[i] = 1.0;
            return ones;
        }

        var name = "d" + char.ToUpperInvariant(activation[0]) + activation[1..];
        return ReadVector("Rnn", NnStdLib.Call(name, new List<RuntimeValue> { ToVector(pre) }), "derivative");
    }

    internal static string RequireActivation(string owner, RuntimeValue value)
    {
        if (value.Type != ValueType.String)
            throw new RuntimeException($"{owner}() activation must be a string");
        var activation = value.AsString();
        if (activation is not ("relu" or "leakyRelu" or "elu" or "gelu" or "silu" or "softplus" or "sigmoid" or "tanh" or "linear"))
            throw new RuntimeException($"{owner}() unknown activation '{activation}'");
        return activation;
    }

    internal static double[] Softmax(double[] logits)
    {
        return ReadVector("Attention", BuiltInFunctions.CallBuiltIn("softmax", new List<RuntimeValue> { ToVector(logits) }, null), "softmax");
    }
}

/// <summary>One shared square kernel. Valid convolution, one channel, no bias.</summary>
public sealed class ConvInstance : ObjectInstance
{
    private readonly RuntimeValue _kernel;
    private double[][]? _image;
    private double[][]? _dKernel;

    public ConvInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new RuntimeException("Conv() expects 1 or 2 arguments: (size, scale?)");
        Size = DenseInstance.RequirePositiveInt("Conv", args[0], "size");
        var scale = args.Count == 2 ? DenseInstance.RequireFinite("Conv", args[1], "scale") : 1.0 / Size;
        if (scale < 0)
            throw new RuntimeException("Conv() scale must be >= 0");
        _kernel = NeuralLayers.Matrix(Size, Size, scale);
    }

    public int Size { get; }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "size")
            return RuntimeValue.Integer(Size);
        if (name == "kernel")
            return _kernel;
        if (name is "forward" or "backward" or "sgd")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Conv.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Conv.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Conv.forward() expects 1 argument: (image)");
        var image = NeuralLayers.ReadMatrix("Conv.forward", args[0], "image");
        if (image.Length < Size || image[0].Length < Size)
            throw new RuntimeException("Conv.forward() image must be at least size x size");
        var kernel = NeuralLayers.ReadMatrix("Conv.forward", _kernel, "kernel");
        var outRows = image.Length - Size + 1;
        var outCols = image[0].Length - Size + 1;
        var output = NeuralLayers.Zeros(outRows, outCols);
        for (var oy = 0; oy < outRows; oy++)
        {
            for (var ox = 0; ox < outCols; ox++)
            {
                var sum = 0.0;
                for (var ky = 0; ky < Size; ky++)
                {
                    for (var kx = 0; kx < Size; kx++)
                        sum += image[oy + ky][ox + kx] * kernel[ky][kx];
                }

                output[oy][ox] = sum;
            }
        }

        _image = image;
        return NeuralLayers.ToMatrix(output);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Conv.backward() expects 1 argument: (upstream)");
        if (_image == null)
            throw new RuntimeException("Conv.backward() requires forward() first");
        var upstream = NeuralLayers.ReadMatrix("Conv.backward", args[0], "upstream");
        var outRows = _image.Length - Size + 1;
        var outCols = _image[0].Length - Size + 1;
        if (upstream.Length != outRows || upstream[0].Length != outCols)
            throw new RuntimeException("Conv.backward() upstream must match the forward output");
        var kernel = NeuralLayers.ReadMatrix("Conv.backward", _kernel, "kernel");
        var dKernel = NeuralLayers.Zeros(Size, Size);
        var dImage = NeuralLayers.Zeros(_image.Length, _image[0].Length);
        for (var oy = 0; oy < outRows; oy++)
        {
            for (var ox = 0; ox < outCols; ox++)
            {
                var dOut = upstream[oy][ox];
                for (var ky = 0; ky < Size; ky++)
                {
                    for (var kx = 0; kx < Size; kx++)
                    {
                        dKernel[ky][kx] += dOut * _image[oy + ky][ox + kx];
                        dImage[oy + ky][ox + kx] += dOut * kernel[ky][kx];
                    }
                }
            }
        }

        _dKernel = dKernel;
        return NeuralLayers.ToMatrix(dImage);
    }

    private RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Conv.sgd() expects 1 argument: (lr)");
        if (_dKernel == null)
            throw new RuntimeException("Conv.sgd() requires backward() first");
        NeuralLayers.ApplyMatrix(_kernel, _dKernel, DenseInstance.RequireFinite("Conv.sgd", args[0], "lr"));
        return RuntimeValue.Null();
    }
}

/// <summary>A table lookup. The gradient writes into the selected row.</summary>
public sealed class EmbeddingInstance : ObjectInstance
{
    private readonly RuntimeValue _table;
    private int _index = -1;
    private double[]? _dRow;

    public EmbeddingInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 2 || args.Count > 3)
            throw new RuntimeException("Embedding() expects 2 or 3 arguments: (rows, dim, scale?)");
        Rows = DenseInstance.RequirePositiveInt("Embedding", args[0], "rows");
        Dim = DenseInstance.RequirePositiveInt("Embedding", args[1], "dim");
        var scale = args.Count == 3 ? DenseInstance.RequireFinite("Embedding", args[2], "scale") : 1.0 / Math.Sqrt(Dim);
        if (scale < 0)
            throw new RuntimeException("Embedding() scale must be >= 0");
        _table = NeuralLayers.Matrix(Rows, Dim, scale);
    }

    public int Rows { get; }

    public int Dim { get; }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "rows")
            return RuntimeValue.Integer(Rows);
        if (name == "dim")
            return RuntimeValue.Integer(Dim);
        if (name == "table")
            return _table;
        if (name is "forward" or "backward" or "sgd")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Embedding.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Embedding.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Embedding.forward() expects 1 argument: (index)");
        if (args[0].Type != ValueType.Integer)
            throw new RuntimeException("Embedding.forward() index must be an integer");
        var index = args[0].AsInteger();
        if (index < 0 || index >= Rows)
            throw new RuntimeException("Embedding.forward() index out of range");
        var row = _table.AsArray()[index].AsArray();
        var copy = new double[row.Count];
        for (var i = 0; i < row.Count; i++)
            copy[i] = DenseInstance.AsNumber(row[i]);
        _index = index;
        return NeuralLayers.ToVector(copy);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Embedding.backward() expects 1 argument: (upstream)");
        if (_index < 0)
            throw new RuntimeException("Embedding.backward() requires forward() first");
        var upstream = NeuralLayers.ReadVector("Embedding.backward", args[0], "upstream");
        if (upstream.Length != Dim)
            throw new RuntimeException("Embedding.backward() upstream length must match dim");
        _dRow = upstream;
        return RuntimeValue.Null();
    }

    private RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Embedding.sgd() expects 1 argument: (lr)");
        if (_dRow == null || _index < 0)
            throw new RuntimeException("Embedding.sgd() requires backward() first");
        var learningRate = DenseInstance.RequireFinite("Embedding.sgd", args[0], "lr");
        NeuralLayers.ApplyVector(_table.AsArray()[_index], _dRow, learningRate);
        return RuntimeValue.Null();
    }
}

/// <summary>
/// One recurrent step unrolled over a sequence. Hidden state starts at zero
/// on each <c>forward</c>. <c>backward</c> walks the sequence from the end.
/// </summary>
public sealed class RnnInstance : ObjectInstance
{
    private readonly RuntimeValue _weightsXh;
    private readonly RuntimeValue _weightsHh;
    private readonly RuntimeValue _bias;
    private List<RnnStep>? _steps;
    private double[][]? _dXh;
    private double[][]? _dHh;
    private double[]? _dBias;

    public RnnInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 2 || args.Count > 4)
            throw new RuntimeException("Rnn() expects 2 to 4 arguments: (inputSize, hiddenSize, activation?, scale?)");
        InputSize = DenseInstance.RequirePositiveInt("Rnn", args[0], "inputSize");
        HiddenSize = DenseInstance.RequirePositiveInt("Rnn", args[1], "hiddenSize");
        Activation = "tanh";
        double? scale = null;
        if (args.Count >= 3)
            Activation = NeuralLayers.RequireActivation("Rnn", args[2]);
        if (args.Count == 4)
            scale = DenseInstance.RequireFinite("Rnn", args[3], "scale");
        var width = scale ?? 1.0 / Math.Sqrt(InputSize);
        if (width < 0)
            throw new RuntimeException("Rnn() scale must be >= 0");
        _weightsXh = NeuralLayers.Matrix(InputSize, HiddenSize, width);
        _weightsHh = NeuralLayers.Matrix(HiddenSize, HiddenSize, width);
        _bias = NeuralLayers.Vector(HiddenSize, width);
    }

    public int InputSize { get; }

    public int HiddenSize { get; }

    public string Activation { get; }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "inputSize")
            return RuntimeValue.Integer(InputSize);
        if (name == "hiddenSize")
            return RuntimeValue.Integer(HiddenSize);
        if (name == "activation")
            return RuntimeValue.String(Activation);
        if (name == "weightsXh")
            return _weightsXh;
        if (name == "weightsHh")
            return _weightsHh;
        if (name == "bias")
            return _bias;
        if (name is "forward" or "backward" or "sgd")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Rnn.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Rnn.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Rnn.forward() expects 1 argument: (sequence)");
        if (args[0].Type != ValueType.Array)
            throw new RuntimeException("Rnn.forward() sequence must be an array of vectors");
        var sequence = args[0].AsArray();
        if (sequence.Count == 0)
            throw new RuntimeException("Rnn.forward() sequence must be non-empty");
        var wxh = NeuralLayers.ReadMatrix("Rnn.forward", _weightsXh, "weightsXh");
        var whh = NeuralLayers.ReadMatrix("Rnn.forward", _weightsHh, "weightsHh");
        var bias = NeuralLayers.ReadVector("Rnn.forward", _bias, "bias");
        var hidden = new double[HiddenSize];
        var steps = new List<RnnStep>(sequence.Count);
        var outputs = new List<RuntimeValue>(sequence.Count);
        foreach (var item in sequence)
        {
            var input = NeuralLayers.ReadVector("Rnn.forward", item, "sequence");
            if (input.Length != InputSize)
                throw new RuntimeException("Rnn.forward() each input length must match inputSize");
            var pre = (double[])bias.Clone();
            for (var j = 0; j < HiddenSize; j++)
            {
                for (var i = 0; i < InputSize; i++)
                    pre[j] += input[i] * wxh[i][j];
                for (var i = 0; i < HiddenSize; i++)
                    pre[j] += hidden[i] * whh[i][j];
            }

            var next = NeuralLayers.Activate(Activation, pre);
            steps.Add(new RnnStep(input, (double[])hidden.Clone(), pre));
            hidden = next;
            outputs.Add(NeuralLayers.ToVector(next));
        }

        _steps = steps;
        return RuntimeValue.Array(outputs);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Rnn.backward() expects 1 argument: (upstreams)");
        if (_steps == null)
            throw new RuntimeException("Rnn.backward() requires forward() first");
        if (args[0].Type != ValueType.Array)
            throw new RuntimeException("Rnn.backward() upstreams must be an array of vectors");
        var upstreams = args[0].AsArray();
        if (upstreams.Count != _steps.Count)
            throw new RuntimeException("Rnn.backward() upstreams must have one vector per step");
        var wxh = NeuralLayers.ReadMatrix("Rnn.backward", _weightsXh, "weightsXh");
        var whh = NeuralLayers.ReadMatrix("Rnn.backward", _weightsHh, "weightsHh");
        var dXh = NeuralLayers.Zeros(InputSize, HiddenSize);
        var dHh = NeuralLayers.Zeros(HiddenSize, HiddenSize);
        var dBias = new double[HiddenSize];
        var dInputs = new double[_steps.Count][];
        var dhNext = new double[HiddenSize];
        for (var t = _steps.Count - 1; t >= 0; t--)
        {
            var upstream = NeuralLayers.ReadVector("Rnn.backward", upstreams[t], "upstreams");
            if (upstream.Length != HiddenSize)
                throw new RuntimeException("Rnn.backward() each upstream length must match hiddenSize");
            var step = _steps[t];
            var dh = new double[HiddenSize];
            for (var j = 0; j < HiddenSize; j++)
                dh[j] = upstream[j] + dhNext[j];
            var dAct = NeuralLayers.ActivateDerivative(Activation, step.Pre);
            var dPre = new double[HiddenSize];
            for (var j = 0; j < HiddenSize; j++)
                dPre[j] = dh[j] * dAct[j];
            for (var i = 0; i < InputSize; i++)
            {
                for (var j = 0; j < HiddenSize; j++)
                    dXh[i][j] += step.Input[i] * dPre[j];
            }

            for (var i = 0; i < HiddenSize; i++)
            {
                for (var j = 0; j < HiddenSize; j++)
                    dHh[i][j] += step.Hidden[i] * dPre[j];
            }

            for (var j = 0; j < HiddenSize; j++)
                dBias[j] += dPre[j];
            var dInput = new double[InputSize];
            for (var i = 0; i < InputSize; i++)
            {
                for (var j = 0; j < HiddenSize; j++)
                    dInput[i] += dPre[j] * wxh[i][j];
            }

            dInputs[t] = dInput;
            dhNext = new double[HiddenSize];
            for (var i = 0; i < HiddenSize; i++)
            {
                for (var j = 0; j < HiddenSize; j++)
                    dhNext[i] += dPre[j] * whh[i][j];
            }
        }

        _dXh = dXh;
        _dHh = dHh;
        _dBias = dBias;
        return NeuralLayers.ToMatrix(dInputs);
    }

    private RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Rnn.sgd() expects 1 argument: (lr)");
        if (_dXh == null || _dHh == null || _dBias == null)
            throw new RuntimeException("Rnn.sgd() requires backward() first");
        var learningRate = DenseInstance.RequireFinite("Rnn.sgd", args[0], "lr");
        NeuralLayers.ApplyMatrix(_weightsXh, _dXh, learningRate);
        NeuralLayers.ApplyMatrix(_weightsHh, _dHh, learningRate);
        NeuralLayers.ApplyVector(_bias, _dBias, learningRate);
        return RuntimeValue.Null();
    }

    private sealed class RnnStep
    {
        public RnnStep(double[] input, double[] hidden, double[] pre)
        {
            Input = input;
            Hidden = hidden;
            Pre = pre;
        }

        public double[] Input { get; }

        public double[] Hidden { get; }

        public double[] Pre { get; }
    }
}

/// <summary>Normalize one vector, then apply a learned scale and shift.</summary>
public sealed class LayerNormInstance : ObjectInstance
{
    private readonly RuntimeValue _gamma;
    private readonly RuntimeValue _beta;
    private double[]? _xhat;
    private double _rstd;
    private double[]? _dGamma;
    private double[]? _dBeta;

    public LayerNormInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count != 1)
            throw new RuntimeException("LayerNorm() expects 1 argument: (features)");
        Features = DenseInstance.RequirePositiveInt("LayerNorm", args[0], "features");
        _gamma = NeuralLayers.Filled(Features, 1.0);
        _beta = NeuralLayers.Filled(Features, 0.0);
    }

    public int Features { get; }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "features")
            return RuntimeValue.Integer(Features);
        if (name == "gamma")
            return _gamma;
        if (name == "beta")
            return _beta;
        if (name is "forward" or "backward" or "sgd")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on LayerNorm.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on LayerNorm.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("LayerNorm.forward() expects 1 argument: (x)");
        var input = NeuralLayers.ReadVector("LayerNorm.forward", args[0], "x");
        if (input.Length != Features)
            throw new RuntimeException("LayerNorm.forward() length must match features");
        var mean = 0.0;
        for (var i = 0; i < input.Length; i++)
            mean += input[i];
        mean /= input.Length;
        var variance = 0.0;
        for (var i = 0; i < input.Length; i++)
        {
            var delta = input[i] - mean;
            variance += delta * delta;
        }

        variance /= input.Length;
        _rstd = 1.0 / Math.Sqrt(variance + NeuralLayers.LayerNormEps);
        _xhat = new double[input.Length];
        var gamma = NeuralLayers.ReadVector("LayerNorm.forward", _gamma, "gamma");
        var beta = NeuralLayers.ReadVector("LayerNorm.forward", _beta, "beta");
        var output = new double[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            _xhat[i] = (input[i] - mean) * _rstd;
            output[i] = gamma[i] * _xhat[i] + beta[i];
        }

        return NeuralLayers.ToVector(output);
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("LayerNorm.backward() expects 1 argument: (upstream)");
        if (_xhat == null)
            throw new RuntimeException("LayerNorm.backward() requires forward() first");
        var upstream = NeuralLayers.ReadVector("LayerNorm.backward", args[0], "upstream");
        if (upstream.Length != Features)
            throw new RuntimeException("LayerNorm.backward() upstream length must match features");
        var gamma = NeuralLayers.ReadVector("LayerNorm.backward", _gamma, "gamma");
        var dGamma = new double[Features];
        var dBeta = new double[Features];
        var dxhat = new double[Features];
        for (var i = 0; i < Features; i++)
        {
            dBeta[i] = upstream[i];
            dGamma[i] = upstream[i] * _xhat[i];
            dxhat[i] = upstream[i] * gamma[i];
        }

        var meanDx = 0.0;
        var meanDxX = 0.0;
        for (var i = 0; i < Features; i++)
        {
            meanDx += dxhat[i];
            meanDxX += dxhat[i] * _xhat[i];
        }

        meanDx /= Features;
        meanDxX /= Features;
        var dInput = new double[Features];
        for (var i = 0; i < Features; i++)
            dInput[i] = _rstd * (dxhat[i] - meanDx - _xhat[i] * meanDxX);
        _dGamma = dGamma;
        _dBeta = dBeta;
        return NeuralLayers.ToVector(dInput);
    }

    private RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("LayerNorm.sgd() expects 1 argument: (lr)");
        if (_dGamma == null || _dBeta == null)
            throw new RuntimeException("LayerNorm.sgd() requires backward() first");
        var learningRate = DenseInstance.RequireFinite("LayerNorm.sgd", args[0], "lr");
        NeuralLayers.ApplyVector(_gamma, _dGamma, learningRate);
        NeuralLayers.ApplyVector(_beta, _dBeta, learningRate);
        return RuntimeValue.Null();
    }
}

/// <summary>
/// One attention head. <c>query</c> and <c>key</c> are the parameters.
/// <c>forward</c> receives the value matrix. A mask entry below 0.5 blocks that score.
/// </summary>
public sealed class AttentionInstance : ObjectInstance
{
    private readonly RuntimeValue _query;
    private readonly RuntimeValue _key;
    private double[][]? _value;
    private double[][]? _probs;
    private bool[][]? _blocked;
    private double[][]? _dQuery;
    private double[][]? _dKey;

    public AttentionInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 2 || args.Count > 3)
            throw new RuntimeException("Attention() expects 2 or 3 arguments: (length, dim, scale?)");
        Length = DenseInstance.RequirePositiveInt("Attention", args[0], "length");
        Dim = DenseInstance.RequirePositiveInt("Attention", args[1], "dim");
        Scale = args.Count == 3
            ? DenseInstance.RequireFinite("Attention", args[2], "scale")
            : 1.0 / Math.Sqrt(Dim);
        var width = 1.0 / Math.Sqrt(Dim);
        _query = NeuralLayers.Matrix(Length, Dim, width);
        _key = NeuralLayers.Matrix(Length, Dim, width);
    }

    public int Length { get; }

    public int Dim { get; }

    public double Scale { get; }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "length")
            return RuntimeValue.Integer(Length);
        if (name == "dim")
            return RuntimeValue.Integer(Dim);
        if (name == "scale")
            return RuntimeValue.Float(Scale);
        if (name == "query")
            return _query;
        if (name == "key")
            return _key;
        if (name == "probs")
        {
            if (_probs == null)
                throw new RuntimeException("Attention.probs requires forward() first");
            return NeuralLayers.ToMatrix(_probs);
        }

        if (name is "forward" or "backward" or "sgd")
            return NeuralLayers.Method(this, name);
        throw new RuntimeException($"Undefined property '{name}' on Attention.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Attention.")
        };
    }

    private RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new RuntimeException("Attention.forward() expects 1 or 2 arguments: (value, mask?)");
        var value = NeuralLayers.ReadMatrix("Attention.forward", args[0], "value");
        if (value.Length != Length || value[0].Length != Dim)
            throw new RuntimeException("Attention.forward() value must be length x dim");
        double[][]? mask = null;
        if (args.Count == 2)
        {
            mask = NeuralLayers.ReadMatrix("Attention.forward", args[1], "mask");
            if (mask.Length != Length || mask[0].Length != Length)
                throw new RuntimeException("Attention.forward() mask must be length x length");
        }

        var query = NeuralLayers.ReadMatrix("Attention.forward", _query, "query");
        var key = NeuralLayers.ReadMatrix("Attention.forward", _key, "key");
        var scores = NeuralLayers.Matmul(query, NeuralLayers.Transpose(key));
        var probs = new double[Length][];
        var blocked = new bool[Length][];
        for (var i = 0; i < Length; i++)
        {
            var row = new double[Length];
            blocked[i] = new bool[Length];
            for (var j = 0; j < Length; j++)
            {
                blocked[i][j] = mask != null && mask[i][j] < 0.5;
                row[j] = blocked[i][j] ? NeuralLayers.AttentionMaskFill : scores[i][j] * Scale;
            }

            probs[i] = NeuralLayers.Softmax(row);
        }

        _blocked = blocked;

        _value = value;
        _probs = probs;
        return NeuralLayers.ToMatrix(NeuralLayers.Matmul(probs, value));
    }

    private RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Attention.backward() expects 1 argument: (upstream)");
        if (_value == null || _probs == null)
            throw new RuntimeException("Attention.backward() requires forward() first");
        var upstream = NeuralLayers.ReadMatrix("Attention.backward", args[0], "upstream");
        if (upstream.Length != Length || upstream[0].Length != Dim)
            throw new RuntimeException("Attention.backward() upstream must be length x dim");
        var dProbs = NeuralLayers.Matmul(upstream, NeuralLayers.Transpose(_value));
        var dScores = NeuralLayers.Zeros(Length, Length);
        for (var i = 0; i < Length; i++)
        {
            var dot = 0.0;
            for (var j = 0; j < Length; j++)
                dot += dProbs[i][j] * _probs[i][j];
            for (var j = 0; j < Length; j++)
            {
                var local = _probs[i][j] * (dProbs[i][j] - dot);
                dScores[i][j] = _blocked != null && _blocked[i][j] ? 0.0 : local * Scale;
            }
        }

        var query = NeuralLayers.ReadMatrix("Attention.backward", _query, "query");
        var key = NeuralLayers.ReadMatrix("Attention.backward", _key, "key");
        _dQuery = NeuralLayers.Matmul(dScores, key);
        _dKey = NeuralLayers.Matmul(NeuralLayers.Transpose(dScores), query);
        return NeuralLayers.ToMatrix(NeuralLayers.Matmul(NeuralLayers.Transpose(_probs), upstream));
    }

    private RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Attention.sgd() expects 1 argument: (lr)");
        if (_dQuery == null || _dKey == null)
            throw new RuntimeException("Attention.sgd() requires backward() first");
        var learningRate = DenseInstance.RequireFinite("Attention.sgd", args[0], "lr");
        NeuralLayers.ApplyMatrix(_query, _dQuery, learningRate);
        NeuralLayers.ApplyMatrix(_key, _dKey, learningRate);
        return RuntimeValue.Null();
    }
}
