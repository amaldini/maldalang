// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Linear-algebra and activation helpers for the neural-nets kit (<c>math.dot</c> / <c>matmul</c> / …).
/// </summary>
internal static class MathNeural
{
    private const double SigmoidClamp = 20.0;

    public static RuntimeValue Dot(List<RuntimeValue> args)
    {
        BuiltInArity.Require("dot", args, 2, 2, "a, b");
        var a = RequireVector("dot", args[0], "first");
        var b = RequireVector("dot", args[1], "second");
        if (a.Count == 0 || b.Count == 0)
            throw new RuntimeException("dot() expects two non-empty numeric vectors of the same length");
        if (a.Count != b.Count)
            throw new RuntimeException("dot() expects two non-empty numeric vectors of the same length");
        var sum = 0.0;
        for (var i = 0; i < a.Count; i++)
            sum += a[i] * b[i];
        return RuntimeValue.Float(sum);
    }

    public static RuntimeValue MatMul(List<RuntimeValue> args)
    {
        BuiltInArity.Require("matmul", args, 2, 2, "a, b");
        var leftIsMatrix = IsMatrix(args[0]);
        var rightIsMatrix = IsMatrix(args[1]);
        if (!leftIsMatrix && !IsVectorValue(args[0]))
            throw new RuntimeException("matmul() expects numeric vectors or 2D matrices");
        if (!rightIsMatrix && !IsVectorValue(args[1]))
            throw new RuntimeException("matmul() expects numeric vectors or 2D matrices");

        if (leftIsMatrix && rightIsMatrix)
        {
            var a = RequireMatrix("matmul", args[0], "first");
            var b = RequireMatrix("matmul", args[1], "second");
            if (a.Count == 0 || b.Count == 0 || a[0].Count == 0 || b[0].Count == 0)
                throw new RuntimeException("matmul() expects non-empty matrices");
            if (a[0].Count != b.Count)
                throw new RuntimeException("matmul() inner dimensions must match");
            var rows = a.Count;
            var inner = b.Count;
            var cols = b[0].Count;
            var outRows = new List<RuntimeValue>(rows);
            for (var i = 0; i < rows; i++)
            {
                var row = new List<RuntimeValue>(cols);
                for (var j = 0; j < cols; j++)
                {
                    var sum = 0.0;
                    for (var k = 0; k < inner; k++)
                        sum += a[i][k] * b[k][j];
                    row.Add(RuntimeValue.Float(sum));
                }
                outRows.Add(RuntimeValue.Array(row));
            }
            return RuntimeValue.Array(outRows);
        }

        if (leftIsMatrix)
        {
            var a = RequireMatrix("matmul", args[0], "first");
            var x = RequireVector("matmul", args[1], "second");
            if (a.Count == 0 || a[0].Count == 0 || x.Count == 0)
                throw new RuntimeException("matmul() expects non-empty matrices");
            if (a[0].Count != x.Count)
                throw new RuntimeException("matmul() inner dimensions must match");
            var result = new List<RuntimeValue>(a.Count);
            for (var i = 0; i < a.Count; i++)
            {
                var sum = 0.0;
                for (var k = 0; k < x.Count; k++)
                    sum += a[i][k] * x[k];
                result.Add(RuntimeValue.Float(sum));
            }
            return RuntimeValue.Array(result);
        }

        var v = RequireVector("matmul", args[0], "first");
        var m = RequireMatrix("matmul", args[1], "second");
        if (v.Count == 0 || m.Count == 0 || m[0].Count == 0)
            throw new RuntimeException("matmul() expects non-empty matrices");
        if (v.Count != m.Count)
            throw new RuntimeException("matmul() inner dimensions must match");
        var colsOut = m[0].Count;
        var rowOut = new List<RuntimeValue>(colsOut);
        for (var j = 0; j < colsOut; j++)
        {
            var sum = 0.0;
            for (var k = 0; k < v.Count; k++)
                sum += v[k] * m[k][j];
            rowOut.Add(RuntimeValue.Float(sum));
        }
        return RuntimeValue.Array(rowOut);
    }

    public static RuntimeValue Transpose(List<RuntimeValue> args)
    {
        BuiltInArity.Require("transpose", args, 1, 1, "matrix");
        var matrix = RequireMatrix("transpose", args[0], "first");
        if (matrix.Count == 0)
            return RuntimeValue.Array(new List<RuntimeValue>());
        var cols = matrix[0].Count;
        var outRows = new List<RuntimeValue>(cols);
        for (var j = 0; j < cols; j++)
        {
            var row = new List<RuntimeValue>(matrix.Count);
            for (var i = 0; i < matrix.Count; i++)
                row.Add(RuntimeValue.Float(matrix[i][j]));
            outRows.Add(RuntimeValue.Array(row));
        }
        return RuntimeValue.Array(outRows);
    }

