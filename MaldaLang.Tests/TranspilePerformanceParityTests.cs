// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// The C# transpiler's fast paths (conditional defer frames, primitive operator fast paths,
/// direct <c>this.field</c> access, synchronous function twins, direct method dispatch, and
/// inferred <c>double</c> locals) must not change what a program prints. Each pair runs the
/// same source through the interpreter and a transpiled build; the shape tests pin that the
/// fast path is actually taken. Benchmarks: <c>scripts/run-runtime-benchmarks.ps1</c>.
/// </summary>
[Collection("Sequential")]
public class TranspilePerformanceParityTests
{
    private static string TranspileSource(string source)
    {
        var lexer = new Lexer(source, "perf_test.malda");
        var parser = new Parser.Parser(lexer.Tokenize(), "perf_test.malda");
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return new CSharpTranspiler().Transpile(statements);
    }

    [Fact]
    public void IntLiteralArithmetic_KeepsMaldaIntSemantics()
    {
        // Int literals alone used to be emitted as raw C#: 7 / 2 printed 3 and an overflowing
        // constant failed the C# build instead of raising "Integer overflow." at run time.
        InterpretTranspilePair.AssertSameFromSource(
            """
            io.print(7 / 2);
            io.print(1 + 2 * 3);
            io.print(-(4 / 8));
            io.print(10 % 4);
            var x: float = 7;
            io.print(x / 2);
            try { io.print(2147483647 + 1); } catch (e) { io.print("overflow"); }
            """,
            "perf-int-literals");
    }

