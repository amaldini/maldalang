// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Text;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Runtime.Actors;
using MaldaLang.Tests.Conformance.Tier0;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>@worker()</c> actors (J4): a Web Worker on JavaScript, a thread on the interpreter and C#,
/// with values crossing by copy (structured clone), <c>buf.transfer</c> moves, send-time errors,
/// replies, timeouts, handler errors and stop behaving the same on the three backends.
/// </summary>
[Collection("Sequential")]
public class WorkerActorTests
{
    private const string CrossLine = "cannot cross the boundary of worker actor 'Crunch': only null, bools, numbers, strings, arrays, dicts, variants and buffers can.";

    /// <summary>
    /// Every crossing rule in one run. The page's callbacks queue behind <c>start</c>, so they print
    /// after "stopped"; the worker's own print arrives while <c>start</c> sleeps.
    /// </summary>
    private const string Program = """
        const SCALE = 2;
        function scaled(x) { return x * SCALE; }
        type Shape = Circle(r) | Square(side);
        class Point {
            var x = 0;
            function Point(x0) { x = x0; }
        }

        actor Echo {
            on twice(v) { io.print($"echo {v * 2}"); }
        }

        @worker()
        actor Crunch {
            var label;
            function Crunch(name, options) {
                label = name + options["suffix"];
            }
            on sum(values) {
                var total = 0;
                for (var v in values) { total = total + v; }
                values.append(99);
                reply($"{label}: {scaled(total)}");
            }
            on shapes(shape, table) {
                reply([shape, table.keys(), table["a"] + 1, table["b"]]);
            }
            on alias(pair) {
                pair[0].append(2);
                reply(pair[1].length);
            }
            on fill(data) {
                for (var i = 0; i < data.length; i = i + 1) { data[i] = i * 1.5; }
                reply(buf.transfer(data));
            }
            on viaEcho(v) {
                var echo = spawn Echo();
                send echo.twice(v);
            }
            on fail(n) {
                if (n < 0) { throw "negative input"; }
                reply(n);
            }
            on silent() {
            }
        }

        actor Page {
            on start() {
                var crunch = spawn Crunch("crunch", { "suffix": "!" });
                var xs = [1, 2, 3];
                send crunch.sum(xs) then (r) { io.print(r); io.print(xs); };
                send crunch.shapes(Circle(2), { "a": 1, "b": [true, null] }) then (r) { io.print(r); };
                var inner = [1];
                send crunch.alias([inner, inner]) then (r) { io.print($"aliased length {r}, sender still {inner.length}"); };
                var data = buf.float64(4);
                send crunch.fill(buf.transfer(data)) then (filled) { io.print(filled); };
                io.print($"sender buffer length {data.length}");
                try { send crunch.sum(function (x) { return x; }); } catch (e) { io.print(e); }
                try { send crunch.sum([self]); } catch (e) { io.print(e); }
                try { send crunch.sum(new Point(1)); } catch (e) { io.print(e); }
                send crunch.viaEcho(21);
                send crunch.fail(-1) then (r) { io.print("unexpected"); } timeout 300 catch (e) { io.print("fail timed out"); };
                send crunch.fail(5) then (r) { io.print($"still alive {r}"); };
                send crunch.silent() then (r) { io.print("never"); } timeout 200 catch (e) { io.print("silent timed out"); };
                sleep(500);
                send crunch.stop();
                send crunch.sum([1]) then (r) { io.print("after stop"); };
                io.print("stopped");
            }
        }

        var page = spawn Page();
        send page.start();
        sleep(900);
        io.print("done");
        """;

    private static readonly string ExpectedOutput = $"""
        sender buffer length 0
        A function {CrossLine}
        An actor reference {CrossLine}
        A class instance or host object {CrossLine}
        echo 42
        stopped
        crunch!: 12
        [1, 2, 3]
        [Circle(2), [a, b], 2, [true, null]]
        aliased length 2, sender still 1
        [0, 1.5, 3, 4.5]
        still alive 5
        silent timed out
        fail timed out
        done
        """;

