// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

public class TranspilerStrongTypingTests
{
    private static string TranspileSource(string source, int typedTranspileLevel = 1)
    {
        var lexer = new Lexer(source, "typed_test.malda");
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens, "typed_test.malda");
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var transpiler = new CSharpTranspiler(profilingOptions: null, typedTranspileLevel: typedTranspileLevel);
        return transpiler.Transpile(statements);
    }

    [Fact]
    public void Transpile_FloatHints_EmitDoubleLocalsAndParameters()
    {
        var source = """
            function add(a: float, b: float) -> float {
                var x: float = a + b;
                return x;
            }
            var result: float = add(1.5, 2.5);
            print(result);
            """;

        var generated = TranspileSource(source);
        Assert.Contains("Task<double>", generated);
        Assert.Contains("double a", generated);
        Assert.Contains("double b", generated);
        Assert.Contains("double x = (double)RuntimeHelpers.CoerceToFloat(", generated);
        Assert.Contains("public static double result = 0d;", generated);
    }

    [Fact]
    public void Transpile_Level0Legacy_IgnoresTypeHints()
    {
        var source = """
            function add(a: float, b: float) -> float {
                var x: float = a + b;
                return x;
            }
            """;

        var generated = TranspileSource(source, typedTranspileLevel: 0);
        Assert.Contains("Task<object>", generated);
        Assert.Contains("object a", generated);
        Assert.Contains("object b", generated);
        Assert.DoesNotContain("Task<double>", generated);
    }

    [Fact]
    public void Transpile_AggressiveLevel_FloatArrayHint_EmitsTypedContainer()
    {
        var source = """
            var xs: floatArray = [1, 2, 3];
            xs.append(4.5);
            print(xs.length);
            """;

        var generated = TranspileSource(source, typedTranspileLevel: 2);
        Assert.Contains("public static System.Collections.Generic.List<double> xs = null!;", generated);
        Assert.Contains("public static List<double> ArrayAppendDouble(List<double> arr, object? item)", generated);
        Assert.Contains("public static List<double> CoerceToDoubleList(object? value)", generated);
    }

    [Fact]
    public void Transpile_TypedMathBuiltins_EmitDirectMathCallsWithoutCoercionWrappers()
    {
        var source = """
            function calc(x: float, y: float) -> float {
                var a: float = min(x, y);
                var b: float = max(x, y);
                var c: float = sqrt(a + b);
                var d: float = pow(c, 2.0);
                return sin(d) + cos(d) + log(d + 1.0);
            }
            """;

        var generated = TranspileSource(source, typedTranspileLevel: 2);
        Assert.Contains("Math.Min(x, y)", generated);
        Assert.Contains("Math.Max(x, y)", generated);
        Assert.Contains("Math.Sqrt(", generated);
        Assert.Contains("Math.Pow(", generated);
        Assert.Contains("Math.Sin(d)", generated);
        Assert.Contains("Math.Cos(d)", generated);
        Assert.Contains("Math.Log(", generated);
        Assert.DoesNotContain("Math.Min((double)RuntimeHelpers.CoerceToFloat(", generated);
        Assert.DoesNotContain("Math.Max((double)RuntimeHelpers.CoerceToFloat(", generated);
    }

    [Fact]
    public void Transpile_TypedCoreOps_EmitNativeOperatorsForProvenDoubleOperands()
    {
        var source = """
            function calc(a: float, b: float, c: float) -> float {
                var x: float = (a + b) * c;
                var y: float = x / (b - a);
                var z: float = y % 3.0;
                return z;
            }
            """;

        var generated = TranspileSource(source, typedTranspileLevel: 2);
        Assert.Contains("(a + b)", generated);
        Assert.Contains("(x /", generated);
        Assert.Contains("%", generated);
        Assert.DoesNotContain("RuntimeHelpers.OperatorAdd(a, b)", generated);
        Assert.DoesNotContain("RuntimeHelpers.OperatorDivide(", generated);
        Assert.DoesNotContain("RuntimeHelpers.OperatorModulo(", generated);
    }
}

