// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class AgenticEnhancementsTests : TestBase
{
    [Fact]
    public void Validate_AttachesAgentError()
    {
        var output = RunProgram("""
            schema Card { name: string; }
            var r = validate("Card", dict { "email": "x" });
            print(r.ok);
            print(r.agentError);
            """);
        Assert.Contains("false", output);
        Assert.Contains("SchemaMismatch", output);
    }

    [Fact]
    public void Within_TimesOutSleep()
    {
        Assert.Throws<MaldaLang.Interpreter.RuntimeException>(() =>
            RunProgram("""
                within (10ms) {
                    sleep(80);
                }
                print("no");
                """));
    }

    [Fact]
    public void Context_AddAndTurns()
    {
        var output = RunProgram("""
            context Session {
                budget: 800 tokens;
                pin: systemFacts;
                retain: last 2;
                evict: oldest;
            }
            var s = new Session();
            s.add("user", "hi");
            s.add("user", "there");
            print(s.turns.length >= 1);
            """);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Policy_DeniesShell()
    {
        Assert.Throws<MaldaLang.Interpreter.RuntimeException>(() =>
            RunProgram("""
                policy {
                    shell: deny;
                }
                var t = cap.shell("echo");
                cap.run(t);
                """));
    }

    [Fact]
    public void Stream_YieldsChunks()
    {
        var output = RunProgram("""
            var n = 0;
            for await (var chunk in stream("hello world from malda")) {
                n = n + 1;
            }
            print(n > 0);
            """);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Expect_Fails()
    {
        Assert.Throws<MaldaLang.Interpreter.RuntimeException>(() =>
            RunProgram("expect(false);"));
    }

    [Fact]
    public void CheckFix_RewritesFlatAlias()
    {
        var fixedSource = CheckFixer.Apply("print(1);\nsqrt(4);\n");
        Assert.Contains("io.print", fixedSource);
        Assert.Contains("math.sqrt", fixedSource);
    }

    [Fact]
    public void ToolSchema_WarnsUntyped()
    {
        var svc = new LanguageService();
        var diags = svc.GetDiagnostics("""
            @MCPTool("add", "add numbers")
            function add(a, b) { return a; }
            """);
        Assert.Contains(diags, d => d.Source == "malda-tools");
    }

    [Fact]
    public void Gotcha_ParseJson_Warns()
    {
        var svc = new LanguageService();
        var diags = svc.GetDiagnostics("var x = parseJson(\"{\");");
        Assert.Contains(diags, d => d.Source == "malda-gotcha");
    }

    [Fact]
    public void Grounded_InterpolationKeepsCitations()
    {
        var output = RunProgram("""
            var g = grounded.wrap("Ada", [{ "source": "wiki", "id": "1" }]);
            var s = $"Hello {g}";
            print(s.citations.length >= 1);
            """);
        Assert.Contains("true", output);
    }
}
