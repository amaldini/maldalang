// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpretDebugSessionTests : TestBase
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
    public async Task Breakpoint_HitsPrintLine_OneBased()
    {
        const string source = "var x = 1\nprint(x)\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        Assert.Equal(2, session.CurrentLine);
        Assert.NotEqual(1, session.CurrentLine);
        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Continue_AfterBreakpoint_CompletesProgram()
    {
        const string source = "var x = 1\nprint(x)\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        RedirectConsole();
        try
        {
            var run = interpreter.InterpretAsync(statements);
            await WaitPausedAsync(paused);
            Assert.Equal(2, session.CurrentLine);
            session.Continue();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("1", GetOutput());
        }
        finally
        {
            RestoreConsole();
        }
    }

    [Fact]
    public async Task StepOver_DoesNotEnterCallee()
    {
        const string source =
            "function inner() {\nprint(\"in\")\n}\nprint(\"before\")\ninner()\nprint(\"after\")\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 5);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        Assert.Equal(5, session.CurrentLine);

        paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();
        session.StepOver();
        await WaitPausedAsync(paused);

        Assert.Equal(6, session.CurrentLine);
        Assert.NotEqual(2, session.CurrentLine);
        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StepInto_PausesAtFirstBodyStatement_NotFunctionDeclaration()
    {
        const string source =
            "function inner() {\nprint(\"in\")\n}\ninner()\nprint(\"after\")\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 4);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        Assert.Equal(4, session.CurrentLine);

        paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();
        session.StepInto();
        await WaitPausedAsync(paused);

        Assert.Equal(2, session.CurrentLine);
        Assert.NotEqual(1, session.CurrentLine);
        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StepOut_ReturnsToCaller()
    {
        const string source =
            "function inner() {\nprint(\"in\")\nprint(\"in2\")\n}\ninner()\nprint(\"after\")\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        Assert.Equal(2, session.CurrentLine);

        paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();
        session.StepOut();
        await WaitPausedAsync(paused);

        Assert.Equal(6, session.CurrentLine);
        Assert.NotEqual(3, session.CurrentLine);
        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StopOnEntry_PausesOnFirstStoppableStatement()
    {
        const string source = "var x = 1\nprint(x)\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession { StopOnEntry = true };
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        Assert.Equal(1, session.CurrentLine);
        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CancelDuringPause_InterpretToken_CompletesCanceled()
    {
        const string source = "var x = 1\nprint(x)\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();
        using var cts = new CancellationTokenSource();

        var run = interpreter.InterpretAsync(statements, cts.Token);
        await WaitPausedAsync(paused);
        Assert.Equal(2, session.CurrentLine);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task StopDuringPause_CancelsInterpretTask()
    {
        const string source = "var x = 1\nprint(x)\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        session.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task FileQualifiedBreakpoint_HitsOnlyMatchingPath()
    {
        var dir = CreateTempDirectory("interpret_debug_");
        var file = Path.GetFullPath(Path.Combine(dir, "prog.malda"));
        var other = Path.GetFullPath(Path.Combine(dir, "other.malda"));
        const string source = "var x = 1\nprint(x)\n";
        var statements = Parse(source, file);

        var miss = new DebugSession();
        miss.SetBreakpoint(other, 2);
        var missPaused = false;
        miss.Paused += (l, f) => missPaused = true;
        var missInterp = new Interpreter.Interpreter(miss, file);
        await missInterp.InterpretAsync(statements).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(missPaused);

        var hit = new DebugSession();
        hit.SetBreakpoint(file, 2);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hit.Paused += (l, f) => paused.TrySetResult();
        var hitInterp = new Interpreter.Interpreter(hit, file);
        var run = hitInterp.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        Assert.Equal(2, hit.CurrentLine);
        hit.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        SafeDeleteDirectory(dir);
    }

    [Fact]
    public async Task DoesNotStopOnBlockStatement_FirstInnerStatementIsThePause()
    {
        const string source =
            "function inner() {\nprint(\"in\")\n}\ninner()\n";
        var statements = Parse(source, MainFile);
        Assert.Contains(statements, s => s is FunctionDeclaration);
        var function = Assert.IsType<FunctionDeclaration>(statements[0]);
        Assert.False(DebugStatementClassifier.IsStoppable(function));
        Assert.False(DebugStatementClassifier.IsStoppable(function.Body));
        Assert.True(DebugStatementClassifier.IsStoppable(function.Body.Statements[0]));

        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 4);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var pauseLines = new List<int>();
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) =>
        {
            pauseLines.Add(l);
            paused.TrySetResult();
        };

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        Assert.Equal(4, session.CurrentLine);

        paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StepInto();
        await WaitPausedAsync(paused);

        Assert.Equal(2, session.CurrentLine);
        Assert.DoesNotContain(1, pauseLines);
        Assert.DoesNotContain(function.Body.Line, pauseLines);
        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
