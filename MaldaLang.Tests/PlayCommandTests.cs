// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Net;
using System.Net.Http;
using MaldaLang.Compiler;
using MaldaLang.Scaffolding;

namespace MaldaLang.Tests;

public class PlayCommandTests : TestBase
{
    private static JavaScriptCompileResult CompileJs(string sourcePath, string outputPath)
    {
        var compiler = new Compiler.Compiler();
        var result = compiler.CompileToJavaScript(sourcePath, outputPath);
        return new JavaScriptCompileResult(result.Success, result.OutputPath, result.ErrorMessage);
    }

    [Fact]
    public void TryParse_MissingFile_WritesUsage()
    {
        var error = new StringWriter();
        var ok = PlayCommandOptionsParser.TryParse(Array.Empty<string>(), error, out var options);

        Assert.False(ok);
        Assert.Null(options);
        Assert.Contains("Usage: malda play", error.ToString());
    }

    [Fact]
    public void TryParse_HappyPath_ParsesPortOpenAndHost()
    {
        var error = new StringWriter();
        var ok = PlayCommandOptionsParser.TryParse(
            new[] { "app.malda", "--port", "9001", "--open", "--host", "127.0.0.1" },
            error,
            out var options);

        Assert.True(ok);
        Assert.NotNull(options);
        Assert.Equal("app.malda", options!.SourcePath);
        Assert.Equal(9001, options.Port);
        Assert.True(options.OpenBrowser);
        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Prepare_CompilesHostAndCopiesAssets()
    {
        var root = CreateTempDirectory("malda_play_prepare_");
        try
        {
            var sourceDir = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(sourceDir, "assets"));
            File.WriteAllText(Path.Combine(sourceDir, "app.malda"), """
                game.createCanvas(64, 48, "#app");
                game.setBackground("#101722");
                """);
            File.WriteAllText(Path.Combine(sourceDir, "index.html"), """
                <!DOCTYPE html>
                <html><body>
                <div id="app"></div>
                <script src="./malda-js-runtime.js"></script>
                <script src="./app.js"></script>
                </body></html>
                """);
            File.WriteAllText(Path.Combine(sourceDir, "assets", "token.txt"), "ok");

            var runner = new PlayCommandRunner(CompileJs);
            var preview = Path.Combine(root, "preview");
            var output = new StringWriter();
            var error = new StringWriter();
            var prepared = runner.Prepare(
                new PlayCommandOptions
                {
                    SourcePath = Path.Combine(sourceDir, "app.malda"),
                    PreviewDirectory = preview
                },
                output,
                error);

            Assert.NotNull(prepared);
            Assert.Equal(string.Empty, error.ToString());
            Assert.True(File.Exists(Path.Combine(preview, "app.js")));
            Assert.True(File.Exists(Path.Combine(preview, "malda-js-runtime.js")));
            Assert.True(File.Exists(Path.Combine(preview, "index.html")));
            Assert.True(File.Exists(Path.Combine(preview, "assets", "token.txt")));

            var html = File.ReadAllText(Path.Combine(preview, "index.html"));
            var runtimeIdx = html.IndexOf("malda-js-runtime.js", StringComparison.Ordinal);
            var appIdx = html.IndexOf("./app.js", StringComparison.Ordinal);
            Assert.True(runtimeIdx >= 0 && appIdx > runtimeIdx);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_MissingFile_ReturnsNull()
    {
        var runner = new PlayCommandRunner(CompileJs);
        var error = new StringWriter();
        var prepared = runner.Prepare(
            new PlayCommandOptions { SourcePath = Path.Combine(Path.GetTempPath(), "missing-play.malda") },
            new StringWriter(),
            error);

        Assert.Null(prepared);
        Assert.Contains("not found", error.ToString());
    }

    [Fact]
    public void Prepare_GameTemplate_Compiles()
    {
        var root = CreateTempDirectory("malda_play_template_");
        var destination = Path.Combine(root, "starter");
        try
        {
            var scaffolder = new TemplateScaffolder();
            Assert.Equal(0, scaffolder.Scaffold("game", destination, new StringWriter(), new StringWriter()));

            var runner = new PlayCommandRunner(CompileJs);
            var preview = Path.Combine(root, "preview");
            var error = new StringWriter();
            var prepared = runner.Prepare(
                new PlayCommandOptions
                {
                    SourcePath = Path.Combine(destination, "app.malda"),
                    PreviewDirectory = preview
                },
                new StringWriter(),
                error);

            Assert.NotNull(prepared);
            Assert.Equal(string.Empty, error.ToString());
            Assert.True(File.Exists(Path.Combine(preview, "malda-js-runtime.js")));
            var html = File.ReadAllText(prepared!.HostHtmlPath);
            Assert.Contains("malda-js-runtime.js", html);
            Assert.Contains("./app.js", html);
            var js = File.ReadAllText(Path.Combine(preview, "app.js"));
            var gameOverIdx = js.IndexOf("gameOver = false", StringComparison.Ordinal);
            var updateIdx = js.IndexOf("function updateGame", StringComparison.Ordinal);
            Assert.True(gameOverIdx >= 0 && updateIdx > gameOverIdx);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task StartServer_ServesIndexHtmlAsync()
    {
        var root = CreateTempDirectory("malda_play_http_");
        try
        {
            var sourceDir = Path.Combine(root, "game");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "app.malda"), """
                game.createCanvas(32, 32, "#app");
                """);

            var runner = new PlayCommandRunner(CompileJs);
            var preview = Path.Combine(root, "preview");
            var prepared = runner.Prepare(
                new PlayCommandOptions
                {
                    SourcePath = Path.Combine(sourceDir, "app.malda"),
                    PreviewDirectory = preview
                },
                new StringWriter(),
                new StringWriter());
            Assert.NotNull(prepared);

            using var server = runner.StartServer(
                new PlayCommandOptions { Port = 0 },
                prepared!.PreviewDirectory,
                new StringWriter());
            Assert.NotNull(server);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(server!.Url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("malda-js-runtime.js", body);

            var runtime = await client.GetAsync(new Uri(new Uri(server.Url), "malda-js-runtime.js"));
            Assert.Equal(HttpStatusCode.OK, runtime.StatusCode);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Prepare_FullstackSource_ReturnsNullWithCompileHint()
    {
        var root = CreateTempDirectory("malda_play_fullstack_");
        try
        {
            var sourcePath = Path.Combine(root, "app.malda");
            File.WriteAllText(sourcePath, """
                @GET("/api/scores")
                function listScores() {
                    return [];
                }

                @client()
                function startGame() {
                    game.createCanvas(64, 48, "#app");
                }
                """);

            var runner = new PlayCommandRunner(CompileJs);
            var error = new StringWriter();
            var prepared = runner.Prepare(
                new PlayCommandOptions
                {
                    SourcePath = sourcePath,
                    PreviewDirectory = Path.Combine(root, "preview")
                },
                new StringWriter(),
                error);

            Assert.Null(prepared);
            Assert.Contains("JavaScript-only preview", error.ToString());
            Assert.Contains("--mode fullstack", error.ToString());
            Assert.False(Directory.Exists(Path.Combine(root, "preview")));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }
}
