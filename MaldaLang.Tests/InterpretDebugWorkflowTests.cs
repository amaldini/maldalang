// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Runtime.Workflows;
using Xunit;

namespace MaldaLang.Tests;

[Collection("WorkflowEngineSerial")]
public class InterpretDebugWorkflowTests : TestBase
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
    public async Task WorkflowStep_PausedInsideCallee_StackIncludesStepFrame()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"interpret_debug_wf_{Guid.NewGuid():N}.db");
        WorkflowEngine.ResetForTesting("Data Source=" + dbPath);

        const string source =
            "function addOne(x) {\nprint(x);\nreturn x + 1;\n}\nworkflow AddOne(input) {\nstep result = addOne(10);\n}\nvar id = startWorkflow(\"AddOne\", 10);\n";
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
            var frames = session.GetStackFrames();
            Assert.Contains(frames, f => f.FunctionName.StartsWith("step ", StringComparison.Ordinal));
            Assert.Contains(frames, f => f.FunctionName == "step result");

            session.Continue();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            RestoreConsole();
            try
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
            catch
            {
            }
        }
    }
}
