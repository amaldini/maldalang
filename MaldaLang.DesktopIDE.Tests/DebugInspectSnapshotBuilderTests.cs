// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

[Collection("Sequential")]
public class DebugInspectSnapshotBuilderTests
{
    private const string MainFile = "main.malda";

    [Fact]
    public async Task BuildScopes_LocalsContainFunctionVariable()
    {
        const string source =
            "function f() {\nvar x = 41;\nprint(x);\n}\nf();\n";
        var statements = Parse(source);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 3);
        var interpreter = new MaldaLang.Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (_, _) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var scopes = DebugInspectSnapshotBuilder.BuildScopes(session, 1);
        Assert.Contains(scopes, scope => scope.Name == "Locals");
        var locals = scopes.First(scope => scope.Name == "Locals");
        var variables = DebugInspectSnapshotBuilder.Expand(session, locals.VariablesReference);
        Assert.Contains(variables, variable => variable.Name == "x" && variable.Value == "41");

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void FromWatchError_KeepsExpressionAsName()
    {
        var node = DebugInspectSnapshotBuilder.FromWatchError("user.name", "Cannot parse watch expression: x", 1);
        Assert.Equal("user.name", node.Name);
        Assert.Equal("user.name = <Cannot parse watch expression: x>", node.Display);
        Assert.False(node.CanExpand);
    }

    [Fact]
    public async Task Expand_FindsNestedObjectByName_AfterHandleReset()
    {
        const string source =
            "var user = dict { \"name\": \"Ada\" };\nprint(user);\nprint(user);\n";
        var statements = Parse(source);
        var session = new DebugSession();
        session.SetBreakpoint(MainFile, 2);
        session.SetBreakpoint(MainFile, 3);
        var interpreter = new MaldaLang.Interpreter.Interpreter(session, MainFile);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (_, _) => paused.TrySetResult();

        var run = interpreter.InterpretAsync(statements);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var firstUserRef = RequireExpandable(session, "Globals", "user");
        var firstChildren = DebugInspectSnapshotBuilder.Expand(session, firstUserRef);
        Assert.Contains(firstChildren, child => child.Name == "name" && child.Value.Contains("Ada"));

        paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Continue();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondUserRef = RequireExpandable(session, "Globals", "user");
        Assert.True(secondUserRef > 0);
        var secondChildren = DebugInspectSnapshotBuilder.Expand(session, secondUserRef);
        Assert.Contains(secondChildren, child => child.Name == "name" && child.Value.Contains("Ada"));

        var watch = await session.EvaluateWatchAsync("user");
        var watchNode = DebugInspectSnapshotBuilder.FromVariable(watch, 1);
        Assert.Equal("user", watchNode.Name);
        Assert.True(watchNode.CanExpand);
        var watchChildren = DebugInspectSnapshotBuilder.Expand(session, watchNode.VariablesReference);
        Assert.Contains(watchChildren, child => child.Name == "name");

        session.Continue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static int RequireExpandable(DebugSession session, string scopeName, string variableName)
    {
        var scopes = DebugInspectSnapshotBuilder.BuildScopes(session, 1);
        var scope = Assert.Single(scopes, item => item.Name == scopeName);
        var variables = DebugInspectSnapshotBuilder.Expand(session, scope.VariablesReference);
        var variable = Assert.Single(variables, item => item.Name == variableName);
        Assert.True(variable.CanExpand);
        return variable.VariablesReference;
    }

    private static List<Statement> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens, MainFile);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }
}
