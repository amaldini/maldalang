// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// <c>nn.*</c> activations, local derivatives, and one dense layer.
/// Linear algebra stays on <c>math.dot</c> / <c>matmul</c> / <c>transpose</c>.
/// Derivatives are with respect to the pre-activation, so a backward step is
/// still one chain-rule multiply. There is no tape.
/// </summary>
public static class NnStdLib
{
    private const double SigmoidClamp = 20.0;
    private const double LeakyAlpha = 0.01;
    private const double EluAlpha = 1.0;
    private const double GeluK = 0.7978845608028654;
    private const double GeluC = 0.044715;
    private const string ActivationList = "relu, leakyRelu, elu, gelu, silu, softplus, sigmoid, tanh, or linear";

    public static RuntimeValue Call(string methodName, List<RuntimeValue> args) =>
        methodName switch
        {
            "relu" => MathNeural.Relu(args),
            "sigmoid" => MathNeural.Sigmoid(args),
            "tanh" => MathNeural.Tanh(args),
            "mse" => MathNeural.Mse(args),
            "softmax" or "crossEntropyFromLogits" => BuiltInFunctions.CallBuiltIn(methodName, args, null),
            "leakyRelu" => LeakyRelu(args),
            "elu" => Elu(args),
            "gelu" => Gelu(args),
            "silu" => Silu(args),
            "softplus" => Softplus(args),
            "dRelu" => DRelu(args),
            "dLeakyRelu" => DLeakyRelu(args),
            "dElu" => DElu(args),
            "dGelu" => DGelu(args),
            "dSilu" => DSilu(args),
            "dSoftplus" => DSoftplus(args),
            "dSigmoid" => DSigmoid(args),
            "dTanh" => DTanh(args),
            "dense" => Dense(args),
            "denseBackward" => DenseBackward(args),
            "mseGrad" => MseGrad(args),
            "softmaxGrad" => SoftmaxGrad(args),
            _ => throw new Exception($"Unknown nn method: {methodName}")
        };

