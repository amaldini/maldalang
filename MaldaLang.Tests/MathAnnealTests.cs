// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class MathAnnealTests : TestBase
{
    [Fact]
    public void IdentityNeighbor_KeepsInitialState()
    {
        var output = RunProgram("""
            math.seed(1);
            var result = math.anneal(0.0, x => x * x, x => x, { steps: 5 });
            print(result.cost == 0.0);
            print(result.steps);
            print(result.state == 0.0);
            """);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("5", lines[1].Trim());
        Assert.Equal("true", lines[2].Trim());
    }

    [Fact]
    public void Quadratic_ImprovesFromFarStart()
    {
        var output = RunProgram("""
            math.seed(7);
            var result = math.anneal(
                10.0,
                x => (x - 3.0) * (x - 3.0),
                x => x + math.randomFloat(-1.0, 1.0),
                { steps: 400, temp: 2.0, cooling: 0.99 }
            );
            print(result.cost < 1.0);
            """);
        Assert.Equal("true", output.Trim());
    }

    [Fact]
    public void CoolingLambda_AndNamedFunctions()
    {
        var output = RunProgram("""
            math.seed(3);
            function energy(x) {
                return x * x;
            }
            function move(x) {
                return x + math.randomFloat(-0.5, 0.5);
            }
            var result = math.anneal(
                4.0,
                energy,
                move,
                { steps: 200, temp: 1.0, cooling: t => t * 0.97 }
            );
            print(result.cost < 4.0);
            """);
        Assert.Equal("true", output.Trim());
    }

    [Fact]
    public void Maximize_PrefersHigherCost()
    {
        var output = RunProgram("""
            math.seed(1);
            var result = math.anneal(
                0.0,
                x => x,
                x => x + 0.25,
                { steps: 20, temp: 0.0, maximize: true }
            );
            print(result.cost > 0);
            """);
        Assert.Equal("true", output.Trim());
    }

    [Fact]
    public void ArrayState_DoesNotMutateCaller()
    {
        var output = RunProgram("""
            math.seed(2);
            var start = [5.0, 5.0];
            function energy(xs) {
                return xs[0] * xs[0] + xs[1] * xs[1];
            }
            function move(xs) {
                xs[0] = xs[0] + math.randomFloat(-0.4, 0.4);
                xs[1] = xs[1] + math.randomFloat(-0.4, 0.4);
                return xs;
            }
            var result = math.anneal(start, energy, move, { steps: 80, temp: 1.0, cooling: 0.95 });
            print(start[0] == 5.0);
            print(result.cost < energy(start));
            """);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("true", lines[1].Trim());
    }

    [Fact]
    public void ArityError_NamesTheSignature()
    {
        var ex = Assert.ThrowsAny<Exception>(() => RunProgram("math.anneal(1.0);"));
        Assert.Contains("anneal() expects 3-4 arguments: (initial, cost, neighbor, options?)", ex.Message);
    }

    [Fact]
    public void CostMustBeFunction()
    {
        var ex = Assert.ThrowsAny<Exception>(() => RunProgram("math.anneal(1.0, 2.0, x => x);"));
        Assert.Contains("cost must be a function", ex.Message);
    }
}
