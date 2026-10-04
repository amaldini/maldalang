// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// 8-bit PNG encode and decode for <c>io.writePng</c> / <c>io.readPng</c>.
/// Decode accepts non-interlaced grayscale, gray+alpha, RGB, and RGBA.
/// </summary>
internal static class PngCodec
{
    private const int MaxPixels = 16 * 1024 * 1024;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] EncodeRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var rowBytes = width * 4;
        var scanlines = new byte[height * (rowBytes + 1)];
        var offset = 0;
        var pixel = 0;
        for (var y = 0; y < height; y++)
        {
            scanlines[offset++] = 0;
            rgba.Slice(pixel, rowBytes).CopyTo(scanlines.AsSpan(offset, rowBytes));
            offset += rowBytes;
            pixel += rowBytes;
        }

        return EncodeScanlines(width, height, colorType: 6, scanlines);
    }

    internal static byte[] EncodeScanlines(int width, int height, byte colorType, ReadOnlySpan<byte> scanlines)
    {
        var expected = height * (width * BytesPerPixel(colorType) + 1);
        if (scanlines.Length != expected)
            throw new InvalidOperationException("PNG scanline buffer has the wrong length.");

        using var output = new MemoryStream();
        output.Write(Signature);
        var ihdr = new byte[13];
        WriteU32(ihdr, 0, (uint)width);
        WriteU32(ihdr, 4, (uint)height);
        ihdr[8] = 8;
        ihdr[9] = colorType;
        WriteChunk(output, "IHDR", ihdr);

        using (var idat = new MemoryStream())
        {
            using (var zlib = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(scanlines);
            WriteChunk(output, "IDAT", idat.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> png, out int width, out int height, out byte[] rgba)
    {
        width = 0;
        height = 0;
        rgba = [];
        try
        {
            return TryDecodeCore(png, out width, out height, out rgba);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            width = 0;
            height = 0;
            rgba = [];
            return false;
        }
    }

    private static bool TryDecodeCore(ReadOnlySpan<byte> png, out int width, out int height, out byte[] rgba)
    {
        width = 0;
        height = 0;
        rgba = [];
        if (png.Length < Signature.Length + 12 || !png.StartsWith(Signature))
            return false;

        var offset = Signature.Length;
        var sawIhdr = false;
        var sawIdat = false;
        var idatClosed = false;
        byte colorType = 0;
        using var idat = new MemoryStream();

        while (offset + 12 <= png.Length)
        {
            var length = (int)ReadU32(png, offset);
            if (length < 0 || offset + 12L + length > png.Length)
                return false;

            var type = png.Slice(offset + 4, 4);
            var data = png.Slice(offset + 8, length);
            var storedCrc = ReadU32(png, offset + 8 + length);
            if (storedCrc != ChunkCrc(type, data))
                return false;

            var typeName = System.Text.Encoding.ASCII.GetString(type);
            if (typeName == "IHDR")
            {
                if (sawIhdr || length != 13)
                    return false;
                width = (int)ReadU32(data, 0);
                height = (int)ReadU32(data, 4);
                if (data[8] != 8 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    return false;
                colorType = data[9];
                if (colorType is not (0 or 2 or 4 or 6))
                    return false;
                if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
                    return false;
                sawIhdr = true;
            }
            else if (typeName == "IDAT")
            {
                if (!sawIhdr || idatClosed)
                    return false;
                sawIdat = true;
                idat.Write(data);
            }
            else if (typeName == "IEND")
            {
                break;
            }
            else if (sawIdat)
            {
                idatClosed = true;
            }

            offset += 12 + length;
        }

        if (!sawIhdr || !sawIdat || idat.Length == 0)
            return false;

        var bpp = BytesPerPixel(colorType);
        var rowBytes = width * bpp;
        var expected = height * (rowBytes + 1);
        var filtered = new byte[expected];
        using (var input = new MemoryStream(idat.ToArray(), writable: false))
        using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
        {
            var read = 0;
            while (read < expected)
            {
                var n = zlib.Read(filtered, read, expected - read);
                if (n == 0)
                    return false;
                read += n;
            }
        }

        var raw = Unfilter(filtered, height, rowBytes, bpp);
        rgba = ToRgba(raw, colorType, bpp);
        return true;
    }

    private static byte[] Unfilter(byte[] filtered, int height, int rowBytes, int bpp)
    {
        var raw = new byte[height * rowBytes];
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[y * (rowBytes + 1)];
            var src = y * (rowBytes + 1) + 1;
            var dst = y * rowBytes;
            for (var x = 0; x < rowBytes; x++)
            {
                var left = x >= bpp ? raw[dst + x - bpp] : 0;
                var up = y > 0 ? raw[dst - rowBytes + x] : 0;
                var upLeft = y > 0 && x >= bpp ? raw[dst - rowBytes + x - bpp] : 0;
                var value = filter switch
                {
                    0 => filtered[src + x],
                    1 => filtered[src + x] + left,
                    2 => filtered[src + x] + up,
                    3 => filtered[src + x] + ((left + up) / 2),
                    4 => filtered[src + x] + Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException("Unsupported PNG filter.")
                };
                raw[dst + x] = (byte)value;
            }
        }

        return raw;
    }

    private static byte[] ToRgba(byte[] raw, byte colorType, int bpp)
    {
        var pixels = raw.Length / bpp;
        var rgba = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            var s = i * bpp;
            var d = i * 4;
            switch (colorType)
            {
                case 0:
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = raw[s];
                    rgba[d + 3] = 255;
                    break;
                case 2:
                    rgba[d] = raw[s];
                    rgba[d + 1] = raw[s + 1];
                    rgba[d + 2] = raw[s + 2];
                    rgba[d + 3] = 255;
                    break;
                case 4:
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = raw[s];
                    rgba[d + 3] = raw[s + 1];
                    break;
                default:
                    rgba[d] = raw[s];
                    rgba[d + 1] = raw[s + 1];
                    rgba[d + 2] = raw[s + 2];
                    rgba[d + 3] = raw[s + 3];
                    break;
            }
        }

        return rgba;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        if (pb <= pc)
            return b;
        return c;
    }

    private static int BytesPerPixel(byte colorType) => colorType switch
    {
        0 => 1,
        2 => 3,
        4 => 2,
        6 => 4,
        _ => throw new InvalidDataException("Unsupported PNG color type.")
    };

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        WriteU32(length, 0, (uint)data.Length);
        output.Write(length);
        output.Write(typeBytes);
        output.Write(data);
        var crc = new byte[4];
        WriteU32(crc, 0, ChunkCrc(typeBytes, data));
        output.Write(crc);
    }

    private static uint ChunkCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}

