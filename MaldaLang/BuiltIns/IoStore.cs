// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MaldaLang.Interpreter;

/// <summary>
/// <c>io.store*</c>: a small persistent key-value store, and the browser file calls
/// (<c>io.openFile</c>, <c>io.saveFile</c>, <c>io.downloadFile</c>, <c>io.onFileDrop</c>).
/// The interpreter and C# transpile keep the store under <c>./.malda/store/</c> (one JSON file per
/// key; <c>MALDA_STORE_DIR</c> moves it); JavaScript keeps it in IndexedDB. The value format is
/// the same everywhere: JSON, with tagged objects for dicts, object literals, variants and
/// buffers, so a value reads back as the type it was stored as. The file calls need a browser
/// and stop with an explanation here.
/// </summary>
public static class IoStore
{
    public const int MaxKeyLength = 256;

    private const string Accepts = "null, bools, numbers, strings, arrays, dicts, variants and buffers";
    private static readonly object Gate = new();

    public static RuntimeValue Call(string methodName, List<RuntimeValue> args) =>
        methodName switch
        {
            "storeGet" => Get(args),
            "storeSet" => Set(args),
            "storeRemove" => Remove(args),
            "storeKeys" => Keys(args),
            "openFile" => BrowserOnly(args, () => BuiltInArity.Require("openFile", args, 0, 1, "options?"), "openFile"),
            "saveFile" => BrowserOnly(args, () => BuiltInArity.Require("saveFile", args, 2, 3, "fileOrName, data, options?"), "saveFile"),
            "downloadFile" => BrowserOnly(args, () => BuiltInArity.Require("downloadFile", args, 2, 3, "name, data, type?"), "downloadFile"),
            "onFileDrop" => BrowserOnly(args, () => BuiltInArity.Require("onFileDrop", args, 2, 3, "target, callback, options?"), "onFileDrop"),
            _ => throw new RuntimeException($"Unknown io method: {methodName}")
        };

    private static RuntimeValue BrowserOnly(List<RuntimeValue> args, Action checkArity, string methodName)
    {
        checkArity();
        throw new RuntimeException(BrowserOnlyMessage(methodName));
    }

    /// <summary>Why a browser file call stops on the interpreter and C# (one wording for both).</summary>
    public static string BrowserOnlyMessage(string methodName)
    {
        var what = methodName switch
        {
            "openFile" => "opens the browser's file picker",
            "saveFile" => "saves through the browser (back to an opened file, a save dialog or a download)",
            "downloadFile" => "downloads a file from the page",
            _ => "receives files dropped on the page"
        };
        return $"io.{methodName}() {what} and runs only on the JavaScript backend (malda compile <file> --mode js). On the interpreter and C#, read and write files with io.readFile / io.writeFile.";
    }

    /// <summary>The folder of the host store: <c>MALDA_STORE_DIR</c>, or <c>.malda/store</c> under the current directory.</summary>
    public static string Directory
    {
        get
        {
            var overridePath = System.Environment.GetEnvironmentVariable("MALDA_STORE_DIR");
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(System.Environment.CurrentDirectory, ".malda", "store")
                : overridePath;
        }
    }

    private static RuntimeValue Get(List<RuntimeValue> args)
    {
        BuiltInArity.Require("storeGet", args, 1, 2, "key, fallback?");
        var key = RequireKey("storeGet", args[0]);
        lock (Gate)
        {
            var path = PathFor(key);
            if (!File.Exists(path))
                return args.Count > 1 ? args[1] : RuntimeValue.Null();
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            return Decode(document.RootElement.GetProperty("value"));
        }
    }

    private static RuntimeValue Set(List<RuntimeValue> args)
    {
        BuiltInArity.Require("storeSet", args, 2, 2, "key, value");
        var key = RequireKey("storeSet", args[0]);
        var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteString("key", key);
            writer.WritePropertyName("value");
            Encode(writer, args[1], new HashSet<object>(ReferenceEqualityComparer.Instance));
            writer.WriteEndObject();
        }

