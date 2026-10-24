// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>Element type of a <see cref="TypedBufferInstance"/>.</summary>
public enum TypedBufferKind
{
    Float64,
    Float32,
    Int32,
    Uint8
}

/// <summary>
/// A fixed-size numeric buffer made by <c>buf.float64 / float32 / int32 / bytes</c>. The same
/// object backs the interpreter and C# transpile (so <c>buf.writeAt</c> and <c>b[i] = v</c> change
/// one storage); JavaScript uses the matching typed array. Indexing reads and writes like an
/// array, but the length is fixed: an index out of range is an error on read and on write, and a
/// written value must be a number, converted to the element type (float32 rounds, int32 and
/// uint8 wrap modulo 2^32 / 2^8 after truncation, like JavaScript typed arrays).
/// </summary>
public sealed class TypedBufferInstance : ObjectInstance
{
    private readonly double[]? _float64;
    private readonly float[]? _float32;
    private readonly int[]? _int32;
    private readonly byte[]? _bytes;

    /// <summary>The error for any member but <c>length</c>, on every backend.</summary>
    public const string NoArrayMethodsMessage =
        "A buffer has a fixed length and no array methods (append, map, ...). Use buf.toArray(b) for an array.";

    public TypedBufferKind Kind { get; }
    public int Length { get; }

    public TypedBufferInstance(TypedBufferKind kind, int length) : base(null)
    {
        Kind = kind;
        Length = length;
        switch (kind)
        {
            case TypedBufferKind.Float64: _float64 = new double[length]; break;
            case TypedBufferKind.Float32: _float32 = new float[length]; break;
            case TypedBufferKind.Int32: _int32 = new int[length]; break;
            default: _bytes = new byte[length]; break;
        }
    }

    /// <summary>A bytes buffer over <paramref name="bytes"/> (not copied).</summary>
    public TypedBufferInstance(byte[] bytes) : base(null)
    {
        Kind = TypedBufferKind.Uint8;
        Length = bytes.Length;
        _bytes = bytes;
    }

    /// <summary>The backing bytes of a <c>buf.bytes</c> buffer.</summary>
    public byte[] Bytes => _bytes ?? throw new RuntimeException($"Expected a bytes buffer (buf.bytes), got a {KindName(Kind)} buffer.");

    public static string KindName(TypedBufferKind kind) => kind switch
    {
        TypedBufferKind.Float64 => "float64",
        TypedBufferKind.Float32 => "float32",
        TypedBufferKind.Int32 => "int32",
        _ => "uint8"
    };

    public RuntimeValue GetAt(int index)
    {
        CheckIndex(index);
        return Kind switch
        {
            TypedBufferKind.Float64 => RuntimeValue.Float(_float64![index]),
            TypedBufferKind.Float32 => RuntimeValue.Float(_float32![index]),
            TypedBufferKind.Int32 => RuntimeValue.Integer(_int32![index]),
            _ => RuntimeValue.Integer(_bytes![index])
        };
    }

    public double GetNumber(int index)
    {
        CheckIndex(index);
        return Kind switch
        {
            TypedBufferKind.Float64 => _float64![index],
            TypedBufferKind.Float32 => _float32![index],
            TypedBufferKind.Int32 => _int32![index],
            _ => _bytes![index]
        };
    }

    public void SetAt(int index, RuntimeValue value)
    {
        CheckIndex(index);
        SetNumber(index, RequireNumber(value));
    }

    /// <summary>Stores <paramref name="value"/> converted to the element type; the index is already checked.</summary>
    public void SetNumber(int index, double value)
    {
        switch (Kind)
        {
            case TypedBufferKind.Float64: _float64![index] = value; break;
            case TypedBufferKind.Float32: _float32![index] = (float)value; break;
            case TypedBufferKind.Int32: _int32![index] = ToInt32(value); break;
            default: _bytes![index] = (byte)ToUint32(value); break;
        }
    }

    public double RequireNumber(RuntimeValue value) => value.Type switch
    {
        ValueType.Integer => value.AsInteger(),
        ValueType.Float => value.AsFloat(),
        _ => throw new RuntimeException($"A {KindName(Kind)} buffer holds numbers, not {TypeLabel(value)}.")
    };

    private static string TypeLabel(RuntimeValue value) => value.Type switch
    {
        ValueType.String => "a string",
        ValueType.Boolean => "a bool",
        ValueType.Null => "null",
        ValueType.Array => "an array",
        _ => "an object"
    };

    public void CheckIndex(int index)
    {
        if (index < 0 || index >= Length)
            throw new RuntimeException($"Buffer index {index} is out of range (length {Length}).");
    }

    /// <summary>ECMAScript ToInt32: NaN and infinities are 0, then truncate and wrap modulo 2^32.</summary>
    public static int ToInt32(double value) => unchecked((int)ToUint32(value));

    public static uint ToUint32(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;
        var wrapped = Math.Truncate(value) % 4294967296.0;
        if (wrapped < 0)
            wrapped += 4294967296.0;
        return (uint)wrapped;
    }

    public List<RuntimeValue> ToList()
    {
        var list = new List<RuntimeValue>(Length);
        for (var i = 0; i < Length; i++)
            list.Add(GetAt(i));
        return list;
    }

    public TypedBufferInstance Slice(int start, int end)
    {
        if (start < 0 || end < start || end > Length)
            throw new RuntimeException($"buf.slice range {start}..{end} is outside the buffer (length {Length}).");
        var copy = new TypedBufferInstance(Kind, end - start);
        for (var i = start; i < end; i++)
            copy.SetNumber(i - start, GetNumber(i));
        return copy;
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "length")
            return RuntimeValue.Integer(Length);
        throw new RuntimeException(NoArrayMethodsMessage);
    }

    public override string ToString() =>
        "[" + string.Join(", ", ToList().Select(value => value.ToString())) + "]";
}
