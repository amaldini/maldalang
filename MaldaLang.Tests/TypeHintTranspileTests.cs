// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Hints are informational in the interpreter. C# transpile maps only numeric hints to a CLR type;
/// a class, host class, or container name used to crash with "Unsupported transpiled type hint".
/// </summary>
[Collection("Sequential")]
public class TypeHintTranspileTests
{
    [Theory]
    [InlineData("Sequential")]
    [InlineData("Dense")]
    [InlineData("Point")]
    [InlineData("array")]
    [InlineData("object")]
    [InlineData("any")]
    public void ReferenceHint_OnFunction_TranspilesLikeInterpreter(string hint)
    {
        InterpretTranspilePair.AssertSameFromSource($$"""
            class Point {
                var x = 1;
            }
            function same(a: {{hint}}) -> {{hint}} {
                return a;
            }
            print(same(new Point()).x);
            print(same([1, 2]).length);
            """, $"hint {hint}");
    }

    [Fact]
    public void ClassHints_OnFieldsMethodsAndConstructors_Transpile()
    {
        InterpretTranspilePair.AssertSameFromSource("""
            class Point {
                var x = 1;
            }
            class Holder {
                var point: Point;
                var net: Sequential;
                function Holder(point: Point) {
                    this.point = point;
                    this.net = new Sequential([new Dense(1, 1, "linear", 0.0)]);
                }
                function get() -> Point {
                    return this.point;
                }
            }
            function build() -> Sequential {
                return new Sequential([new Dense(1, 1, "linear", 0.0)]);
            }
            var holder = new Holder(new Point());
            print(holder.get().x);
            print(holder.net.layers.length);
            print(build().forward([2.0])[0]);
            """, "class hints");
    }

    [Fact]
    public void DoubleHint_IsFloat()
    {
        InterpretTranspilePair.AssertSameFromSource("""
            function half(x: double) -> double {
                return x / 2.0;
            }
            print(half(3.0));
            """, "double hint");
    }
}
