// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Compiler;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

public class PromptGatherTests : TestBase
{
    [Fact]
    public void ParsePromptDeclaration_ObjectLiteralGather()
    {
        var source = """
            schema Answer { text: string; }
            prompt research(q) -> Answer {
                gather: ["read_file", "grep"],
                user: "Question: {q}"
            }
            """;
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var prompt = Assert.IsType<PromptDeclaration>(statements[1]);
        Assert.Equal("research", prompt.Name);
        Assert.Equal("Answer", prompt.ReturnType);
        Assert.Equal(PromptBodyType.ObjectLiteral, prompt.BodyType);
    }

    [Fact]
    public void ParsePromptDeclaration_StatementBasedGather()
    {
        var source = """
            schema Answer { text: string; }
            prompt research(q) -> Answer {
                gather ["read_file", "grep"];
                system "Use tools.";
                user q;
            }
            """;
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var prompt = Assert.IsType<PromptDeclaration>(statements[1]);
        Assert.Equal(PromptBodyType.Statements, prompt.BodyType);
        Assert.NotNull(prompt.StatementBody);
        Assert.Contains(prompt.StatementBody!, s =>
            s is PromptBodyStatement body && body.Keyword == "gather");
    }

    [Fact]
    public void InvokePrompt_Gather_OfflineInstance_DoesNotCallModel()
    {
        var output = RunProgram("""
            schema ResearchAnswer {
                summary: string;
                sources: string[];
            }

            prompt research(question) -> ResearchAnswer {
                gather: ["read_file", "grep"];
                system: "Use tools, then extract.";
                user: "Question: {question}"
            }

            var inst = research("What is Mode C?");
            print("gather=" + toJSON(inst.gather));
            print("tools=" + toJSON(inst.tools));
            print("user=" + inst.user);
            """);

        Assert.Contains("gather=", output);
        Assert.Contains("read_file", output);
        Assert.Contains("grep", output);
        Assert.Contains("tools=null", output);
        Assert.Contains("Question: What is Mode C?", output);
        Assert.DoesNotContain("after 3 attempts", output);
    }

    [Fact]
    public void InvokePrompt_ModeB_ToolsUnchanged_GatherIsNull()
    {
        var output = RunProgram("""
            prompt researchWithTools(topic) -> Plan {
                system: "Research assistant.",
                user: "Investigate: {topic}",
                tools: ["read_file", "grep"]
            }

            var inst = researchWithTools("binding");
            print("tools=" + toJSON(inst.tools));
            print("gather=" + toJSON(inst.gather));
            """);

        Assert.Contains("read_file", output);
        Assert.Contains("gather=null", output);
    }

    [Fact]
    public async Task InvokePrompt_GatherWithoutReturnType_Throws()
    {
        await AssertRuntimeContains(
            """
            prompt research(q) {
                gather: ["read_file"];
                user: q
            }
            var inst = research("x");
            """,
            "requires a -> Type");
    }

    [Fact]
    public async Task InvokePrompt_GatherAndTools_Throws()
    {
        await AssertRuntimeContains(
            """
            schema Answer { text: string; }
            prompt research(q) -> Answer {
                gather: ["read_file"];
                tools: ["grep"];
                user: q
            }
            var inst = research("x");
            """,
            "cannot list both gather: and tools:");
    }

    [Fact]
    public async Task InvokePrompt_EmptyGather_Throws()
    {
        await AssertRuntimeContains(
            """
            schema Answer { text: string; }
            prompt research(q) -> Answer {
                gather: [];
                user: q
            }
            var inst = research("x");
            """,
            "non-empty array");
    }