        lock (Gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = PathFor(key);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, json.ToArray());
            File.Move(temp, path, overwrite: true);
        }
        return RuntimeValue.Null();
    }

    private static RuntimeValue Remove(List<RuntimeValue> args)
    {
        BuiltInArity.Require("storeRemove", args, 1, 1, "key");
        var key = RequireKey("storeRemove", args[0]);
        lock (Gate)
        {
            var path = PathFor(key);
            if (!File.Exists(path))
                return RuntimeValue.Boolean(false);
            File.Delete(path);
            return RuntimeValue.Boolean(true);
        }
    }

    private static RuntimeValue Keys(List<RuntimeValue> args)
    {
        BuiltInArity.Require("storeKeys", args, 0, 0, "");
        lock (Gate)
        {
            if (!System.IO.Directory.Exists(Directory))
                return RuntimeValue.Array(new List<RuntimeValue>());
            var keys = new List<string>();
            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                keys.Add(document.RootElement.GetProperty("key").GetString() ?? string.Empty);
            }
            keys.Sort(StringComparer.Ordinal);
            return RuntimeValue.Array(keys.Select(RuntimeValue.String).ToList());
        }
    }

    private static string RequireKey(string name, RuntimeValue value)
    {
        if (value.Type != ValueType.String || value.AsString().Length == 0 || value.AsString().Length > MaxKeyLength)
            throw new RuntimeException($"io.{name}() key must be a non-empty string of at most {MaxKeyLength} characters.");
        return value.AsString();
    }

    /// <summary>One file per key, named by a hash so any key is a safe (and case-distinct) file name.</summary>
    private static string PathFor(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24].ToLowerInvariant();
        return Path.Combine(Directory, hash + ".json");
    }

    private static RuntimeException CannotStore(string what) =>
        new($"{what} cannot be stored: io.storeSet() takes {Accepts}.");

    private static void Encode(Utf8JsonWriter writer, RuntimeValue value, HashSet<object> open)
    {
        switch (value.Type)
        {
            case ValueType.Null:
                writer.WriteNullValue();
                return;
            case ValueType.Boolean:
                writer.WriteBooleanValue(value.AsBoolean());
                return;
            case ValueType.Integer:
                writer.WriteNumberValue(value.AsInteger());
                return;
            case ValueType.Float:
                WriteFloat(writer, value.AsFloat());
                return;
            case ValueType.String:
                writer.WriteStringValue(value.AsString());
                return;
            case ValueType.Function:
            case ValueType.Prompt:
            case ValueType.Class:
            case ValueType.Task:
                throw CannotStore(WorkerBoundary.Function);
            case ValueType.ActorReference:
            case ValueType.Actor:
                throw CannotStore(WorkerBoundary.ActorReference);
        }

        var identity = value.Value!;
        if (!open.Add(identity))
            throw new RuntimeException("io.storeSet() cannot store a value that contains itself.");
        try
        {
            switch (value.Type)
            {
                case ValueType.Array:
                    writer.WriteStartArray();
                    foreach (var item in value.AsArray())
                        Encode(writer, item, open);
                    writer.WriteEndArray();
                    return;
                case ValueType.Variant:
                {
                    var variant = value.AsVariant();
                    writer.WriteStartObject();
                    writer.WriteString("$t", "variant");
                    writer.WriteString("tag", variant.Tag);
                    writer.WritePropertyName("v");
                    writer.WriteStartArray();
                    foreach (var item in variant.Payload)
                        Encode(writer, item, open);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    return;
                }
            }

            switch (value.AsObject())
            {
                case ArrayInstance array:
                    writer.WriteStartArray();
                    foreach (var item in array.Elements)
                        Encode(writer, item, open);
                    writer.WriteEndArray();
                    return;
                case TypedBufferInstance buffer:
                    writer.WriteStartObject();
                    writer.WriteString("$t", "buffer");
                    writer.WriteString("kind", TypedBufferInstance.KindName(buffer.Kind));
                    writer.WriteString("b64", Convert.ToBase64String(BufferBytes(buffer)));
                    writer.WriteEndObject();
                    return;
                case DictionaryInstance dict:
                    WriteTagged(writer, "dict", dict.Entries, open);
                    return;
                case JsonObject literal:
                    WriteTagged(writer, "object", literal.GetProperties(), open);
                    return;
                default:
                    throw CannotStore(WorkerBoundary.ObjectWithMethods);
            }
        }
        finally
        {
            open.Remove(identity);
        }
    }

    private static void WriteTagged(Utf8JsonWriter writer, string tag, IEnumerable<KeyValuePair<string, RuntimeValue>> entries, HashSet<object> open)
    {
        writer.WriteStartObject();
        writer.WriteString("$t", tag);
        writer.WritePropertyName("v");
        writer.WriteStartObject();
        foreach (var (key, item) in entries.ToList())
        {
            writer.WritePropertyName(key);
            Encode(writer, item, open);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>A float keeps its fraction mark ("3.0") so it reads back as a float; NaN and infinities are tagged.</summary>
    private static void WriteFloat(Utf8JsonWriter writer, double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            writer.WriteStartObject();
            writer.WriteString("$t", "number");
            writer.WriteString("v", double.IsNaN(number) ? "NaN" : number > 0 ? "Infinity" : "-Infinity");
            writer.WriteEndObject();
            return;
        }
        var text = number.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E') && !text.Contains('e'))
            text += ".0";
        writer.WriteRawValue(text);
    }

    /// <summary>The buffer's elements as little-endian bytes (the layout JavaScript typed arrays use).</summary>
    private static byte[] BufferBytes(TypedBufferInstance buffer)
    {
        if (buffer.Kind == TypedBufferKind.Uint8)
            return buffer.Bytes.ToArray();
        var size = buffer.Kind == TypedBufferKind.Float64 ? 8 : 4;
        var bytes = new byte[buffer.Length * size];
        for (var i = 0; i < buffer.Length; i++)
        {
            var span = bytes.AsSpan(i * size, size);
            switch (buffer.Kind)
            {
                case TypedBufferKind.Float64: BinaryPrimitives.WriteDoubleLittleEndian(span, buffer.GetNumber(i)); break;
                case TypedBufferKind.Float32: BinaryPrimitives.WriteSingleLittleEndian(span, (float)buffer.GetNumber(i)); break;
                default: BinaryPrimitives.WriteInt32LittleEndian(span, (int)buffer.GetNumber(i)); break;
            }
        }
        return bytes;
    }

    private static RuntimeValue Decode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return RuntimeValue.Null();
            case JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            case JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            case JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? string.Empty);
            case JsonValueKind.Number:
            {
                var raw = element.GetRawText();
                if (raw.IndexOfAny(['.', 'e', 'E']) < 0 && element.TryGetInt32(out var integer))
                    return RuntimeValue.Integer(integer);
                return RuntimeValue.Float(element.GetDouble());
            }
            case JsonValueKind.Array:
                return RuntimeValue.Array(element.EnumerateArray().Select(Decode).ToList());
        }

        var tag = element.TryGetProperty("$t", out var tagElement) ? tagElement.GetString() : null;
        switch (tag)
        {
            case "dict":
            {
                var entries = new Dictionary<string, RuntimeValue>();
                foreach (var property in element.GetProperty("v").EnumerateObject())
                    entries[property.Name] = Decode(property.Value);
                return RuntimeValue.Object(new DictionaryInstance(entries));
            }
            case "object":
            {
                var literal = new JsonObject();
                foreach (var property in element.GetProperty("v").EnumerateObject())
                    literal.Set(property.Name, Decode(property.Value));
                return RuntimeValue.Object(literal);
            }
            case "variant":
                return RuntimeValue.Variant(
                    element.GetProperty("tag").GetString() ?? string.Empty,
                    element.GetProperty("v").EnumerateArray().Select(Decode).ToList());
            case "number":
                return RuntimeValue.Float(element.GetProperty("v").GetString() switch
                {
                    "NaN" => double.NaN,
                    "Infinity" => double.PositiveInfinity,
                    _ => double.NegativeInfinity
                });
            case "buffer":
                return RuntimeValue.Object(DecodeBuffer(element.GetProperty("kind").GetString() ?? "uint8", Convert.FromBase64String(element.GetProperty("b64").GetString() ?? string.Empty)));
            default:
                throw new RuntimeException("io.storeGet() found a stored value it cannot read.");
        }
    }

    private static TypedBufferInstance DecodeBuffer(string kind, byte[] bytes)
    {
        if (kind == "uint8")
            return new TypedBufferInstance(bytes);
        var bufferKind = kind switch
        {
            "float64" => TypedBufferKind.Float64,
            "float32" => TypedBufferKind.Float32,
            _ => TypedBufferKind.Int32
        };
        var size = bufferKind == TypedBufferKind.Float64 ? 8 : 4;
        var buffer = new TypedBufferInstance(bufferKind, bytes.Length / size);
        for (var i = 0; i < buffer.Length; i++)
        {
            var span = bytes.AsSpan(i * size, size);
            buffer.SetNumber(i, bufferKind switch
            {
                TypedBufferKind.Float64 => BinaryPrimitives.ReadDoubleLittleEndian(span),
                TypedBufferKind.Float32 => BinaryPrimitives.ReadSingleLittleEndian(span),
                _ => BinaryPrimitives.ReadInt32LittleEndian(span)
            });
        }
        return buffer;
    }
}
