// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// One dense layer. Forward and backward call <c>nn.dense</c> / <c>nn.denseBackward</c>.
/// There is no tape. <c>sgd</c> applies the gradients stored by the last <c>backward</c>.
/// </summary>
public sealed class DenseInstance : ObjectInstance
{
    private readonly RuntimeValue _weights;
    private readonly RuntimeValue _bias;
    private RuntimeValue? _lastInput;
    private RuntimeValue? _pre;
    private RuntimeValue? _dWeights;
    private RuntimeValue? _dBias;

    public DenseInstance(List<RuntimeValue> args) : base(null)
    {
        if (args.Count < 2 || args.Count > 4)
            throw new RuntimeException("Dense() expects 2 to 4 arguments: (inFeatures, outFeatures, activation?, scale?)");

        InFeatures = RequirePositiveInt("Dense", args[0], "inFeatures");
        OutFeatures = RequirePositiveInt("Dense", args[1], "outFeatures");
        Activation = "linear";
        double? scale = null;
        if (args.Count >= 3)
        {
            if (args[2].Type != ValueType.String)
                throw new RuntimeException("Dense() activation must be a string");
            Activation = args[2].AsString();
            if (Activation is not ("relu" or "leakyRelu" or "elu" or "gelu" or "silu" or "softplus" or "sigmoid" or "tanh" or "linear"))
                throw new RuntimeException($"Dense() unknown activation '{Activation}'");
        }

        if (args.Count == 4)
            scale = RequireFinite("Dense", args[3], "scale");

        var width = scale ?? 1.0 / Math.Sqrt(InFeatures);
        if (width < 0)
            throw new RuntimeException("Dense() scale must be >= 0");

        _weights = RandomMatrix(InFeatures, OutFeatures, width);
        _bias = RandomVector(OutFeatures, width);
    }

    public int InFeatures { get; }

    public int OutFeatures { get; }

    public string Activation { get; }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "inFeatures")
            return RuntimeValue.Integer(InFeatures);
        if (name == "outFeatures")
            return RuntimeValue.Integer(OutFeatures);
        if (name == "activation")
            return RuntimeValue.String(Activation);
        if (name == "weights")
            return _weights;
        if (name == "bias")
            return _bias;
        if (name is "forward" or "backward" or "sgd")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }

        throw new RuntimeException($"Undefined property '{name}' on Dense.");
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "forward" => Forward(args),
            "backward" => Backward(args),
            "sgd" => Sgd(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on Dense.")
        };
    }

    public RuntimeValue Forward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Dense.forward() expects 1 argument: (x)");
        return Forward(args[0]);
    }

    public RuntimeValue Forward(RuntimeValue input)
    {
        if (IsMatrix(input))
            throw new RuntimeException("Dense.forward() expects a numeric vector");

        var call = new List<RuntimeValue> { input, _weights, _bias };
        if (Activation != "linear")
            call.Add(RuntimeValue.String(Activation));
        var result = NnStdLib.Dense(call);
        _lastInput = input;
        _pre = Field(result, "pre");
        return Field(result, "out");
    }

    public RuntimeValue Backward(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Dense.backward() expects 1 argument: (upstream)");
        if (_lastInput == null || _pre == null)
            throw new RuntimeException("Dense.backward() requires forward() first");

        var call = new List<RuntimeValue> { _lastInput, _weights, args[0] };
        if (Activation != "linear")
        {
            call.Add(RuntimeValue.String(Activation));
            call.Add(_pre);
        }

        var result = NnStdLib.DenseBackward(call);
        _dWeights = Field(result, "dWeights");
        _dBias = Field(result, "dBias");
        return Field(result, "dInput");
    }

    public RuntimeValue Sgd(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Dense.sgd() expects 1 argument: (lr)");
        return Sgd(RequireFinite("Dense.sgd", args[0], "lr"));
    }

    public RuntimeValue Sgd(double learningRate)
    {
        if (_dWeights == null || _dBias == null)
            throw new RuntimeException("Dense.sgd() requires backward() first");
        ApplyMatrix(_weights, _dWeights, learningRate);
        ApplyVector(_bias, _dBias, learningRate);
        return RuntimeValue.Null();
    }

    private static RuntimeValue Field(RuntimeValue value, string name) =>
        value.AsObject().Get(name);

    private static RuntimeValue RandomMatrix(int rows, int cols, double scale)
    {
        var matrix = new List<RuntimeValue>(rows);
        for (var i = 0; i < rows; i++)
            matrix.Add(RandomVector(cols, scale));
        return RuntimeValue.Array(matrix);
    }

    private static RuntimeValue RandomVector(int length, double scale)
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

    private static void ApplyMatrix(RuntimeValue weights, RuntimeValue grad, double learningRate)
    {
        var rows = weights.AsArray();
        var gradRows = grad.AsArray();
        for (var i = 0; i < rows.Count; i++)
            ApplyVector(rows[i], gradRows[i], learningRate);
    }

    private static void ApplyVector(RuntimeValue values, RuntimeValue grad, double learningRate)
    {
        var row = values.AsArray();
        var gradRow = grad.AsArray();
        for (var i = 0; i < row.Count; i++)
        {
            var current = AsNumber(row[i]);
            var delta = AsNumber(gradRow[i]);
            row[i] = RuntimeValue.Float(current - learningRate * delta);
        }
    }

    internal static bool IsMatrix(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            return false;
        var rows = value.AsArray();
        return rows.Count > 0 && rows[0].Type == ValueType.Array;
    }

    internal static int RequirePositiveInt(string name, RuntimeValue value, string which)
    {
        if (value.Type != ValueType.Integer)
            throw new RuntimeException($"{name}() {which} must be an integer");
        var number = value.AsInteger();
        if (number <= 0)
            throw new RuntimeException($"{name}() {which} must be > 0");
        return number;
    }

    internal static double RequireFinite(string name, RuntimeValue value, string which)
    {
        double number;
        if (value.Type == ValueType.Integer)
            number = value.AsInteger();
        else if (value.Type == ValueType.Float)
            number = value.AsFloat();
        else
            throw new RuntimeException($"{name}() {which} must be a number");
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new RuntimeException($"{name}() {which} must be finite");
        return number;
    }

    internal static double AsNumber(RuntimeValue value)
    {
        if (value.Type == ValueType.Integer)
            return value.AsInteger();
        if (value.Type == ValueType.Float)
            return value.AsFloat();
        throw new RuntimeException("Dense() expects numeric values");
    }
}
