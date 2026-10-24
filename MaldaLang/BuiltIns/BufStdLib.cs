// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Buffers.Binary;
using System.Text;
using MaldaLang.Interpreter;

/// <summary>
/// <c>buf.*</c>: fixed-size numeric buffers (<see cref="TypedBufferInstance"/>) and binary data.
/// The interpreter and C# transpile both call <see cref="Call"/>; JavaScript has the same
/// functions in <c>mlRuntime.buf</c> over typed arrays. Binary reads and writes are
/// little-endian.
/// </summary>
public static class BufStdLib
{
    /// <summary>Largest element count a constructor accepts (2^28): a guard against a mistyped size.</summary>
    public const int MaxLength = 268435456;

    private const string BinaryTypes = "int8, uint8, int16, uint16, int32, uint32, float32, or float64";

    public static RuntimeValue Call(string methodName, List<RuntimeValue> args) =>
        methodName switch
        {
            "float64" => Float64(args),
            "float32" => Float32(args),
            "int32" => Int32(args),
            "bytes" => Bytes(args),
            "from" => From(args),
            "toArray" => ToArray(args),
            "kind" => Kind(args),
            "slice" => Slice(args),
            "fill" => Fill(args),
            "readAt" => ReadAt(args),
            "writeAt" => WriteAt(args),
            "fromUtf8" => FromUtf8(args),
            "toUtf8" => ToUtf8(args),
            "fromBase64" => FromBase64(args),
            "toBase64" => ToBase64(args),
            _ => throw new RuntimeException($"Unknown buf method: {methodName}")
        };

    private static RuntimeValue Float64(List<RuntimeValue> args)
    {
        BuiltInArity.Require("float64", args, 1, 1, "length");
        return Create("float64", TypedBufferKind.Float64, args[0]);
    }

    private static RuntimeValue Float32(List<RuntimeValue> args)
    {
        BuiltInArity.Require("float32", args, 1, 1, "length");
        return Create("float32", TypedBufferKind.Float32, args[0]);
    }

    private static RuntimeValue Int32(List<RuntimeValue> args)
    {
        BuiltInArity.Require("int32", args, 1, 1, "length");
        return Create("int32", TypedBufferKind.Int32, args[0]);
    }

    private static RuntimeValue Bytes(List<RuntimeValue> args)
    {
        BuiltInArity.Require("bytes", args, 1, 1, "length");
        return Create("bytes", TypedBufferKind.Uint8, args[0]);
    }

    private static RuntimeValue Create(string name, TypedBufferKind kind, RuntimeValue length) =>
        RuntimeValue.Object(new TypedBufferInstance(kind, RequireLength(name, length)));

    private static int RequireLength(string name, RuntimeValue value)
    {
        if (!NumericCoercion.TryAsInteger(value, out var length) || length < 0 || length > MaxLength)
            throw new RuntimeException($"buf.{name}() length must be an integer from 0 to {MaxLength}.");
        return length;
    }

    private static RuntimeValue From(List<RuntimeValue> args)
    {
        BuiltInArity.Require("from", args, 1, 2, "values, kind?");
        var kind = args.Count > 1 ? ParseKind(args[1]) : TypedBufferKind.Float64;
        TypedBufferInstance result;
        if (args[0].Type == ValueType.Object && args[0].AsObject() is TypedBufferInstance source)
        {
            result = new TypedBufferInstance(kind, source.Length);
            for (var i = 0; i < source.Length; i++)
                result.SetNumber(i, source.GetNumber(i));
            return RuntimeValue.Object(result);
        }

        if (args[0].Type != ValueType.Array)
            throw new RuntimeException("buf.from() expects an array or a buffer.");
        var values = args[0].AsArray();
        result = new TypedBufferInstance(kind, values.Count);
        for (var i = 0; i < values.Count; i++)
            result.SetNumber(i, result.RequireNumber(values[i]));
        return RuntimeValue.Object(result);
    }

    private static TypedBufferKind ParseKind(RuntimeValue value) =>
        (value.Type == ValueType.String ? value.AsString() : null) switch
        {
            "float64" => TypedBufferKind.Float64,
            "float32" => TypedBufferKind.Float32,
            "int32" => TypedBufferKind.Int32,
            "uint8" or "bytes" => TypedBufferKind.Uint8,
            _ => throw new RuntimeException("buf.from() kind must be \"float64\", \"float32\", \"int32\" or \"uint8\".")
        };

    public static TypedBufferInstance RequireBuffer(string name, RuntimeValue value)
    {
        if (value.Type == ValueType.Object && value.AsObject() is TypedBufferInstance buffer)
            return buffer;
        throw new RuntimeException($"buf.{name}() expects a buffer.");
    }

    private static RuntimeValue ToArray(List<RuntimeValue> args)
    {
        BuiltInArity.Require("toArray", args, 1, 1, "buffer");
        return RuntimeValue.Array(RequireBuffer("toArray", args[0]).ToList());
    }

    private static RuntimeValue Kind(List<RuntimeValue> args)
    {
        BuiltInArity.Require("kind", args, 1, 1, "buffer");
        return RuntimeValue.String(TypedBufferInstance.KindName(RequireBuffer("kind", args[0]).Kind));
    }

    private static RuntimeValue Slice(List<RuntimeValue> args)
    {
        BuiltInArity.Require("slice", args, 2, 3, "buffer, start, end?");
        var buffer = RequireBuffer("slice", args[0]);
        var start = RequireInteger("slice", args[1]);
        var end = args.Count > 2 && args[2].Type != ValueType.Null ? RequireInteger("slice", args[2]) : buffer.Length;
        return RuntimeValue.Object(buffer.Slice(start, end));
    }

