// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TargetDecoratorPartitionTests : TestBase
{
    [Fact]
    public void TranspileToCSharpFromSource_ExcludesClientOnlyFunctions()
    {
        var source = """
            @client()
            function browserOnly() {
                return "b";
            }

            function sharedByDefault() {
                return "ok";
            }
            """;

        var compiler = new Compiler.Compiler();
        var csharp = compiler.TranspileToCSharpFromSource(source);

        Assert.DoesNotContain("browserOnly(", csharp, StringComparison.Ordinal);
        Assert.Contains("sharedByDefault(", csharp, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileToJavaScriptFromSource_ExcludesServerAndRouteFunctions()
    {
        var source = """
            @server()
            function serverOnly() {
                return "s";
            }

            @GET("/api/health")
            function apiHealth() {
                return "ok";
            }

            @client()
            function browserOnly() {
                return "b";
            }
            """;

        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.DoesNotContain("function serverOnly()", js, StringComparison.Ordinal);
        Assert.DoesNotContain("function apiHealth()", js, StringComparison.Ordinal);
        Assert.Contains("function browserOnly()", js, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileToCSharpFromSource_DoesNotEmitCompileTimeTargetAttributes()
    {
        var source = """
            @client()
            function browserOnly() {
                return "b";
            }

            @shared()
            function sharedFn() {
                return "x";
            }
            """;

        var compiler = new Compiler.Compiler();
        var csharp = compiler.TranspileToCSharpFromSource(source);

        Assert.DoesNotContain("class clientAttribute", csharp, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class sharedAttribute", csharp, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FailsWhenClientDecoratorIsCombinedWithRouteDecorator()
    {
        var source = """
            @client()
            @GET("/api/test")
            function invalidRoute() {
                return "no";
            }
            """;

        var compiler = new Compiler.Compiler();
        var validation = compiler.Validate(source);

        Assert.False(validation.Success);
        Assert.Contains("client-only", validation.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranspileToCSharpFromSource_FailsWhenSharedUsesServerOnlyBuiltIn()
    {
        var source = """
            @shared()
            function invalidShared() {
                return readFile("x.txt");
            }
            """;

        var compiler = new Compiler.Compiler();
        var ex = Assert.Throws<Exception>(() => compiler.TranspileToCSharpFromSource(source));

        Assert.Contains("@shared()", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
