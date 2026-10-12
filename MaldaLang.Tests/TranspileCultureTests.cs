// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using MaldaLang;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Transpiled programs must parse and print numbers the same way on every OS locale
/// (on it-IT, float("0.5") used to read 5 and 1.5 printed as "1,5").
/// </summary>
public class TranspileCultureTests
{
    [Fact]
    public void Transpile_Main_PinsInvariantCulture()
    {
        var lexer = new Lexer("print(float(\"0.5\"));", "culture_test.malda");
        var parser = new Parser.Parser(lexer.Tokenize(), "culture_test.malda");
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var generated = new CSharpTranspiler(profilingOptions: null).Transpile(statements);

        Assert.Contains("CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;", generated);
        Assert.Contains("CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;", generated);
        Assert.DoesNotContain("double.TryParse(s, out", generated);
    }

    [Fact]
    public void RuntimeHelpers_ParseAndFormat_IgnoreItalianCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            Assert.Equal(0.5, RuntimeHelpers.CoerceToFloat("0.5"));
            Assert.Equal("1.5", RuntimeHelpers.CoerceToString(1.5));
            Assert.Equal(-12, RuntimeHelpers.CoerceToInt("-12"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
