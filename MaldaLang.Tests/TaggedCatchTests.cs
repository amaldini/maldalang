// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

public class TaggedCatchTests : TestBase
{
    [Fact]
    public void Catch_WithKindFilter_MatchesTaggedDict()
    {
        var source = """
            try {
                throw dict { "kind": "IO", "message": "disk full" };
            } catch (e if e.kind == "IO") {
                print("io:" + e.message);
            } catch (e) {
                print("other");
            }
            """;
        Assert.Equal("io:disk full", RunProgram(source).Trim());
    }

    [Fact]
    public void Catch_WithKindFilter_FallsThroughToGenericCatch()
    {
        var source = """
            try {
                throw dict { "kind": "Parse", "message": "bad token" };
            } catch (e if e.kind == "IO") {
                print("io");
            } catch (e) {
                print("generic:" + e.message);
            }
            """;
        Assert.Equal("generic:bad token", RunProgram(source).Trim());
    }

    [Fact]
    public void Catch_WithKindFilter_NoMatchingClause_Rethrows()
    {
        var source = """
            var handled = false;
            try {
                try {
                    throw dict { "kind": "Parse", "message": "x" };
                } catch (e if e.kind == "IO") {
                    handled = true;
                }
            } catch (e) {
                handled = true;
            }
            print(handled);
            """;
        Assert.Equal("true", RunProgram(source).Trim());
    }

    [Fact]
    public void Catch_WithoutFilter_StillHandlesAnyException()
    {
        var source = """
            try {
                throw "plain";
            } catch (e if e == "plain") {
                print("matched");
            }
            """;
        Assert.Equal("matched", RunProgram(source).Trim());
    }
}
