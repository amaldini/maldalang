// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// A local binding (parameter, lambda parameter, local variable, loop / catch / pattern
/// variable, nested function) shadows a top-level user function of the same name inside its
/// scope, and only there. Interpreter, C# transpile and JS transpile must print the expected
/// output (each case runs on the three backends).
/// </summary>
[Collection("Sequential")]
public class LocalShadowingTests
{
    private const string Global = "function join(a) { return \"global\"; }\n";

    [Fact]
    public void Parameter_ShadowsTopLevelFunction() =>
        AssertAllBackends(Global + """
            function apply(join, x) { return join(x); }
            io.print(apply((v) => "param " + v, 1));
            function piped(join) { return 2 |> join; }
            io.print(piped((v) => "piped " + v));
            function apply2(join) { return join("a", "b"); }
            io.print(apply2((x, y) => "two " + x + y));
            """, "param 1\npiped 2\ntwo ab", "local-shadow-param");

    [Fact]
    public void LambdaParameter_ShadowsTopLevelFunction() =>
        AssertAllBackends(Global + """
            var g = (join) => join(2);
            io.print(g((v) => "lambda " + v));
            var h = (join, x) => { return join(x); };
            io.print(h((v) => "block-lambda " + v, 3));
            """, "lambda 2\nblock-lambda 3", "local-shadow-lambda");

    [Fact]
    public void LocalVariableHoldingLambda_ShadowsTopLevelFunction() =>
        AssertAllBackends(Global + """
            function f() {
                var join = (v) => "local " + v;
                return join(3);
            }
            io.print(f());
            """, "local 3", "local-shadow-var");

    [Fact]
    public void LoopCatchAndPatternVariables_ShadowTopLevelFunction() =>
        AssertAllBackends(Global + """
            for (var join in [(v) => "loop " + v]) {
                io.print(join(4));
            }
            try {
                throw "boom";
            } catch (join) {
                io.print("caught " + join);
            }
            var fnv = (v) => "pattern " + v;
            match fnv {
                case join: io.print(join(5));
            }
            io.print(join(0));
            """, "loop 4\ncaught boom\npattern 5\nglobal", "local-shadow-loop-catch-pattern");

    [Fact]
    public void NestedFunction_ShadowsTopLevelFunction() =>
        AssertAllBackends(Global + """
            function outer() {
                function join(a) { return "nested " + a; }
                return join(6);
            }
            io.print(outer());
            io.print(join(0));
            """, "nested 6\nglobal", "local-shadow-nested-function");

    [Fact]
    public void BlockShadow_EndsWithTheBlock() =>
        AssertAllBackends(Global + """
            function blk() {
                var out = "";
                if (true) {
                    var join = (v) => "block " + v;
                    out = join(7);
                }
                return out + " / " + join(8);
            }
            io.print(blk());
            """, "block 7 / global", "local-shadow-block");

    [Fact]
    public void Closure_CapturesOuterParameter() =>
        AssertAllBackends(Global + """
            function mk(join) { return (v) => join(v); }
            io.print(mk((v) => "closure " + v)(9));
            """, "closure 9", "local-shadow-closure");

    [Fact]
    public void ClassMethodParameter_ShadowsTopLevelFunction() =>
        AssertAllBackends(Global + """
            class K {
                function run(join) { return join(10); }
                function plain() { return join(11); }
            }
            var k = new K();
            io.print(k.run((v) => "method " + v));
            io.print(k.plain());
            """, "method 10\nglobal", "local-shadow-method");

    [Fact]
    public void TopLevelFunction_StillCalledBesideUnrelatedLocals() =>
        AssertAllBackends(Global + """
            function rev(other) {
                var tmp = 1;
                return join(other + tmp);
            }
            io.print(rev(1));
            var ref = join;
            io.print(ref(0));
            """, "global\nglobal", "local-shadow-reverse");

    [Fact]
    public void ParameterPassedAsValue_IsTheParameter() =>
        AssertAllBackends(Global + """
            function call1(f, x) { return f(x); }
            function pass(join) { return call1(join, 12); }
            io.print(pass((v) => "passed " + v));
            """, "passed 12", "local-shadow-value");

    [Fact]
    public void IndexExpressionCallee_IsCalled() =>
        AssertAllBackends("""
            var opts = { "action": (v) => "act " + v };
            io.print(opts["action"](5));
            var fs = [(v) => "first " + v];
            io.print(fs[0](6));
            """, "act 5\nfirst 6", "index-callee");

    [Fact]
    public void Analysis_MarksOnlyLocalValueUses()
    {
        var parser = new MaldaLang.Parser.Parser(new Lexer(Global + """
            function f(join) { return join(1); }
            function g() { function join(a) { return a; } return join(2); }
            var x = join(3);
            """).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        MaldaLang.BuiltIns.BuiltInShadowAnalysis.MarkLocalValueUses(statements);

        var marked = statements
            .SelectMany(s => MaldaLang.Parser.AST.AstNodeWalker.Descendants(s))
            .OfType<MaldaLang.Parser.AST.Expressions.IdentifierExpression>()
            .Where(id => id.Name == "join" && id.ResolvesToLocalValue)
            .ToList();
        var use = Assert.Single(marked);
        Assert.Equal(2, use.Line);
    }

    private static void AssertAllBackends(string source, string expected, string label)
    {
        var interpreted = TestBase.CaptureInterpretAsync(source).GetAwaiter().GetResult();
        Assert.Equal(expected, InterpretTranspilePair.Normalize(interpreted));
        InterpretTranspilePair.AssertSameFromSource(source, label);
        InterpretJsPair.AssertSameFromSource(source, label + "-js");
    }
}