    public static RuntimeValue Relu(List<RuntimeValue> args)
    {
        BuiltInArity.Require("relu", args, 1, 1, "x");
        return MapNumeric("relu", args[0], static x => x > 0.0 ? x : 0.0);
    }

    public static RuntimeValue Sigmoid(List<RuntimeValue> args)
    {
        BuiltInArity.Require("sigmoid", args, 1, 1, "x");
        return MapNumeric("sigmoid", args[0], SigmoidScalar);
    }

    public static RuntimeValue Tanh(List<RuntimeValue> args)
    {
        BuiltInArity.Require("tanh", args, 1, 1, "x");
        return MapNumeric("tanh", args[0], static x => Math.Tanh(x));
    }

    public static RuntimeValue Mse(List<RuntimeValue> args)
    {
        BuiltInArity.Require("mse", args, 2, 2, "pred, target");
        if (IsNumeric(args[0]) && IsNumeric(args[1]))
        {
            var d = AsNumeric("mse", args[0]) - AsNumeric("mse", args[1]);
            return RuntimeValue.Float(d * d);
        }

        var pred = RequireVector("mse", args[0], "first");
        var target = RequireVector("mse", args[1], "second");
        if (pred.Count == 0 || target.Count == 0)
            throw new RuntimeException("mse() expects two non-empty numeric vectors of the same length");
        if (pred.Count != target.Count)
            throw new RuntimeException("mse() expects two non-empty numeric vectors of the same length");
        var sum = 0.0;
        for (var i = 0; i < pred.Count; i++)
        {
            var d = pred[i] - target[i];
            sum += d * d;
        }
        return RuntimeValue.Float(sum / pred.Count);
    }

    private static double SigmoidScalar(double x)
    {
        if (x < -SigmoidClamp)
            return 0.0;
        if (x > SigmoidClamp)
            return 1.0;
        return 1.0 / (1.0 + Math.Exp(-x));
    }

    private static RuntimeValue MapNumeric(string name, RuntimeValue value, Func<double, double> fn)
    {
        if (IsNumeric(value))
            return RuntimeValue.Float(fn(AsNumeric(name, value)));
        if (IsMatrix(value))
        {
            var matrix = RequireMatrix(name, value, "first");
            var rows = new List<RuntimeValue>(matrix.Count);
            foreach (var src in matrix)
            {
                var row = new List<RuntimeValue>(src.Count);
                foreach (var cell in src)
                    row.Add(RuntimeValue.Float(fn(cell)));
                rows.Add(RuntimeValue.Array(row));
            }
            return RuntimeValue.Array(rows);
        }

        if (IsVectorValue(value))
        {
            var vector = RequireVector(name, value, "first");
            var mapped = new List<RuntimeValue>(vector.Count);
            foreach (var cell in vector)
                mapped.Add(RuntimeValue.Float(fn(cell)));
            return RuntimeValue.Array(mapped);
        }

        throw new RuntimeException($"{name}() expects a number or a numeric array");
    }

    private static bool IsNumeric(RuntimeValue value) =>
        value.Type is ValueType.Integer or ValueType.Float;

    private static bool IsVectorValue(RuntimeValue value)
    {
        if (value.Type != ValueType.Array)
            return false;
        var items = value.AsArray();
        if (items.Count == 0)
            return true;
        return IsNumeric(items[0]);
    }

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

    private static List<double> RequireVector(string name, RuntimeValue value, string which)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"{name}() expects a numeric vector as {which} argument");
        var items = value.AsArray();
        var vector = new List<double>(items.Count);
        foreach (var item in items)
        {
            if (!IsNumeric(item))
                throw new RuntimeException($"{name}() expects numeric values");
            vector.Add(AsNumeric(name, item));
        }
        return vector;
    }

    private static List<List<double>> RequireMatrix(string name, RuntimeValue value, string which)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"{name}() expects a 2D numeric matrix as {which} argument");
        var rows = value.AsArray();
        var matrix = new List<List<double>>(rows.Count);
        var width = -1;
        foreach (var rowValue in rows)
        {
            if (rowValue.Type != ValueType.Array)
                throw new RuntimeException($"{name}() expects a 2D numeric matrix as {which} argument");
            var cells = rowValue.AsArray();
            if (width < 0)
                width = cells.Count;
            else if (cells.Count != width)
                throw new RuntimeException($"{name}() matrix rows must have the same length");
            var row = new List<double>(cells.Count);
            foreach (var cell in cells)
            {
                if (!IsNumeric(cell))
                    throw new RuntimeException($"{name}() expects numeric values");
                row.Add(AsNumeric(name, cell));
            }
            matrix.Add(row);
        }
        return matrix;
    }
}
