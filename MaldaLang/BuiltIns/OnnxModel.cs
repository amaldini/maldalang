// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Host-only ONNX inspect + forward. Not a trainer.
/// </summary>
public sealed class OnnxModelInstance : ObjectInstance, IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _modelPath;

    public OnnxModelInstance(string modelPath) : base(null)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new RuntimeException("OnnxModel() expects 1 argument: (path)");

        var resolved = ResolveModelPath(modelPath);
        if (resolved == null || !File.Exists(resolved))
            throw new RuntimeException($"OnnxModel() model file not found: {modelPath}");

        _modelPath = resolved;
        try
        {
            _session = new InferenceSession(resolved);
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"OnnxModel() failed to load '{resolved}': {ex.Message}");
        }
    }

    public string ModelPath => _modelPath;

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "modelPath")
            return RuntimeValue.String(_modelPath);
        if (name is "inputs" or "outputs" or "run")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }

        throw new RuntimeException($"Undefined property '{name}' on OnnxModel.");
    }

    public RuntimeValue inputs() => Inputs();

    public RuntimeValue outputs() => Outputs();

    public RuntimeValue run(object? feeds) =>
        Run(new List<RuntimeValue> { RuntimeHelpersToValue(feeds) });

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "inputs" => Inputs(),
            "outputs" => Outputs(),
            "run" => Run(args),
            _ => throw new RuntimeException($"Undefined method '{methodName}' on OnnxModel.")
        };
    }

    public RuntimeValue Inputs() => Describe(_session.InputMetadata);

    public RuntimeValue Outputs() => Describe(_session.OutputMetadata);

    public RuntimeValue Run(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("OnnxModel.run() expects 1 argument: (feeds)");
        var feeds = ReadFeeds(args[0]);
        if (feeds.Count == 0)
            throw new RuntimeException("OnnxModel.run() expects a non-empty feeds object");

        var named = new List<NamedOnnxValue>();
        foreach (var (name, tensor) in feeds)
        {
            if (!_session.InputMetadata.TryGetValue(name, out var meta))
                throw new RuntimeException($"OnnxModel.run() unknown input '{name}'");
            named.Add(ToNamedValue(name, tensor, meta));
        }

        try
        {
            using var results = _session.Run(named);
            var output = new DictionaryInstance();
            foreach (var result in results)
            {
                output.Set(result.Name, TensorToValue(result));
            }

            return RuntimeValue.Object(output);
        }
        catch (Exception ex) when (ex is not RuntimeException)
        {
            throw new RuntimeException($"OnnxModel.run() failed: {ex.Message}");
        }
    }

    public void Dispose() => _session.Dispose();

    static RuntimeValue Describe(IReadOnlyDictionary<string, NodeMetadata> metadata)
    {
        var list = new List<RuntimeValue>(metadata.Count);
        foreach (var (name, info) in metadata)
        {
            var row = new DictionaryInstance();
            row.Set("name", RuntimeValue.String(name));
            row.Set("type", RuntimeValue.String(info.ElementType.ToString()));
            var shape = new List<RuntimeValue>(info.Dimensions.Length);
            foreach (var dim in info.Dimensions)
                shape.Add(RuntimeValue.Integer(dim));
            row.Set("shape", RuntimeValue.Array(shape));
            list.Add(RuntimeValue.Object(row));
        }

        return RuntimeValue.Array(list);
    }

    static Dictionary<string, TensorValue> ReadFeeds(RuntimeValue value)
    {
        if (value.Type != ValueType.Object)
            throw new RuntimeException("OnnxModel.run() expects a feeds object mapping input names to arrays");

        var obj = value.AsObject();
        var feeds = new Dictionary<string, TensorValue>(StringComparer.Ordinal);
        foreach (var key in obj.GetAllKeys())
        {
            var tensorValue = obj.Get(key, null);
            feeds[key] = ReadTensor(tensorValue, key);
        }

        return feeds;
    }

    static TensorValue ReadTensor(RuntimeValue value, string name)
    {
        if (value.Type != ValueType.Array)
            throw new RuntimeException($"OnnxModel.run() feed '{name}' must be a nested numeric array");

        var items = value.AsArray();
        if (items.Count == 0)
            throw new RuntimeException($"OnnxModel.run() feed '{name}' must be a nested numeric array");

        if (items[0].Type == ValueType.Array)
        {
            var rows = new List<List<double>>(items.Count);
            var width = -1;
            foreach (var rowValue in items)
            {
                if (rowValue.Type != ValueType.Array)
                    throw new RuntimeException($"OnnxModel.run() feed '{name}' must be a rectangular matrix");
                var cells = rowValue.AsArray();
                if (width < 0)
                    width = cells.Count;
                else if (cells.Count != width)
                    throw new RuntimeException($"OnnxModel.run() feed '{name}' must be a rectangular matrix");
                var row = new List<double>(cells.Count);
                foreach (var cell in cells)
                    row.Add(AsNumeric(cell, name));
                rows.Add(row);
            }

            return new TensorValue(new[] { rows.Count, width }, rows.SelectMany(r => r).ToArray());
        }

        var flat = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
            flat[i] = AsNumeric(items[i], name);
        return new TensorValue(new[] { flat.Length }, flat);
    }

    static NamedOnnxValue ToNamedValue(string name, TensorValue tensor, NodeMetadata meta)
    {
        if (meta.ElementType == typeof(long) || meta.ElementType == typeof(int))
        {
            var longs = tensor.Values.Select(v => (long)Math.Truncate(v)).ToArray();
            return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(longs, tensor.Shape));
        }

        var floats = tensor.Values.Select(v => (float)v).ToArray();
        return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(floats, tensor.Shape));
    }

    static RuntimeValue TensorToValue(DisposableNamedOnnxValue result)
    {
        try
        {
            var floats = result.AsEnumerable<float>().ToArray();
            return PackTensor(floats.Select(v => (double)v).ToArray(), result);
        }
        catch
        {
            try
            {
                var longs = result.AsEnumerable<long>().ToArray();
                return PackTensor(longs.Select(v => (double)v).ToArray(), result);
            }
            catch
            {
                var first = result.AsEnumerable<float>().FirstOrDefault();
                return RuntimeValue.Float(first);
            }
        }
    }

    static RuntimeValue PackTensor(double[] values, DisposableNamedOnnxValue result)
    {
        var dims = TryGetDimensions(result);
        if (dims is { Length: 2 } && dims[0] > 0 && dims[1] > 0 && values.Length == dims[0] * dims[1])
        {
            var rows = new List<RuntimeValue>(dims[0]);
            for (var i = 0; i < dims[0]; i++)
            {
                var row = new List<RuntimeValue>(dims[1]);
                for (var j = 0; j < dims[1]; j++)
                    row.Add(RuntimeValue.Float(values[i * dims[1] + j]));
                rows.Add(RuntimeValue.Array(row));
            }

            return RuntimeValue.Array(rows);
        }

        var list = new List<RuntimeValue>(values.Length);
        foreach (var value in values)
            list.Add(RuntimeValue.Float(value));
        return RuntimeValue.Array(list);
    }

    static int[]? TryGetDimensions(DisposableNamedOnnxValue result)
    {
        try
        {
            if (result.Value is Tensor<float> tf)
                return tf.Dimensions.ToArray();
            if (result.Value is Tensor<long> tl)
                return tl.Dimensions.ToArray();
        }
        catch
        {
            // Fall through to a flat list.
        }

        return null;
    }

    static double AsNumeric(RuntimeValue value, string name)
    {
        if (value.Type == ValueType.Integer)
            return value.AsInteger();
        if (value.Type == ValueType.Float)
            return value.AsFloat();
        throw new RuntimeException($"OnnxModel.run() feed '{name}' must be a nested numeric array");
    }

    static string? ResolveModelPath(string modelPath)
    {
        var trimmed = CrossEncoderOnnxModels.ExpandMaldaPath(modelPath);
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;
        if (File.Exists(trimmed))
            return trimmed;
        if (Directory.Exists(trimmed))
        {
            var candidate = Path.Combine(trimmed, "model.onnx");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    static RuntimeValue RuntimeHelpersToValue(object? feeds)
    {
        if (feeds is RuntimeValue runtime)
            return runtime;
        if (feeds is ObjectInstance obj)
            return RuntimeValue.Object(obj);
        throw new RuntimeException("OnnxModel.run() expects a feeds object mapping input names to arrays");
    }

    readonly record struct TensorValue(int[] Shape, double[] Values);
}
