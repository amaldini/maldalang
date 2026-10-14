// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Expressions without calls are evaluated without awaiting (Interpreter.SyncEval.cs) and
/// fall back to the async path when an operand is an object. These pin that both paths agree.
/// </summary>
[Collection("Sequential")]
public class InterpreterSyncEvalTests : TestBase
{
    private string[] Lines(string source) =>
        RunProgram(source).Replace("\r", "").Trim().Split('\n');

    [Fact]
    public void ObjectOperands_StillUseOperatorOverloads()
    {
        var lines = Lines(@"
            class V {
                var x;
                function V(x) { this.x = x; }
                function __add__(o) { return new V(this.x + o.x); }
                function __neg__() { return new V(0 - this.x); }
                function __eq__(o) { return this.x == o.x; }
                function __radd__(o) { return new V(o + this.x); }
            }
            var a = new V(2);
            var b = new V(3);
            var c = a + b;
            print(c.x);
            print((-a).x);
            print(a == new V(2));
            print((10 + a).x);
        ");
        Assert.Equal(new[] { "5", "-2", "true", "12" }, lines);
    }

    [Fact]
    public void ShortCircuit_NullCoalesce_AndMixedOperands()
    {
        var lines = Lines(@"
            var calls = 0;
            function bump() { calls = calls + 1; return true; }
            var n = null;
            print(n ?? ""dflt"");
            print(0 ?? ""zero-kept"");
            print(false and bump());
            print(true or bump());
            print(true and bump());
            print(calls);
            print(""s"" + 1 + 2.5);
            print(7 / 2);
            print(-7 % 3);
            print(1 == 1.0);
            print(!0);
        ");
        Assert.Equal(
            new[] { "dflt", "0", "false", "true", "true", "1", "s12.5", "3.5", "-1", "false", "false" },
            lines);
    }

    [Fact]
    public void CompoundAssignment_AndImplicitFields()
    {
        var lines = Lines(@"
            var t = 5;
            t += 2.5;
            print(t);
            t -= 1;
            t *= 2;
            t /= 4;
            print(t);
            var s = ""x"";
            s += 1;
            print(s);
            class Counter {
                var count;
                function Counter() { count = 0; }
                function tick(by) {
                    count = count + by;
                    var local = count * 2;
                    local += 1;
                    return local;
                }
            }
            var k = new Counter();
            print(k.tick(3));
            print(k.tick(4));
            print(k.count);
        ");
        Assert.Equal(new[] { "7.5", "3.25", "x1", "7", "15", "7" }, lines);
    }

    [Fact]
    public void Errors_MatchTheAsyncPath()
    {
        var lines = Lines(@"
            const K = 3;
            try { K = 4; } catch (e) { print(""const: "" + e); }
            try { print(1 / 0); } catch (e) { print(""div: "" + e); }
            try { print(2147483647 + 1); } catch (e) { print(""ovf: "" + e); }
            try { print(2147483647 * 2); } catch (e) { print(""mul: "" + e); }
            try { print(-(0 - 2147483647 - 1)); } catch (e) { print(""neg: "" + e); }
            try { print((0 - 2147483647 - 1) % -1); } catch (e) { print(""mod: "" + e); }
            try { print(undefinedThing + 1); } catch (e) { print(""undef: "" + e); }
            try { print(-""str""); } catch (e) { print(""negstr: "" + e); }
        ");
        Assert.Equal(new[]
        {
            "const: Cannot assign to const 'K'.",
            "div: Division by zero.",
            "ovf: Integer overflow.",
            "mul: Integer overflow.",
            "neg: Integer overflow.",
            "mod: Integer overflow.",
            "undef: Undefined variable 'undefinedThing'.",
            "negstr: Operand must be a number."
        }, lines);
    }

    [Fact]
    public void BlocksWithoutDeclarations_KeepScopeDeferAndControlFlow()
    {
        var lines = Lines(@"
            var total = 0;
            for (var i = 0; i < 5; i = i + 1) {
                if (i == 1) { continue; }
                if (i == 4) { break; }
                { total = total + i; }
            }
            print(total);

            function blocks() {
                print(""a"");
                {
                    defer { print(""d""); }
                    print(""b"");
                }
                print(""c"");
            }
            blocks();

            try { { if (true) var leaked = 1; } print(leaked); } catch (e) { print(""scoped: "" + e); }

            function firstOver(xs, limit) {
                for (var x in xs) {
                    if (x > limit) { if (true) { return x; } }
                }
                return -1;
            }
            print(firstOver([1, 5, 9], 4));

            function twice(n) { return n * 2; }
            var fns = [];
            var sum = 0;
            for (var k = 0; k < 3; k = k + 1) {
                { sum = sum + twice(k); fns.append(() => k); }
            }
            print(sum);
            print(fns[0]());

            const C = 1;
            try { if (true) { C = 2; } } catch (e) { print(""const: "" + e); }
        ");
        Assert.Equal(new[]
        {
            "5", "a", "b", "d", "c",
            "scoped: Undefined variable 'leaked'.",
            "5", "6", "3",
            "const: Cannot assign to const 'C'."
        }, lines);
    }
}
