// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

public class PngIoTests : TestBase
{
    private static readonly byte[] OneByOnePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void EncodeRgba_RoundTripsPixels()
    {
        var rgba = new byte[] { 10, 20, 30, 255, 1, 2, 3, 128, 0, 0, 0, 0, 255, 255, 255, 255 };
        var png = PngCodec.EncodeRgba(2, 2, rgba);

        Assert.True(PngCodec.TryDecode(png, out var width, out var height, out var decoded));
        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal(rgba, decoded);
    }

    [Fact]
    public void TryDecode_ReadsForeignDeflatePng()
    {
        Assert.True(PngCodec.TryDecode(OneByOnePng, out var width, out var height, out var rgba));
        Assert.Equal(1, width);
        Assert.Equal(1, height);
        Assert.Equal(new byte[] { 255, 0, 0, 127 }, rgba);
    }

    [Fact]
    public void TryDecode_UnfiltersSubUpAverageAndPaeth()
    {
        AssertRgba(Decode(2, 1, colorType: 6, [1, 5, 6, 7, 8, 10, 10, 10, 10]), [5, 6, 7, 8, 15, 16, 17, 18]);
        AssertRgba(Decode(2, 2, colorType: 6, [
            0, 9, 8, 7, 255, 1, 2, 3, 4,
            2, 0, 0, 0, 0, 0, 0, 0, 0
        ]), [9, 8, 7, 255, 1, 2, 3, 4, 9, 8, 7, 255, 1, 2, 3, 4]);
        AssertRgba(Decode(2, 1, colorType: 0, [3, 10, 25]), [10, 10, 10, 255, 30, 30, 30, 255]);
        AssertRgba(Decode(2, 2, colorType: 0, [0, 10, 20, 4, 30, 10]), [10, 10, 10, 255, 20, 20, 20, 255, 40, 40, 40, 255, 50, 50, 50, 255]);
    }

    [Fact]
    public void TryDecode_RejectsIndexedAndTruncatedFiles()
    {
        Assert.False(PngCodec.TryDecode(OneByOnePng.AsSpan(0, 20), out _, out _, out _));
        Assert.False(PngCodec.TryDecode(ReadOnlySpan<byte>.Empty, out _, out _, out _));
    }

    [Fact]
    public void WritePng_RoundTripsThroughTheInterpreter()
    {
        var path = TempPng();
        try
        {
            var source = $$"""
                var px = [255, 1, 2, 200, 3, 4, 5, 6];
                io.print(io.writePng("{{path}}", 2, 1, px));
                var img = io.readPng("{{path}}");
                io.print(img.width);
                io.print(img.height);
                io.print(img.pixels.length);
                io.print(img.pixels[0]);
                io.print(img.pixels[7]);
                """;

            Assert.Equal("true\n2\n1\n8\n255\n6", RunProgram(source));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void WritePng_TranspiledRoundTrip()
    {
        var path = TempPng();
        try
        {
            var source = $$"""
                var px = [1, 2, 3, 4];
                io.print(io.writePng("{{path}}", 1, 1, px));
                var img = io.readPng("{{path}}");
                io.print(img.pixels[0]);
                io.print(img.pixels[3]);
                """;

            var result = TranspiledTestRunner.CompileAndRunFromSource(source);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("true\n1\n4", result.StdOut);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void WritePng_RejectsAMismatchedBufferAndEmbeddedPaths()
    {
        var mismatch = Assert.Throws<Exception>(() => BuiltInFunctions.CallBuiltIn(
            "writePng",
            [
                RuntimeValue.String(TempPng()),
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(1),
                RuntimeValue.Array(new List<RuntimeValue> { RuntimeValue.Integer(1) })
            ],
            null));
        Assert.Contains("4 channel", mismatch.Message, StringComparison.Ordinal);

        var embedded = Assert.Throws<Exception>(() => BuiltInFunctions.CallBuiltIn(
            "writePng",
            [
                RuntimeValue.String("embed:pack/pixel.png"),
                RuntimeValue.Integer(1),
                RuntimeValue.Integer(1),
                RuntimeValue.Array(new List<RuntimeValue>
                {
                    RuntimeValue.Integer(1),
                    RuntimeValue.Integer(2),
                    RuntimeValue.Integer(3),
                    RuntimeValue.Integer(4)
                })
            ],
            null));
        Assert.Contains("embed:", embedded.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadPng_ReturnsNullWhenMissingOrNotAPng()
    {
        var missing = BuiltInFunctions.CallBuiltIn(
            "readPng",
            [RuntimeValue.String(TempPng())],
            null);
        Assert.Equal(ValueType.Null, missing.Type);

        var text = TempPng();
        try
        {
            File.WriteAllText(text, "not a png");
            var bad = BuiltInFunctions.CallBuiltIn("readPng", [RuntimeValue.String(text)], null);
            Assert.Equal(ValueType.Null, bad.Type);
        }
        finally
        {
            if (File.Exists(text))
                File.Delete(text);
        }
    }

    private static byte[] Decode(int width, int height, byte colorType, byte[] scanlines)
    {
        var png = PngCodec.EncodeScanlines(width, height, colorType, scanlines);
        Assert.True(PngCodec.TryDecode(png, out var decodedWidth, out var decodedHeight, out var rgba));
        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        return rgba;
    }

    private static void AssertRgba(byte[] actual, byte[] expected) => Assert.Equal(expected, actual);

    private static string TempPng() =>
        Path.Combine(Path.GetTempPath(), "malda-png-" + Guid.NewGuid().ToString("N") + ".png").Replace('\\', '/');
}
