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
}