    public static RuntimeValue LeakyRelu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("leakyRelu", args, 1, 2, "x, alpha?");
        var alpha = OptionalAlpha("leakyRelu", args, LeakyAlpha);
        return Map("leakyRelu", args[0], x => x > 0.0 ? x : alpha * x);
    }

    public static RuntimeValue Elu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("elu", args, 1, 2, "x, alpha?");
        var alpha = OptionalAlpha("elu", args, EluAlpha);
        return Map("elu", args[0], x => x > 0.0 ? x : alpha * (Math.Exp(x) - 1.0));
    }

    public static RuntimeValue Gelu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("gelu", args, 1, 1, "x");
        return Map("gelu", args[0], GeluScalar);
    }

    public static RuntimeValue Silu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("silu", args, 1, 1, "x");
        return Map("silu", args[0], x => x * SigmoidScalar(x));
    }

    public static RuntimeValue Softplus(List<RuntimeValue> args)
    {
        BuiltInArity.Require("softplus", args, 1, 1, "x");
        return Map("softplus", args[0], SoftplusScalar);
    }

    public static RuntimeValue DRelu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dRelu", args, 1, 1, "x");
        return Map("dRelu", args[0], x => x > 0.0 ? 1.0 : 0.0);
    }

    public static RuntimeValue DLeakyRelu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dLeakyRelu", args, 1, 2, "x, alpha?");
        var alpha = OptionalAlpha("dLeakyRelu", args, LeakyAlpha);
        return Map("dLeakyRelu", args[0], x => x > 0.0 ? 1.0 : alpha);
    }

    public static RuntimeValue DElu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dElu", args, 1, 2, "x, alpha?");
        var alpha = OptionalAlpha("dElu", args, EluAlpha);
        return Map("dElu", args[0], x => x > 0.0 ? 1.0 : alpha * Math.Exp(x));
    }

    public static RuntimeValue DGelu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dGelu", args, 1, 1, "x");
        return Map("dGelu", args[0], DGeluScalar);
    }

    public static RuntimeValue DSilu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dSilu", args, 1, 1, "x");
        return Map("dSilu", args[0], DSiluScalar);
    }

    public static RuntimeValue DSoftplus(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dSoftplus", args, 1, 1, "x");
        return Map("dSoftplus", args[0], SigmoidScalar);
    }

    public static RuntimeValue DSigmoid(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dSigmoid", args, 1, 1, "x");
        return Map("dSigmoid", args[0], z =>
        {
            var s = SigmoidScalar(z);
            return s * (1.0 - s);
        });
    }

    public static RuntimeValue DTanh(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dTanh", args, 1, 1, "x");
        return Map("dTanh", args[0], z =>
        {
            var t = Math.Tanh(z);
            return 1.0 - t * t;
        });
    }

    public static RuntimeValue Dense(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dense", args, 2, 4, "x, weights, bias?, activation?");
        var (biasValue, activation) = SplitBiasAndActivation("dense", args, 2);
        var weights = RequireMatrix("dense", args[1], "weights");
        var bias = ResolveBias("dense", biasValue, weights[0].Length);
        activation = NormalizeActivation("dense", activation);

        if (IsMatrix(args[0]))
        {
            var batch = RequireMatrix("dense", args[0], "x");
            if (batch[0].Length != weights.Length)
                throw new RuntimeException("dense() inner dimensions must match");
            var pre = new double[batch.Length][];
            for (var b = 0; b < batch.Length; b++)
            {
                pre[b] = new double[bias.Length];
                for (var j = 0; j < bias.Length; j++)
                {
                    var sum = bias[j];
                    for (var i = 0; i < weights.Length; i++)
                        sum += batch[b][i] * weights[i][j];
                    pre[b][j] = sum;
                }
            }

            return Dict(
                ("pre", ToMatrix(pre)),
                ("out", ToMatrix(ActivateMatrix(activation, pre))));
        }

        var vector = RequireVector("dense", args[0], "x");
        if (vector.Length == 0)
            throw new RuntimeException("dense() expects non-empty input");
        if (vector.Length != weights.Length)
            throw new RuntimeException("dense() inner dimensions must match");
        var preRow = new double[bias.Length];
        for (var j = 0; j < bias.Length; j++)
        {
            var sum = bias[j];
            for (var i = 0; i < weights.Length; i++)
                sum += vector[i] * weights[i][j];
            preRow[j] = sum;
        }

        return Dict(
            ("pre", ToVector(preRow)),
            ("out", ToVector(ActivateVector(activation, preRow))));
    }

    public static RuntimeValue DenseBackward(List<RuntimeValue> args)
    {
        BuiltInArity.Require("denseBackward", args, 3, 5, "x, weights, upstream, activation?, pre?");
        string? activation = null;
        RuntimeValue? preValue = null;
        if (args.Count >= 4)
        {
            activation = RequireActivationString("denseBackward", args[3]);
            if (args.Count == 5)
                preValue = args[4];
        }

        activation = NormalizeActivation("denseBackward", activation);
        if (!IsLinear(activation) && preValue == null)
            throw new RuntimeException("denseBackward() pre is required when activation is not linear");

        var weights = RequireMatrix("denseBackward", args[1], "weights");
        var outFeatures = weights[0].Length;

        if (IsMatrix(args[0]))
        {
            var batch = RequireMatrix("denseBackward", args[0], "x");
            if (batch[0].Length != weights.Length)
                throw new RuntimeException("denseBackward() inner dimensions must match");
            var upstream = RequireMatrix("denseBackward", args[2], "upstream");
            if (upstream.Length != batch.Length || upstream[0].Length != outFeatures)
                throw new RuntimeException("denseBackward() upstream shape must match the layer output");
            var pre = preValue == null ? upstream : RequireMatrix("denseBackward", preValue, "pre");
            if (preValue != null && (pre.Length != upstream.Length || pre[0].Length != upstream[0].Length))
                throw new RuntimeException("denseBackward() pre shape must match upstream");
            var dZ = ActivateDerivativeMatrix(activation, pre, upstream);
            var dW = new double[weights.Length][];
            for (var i = 0; i < weights.Length; i++)
            {
                dW[i] = new double[outFeatures];
                for (var j = 0; j < outFeatures; j++)
                {
                    var sum = 0.0;
                    for (var b = 0; b < batch.Length; b++)
                        sum += batch[b][i] * dZ[b][j];
                    dW[i][j] = sum;
                }
            }

            var dBias = new double[outFeatures];
            for (var j = 0; j < outFeatures; j++)
            {
                var sum = 0.0;
                for (var b = 0; b < batch.Length; b++)
                    sum += dZ[b][j];
                dBias[j] = sum;
            }

            var dInput = new double[batch.Length][];
            for (var b = 0; b < batch.Length; b++)
            {
                dInput[b] = new double[weights.Length];
                for (var i = 0; i < weights.Length; i++)
                {
                    var sum = 0.0;
                    for (var j = 0; j < outFeatures; j++)
                        sum += dZ[b][j] * weights[i][j];
                    dInput[b][i] = sum;
                }
            }

            return Dict(
                ("dInput", ToMatrix(dInput)),
                ("dWeights", ToMatrix(dW)),
                ("dBias", ToVector(dBias)));
        }

        var vector = RequireVector("denseBackward", args[0], "x");
        if (vector.Length == 0)
            throw new RuntimeException("denseBackward() expects non-empty input");
        if (vector.Length != weights.Length)
            throw new RuntimeException("denseBackward() inner dimensions must match");
        var upstreamRow = RequireVector("denseBackward", args[2], "upstream");
        if (upstreamRow.Length != outFeatures)
            throw new RuntimeException("denseBackward() upstream shape must match the layer output");
        var preRow = preValue == null ? upstreamRow : RequireVector("denseBackward", preValue, "pre");
        if (preValue != null && preRow.Length != upstreamRow.Length)
            throw new RuntimeException("denseBackward() pre shape must match upstream");
        var dZRow = ActivateDerivativeVector(activation, preRow, upstreamRow);
        var dWRow = new double[weights.Length][];
        for (var i = 0; i < weights.Length; i++)
        {
            dWRow[i] = new double[outFeatures];
            for (var j = 0; j < outFeatures; j++)
                dWRow[i][j] = vector[i] * dZRow[j];
        }

        var dInputRow = new double[weights.Length];
        for (var i = 0; i < weights.Length; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < outFeatures; j++)
                sum += dZRow[j] * weights[i][j];
            dInputRow[i] = sum;
        }

        return Dict(
            ("dInput", ToVector(dInputRow)),
            ("dWeights", ToMatrix(dWRow)),
            ("dBias", ToVector(dZRow)));
    }

    public static RuntimeValue MseGrad(List<RuntimeValue> args)
    {
        BuiltInArity.Require("mseGrad", args, 2, 2, "pred, target");
        if (IsNumeric(args[0]) && IsNumeric(args[1]))
            return RuntimeValue.Float(AsNumeric("mseGrad", args[0]) - AsNumeric("mseGrad", args[1]));

        if (IsMatrix(args[0]) || IsMatrix(args[1]))
        {
            var pred = RequireMatrix("mseGrad", args[0], "pred");
            var target = RequireMatrix("mseGrad", args[1], "target");
            if (pred.Length != target.Length || pred[0].Length != target[0].Length)
                throw new RuntimeException("mseGrad() expects pred and target with the same shape");
            var grad = new double[pred.Length][];
            for (var i = 0; i < pred.Length; i++)
            {
                if (pred[i].Length != target[i].Length)
                    throw new RuntimeException("mseGrad() expects pred and target with the same shape");
                grad[i] = new double[pred[i].Length];
                for (var j = 0; j < pred[i].Length; j++)
                    grad[i][j] = pred[i][j] - target[i][j];
            }

            return ToMatrix(grad);
        }

        var predRow = RequireVector("mseGrad", args[0], "pred");
        var targetRow = RequireVector("mseGrad", args[1], "target");
        if (predRow.Length == 0 || predRow.Length != targetRow.Length)
            throw new RuntimeException("mseGrad() expects pred and target with the same shape");
        var row = new double[predRow.Length];
        for (var i = 0; i < predRow.Length; i++)
            row[i] = predRow[i] - targetRow[i];
        return ToVector(row);
    }

    public static RuntimeValue SoftmaxGrad(List<RuntimeValue> args)
    {
        BuiltInArity.Require("softmaxGrad", args, 2, 2, "logits, target");
        var probs = BuiltInFunctions.CallBuiltIn("softmax", new List<RuntimeValue> { args[0] }, null);
        var values = probs.AsArray();
        var grad = new List<RuntimeValue>(values.Count);
        if (args[1].Type == ValueType.Array)
        {
            var target = RequireVector("softmaxGrad", args[1], "target");
            if (target.Length != values.Count)
                throw new RuntimeException("softmaxGrad() target length must match logits");
            for (var i = 0; i < values.Count; i++)
                grad.Add(RuntimeValue.Float(values[i].AsFloat() - target[i]));
            return RuntimeValue.Array(grad);
        }

        if (!NumericCoercion.TryAsInteger(args[1], out var index))
            throw new RuntimeException("softmaxGrad() target must be a class index or a numeric vector");
        if (index < 0 || index >= values.Count)
            throw new RuntimeException("softmaxGrad() target index out of range");
        for (var i = 0; i < values.Count; i++)
        {
            var p = values[i].AsFloat();
            if (i == index)
                p -= 1.0;
            grad.Add(RuntimeValue.Float(p));
        }

        return RuntimeValue.Array(grad);
    }

    private static (RuntimeValue? Bias, string? Activation) SplitBiasAndActivation(string name, List<RuntimeValue> args, int firstOptional)
    {
        if (args.Count <= firstOptional)
            return (null, null);
        if (args[firstOptional].Type == ValueType.String)
        {
            if (args.Count > firstOptional + 1)
                throw new RuntimeException($"{name}() activation is the last argument");
            return (null, args[firstOptional].AsString());
        }

        string? activation = null;
        if (args.Count > firstOptional + 1)
            activation = RequireActivationString(name, args[firstOptional + 1]);
        return (args[firstOptional], activation);
    }

    private static string RequireActivationString(string name, RuntimeValue value)
    {
        if (value.Type != ValueType.String)
            throw new RuntimeException($"{name}() activation must be a string");
        return value.AsString();
    }

    private static string? NormalizeActivation(string name, string? activation)
    {
        if (activation == null || activation == "linear")
            return null;
        if (activation is "relu" or "leakyRelu" or "elu" or "gelu" or "silu" or "softplus" or "sigmoid" or "tanh")
            return activation;
        throw new RuntimeException($"{name}() unknown activation '{activation}'; expected {ActivationList}");
    }

    private static bool IsLinear(string? activation) => activation == null;

    private static double[] ResolveBias(string name, RuntimeValue? biasValue, int outFeatures)
    {
        if (biasValue == null)
        {
            var zeros = new double[outFeatures];
            return zeros;
        }

        var bias = RequireVector(name, biasValue, "bias");
        if (bias.Length != outFeatures)
            throw new RuntimeException($"{name}() bias length must match the output size");
        return bias;
    }

    private static double[] ActivateVector(string? activation, double[] pre)
    {
        var result = new double[pre.Length];
        for (var i = 0; i < pre.Length; i++)
            result[i] = ApplyActivation(activation, pre[i]);
        return result;
    }

    private static double[][] ActivateMatrix(string? activation, double[][] pre)
    {
        var result = new double[pre.Length][];
        for (var i = 0; i < pre.Length; i++)
            result[i] = ActivateVector(activation, pre[i]);
        return result;
    }

    private static double[] ActivateDerivativeVector(string? activation, double[] pre, double[] upstream)
    {
        var result = new double[upstream.Length];
        for (var i = 0; i < upstream.Length; i++)
            result[i] = upstream[i] * ApplyDerivative(activation, pre[i]);
        return result;
    }

    private static double[][] ActivateDerivativeMatrix(string? activation, double[][] pre, double[][] upstream)
    {
        var result = new double[upstream.Length][];
        for (var i = 0; i < upstream.Length; i++)
            result[i] = ActivateDerivativeVector(activation, pre[i], upstream[i]);
        return result;
    }

    private static double ApplyActivation(string? activation, double x) =>
        activation switch
        {
            null => x,
            "relu" => x > 0.0 ? x : 0.0,
            "leakyRelu" => x > 0.0 ? x : LeakyAlpha * x,
            "elu" => x > 0.0 ? x : EluAlpha * (Math.Exp(x) - 1.0),
            "gelu" => GeluScalar(x),
            "silu" => x * SigmoidScalar(x),
            "softplus" => SoftplusScalar(x),
            "sigmoid" => SigmoidScalar(x),
            "tanh" => Math.Tanh(x),
            _ => throw new RuntimeException($"dense() unknown activation '{activation}'")
        };

    private static double ApplyDerivative(string? activation, double x) =>
        activation switch
        {
            null => 1.0,
            "relu" => x > 0.0 ? 1.0 : 0.0,
            "leakyRelu" => x > 0.0 ? 1.0 : LeakyAlpha,
            "elu" => x > 0.0 ? 1.0 : EluAlpha * Math.Exp(x),
            "gelu" => DGeluScalar(x),
            "silu" => DSiluScalar(x),
            "softplus" => SigmoidScalar(x),
            "sigmoid" => SigmoidScalar(x) * (1.0 - SigmoidScalar(x)),
            "tanh" => 1.0 - Math.Tanh(x) * Math.Tanh(x),
            _ => throw new RuntimeException($"denseBackward() unknown activation '{activation}'")
        };

    private static double SigmoidScalar(double x)
    {
        if (x < -SigmoidClamp)
            return 0.0;
        if (x > SigmoidClamp)
            return 1.0;
        return 1.0 / (1.0 + Math.Exp(-x));
    }

    private static double SoftplusScalar(double x)
    {
        if (x > SigmoidClamp)
            return x;
        if (x < -SigmoidClamp)
            return Math.Exp(x);
        return Math.Log(1.0 + Math.Exp(x));
    }

    private static double GeluScalar(double x)
    {
        var inner = GeluK * (x + GeluC * x * x * x);
        return 0.5 * x * (1.0 + Math.Tanh(inner));
    }

    private static double DGeluScalar(double x)
    {
        var inner = GeluK * (x + GeluC * x * x * x);
        var tanh = Math.Tanh(inner);
        var dInner = GeluK * (1.0 + 3.0 * GeluC * x * x);
        var sech2 = 1.0 - tanh * tanh;
        return 0.5 * (1.0 + tanh) + 0.5 * x * sech2 * dInner;
    }

    private static double DSiluScalar(double x)
    {
        var s = SigmoidScalar(x);
        return s * (1.0 + x * (1.0 - s));
    }

    private static double OptionalAlpha(string name, List<RuntimeValue> args, double fallback)
    {
        if (args.Count < 2)
            return fallback;
        return AsNumeric(name, args[1]);
    }

    private static RuntimeValue Map(string name, RuntimeValue value, Func<double, double> fn)
    {
        if (IsNumeric(value))
            return RuntimeValue.Float(fn(AsNumeric(name, value)));
        if (IsMatrix(value))
        {
            var matrix = RequireMatrix(name, value, "first");
            var mapped = new double[matrix.Length][];
            for (var i = 0; i < matrix.Length; i++)
            {
                mapped[i] = new double[matrix[i].Length];
                for (var j = 0; j < matrix[i].Length; j++)
                    mapped[i][j] = fn(matrix[i][j]);
            }

            return ToMatrix(mapped);
        }

        var vector = RequireVector(name, value, "first");
        var row = new double[vector.Length];
        for (var i = 0; i < vector.Length; i++)
            row[i] = fn(vector[i]);
        return ToVector(row);
    }

    private static RuntimeValue Dict(params (string Key, RuntimeValue Value)[] entries)
    {
        var dict = new DictionaryInstance();
        foreach (var (key, value) in entries)
            dict.SetEntry(key, value);
        return RuntimeValue.Object(dict);
    }

    private static RuntimeValue ToVector(double[] values)
    {
        var list = new List<RuntimeValue>(values.Length);
        foreach (var value in values)
            list.Add(RuntimeValue.Float(value));
        return RuntimeValue.Array(list);
    }

    private static RuntimeValue ToMatrix(double[][] rows)
    {
        var list = new List<RuntimeValue>(rows.Length);
        foreach (var row in rows)
            list.Add(ToVector(row));
        return RuntimeValue.Array(list);
    }

    private static bool IsNumeric(RuntimeValue value) =>
        value.Type is ValueType.Integer or ValueType.Float;

    private static bool IsMatrix(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            return false;
        var rows = value.AsArray();
        return rows.Count > 0 && rows[0].Type == ValueType.Array;
    }

    private static double AsNumeric(string name, RuntimeValue value)
    {
        if (value.Type == ValueType.Integer)
            return value.AsInteger();
        if (value.Type == ValueType.Float)
            return value.AsFloat();
        throw new RuntimeException($"{name}() expects numeric values");
    }

    private static double[] RequireVector(string name, RuntimeValue value, string which)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"{name}() expects a numeric vector as {which}");
        var items = value.AsArray();
        var vector = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Type == ValueType.Array)
                throw new RuntimeException($"{name}() expects a numeric vector as {which}");
            vector[i] = AsNumeric(name, items[i]);
        }

        return vector;
    }

    private static double[][] RequireMatrix(string name, RuntimeValue value, string which)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"{name}() expects a 2D numeric matrix as {which}");
        var rows = value.AsArray();
        if (rows.Count == 0)
            throw new RuntimeException($"{name}() expects a non-empty matrix as {which}");
        var matrix = new double[rows.Count][];
        var width = -1;
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Type != ValueType.Array)
                throw new RuntimeException($"{name}() expects a 2D numeric matrix as {which}");
            var cells = rows[r].AsArray();
            if (width < 0)
                width = cells.Count;
            else if (cells.Count != width)
                throw new RuntimeException($"{name}() matrix rows must have the same length");
            if (cells.Count == 0)
                throw new RuntimeException($"{name}() expects non-empty matrices");
            matrix[r] = new double[cells.Count];
            for (var c = 0; c < cells.Count; c++)
                matrix[r][c] = AsNumeric(name, cells[c]);
        }

        return matrix;
    }
}
