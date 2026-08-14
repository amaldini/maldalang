// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class InterpretDebugImportTests : TestBase
{
    private static List<Statement> ParseFile(string source, string sourceFileName)
    {
        var lexer = new Lexer(source, sourceFileName);
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
    public async Task Include_BreakpointInIncludedFile_PausesOnPrint()
    {
        var dir = CreateTempDirectory("interpret_debug_include_");
        var libPath = Path.GetFullPath(Path.Combine(dir, "lib.malda"));
        var mainPath = Path.GetFullPath(Path.Combine(dir, "main.malda"));
        await File.WriteAllTextAsync(libPath, "print(\"included\");\n");
        await File.WriteAllTextAsync(mainPath, "include \"lib.malda\";\n");

        var mainSource = await File.ReadAllTextAsync(mainPath);
        var statements = ParseFile(mainSource, mainPath);

        var session = new DebugSession();
        session.SetBreakpoint(libPath, 1);
        var interpreter = new Interpreter.Interpreter(session, mainPath);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        RedirectConsole();
        try
        {
            var run = interpreter.InterpretAsync(statements);
            await WaitPausedAsync(paused);

            Assert.Equal(1, session.CurrentLine);
            Assert.Equal(DebugSession.NormalizeFile(libPath), DebugSession.NormalizeFile(session.CurrentFile));
            session.Continue();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            RestoreConsole();
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Import_BreakpointInImportedFunctionBody_PausesOnHostCall()
    {
        var dir = CreateTempDirectory("interpret_debug_import_");
        var libPath = Path.GetFullPath(Path.Combine(dir, "lib.malda"));
        var mainPath = Path.GetFullPath(Path.Combine(dir, "main.malda"));
        await File.WriteAllTextAsync(libPath,
            "export function greet() {\nprint(\"from lib\");\n}\n");
        await File.WriteAllTextAsync(mainPath,
            "import { greet } from \"lib.malda\";\ngreet();\n");

        var mainSource = await File.ReadAllTextAsync(mainPath);
        var statements = ParseFile(mainSource, mainPath);

        var session = new DebugSession();
        session.SetBreakpoint(libPath, 2);
        var interpreter = new Interpreter.Interpreter(session, mainPath);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Paused += (l, f) => paused.TrySetResult();

        RedirectConsole();
        try
        {
            var run = interpreter.InterpretAsync(statements);
            await WaitPausedAsync(paused);

            Assert.Equal(2, session.CurrentLine);
            Assert.Equal(DebugSession.NormalizeFile(libPath), DebugSession.NormalizeFile(session.CurrentFile));
            session.Continue();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            RestoreConsole();
            SafeDeleteDirectory(dir);
        }
    }
}