public static partial class BuiltInFunctions
{
    private const int PngMaxPixels = 16 * 1024 * 1024;

    private static RuntimeValue BuiltInWritePng(List<RuntimeValue> args)
    {
        BuiltInArity.Require("writePng", args, 4, 4, "path, width, height, rgba");
        if (args[0].Type != ValueType.String)
            throw new Exception("writePng() expects (path, width, height, rgba)");

        var width = RequirePngDimension(args[1], "width");
        var height = RequirePngDimension(args[2], "height");
        if ((long)width * height > PngMaxPixels)
            throw new Exception("writePng() image is too large");

        var rgba = PackRgba(args[3], width, height);
        var path = args[0].AsString();
        RejectEmbedWrite(path, "writePng");

        try
        {
            File.WriteAllBytes(path, PngCodec.EncodeRgba(width, height, rgba));
            return RuntimeValue.Boolean(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return RuntimeValue.Boolean(false);
        }
    }

    private static RuntimeValue BuiltInReadPng(List<RuntimeValue> args)
    {
        BuiltInArity.Require("readPng", args, 1, 1, "path");
        if (args[0].Type != ValueType.String)
            throw new Exception("readPng() expects a string path");

        try
        {
            var path = args[0].AsString();
            byte[]? bytes;
            if (EmbeddedFolderStore.IsEmbedPath(path))
            {
                bytes = EmbeddedFolderStore.ReadBytes(path);
                if (bytes == null)
                    return RuntimeValue.Null();
            }
            else
            {
                if (!File.Exists(path))
                    return RuntimeValue.Null();
                bytes = File.ReadAllBytes(path);
            }

            if (!PngCodec.TryDecode(bytes, out var width, out var height, out var rgba))
                return RuntimeValue.Null();

            var pixels = new List<RuntimeValue>(rgba.Length);
            foreach (var channel in rgba)
                pixels.Add(RuntimeValue.Integer(channel));

            var image = new DictionaryInstance();
            image.SetEntry("width", RuntimeValue.Integer(width));
            image.SetEntry("height", RuntimeValue.Integer(height));
            image.SetEntry("pixels", RuntimeValue.Array(pixels));
            return RuntimeValue.Object(image);
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }

    private static int RequirePngDimension(RuntimeValue value, string name)
    {
        if (!TryWholeNumber(value, out var dimension) || dimension <= 0)
            throw new Exception($"writePng() {name} must be a positive integer");
        return dimension;
    }

    private static byte[] PackRgba(RuntimeValue value, int width, int height)
    {
        if (value.Type != ValueType.Array)
            throw new Exception("writePng() expects a flat RGBA array of length width*height*4");

        var pixels = value.AsArray();
        var expected = (long)width * height * 4;
        if (pixels.Count != expected)
            throw new Exception($"writePng() expects {expected} channel values, got {pixels.Count}");

        var rgba = new byte[pixels.Count];
        for (var i = 0; i < pixels.Count; i++)
        {
            if (!TryChannel(pixels[i], out var channel))
                throw new Exception($"writePng() pixel channel at index {i} must be a number");
            rgba[i] = channel;
        }

        return rgba;
    }

    private static bool TryChannel(RuntimeValue value, out byte channel)
    {
        channel = 0;
        if (!TryFiniteNumber(value, out var number))
            return false;

        var rounded = (int)Math.Round(number, MidpointRounding.AwayFromZero);
        if (rounded < 0)
            rounded = 0;
        if (rounded > 255)
            rounded = 255;
        channel = (byte)rounded;
        return true;
    }

    private static bool TryWholeNumber(RuntimeValue value, out int number)
    {
        number = 0;
        if (!TryFiniteNumber(value, out var real))
            return false;
        if (real < int.MinValue || real > int.MaxValue || real != Math.Floor(real))
            return false;
        number = (int)real;
        return true;
    }

    private static bool TryFiniteNumber(RuntimeValue value, out double number)
    {
        switch (value.Type)
        {
            case ValueType.Integer:
                number = value.AsInteger();
                return true;
            case ValueType.Float:
                number = value.AsFloat();
                return double.IsFinite(number);
            default:
                number = 0;
                return false;
        }
    }
}
