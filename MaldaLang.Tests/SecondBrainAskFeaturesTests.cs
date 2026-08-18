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

    private static string CombinedSecondBrainSource(string hostPath)
    {
        var parts = new List<string> { File.ReadAllText(hostPath) };
        var sbDir = Path.Combine(RepoRoot, "Examples", "Agents", "sb");
        if (Directory.Exists(sbDir))
        {
            var extras = Directory.GetFiles(sbDir, "*.malda");
            Array.Sort(extras, StringComparer.Ordinal);
            foreach (var extra in extras)
            {
                parts.Add(File.ReadAllText(extra));
            }
        }
        foreach (var name in new[] { "secondbrain_cli_lib.malda", "secondbrain_cli_apply_lib.malda", "secondbrain_ask_ui_lib.malda" })
        {
            parts.Add(File.ReadAllText(Path.Combine(RepoRoot, "Examples", "Agents", name)));
        }
        return string.Join("\n", parts);
    }

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
                var ASK_LOGO_RIGHT = "";
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
            Assert.Contains("syncLiveDockPosition(", html, StringComparison.Ordinal);
            Assert.Contains("is-floating", html, StringComparison.Ordinal);
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
            Assert.DoesNotContain("data-ask-lang='it'", html, StringComparison.Ordinal);
            Assert.Contains("lang=it", html, StringComparison.Ordinal);
            Assert.Contains("data-theme='light'", html, StringComparison.Ordinal);
            Assert.Contains("data-ask-theme='light'", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-ask-theme='dark'", html, StringComparison.Ordinal);
            Assert.Contains("theme=dark", html, StringComparison.Ordinal);
            Assert.Contains("data-theme=dark", html, StringComparison.Ordinal);
            Assert.Contains("navigateLang(", html, StringComparison.Ordinal);
            Assert.Contains("disconnectLive(", html, StringComparison.Ordinal);
            // Mobile layout: brand must shrink; has-logo fit-content only on desktop.
            Assert.Contains("min-width:0;flex:1 1 12rem", html, StringComparison.Ordinal);
            Assert.Contains("@media (min-width:641px){main.has-logo{", html, StringComparison.Ordinal);
            Assert.Contains("@media (max-width:640px){", html, StringComparison.Ordinal);
            Assert.Contains("font-size:16px", html, StringComparison.Ordinal); // avoid iOS input zoom
            Assert.DoesNotContain("min-width:min-content", html, StringComparison.Ordinal);
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
                var ASK_LOGO_RIGHT = "";
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
                var ASK_LOGO_RIGHT = "";
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
                var ASK_LOGO_RIGHT = "";
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
        Assert.Contains("@PAGE(\"/login\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@POST(\"/login\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@PAGE(\"/register\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@POST(\"/register\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@GET(\"/logout\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@GET(\"/health\")", libSource, StringComparison.Ordinal);
        Assert.Contains("var ASK_SERVICE_VERSION = \"0.4.0\"", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var ASK_SERVICE_VERSION = \"0.3.0\"", libSource, StringComparison.Ordinal);
        Assert.Contains("@GET(\"/note\")", libSource, StringComparison.Ordinal);
        Assert.Contains("function askExtractCitedSlugs(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askRewriteNoteCitations(", libSource, StringComparison.Ordinal);
        Assert.Contains("data-ask-note", libSource, StringComparison.Ordinal);
        Assert.Contains("@GET(\"/generate/download\")", libSource, StringComparison.Ordinal);
        Assert.Contains("function askGetAskMode()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askBuildGeneratedDownload(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askMarkGeneratedTurn(", libSource, StringComparison.Ordinal);
        Assert.Contains("name='askMode'", libSource, StringComparison.Ordinal);
        Assert.Contains("/generate/download?", libSource, StringComparison.Ordinal);
        Assert.Contains("Draft a project brief following the indexed examples.", libSource, StringComparison.Ordinal);
        Assert.Contains("brief, summary, checklist", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain("itinerary", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain("preventivo", libSource, StringComparison.Ordinal);
        Assert.Contains("@PAGE(\"/admin/users\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@PAGE(\"/admin/upload\")", libSource, StringComparison.Ordinal);
        Assert.Contains("@ACTION(\"/feedback\")", libSource, StringComparison.Ordinal);
        Assert.Contains("function askConfigureHttpAuth(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askRefuseInsecurePublicBind(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askIsLoopbackHttpHost(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askHasDefaultDevSecrets(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askRequireAuth(", libSource, StringComparison.Ordinal);
        Assert.Contains("res.redirect(\"/login\")", libSource, StringComparison.Ordinal);
        Assert.Contains("authenticateCookieJwt(ASK_AUTH_COOKIE", libSource, StringComparison.Ordinal);
        Assert.Contains("function askRequireAdmin(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askHistoryScope(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askRegisterUser(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askSetUserEnabled(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askSetAuthUsersRoot(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askAudit(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askWeakRetrievalMessage(", libSource, StringComparison.Ordinal);
        Assert.Contains("users.json", libSource, StringComparison.Ordinal);
        Assert.Contains("ask_audit.jsonl", libSource, StringComparison.Ordinal);
        Assert.Contains("--no-auth", libSource, StringComparison.Ordinal);
        Assert.Contains("--allow-register", libSource, StringComparison.Ordinal);
        Assert.Contains("--no-examples", libSource, StringComparison.Ordinal);
        Assert.Contains("ASK_SHOW_EXAMPLES", libSource, StringComparison.Ordinal);
        Assert.Contains("malda_ask_session", libSource, StringComparison.Ordinal);
        Assert.Contains("componentFragment(\"ask-panel\"", libSource, StringComparison.Ordinal);
        Assert.Contains("malda_ask_c", libSource, StringComparison.Ordinal);
        Assert.Contains("function askLiveChannel()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askConvScope()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askBeginRequest(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askGetCatalog()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askSetCatalog(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askConfigureSharedStore()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askPinSharedStore()", libSource, StringComparison.Ordinal);
        Assert.Contains("ui.pinState(ASK_STORE, ASK_SESSION_ID)", libSource, StringComparison.Ordinal);
        Assert.Contains("ui.getState(ASK_STORE, \"session\"", libSource, StringComparison.Ordinal);
        Assert.Contains("ui.getState(ASK_STORE, \"catalog\"", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ASK_BOOT_SESSION", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ui.state(ASK_STORE, \"session\", {}",
            libSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ui.state(ASK_STORE, \"catalog\", null",
            libSource,
            StringComparison.Ordinal);
        Assert.Contains("RedirectTo(\"/?c=\"", libSource, StringComparison.Ordinal);
        Assert.Contains("onAgentProgress(liveChannel)", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain("onAgentProgress(\"ask\")", libSource, StringComparison.Ordinal);
        // Regression: citation preview must not drop the live-round JS helpers.
        Assert.Contains("function progressMessage(p){", libSource, StringComparison.Ordinal);
        Assert.Contains("var lang=t.closest('a[data-ask-lang]')", libSource, StringComparison.Ordinal);
        Assert.Contains("id='ask-live-draft'", libSource, StringComparison.Ordinal);
        Assert.Contains("data.type==='connected'||data.type==='heartbeat'", libSource, StringComparison.Ordinal);
        Assert.Contains("if(!payload.phase&&payload.round==null){return;}", libSource, StringComparison.Ordinal);
        Assert.Contains("payload.phase==='draft'", libSource, StringComparison.Ordinal);
        Assert.Contains("id='ask-live-status'", libSource, StringComparison.Ordinal);
        Assert.Contains("id='ask-live-home'", libSource, StringComparison.Ordinal);
        Assert.Contains("placeLiveDock", libSource, StringComparison.Ordinal);
        Assert.Contains("syncLiveDockPosition", libSource, StringComparison.Ordinal);
        Assert.Contains("is-floating", libSource, StringComparison.Ordinal);
        // Must not mount the dock inside the sticky composer (hidden on tall phone UIs).
        Assert.DoesNotContain("composer.insertBefore(dock", libSource, StringComparison.Ordinal);
        Assert.Contains("startLiveTimer", libSource, StringComparison.Ordinal);
        Assert.Contains("formatElapsed", libSource, StringComparison.Ordinal);
        Assert.Contains("function askGetToolsEnabled()", libSource, StringComparison.Ordinal);
        Assert.Contains("function askSetToolsEnabled(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askApplyToolsFromBody(", libSource, StringComparison.Ordinal);
        Assert.Contains("name='useTools'", libSource, StringComparison.Ordinal);
        Assert.Contains("askApplyToolsFromBody(body)", libSource, StringComparison.Ordinal);
        Assert.Contains("function askApplyTagFilterFromBody(", libSource, StringComparison.Ordinal);
        Assert.Contains("function askRenderTagPickerHtml(", libSource, StringComparison.Ordinal);
        Assert.Contains("class='tag-picker'", libSource, StringComparison.Ordinal);
        Assert.Contains("name='tags'", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class='tag-filters'", libSource, StringComparison.Ordinal);
        // Regression: strings have .length; foreach must only run on typeOf == "array".
        Assert.Contains("typeOf(raw) == \"array\"", libSource, StringComparison.Ordinal);
        Assert.DoesNotContain("raw.length != null", libSource, StringComparison.Ordinal);

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
                var ASK_LOGO_RIGHT = "";
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
            Assert.Contains("name='askMode' value='ask' checked", html, StringComparison.Ordinal);
            Assert.Contains("name='askMode' value='generate'", html, StringComparison.Ordinal);
            Assert.DoesNotContain("name='useTools' value='1' checked", html, StringComparison.Ordinal);
            Assert.DoesNotContain("name='forceAnswer' value='1' checked", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<!DOCTYPE", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<html", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskServiceVersion_Is_0_3_0_And_Hosts_Drop_0_1_0_Banner()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);
        var libSource = await File.ReadAllTextAsync(AskUiLibPath);
        Assert.Contains("var ASK_SERVICE_VERSION = \"0.4.0\"", libSource, StringComparison.Ordinal);

        var lexical = Path.Combine(RepoRoot, "Examples", "Agents", "secondbrain.malda");
        var semantic = Path.Combine(RepoRoot, "Examples", "Agents", "secondbrain_semantic.malda");
        foreach (var path in new[] { lexical, semantic })
        {
            Assert.True(File.Exists(path), "missing " + path);
            var source = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("MALDA 0.1.0", source, StringComparison.Ordinal);
            Assert.Contains("GET /health", source, StringComparison.Ordinal);
            Assert.Contains("ASK_SERVICE_VERSION", source, StringComparison.Ordinal);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_version", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            await File.WriteAllTextAsync(harnessPath,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-version";
                var ASK_STORE = "SecondBrainAskVersion";
                var PRODUCT_NAME = "Version Brain";
                var ASK_PAGE_TITLE = PRODUCT_NAME;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                print("VER=" + ASK_SERVICE_VERSION);
                """);
            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("VER=0.4.0", output, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_NoteCitations_RewriteExtractAndPreviewLookup()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_cites", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            await File.WriteAllTextAsync(harnessPath,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_HTTP_HOST = "localhost";
                var ASK_SESSION_ID = "secondbrain-ask-cites";
                var ASK_STORE = "SecondBrainAskCites";
                var PRODUCT_NAME = "Cite Brain";
                var ASK_PAGE_TITLE = PRODUCT_NAME;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                var slugs = askExtractCitedSlugs("See [nota: alpha-note] and [nota: Beta Note] plus [nota: alpha-note].");
                print("N=" + string(slugs.length));
                print("S0=" + slugs[0]);
                print("S1=" + slugs[1]);
                var sources = [
                    { "slug": "alpha-note", "title": "Alpha", "path": "notes/alpha-note.md", "source": "a.md" }
                ];
                var md = askRewriteNoteCitations("Based on [nota: alpha-note].", sources);
                print("MD=" + md);
                var html = askAnswerHtmlWithCitations("Based on [nota: alpha-note].", sources);
                print("HTML=" + html);
                var chips = askRenderSourcesHtml(sources, ["alpha-note"]);
                print("CHIP=" + chips);
                askSetCatalog({
                    "notes": [
                        { "slug": "alpha-note", "title": "Alpha", "path": "notes/alpha-note.md", "source": "a.md", "summary": "Alpha summary" }
                    ]
                });
                var preview = askReadNotePreview("alpha-note");
                print("PREV=" + string(preview.ok) + "," + preview.title);
                var missing = askReadNotePreview("../etc/passwd");
                print("MISS=" + string(missing.ok));
                """);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("N=2", output, StringComparison.Ordinal);
            Assert.Contains("S0=alpha-note", output, StringComparison.Ordinal);
            Assert.Contains("S1=beta-note", output, StringComparison.Ordinal);
            Assert.Contains("MD=Based on [Alpha](#src-alpha-note).", output, StringComparison.Ordinal);
            Assert.Contains("class=\"note-cite\"", output, StringComparison.Ordinal);
            Assert.Contains("href=\"#src-alpha-note\"", output, StringComparison.Ordinal);
            Assert.Contains("data-ask-note=\"alpha-note\"", output, StringComparison.Ordinal);
            Assert.Contains("source-chip cited", output, StringComparison.Ordinal);
            Assert.Contains("PREV=true,Alpha", output, StringComparison.Ordinal);
            Assert.Contains("MISS=false", output, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskRefuseInsecurePublicBind_Blocks_Default_Creds_On_All_Interfaces()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);
        var libSource = File.ReadAllText(AskUiLibPath);
        Assert.Contains("function askRefuseInsecurePublicBind()", libSource, StringComparison.Ordinal);
        Assert.Contains("Refusing to start ASK on a non-loopback host with default credentials.", libSource, StringComparison.Ordinal);
        Assert.Contains("ASK is bound on a non-loopback host with auth off.", libSource, StringComparison.Ordinal);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_bind_gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previous = new Dictionary<string, string?>
        {
            ["MALDA_ASK_PASSWORD"] = System.Environment.GetEnvironmentVariable("MALDA_ASK_PASSWORD"),
            ["MALDA_ASK_PASSWORD_HASH"] = System.Environment.GetEnvironmentVariable("MALDA_ASK_PASSWORD_HASH"),
            ["MALDA_JWT_SECRET"] = System.Environment.GetEnvironmentVariable("MALDA_JWT_SECRET"),
            ["MALDA_COOKIE_SECRET"] = System.Environment.GetEnvironmentVariable("MALDA_COOKIE_SECRET"),
            ["MALDA_SESSION_SECRET"] = System.Environment.GetEnvironmentVariable("MALDA_SESSION_SECRET")
        };
        try
        {
            foreach (var key in previous.Keys)
            {
                System.Environment.SetEnvironmentVariable(key, null);
            }

            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            await File.WriteAllTextAsync(harnessPath,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_HTTP_HOST = "localhost";
                var ASK_SESSION_ID = "secondbrain-ask-bind-gate";
                var ASK_STORE = "SecondBrainAskBindGate";
                var PRODUCT_NAME = "Bind Gate";
                var ASK_PAGE_TITLE = PRODUCT_NAME;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                ASK_AUTH_ENABLED = true;
                ASK_HTTP_HOST = "localhost";
                askLoadAuthCredentials();
                print("LOOP=" + string(askRefuseInsecurePublicBind()));

                ASK_HTTP_HOST = "0.0.0.0";
                print("PUBLIC_DEFAULT=" + string(askRefuseInsecurePublicBind()));

                ASK_AUTH_ENABLED = false;
                print("PUBLIC_NOAUTH=" + string(askRefuseInsecurePublicBind()));
                """);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("LOOP=false", output, StringComparison.Ordinal);
            Assert.Contains("PUBLIC_DEFAULT=true", output, StringComparison.Ordinal);
            Assert.Contains("PUBLIC_NOAUTH=false", output, StringComparison.Ordinal);

            // GetEnvCache is process-wide: env vars set after the first askLoadAuthCredentials
            // would still look empty. Drive the allow-path through the same globals the
            // loader would have filled.
            await File.WriteAllTextAsync(harnessPath,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_HTTP_HOST = "0.0.0.0";
                var ASK_SESSION_ID = "secondbrain-ask-bind-ok";
                var ASK_STORE = "SecondBrainAskBindOk";
                var PRODUCT_NAME = "Bind Ok";
                var ASK_PAGE_TITLE = PRODUCT_NAME;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                ASK_AUTH_ENABLED = true;
                ASK_HTTP_HOST = "0.0.0.0";
                ASK_AUTH_USING_DEFAULT_PASSWORD = false;
                askAuthJwtSecret = "jwt-secret-for-tests";
                askAuthCookieSecret = "cookie-secret-for-tests";
                askAuthSessionSecret = "session-secret-for-tests";
                print("PUBLIC_SECURE=" + string(askRefuseInsecurePublicBind()));
                """);

            var secureOut = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("PUBLIC_SECURE=false", secureOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Refusing to start ASK", secureOut, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var pair in previous)
            {
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_GenerateDocument_HelpersAndDownloadPayload()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_generate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39022;
                var ASK_SESSION_ID = "secondbrain-ask-generate";
                var ASK_STORE = "SecondBrainAskGenerate";
                var PRODUCT_NAME = "Demo Brain";
                var ASK_PAGE_TITLE = "Demo Brain";
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                print("SAN=" + askSanitizeDownloadFilename("Q3 Summary 2026!.md"));
                print("SUG=" + askSuggestDocumentFilename("draft please", "# Project Brief\n\nScope"));
                print("MODE0=" + askGetAskMode());
                askApplyAskModeFromBody({ "askMode": "generate" });
                print("MODE1=" + askGetAskMode());

                var marked = askMarkGeneratedTurn({
                    "question": "Draft a Q3 summary",
                    "answer": "# Q3 Summary\n\n| Item | Status |\n| --- | --- |\n| Launch | [TO CONFIRM: date] |\n",
                    "sources": [],
                    "error": ""
                }, "Draft a Q3 summary");
                print("GEN=" + string(marked.generated) + "," + marked.filename);

                var weak = askMarkGeneratedTurn({
                    "question": "q",
                    "answer": "not enough notes",
                    "sources": [],
                    "error": "",
                    "weakRetrieval": true
                }, "q");
                print("WEAK=" + string(weak.generated));

                var md = askBuildGeneratedDownload(marked, "md");
                print("MD=" + string(md.ok) + "," + md.filename + "," + md.contentType);
                var html = askBuildGeneratedDownload(marked, "html");
                print("HTML=" + string(html.ok) + "," + html.filename);
                if (str.indexOf(html.body, "<!DOCTYPE html>") >= 0) {
                    print("WRAP=1");
                }
                var bad = askBuildGeneratedDownload(marked, "pdf");
                print("BAD=" + string(bad.ok) + "," + bad.error);

                askSetSession({
                    "brainDir": "secondbrain",
                    "chatOnly": false,
                    "noteCount": 1,
                    "topicCount": 1,
                    "sourceFolder": "docs",
                    "retrieval": "lexical",
                    "llm": "test-model",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "generate"
                });
                askAppendTurn(marked);
                print(askRenderPanelHtml());
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("SAN=q3-summary-2026", output, StringComparison.Ordinal);
            Assert.Contains("SUG=project-brief", output, StringComparison.Ordinal);
            Assert.Contains("MODE0=ask", output, StringComparison.Ordinal);
            Assert.Contains("MODE1=generate", output, StringComparison.Ordinal);
            Assert.Contains("GEN=true,q3-summary", output, StringComparison.Ordinal);
            Assert.Contains("WEAK=", output, StringComparison.Ordinal);
            Assert.DoesNotContain("WEAK=true", output, StringComparison.Ordinal);
            Assert.Contains("MD=true,q3-summary.md,text/markdown; charset=utf-8", output, StringComparison.Ordinal);
            Assert.Contains("HTML=true,q3-summary.html", output, StringComparison.Ordinal);
            Assert.Contains("WRAP=1", output, StringComparison.Ordinal);
            Assert.Contains("BAD=false,Invalid format.", output, StringComparison.Ordinal);
            Assert.Contains("Download Markdown", output, StringComparison.Ordinal);
            Assert.Contains("/generate/download?", output, StringComparison.Ordinal);
            Assert.Contains("fmt=md", output, StringComparison.Ordinal);
            Assert.Contains("name='askMode' value='generate' checked", output, StringComparison.Ordinal);
            Assert.Contains("data-btn-generate", output, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_TagFilterFromBody_AcceptsStringAndArray()
    {
        // HTML checkbox groups post tags as a string (single) or array (multi after form parser).
        // The old guard used `raw.length != null`, which is true for strings and crashed foreach.
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_tags", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39021;
                var ASK_SESSION_ID = "secondbrain-ask-tags";
                var ASK_STORE = "SecondBrainAskTags";
                var PRODUCT_NAME = "Tags";
                var ASK_PAGE_TITLE = "Tags";
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                askApplyTagFilterFromBody({ "tags": "alpha" });
                var one = askGetTagFilter();
                print("ONE_LEN=" + string(one.length));
                print("ONE_0=" + one[0]);

                askApplyTagFilterFromBody({ "tags": "beta, gamma" });
                var csv = askGetTagFilter();
                print("CSV_LEN=" + string(csv.length));
                print("CSV_0=" + csv[0]);
                print("CSV_1=" + csv[1]);

                askApplyTagFilterFromBody({ "tags": ["delta", "epsilon"] });
                var arr = askGetTagFilter();
                print("ARR_LEN=" + string(arr.length));
                print("ARR_0=" + arr[0]);
                print("ARR_1=" + arr[1]);

                askApplyTagFilterFromBody({ "question": "no tags field" });
                print("EMPTY_LEN=" + string(askGetTagFilter().length));
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("ONE_LEN=1", output, StringComparison.Ordinal);
            Assert.Contains("ONE_0=alpha", output, StringComparison.Ordinal);
            Assert.Contains("CSV_LEN=2", output, StringComparison.Ordinal);
            Assert.Contains("CSV_0=beta", output, StringComparison.Ordinal);
            Assert.Contains("CSV_1=gamma", output, StringComparison.Ordinal);
            Assert.Contains("ARR_LEN=2", output, StringComparison.Ordinal);
            Assert.Contains("ARR_0=delta", output, StringComparison.Ordinal);
            Assert.Contains("ARR_1=epsilon", output, StringComparison.Ordinal);
            Assert.Contains("EMPTY_LEN=0", output, StringComparison.Ordinal);
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
                var ASK_LOGO_RIGHT = "";
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

                var catalog = askGetCatalog();
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
            var source = CombinedSecondBrainSource(path);
            Assert.Contains("include \"sb/00-i18n.malda\"", source, StringComparison.Ordinal);
            Assert.Contains("include \"sb/04-build.malda\"", source, StringComparison.Ordinal);
            Assert.Contains("include \"sb/05-ask-common.malda\"", source, StringComparison.Ordinal);
            Assert.Contains("var PRODUCT_NAME = ", source, StringComparison.Ordinal);
            Assert.Contains("var ASK_TITLE_SUFFIX = ", source, StringComparison.Ordinal);
            Assert.Contains("function productName()", source, StringComparison.Ordinal);
            Assert.Contains("function applyProductNameFromBrain(", source, StringComparison.Ordinal);
            Assert.Contains("askApplyProductNameFromBrain(", source, StringComparison.Ordinal);
            Assert.Contains("askApplyLogoFromBrain(", source, StringComparison.Ordinal);
            Assert.Contains("var ASK_LOGO_RIGHT = ", source, StringComparison.Ordinal);
            Assert.Contains("var ASK_LOGO_RIGHT_HEIGHT = ", source, StringComparison.Ordinal);
            Assert.Contains("askApplyLogoRightFromBrain(", source, StringComparison.Ordinal);
            Assert.Contains("applyAskHttpPortFromCli()", source, StringComparison.Ordinal);
            Assert.Contains("applyAskHttpHostFromCli()", source, StringComparison.Ordinal);
            Assert.Contains("applyAskHttpsFromCli()", source, StringComparison.Ordinal);
            Assert.Contains("var ASK_HTTP_HOST = ", source, StringComparison.Ordinal);
            Assert.Contains("askHttpServer.setHost(", source, StringComparison.Ordinal);
            Assert.Contains("askHttpServer.enableHttps(", source, StringComparison.Ordinal);
            Assert.Contains("--https", source, StringComparison.Ordinal);
            Assert.Contains("askApplyAuthFromCli(", source, StringComparison.Ordinal);
            Assert.Contains("askRefuseInsecurePublicBind(", source, StringComparison.Ordinal);
            Assert.Contains("askSetAuthUsersRoot(", source, StringComparison.Ordinal);
            Assert.Contains("askConfigureHttpAuth(", source, StringComparison.Ordinal);
            Assert.Contains("runAskUpdateFromUpload(", source, StringComparison.Ordinal);
            Assert.Contains("--port", source, StringComparison.Ordinal);
            Assert.Contains("--host", source, StringComparison.Ordinal);
            Assert.Contains("--no-auth", source, StringComparison.Ordinal);
            Assert.Contains("--allow-register", source, StringComparison.Ordinal);
            Assert.Contains("--no-examples", source, StringComparison.Ordinal);
            Assert.Contains("--no-powered-by", source, StringComparison.Ordinal);
            Assert.Contains("--powered-by", source, StringComparison.Ordinal);
            Assert.Contains("--product-name", source, StringComparison.Ordinal);
            Assert.Contains("sbCliParseArgs(", source, StringComparison.Ordinal);
            Assert.Contains("sbCliApplyProductName(", source, StringComparison.Ordinal);
            Assert.Contains("sbCliApplyPoweredBy(", source, StringComparison.Ordinal);
            Assert.Contains("build --docs", source, StringComparison.Ordinal);
            Assert.Contains("import \"secondbrain_cli_lib.malda\"", source, StringComparison.Ordinal);
            Assert.Contains("include \"secondbrain_cli_apply_lib.malda\"", source, StringComparison.Ordinal);
            if (path.Contains("secondbrain_semantic", StringComparison.Ordinal))
            {
                Assert.Contains("find_related_notes", source, StringComparison.Ordinal);
                Assert.Contains("new Tool(", source, StringComparison.Ordinal);
            }
            Assert.Contains("product_name.txt", source, StringComparison.Ordinal);
            Assert.Contains("logo_right_height.txt", source, StringComparison.Ordinal);
            Assert.Contains("askGetToolsEnabled()", source, StringComparison.Ordinal);
            Assert.Contains("askSetToolsEnabled(false)", source, StringComparison.Ordinal);
            Assert.Contains("function answerInstructions(useTools)", source, StringComparison.Ordinal);
            Assert.Contains("function documentInstructions(useTools)", source, StringComparison.Ordinal);
            Assert.Contains("riepilogo, proposta, verbale, checklist, brief", source, StringComparison.Ordinal);
            Assert.DoesNotContain("preventivo, itinerario", source, StringComparison.Ordinal);
            Assert.Contains("function generateDocumentCli(", source, StringComparison.Ordinal);
            Assert.Contains("function askExtractCitedSlugs(", source, StringComparison.Ordinal);
            Assert.Contains("function askAnswerHtmlWithCitations(", source, StringComparison.Ordinal);
            Assert.Contains("@GET(\"/note\")", source, StringComparison.Ordinal);
            Assert.Contains("function indexShouldUseFullRebuild(", source, StringComparison.Ordinal);
            Assert.Contains("function upsertNoteMemory(", source, StringComparison.Ordinal);
            Assert.Contains("\"memoryNodeId\": noteMemoryId(note)", source, StringComparison.Ordinal);
            Assert.Contains("--reindex-memory", source, StringComparison.Ordinal);
            Assert.Contains("--rerank", source, StringComparison.Ordinal);
            Assert.Contains("sbCliNormalizeRerank(", source, StringComparison.Ordinal);
            Assert.Contains("sbCliApplyRerank(", source, StringComparison.Ordinal);
            if (path.Contains("secondbrain_semantic", StringComparison.Ordinal))
            {
                Assert.Contains("function resolveAskRerankMode(", source, StringComparison.Ordinal);
                Assert.Contains("queryOpts.rerankMode", source, StringComparison.Ordinal);
            }
            Assert.Contains("askGetAskMode()", source, StringComparison.Ordinal);
            Assert.Contains("askMarkGeneratedTurn(", source, StringComparison.Ordinal);
            Assert.Contains("newReaderAgent(", source, StringComparison.Ordinal);
            Assert.Contains("newPlainAgent(", source, StringComparison.Ordinal);
            Assert.Contains("var distiller = newPlainAgent(", source, StringComparison.Ordinal);
            Assert.Contains("var curator = newPlainAgent(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("newCodingAgent", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new CodingAgent", source, StringComparison.Ordinal);
            Assert.Contains("sbJoinUnderRoot2(brainDir, \"notes\", current.slug + \".md\")", source, StringComparison.Ordinal);
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
                var ASK_HTTP_HOST = "localhost";
                var ASK_HTTPS = false;
                var ASK_CERT_PATH = "";
                var ASK_CERT_PASSWORD = "";
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
                var f = sbCliParseArgs(["ask", "--no-auth", "--port", "9090"]);
                print("F=" + f.mode + "," + string(f.auth) + "," + string(f.port) + "," + f.error);
                var g = sbCliParseArgs(["ask", "--auth"]);
                print("G=" + g.mode + "," + string(g.auth) + "," + g.error);
                var h = sbCliParseArgs(["ask", "--allow-register"]);
                print("H=" + h.mode + "," + string(h.allowRegister) + "," + h.error);
                var i = sbCliParseArgs(["ask", "--allow-register", "--no-register"]);
                print("I=" + i.mode + "," + string(i.allowRegister) + "," + i.error);
                var j = sbCliParseArgs(["ask", "--product-name", "Acme Brain"]);
                print("J=" + j.mode + "," + j.productName + "," + j.error);
                var k = sbCliParseArgs(["build", "--docs", "./d", "--name", "Alias Name"]);
                print("K=" + k.mode + "," + k.productName + "," + k.error);
                var l = sbCliParseArgs(["ask", "--product-name"]);
                print("L=" + l.error);
                var m = sbCliParseArgs(["ask", "--product-name=Brand"]);
                print("M=" + m.mode + "," + m.productName + "," + m.error);
                var n = sbCliParseArgs(["ask", "--host", "0.0.0.0", "--port", "80"]);
                print("N=" + n.mode + "," + n.host + "," + string(n.port) + "," + n.error);
                var o = sbCliParseArgs(["ask", "--host=*"]);
                print("O=" + o.mode + "," + o.host + "," + o.error);
                var p = sbCliParseArgs(["ask", "--host"]);
                print("P=" + p.error);
                var q = sbCliParseArgs(["ask", "--host="]);
                print("Q=" + q.error);
                var r = sbCliParseArgs(["ask", "--https", "--cert", "./ask.pfx", "--cert-password", "secret"]);
                print("R=" + r.mode + "," + string(r.https) + "," + r.cert + "," + r.certPassword + "," + r.error);
                var s = sbCliParseArgs(["ask", "--https"]);
                print("S=" + s.error);
                var tEx = sbCliParseArgs(["ask", "--no-examples"]);
                print("T=" + tEx.mode + "," + string(tEx.showExamples) + "," + tEx.error);
                var uEx = sbCliParseArgs(["ask", "--no-examples", "--examples"]);
                print("U=" + uEx.mode + "," + string(uEx.showExamples) + "," + uEx.error);
                var vPb = sbCliParseArgs(["ask", "--no-powered-by"]);
                print("V=" + vPb.mode + "," + vPb.poweredBy + "," + vPb.poweredByUrl + "," + vPb.error);
                var wPb = sbCliParseArgs(["ask", "--powered-by", "Internal KB", "--powered-by-url", "https://example.local"]);
                print("W=" + wPb.mode + "," + wPb.poweredBy + "," + wPb.poweredByUrl + "," + wPb.error);
                var xPb = sbCliParseArgs(["ask", "--powered-by"]);
                print("X=" + xPb.error);
                var yPb = sbCliParseArgs(["ask", "--no-powered-by", "--powered-by", "Shown again"]);
                print("Y=" + yPb.mode + "," + yPb.poweredBy + "," + yPb.poweredByUrl + "," + yPb.error);
                var zGen = sbCliParseArgs(["generate", "--prompt", "Draft a project brief", "--out", "brief.md", "--format", "html", "--force"]);
                print("Z=" + zGen.mode + "," + zGen.prompt + "," + zGen.outPath + "," + zGen.format + "," + string(zGen.forceAnswer) + "," + zGen.error);
                var zzGen = sbCliParseArgs(["generate", "--brain", "b1", "--prompt", "New status report"]);
                print("ZZ=" + zzGen.mode + "," + zzGen.prompt + "," + zzGen.brain + "," + zzGen.error);
                var zzLeft = sbCliParseArgs(["generate", "Draft", "a", "brief"]);
                print("ZZL=" + zzLeft.error);
                var zzExtra = sbCliParseArgs(["generate", "--prompt", "ok", "extra"]);
                print("ZZX=" + zzExtra.error);
                var zf = sbCliParseArgs(["generate", "--format", "pdf"]);
                print("ZF=" + zf.error);
                var rm = sbCliParseArgs(["update", "--reindex-memory"]);
                print("RM=" + rm.mode + "," + string(rm.reindexMemory) + "," + rm.error);
                var rmOff = sbCliParseArgs(["update"]);
                print("RMOFF=" + rmOff.mode + "," + string(rmOff.reindexMemory) + "," + rmOff.error);
                var rk = sbCliParseArgs(["ask", "--rerank", "cross"]);
                print("RK=" + rk.mode + "," + rk.rerank + "," + rk.error);
                var rkOnnx = sbCliParseArgs(["ask", "--rerank=onnx"]);
                print("RKONNX=" + rkOnnx.mode + "," + rkOnnx.rerank + "," + rkOnnx.error);
                var rkOff = sbCliParseArgs(["ask", "--rerank", "off"]);
                print("RKOFF=" + rkOff.mode + "," + rkOff.rerank + "," + rkOff.error);
                var rkBad = sbCliParseArgs(["ask", "--rerank", "llm"]);
                print("RKBAD=" + rkBad.error);
                var rkMiss = sbCliParseArgs(["ask", "--rerank"]);
                print("RKMISS=" + rkMiss.error);
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("A=build,P:/docs,brain1,folders,en,", output, StringComparison.Ordinal);
            Assert.Contains("B=update,true,./src,", output, StringComparison.Ordinal);
            Assert.Contains("C=ask,8080,", output, StringComparison.Ordinal);
            Assert.Contains("D=build,,", output, StringComparison.Ordinal);
            Assert.Contains("E=Unknown flag: --unknown", output, StringComparison.Ordinal);
            Assert.Contains("F=ask,false,9090,", output, StringComparison.Ordinal);
            Assert.Contains("G=ask,true,", output, StringComparison.Ordinal);
            Assert.Contains("H=ask,true,", output, StringComparison.Ordinal);
            Assert.Contains("I=ask,false,", output, StringComparison.Ordinal);
            Assert.Contains("J=ask,Acme Brain,", output, StringComparison.Ordinal);
            Assert.Contains("K=build,Alias Name,", output, StringComparison.Ordinal);
            Assert.Contains("L=Missing value for --product-name.", output, StringComparison.Ordinal);
            Assert.Contains("M=ask,Brand,", output, StringComparison.Ordinal);
            Assert.Contains("N=ask,0.0.0.0,80,", output, StringComparison.Ordinal);
            Assert.Contains("O=ask,0.0.0.0,", output, StringComparison.Ordinal);
            Assert.Contains("P=Missing value for --host.", output, StringComparison.Ordinal);
            Assert.Contains("Q=Invalid --host value.", output, StringComparison.Ordinal);
            Assert.Contains("R=ask,true,./ask.pfx,secret,", output, StringComparison.Ordinal);
            Assert.Contains("S=--https requires --cert <path>.", output, StringComparison.Ordinal);
            Assert.Contains("T=ask,false,", output, StringComparison.Ordinal);
            Assert.Contains("U=ask,true,", output, StringComparison.Ordinal);
            Assert.Contains("V=ask,,,", output, StringComparison.Ordinal);
            Assert.Contains("W=ask,Internal KB,https://example.local,", output, StringComparison.Ordinal);
            Assert.Contains("X=Missing value for --powered-by.", output, StringComparison.Ordinal);
            Assert.Contains("Y=ask,Shown again,,", output, StringComparison.Ordinal);
            Assert.Contains("Z=generate,Draft a project brief,brief.md,html,true,", output, StringComparison.Ordinal);
            Assert.Contains("ZZ=generate,New status report,b1,", output, StringComparison.Ordinal);
            Assert.Contains("ZZL=Unexpected argument: Draft", output, StringComparison.Ordinal);
            Assert.Contains("ZZX=Unexpected argument: extra", output, StringComparison.Ordinal);
            Assert.Contains("ZF=Invalid --format (use md or html).", output, StringComparison.Ordinal);
            Assert.Contains("RM=update,true,", output, StringComparison.Ordinal);
            Assert.Contains("RMOFF=update,false,", output, StringComparison.Ordinal);
            Assert.Contains("RK=ask,cross,", output, StringComparison.Ordinal);
            Assert.Contains("RKONNX=ask,onnx,", output, StringComparison.Ordinal);
            Assert.Contains("RKOFF=ask,off,", output, StringComparison.Ordinal);
            Assert.Contains("RKBAD=Invalid --rerank (use off, cross, or onnx).", output, StringComparison.Ordinal);
            Assert.Contains("RKMISS=Missing value for --rerank.", output, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_UsersStore_RegisterAndVerifyLogin()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_users", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                $$"""
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-users-test";
                var ASK_STORE = "SecondBrainAskUsersTest";
                var PRODUCT_NAME = "Users Brain";
                var ASK_TITLE_SUFFIX = " — ASK";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                askSetAuthUsersRoot("{{tempDir.Replace("\\", "/")}}");
                askLoadAuthCredentials();
                print("PATH=" + askUsersPath());
                print("COUNT0=" + string(askUsersCount()));
                var reg = askRegisterUser("alice", "secret1");
                print("REG=" + string(reg.ok) + "," + reg.error);
                print("COUNT1=" + string(askUsersCount()));
                var regEmail = askRegisterUser("alice@example.com", "secret1");
                print("REG_EMAIL=" + string(regEmail.ok) + "," + regEmail.error);
                var okEmail = askVerifyLogin("alice@example.com", "secret1");
                print("LOGIN_EMAIL=" + string(okEmail.ok) + "," + okEmail.username);
                var dup = askRegisterUser("Alice", "other99");
                print("DUP=" + string(dup.ok));
                var reserved = askRegisterUser("admin", "secret1");
                print("RESERVED=" + string(reserved.ok));
                var okUser = askVerifyLogin("alice", "secret1");
                print("LOGIN_USER=" + string(okUser.ok) + "," + okUser.username + "," + okUser.role);
                var badUser = askVerifyLogin("alice", "wrong");
                print("LOGIN_BAD=" + string(badUser.ok));
                var boot = askVerifyLogin("admin", "password");
                print("LOGIN_BOOT=" + string(boot.ok) + "," + boot.role);
                var dis = askSetUserEnabled("alice", false);
                print("DIS=" + string(dis.ok));
                var disabledLogin = askVerifyLogin("alice", "secret1");
                print("LOGIN_DIS=" + string(disabledLogin.ok));
                askSetUserEnabled("alice", true);
                var role = askSetUserRole("alice", "admin");
                print("ROLE=" + string(role.ok));
                ASK_CONV_CURRENT = "abcdef0123456789abcdef0123456789";
                ASK_AUTH_ENABLED = false;
                print("SCOPE_OFF=" + askHistoryScope(null));
                ASK_AUTH_ENABLED = true;
                print("WEAK=" + askWeakRetrievalMessage());
                askAudit("test", null, { "n": 1 });
                print("AUDIT=" + string(io.hasFile(ASK_AUDIT_PATH)));
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("COUNT0=0", output, StringComparison.Ordinal);
            Assert.Contains("REG=true,", output, StringComparison.Ordinal);
            Assert.Contains("COUNT1=1", output, StringComparison.Ordinal);
            Assert.Contains("REG_EMAIL=true,", output, StringComparison.Ordinal);
            Assert.Contains("LOGIN_EMAIL=true,alice@example.com", output, StringComparison.Ordinal);
            Assert.Contains("DUP=false", output, StringComparison.Ordinal);
            Assert.Contains("RESERVED=false", output, StringComparison.Ordinal);
            Assert.Contains("LOGIN_USER=true,alice,ask", output, StringComparison.Ordinal);
            Assert.Contains("LOGIN_BAD=false", output, StringComparison.Ordinal);
            Assert.Contains("LOGIN_BOOT=true,admin", output, StringComparison.Ordinal);
            Assert.Contains("ROLE=true", output, StringComparison.Ordinal);
            Assert.Contains("DIS=true", output, StringComparison.Ordinal);
            Assert.Contains("LOGIN_DIS=false", output, StringComparison.Ordinal);
            Assert.Contains("SCOPE_OFF=abcdef0123456789abcdef0123456789", output, StringComparison.Ordinal);
            Assert.Contains("AUDIT=true", output, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(tempDir, "users.json")), "users.json should be written");
            Assert.True(File.Exists(Path.Combine(tempDir, "ask_audit.jsonl")), "audit log should be written");
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
                var ASK_HTTP_HOST = "localhost";
                var ASK_SESSION_ID = "secondbrain-ask-port-test";
                var ASK_STORE = "SecondBrainAskPortTest";
                var PRODUCT_NAME = "Port Brain";
                var ASK_TITLE_SUFFIX = " — ASK";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
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
                var f = askParseHttpHostFromArgs(["script.malda", "--host", "0.0.0.0"], "localhost");
                print("F=" + string(f.ok) + "," + string(f.changed) + "," + f.host);
                var g = askParseHttpHostFromArgs(["--host=*"], "localhost");
                print("G=" + string(g.ok) + "," + string(g.changed) + "," + g.host);
                var h = askParseHttpHostFromArgs(["--host="], "localhost");
                print("H=" + string(h.ok) + "," + string(h.changed) + "," + h.host);
                var i = askParseHttpHostFromArgs(["--strict-types"], "localhost");
                print("I=" + string(i.ok) + "," + string(i.changed) + "," + i.host);
                print("J=" + askFormatOpenUrl());
                ASK_HTTP_HOST = "0.0.0.0";
                print("K=" + askFormatOpenUrl());
                var l = askParseHttpsFromArgs(["ask", "--https", "--cert", "./a.pfx", "--cert-password", "x"]);
                print("L=" + string(l.ok) + "," + string(l.https) + "," + l.cert + "," + l.certPassword + "," + l.error);
                var m = askParseHttpsFromArgs(["ask", "--https"]);
                print("M=" + string(m.ok) + "," + m.error);
                ASK_HTTPS = true;
                print("N=" + askFormatOpenUrl());
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("A=true,true,8080", output, StringComparison.Ordinal);
            Assert.Contains("B=true,true,41234", output, StringComparison.Ordinal);
            Assert.Contains("C=true,true,9090", output, StringComparison.Ordinal);
            Assert.Contains("D=false,false,39018", output, StringComparison.Ordinal);
            Assert.Contains("E=true,false,39018", output, StringComparison.Ordinal);
            Assert.Contains("F=true,true,0.0.0.0", output, StringComparison.Ordinal);
            Assert.Contains("G=true,true,0.0.0.0", output, StringComparison.Ordinal);
            Assert.Contains("H=false,false,localhost", output, StringComparison.Ordinal);
            Assert.Contains("I=true,false,localhost", output, StringComparison.Ordinal);
            Assert.Contains("J=http://localhost:39018/", output, StringComparison.Ordinal);
            Assert.Contains("K=http://0.0.0.0:39018/", output, StringComparison.Ordinal);
            Assert.Contains("L=true,true,./a.pfx,x,", output, StringComparison.Ordinal);
            Assert.Contains("M=false,--https requires --cert <path>.", output, StringComparison.Ordinal);
            Assert.Contains("N=https://0.0.0.0:39018/", output, StringComparison.Ordinal);
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
                var ASK_LOGO_RIGHT = "";
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
            Assert.DoesNotContain("<img class='logo-right'", html, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_ThemeToggle_DefaultsLight_And_AppliesDark()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_ui_theme", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-theme";
                var ASK_STORE = "SecondBrainAskTheme";
                var PRODUCT_NAME = "Theme Brain";
                var ASK_TITLE_SUFFIX = " — ASK";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
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
                    "llm": "test",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "theme"
                });
                print("THEME=" + askGetTheme());
                print("---LIGHT---");
                print(askRenderPage());
                askSetTheme("dark");
                print("THEME2=" + askGetTheme());
                print("---DARK---");
                print(askRenderPage());
                ASK_THEME_CURRENT = "";
                print("COOKIE_LOAD=" + askLoadThemeFromCookie({ "malda_ask_theme": "dark" }));
                print("THEME3=" + askGetTheme());
                ASK_THEME_CURRENT = "";
                print("COOKIE_CLEAR=" + askLoadThemeFromCookie({}));
                print("THEME4=" + askGetTheme());
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("THEME=light", output, StringComparison.Ordinal);
            Assert.Contains("THEME2=dark", output, StringComparison.Ordinal);
            Assert.Contains("COOKIE_LOAD=dark", output, StringComparison.Ordinal);
            Assert.Contains("THEME3=dark", output, StringComparison.Ordinal);
            // Cookie clear leaves ui.state theme=dark from prior set/load.
            Assert.Contains("COOKIE_CLEAR=", output, StringComparison.Ordinal);
            Assert.Contains("THEME4=dark", output, StringComparison.Ordinal);
            var lightIdx = output.IndexOf("---LIGHT---", StringComparison.Ordinal);
            var darkIdx = output.IndexOf("---DARK---", StringComparison.Ordinal);
            Assert.True(lightIdx >= 0 && darkIdx > lightIdx);
            var lightHtml = output.Substring(lightIdx, darkIdx - lightIdx);
            var darkHtml = output.Substring(darkIdx);
            Assert.Contains("data-theme='light'", lightHtml, StringComparison.Ordinal);
            Assert.Contains("data-ask-theme='light'", lightHtml, StringComparison.Ordinal);
            Assert.Contains(">Light</a>", lightHtml, StringComparison.Ordinal);
            Assert.DoesNotContain(">Dark</a>", lightHtml, StringComparison.Ordinal);
            Assert.Contains("theme=dark", lightHtml, StringComparison.Ordinal);
            Assert.Contains("data-theme='dark'", darkHtml, StringComparison.Ordinal);
            Assert.Contains("data-ask-theme='dark'", darkHtml, StringComparison.Ordinal);
            Assert.Contains(">Dark</a>", darkHtml, StringComparison.Ordinal);
            Assert.DoesNotContain(">Light</a>", darkHtml, StringComparison.Ordinal);
            Assert.Contains("theme=light", darkHtml, StringComparison.Ordinal);
            Assert.Contains("class='active' href='/?c=", darkHtml, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task AskUi_AutoLoadsLogoRight_From_DiskBrainFolder_When_ASK_LOGO_RIGHT_Empty()
    {
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var leftSvg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"#2f6f5e\"/></svg>";
        var rightSvg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><circle cx=\"8\" cy=\"8\" r=\"7\" fill=\"#8a5a12\"/></svg>";
        var leftB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(leftSvg));
        var rightB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rightSvg));
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_ui_disk_logo_right", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var brainDir = Path.Combine(tempDir, "brain");
            Directory.CreateDirectory(brainDir);
            File.WriteAllText(Path.Combine(brainDir, "logo.svg"), leftSvg, utf8NoBom);
            File.WriteAllText(Path.Combine(brainDir, "logo_right.svg"), rightSvg, utf8NoBom);
            // Brain file overrides host ASK_LOGO_RIGHT_HEIGHT (96).
            File.WriteAllText(Path.Combine(brainDir, "logo_right_height.txt"), "96px\n", utf8NoBom);
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));

            var brainLiteral = brainDir.Replace("\\", "\\\\");
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                $$"""
                var ASK_HTTP_PORT = 39018;
                var ASK_SESSION_ID = "secondbrain-ask-disk-logo-right";
                var ASK_STORE = "SecondBrainAskDiskLogoRight";
                var PRODUCT_NAME = "Dual Logo Brain";
                var ASK_TITLE_SUFFIX = "";
                var ASK_PAGE_TITLE = PRODUCT_NAME + ASK_TITLE_SUFFIX;
                var ASK_POWERED_BY = "Powered by MALDA";
                var ASK_POWERED_BY_URL = "https://example.com/malda";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var ASK_LOGO_RIGHT_HEIGHT = 72;
                var UI_LANG = "en";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                print(askApplyLogoFromBrain("{{brainLiteral}}"));
                print(askApplyLogoRightFromBrain("{{brainLiteral}}"));
                print("HEIGHT=" + ASK_LOGO_RIGHT_HEIGHT);
                askSetSession({
                    "brainDir": "{{brainLiteral}}",
                    "chatOnly": false,
                    "noteCount": 1,
                    "topicCount": 1,
                    "sourceFolder": "docs",
                    "retrieval": "lexical",
                    "llm": "test",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "dual logo"
                });
                print("---HTML---");
                print(askRenderPage());
                """,
                utf8NoBom);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("logo.svg", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("logo_right.svg", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("data:image/svg+xml;base64," + leftB64, output, StringComparison.Ordinal);
            Assert.Contains("data:image/svg+xml;base64," + rightB64, output, StringComparison.Ordinal);
            Assert.Contains("<img class='logo'", output, StringComparison.Ordinal);
            Assert.Contains("<img class='logo-right'", output, StringComparison.Ordinal);
            Assert.Contains("class='brand has-left-logo'", output, StringComparison.Ordinal);
            Assert.Contains("HEIGHT=96", output, StringComparison.Ordinal);
            Assert.Contains(".logo-right{height:96px;", output, StringComparison.Ordinal);
            Assert.Contains("class='brand-title-row'", output, StringComparison.Ordinal);
            // Left logo + title row share grid row 1; powered-by is under the title column.
            var logoPos = output.IndexOf("<img class='logo'", StringComparison.Ordinal);
            var h1Pos = output.IndexOf("<h1>Dual Logo Brain</h1>", StringComparison.Ordinal);
            var logoRightPos = output.IndexOf("<img class='logo-right'", StringComparison.Ordinal);
            var poweredPos = output.IndexOf("<p class='powered-by'", StringComparison.Ordinal);
            Assert.True(logoPos >= 0 && logoPos < h1Pos, "left logo should precede title in brand grid");
            Assert.True(h1Pos >= 0, "expected product title");
            Assert.True(logoRightPos > h1Pos, "right logo should follow product name in title row");
            Assert.True(poweredPos > logoRightPos, "powered-by markup should be below title row / right logo");
            Assert.Contains("</h1><img class='logo-right'", output, StringComparison.Ordinal);
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
                var ASK_LOGO_RIGHT = "";
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
                var ASK_LOGO_RIGHT = "";
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

    [Fact]
    public async Task AskUi_SessionMeta_PinnedSharedStoreSurvivesLruPressure()
    {
        // Regression: unpinned shared meta + ui.state(..., {}) left the meta bar
        // as "Note null · Temi null" after TTL/LRU. ASK now pins the shared scope.
        Assert.True(File.Exists(AskUiLibPath), "missing ask UI lib: " + AskUiLibPath);

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_sb_ask_session_pin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        HttpServerInstance.ClearAllComponentState();
        HttpServerInstance.ConfigureComponentStatePolicy(512, 128, 1_800_000);
        try
        {
            File.Copy(AskUiLibPath, Path.Combine(tempDir, "secondbrain_ask_ui_lib.malda"));
            var harnessPath = Path.Combine(tempDir, "harness.malda");
            File.WriteAllText(harnessPath,
                """
                var ASK_HTTP_PORT = 39029;
                var ASK_SESSION_ID = "secondbrain-ask-pin-test";
                var ASK_STORE = "SecondBrainAskPinTest";
                var PRODUCT_NAME = "Pin Brain";
                var ASK_PAGE_TITLE = "Pin Brain";
                var ASK_POWERED_BY = "";
                var ASK_POWERED_BY_URL = "";
                var ASK_LOGO = "";
                var ASK_LOGO_RIGHT = "";
                var UI_LANG = "it";
                var askHttpServer = null;

                function runAskTurn(question) {
                    return { "question": question, "answer": "ok", "sources": [], "error": "" };
                }

                include "secondbrain_ask_ui_lib.malda";

                // Isolate from other Sequential tests that leave component state.
                componentStateClear();

                askSetCatalog({
                    "notes": [{ "title": "n1", "tags": ["t"] }],
                    "topics": [{ "slug": "tema" }],
                    "sourceFolder": "docs"
                });
                askSetSession({
                    "brainDir": "secondbrain",
                    "chatOnly": false,
                    "noteCount": 7,
                    "topicCount": 3,
                    "sourceFolder": "docs",
                    "retrieval": "GraphMemory",
                    "llm": "test-model",
                    "title": ASK_PAGE_TITLE,
                    "subtitle": "meta"
                });

                // Tiny store: flood with unpinned conversation scopes. Pinned shared
                // entry must survive (askPinSharedStore already ran).
                componentStateConfigure(4, 256, 86400000);
                var i = 0;
                while (i < 12) {
                    ui.setState(ASK_STORE, "history", [], "conv-" + string(i));
                    i = i + 1;
                }

                var session = askGetSession();
                var catalog = askGetCatalog();
                print(string(session.noteCount));
                print(string(session.topicCount));
                print(askAsText(session.retrieval));
                print(askAsText(session.llm));
                if (catalog == null) {
                    print("catalog-missing");
                } else {
                    print(string(catalog.notes.length));
                }
                print(askRenderPage());

                // Explicit clear still wipes; peek must not resurrect via get-or-create.
                componentStateClear(ASK_STORE, ASK_SESSION_ID);
                var empty = askGetSession();
                print("after-clear:" + askMetaCount(empty.noteCount));
                """,
                Encoding.UTF8);

            var output = await InterpretAndCaptureAsync(harnessPath);
            Assert.Contains("7", output, StringComparison.Ordinal);
            Assert.Contains("3", output, StringComparison.Ordinal);
            Assert.Contains("GraphMemory", output, StringComparison.Ordinal);
            Assert.Contains("test-model", output, StringComparison.Ordinal);
            Assert.Contains("1", output, StringComparison.Ordinal); // catalog notes length
            Assert.DoesNotContain("catalog-missing", output, StringComparison.Ordinal);
            Assert.Contains("Note <strong>7</strong>", output, StringComparison.Ordinal);
            Assert.Contains("Temi <strong>3</strong>", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Note <strong>null</strong>", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Temi <strong>null</strong>", output, StringComparison.Ordinal);
            Assert.Contains("after-clear:0", output, StringComparison.Ordinal);
        }
        finally
        {
            HttpServerInstance.ClearAllComponentState();
            HttpServerInstance.ConfigureComponentStatePolicy(512, 128, 1_800_000);
            SafeDeleteDirectory(tempDir);
        }
    }
}
