// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpretDebugPromptTests : TestBase
{
    private const string MainFile = "main.malda";

    private static List<Statement> Parse(string source, string? sourceFileName = null)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens, sourceFileName);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }

    [Fact]
    public async Task AwaitPrompt_EmitsWaitMessageBeforeThinkOrFailure()
    {
        const string source =
            "prompt ask(q) {\nuser: \"{q}\";\n}\nawait ask(\"hi\");\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession { StopOnEntry = false };
        var interpreter = new Interpreter.Interpreter(session, MainFile);

        // Fail-fast: no LLM client, so Think returns an error instead of downloading a model.
        var agent = new AgentInstance();
        agent.Initialize("TestAgent", "assistant", "test", null, null, null, null);
        interpreter._defaultAgent = agent;

        var programOutput = new List<string>();
        var debugOutput = new List<string>();
        interpreter.SetOutputCallback(s => programOutput.Add(s));
        session.Output += s => debugOutput.Add(s);

        try
        {
            await interpreter.InterpretAsync(statements).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception)
        {
            // Missing model / validation failure is OK; the wait message must already be emitted.
        }

        Assert.Contains(debugOutput, s => s.Contains("await prompt", StringComparison.Ordinal));
        Assert.DoesNotContain(programOutput, s => s.Contains("await prompt", StringComparison.Ordinal));
    }
}