    [Fact]
    public void Parser_WorkerDecorator_MarksTheActor()
    {
        var statements = Parse("@worker()\nactor W { on go() { } }\nactor L { on go() { } }");

        var actors = statements.OfType<ActorDeclaration>().ToList();
        Assert.True(actors[0].IsWorker);
        Assert.False(actors[1].IsWorker);
    }

    [Fact]
    public void Parser_OtherDecoratorOnAnActor_IsAnError()
    {
        var parser = new Parser.Parser(new Lexer("@pure()\nactor W { on go() { } }").Tokenize());
        parser.Parse();

        Assert.Contains(parser.Errors, e => e.ToString()!.Contains("'@pure' is not an actor decorator. An actor takes only '@worker()'."));
    }

    [Fact]
    public async Task Interpreter_PrintsTheExpectedOutput()
    {
        var output = await TestBase.CaptureInterpretAsync(Program);

        Assert.Equal(InterpretTranspilePair.Normalize(ExpectedOutput), InterpretTranspilePair.Normalize(output));
    }

    [Fact]
    public void CSharpTranspile_MatchesTheInterpreter()
    {
        InterpretTranspilePair.AssertSameFromSource(Program, "worker-csharp");
    }

    [Fact]
    public void JavaScript_MatchesTheInterpreter()
    {
        InterpretJsPair.AssertSameFromSource(Program, "worker-js");
    }

    [Fact]
    public void JavaScript_AFailingMessage_IsReportedOnStderrAndTheWorkerKeepsGoing()
    {
        var (stdout, stderr) = RunJavaScript(Program);

        Assert.Contains("still alive 5", stdout);
        Assert.Contains("Worker actor 'Crunch' error: negative input", stderr);
    }

    [Fact]
    public void JavaScript_EmitsAWorkerScopeAndSpawnsThroughIt()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource(Program);

