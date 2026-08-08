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
                var PRODUCT_NAME = "Acme Knowledge";
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
            Assert.Contains("id='ask-panel'", html, StringComparison.Ordinal);
            Assert.Contains("id='ask-live-home'", html, StringComparison.Ordinal);
            Assert.Contains("id='ask-live-dock'", html, StringComparison.Ordinal);
            Assert.Contains("class='live-timer'", html, StringComparison.Ordinal);
            Assert.Contains("placeLiveDock(", html, StringComparison.Ordinal);
            Assert.Contains("startLiveTimer()", html, StringComparison.Ordinal);
            Assert.Contains("action='/ask'", html, StringComparison.Ordinal);
            Assert.Contains("action='/clear'", html, StringComparison.Ordinal);
            Assert.Contains("name='c'", html, StringComparison.Ordinal);
            Assert.Contains("var askLiveChannel='ask-secondbrain-ask-test';", html, StringComparison.Ordinal);
            Assert.Contains("EventSource('/ask/live?channel='+encodeURIComponent(askLiveChannel))", html, StringComparison.Ordinal);
            Assert.Contains("class='new-conv'", html, StringComparison.Ordinal);
            Assert.Contains("New conversation", html, StringComparison.Ordinal);
            Assert.Contains("href='/?c=", html, StringComparison.Ordinal);
            Assert.Contains("data-ask-lang='en'", html, StringComparison.Ordinal);
            Assert.Contains("data-ask-lang='it'", html, StringComparison.Ordinal);
            Assert.Contains("navigateLang(", html, StringComparison.Ordinal);
            Assert.Contains("disconnectLive(", html, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_ResolvesRelativeAskLogo_Under_DiskBrainFolder()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"#2f6f5e\"/></svg>";
        var expectedPrefix = "data:image/svg+xml;base64,";
        var expectedB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_rel_logo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var brainDir = Path.Combine(tempDir, "brain");
            Directory.CreateDirectory(brainDir);
            File.WriteAllText(Path.Combine(brainDir, "logo.png"), svg, utf8NoBom);
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));

            var brainLiteral = brainDir.Replace("\\", "/");
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                $$"""
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-rel-logo";
                var ASK_STORE = "SecondBrainAskRelLogo";
                var PRODUCT_NAME = "Rel Logo";
                var ASK_TITLE_SUFFIX = " — ASK";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "logo.png";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                print(askApplyLogoFromBrain("{{brainLiteral}}"));
                print(askResolveLogoSrc());
                """,
                utf8NoBom);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("logo.png", output, StringComparison.OrdinalIgnoreCase);
            // Basename resolves under brain; MIME follows the filename (.png), not the bytes.
            Assert.Contains("data:image/png;base64," + expectedB64, output, StringComparison.Ordinal);
            Assert.DoesNotContain("ASK_LOGO not found", output, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_AutoLoadsLogo_From_DiskBrainFolder_When_ASK_LOGO_Empty()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"#2f6f5e\"/></svg>";
        var expectedPrefix = "data:image/svg+xml;base64,";
        var expectedB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_ui_disk_logo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var brainDir = Path.Combine(tempDir, "brain");
            Directory.CreateDirectory(brainDir);
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(Path.Combine(brainDir, "logo.svg"), svg, utf8NoBom);
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));

            var brainLiteral = brainDir.Replace("\\", "\\\\");
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                $$"""
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-disk-logo";
                var ASK_STORE = "SecondBrainAskDiskLogo";
                var PRODUCT_NAME = "Disk Logo Brain";
                var ASK_TITLE_SUFFIX = " — ASK";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                print(askApplyLogoFromBrain("{{brainLiteral}}"));
                askSetSession({
                    "brainDir": "{{brainLiteral}}",
                    "chatOnly": false,
                    "noteCount": 1,
                    "topicCount": 1,
                    "sourceFolder": "docs",
                    "retrieval": "lexical",
                    "llm": "test",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "disk logo"
                });
                print("---HTML---");
                print(askRenderPage());
                """,
                utf8NoBom);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("logo.svg", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedPrefix + expectedB64, output, StringComparison.Ordinal);
            Assert.Contains("<img class='logo'", output, StringComparison.Ordinal);
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
                var PRODUCT_NAME = "Embedded Logo Brain";
                var ASK_PAGE_TITLE = "Embedded Logo Brain";
                var ASK_POWERED_BY = "Powered by MALDA";
                var ASK_POWERED_BY_URL = "https://github.com/amaldini/maldalang";
                var ASK_LOGO = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                print(askApplyLogoFromBrain("embed:secondbrain"));
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
            Assert.Contains("embed:secondbrain/logo.svg", output, StringComparison.Ordinal);
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
    public async Task AskUi_PanelFragment_OmitsDocumentShell()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);
        var libSource = await File.ReadAllTextAsync(AskUiLibPath);
        Assert.Contains("@ACTION(\"/ask\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@ACTION(\"/clear\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@LIVE(\"/ask/live\")", libSource, StringComparison.Ordinal);
        Assert.Contains("componentFragment(\"ask-panel\"", libSource, StringComparison.Ordinal);
        Assert.Contains("malda_ask_c", libSource, StringComparison.Ordinal);
        Assert.Contains("function askLiveChannel()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askConvScope()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askBeginRequest(", libSource, StringComparison.Ordinal);
        Assert.Contains("RedirectTo(\"/?c=\"", libSource, StringComparison.Ordinal);
        Assert.Contains("onAgentProgress(liveChannel)", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain("onAgentProgress(\"ask\")", libSource, StringComparison.Ordinal);
        Assert.Contains("id='ask-live-status'", libSource, StringComparison.Ordinal);
        Assert.Contains("id='ask-live-home'", libSource, StringComparison.Ordinal);
        Assert.Contains("placeLiveDock", libSource, StringComparison.Ordinal);
        Assert.Contains("startLiveTimer", libSource, StringComparison.Ordinal);
        Assert.Contains("formatElapsed", libSource, StringComparison.Ordinal);
        Assert.Contains("function askGetToolsEnabled()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askSetToolsEnabled(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askApplyToolsFromBody(", libSource, StringComparison.Ordinal);
        Assert.Contains("name='useTools'", libSource, StringComparison.Ordinal);
        Assert.Contains("askApplyToolsFromBody(body)", libSource, StringComparison.Ordinal);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_panel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39019;
                var ASK_SESSION_ID = "secondbrain-ask-panel-test";
                var ASK_STORE = "SecondBrainAskPanelTest";
                var PRODUCT_NAME = "Panel Brain";
                var ASK_PAGE_TITLE = "Panel Brain";
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                askSetSession({
                    "brainDir": "secondbrain",
                    "chatOnly": false,
                    "noteCount": 1,
                    "topicCount": 1,
                    "sourceFolder": "docs",
                    "retrieval": "lexical",
                    "llm": "test-model",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "panel"
                });
                askAppendTurn({
                    "question": "hello",
                    "answer": "world",
                    "sources": [],
                    "error": "",
                    "pending": false
                });
                print(askRenderPanelHtml());
                """,
                Encoding.UTF8);

            var html = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("id='thread'", html, StringComparison.Ordinal);
            Assert.Contains("id='turn-0'", html, StringComparison.Ordinal);
            Assert.Contains("hello", html, StringComparison.Ordinal);
            Assert.Contains("name='useTools'", html, StringComparison.Ordinal);
            Assert.Contains("tool-toggle", html, StringComparison.Ordinal);
            Assert.DoesNotContain(" checked", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<!DOCTYPE", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<html", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_ConversationScopes_IsolateHistory_ShareCatalog()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_conv", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39020;
                var ASK_SESSION_ID = "secondbrain-ask-conv-shared";
                var ASK_STORE = "SecondBrainAskConvIso";
                var PRODUCT_NAME = "Conv Iso";
                var ASK_PAGE_TITLE = "Conv Iso";
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                askSetSession({
                    "brainDir": "secondbrain",
                    "chatOnly": false,
                    "noteCount": 2,
                    "topicCount": 1,
                    "sourceFolder": "docs",
                    "retrieval": "lexical",
                    "llm": "test-model",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "iso"
                });
                ui.setState(ASK_STORE, "catalog", { "notes": ["shared-note"] }, ASK_SESSION_ID);

                ASK_CONV_CURRENT = "conv-aaaa-1111";
                askAppendTurn({
                    "question": "from-a",
                    "answer": "answer-a",
                    "sources": [],
                    "error": "",
                    "pending": false
                });
                askSetLang("it");

                ASK_CONV_CURRENT = "conv-bbbb-2222";
                askAppendTurn({
                    "question": "from-b",
                    "answer": "answer-b",
                    "sources": [],
                    "error": "",
                    "pending": false
                });
                askSetLang("en");

                ASK_CONV_CURRENT = "conv-aaaa-1111";
                var histA = askGetHistory();
                print("A_LEN=" + string(histA.length));
                print("A_Q=" + histA[0].question);
                print("A_LANG=" + askGetLang());
                print("A_CH=" + askLiveChannel());

                ASK_CONV_CURRENT = "conv-bbbb-2222";
                var histB = askGetHistory();
                print("B_LEN=" + string(histB.length));
                print("B_Q=" + histB[0].question);
                print("B_LANG=" + askGetLang());
                print("B_CH=" + askLiveChannel());

                askClearHistory();
                print("B_AFTER_CLEAR=" + string(askGetHistory().length));

                ASK_CONV_CURRENT = "conv-aaaa-1111";
                print("A_AFTER_B_CLEAR=" + string(askGetHistory().length));
                print("A_Q2=" + askGetHistory()[0].question);

                var catalog = ui.state(ASK_STORE, "catalog", null, ASK_SESSION_ID);
                print("CATALOG=" + catalog.notes[0]);
                print("SHARED=" + askSharedScope());
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("A_LEN=1", output, StringComparison.Ordinal);
            Assert.Contains("A_Q=from-a", output, StringComparison.Ordinal);
            Assert.Contains("A_LANG=it", output, StringComparison.Ordinal);
            Assert.Contains("A_CH=ask-conv-aaaa-1111", output, StringComparison.Ordinal);
            Assert.Contains("B_LEN=1", output, StringComparison.Ordinal);
            Assert.Contains("B_Q=from-b", output, StringComparison.Ordinal);
            Assert.Contains("B_LANG=en", output, StringComparison.Ordinal);
            Assert.Contains("B_CH=ask-conv-bbbb-2222", output, StringComparison.Ordinal);
            Assert.Contains("B_AFTER_CLEAR=0", output, StringComparison.Ordinal);
            Assert.Contains("A_AFTER_B_CLEAR=1", output, StringComparison.Ordinal);
            Assert.Contains("A_Q2=from-a", output, StringComparison.Ordinal);
            Assert.Contains("CATALOG=shared-note", output, StringComparison.Ordinal);
            Assert.Contains("SHARED=secondbrain-ask-conv-shared", output, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void AskHosts_WireToolsToggle_In_RunAskTurn()
    {
        var lexical = Path.Combine(RepoRoot, "Examples", "Agents", "secondbrain.malda");
        var semantic = Path.Combine(RepoRoot, "Examples", "Agents", "secondbrain_semantic.malda");
        Assert.True(File.Exists(lexical), "missing " + lexical);
        Assert.True(File.Exists(semantic), "missing " + semantic);

        foreach (var path in new[] { lexical, semantic })
        {
            var source = File.ReadAllText(path);
            Assert.Contains("var PRODUCT_NAME = ", source, StringComparison.Ordinal);
            Assert.Contains("var ASK_TITLE_SUFFIX = ", source, StringComparison.Ordinal);
            Assert.Contains("function productName()", source, StringComparison.Ordinal);
            Assert.Contains("function applyProductNameFromBrain(", source, StringComparison.Ordinal);
            Assert.Contains("askApplyProductNameFromBrain(", source, StringComparison.Ordinal);
            Assert.Contains("askApplyLogoFromBrain(", source, StringComparison.Ordinal);
            Assert.Contains("applyAskHttpPortFromCli()", source, StringComparison.Ordinal);
            Assert.Contains("--port", source, StringComparison.Ordinal);
            Assert.Contains("sbCliParseArgs(", source, StringComparison.Ordinal);
            Assert.Contains("build --docs", source, StringComparison.Ordinal);
            Assert.Contains("include \"secondbrain_cli_lib.malda\"", source, StringComparison.Ordinal);
            if (path.Contains("secondbrain_semantic", StringComparison.Ordinal))
            {
                Assert.Contains("find_related_notes", source, StringComparison.Ordinal);
                Assert.Contains("new Tool(", source, StringComparison.Ordinal);
            }
            Assert.Contains("product_name.txt", source, StringComparison.Ordinal);
            Assert.Contains("askGetToolsEnabled()", source, StringComparison.Ordinal);
            Assert.Contains("askSetToolsEnabled(false)", source, StringComparison.Ordinal);
            Assert.Contains("function answerInstructions(useTools)", source, StringComparison.Ordinal);
            Assert.Contains("newReaderAgent(", source, StringComparison.Ordinal);
            Assert.Contains("newPlainAgent(", source, StringComparison.Ordinal);
            Assert.Contains("ASK tools: off", source, StringComparison.Ordinal);
            Assert.Contains("ASK tools: on", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SbCli_ParseArgs_SupportsBuildUpdateFlags()
    {
        var cliLibPath = Path.Combine(RepoRoot, "Examples", "Agents", "secondbrain_cli_lib.malda");
        Assert.True(File.Exists(cliLibPath), "missing cli lib: " + cliLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_cli_parse", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(cliLibPath, Path.Combine(tempDir, "secondbrain_cli_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var UI_LANG = "en";
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "cli-test";
                var ASK_STORE = "CliTest";

                function t(en, it) { return en; }

                include "secondbrain_cli_lib.malda";

                var a = sbCliParseArgs(["script.malda", "build", "--docs", "P:/docs", "--brain", "brain1", "--taxonomy", "folders", "--lang", "en", "--strict-types"]);
                print("A=" + a.mode + "," + a.docs + "," + a.brain + "," + a.taxonomy + "," + a.lang + "," + a.error);
                var b = sbCliParseArgs(["update", "--remove-orphans", "--docs=./src"]);
                print("B=" + b.mode + "," + string(b.removeOrphans) + "," + b.docs + "," + b.error);
                var c = sbCliParseArgs(["--mode", "ask", "-p", "8080"]);
                print("C=" + c.mode + "," + string(c.port) + "," + c.error);
                var d = sbCliParseArgs(["build"]);
                print("D=" + d.mode + "," + d.docs + "," + d.error);
                var e = sbCliParseArgs(["--unknown"]);
                print("E=" + e.error);
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("A=build,P:/docs,brain1,folders,en,", output, StringComparison.Ordinal);
            Assert.Contains("B=update,true,./src,", output, StringComparison.Ordinal);
            Assert.Contains("C=ask,8080,", output, StringComparison.Ordinal);
            Assert.Contains("D=build,,", output, StringComparison.Ordinal);
            Assert.Contains("E=Unknown flag: --unknown", output, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_ParseHttpPortFromArgs_SupportsPortFlags()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_port", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-port-test";
                var ASK_STORE = "SecondBrainAskPortTest";
                var PRODUCT_NAME = "Port Brain";
                var ASK_TITLE_SUFFIX = " — ASK";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                var a = askParseHttpPortFromArgs(["script.malda", "--port", "8080"], 39018);
                print("A=" + string(a.ok) + "," + string(a.changed) + "," + string(a.port));
                var b = askParseHttpPortFromArgs(["-p", "41234"], 39018);
                print("B=" + string(b.ok) + "," + string(b.changed) + "," + string(b.port));
                var c = askParseHttpPortFromArgs(["--port=9090"], 39018);
                print("C=" + string(c.ok) + "," + string(c.changed) + "," + string(c.port));
                var d = askParseHttpPortFromArgs(["--port", "0"], 39018);
                print("D=" + string(d.ok) + "," + string(d.changed) + "," + string(d.port));
                var e = askParseHttpPortFromArgs(["--strict-types"], 39018);
                print("E=" + string(e.ok) + "," + string(e.changed) + "," + string(e.port));
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("A=true,true,8080", output, StringComparison.Ordinal);
            Assert.Contains("B=true,true,41234", output, StringComparison.Ordinal);
            Assert.Contains("C=true,true,9090", output, StringComparison.Ordinal);
            Assert.Contains("D=false,false,39018", output, StringComparison.Ordinal);
            Assert.Contains("E=true,false,39018", output, StringComparison.Ordinal);
        }
        finally
        {
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
                var PRODUCT_NAME = "Acme Hub";
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
            Assert.Contains("No Acme Hub loaded", html, StringComparison.Ordinal);
            Assert.DoesNotContain("second brain", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img class='logo'", html, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_LoadsProductName_From_BrainFile_DiskAndEmbed()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_product_name", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        EmbeddedFolderStore.ResetForTests();
        try
        {
            var brainDir = Path.Combine(tempDir, "brain");
            Directory.CreateDirectory(brainDir);
            File.WriteAllText(Path.Combine(brainDir, "product_name.txt"), "Disk Brand\n", Encoding.UTF8);
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));

            var brainLiteral = brainDir.Replace("\\", "\\\\");
            var diskHarness = Path.Combine(tempDir, "disk.malda");
            File.WriteAllText(diskHarness,
                $$"""
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-product-disk";
                var ASK_STORE = "SecondBrainAskProductDisk";
                var PRODUCT_NAME = "Second brain";
                var ASK_TITLE_SUFFIX = " — ASK";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                print(askApplyProductNameFromBrain("{{brainLiteral}}"));
                askSetSession({
                    "brainDir": "{{brainLiteral}}",
                    "chatOnly": true,
                    "noteCount": 0,
                    "topicCount": 0,
                    "sourceFolder": "none",
                    "retrieval": "none",
                    "llm": "test",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "x"
                });
                print("---HTML---");
                print(askRenderPage());
                """,
                Encoding.UTF8);

            var diskOut = await InterpretAndCaptureAsync(diskHarness);
            Assert.Contains("Disk Brand", diskOut, StringComparison.Ordinal);
            Assert.Contains("<h1>Disk Brand", diskOut, StringComparison.Ordinal);
            Assert.Contains("No Disk Brand loaded", diskOut, StringComparison.Ordinal);

            EmbeddedFolderStore.RegisterForTests("secondbrain", new Dictionary<string, string>
            {
                ["product_name.txt"] = "Embed Brand\n",
                ["brain.json"] = "{\"notes\":[]}"
            });

            var embedHarness = Path.Combine(tempDir, "embed.malda");
            File.WriteAllText(embedHarness,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-product-embed";
                var ASK_STORE = "SecondBrainAskProductEmbed";
                var PRODUCT_NAME = "Second brain";
                var ASK_TITLE_SUFFIX = " — ASK";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                print(askApplyProductNameFromBrain("embed:secondbrain"));
                print(askProductName());
                """,
                Encoding.UTF8);

            var embedOut = await InterpretAndCaptureAsync(embedHarness);
            Assert.Contains("Embed Brand", embedOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Second brain", embedOut, StringComparison.Ordinal);
        }
        finally
        {
            EmbeddedFolderStore.ResetForTests();
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
