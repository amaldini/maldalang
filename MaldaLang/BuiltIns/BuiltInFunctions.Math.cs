// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInAbs(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("abs() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.Integer)
            return RuntimeValue.Integer(Math.Abs(arg.AsInteger()));
        if (arg.Type == MaldaLang.Interpreter.ValueType.Float)
            return RuntimeValue.Float(Math.Abs(arg.AsFloat()));
        throw new Exception("abs() expects a number");
    }

    private static RuntimeValue BuiltInSum(List<RuntimeValue> args)
    {
        BuiltInArity.Require("sum", args, 1, 1, "array");
        var values = GetNumericArrayArgument("sum", args, requireNonEmpty: false);
        double sum = 0;
        var allIntegers = true;

        foreach (var value in values)
        {
            if (value.Type == ValueType.Integer)
            {
                sum += value.AsInteger();
            }
            else
            {
                allIntegers = false;
                sum += value.AsFloat();
            }
        }

        return CreateNumericRuntimeValue(sum, allIntegers);
    }

    private static RuntimeValue BuiltInAverage(List<RuntimeValue> args)
    {
        BuiltInArity.Require("average", args, 1, 1, "array");
        var values = GetNumericArrayArgument("average", args, requireNonEmpty: true);
        double sum = 0;

        foreach (var value in values)
        {
            sum += value.Type == ValueType.Integer ? value.AsInteger() : value.AsFloat();
        }

        return RuntimeValue.Float(sum / values.Count);
    }
    
    private static RuntimeValue BuiltInMax(List<RuntimeValue> args)
    {
        if (args.Count == 1)
        {
            var values = GetNumericArrayArgument("max", args, requireNonEmpty: true);
            var best = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
            var allIntegers = values[0].Type == ValueType.Integer;

            for (var i = 1; i < values.Count; i++)
            {
                var current = values[i];
                if (current.Type == ValueType.Float)
                    allIntegers = false;

                var currentValue = current.Type == ValueType.Integer ? current.AsInteger() : current.AsFloat();
                if (currentValue > best)
                    best = currentValue;
            }

            return CreateNumericRuntimeValue(best, allIntegers);
        }

        if (args.Count != 2) throw new Exception("max() expects 2 arguments or 1 array");
        var a = args[0];
        var b = args[1];
        if (!IsNumericValue(a) || !IsNumericValue(b))
            throw new Exception("max() expects numbers");
        if (a.Type == MaldaLang.Interpreter.ValueType.Integer && b.Type == MaldaLang.Interpreter.ValueType.Integer)
            return RuntimeValue.Integer(Math.Max(a.AsInteger(), b.AsInteger()));
        var aVal = a.Type == MaldaLang.Interpreter.ValueType.Integer ? a.AsInteger() : a.AsFloat();
        var bVal = b.Type == MaldaLang.Interpreter.ValueType.Integer ? b.AsInteger() : b.AsFloat();
        return RuntimeValue.Float(Math.Max(aVal, bVal));
    }
    
    private static RuntimeValue BuiltInMin(List<RuntimeValue> args)
    {
        if (args.Count == 1)
        {
            var values = GetNumericArrayArgument("min", args, requireNonEmpty: true);
            var best = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
            var allIntegers = values[0].Type == ValueType.Integer;

            for (var i = 1; i < values.Count; i++)
            {
                var current = values[i];
                if (current.Type == ValueType.Float)
                    allIntegers = false;

                var currentValue = current.Type == ValueType.Integer ? current.AsInteger() : current.AsFloat();
                if (currentValue < best)
                    best = currentValue;
            }

            return CreateNumericRuntimeValue(best, allIntegers);
        }

        if (args.Count != 2) throw new Exception("min() expects 2 arguments or 1 array");
        var a = args[0];
        var b = args[1];
        if (!IsNumericValue(a) || !IsNumericValue(b))
            throw new Exception("min() expects numbers");
        if (a.Type == MaldaLang.Interpreter.ValueType.Integer && b.Type == MaldaLang.Interpreter.ValueType.Integer)
            return RuntimeValue.Integer(Math.Min(a.AsInteger(), b.AsInteger()));
        var aVal = a.Type == MaldaLang.Interpreter.ValueType.Integer ? a.AsInteger() : a.AsFloat();
        var bVal = b.Type == MaldaLang.Interpreter.ValueType.Integer ? b.AsInteger() : b.AsFloat();
        return RuntimeValue.Float(Math.Min(aVal, bVal));
    }

    private static List<RuntimeValue> GetNumericArrayArgument(string functionName, List<RuntimeValue> args, bool requireNonEmpty)
    {
        if (args[0].Type != ValueType.Array) throw new Exception($"{functionName}() expects an array argument");

        var values = args[0].AsArray();
        if (requireNonEmpty && values.Count == 0)
            throw new Exception($"{functionName}() expects a non-empty array");

        foreach (var value in values)
        {
            if (!IsNumericValue(value))
                throw new Exception($"{functionName}() array elements must be numbers");
        }

        return values;
    }

    private static bool IsNumericValue(RuntimeValue value)
    {
        return value.Type == ValueType.Integer || value.Type == ValueType.Float;
    }

    private static RuntimeValue CreateNumericRuntimeValue(double value, bool preferInteger)
    {
        if (preferInteger)
            return RuntimeValue.Integer((int)value);

        return RuntimeValue.Float(value);
    }

    private static RuntimeValue BuiltInPow(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("pow() expects 2 arguments");
        var a = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        var b = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        return RuntimeValue.Float(Math.Pow(a, b));
    }
    
    private static RuntimeValue BuiltInSqrt(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("sqrt() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Sqrt(arg));
    }

    // Extended math: rounding and sign

    private static RuntimeValue BuiltInFloor(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("floor() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Floor(arg));
    }

    private static RuntimeValue BuiltInCeil(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("ceil() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Ceiling(arg));
    }

    private static RuntimeValue BuiltInRound(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("round() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Round(arg));
    }

    private static RuntimeValue BuiltInTrunc(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("trunc() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Truncate(arg));
    }

    private static RuntimeValue BuiltInSign(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("sign() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float((double)Math.Sign(arg));
    }

    // Extended math: exponential and logarithm

    private static RuntimeValue BuiltInExp(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("exp() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Exp(arg));
    }

    private static RuntimeValue BuiltInLog(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("log() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Log(arg));
    }

    private static RuntimeValue BuiltInLog10(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("log10() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Log10(arg));
    }

    private static RuntimeValue BuiltInLog2(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("log2() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Log(arg, 2.0));
    }

    // Extended math: trigonometry (radians)

    private static RuntimeValue BuiltInSin(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("sin() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Sin(arg));
    }

    private static RuntimeValue BuiltInCos(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("cos() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Cos(arg));
    }

    private static RuntimeValue BuiltInTan(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("tan() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Tan(arg));
    }

    private static RuntimeValue BuiltInAsin(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("asin() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Asin(arg));
    }

    private static RuntimeValue BuiltInAcos(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("acos() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Acos(arg));
    }

    private static RuntimeValue BuiltInAtan(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("atan() expects 1 argument");
        var arg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(Math.Atan(arg));
    }

    private static RuntimeValue BuiltInAtan2(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("atan2() expects 2 arguments");
        var y = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        var x = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        return RuntimeValue.Float(Math.Atan2(y, x));
    }

    // Extended math: utility functions

    private static RuntimeValue BuiltInHypot(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("hypot() expects 2 arguments");
        var x = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        var y = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        return RuntimeValue.Float(Math.Sqrt(x * x + y * y));
    }

    private static RuntimeValue BuiltInClamp(List<RuntimeValue> args)
    {
        if (args.Count != 3) throw new Exception("clamp() expects 3 arguments");
        var x = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        var lo = args[1].Type == MaldaLang.Interpreter.ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        var hi = args[2].Type == MaldaLang.Interpreter.ValueType.Integer ? args[2].AsInteger() : args[2].AsFloat();
        if (lo > hi)
        {
            var tmp = lo;
            lo = hi;
            hi = tmp;
        }
        var result = x < lo ? lo : (x > hi ? hi : x);
        return RuntimeValue.Float(result);
    }

    private static RuntimeValue BuiltInDegToRad(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("degToRad() expects 1 argument");
        var deg = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(deg * Math.PI / 180.0);
    }

    private static RuntimeValue BuiltInRadToDeg(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("radToDeg() expects 1 argument");
        var rad = args[0].Type == MaldaLang.Interpreter.ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(rad * 180.0 / Math.PI);
    }

    private static RuntimeValue BuiltInZeros(List<RuntimeValue> args)
    {
        BuiltInArity.Require("zeros", args, 1, 2, "n, cols?");
        var n = RequireNonNegativeDimension(args[0]);
        if (args.Count == 1)
            return RuntimeValue.Array(ZeroRow(n));

        var cols = RequireNonNegativeDimension(args[1]);
        var rows = new List<RuntimeValue>(n);
        for (var i = 0; i < n; i++)
            rows.Add(RuntimeValue.Array(ZeroRow(cols)));
        return RuntimeValue.Array(rows);
    }

    private static int RequireNonNegativeDimension(RuntimeValue value)
    {
        if (!NumericCoercion.TryAsInteger(value, out var n))
            throw new RuntimeException("zeros() expects integer dimensions");
        if (n < 0)
            throw new RuntimeException("zeros() dimensions must be >= 0");
        return n;
    }

    private static List<RuntimeValue> ZeroRow(int length)
    {
        var row = new List<RuntimeValue>(length);
        for (var i = 0; i < length; i++)
            row.Add(RuntimeValue.Float(0.0));
        return row;
    }

    // LLM-oriented math helpers

    private static RuntimeValue BuiltInRsqrt(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("rsqrt() expects 1 argument");
        var x = args[0].Type == ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        return RuntimeValue.Float(1.0 / Math.Sqrt(x));
    }

    private static RuntimeValue BuiltInRandn(List<RuntimeValue> args)
    {
        if (args.Count > 2) throw new Exception("randn() expects 0-2 arguments: (std?, mean?)");
        var std = 1.0;
        var mean = 0.0;
        if (args.Count >= 1)
            std = args[0].Type == ValueType.Integer ? args[0].AsInteger() : args[0].AsFloat();
        if (args.Count == 2)
            mean = args[1].Type == ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();

        // Box-Muller transform
        var u1 = Math.Max(_random.NextDouble(), 1e-12);
        var u2 = _random.NextDouble();
        var z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return RuntimeValue.Float(mean + z0 * std);
    }

    private static RuntimeValue BuiltInArgmax(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("argmax() expects 1 argument: (array)");
        if (args[0].Type != ValueType.Array) throw new Exception("argmax() expects an array argument");
        var values = args[0].AsArray();
        if (values.Count == 0) throw new Exception("argmax() expects a non-empty array");

        var bestIdx = 0;
        var bestVal = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
        for (var i = 1; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            if (v > bestVal)
            {
                bestVal = v;
                bestIdx = i;
            }
        }
        return RuntimeValue.Integer(bestIdx);
    }

    private static RuntimeValue BuiltInArgmin(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("argmin() expects 1 argument: (array)");
        if (args[0].Type != ValueType.Array) throw new Exception("argmin() expects an array argument");
        var values = args[0].AsArray();
        if (values.Count == 0) throw new Exception("argmin() expects a non-empty array");

        var bestIdx = 0;
        var bestVal = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
        for (var i = 1; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            if (v < bestVal)
            {
                bestVal = v;
                bestIdx = i;
            }
        }
        return RuntimeValue.Integer(bestIdx);
    }

    private static RuntimeValue BuiltInLogSumExp(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("logSumExp() expects 1 argument: (array)");
        if (args[0].Type != ValueType.Array) throw new Exception("logSumExp() expects an array argument");
        var values = args[0].AsArray();
        if (values.Count == 0) throw new Exception("logSumExp() expects a non-empty array");

        var maxVal = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
        for (var i = 1; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            if (v > maxVal) maxVal = v;
        }

        var sumExp = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            sumExp += Math.Exp(v - maxVal);
        }

        return RuntimeValue.Float(maxVal + Math.Log(sumExp));
    }

    private static RuntimeValue BuiltInSoftmax(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("softmax() expects 1-2 arguments: (array, temperature?)");
        if (args[0].Type != ValueType.Array) throw new Exception("softmax() expects an array as first argument");
        var values = args[0].AsArray();
        if (values.Count == 0) throw new Exception("softmax() expects a non-empty array");

        var temperature = 1.0;
        if (args.Count == 2)
            temperature = args[1].Type == ValueType.Integer ? args[1].AsInteger() : args[1].AsFloat();
        if (temperature <= 0.0) throw new Exception("softmax() temperature must be > 0");

        var first = values[0].Type == ValueType.Integer ? values[0].AsInteger() : values[0].AsFloat();
        var maxVal = first / temperature;
        for (var i = 1; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            var scaled = v / temperature;
            if (scaled > maxVal) maxVal = scaled;
        }

        var exps = new double[values.Count];
        var sumExp = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i].Type == ValueType.Integer ? values[i].AsInteger() : values[i].AsFloat();
            var e = Math.Exp((v / temperature) - maxVal);
            exps[i] = e;
            sumExp += e;
        }

        var probs = new List<RuntimeValue>(values.Count);
        for (var i = 0; i < exps.Length; i++)
            probs.Add(RuntimeValue.Float(exps[i] / sumExp));
        return RuntimeValue.Array(probs);
    }

    private static RuntimeValue BuiltInCrossEntropyFromLogits(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("crossEntropyFromLogits() expects 2 arguments: (logits, targetIndex)");
        if (args[0].Type != ValueType.Array) throw new Exception("crossEntropyFromLogits() expects an array as first argument");
        if (!NumericCoercion.TryAsInteger(args[1], out var targetIndex))
            throw new Exception("crossEntropyFromLogits() expects integer targetIndex");

        var logits = args[0].AsArray();
        if (logits.Count == 0) throw new Exception("crossEntropyFromLogits() expects non-empty logits");
        if (targetIndex < 0 || targetIndex >= logits.Count) throw new Exception("crossEntropyFromLogits() targetIndex out of range");

        var maxVal = logits[0].Type == ValueType.Integer ? logits[0].AsInteger() : logits[0].AsFloat();
        for (var i = 1; i < logits.Count; i++)
        {
            var v = logits[i].Type == ValueType.Integer ? logits[i].AsInteger() : logits[i].AsFloat();
            if (v > maxVal) maxVal = v;
        }

        var sumExp = 0.0;
        for (var i = 0; i < logits.Count; i++)
        {
            var v = logits[i].Type == ValueType.Integer ? logits[i].AsInteger() : logits[i].AsFloat();
            sumExp += Math.Exp(v - maxVal);
        }
        var logSumExp = maxVal + Math.Log(sumExp);
        var target = logits[targetIndex].Type == ValueType.Integer ? logits[targetIndex].AsInteger() : logits[targetIndex].AsFloat();
        return RuntimeValue.Float(logSumExp - target);
    }

    private static RuntimeValue BuiltInRandomChoiceWeighted(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("randomChoiceWeighted() expects 1 argument: (weights)");
        if (args[0].Type != ValueType.Array) throw new Exception("randomChoiceWeighted() expects an array argument");
        var weights = args[0].AsArray();
        if (weights.Count == 0) throw new Exception("randomChoiceWeighted() expects a non-empty array");

        var total = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i].Type == ValueType.Integer ? weights[i].AsInteger() : weights[i].AsFloat();
            if (w < 0) throw new Exception("randomChoiceWeighted() weights must be >= 0");
            total += w;
        }
        if (total <= 0.0) throw new Exception("randomChoiceWeighted() sum of weights must be > 0");

        var r = _random.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i].Type == ValueType.Integer ? weights[i].AsInteger() : weights[i].AsFloat();
            cumulative += w;
            if (r <= cumulative)
                return RuntimeValue.Integer(i);
        }
        return RuntimeValue.Integer(weights.Count - 1);
    }

    private static RuntimeValue BuiltInSeed(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("seed() expects 1 argument");
        if (!NumericCoercion.TryAsInteger(args[0], out var seed))
            throw new Exception("seed() expects an integer argument");
        _random = new Random(seed);
        return RuntimeValue.Null();
    }
}
