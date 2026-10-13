// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// return/break/continue unwind as a pending signal on blocks, if, loops and function
/// bodies, and as exceptions through try/finally, defer, match and other callers
/// (Interpreter.ControlFlow.cs). These pin the two paths meeting.
/// </summary>
[Collection("Sequential")]
public class InterpreterControlFlowTests : TestBase
{
    private string[] Lines(string source) =>
        RunProgram(source).Replace("\r", "").Trim().Split('\n');

    [Fact]
    public void Return_FromNestedLoopsAndBlocks()
    {
        var lines = Lines(@"
            function firstEven(xs) {
                for (var x in xs) {
                    if (x % 2 == 0) { return x; }
                }
                return -1;
            }
            function nested() {
                for (var i = 0; i < 5; i = i + 1) {
                    var j = 0;
                    while (j < 5) {
                        if (i * j == 6) { return """" + i + "","" + j; }
                        j = j + 1;
                    }
                }
                return ""none"";
            }
            function early() { while (true) { while (true) { return ""out""; } } }
            function deep(n) {
                if (n == 0) { return 0; }
                { { return 1 + deep(n - 1); } }
            }
            print(firstEven([1, 3, 6, 8]));
            print(firstEven([1, 3]));
            print(nested());
            print(early());
            print(deep(50));
        ");
        Assert.Equal(new[] { "6", "-1", "2,3", "out", "50" }, lines);
    }

    [Fact]
    public void BreakAndContinue_InForWhileAndForIn()
    {
        var lines = Lines(@"
            var s = """";
            for (var i = 0; i < 10; i = i + 1) {
                if (i % 2 == 0) { continue; }
                if (i > 7) { break; }
                s = s + i;
            }
            print(s);
            var k = 0;
            var w = """";
            while (k < 10) {
                k = k + 1;
                if (k == 3) { continue; }
                if (k == 6) { break; }
                w = w + k;
            }
            print(w);
            var f = """";
            for (var v in [1, 2, 3, 4, 5]) {
                if (v == 2) { continue; }
                if (v == 4) { break; }
                f = f + v;
            }
            print(f);
        ");
        Assert.Equal(new[] { "1357", "1245", "13" }, lines);
    }

    [Fact]
    public void ControlFlow_ThroughTryFinallyDeferAndCatch()
    {
        var lines = Lines(@"
            function withFinally() {
                try { return ""from-try""; } finally { print(""finally ran""); }
            }
            function loopTry() {
                var out = """";
                for (var i = 0; i < 5; i = i + 1) {
                    try {
                        if (i == 1) { continue; }
                        if (i == 3) { break; }
                        out = out + i;
                    } finally {
                        out = out + ""f"";
                    }
                }
                return out;
            }
            function withDefer() {
                defer { print(""deferred""); }
                if (true) { return ""after-defer""; }
                return ""no"";
            }
            function deferInLoop() {
                var n = 0;
                for (var i = 0; i < 3; i = i + 1) {
                    defer { n = n + 10; }
                    if (i == 1) { return n; }
                }
                return -1;
            }
            function catcher() {
                try { throw ""boom""; } catch (e) { return ""caught "" + e; }
                return ""no"";
            }
            print(withFinally());
            print(loopTry());
            print(withDefer());
            print(deferInLoop());
            print(catcher());
        ");
        Assert.Equal(
            new[] { "finally ran", "from-try", "0ff2ff", "deferred", "after-defer", "10", "caught boom" },
            lines);
    }

    [Fact]
    public void Return_FromMatchLambdaAndMethod_KeepsLastExpressionWins()
    {
        var lines = Lines(@"
            function matchRet(x) {
                match x {
                    case 1: { return ""one""; }
                    default: { print(""in default""); }
                }
                return ""other"";
            }
            class Box {
                var v;
                function Box(v) { this.v = v; }
                function get() {
                    if (this.v > 0) { return this.v; }
                    return 0;
                }
            }
            function lastExpr(a) { a * 2; }
            function noReturn() { var x = 1; }
            var fn = (x) => { if (x > 1) { return ""big""; } return ""small""; };
            print(matchRet(1));
            print(matchRet(2));
            print(new Box(5).get());
            print(fn(3));
            print(fn(0));
            print([1, 2, 3].map((v) => { if (v > 1) { return v * 10; } return v; }));
            print(lastExpr(21));
            print(noReturn());
        ");
        Assert.Equal(
            new[] { "one", "in default", "other", "5", "big", "small", "[1, 20, 30]", "42", "null" },
            lines);
    }
    /// <summary>Like DesktopInputProvider: the answer arrives later, from another thread.</summary>
    private sealed class DelayedInputProvider : IInputProvider
    {
        private readonly Queue<string> _answers;
        public DelayedInputProvider(params string[] answers) => _answers = new Queue<string>(answers);
        public int Requests { get; private set; }

        public Task<string> GetInputAsync(string prompt)
        {
            Requests++;
            var tcs = new TaskCompletionSource<string>();
            var answer = _answers.Dequeue();
            _ = Task.Run(async () =>
            {
                await Task.Delay(5);
                tcs.SetResult(answer);
            });
            return tcs.Task;
        }

        public bool HasQueuedInput() => false;
        public string GetQueuedInput() => "";
        public void QueueInput(string input) => _answers.Enqueue(input);
    }

    [Fact]
    public async Task Input_SuspendedInsideLoops_KeepsBreakContinueAndReturn()
    {
        var provider = new DelayedInputProvider("a", "skip", "b", "stop", "x", "quit");
        var interpreter = new MaldaLang.Interpreter.Interpreter(null, null, provider);
        var lexer = new Lexer(@"
            function collect() {
                var got = """";
                for (var i = 0; i < 10; i = i + 1) {
                    var a = input(""Q: "");
                    if (a == ""skip"") { continue; }
                    if (a == ""stop"") { break; }
                    got = got + a;
                }
                return got;
            }
            function untilQuit() {
                defer { print(""cleanup""); }
                var n = 0;
                while (true) {
                    var a = input(""Q: "");
                    if (a == ""quit"") { return ""quit after "" + n; }
                    n = n + 1;
                }
            }
            print(collect());
            print(untilQuit());
            print(""done"");
        ");
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        RedirectConsole();
        try
        {
            await interpreter.InterpretAsync(statements);
            var lines = GetOutput().Replace("\r", "").Trim().Split('\n');
            Assert.Equal(new[] { "ab", "cleanup", "quit after 1", "done" }, lines);
            Assert.Equal(6, provider.Requests);
        }
        finally
        {
            RestoreConsole();
        }
    }
}
