// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

public class MaldaDebugLaunchTests
{
    [Fact]
    public void Classify_HelloWorld_IsInterpret()
    {
        Assert.Equal(MaldaDebugLaunchKind.Interpret, MaldaDebugLaunch.Classify("println(\"hello\");"));
    }

    [Fact]
    public void Classify_MaldanoidExample_IsJavaScript()
    {
        var path = PlanningPaths.ResolveRepoPath("Examples", "Games", "maldanoid.malda");
        Assert.True(File.Exists(path), path);
        Assert.Equal(MaldaDebugLaunchKind.JavaScript, MaldaDebugLaunch.Classify(File.ReadAllText(path)));
    }

    [Fact]
    public void Classify_ClientAndServerDecorators_IsFullStack()
    {
        const string source = """
            @server()
            function api() { return 1; }

            @client()
            function ui() { return 2; }
            """;

        Assert.Equal(MaldaDebugLaunchKind.FullStack, MaldaDebugLaunch.Classify(source));
        Assert.True(JsBrowserApiDetector.UsesBrowserHost(source));
    }

    [Fact]
    public void Classify_ClientOnly_IsJavaScript()
    {
        const string source = """
            @client()
            function draw() { }
            """;

        Assert.Equal(MaldaDebugLaunchKind.JavaScript, MaldaDebugLaunch.Classify(source));
    }

    [Fact]
    public void Classify_ClientAndRouteDecorator_IsFullStack()
    {
        const string source = """
            @GET("/items")
            function items() { return []; }

            @client()
            function ui() { }
            """;

        Assert.Equal(MaldaDebugLaunchKind.FullStack, MaldaDebugLaunch.Classify(source));
    }

    [Fact]
    public void KeepHostStatements_DropsClientKeepsServerAndShared()
    {
        const string source = """
            @shared()
            function answer() { return 42; }

            @server()
            function boot() {
                println("host");
            }

            @client()
            function ui() {
                println("client");
            }
            """;

        var statements = Parse(source);
        var host = HostDebugPartition.KeepHostStatements(statements);
        var names = FunctionNames(host);

        Assert.Contains("answer", names);
        Assert.Contains("boot", names);
        Assert.DoesNotContain("ui", names);

        var boot = host.OfType<FunctionDeclaration>().Single(function => function.Name == "boot");
        var originalBoot = statements.OfType<FunctionDeclaration>().Single(function => function.Name == "boot");
        Assert.Equal(originalBoot.Line, boot.Line);
    }

    [Fact]
    public void KeepHostStatements_KeepsUnannotatedTopLevel()
    {
        const string source = """
            println("shared top");

            @client()
            function ui() { }

            @server()
            function boot() { }
            """;

        var host = HostDebugPartition.KeepHostStatements(Parse(source));
        Assert.Contains(host, statement => statement is not FunctionDeclaration);
        var names = FunctionNames(host.OfType<FunctionDeclaration>());
        Assert.Equal("boot", Assert.Single(names));
    }

    private static List<Statement> Parse(string source)
    {
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }

    private static List<string> FunctionNames(IEnumerable<Statement> statements)
    {
        return statements.OfType<FunctionDeclaration>().Select(function => function.Name).ToList();
    }
}