    private static RuntimeValue Fill(List<RuntimeValue> args)
    {
        BuiltInArity.Require("fill", args, 2, 2, "buffer, value");
        var buffer = RequireBuffer("fill", args[0]);
        var value = buffer.RequireNumber(args[1]);
        for (var i = 0; i < buffer.Length; i++)
            buffer.SetNumber(i, value);
        return args[0];
    }

    private static int RequireInteger(string name, RuntimeValue value)
    {
        if (!NumericCoercion.TryAsInteger(value, out var result))
            throw new RuntimeException($"buf.{name}() expects an integer offset.");
        return result;
    }

    private static int BinarySize(string type) => type switch
    {
        "int8" or "uint8" => 1,
        "int16" or "uint16" => 2,
        "int32" or "uint32" or "float32" => 4,
        "float64" => 8,
        _ => throw new RuntimeException($"buf binary type must be {BinaryTypes}.")
    };

    private static Span<byte> BinarySpan(string name, List<RuntimeValue> args, out string type)
    {
        var bytes = RequireBuffer(name, args[0]).Bytes;
        var offset = RequireInteger(name, args[1]);
        type = args[2].Type == ValueType.String ? args[2].AsString() : string.Empty;
        var size = BinarySize(type);
        if (offset < 0 || offset + size > bytes.Length)
            throw new RuntimeException($"buf.{name}() of {type} at offset {offset} is outside the buffer (length {bytes.Length}).");
        return bytes.AsSpan(offset, size);
    }

    private static RuntimeValue ReadAt(List<RuntimeValue> args)
    {
        BuiltInArity.Require("readAt", args, 3, 3, "bytes, offset, type");
        var span = BinarySpan("readAt", args, out var type);
        return type switch
        {
            "int8" => RuntimeValue.Integer(unchecked((sbyte)span[0])),
            "uint8" => RuntimeValue.Integer(span[0]),
            "int16" => RuntimeValue.Integer(BinaryPrimitives.ReadInt16LittleEndian(span)),
            "uint16" => RuntimeValue.Integer(BinaryPrimitives.ReadUInt16LittleEndian(span)),
            "int32" => RuntimeValue.Integer(BinaryPrimitives.ReadInt32LittleEndian(span)),
            // MALDA ints are 32-bit: a uint32 above int.MaxValue is a float.
            "uint32" => BinaryPrimitives.ReadUInt32LittleEndian(span) is var u && u <= int.MaxValue
                ? RuntimeValue.Integer((int)u)
                : RuntimeValue.Float(BinaryPrimitives.ReadUInt32LittleEndian(span)),
            "float32" => RuntimeValue.Float(BinaryPrimitives.ReadSingleLittleEndian(span)),
            _ => RuntimeValue.Float(BinaryPrimitives.ReadDoubleLittleEndian(span))
        };
    }

    private static RuntimeValue WriteAt(List<RuntimeValue> args)
    {
        BuiltInArity.Require("writeAt", args, 4, 4, "bytes, offset, type, value");
        var span = BinarySpan("writeAt", args, out var type);
        var value = args[3].Type switch
        {
            ValueType.Integer => args[3].AsInteger(),
            ValueType.Float => args[3].AsFloat(),
            _ => throw new RuntimeException("buf.writeAt() value must be a number.")
        };
        var bits = TypedBufferInstance.ToUint32(value);
        switch (type)
        {
            case "int8":
            case "uint8":
                span[0] = (byte)bits;
                break;
            case "int16":
            case "uint16":
                BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)bits);
                break;
            case "int32":
            case "uint32":
                BinaryPrimitives.WriteUInt32LittleEndian(span, bits);
                break;
            case "float32":
                BinaryPrimitives.WriteSingleLittleEndian(span, (float)value);
                break;
            default:
                BinaryPrimitives.WriteDoubleLittleEndian(span, value);
                break;
        }

        return args[0];
    }

    private static RuntimeValue FromUtf8(List<RuntimeValue> args)
    {
        BuiltInArity.Require("fromUtf8", args, 1, 1, "text");
        if (args[0].Type != ValueType.String)
            throw new RuntimeException("buf.fromUtf8() expects a string.");
        return RuntimeValue.Object(new TypedBufferInstance(Encoding.UTF8.GetBytes(args[0].AsString())));
    }

    private static RuntimeValue ToUtf8(List<RuntimeValue> args)
    {
        BuiltInArity.Require("toUtf8", args, 1, 1, "bytes");
        return RuntimeValue.String(Encoding.UTF8.GetString(RequireBuffer("toUtf8", args[0]).Bytes));
    }

    private static RuntimeValue FromBase64(List<RuntimeValue> args)
    {
        BuiltInArity.Require("fromBase64", args, 1, 1, "text");
        if (args[0].Type != ValueType.String)
            throw new RuntimeException("buf.fromBase64() expects a string.");
        try
        {
            return RuntimeValue.Object(new TypedBufferInstance(Convert.FromBase64String(args[0].AsString())));
        }
        catch (FormatException)
        {
            throw new RuntimeException("buf.fromBase64() text is not valid base64.");
        }
    }

    private static RuntimeValue ToBase64(List<RuntimeValue> args)
    {
        BuiltInArity.Require("toBase64", args, 1, 1, "bytes");
        return RuntimeValue.String(Convert.ToBase64String(RequireBuffer("toBase64", args[0]).Bytes));
    }
}
