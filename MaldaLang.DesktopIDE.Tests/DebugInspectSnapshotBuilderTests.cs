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