        Assert.Contains("function __maldaWorkerScope() {", js);
        Assert.Contains("mlRuntime.actors.spawnWorker(__maldaWorkerScope, \"Crunch\", [\"crunch\", ", js);
        Assert.Contains("return { \"Crunch\": Crunch };", js);
        Assert.Contains("mlRuntime.actors.spawn(new Echo())", js);
    }

    [Fact]
    public void JavaScript_NoWorkerActor_EmitsNoWorkerScope()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("actor A { on go() { } }\nvar a = spawn A();\nsend a.go();");

        Assert.DoesNotContain("__maldaWorkerScope", js);
    }

    /// <summary>
    /// The page keeps drawing frames while a worker counts primes; the same count in a plain actor
    /// blocks the only thread, so (almost) no frame is drawn until it ends.
    /// </summary>
    [Fact]
    public void JavaScript_WorkerRunsOffTheMainThread_ThePageKeepsDrawing()
    {
        const string source = """
            function isPrime(n) {
                for (var d = 2; d * d <= n; d = d + 1) {
                    if (n % d == 0) { return false; }
                }
                return n > 1;
            }

            @worker()
            actor Primes {
                on count(limit) {
                    var found = 0;
                    for (var n = 2; n < limit; n = n + 1) {
                        if (isPrime(n)) { found = found + 1; }
                    }
                    reply(found);
                }
            }

            actor Page {
                var frames = 0;
                var counting = true;
                on start() {
                    var primes = spawn Primes();
                    send primes.count(1000000) then (found) {
                        counting = false;
                        io.print($"found {found}");
                        io.print($"frames {frames}");
                    };
                    send self.frame();
                }
                on frame() {
                    if (counting) {
                        frames = frames + 1;
                        sleep(5);
                        send self.frame();
                    }
                }
            }

            var page = spawn Page();
            send page.start();
            sleep(4000);
            """;

        var worker = FramesDrawn(source);
        var blocking = FramesDrawn(source.Replace("@worker()", string.Empty));

        Assert.True(worker >= 5, $"only {worker} frames while the worker counted");
        Assert.True(blocking <= 2, $"{blocking} frames while a main-thread actor counted");
    }

    [Fact]
    public void JavaScript_EsModuleOutput_RunsAWorker()
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var directory = Path.Combine(Path.GetTempPath(), "malda_worker_esm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "app.malda");
            File.WriteAllText(sourcePath, """
                const BASE = 40;
                @worker()
                actor Adder {
                    on add(n) { reply(BASE + n); }
                }
                actor Page {
                    on start() {
                        var adder = spawn Adder();
                        send adder.add(2) then (r) { io.print($"answer {r}"); };
                    }
                }
                var page = spawn Page();
                send page.start();
                """);
            var result = new Compiler.Compiler().CompileToJavaScriptModule(sourcePath, Path.Combine(directory, "app.mjs"));
            Assert.True(result.Success, result.ErrorMessage);
            File.WriteAllText(Path.Combine(directory, "main.mjs"), """
                import "./app.mjs";
                await new Promise((resolve) => setTimeout(resolve, 1500));
                await globalThis.mlRuntime.actors.shutdownAsync();
                """);

            var (stdout, stderr) = RunNode(Path.Combine(directory, "main.mjs"), directory);

            Assert.Equal("answer 42", stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private const string UsesATopLevelVar = """
        var counter = 0;
        function bump() { counter = counter + 1; return counter; }
        @worker()
        actor W {
            on go() { io.print(bump()); }
        }
        var w = spawn W();
        send w.go();
        """;

    private const string TopLevelVarMessage =
        "Worker actor 'W' uses the top-level variable 'counter' through 'bump'. A worker has its own copy of the program and shares no variables: pass the value in a message or a spawn argument, or declare it with const.";

    [Fact]
    public void Rules_TopLevelVarThroughAFunction_IsReported()
    {
        var violations = WorkerActorRules.Check(Parse(UsesATopLevelVar));

        var violation = Assert.Single(violations);
        Assert.Equal(TopLevelVarMessage, violation.Message);
        Assert.Equal(2, violation.Line);
    }

    [Fact]
    public void Rules_TopLevelVarUsedDirectly_IsReported()
    {
        var violations = WorkerActorRules.Check(Parse("""
            var limit = 3;
            @worker()
            actor W {
                on go() { io.print(limit); }
            }
            """));

        Assert.Equal("Worker actor 'W' uses the top-level variable 'limit'. A worker has its own copy of the program and shares no variables: pass the value in a message or a spawn argument, or declare it with const.",
            Assert.Single(violations).Message);
    }

    [Fact]
    public void Rules_ConstsFunctionsClassesAndShadowingLocals_AreAllowed()
    {
        var violations = WorkerActorRules.Check(Parse("""
            const LIMIT = 3;
            var total = 0;
            class Box { var total = 1; function size() { return total; } }
            function twice(x) { var total = x * 2; return total; }
            @worker()
            actor W {
                var total = 0;
                on go(n) {
                    total = twice(n) + LIMIT + new Box().size();
                    for (var limit in [1]) { total = total + limit; }
                }
            }
            total = 5;
            """));

        Assert.Empty(violations);
    }

    [Fact]
    public void Rules_JsImportInAWorker_IsReported()
    {
        var violations = WorkerActorRules.Check(Parse("""
            import js global window;
            @worker()
            actor W {
                on go() { io.print(window.name); }
            }
            """));

        Assert.Equal("Worker actor 'W' uses the JavaScript import 'window'. 'import js' bindings are not loaded into a worker.",
            Assert.Single(violations).Message);
    }

    [Fact]
    public async Task Rules_EveryBackendRejectsTheProgram()
    {
        var interpreted = await TestBase.CaptureInterpretOutcomeAsync(UsesATopLevelVar);
        Assert.NotNull(interpreted.Exception);
        Assert.Contains(TopLevelVarMessage, interpreted.Exception!.Message);

        var js = Assert.ThrowsAny<Exception>(() => new Compiler.Compiler().TranspileToJavaScriptFromSource(UsesATopLevelVar));
        Assert.Contains(TopLevelVarMessage, js.ToString());

        var csharp = Assert.ThrowsAny<Exception>(() => new Compiler.CSharpTranspiler().Transpile(Parse(UsesATopLevelVar)));
        Assert.Contains(TopLevelVarMessage, csharp.Message);
    }

    /// <summary>
    /// An actor spawned by another actor resolves top-level functions; it used to look them up in
    /// the spawning actor's interpreter, which has none.
    /// </summary>
    [Fact]
    public async Task Interpreter_ActorSpawnedInsideAnActor_SeesTopLevelFunctions()
    {
        var output = await TestBase.CaptureInterpretAsync("""
            function helper(x) { return x * 3; }
            actor Child {
                on go(v) { reply(helper(v)); }
            }
            actor Parent {
                on start() {
                    var child = spawn Child();
                    send child.go(7) then (r) { io.print(r); };
                }
            }
            var parent = spawn Parent();
            send parent.start();
            sleep(300);
            """);

        Assert.Equal("21", InterpretTranspilePair.Normalize(output));
    }

    /// <summary>
    /// A send's timeout handler runs in the sender's own loop: it used to run on a pool thread on
    /// the sender's interpreter while the sender was still in a handler, losing that handler's locals.
    /// </summary>
    [Fact]
    public async Task Interpreter_TimeoutHandler_WaitsForTheSendersCurrentMessage()
    {
        var output = await TestBase.CaptureInterpretAsync("""
            actor Quiet {
                on ask() { }
            }
            actor Page {
                on start() {
                    var quiet = spawn Quiet();
                    var label = "still here";
                    send quiet.ask() then (r) { io.print("never"); } timeout 50 catch (e) { io.print("timed out"); };
                    sleep(300);
                    io.print(label);
                }
            }
            var page = spawn Page();
            send page.start();
            sleep(600);
            """);

        Assert.Equal("still here\ntimed out", InterpretTranspilePair.Normalize(output));
    }

    private static List<Parser.AST.Statements.Statement> Parse(string source)
    {
        var parser = new Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }

    private static int FramesDrawn(string source)
    {
        var (stdout, _) = RunJavaScript(source);
        var line = stdout.Split('\n').Single(l => l.StartsWith("frames ", StringComparison.Ordinal));
        return int.Parse(line["frames ".Length..]);
    }

    /// <summary>Runs a program compiled to JavaScript in Node and returns stdout and stderr.</summary>
    private static (string StdOut, string StdErr) RunJavaScript(string source)
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var directory = Path.Combine(Path.GetTempPath(), "malda_worker_js", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "program.js"), new Compiler.Compiler().TranspileToJavaScriptFromSource(source));
            File.Copy(PlanningRuntimePath(), Path.Combine(directory, "malda-js-runtime.js"));
            File.WriteAllText(Path.Combine(directory, "run.js"), """
                require("./malda-js-runtime.js");
                (async () => {
                  const app = require("./program.js");
                  await app.main();
                  await globalThis.mlRuntime.actors.shutdownAsync();
                })().catch((error) => { process.stderr.write(String(error && error.stack || error) + "\n"); process.exit(1); });
                """);
            return RunNode(Path.Combine(directory, "run.js"), directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string PlanningRuntimePath() =>
        Planning.PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");

    private static (string StdOut, string StdErr) RunNode(string scriptPath, string workingDirectory)
    {
        var node = Environment.GetEnvironmentVariable("MALDA_NODE_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(node) ? "node" : node,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(scriptPath);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        // A worker left running would keep Node alive: exiting at all checks shutdown.
        Assert.True(process.WaitForExit(60_000), "Node did not exit.");
        Assert.True(process.ExitCode == 0, stderr.Result);
        return (stdout.Result.Replace("\r", string.Empty).TrimEnd('\n'), stderr.Result.Replace("\r", string.Empty).TrimEnd('\n'));
    }
}
