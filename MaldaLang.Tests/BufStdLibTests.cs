// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>buf.*</c> fixed-size numeric buffers and binary data (J3): the same output on the
/// interpreter, C# transpile and JavaScript, plus the conversion rules and the JavaScript emit.
/// </summary>
[Collection("Sequential")]
public class BufStdLibTests
{
    /// <summary>Indexing, element conversion, iteration, formatting, binary I/O, encodings and errors.</summary>
    private const string Program = """
        var g = buf.float64(4);
        g[0] = 1.5;
        g[1] = 2;
        g[2] += 3;
        g[3]++;
        io.print(g);
        io.print(g.length);
        io.print(typeOf(g));
        io.print(toJSON(g));
        var f = buf.float32(1);
        f[0] = 0.1;
        io.print(f[0] == 0.1);
        var n = buf.int32(3);
        n[0] = 3.9;
        n[1] = -3.9;
        n[2] = 4294967297.0;
        io.print(n);
        var b = buf.bytes(2);
        b[0] = 257;
        b[1] = -1;
        io.print(b);
        var sum = 0;
        for (var x in g) {
            sum = sum + x;
        }
        io.print(sum);
        var h = buf.bytes(8);
        buf.writeAt(h, 0, "uint32", 1179011410);
        buf.writeAt(h, 4, "float32", 1.5);
        io.print(buf.toBase64(h));
        io.print(buf.readAt(h, 0, "uint32"));
        io.print(buf.readAt(h, 4, "float32"));
        io.print(buf.readAt(h, 0, "int8"));
        io.print(buf.readAt(buf.fromBase64("/////w=="), 0, "uint32"));
        io.print(buf.toUtf8(buf.fromUtf8("ciao è")));
        io.print(buf.kind(buf.from([1, 2.5], "int32")));
        io.print(buf.toArray(buf.from([1, 2.5], "int32")));
        io.print(buf.slice(g, 1, 3));
        io.print(buf.fill(buf.int32(2), 7));
        var swap = g;
        io.print(swap[0] + swap[1]);
        try { g[4] = 1; } catch (e) { io.print(e); }
        try { g[0] = "x"; } catch (e) { io.print(e); }
        try { io.print(g[-1]); } catch (e) { io.print(e); }
        try { g.append(1); } catch (e) { io.print(e); }
        try { buf.readAt(h, 6, "uint32"); } catch (e) { io.print(e); }
        try { buf.readAt(g, 0, "uint8"); } catch (e) { io.print(e); }
        try { buf.float64(-1); } catch (e) { io.print(e); }
        """;

    private const string ExpectedOutput = """
        [1.5, 2, 3, 1]
        4
        buffer
        [1.5,2,3,1]
        false
        [3, -3, 1]
        [1, 255]
        7.5
        UklGRgAAwD8=
        1179011410
        1.5
        82
        4294967295
        ciao è
        int32
        [1, 2]
        [2, 3]
        [7, 7]
        3.5
        Buffer index 4 is out of range (length 4).
        A float64 buffer holds numbers, not a string.
        Buffer index -1 is out of range (length 4).
        A buffer has a fixed length and no array methods (append, map, ...). Use buf.toArray(b) for an array.
        buf.readAt() of uint32 at offset 6 is outside the buffer (length 8).
        Expected a bytes buffer (buf.bytes), got a float64 buffer.
        buf.float64() length must be an integer from 0 to 268435456.
        """;

    [Fact]
    public async Task Interpreter_PrintsTheExpectedOutput()
    {
        var output = await TestBase.CaptureInterpretAsync(Program);

        Assert.Equal(InterpretTranspilePair.Normalize(ExpectedOutput), InterpretTranspilePair.Normalize(output));
    }

    [Fact]
    public void CSharpTranspile_MatchesTheInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(Program, "buf-csharp");
    }

    [Fact]
    public void JavaScript_MatchesTheInterpreter()
    {
        InterpretJsPair.AssertSameFromSource(Program, "buf-js");
    }

    [Theory]
    [InlineData(3.9, 3)]
    [InlineData(-3.9, -3)]
    [InlineData(4294967297.0, 1)]
    [InlineData(2147483648.0, -2147483648)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void ToInt32_FollowsEcmaScript(double value, int expected)
    {
        Assert.Equal(expected, TypedBufferInstance.ToInt32(value));
    }

    [Fact]
    public void JavaScriptEmit_BufferVariables_UseTheFastHelpersAndPlainArithmetic()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            var grid = buf.float64(4);
            var next = buf.float64(4);
            for (var k = 1; k < 3; k = k + 1) {
                next[k] = (grid[k - 1] + grid[k + 1]) * 0.5;
            }
            var swap = grid;
            grid = next;
            next = swap;
            """);

        Assert.Contains("__maldaBufSet(next, k, ((__maldaBufGet(grid, (k - 1)) + __maldaBufGet(grid, (k + 1))) * 0.5));", js);
        Assert.DoesNotContain("__maldaAdd(", js.Replace("const __maldaAdd = mlRuntime.add", string.Empty));
    }

    [Fact]
    public void JavaScriptEmit_ANameThatMayHoldAnArray_KeepsTheGeneralHelpers()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            var data = buf.float64(4);
            data = [1, 2, 3];
            var first = data[0] + 1;
            """);

        Assert.Contains("__maldaGetIndex(data, 0)", js);
        Assert.DoesNotContain("__maldaBufGet(data", js);
    }
}
