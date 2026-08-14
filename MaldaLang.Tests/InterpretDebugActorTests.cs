// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpretDebugActorTests : TestBase
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

    private static async Task WaitPausedAsync(TaskCompletionSource paused)
    {
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SpawnedActor_DoesNotShareHook_ParentPauseDoesNotDeadlock()
    {
        const string source =
            "actor Worker {\nfunction Worker() {\nprint(\"from actor\");\n}\n}\nvar w = spawn Worker();\nprint(\"parent\");\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession { StopOnEntry = true };
        session.SetBreakpoint(MainFile, 3);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var pauseCount = 0;
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) =>
        {
            pauseCount++;
            paused.TrySetResult();
        };

        RedirectConsole();
        try
        {
            var run = interpreter.InterpretAsync(statements);
            await WaitPausedAsync(paused);
            Assert.Equal(1, pauseCount);
            Assert.Equal(6, session.CurrentLine);

            session.Continue();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, pauseCount);
        }
        finally
        {
            RestoreConsole();
        }
    }
}