    [Fact]
    public void OperatorFastPaths_MatchInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            io.print(3 + 4);
            io.print(3 + 0.5);
            io.print(0.5 + 3);
            io.print(2.5 * 2);
            io.print(9 - 10);
            io.print(1 / 4);
            io.print(7 % 3);
            io.print(7.5 % 2);
            io.print(1 == 1.0);
            io.print(1.0 == 1.0);
            io.print("a" == "a");
            io.print(2 < 2.5);
            io.print(3 >= 3);
            io.print(-(5));
            io.print(-(2.5));
            io.print("n=" + 3);
            try { io.print(5 / 0); } catch (e) { io.print("div0"); }
            try { io.print(5 % 0); } catch (e) { io.print("mod0"); }
            try { io.print(2147483647 * 2); } catch (e) { io.print("mul overflow"); }
            """,
            "perf-operator-fast-paths");
    }

    [Fact]
    public void DeferFrames_StillRunWhereDeclared()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            function work(n) {
                var i = 0;
                while (i < n) {
                    if (i == 1) {
                        defer { io.print("inner defer"); }
                    }
                    i = i + 1;
                }
                defer { io.print("outer defer"); }
                io.print("body done");
                return i;
            }

            function plain(n) {
                var total = 0;
                for (var k = 0; k < n; k = k + 1) {
                    total = total + k;
                }
                return total;
            }

            io.print(work(3));
            io.print(plain(5));
            """,
            "perf-defer-frames");
    }

    [Fact]
    public void DeferFrames_OnlyEmittedForBlocksThatDefer()
    {
        var generated = TranspileSource(
            """
            function plain(n) {
                var total = 0;
                while (total < n) { total = total + 1; }
                return total;
            }
            io.print(plain(3));
            """);
        Assert.DoesNotContain("RuntimeHelpers.PushDeferFrame();", generated);

        var withDefer = TranspileSource(
            """
            function cleanup() {
                defer { io.print("bye"); }
                io.print("hi");
            }
            cleanup();
            """);
        Assert.Contains("RuntimeHelpers.PushDeferFrame();", withDefer);
    }

    [Fact]
    public void SyncTwins_MatchInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            function fib(n) {
                if (n < 2) { return n; }
                return fib(n - 1) + fib(n - 2);
            }

            function delayed(n) {
                sleep(1);
                return n * 2;
            }

            function usesDelayed(n) {
                return delayed(n) + 1;
            }

            function thrower(n) {
                if (n > 2) { throw "too big"; }
                return n;
            }

            class Calc {
                function twice(n) { return fib(n) * 2; }
            }

            var apply = (f, v) => f(v);
            io.print(fib(15));
            io.print(usesDelayed(4));
            io.print(apply(fib, 10));
            io.print(new Calc().twice(10));
            try { thrower(5); } catch (e) { io.print("caught " + e); }
            """,
            "perf-sync-twins");
    }

    [Fact]
    public void SyncTwins_EmittedForPureFunctionsOnly()
    {
        var generated = TranspileSource(
            """
            function fib(n) {
                if (n < 2) { return n; }
                return fib(n - 1) + fib(n - 2);
            }
            function waits(n) {
                sleep(1);
                return n;
            }
            function callsWaits(n) { return waits(n); }
            function c4(n) { return callsWaits(n); }
            function c3(n) { return c4(n); }
            function c2(n) { return c3(n); }
            function c1(n) { return c2(n); }
            io.print(fib(10));
            io.print(c1(1));
            """);
        Assert.Contains("static object fib__Sync(object n)", generated);
        Assert.Contains("fib__Sync(RuntimeHelpers.OperatorSubtract(n, 1))", generated);
        // The async method stays for delegates, reflection and spawn.
        Assert.Contains("static async Task<object> fib(object n)", generated);
        // An await deep in a call chain takes every caller's twin with it, without dropping
        // the unrelated fib twin (the pass cap used to give up on all twins).
        Assert.DoesNotContain("waits__Sync", generated);
        Assert.DoesNotContain("c1__Sync", generated);
    }

    [Fact]
    public void ClassFastPaths_MatchInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            class Vec {
                var x;
                var y;
                function Vec(x, y) {
                    this.x = x;
                    this.y = y;
                }
                function dotWith(other) {
                    return this.x * other.x + this.y * other.y;
                }
                function describe() { return "Vec(" + this.x + ", " + this.y + ")"; }
            }

            class Base {
                var label;
                function Base() { this.label = "base"; }
                function name() { return this.label; }
                function greet() { return "hi from " + this.name(); }
            }

            class Derived extends Base {
                function Derived() { super(); this.label = "derived"; }
                function name() { return "derived:" + this.label; }
            }

            class Other {
                function dotWith(v) { return -1; }
            }

            var a = new Vec(1, 2);
            var b = new Vec(3, 4);
            io.print(a.dotWith(b));
            io.print(a.describe());
            var o = new Other();
            io.print(o.dotWith(a));
            var d = new Derived();
            d.greet();
            a.dotWith(b);
            io.print(d.greet());
            io.print(new Base().greet());
            var dict = { "dotWith": 5 };
            io.print(dict.dotWith);
            """,
            "perf-class-fast-paths");
    }

    [Fact]
    public void ClassFastPaths_UseDirectFieldAndMethodAccess()
    {
        var generated = TranspileSource(
            """
            class Vec {
                var x;
                function Vec(x) { this.x = x; }
                function scaled(k) { return this.x * k; }
            }
            var v = new Vec(2);
            io.print(v.scaled(3));
            """);
        Assert.Contains("this.x = x;", generated);
        Assert.Contains("RuntimeHelpers.OperatorMultiply(this.x, k)", generated);
        Assert.Contains("is Vec __dm", generated);
    }

    [Fact]
    public void InferredDoubles_MatchInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var x = 0.0;
            var v = 1.0;
            var dt = 0.25;
            var steps = 0;
            while (steps < 8) {
                var a = -x;
                v = v + a * dt;
                x = x + v * dt;
                x += 1;
                steps = steps + 1;
            }
            io.print(x);
            io.print(v);
            io.print($"{x} and {v}");
            io.print(x > 1);

            var changes = 1.5;
            changes = "now a string";
            io.print(changes);

            function scale(k) {
                var f = 2.0;
                f = f * k;
                return f;
            }
            io.print(scale(3));
            io.print(1.5 / 0.5);
            """,
            "perf-inferred-doubles");
    }

    [Fact]
    public void InferredInts_MatchInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var total = 0;
            for (var i = 0; i < 10; i = i + 1) {
                for (var j = 0; j < 5; j++) {
                    if ((i + j) % 3 == 0) { total += 2; }
                }
            }
            io.print(total);

            var k = 7;
            k -= 2;
            k *= 3;
            k--;
            ++k;
            io.print(k);
            io.print(-k);
            io.print(k / 2);
            io.print(k / 2 * 2);
            io.print(k % 4);
            io.print(k == 15);
            io.print(k != 15);
            io.print(k > 14.5);
            io.print(k * 0.5);
            var avg = k / 4;
            io.print(avg);
            var scaled = 1.5;
            scaled = scaled * k;
            io.print(scaled);
            io.print($"k={k}");

            var big = 2147483000;
            try { big = big + 1000; } catch (e) { io.print("add overflow"); }
            try { big = big * 2; } catch (e) { io.print("mul overflow"); }
            var small = -2147483647;
            try { small--; small--; } catch (e) { io.print("dec overflow"); }
            var zero = 0;
            try { io.print(k % zero); } catch (e) { io.print("int mod zero"); }
            try { io.print(k / zero); } catch (e) { io.print("int div zero"); }
            var fzero = 0.0;
            try { io.print(scaled / fzero); } catch (e) { io.print("float div zero"); }
            try { io.print(scaled % fzero); } catch (e) { io.print("float mod zero"); }
            var hinted: float = 3.0;
            try { io.print(hinted / fzero); } catch (e) { io.print("hinted div zero"); }
            var halved = 9.0;
            halved /= 2;
            io.print(halved);

            var loose = 1;
            loose += 2;
            io.print(loose);
            var text = "a";
            text += "b";
            io.print(text);
            var mixed = 3;
            mixed = "now text";
            io.print(mixed);
            """,
            "perf-inferred-ints");
    }

    [Fact]
    public void InferredInts_EmitNativeIntArithmetic()
    {
        var generated = TranspileSource(
            """
            var total = 0;
            var i = 0;
            while (i < 100) {
                if (i % 3 == 0) { total += i; }
                i++;
            }
            var ratio = total / 7;
            var label = 0;
            label = "x";
            io.print(total);
            io.print(ratio);
            io.print(label);
            """);
        Assert.Contains("public static int total = 0;", generated);
        Assert.Contains("public static int i = 0;", generated);
        Assert.Contains("public static double ratio = 0d;", generated);
        Assert.Contains("public static object label = null!;", generated);
        Assert.Contains("while ((i < 100))", generated);
        Assert.Contains("(RuntimeHelpers.IntModulo(i, 3) == 0)", generated);
        Assert.Contains("total = RuntimeHelpers.CheckedIntAdd(total, i);", generated);
        Assert.Contains("i = RuntimeHelpers.CheckedIntIncrement(i)", generated);
    }

    [Fact]
    public void TypedFunctionTwins_MatchInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            function fib(n) {
                if (n < 2) { return n; }
                return fib(n - 1) + fib(n - 2);
            }
            function area(r) { return r * r * 3.5; }
            function half(x) { return x / 2; }
            function classify(n) {
                if (n > 0) { return n; }
                return "non-positive";
            }
            function maybe(n) {
                if (n > 0) { return 1; }
            }
            function twice(n) { n * 2; }
            function countdown(n) {
                var steps = 0;
                while (n > 0) { n = n - 1; steps = steps + 1; }
                return steps;
            }
            function shrink(n) {
                n = n / 2;
                return n;
            }
            function square(n) { return n * n; }
            function isEven(n) { if (n == 0) { return true; } return isOdd(n - 1); }
            function isOdd(n) { if (n == 0) { return false; } return isEven(n - 1); }
            function label(v) { return "v=" + v; }

            class Calc {
                function run(k) { return fib(k) + square(3); }
            }

            io.print(fib(20));
            io.print(area(2.0));
            io.print(half(3));
            io.print(half(2.5));
            io.print(classify(4));
            io.print(classify(-4));
            io.print(maybe(1));
            io.print(maybe(-1));
            io.print(twice(21));
            io.print(countdown(5));
            io.print(shrink(9));
            io.print(isEven(10));
            io.print(isOdd(7));
            io.print(label(3));
            io.print(label("x"));
            io.print(new Calc().run(10));
            var f = fib;
            io.print(f(12));
            var t = async fib(11);
            io.print(await t);
            var total = 0;
            for (var i = 0; i < 5; i++) { total = total + square(i); }
            io.print(total);
            try { io.print(square(100000)); } catch (e) { io.print("square overflow"); }
            """,
            "perf-typed-function-twins");
    }

    [Fact]
    public void TypedFunctionTwins_EmitTypedSignatures()
    {
        var generated = TranspileSource(
            """
            function fib(n) {
                if (n < 2) { return n; }
                return fib(n - 1) + fib(n - 2);
            }
            function area(r) { return r * r * 3.5; }
            function classify(n) {
                if (n > 0) { return n; }
                return "non-positive";
            }
            function label(v) { return "v=" + v; }
            io.print(fib(20));
            io.print(area(2.0));
            io.print(classify(3));
            io.print(label(1));
            io.print(label("a"));
            """);
        Assert.Contains("static int fib__Typed(int n)", generated);
        Assert.Contains("RuntimeHelpers.CheckedIntAdd(fib__Typed(RuntimeHelpers.CheckedIntSubtract(n, 1)), fib__Typed(RuntimeHelpers.CheckedIntSubtract(n, 2)))", generated);
        Assert.Contains("fib__Typed(20)", generated);
        Assert.Contains("static double area__Typed(double r)", generated);
        Assert.Contains("static object classify__Typed(int n)", generated);
        // A typed parameter that is only concatenated would just duplicate the sync twin.
        Assert.DoesNotContain("label__Typed", generated);
    }

    [Fact]
    public void JavaScriptConditionAndOperandShortcuts_MatchInterpreter()
    {
        // The JS emitter drops isTruthy around comparisons/!/&&/||, coerceToFloat around
        // numeric literals and `- * / %` results, and turns equals(x, literal) into === / == null.
        InterpretJsPair.AssertSameFromSource(
            """
            var a = 0;
            var s = "";
            var n = null;
            var arr = [];
            var d = {};
            if (a) { io.print("0 truthy"); }
            if (s) { io.print("empty truthy"); }
            if (n) { io.print("null truthy"); } else { io.print("null falsy"); }
            io.print(a == 0);
            io.print(n == null);
            io.print(null == n);
            io.print(s == "");
            io.print(a != 1);
            io.print(arr == null);
            io.print(d["missing"] == null);
            io.print(d["missing"] != null);
            io.print((a < 1) ? "lt" : "ge");
            io.print(!(a < 1));
            io.print((a < 1) && (s == ""));
            io.print((a > 1) || (n == null));
            io.print(-(3 - 5) * 2);
            io.print((7 - 2) % 3);
            io.print("x" + (2 * 3));
            var i = 0;
            while (i < 3 && i != 2) { i = i + 1; }
            io.print(i);
            """,
            "perf-js-shortcuts");
    }

    [Fact]
    public void InferredDoubles_OnlyForFloatOnlyNames()
    {
        var generated = TranspileSource(
            """
            var x = 0.5;
            x = x * 2;
            var i = 0;
            var s = 1.5;
            s = "text";
            io.print(x);
            io.print(i);
            io.print(s);
            """);
        Assert.Contains("public static double x = 0d;", generated);
        Assert.Contains("public static int i = 0;", generated);
        Assert.Contains("public static object s = null!;", generated);
    }
}
