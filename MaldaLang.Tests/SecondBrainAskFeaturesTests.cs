// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MaldaLang.BuiltIns;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

/// <summary>
/// Covers recent Second Brain ASK features: compileMALDA embedFolder,
/// header branding (title / powered-by / optional logo), and pack-style alias.
/// </summary>
[Collection("Sequential")]
public class SecondBrainAskFeaturesTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string AskUiLibPath =>
        Path.Combine(RepoRoot, "Examples", "Agents", "secondbrain_ask_ui_lib.malda");

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static async Task<string> InterpretAndCaptureAsync(string sourcePath)
    {
        var source = await File.ReadAllTextAsync(sourcePath);
        var lexer = new Lexer(source, sourcePath);
        var parser = new Parser.Parser(lexer.Tokenize(), sourcePath);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var interpreter = new Interpreter.Interpreter();
        return CaptureStdout(() =>
        {
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void CompileMALDA_WithEmbedFolder_Arg_ProducesReadableExe()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_features", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var brainDir = Path.Combine(tempDir, "brain_disk");
        Directory.CreateDirectory(brainDir);
        File.WriteAllText(Path.Combine(brainDir, "hello.txt"), "packed-brain", Encoding.UTF8);

        var sourcePath = Path.Combine(tempDir, "program.malda");
        File.WriteAllText(sourcePath,
            "print(io.hasEmbeddedFolder(\"secondbrain\"));\n" +
            "print(io.readFile(\"embed:secondbrain/hello.txt\"));\n",
            Encoding.UTF8);

        var outputExe = Path.Combine(tempDir, "program.exe");
        try
        {
            var result = BuiltInFunctions.CallBuiltIn(
                "compileMALDA",
                new List<RuntimeValue>
                {
                    RuntimeValue.String(sourcePath),
                    RuntimeValue.String(outputExe),
                    RuntimeValue.String("transpile"),
                    RuntimeValue.String(brainDir + "=secondbrain")
                },
                null);

            Assert.Equal(ValueType.Object, result.Type);
            var obj = result.AsObject();
            var success = obj.Get("success", null)?.AsBoolean() ?? false;
            var error = obj.Get("error", null)?.AsString() ?? "";
            Assert.True(success, "compileMALDA embedFolder failed: " + error);

            var written = obj.Get("outputPath", null)?.AsString() ?? outputExe;
            Assert.True(File.Exists(written), "compiled exe missing");

            Directory.Delete(brainDir, recursive: true);

            var psi = new ProcessStartInfo
            {
                FileName = written,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };
            using var process = Process.Start(psi);
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(120_000), "compiled exe timed out");
            Assert.True(process.ExitCode == 0, $"exit={process.ExitCode}\nstdout={stdout}\nstderr={stderr}");
            Assert.Contains("true", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("packed-brain", stdout, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void CompileMALDA_RejectsTooManyArgs()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            BuiltInFunctions.CallBuiltIn(
                "compileMALDA",
                new List<RuntimeValue>
                {
                    RuntimeValue.String("a.malda"),
                    RuntimeValue.String("a.exe"),
                    RuntimeValue.String("transpile"),
                    RuntimeValue.String("folder"),
                    RuntimeValue.String("extra")
                },
                null));
        Assert.Contains("1-4 arguments", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskUi_RendersTitle_PoweredBy_And_Logo()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var logoPath = Path.Combine(tempDir, "logo.svg");
            File.WriteAllText(logoPath,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"#2f6f5e\"/></svg>",
                Encoding.UTF8);

            // Absolute path: interpreter cwd is not the temp harness folder.
            var logoLiteral = logoPath.Replace("\\", "\\\\");
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                $$"""
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-test";
                var ASK_STORE = "SecondBrainAskTest";
                var ASK_PAGE_TITLE = "Acme Knowledge";
                var ASK_POWERED_BY = "Powered by MALDA: Multi Agent Language with Development Automation";
                var ASK_POWERED_BY_URL = "https://github.com/amaldini/maldalang";
                var ASK_LOGO = "{{logoLiteral}}";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return {
                        "question": question,
                        "answer": "ok",
                        "sources": [],
                        "error": ""
                    };
                }

                include "secondbrain_ask_ui_lib.malda";

                askSetSession({
                    "brainDir": "secondbrain",
                    "chatOnly": false,
                    "noteCount": 3,
                    "topicCount": 1,
                    "sourceFolder": "docs",
                    "retrieval": "lexical",
                    "llm": "test-model",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "Lexical retrieval over notes."
                });

                print(askRenderPage());
                """,
                Encoding.UTF8);

            var html = await InterpretAndCaptureAsync(harnessPath);

            Assert.Contains("<h1>Acme Knowledge</h1>", html, StringComparison.Ordinal);
            Assert.Contains("Powered by MALDA: Multi Agent Language with Development Automation", html, StringComparison.Ordinal);
            Assert.Contains("https://github.com/amaldini/maldalang", html, StringComparison.Ordinal);
            Assert.Contains("class='powered-by'", html, StringComparison.Ordinal);
            Assert.Contains("<img class='logo'", html, StringComparison.Ordinal);
            Assert.Contains("data:image/svg+xml;base64,", html, StringComparison.Ordinal);
            Assert.Contains("Lexical retrieval over notes.", html, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_RendersLogo_From_EmbeddedFolder()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"#2f6f5e\"/></svg>";
        var expectedPrefix = "data:image/svg+xml;base64,";
        var expectedB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_ui_embed_logo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        EmbeddedFolderStore.ResetForTests();
        try
        {
            EmbeddedFolderStore.RegisterForTests("secondbrain", new Dictionary<string, string>
            {
                ["logo.svg"] = svg,
                ["brain.json"] = "{\"notes\":[]}"
            });

            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-test-embed-logo";
                var ASK_STORE = "SecondBrainAskTestEmbedLogo";
                var ASK_PAGE_TITLE = "Embedded Logo Brain";
                var ASK_POWERED_BY = "Powered by MALDA";
                var ASK_POWERED_BY_URL = "https://github.com/amaldini/maldalang";
                var ASK_LOGO = "embed:secondbrain/logo.svg";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                askSetSession({
                    "brainDir": "embed:secondbrain",
                    "chatOnly": false,
                    "noteCount": 1,
                    "topicCount": 1,
                    "sourceFolder": "embed:secondbrain",
                    "retrieval": "lexical",
                    "llm": "test-model",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "Embedded logo check"
                });

                print(askResolveLogoSrc());
                print("---HTML---");
                print(askRenderPage());
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains(expectedPrefix + expectedB64, output, StringComparison.Ordinal);

            var htmlPart = output.Contains("---HTML---", StringComparison.Ordinal)
                ? output.Split("---HTML---", 2, StringSplitOptions.None)[1]
                : output;
            Assert.Contains("<img class='logo' src='" + expectedPrefix + expectedB64 + "'", htmlPart, StringComparison.Ordinal);
            Assert.Contains("<h1>Embedded Logo Brain</h1>", htmlPart, StringComparison.Ordinal);
        }
        finally
        {
            EmbeddedFolderStore.ResetForTests();
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_OmitsLogo_When_ASK_LOGO_Empty()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_ui_nologo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-test-nologo";
                var ASK_STORE = "SecondBrainAskTestNoLogo";
                var ASK_PAGE_TITLE = "No Logo Brain";
                var ASK_POWERED_BY = "Powered by MALDA";
                var ASK_POWERED_BY_URL = "https://github.com/amaldini/maldalang";
                var ASK_LOGO = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                askSetSession({
                    "brainDir": "secondbrain",
                    "chatOnly": true,
                    "noteCount": 0,
                    "topicCount": 0,
                    "sourceFolder": "none",
                    "retrieval": "none",
                    "llm": "test-model",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "Direct chat"
                });

                print(askRenderPage());
                """,
                Encoding.UTF8);

            var html = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("<h1>No Logo Brain</h1>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<img class='logo'", html, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task EmbeddedFolder_TriggersAskOnlyBranch()
    {
        // Mirrors secondbrain startup: embedded brain => ASK path, else MENU.
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_branch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var brainDir = Path.Combine(tempDir, "notes");
        Directory.CreateDirectory(brainDir);
        File.WriteAllText(Path.Combine(brainDir, "brain.json"), "{\"notes\":[]}", Encoding.UTF8);

        var sourcePath = Path.Combine(tempDir, "startup.malda");
        File.WriteAllText(sourcePath,
            """
            if (io.hasEmbeddedFolder("secondbrain")) {
                print("ASK");
            } else {
                print("MENU");
            }
            """,
            Encoding.UTF8);

        var outputExe = Path.Combine(tempDir, "startup.exe");
        try
        {
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(
                sourcePath,
                outputExe,
                CompilationMode.TranspileToCSharp,
                includeLLamaSharp: false,
                includeUiHost: false,
                profilingOptions: null,
                typedTranspileLevel: 1,
                includeOptionalPacks: false,
                embedFolderArgs: new[] { brainDir + "=secondbrain" });
            Assert.True(result.Success, result.ErrorMessage);

            Directory.Delete(brainDir, recursive: true);

            var psi = new ProcessStartInfo
            {
                FileName = result.OutputPath!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };
            using var process = Process.Start(psi);
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(120_000), "startup exe timed out");
            Assert.True(process.ExitCode == 0, $"exit={process.ExitCode}\nstdout={stdout}\nstderr={stderr}");
            Assert.Contains("ASK", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("MENU", stdout, StringComparison.Ordinal);

            // Without embed, same source chooses MENU (interpreter path).
            var menuOut = await InterpretAndCaptureAsync(sourcePath);
            Assert.Contains("MENU", menuOut, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void MarkupLine_WithBracketedCompilePath_DoesNotThrow()
    {
        // Mode-4 pack used to crash here: Spectre treated [C:\...\foo.csproj] as a style name
        // and hid the real compileMALDA failure behind "Could not find color or style ...".
        var csproj = @"C:\Users\amaldini.ENGINEERING\AppData\Local\Temp\spl_transpile_x\MaldaLang.Executable.csproj";
        var line = "[red]Pack failed:[/] Compilation error: missing [" + csproj + "]";

        var ex = Record.Exception(() =>
            BuiltInFunctions.WriteSpectreMarkup(line, appendNewLine: true));
        Assert.Null(ex);
    }

    [Fact]
    public void Transpile_UiStateHistory_AppendAndReplace_PersistAnswer()
    {
        // Portable ASK (exe) used to lose the completed turn: append/index-assign mutated a
        // List<object> bridge while ui.setState re-saved the original List<RuntimeValue>.
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_history", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "history.malda");
        var outputExe = Path.Combine(tempDir, "history.exe");
        File.WriteAllText(sourcePath,
            """
            var STORE = "AskHistoryBridge";
            var SESSION = "s1";

            function getHistory() {
                var history = ui.state(STORE, "history", [], SESSION);
                if (history == null) {
                    return [];
                }
                return history;
            }

            function setHistory(history) {
                ui.setState(STORE, "history", history, SESSION);
            }

            function appendTurn(turn) {
                var history = getHistory();
                history.append(turn);
                setHistory(history);
            }

            function replaceLast(turn) {
                var history = getHistory();
                if (history.length == 0) {
                    appendTurn(turn);
                    return;
                }
                history[history.length - 1] = turn;
                setHistory(history);
            }

            appendTurn({ "question": "q", "answer": "", "pending": true });
            replaceLast({ "question": "q", "answer": "final-answer", "pending": false });
            var h = getHistory();
            print(h.length);
            print(h[0].answer);
            print(h[0].pending);
            """,
            Encoding.UTF8);

        try
        {
            var result = BuiltInFunctions.CallBuiltIn(
                "compileMALDA",
                new List<RuntimeValue>
                {
                    RuntimeValue.String(sourcePath),
                    RuntimeValue.String(outputExe),
                    RuntimeValue.String("transpile")
                },
                null);

            Assert.Equal(ValueType.Object, result.Type);
            var obj = result.AsObject();
            var success = obj.Get("success", null)?.AsBoolean() ?? false;
            var error = obj.Get("error", null)?.AsString() ?? "";
            Assert.True(success, "compileMALDA history bridge failed: " + error);

            var written = obj.Get("outputPath", null)?.AsString() ?? outputExe;
            var psi = new ProcessStartInfo
            {
                FileName = written,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };
            using var process = Process.Start(psi);
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(120_000), "history exe timed out");
            Assert.True(process.ExitCode == 0, $"exit={process.ExitCode}\nstdout={stdout}\nstderr={stderr}");
            Assert.Contains("1", stdout, StringComparison.Ordinal);
            Assert.Contains("final-answer", stdout, StringComparison.Ordinal);
            Assert.Contains("false", stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
