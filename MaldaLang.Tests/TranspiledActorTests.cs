using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspiledActorTests
{
    [Fact]
    public void Transpiled_BasicCounter_Run()
    {
        var source = @"
            actor Counter {
                var count = 0;

                on increment() {
                    count = count + 1;
                    print($""Counter incremented to {count}"");
                }
            }

            var counter = spawn Counter();
            
            // Small initial delay to ensure Counter actor loop has started
            sleep(100);
            
            print(""Sending increment messages..."");
            send counter.increment();
            send counter.increment();

            // Give actor time to process messages
            sleep(500);

            print(""Done"");
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Sending increment messages...", result.StdOut);
        Assert.Contains("Counter incremented to 1", result.StdOut);
        Assert.Contains("Counter incremented to 2", result.StdOut);
        Assert.Contains("Done", result.StdOut);
    }

    [Fact]
    public void Transpiled_Stop_External()
    {
        var source = @"
            actor Worker {
                on work() {
                    print(""working"");
                }
            }

            actor Controller {
                on start() {
                    var w = spawn Worker();
                    send w.work();
                    
                    // Small delay to allow first work message to be processed
                    sleep(100);
                    
                    send w.stop();
                    
                    // Small delay to allow stop to be processed
                    sleep(100);
                    
                    send w.work();

                    // Additional delay to ensure second work message attempt is processed (or rejected)
                    sleep(300);
                }
            }

            var c = spawn Controller();
            
            // Small initial delay to ensure Controller actor loop has started
            sleep(100);
            
            send c.start();

            // Give actors time to process messages and complete
            sleep(500);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        // First message should be processed
        Assert.Contains("working", result.StdOut);
        // Second message should not produce another ""working"" line
        Assert.True(result.StdOut.Split("working", StringSplitOptions.None).Length <= 2);
    }

    [Fact]
    public void Transpiled_Stop_Self()
    {
        var source = @"
            actor Worker {
                on work() {
                    print(""before stop"");
                    self.stop();
                    print(""after stop"");
                }
            }

            actor Controller {
                on start() {
                    var w = spawn Worker();
                    
                    // Small initial delay to ensure Worker actor loop has started
                    sleep(100);
                    
                    send w.work();
                    
                    // Small delay to allow first work message to be processed
                    sleep(100);
                    
                    send w.work();

                    // Additional delay to ensure messages are processed
                    sleep(300);
                }
            }

            var c = spawn Controller();
            
            // Small initial delay to ensure Controller actor loop has started
            sleep(100);
            
            send c.start();

            // Give actors time to process messages and complete
            sleep(500);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("before stop", result.StdOut);
        // 'after stop' from the same message handler is allowed; but second work should not run.
        var beforeCount = result.StdOut.Split("before stop", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, beforeCount);
    }

    [Fact]
    public void Transpiled_SendThenReply_Example()
    {
        // Find the Examples directory by locating the solution root
        // Strategy: Start from test assembly location and navigate up until we find Examples/Actors directory
        string? examplesDir = null;
        
        // Get potential starting directories
        var startDirs = new List<string>();
        
        // Try assembly location
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var assemblyLocation = assembly.Location;
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            var assemblyDir = System.IO.Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                startDirs.Add(assemblyDir);
            }
        }
        
        // Try current working directory
        var currentDir = System.IO.Directory.GetCurrentDirectory();
        if (!string.IsNullOrEmpty(currentDir))
        {
            startDirs.Add(currentDir);
        }
        
        // Try AppDomain base directory
        var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            startDirs.Add(baseDir);
        }
        
        // Search from each starting directory
        foreach (var startDir in startDirs.Distinct())
        {
            if (string.IsNullOrEmpty(startDir)) continue;
            
            var searchDir = new System.IO.DirectoryInfo(startDir);
            while (searchDir != null)
            {
                var testExamplesDir = System.IO.Path.Combine(searchDir.FullName, "Examples");
                var actorsDir = System.IO.Path.Combine(testExamplesDir, "Actors");
                
                // Verify it's the right Examples directory by checking for Actors subdirectory and the target file
                if (System.IO.Directory.Exists(actorsDir))
                {
                    var targetFile = System.IO.Path.Combine(actorsDir, "callback_message.malda");
                    if (System.IO.File.Exists(targetFile))
                    {
                        examplesDir = testExamplesDir;
                        break;
                    }
                }
                
                searchDir = searchDir.Parent;
            }
            
            if (examplesDir != null) break;
        }
        
        if (examplesDir == null)
        {
            var debugInfo = string.Join("\n  - ", startDirs.Distinct().Where(d => !string.IsNullOrEmpty(d)));
            throw new System.IO.FileNotFoundException(
                $"Could not find Examples/Actors/callback_message.malda. Searched from:\n  - {debugInfo}\n" +
                $"Assembly location: {assemblyLocation ?? "null"}");
        }
        
        var examplePath = System.IO.Path.Combine(examplesDir, "Actors", "callback_message.malda");
        
        var result = TranspiledTestRunner.CompileAndRunFromFile(examplePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Coordinator: received result = 42", result.StdOut);
    }

    [Fact]
    public void Transpiled_Receive_InHandler()
    {
        var source = @"
            actor Echo {
                on echo(msg) {
                    var received = receive();
                    print($""echo received: {received}"");
                }
            }

            actor Starter {
                on start() {
                    var e = spawn Echo();
                    send e.echo(""hello"");

                    // Small delay to allow echo message to be processed
                    sleep(200);
                }
            }

            var s = spawn Starter();
            
            // Small initial delay to ensure Starter actor loop has started
            sleep(100);
            
            send s.start();

            // Give actors time to process messages and complete
            sleep(500);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("echo received: hello", result.StdOut);
    }

    [Fact]
    public void Transpiled_ActorSugar_MessageDeclarations_UseReceiveLoop()
    {
        var source = @"
            actor Counter {
                message Inc(amount);

                on start() {
                    var running = true;
                    while (running) {
                        var msg = receive();
                        match msg {
                            case Inc(amount): print($""inc: {amount}"");
                            case ""stop"": running = false;
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
            send c(""stop"");

            // Give actors time to process
            sleep(500);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("inc: 1", result.StdOut);
    }

    [Fact]
    public void Transpiled_Timeout_FiresWhenNoReply()
    {
        var source = @"
            actor Worker {
                on slowWork() {
                    // Intentionally don't reply to trigger timeout
                }
            }

            actor Coordinator {
                on start() {
                    var worker = spawn Worker();
                    send worker.slowWork() then (result) {
                        print(""This should not print"");
                    } timeout 100 catch (error) {
                        print($""Timeout caught: {error}"");
                    };
                }
            }

            var c = spawn Coordinator();
            
            // Small initial delay to ensure Coordinator actor loop has started
            sleep(100);
            
            send c.start();

            // Timeout is 100ms, so we need enough delay for:
            // 1. Coordinator to process start message
            // 2. Worker to be spawned and receive message
            // 3. Timeout timer to start (100ms)
            // 4. Timeout to fire (100ms real-world time)
            // 5. Timeout error handler message to be queued
            // 6. Coordinator to process timeout error handler message
            // 7. Timeout error handler to execute and print
            // Use sleep() to ensure real-world time passes for timeout to fire
            sleep(500);

            print(""Done"");
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Timeout caught:", result.StdOut);
        Assert.DoesNotContain("This should not print", result.StdOut);
    }

    [Fact]
    public void Transpiled_Timeout_CancelledWhenReplyArrives()
    {
        var source = @"
            actor Worker {
                on quickWork() {
                    reply(42);
                }
            }

            actor Coordinator {
                on start() {
                    var worker = spawn Worker();
                    send worker.quickWork() then (result) {
                        print($""Got result: {result}"");
                    } timeout 5000 catch (error) {
                        print($""Timeout should not fire: {error}"");
                    };
                }
            }

            var c = spawn Coordinator();
            
            // Small initial delay to ensure Coordinator actor loop has started
            sleep(100);
            
            send c.start();

            // Give enough time for:
            // 1. Coordinator to process start message
            // 2. Worker to be spawned and receive message
            // 3. Worker to reply
            // 4. Coordinator to receive reply and execute callback
            // 5. Timeout to be cancelled before it fires
            sleep(500);

            print(""Done"");
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Got result: 42", result.StdOut);
        Assert.DoesNotContain("Timeout should not fire", result.StdOut);
    }

    [Fact]
    public void Transpiled_Timeout_WithoutErrorHandler()
    {
        var source = @"
            actor Worker {
                on slowWork() {
                    // Intentionally don't reply to trigger timeout
                }
            }

            actor Coordinator {
                on start() {
                    var worker = spawn Worker();
                    send worker.slowWork() then (result) {
                        print(""This should not print"");
                    } timeout 100;
                    print(""Send completed"");
                }
            }

            var c = spawn Coordinator();
            
            // Small initial delay to ensure Coordinator actor loop has started
            sleep(100);
            
            send c.start();

            // Give actors time to process messages
            sleep(500);

            print(""Done"");
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Send completed", result.StdOut);
        Assert.DoesNotContain("This should not print", result.StdOut);
    }

    [Fact]
    public void Transpiled_Timeout_WithErrorHandler()
    {
        var source = @"
            actor Worker {
                on slowWork() {
                    // Intentionally don't reply to trigger timeout
                }
            }

            actor Coordinator {
                var timeoutCount = 0;
                
                on start() {
                    var worker = spawn Worker();
                    send worker.slowWork() then (result) {
                        print(""This should not print"");
                    } timeout 100 catch (error) {
                        timeoutCount = timeoutCount + 1;
                        print($""Timeout #{timeoutCount}: {error}"");
                    };
                }
            }

            var c = spawn Coordinator();
            
            // Small initial delay to ensure Coordinator actor loop has started
            sleep(100);
            
            send c.start();

            // Timeout is 100ms, so we need enough delay for:
            // 1. Coordinator to process start message
            // 2. Worker to be spawned and receive message
            // 3. Timeout timer to start (100ms)
            // 4. Timeout to fire (100ms real-world time)
            // 5. Timeout error handler message to be queued
            // 6. Coordinator to process timeout error handler message
            // 7. Timeout error handler to execute and print
            // Use sleep() to ensure real-world time passes for timeout to fire
            sleep(500);

            print(""Done"");
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Timeout #1:", result.StdOut);
        Assert.Contains("timed out after 100ms", result.StdOut);
        Assert.DoesNotContain("This should not print", result.StdOut);
    }

    [Fact]
    public void Transpiled_AsyncAwait_SleepThenAwait()
    {
        var source = @"
            var t = async sleep(50);
            await t;
            print(""done"");
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("done", result.StdOut);
    }
}