    [Fact]
    public void GatherDiagnostics_WithoutReturnType_IsError()
    {
        var diagnostics = Analyze("""
            prompt research(q) {
                gather: ["read_file"];
                user: q
            }
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-prompt" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("requires a -> Type", StringComparison.Ordinal));
    }

    [Fact]
    public void GatherDiagnostics_CombinedWithTools_IsError()
    {
        var diagnostics = Analyze("""
            schema Answer { text: string; }
            prompt research(q) -> Answer {
                gather: ["read_file"];
                tools: ["grep"];
                user: q
            }
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-prompt" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("cannot list both gather: and tools:", StringComparison.Ordinal));
    }

    [Fact]
    public void GatherDiagnostics_ValidDeclaration_NoError()
    {
        var diagnostics = Analyze("""
            schema Answer { text: string; }
            prompt research(q) -> Answer {
                gather: ["read_file"];
                user: q
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-prompt");
    }

    [Fact]
    public async Task AwaitGather_WithNoLlmClient_FailsOnGatherStep()
    {
        SchemaRegistry.ClearForTesting();
        var source = """
            schema Answer { text: string; }
            prompt research(q) -> Answer {
                gather: ["read_file"];
                user: q
            }
            """;
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

            var promptDecl = Assert.IsType<PromptDeclaration>(statements[1]);
        var prompt = new PromptValue(promptDecl);
        var interpreter = new Interpreter.Interpreter();

        var agent = new AgentInstance();
        var defaultAgentField = typeof(Interpreter.Interpreter).GetField(
            "_defaultAgent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(defaultAgentField);
        defaultAgentField!.SetValue(interpreter, agent);

        var ex = await Assert.ThrowsAsync<RuntimeException>(() =>
            prompt.CallAsync(new System.Collections.Generic.List<RuntimeValue> { RuntimeValue.String("q") }, interpreter));

        Assert.Contains("Gather step of prompt 'research' failed", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("after 3 attempts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpiler_EmitsGatherThenExtract_ForGatherPrompt()
    {
        var source = """
            schema ResearchAnswer {
                summary: string;
                sources: string[];
            }

            prompt research(question) -> ResearchAnswer {
                gather: ["read_file", "grep"];
                user: "Question: " + question;
            }

            var result = await research("Mode C");
            print(result);
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_gather_prompt_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "gather_prompt.malda");
        var generatedPath = Path.Combine(tempDir, "GeneratedProgram.cs");
        File.WriteAllText(sourcePath, source);

        try
        {
            var compiler = new Compiler.Compiler();
            var csharpResult = compiler.CompileToCSharp(sourcePath, generatedPath);
            Assert.True(csharpResult.Success, csharpResult.ErrorMessage ?? "Transpile to C# failed.");
            Assert.True(File.Exists(generatedPath), "Expected GeneratedProgram.cs to be written.");

            var generated = File.ReadAllText(generatedPath);
            Assert.Contains("HasGather", generated);
            Assert.Contains("Gathered notes:", generated);
            Assert.Contains("gather", generated);
            Assert.Contains("research__ExecuteAsync(", generated);
            Assert.Contains("(gather == null || gather.Count == 0)", generated);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void OfflineExample_RunsWithoutLlm()
    {
        var examplePath = PlanningPaths.ResolveRepoFile("Examples", "Prompts", "prompt_tools_then_structured.malda");
        var source = File.ReadAllText(examplePath);
        var output = RunProgram(source);
        Assert.Contains("gather=", output);
        Assert.Contains("read_file", output);
        Assert.Contains("tools=null", output);
    }

    private static System.Collections.Generic.List<Diagnostic> Analyze(string source)
    {
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var diagnostics = new System.Collections.Generic.List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        return diagnostics;
    }

    private async Task AssertRuntimeContains(string source, string expected)
    {
        RedirectConsole();
        try
        {
            var parser = new Parser.Parser(new Lexer(source).Tokenize());
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);
            var interpreter = new Interpreter.Interpreter();
            var ex = await Assert.ThrowsAsync<RuntimeException>(async () => await interpreter.InterpretAsync(statements));
            Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            RestoreConsole();
        }
    }
}
