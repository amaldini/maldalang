// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Curated interpret vs JavaScript transpile pairs (same stdout).
/// Requires Node and <c>malda-js-runtime.js</c> (CI installs Node; local runs fail if missing).
/// </summary>
[Collection("Sequential")]
public class InterpretJsPairTests
{
    [Theory]
    [InlineData("Examples/Basics/schema_validate.malda")]
    [InlineData("Examples/Basics/as_variant.malda")]
    [InlineData("Examples/Modules/selective_import.malda")]
    public void Example_InterpretAndJavaScript_SameStdout(string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sourcePath = PlanningPaths.ResolveRepoFile(parts);
        InterpretJsPair.AssertSameFromFile(sourcePath, relativePath);
    }

    [Fact]
    public void HelloPrint_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print("hello-js-pair");
            """,
            "hello-js-pair");
    }

    [Fact]
    public void Interpolation_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var n = 3;
            io.print($"n is {n}");
            io.print("n is " + string(n));
            """,
            "interpolation-js");
    }

    /// <summary>
    /// A script var a function reads only inside an object literal must be module scope, not a
    /// <c>main()</c> local (the reference used to throw ReferenceError).
    /// </summary>
    [Fact]
    public void ScriptVarUsedInsideObjectLiteral_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var greeting = "hi";
            function make() {
                return { msg: greeting };
            }
            io.print(make().msg);
            """,
            "script-var-object-literal-js");
    }

    [Fact]
    public void Truthiness_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(TruthinessTests.CoreSource, "truthiness-js");
    }

    [Fact]
    public void MathAndStr_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            math.seed(1);
            io.print(math.abs(-4));
            io.print(math.sqrt(9));
            io.print(str.upper("ada"));
            io.print(str.lower("ADA"));
            io.print(toJSON(parseJSON("{\"k\":1}")));
            """,
            "math-str-json-js");
    }

    [Fact]
    public void MathMinMax_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print(math.max(10, 20));
            io.print(math.max(20, 10));
            io.print(math.min(10, 20));
            io.print(math.min(3.5, 1.25));
            io.print(math.max(1, 2.5));
            var values = [1, 4, 2];
            io.print(math.max(values));
            io.print(math.min(values));
            io.print(values.max());
            io.print(values.min());
            """,
            "math-min-max-js");
    }

    [Fact]
    public void MathAnneal_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            math.seed(1);
            var result = math.anneal(0.0, x => x * x, x => x, { steps: 5 });
            io.print(result.cost == 0.0);
            io.print(result.steps);
            """,
            "math-anneal-js");
    }

    [Fact]
    public void MatchConstIdentifier_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            const BUY = 1;
            const SELL = -1;
            var t = SELL;
            io.print(match t {
                case BUY: "BUY";
                case SELL: "SELL";
            });
            """,
            "match-const-identifier-js");
    }

    [Fact]
    public void MatchAndDict_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var d = dict { "k": 2 };
            io.print(d.k);
            var tag = match 2 {
                case 1: "one";
                case 2: "two";
                default: "other";
            };
            io.print(tag);
            """,
            "match-dict-js");
    }

    [Fact]
    public void Destructuring_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var [a, b, ...rest] = [1, 2, 3, 4];
            io.print(a);
            io.print(b);
            io.print(rest.length);
            var { name: n } = dict { "name": "Ada" };
            io.print(n);
            """,
            "destructuring-js");
    }

    [Fact]
    public void ResultMapNestedVariant_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var nested = result.map(result.ok(10), (x) => result.ok(x + 1));
            match nested {
                case Ok(inner):
                    match inner {
                        case Ok(n): io.print("nested:" + string(n));
                        default: io.print("inner-bare");
                    }
                default: io.print("outer-fail");
            }
            """,
            "result-map-nested-js");
    }

    [Fact]
    public void ClassExtends_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class Animal {
                var name;
                function Animal(name) {
                    this.name = name;
                }
                function label() {
                    return this.name;
                }
            }
            class Dog extends Animal {
                function Dog(name) {
                    super(name);
                }
            }
            var dog = new Dog("Rex");
            io.print(dog.label());
            """,
            "class-extends-js");
    }

    [Fact]
    public void ArrayExcept_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var a = [1, 2, 2, 3, 4];
            var b = [2, 4];
            var c = a.except(b);
            io.print(c.length);
            io.print(c.join(","));
            io.print(a.length);
            var kept = [1, 1, 2].except([2]);
            io.print(kept.join(","));
            var piped = [1, 2, 3, 4] |> except([2, 4]);
            io.print(piped.join(","));
            """,
            "array-except-js");
    }

    [Fact]
    public void ArrayPopShiftConcat_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var stack = [1, 2, 3];
            io.print(stack.pop());
            io.print(stack.length);
            io.print(stack.join(","));
            var queued = [10, 20, 30];
            io.print(queued.shift());
            io.print(queued.join(","));
            var left = [1, 2];
            var merged = left.concat([3, 4]);
            io.print(merged.join(","));
            io.print(left.join(","));
            var stack2 = [8, 9, 10];
            var piped = stack2 |> pop();
            io.print(piped);
            io.print(stack2.join(","));
            """,
            "array-pop-shift-concat-js");
    }

    [Fact]
    public void SubclassWithoutConstructor_ForwardsArgsToBase_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class Person(nome, cognome);
            class Operaio extends Person {}
            var p = new Operaio("Mario", "Rossi");
            io.print(p.nome);
            """,
            "subclass-forwards-primary-ctor-js");
    }

    [Fact]
    public void SubclassWithoutConstructor_ForwardsThroughLevels_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class A {
                var tag;
                function A(v) { this.tag = "A:" + string(v); }
            }
            class B extends A { }
            class C extends B { }
            var c = new C("deep");
            io.print(c.tag);
            """,
            "subclass-forwards-deep-js");
    }

    [Fact]
    public void SuperWithNoAncestorConstructor_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class A {
                var x = 5;
            }
            class B extends A {
                function B() { super(); }
            }
            var b = new B();
            io.print("x=" + string(b.x));
            """,
            "super-no-ancestor-ctor-js");
    }

    [Fact]
    public void PrivateBaseFieldViaInheritedMethod_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class A {
                private var x;
                function A() { this.x = 42; }
                public function getX() { return this.x; }
            }
            class B extends A {
                function B() { super(); }
            }
            var b = new B();
            io.print("x=" + string(b.getX()));
            """,
            "private-base-field-js");
    }

    [Fact]
    public void PrimaryConstructor_BracelessMethod_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class Point(x, y) function total() this.x + this.y;
            var p = new Point(3, 4);
            io.print(p.x);
            io.print(p.total());
            """,
            "primary-constructor-braceless-js");
    }

    [Fact]
    public void ImplicitField_BeatsOuterVar_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var x = 10;
            class Point(x, y) {
                function total() {
                    return x + y;
                }
            }
            io.print(new Point(2, 3).total());
            """,
            "implicit-field-beats-outer-var-js");
    }

    [Fact]
    public void ClassAugmentation_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class Point(x, y);
            class Point {
                function total() {
                    return this.x + this.y;
                }
            }
            var p = new Point(3, 4);
            io.print(p.x);
            io.print(p.total());
            """,
            "class-augmentation-js");
    }

    [Fact]
    public void ClassAugmentation_CompactMethod_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class Point(x, y);
            class Point { function total() this.x + this.y; }
            var p = new Point(3, 4);
            io.print(p.x);
            io.print(p.total());
            """,
            "class-augmentation-compact-js");
    }

    [Fact]
    public void ClassAugmentation_Braceless_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class Point(x, y);
            class Point function doubled() this.x * 2;
            var p = new Point(3, 4);
            io.print(p.x);
            io.print(p.doubled());
            """,
            "class-augmentation-braceless-js");
    }

    [Fact]
    public void ToJSON_ClassInstancePublicFields_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            class Point(x, y);
            var d = parseJSON(toJSON(new Point(3, 4)));
            io.print(d.x);
            io.print(d.y);
            """,
            "tojson-class-js");
    }

    [Fact]
    public void StrTextAndDictMethods_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print("[" + str.text(null) + "][" + str.text(12) + "]");
            io.print("[" + str.trimText("  a  ") + "][" + str.trimText(null) + "]");
            var d = {"a": 1, "b": 2};
            io.print(str.join(d.keys(), ","));
            io.print(string(d.values().length));
            io.print(string(d.containsKey("a")) + " " + string(d.containsKey("z")));
            class Bag {
                function keys() { return "own"; }
            }
            io.print(new Bag().keys());
            """,
            "str-text-dict-methods-js");
    }

    [Fact]
    public void InfinitySurvivesArithmetic_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var big = 1.0e308 * 10.0;
            io.print(string(big > 1.0e308));
            io.print(string((big - 1.0) > 0.0));
            io.print(string(-big < 0.0));
            """,
            "infinity-arithmetic-js");
    }

    [Fact]
    public void ActorFieldsAndHelperFunctions_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            actor Counter {
                var count = 0;
                function double(n) {
                    return n * 2;
                }
                on increment() {
                    count = double(count + 1);
                    io.print("count " + string(count));
                }
            }

            var c = spawn Counter();
            sleep(100);
            send c.increment();
            send c.increment();
            sleep(300);
            """,
            "actor-fields-helpers-js");
    }

    [Fact]
    public void AllFailedBranchIsUpstreamSlot_AndErrorBuiltin_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            function boom() {
                error("boom");
            }
            var t1 = async boom();
            var t2 = async 2;
            var results = await all(t1, t2);
            match results[0] {
                case Upstream(status, body): io.print("upstream " + string(status) + " " + body);
                default: io.print("not-upstream");
            }
            io.print(results[1]);
            try {
                error("direct");
            } catch (e) {
                io.print("caught: " + string(e));
            }
            """,
            "all-upstream-error-js");
    }

    [Fact]
    public void ActorConstructorCallsSyncHelper_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            actor Box {
                var label = "";
                function Box(name) {
                    label = decorate(name);
                }
                function decorate(s) {
                    return "[" + s + "]";
                }
                on show() {
                    io.print(label);
                    io.print(decorate("again"));
                }
            }
            var b = spawn Box("x");
            sleep(100);
            send b.show();
            sleep(300);
            """,
            "actor-ctor-helper-js");
    }

    [Fact]
    public void MultiArgLambdasThroughVariables_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var add3 = (a, b, c) => a + b + c;
            io.print(add3(1, 2, 3));
            function apply2(f, x, y) { return f(x, y); }
            io.print(apply2((p, q) => p - q, 9, 4));
            """,
            "multi-arg-lambdas-js");
    }

    [Fact]
    public void ContinueInNestedAndTwoStatementLoops_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            InterpreterControlFlowTests.ContinueInNestedAndTwoStatementLoopsSource,
            "continue-nested-loops-js");
    }

    [Fact]
    public void MathRound_IsBankersRounding_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print([math.round(-1.5), math.round(-2.5), math.round(0.5), math.round(1.5)]);
            io.print([math.round(2.5), math.round(2.6), math.round(-2.6), math.round(7)]);
            """,
            "math-round-js");
    }

    [Fact]
    public void AnonymousFunctionAndTrailingCommas_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            ParserSyntaxGapTests.AnonymousFunctionSource,
            "anonymous-function-js");
    }

    // ---- I7: pure stdlib on JavaScript (one test per group) ----

    [Fact]
    public void MathStdLib_ArraysClampHypotAndExactOps_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var xs = [5, -3, 12, 7];
            var ds = [0.5, -3.75, 2, 10.0];
            io.print([math.sum(xs), math.sum(ds), math.average(xs), math.max(ds), math.min(xs)]);
            io.print([math.argmax(ds), math.argmin(xs), math.dot(xs, xs), math.dot(ds, xs)]);
            io.print([math.clamp(15.5, 10, 0), math.clamp(-2, 0, 10), math.hypot(3, 4.5), math.hypot(1, 1)]);
            io.print([math.trunc(-2.75), math.sign(-0.5), math.pow(7, 3), math.rsqrt(4.5)]);
            io.print([math.degToRad(45), math.radToDeg(1.25), math.floor(-0.5), math.ceil(1.25)]);
            io.print(math.zeros(2, 3));
            io.print(math.transpose([[1, 2, 3], [4, 5, 6]]));
            io.print(math.matmul([[1, 2], [3, 4]], [[5, 6], [7, 8]]));
            """,
            "i7-math-js");
    }

    [Fact]
    public void StrStdLib_SplitPadRegexAndCase_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print(str.split("x y  z"));
            io.print(str.split("a1b22c3", "[0-9]+"));
            io.print(str.split("a1b22c", "([0-9]+)", true));
            io.print(str.split("a,b,,c", ","));
            io.print(str.split("abc", ""));
            io.print(str.padStart("ab", 5, "xy") + "|" + str.padEnd("ab", 4, "-") + "|" + str.padStart("abcdef", 3));
            io.print(str.regexReplace("Hello world", "[aeiou]", "<$0>"));
            io.print(str.regexReplace("a1b22", "([a-z])([0-9]+)", "$2$1 $$"));
            io.print(toJSON(str.regexFind("a1 b22 c", "([a-z])([0-9]+)?")));
            io.print(str.regexMatch("Zed", "^[A-Z]"));
            io.print(str.join([1, null, "q", 2.5, true], "|") + " " + str.join([1, 2], 5));
            io.print(str.includes([1, "x", null], null) + " " + str.includes([1, 2], "2"));
            io.print(str.upper("straße héllo ǆ") + " " + str.lower("HÉLLO Wörld ΣΑΣ"));
            io.print("[" + str.trim(" \t x \n") + "][" + str.trimText(null) + "][" + str.text(12) + "]");
            io.print(str.repeat("ab", 3) + str.replace("aaa", "aa", "b"));
            """,
            "i7-str-js");
    }

    [Fact]
    public void StrStdLib_TextSimilarityHelpers_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print(str.normalizeText("  Héllo, Wörld!  "));
            io.print(str.normalizeText("A-B c", { "removePunctuation": false, "lowercase": false }));
            io.print(str.tokenize("naïve café, OK"));
            var overlap = str.tokenOverlap("the cat sat", "the dog sat down");
            io.print(toJSON(overlap));
            io.print(typeOf(overlap));
            io.print([str.similarity("the cat", "the cat sat"), str.similarity("", "")]);
            io.print([str.similarity("hello", "help", "char-ngram"), str.similarity("ab", "abc", "char-ngram")]);
            io.print([str.similarity("hello world", "world", "contains"), str.similarity("Héllo", "hello", " EXACT ")]);
            io.print(str.extractNumbers("x12 y-3.75 z4,25 big99999999999 -0 3,"));
            """,
            "i7-str-text-js");
    }

    [Fact]
    public void EncodingAndHashBuiltIns_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print(str.base64Encode("Hi there é €😀"));
            io.print(str.base64Decode(" aGk = ") + str.base64Decode(str.base64Encode("x y")));
            io.print(base64Encode("") + "|" + base64Decode("") + "|");
            io.print(str.md5("") + " " + str.md5("The quick brown fox jumps over the lazy dog"));
            io.print(str.sha256("") + " " + sha256("abc€"));
            io.print(md5("é"));
            io.print(urlEncode("a b&c=d/é!'()*~-_."));
            io.print(urlDecode("%E2%82%AC%zz%41+%") + " " + urlDecode("%C3%A9%C3") + " " + urlDecode("%e9"));
            io.print(urlDecode(urlEncode("x y+z😀")));
            """,
            "i7-encoding-js");
    }

    [Fact]
    public void ConversionAndTypeBuiltIns_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print([toIntOr(" 42 ", 0), toIntOr("+7", 0), toIntOr("x", -1), toIntOr("3.7", 0), toIntOr("-3.75", 0)]);
            io.print([toIntOr("", 9), toIntOr(null, 9), toIntOr("1e3", 0), toIntOr(".5", 9), toIntOr("0x10", 9), toIntOr(true, 0)]);
            io.print([toIntOrNull("1e2"), toIntOrNull("abc"), toIntOrNull(" "), toIntOrNull("-8")]);
            io.print([formatNumber(2.5, 0), formatNumber(3.5, 0), formatNumber(-2.5, 0), formatNumber(0.125, 2)]);
            io.print([formatNumber(0.625, 2), formatNumber(1.005, 2), formatNumber(-0.001, 2), formatNumber(12, 3)]);
            io.print([formatNumber(123456789.123, 1), formatNumber(1e21, 1), formatNumber(7, 0)]);
            io.print([isString("a"), isString(1), isArray([1]), isArray("a"), isObject({ "a": 1 }), isObject([1])]);
            """,
            "i7-conversion-js");
    }

    [Fact]
    public void CollectionBuiltIns_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            var xs = [5, -3, 12, 7];
            io.print(reverse(xs));
            io.print(xs);
            io.print(sort([0.5, -3.75, 2, 10.0]));
            io.print(sort(["pear", "fig", "apple", "kiwi"]));
            io.print(sort(xs, (a, b) => b - a));
            io.print(sort(xs, (a, b) => a > b));
            io.print([includes(xs, 7), includes(["a", "b"], "c"), includes([[1]], [1])]);
            io.print(range(2, 11, 3));
            io.print(range(5, 0, -2));
            io.print(toCsv([{ "a": 1, "b": "x,y", "c": 2.5, "d": [1, 2] }, { "a": -3, "b": "q\"r", "c": null }]));
            io.print(toCsv(xs));
            io.print(toCsv([{ "a": 1, "b": 2 }], ["b", "a", "zz"], { "delimiter": ";", "includeHeader": false }));
            io.print(toCsv([{ "a": "x" }], null, { "quoteAll": true, "newline": "\r\n" }));
            """,
            "i7-collections-js");
    }

    [Fact]
    public void JsonBuiltIns_EscapingNumbersAndOrder_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            io.print(toJSON({ "s": "a<b> & \"c\" é +'`", "n": [1, 2.5, -3.75, null, true, 1e20, 0.1], "d": { "k": "v" } }));
            io.print(toJSON(["a", (x) => x, 2.0]));
            class P { var x = 1; var y = "z"; function P() {} function m() { return 1; } }
            io.print(toJSON(new P()));
            var parsed = parseJSON("{\"b\": 1.0, \"a\": [1, 2.5, {\"y\": null}], \"c\": 1e3}");
            io.print(toJSON(parsed));
            io.print(typeOf(parsed) + " " + isObject(parsed) + " " + parsed.c);
            io.print(parseJSON(toJSON({ "a": [1, 2], "b": "x" })).b);
            """,
            "i7-json-js");
    }

    [Fact]
    public void AssertAndExpect_ThrowCatchableErrors_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            """
            try { assert(1 > 2, "nope"); io.print("held"); } catch (e) { io.print("caught " + e); }
            try { assert(true); io.print("held"); } catch (e) { io.print("caught " + e); }
            try { assert(false); } catch (e) { io.print("caught " + e); }
            try { expect(false); } catch (e) { io.print("caught " + e); }
            try { expect(1 == 1, "fine"); io.print("held"); } catch (e) { io.print("caught " + e); }
            """,
            "i7-assert-js");
    }

    [Fact]
    public void TrailingCommaParams_SameStdout()
    {
        InterpretJsPair.AssertSameFromSource(
            ParserSyntaxGapTests.TrailingCommaParamsSource,
            "trailing-comma-params-js");
    }
}
