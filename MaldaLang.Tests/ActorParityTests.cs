using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ActorParityTests : TestBase
{
    private static string NormalizeActorOutput(string output)
    {
        var normalized = output.Replace("\r", "").Trim();
        normalized = Regex.Replace(normalized, "<ActorRef [^>]+>", "<actor>");
        normalized = Regex.Replace(normalized, "<ActorReference: [^>]+>", "<actor>");
        return normalized;
    }

    private async Task<string> RunInterpreterActorProgramAsync(string source)
    {
        RedirectConsole();
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();

            Assert.Empty(parser.Errors);

            var interpreter = new Interpreter.Interpreter();
            await interpreter.InterpretAsync(statements);

            // Actor callback and timeout handlers are asynchronous; give the mailbox
            // loop additional time beyond the shared TestBase helper to stabilize
            // parity checks when this test runs alongside the broader actor suite.
            await Task.Delay(1200);
            return GetOutput();
        }
        finally
        {
            RestoreConsole();
        }
    }

    private void AssertInterpreterAndTranspiledOutputsMatch(string source, params string[] expectedLines)
    {
        var interpreterOutput = NormalizeActorOutput(RunInterpreterActorProgramAsync(source).GetAwaiter().GetResult());
        var transpiledOutput = NormalizeActorOutput(TranspiledTestRunner.CompileAndRunFromSource(source).StdOut);
        var expectedOutput = string.Join("\n", expectedLines);

        Assert.Equal(expectedOutput, interpreterOutput);
        Assert.Equal(expectedOutput, transpiledOutput);
    }

    [Fact]
    public void ActorParity_SendOrdering_MatchesAcrossRuntimes()
    {
        var source = """
            actor Worker {
                on work(label) {
                    print(label);
                }
            }

            actor Controller {
                on start() {
                    var w = spawn Worker();
                    sleep(100);
                    send w.work("first");
                    send w.work("second");
                    sleep(300);
                }
            }

            var c = spawn Controller();
            sleep(100);
            send c.start();
            sleep(500);
            """;

        AssertInterpreterAndTranspiledOutputsMatch(source, "first", "second");
    }

    [Fact]
    public void ActorParity_CallbackReply_MatchesAcrossRuntimes()
    {
        var source = """
            actor Worker {
                on compute(value) {
                    reply(value * 2);
                }
            }

            actor Coordinator {
                on start() {
                    var w = spawn Worker();
                    sleep(100);
                    send w.compute(21) then (result) {
                        print("reply:" + string(result));
                    };
                    sleep(200);
                }
            }

            var c = spawn Coordinator();
            sleep(100);
            send c.start();
            sleep(1500);
            """;

        AssertInterpreterAndTranspiledOutputsMatch(source, "reply:42");
    }

    [Fact]
    public void ActorParity_CallbackTimeout_MatchesAcrossRuntimes()
    {
        var source = """
            actor Worker {
                on slowWork() {
                }
            }

            actor Coordinator {
                on start() {
                    var w = spawn Worker();
                    sleep(100);
                    send w.slowWork() then (result) {
                        print("reply:" + string(result));
                    } timeout 100 catch (error) {
                        print("timeout:" + string(error));
                    };
                    sleep(500);
                }
            }

            var c = spawn Coordinator();
            sleep(100);
            send c.start();
            sleep(2000);
            """;

        AssertInterpreterAndTranspiledOutputsMatch(
            source,
            "timeout:Request to <actor>.slowWork timed out after 100ms");
    }

    [Fact]
    public void ActorParity_StopBehavior_MatchesAcrossRuntimes()
    {
        var source = """
            actor Worker {
                on work(label) {
                    print(label);
                }
            }

            actor Controller {
                on start() {
                    var w = spawn Worker();
                    sleep(100);
                    send w.work("before-stop");
                    sleep(100);
                    send w.stop();
                    sleep(100);
                    send w.work("after-stop");
                    sleep(300);
                }
            }

            var c = spawn Controller();
            sleep(100);
            send c.start();
            sleep(600);
            """;

        AssertInterpreterAndTranspiledOutputsMatch(source, "before-stop");
    }

    [Fact]
    public void ActorParity_MessageDeclarationReceiveLoop_MatchesAcrossRuntimes()
    {
        var source = """
            actor Counter {
                message Inc(amount);

                on start() {
                    var running = true;
                    while (running) {
                        var msg = receive();
                        match msg {
                            case Inc(n): print("inc:" + string(n));
                            case "stop": running = false;
                            default: {};
                        }
                    }
                }
            }

            var c = spawn Counter();
            sleep(100);
            send c.start();
            sleep(100);
            send c.Inc(1);
            sleep(100);
            send c("stop");
            sleep(500);
            """;

        AssertInterpreterAndTranspiledOutputsMatch(source, "inc:1");
    }

    [Fact]
    public void ActorParity_ReceiveUsesFirstArgument_MatchesAcrossRuntimes()
    {
        var source = """
            actor Echo {
                on echo(first, second) {
                    print("recv:" + string(receive()));
                    print("args:" + string(first) + "," + string(second));
                }
            }

            actor Starter {
                on start() {
                    var e = spawn Echo();
                    sleep(100);
                    send e.echo("hello", "world");
                    sleep(300);
                }
            }

            var s = spawn Starter();
            sleep(100);
            send s.start();
            sleep(600);
            """;

        AssertInterpreterAndTranspiledOutputsMatch(source, "recv:hello", "args:hello,world");
    }

    [Fact]
    public void ActorParity_ConstructorInitialization_MatchesAcrossRuntimes()
    {
        var source = """
            actor Greeter {
                var greeting = "hello";

                function Greeter(message) {
                    greeting = message;
                }

                on start() {
                    print(greeting);
                }
            }

            var g = spawn Greeter("bonjour");
            sleep(100);
            send g.start();
            sleep(500);
            """;

        AssertInterpreterAndTranspiledOutputsMatch(source, "bonjour");
    }

    [Fact]
    public void ActorParity_ExternalStopDropsPendingQueuedMessages_MatchesAcrossRuntimes()
    {
        var source = """
            actor Worker {
                on work(label) {
                    print(label);
                }
            }

            actor Controller {
                on start() {
                    var w = spawn Worker();
                    sleep(100);
                    send w.work("one");
                    send w.work("two");
                    send w.stop();
                    send w.work("three");
                    sleep(300);
                }
            }

            var c = spawn Controller();
            sleep(100);
            send c.start();
            sleep(700);
            """;

        AssertInterpreterAndTranspiledOutputsMatch(source, "one");
    }
}
