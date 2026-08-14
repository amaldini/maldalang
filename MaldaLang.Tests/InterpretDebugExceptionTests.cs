// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpretDebugExceptionTests : TestBase
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
    public async Task UncaughtThrow_PausesWithExceptionReason_ThenRethrowsAfterContinue()
    {
        const string source = "throw \"boom\";\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession { StopOnEntry = false };
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        Assert.Equal("exception", session.LastStopReason);
        Assert.Equal(1, session.CurrentLine);
        Assert.Contains("boom", session.LastStopText ?? "", StringComparison.Ordinal);
        Assert.Contains("boom", session.ExceptionMessage ?? "", StringComparison.Ordinal);

        session.Continue();
        await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task UncaughtUndefined_PausesOnErrorLine()
    {
        const string source = "print(no_such);\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession { StopOnEntry = false };
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        Assert.Equal("exception", session.LastStopReason);
        Assert.Equal(1, session.CurrentLine);

        session.Continue();
        await Assert.ThrowsAsync<RuntimeException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task CaughtThrow_DoesNotPause()
    {
        const string source =
            "try {\nthrow \"boom\";\n} catch (e) {\nprint(e);\n}\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession { StopOnEntry = false };
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = false;
        session.Paused += (l, f) => paused = true;

        RedirectConsole();
        try
        {
            await interpreter.InterpretAsync(statements).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(paused);
            Assert.NotEqual("exception", session.LastStopReason);
        }
        finally
        {
            RestoreConsole();
        }
    }
}
