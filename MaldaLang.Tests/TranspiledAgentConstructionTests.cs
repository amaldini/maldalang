// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Text;
using MaldaLang;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Transpile coverage for Agent / CodingAgent / OpenRouterClient construction
/// with object-typed function parameters (secondbrain helper patterns).
/// </summary>
[Collection("Sequential")]
public class TranspiledAgentConstructionTests
{
    [Fact]
    public void TranspiledOpenRouterClient_ModelFromVariable_CompilesAndRuns()
    {
        const string source = """
            function make(model) {
                return new OpenRouterClient(model);
            }
            var client = make("vendor/model");
            print(string(client != null));
            print(string(client.model));
            """;

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("true\nvendor/model", result.StdOut);
    }

    [Fact]
    public void TranspiledAgentAndCodingAgent_FromFunctionParams_CompilesAndRuns()
    {
        // Always pass an explicit client so tests never hit DefaultLocalLlm download.
        const string source = """
            function makeAgents(name, role, instructions, workDir) {
                var client = new OpenRouterClient("vendor/model");
                var plain = new Agent(name, role, instructions, client);
                var codingClient = new CodingAgent(name, role, instructions, client);
                var codingBoth = new CodingAgent(name, role, instructions, client, workDir);
                return string(plain != null) + "," + string(codingClient != null) + "," + string(codingBoth != null);
            }
            print(makeAgents("A", "role", "instructions", "."));
            """;

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("true,true,true", result.StdOut);
    }

    [Fact]
    public void TranspiledAgent_ThreeArg_CompilesWithDefaultLocalClient()
    {
        // Compile-only: constructing Agent() without a client uses DefaultLocalLlm at runtime.
        const string source = """
            function newPlainAgent(name, role, instructions) {
                return new Agent(name, role, instructions);
            }
            function newCodingAgent(name, role, instructions, workDir) {
                var client = null;
                if (client == null) {
                    return new CodingAgent(name, role, instructions, workDir);
                }
                return new CodingAgent(name, role, instructions, client, workDir);
            }
            """;

        AssertTranspileCompileSucceeds(source);

        var csharp = TranspileToCSharp(source);
        Assert.Contains("DefaultLocalLlm.GetDefaultLocalClient", csharp);
        Assert.Contains("RuntimeHelpers.CoerceToString", csharp);
        Assert.Contains("CodingAgentInstance", csharp);
    }

    [Fact]
    public void TranspiledFunction_EndingWithVoidCall_CompilesAndRuns()
    {
        // Last-expression-wins must not return void C# calls (print / AnsiConsole.markupLine).
        const string source = """
            function sayHi() {
                print("hi");
            }
            function sayMarkup() {
                AnsiConsole.markupLine("ok");
            }
            sayHi();
            sayMarkup();
            print("done");
            """;

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hi", result.StdOut);
        Assert.Contains("done", result.StdOut);
    }

    [Fact]
    public void TranspiledSecondBrainClientHelpers_Compile()
    {
        const string source = """
            function makeClient() {
                if (not io.hasEnv("OPENROUTER_API_KEY")) {
                    return null;
                }
                if (io.hasEnv("MALDA_BRAIN_MODEL")) {
                    var m = str.trim(string(io.getEnv("MALDA_BRAIN_MODEL")));
                    if (m != "") {
                        return new OpenRouterClient(m);
                    }
                }
                return new OpenRouterClient();
            }

            function newCodingAgent(name, role, instructions, workDir) {
                var client = makeClient();
                if (client == null) {
                    return new CodingAgent(name, role, instructions, workDir);
                }
                return new CodingAgent(name, role, instructions, client, workDir);
            }

            function newReaderAgent(name, role, instructions, brainDir) {
                var client = makeClient();
                var agent = null;
                if (client == null) {
                    agent = new Agent(name, role, instructions);
                } else {
                    agent = new Agent(name, role, instructions, client);
                }
                agent.addTool(createReadFileTool(brainDir));
                return agent;
            }

            function newPlainAgent(name, role, instructions) {
                var client = makeClient();
                if (client == null) {
                    return new Agent(name, role, instructions);
                }
                return new Agent(name, role, instructions, client);
            }
            """;

        AssertTranspileCompileSucceeds(source);
    }

    private static void AssertTranspileCompileSucceeds(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_agent_transpile_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "program.malda");
        var outputExe = Path.Combine(tempDir, "program.exe");
        File.WriteAllText(sourcePath, source, Encoding.UTF8);

        try
        {
            var result = new Compiler.Compiler().Compile(
                sourcePath,
                outputExe,
                CompilationMode.TranspileToCSharp,
                includeLLamaSharp: false,
                includeUiHost: false,
                profilingOptions: null,
                typedTranspileLevel: 1,
                includeOptionalPacks: true);

            if (!result.Success)
            {
                var errorPath = Path.Combine(tempDir, "build_errors.txt");
                var extra = File.Exists(errorPath) ? File.ReadAllText(errorPath) : "";
                Assert.Fail($"Transpile compile failed: {result.ErrorMessage}\n{extra}");
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    private static string TranspileToCSharp(string source)
    {
        var lexer = new Lexer(source, "agent_ctor_test.malda");
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens, "agent_ctor_test.malda");
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var transpiler = new CSharpTranspiler(profilingOptions: null, typedTranspileLevel: 1);
        return transpiler.Transpile(statements);
    }
}
