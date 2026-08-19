// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpretDebugInspectTests : TestBase
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

    private static DebugScope RequireScope(IReadOnlyList<DebugScope> scopes, string name)
    {
        var scope = scopes.FirstOrDefault(s => s.Name == name);
        Assert.NotNull(scope);
        return scope;
    }

    private static DebugVariable RequireVariable(IReadOnlyList<DebugVariable> variables, string name)
    {
        var variable = variables.FirstOrDefault(v => v.Name == name);
        Assert.NotNull(variable);
        return variable;
    }

    [Fact]
    public async Task Locals_InsideFunction_ContainsLocalNotOnlyGlobals()
    {
        const string source =
            "function f() {\nvar x = 41;\nprint(x);\n}\nf();\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 3);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        Assert.Equal(3, session.CurrentLine);

        var scopes = session.GetFrameScopes(1);
        var locals = RequireScope(scopes, "Locals");
        var localVars = session.GetVariables(locals.VariablesReference);
        var x = RequireVariable(localVars, "x");
        Assert.Equal("41", x.Value);
        Assert.DoesNotContain(localVars, v => v.Name == "math");

        var globals = RequireScope(scopes, "Globals");
        var globalVars = session.GetVariables(globals.VariablesReference);
        Assert.DoesNotContain(globalVars, v => v.Name == "x");

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Globals_HideMathStrIo_UnlessShowBuiltins()
    {
        const string source = "var user = 1;\nprint(user);\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        var scopes = session.GetFrameScopes(1);
        var globals = RequireScope(scopes, "Globals");
        var vars = session.GetVariables(globals.VariablesReference);
        Assert.Equal("1", RequireVariable(vars, "user").Value);
        Assert.DoesNotContain(vars, v => v.Name == "math");
        Assert.DoesNotContain(vars, v => v.Name == "str");
        Assert.DoesNotContain(vars, v => v.Name == "io");

        session.ShowBuiltins = true;
        scopes = session.GetFrameScopes(1);
        globals = RequireScope(scopes, "Globals");
        vars = session.GetVariables(globals.VariablesReference);
        Assert.Equal("1", RequireVariable(vars, "user").Value);
        Assert.Contains(vars, v => v.Name == "math");

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task NestedDictArray_LazyChildren()
    {
        const string source = "var d = dict { \"a\": [1, 2] };\nprint(d);\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        var scopes = session.GetFrameScopes(1);
        var globals = RequireScope(scopes, "Globals");
        var vars = session.GetVariables(globals.VariablesReference);
        var d = RequireVariable(vars, "d");
        Assert.True(d.VariablesReference > 0);

        var dChildren = session.GetVariables(d.VariablesReference);
        var a = RequireVariable(dChildren, "a");
        Assert.True(a.VariablesReference > 0);

        var aChildren = session.GetVariables(a.VariablesReference);
        Assert.Equal("1", RequireVariable(aChildren, "[0]").Value);
        Assert.Equal("2", RequireVariable(aChildren, "[1]").Value);

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetFrameScopes_ResetsHandles_ChildrenStillFoundByName()
    {
        const string source = "var d = dict { \"a\": [1, 2] };\nprint(d);\nprint(d);\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        session.SetBreakpoint(MainFile, 3);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (_, _) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        var firstD = RequireNestedDict(session);
        paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Continue();
        await WaitPausedAsync(paused);

        var secondD = RequireNestedDict(session);
        Assert.True(firstD > 0);
        Assert.True(secondD > 0);

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static int RequireNestedDict(DebugSession session)
    {
        var scopes = session.GetFrameScopes(1);
        var globals = RequireScope(scopes, "Globals");
        var vars = session.GetVariables(globals.VariablesReference);
        var d = RequireVariable(vars, "d");
        Assert.True(d.VariablesReference > 0);
        var dChildren = session.GetVariables(d.VariablesReference);
        var a = RequireVariable(dChildren, "a");
        var aChildren = session.GetVariables(a.VariablesReference);
        Assert.Equal("1", RequireVariable(aChildren, "[0]").Value);
        return d.VariablesReference;
    }

    [Fact]
    public async Task Watch_OnePlusTwo_PreviewIsThree()
    {
        const string source = "var user = 1;\nprint(user);\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        var watch = await session.EvaluateWatchAsync("1 + 2");
        Assert.Equal("3", watch.Value);
        Assert.Equal("1 + 2", watch.Name);

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Watch_LocalX_InPausedFunction()
    {
        const string source =
            "function f() {\nvar x = 41;\nprint(x);\n}\nf();\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 3);
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);

        var watch = await session.EvaluateWatchAsync("x");
        Assert.Equal("41", watch.Value);
        Assert.Equal("x", watch.Name);

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ConditionalBreakpoint_SkipsUntilTruthy()
    {
        const string source =
            "var x = 0;\nwhile (x < 3) {\nprint(x);\nx = x + 1;\n}\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 3, "x == 2");
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pauseCount = 0;
        session.Paused += (l, f) =>
        {
            pauseCount++;
            paused.TrySetResult();
        };

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        Assert.Equal(3, session.CurrentLine);

        var watch = await session.EvaluateWatchAsync("x");
        Assert.Equal("2", watch.Value);
        Assert.Equal(1, pauseCount);

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ConditionalBreakpoint_BadCondition_BreaksAndRaisesError()
    {
        const string source = "var x = 1;\nprint(x);\n";
        var statements = Parse(source, MainFile);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2, "no_such_var");
        var interpreter = new Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? conditionError = null;
        session.ConditionError += msg => conditionError = msg;
        session.Paused += (l, f) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await WaitPausedAsync(paused);
        Assert.Equal(2, session.CurrentLine);
        Assert.NotNull(conditionError);
        Assert.Contains("breakpoint condition error:", conditionError, StringComparison.Ordinal);

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
