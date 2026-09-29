// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class NestedFunctionDeclarationTests : TestBase
{
    [Fact]
    public void BlockFunction_IsCallableAndClosesOverLocals()
    {
        var source = """
            if (true) {
                var factor = 3;
                function scale(n) {
                    return n * factor;
                }
                factor = 4;
                print(scale(2));
            }
            """;

        InterpretTranspilePair.AssertSameFromSource(source, "block function closure");
    }

    [Fact]
    public void BlockFunction_CanBePassedAsACallback()
    {
        var source = """
            function apply(fn, x) {
                return fn(x);
            }
            if (true) {
                var extra = 10;
                function memoryEmbed(n) {
                    return n + extra;
                }
                print(apply(memoryEmbed, 5));
            }
            """;

        InterpretTranspilePair.AssertSameFromSource(source, "block function callback");
    }

    [Fact]
    public void BlockFunction_DoesNotLeakOutsideTheBlock()
    {
        var source = """
            if (true) {
                function hidden() {
                    return 1;
                }
            }
            hidden();
            """;

        var ex = Assert.Throws<MaldaLang.Interpreter.RuntimeException>(() => RunProgram(source));
        Assert.Contains("hidden", ex.Message);
    }

    [Fact]
    public void FunctionInsideFunction_ClosesOverParameters()
    {
        var source = """
            function outer(x) {
                function inner() {
                    return x + 1;
                }
                return inner();
            }
            print(outer(5));
            """;

        InterpretTranspilePair.AssertSameFromSource(source, "function inside function");
    }
}
