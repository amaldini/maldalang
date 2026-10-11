// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Spec §6: only null and false are falsy. 0, 0.0, "", and empty arrays are truthy.
/// </summary>
[Collection("Sequential")]
public class TruthinessTests : TestBase
{
    public const string CoreSource = """
        io.print(0 ? "T" : "F");
        io.print(0.0 ? "T" : "F");
        io.print("" ? "T" : "F");
        io.print([] ? "T" : "F");
        io.print(null ? "T" : "F");
        io.print(false ? "T" : "F");
        io.print(true ? "T" : "F");
        io.print(not 0);
        io.print(not "");
        io.print(not []);
        if (0) {
            io.print("if0");
        }
        var n = 0;
        if (n) {
            io.print("var0");
        }
        var s = "";
        if (s) {
            io.print("varempty");
        }
        try {
            throw "x";
        } catch (e if 0) {
            io.print("catch0");
        } catch (e) {
            io.print("miss");
        }
        try {
            throw "y";
        } catch (e if false) {
            io.print("catchf");
        } catch (e) {
            io.print("catchfalse");
        }
        """;

    public const string Source = CoreSource + """
        io.print(ui.template("{{#if n}}T{{/if}}{{#if s}}S{{/if}}{{#if missing}}M{{/if}}", { "n": 0, "s": "" }));
        """;

    private const string Expected = """
        T
        T
        T
        T
        F
        F
        T
        false
        false
        false
        if0
        var0
        varempty
        catch0
        catchfalse
        TS
        """;

    [Fact]
    public void Interpreter_OnlyNullAndFalse_AreFalsy()
    {
        var output = RunProgram(Source).Replace("\r", "").Trim();
        Assert.Equal(Expected.Replace("\r", "").Trim(), output);
    }

    [Fact]
    public void CoerceToBool_MatchesInterpreterTruthiness()
    {
        Assert.True(RuntimeHelpers.CoerceToBool(0));
        Assert.True(RuntimeHelpers.CoerceToBool(0L));
        Assert.True(RuntimeHelpers.CoerceToBool(0.0));
        Assert.True(RuntimeHelpers.CoerceToBool(0f));
        Assert.True(RuntimeHelpers.CoerceToBool(double.NaN));
        Assert.True(RuntimeHelpers.CoerceToBool(""));
        Assert.True(RuntimeHelpers.CoerceToBool(new List<object>()));
        Assert.True(RuntimeHelpers.CoerceToBool(RuntimeValue.Integer(0)));
        Assert.True(RuntimeHelpers.CoerceToBool(RuntimeValue.Float(0.0)));
        Assert.True(RuntimeHelpers.CoerceToBool(RuntimeValue.String("")));
        Assert.True(RuntimeHelpers.CoerceToBool(RuntimeValue.Array(new List<RuntimeValue>())));
        Assert.True(RuntimeHelpers.CoerceToBool(RuntimeValue.Boolean(true)));
        Assert.True(RuntimeHelpers.CoerceToBool(true));

        Assert.False(RuntimeHelpers.CoerceToBool(null));
        Assert.False(RuntimeHelpers.CoerceToBool(false));
        Assert.False(RuntimeHelpers.CoerceToBool(RuntimeValue.Null()));
        Assert.False(RuntimeHelpers.CoerceToBool(RuntimeValue.Boolean(false)));
    }
}
