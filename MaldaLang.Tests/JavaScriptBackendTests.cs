// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MaldaLang.Compiler;
using MaldaLang.Tests.Conformance.Tier0;
using MaldaLang.Tests.Planning;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class JavaScriptBackendTests : TestBase
{
    [Fact]
    public void TranspileToJavaScriptFromSource_ReturnsExpectedModuleStructure()
    {
        var source = """
            println("hello");
            """;

        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("const MaldaApp = (() => {", js, StringComparison.Ordinal);
        Assert.Contains("const mlRuntime = globalThis.mlRuntime;", js, StringComparison.Ordinal);
        Assert.Contains("function main()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.builtins.println(\"hello\")", js, StringComparison.Ordinal);
        Assert.Contains("return { main };", js, StringComparison.Ordinal);
        Assert.Contains("globalThis.MaldaApp = MaldaApp;", js, StringComparison.Ordinal);
        Assert.Contains("module.exports = MaldaApp;", js, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileToJavaScript_FromFile_ReturnsExpectedJavaScript()
    {
        var root = CreateTempDirectory("malda_js_transpile_file_");
        try
        {
            var sourcePath = Path.Combine(root, "program.malda");
            File.WriteAllText(sourcePath, "println(\"from-file\");");

            var compiler = new Compiler.Compiler();
            var js = compiler.TranspileToJavaScript(sourcePath);

            Assert.Contains("function main()", js, StringComparison.Ordinal);
            Assert.Contains("mlRuntime.builtins.println(\"from-file\")", js, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Compile_ModeJavaScript_AppendsJsExtension_WhenOutputHasNoExtension()
    {
        var root = CreateTempDirectory("malda_js_compile_ext_");
        try
        {
            var sourcePath = Path.Combine(root, "program.malda");
            File.WriteAllText(sourcePath, "print(\"ok\");");

            var outputBasePath = Path.Combine(root, "bundle");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputBasePath, CompilationMode.JavaScript, includeLLamaSharp: false);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(outputBasePath + ".js", result.OutputPath);
            Assert.True(File.Exists(result.OutputPath!));
            Assert.True(File.Exists(Path.Combine(root, "index.html")));
            Assert.True(File.Exists(Path.Combine(root, "malda-js-runtime.js")));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Compile_ModeJavaScript_RespectsExplicitOutputPathWithExtension()
    {
        var root = CreateTempDirectory("malda_js_compile_explicit_");
        try
        {
            var sourcePath = Path.Combine(root, "program.malda");
            File.WriteAllText(sourcePath, "print(\"ok\");");

            var explicitOutputPath = Path.Combine(root, "custom-output.mjs");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, explicitOutputPath, CompilationMode.JavaScript, includeLLamaSharp: false);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(explicitOutputPath, result.OutputPath);
            Assert.True(File.Exists(explicitOutputPath));

            var indexHtml = File.ReadAllText(Path.Combine(root, "index.html"));
            Assert.Contains("./malda-js-runtime.js", indexHtml, StringComparison.Ordinal);
            Assert.Contains("./custom-output.mjs", indexHtml, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Compile_ModeJavaScript_WritesSourceMapAndReferenceComment()
    {
        var root = CreateTempDirectory("malda_js_sourcemap_compile_");
        try
        {
            var sourcePath = Path.Combine(root, "program.malda");
            File.WriteAllText(sourcePath, "println(\"map\");");

            var outputPath = Path.Combine(root, "bundle.js");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputPath, CompilationMode.JavaScript, includeLLamaSharp: false);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(outputPath));
            Assert.True(File.Exists(outputPath + ".map"));

            var generatedJs = File.ReadAllText(outputPath);
            Assert.Contains("//# sourceMappingURL=bundle.js.map", generatedJs, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Compile_ModeJavaScript_SourceMapContainsSourceAndContent()
    {
        var root = CreateTempDirectory("malda_js_sourcemap_payload_");
        try
        {
            var sourceCode = """
                var x = 1;
                println(x);
                """;
            var sourcePath = Path.Combine(root, "program.malda");
            File.WriteAllText(sourcePath, sourceCode);

            var outputPath = Path.Combine(root, "mapped.js");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputPath, CompilationMode.JavaScript, includeLLamaSharp: false);

            Assert.True(result.Success, result.ErrorMessage);
            var sourceMapPath = outputPath + ".map";
            Assert.True(File.Exists(sourceMapPath));

            var sourceMapJson = File.ReadAllText(sourceMapPath);
            using var document = JsonDocument.Parse(sourceMapJson);
            var rootElement = document.RootElement;

            Assert.Equal(3, rootElement.GetProperty("version").GetInt32());
            Assert.Equal("mapped.js", rootElement.GetProperty("file").GetString());
            Assert.Contains("program.malda", rootElement.GetProperty("sources")[0].GetString(), StringComparison.Ordinal);
            Assert.Contains("println(x);", rootElement.GetProperty("sourcesContent")[0].GetString(), StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(rootElement.GetProperty("mappings").GetString()));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Compile_ModePwa_WritesPwaShellArtifactsRuntimeAndSourceMap()
    {
        var root = CreateTempDirectory("malda_pwa_compile_");
        try
        {
            var sourcePath = Path.Combine(root, "program.malda");
            File.WriteAllText(sourcePath, "println(\"pwa\");");

            var outputDirectory = Path.Combine(root, "dist");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDirectory, CompilationMode.PWA, includeLLamaSharp: false);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(outputDirectory, result.OutputPath);
            Assert.True(Directory.Exists(outputDirectory));

            var scriptPath = Path.Combine(outputDirectory, "program.js");
            var sourceMapPath = scriptPath + ".map";
            var manifestPath = Path.Combine(outputDirectory, "manifest.webmanifest");
            var indexPath = Path.Combine(outputDirectory, "index.html");
            var serviceWorkerPath = Path.Combine(outputDirectory, "sw.js");
            var runtimePath = Path.Combine(outputDirectory, "malda-js-runtime.js");
            var iconPath = Path.Combine(outputDirectory, "icon.svg");

            Assert.True(File.Exists(indexPath));
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(serviceWorkerPath));
            Assert.True(File.Exists(runtimePath));
            Assert.True(File.Exists(iconPath));
            Assert.True(File.Exists(scriptPath));
            Assert.True(File.Exists(sourceMapPath));

            var manifest = File.ReadAllText(manifestPath);
            Assert.Contains("\"display\": \"standalone\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"src\": \"./icon.svg\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"sizes\": \"512x512\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"type\": \"image/svg+xml\"", manifest, StringComparison.Ordinal);

            var indexHtml = File.ReadAllText(indexPath);
            Assert.Contains("./malda-js-runtime.js", indexHtml, StringComparison.Ordinal);
            Assert.Contains("./program.js", indexHtml, StringComparison.Ordinal);
            Assert.Contains("navigator.serviceWorker.register(\"./sw.js\")", indexHtml, StringComparison.Ordinal);

            var serviceWorker = File.ReadAllText(serviceWorkerPath);
            Assert.Contains("./program.js", serviceWorker, StringComparison.Ordinal);
            Assert.Contains("./malda-js-runtime.js", serviceWorker, StringComparison.Ordinal);
            Assert.Contains("./icon.svg", serviceWorker, StringComparison.Ordinal);

            var generatedJs = File.ReadAllText(scriptPath);
            Assert.Contains("//# sourceMappingURL=program.js.map", generatedJs, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Compile_ModePwa_DefaultsToSourceNamedDirectory_WhenOutputPathIsBlank()
    {
        var root = CreateTempDirectory("malda_pwa_default_dir_");
        try
        {
            var sourcePath = Path.Combine(root, "hello_template.malda.html");
            File.WriteAllText(sourcePath, "<div>Hello {{ name }}</div>");

            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, string.Empty, CompilationMode.PWA, includeLLamaSharp: false);

            var expectedOutputDirectory = Path.Combine(root, "hello_template");
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(expectedOutputDirectory, result.OutputPath);
            Assert.True(Directory.Exists(expectedOutputDirectory));
            Assert.True(File.Exists(Path.Combine(expectedOutputDirectory, "hello_template.js")));
            Assert.True(File.Exists(Path.Combine(expectedOutputDirectory, "index.html")));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void MaldaHtmlTemplate_Preprocessing_ProducesRenderRootAndBootstrapArtifacts()
    {
        var templateSource = """
            <section class="welcome">Hello {{ name }}</section>
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(templateSource, "view.malda.html");

        Assert.Contains("function renderRoot(rootSelector)", js, StringComparison.Ordinal);
        Assert.Contains("function bootstrap(rootSelector)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.dom.html(rootSelector, __maldaTemplateHtml)", js, StringComparison.Ordinal);
        Assert.Contains("return { main, renderRoot, bootstrap };", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsPrintPrintlnSleep_ToMlRuntimeBuiltins()
    {
        var source = """
            var printed = print("p");
            println("pl");
            sleep(10);
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.builtins.print(\"p\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.builtins.println(\"pl\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.builtins.sleep(10)", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsTypeIntrospectionAndAllBuiltins()
    {
        var source = """
            var d = dict { "k": 1 };
            var tag = typeOf(d);
            var legacy = isTag(1, "integer");
            var numeric = isNumber(3.14);
            var tasks = all(async 1, async 2);
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.markDict({", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.typeOf(d)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.isTag(1, \"integer\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.isNumber(3.14)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.all(", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesAppendForInTryCatchAndNullConditional()
    {
        var source = """
            var a = [];
            a.append(1);
            foreach (var x in [1, 2]) {
                println(x);
            }
            try {
                throw "err";
            } catch (e if e == "err") {
                println("caught");
            }
            var d = null;
            var v = d?.missing;
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.arrayAppend(a, 1)", js, StringComparison.Ordinal);
        Assert.Contains("for (const x of [1, 2])", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.throwMalda(\"err\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.unwrapMaldaException(__maldaException)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.getMemberNullSafe(", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsDomCallsAndTruthinessEqualityHelpers()
    {
        var source = """
            var a = 1;
            var b = 2;
            dom.setText("#title", "hello");
            var same = a == b;
            var diff = a != b;
            var both = a && b;
            if (same) {
                println("same");
            }
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.dom.setText(\"#title\", \"hello\")", js, StringComparison.Ordinal);
        Assert.Contains("let same = mlRuntime.equals(a, b);", js, StringComparison.Ordinal);
        Assert.Contains("let diff = (!mlRuntime.equals(a, b));", js, StringComparison.Ordinal);
        Assert.Contains("let both = (mlRuntime.isTruthy(a) && mlRuntime.isTruthy(b));", js, StringComparison.Ordinal);
        Assert.Contains("if (mlRuntime.isTruthy(same))", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_UsesFloatCoercion_ForNumericOperators()
    {
        var source = """
            var a = 5;
            var b = 2;
            var c = a / b;
            var d = a * 0.5;
            var e = a - 0.25;
            var f = a % b;
            var g = -d;
            var lt = a < 2.5;
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let c = (mlRuntime.coerceToFloat(a) / mlRuntime.coerceToFloat(b));", js, StringComparison.Ordinal);
        Assert.Contains("let d = (mlRuntime.coerceToFloat(a) * mlRuntime.coerceToFloat(0.5));", js, StringComparison.Ordinal);
        Assert.Contains("let e = (mlRuntime.coerceToFloat(a) - mlRuntime.coerceToFloat(0.25));", js, StringComparison.Ordinal);
        Assert.Contains("let f = (mlRuntime.coerceToFloat(a) % mlRuntime.coerceToFloat(b));", js, StringComparison.Ordinal);
        Assert.Contains("let g = (-mlRuntime.coerceToFloat(d));", js, StringComparison.Ordinal);
        Assert.Contains("let lt = (mlRuntime.coerceToFloat(a) < mlRuntime.coerceToFloat(2.5));", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsGameMemberAccessAndCalls_ToMlRuntimeGame()
    {
        var source = """
            var clearFn = game.clear;
            game.clear("black");
            game.fillRect(10, 20, 30, 40, "red");
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let clearFn = mlRuntime.game.clear;", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.clear(\"black\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.fillRect(10, 20, 30, 40, \"red\")", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsKeyGameApis_ToMlRuntimeGame()
    {
        var source = """
            game.createCanvas(640, 360, "#app");
            game.fillRect(10, 20, 30, 40, "#33cc66");
            game.start(update, render);
            var leftPressed = game.isKeyDown("arrowleft");
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.game.createCanvas(640, 360, \"#app\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.fillRect(10, 20, 30, 40, \"#33cc66\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.start(update, render)", js, StringComparison.Ordinal);
        Assert.Contains("let leftPressed = mlRuntime.game.isKeyDown(\"arrowleft\");", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsGameInputEdgeAndGamepadApis_ToMlRuntimeGame()
    {
        var source = """
            var pressed = game.wasKeyPressed(" ");
            var released = game.wasKeyReleased("arrowup");
            var touches = game.getTouches();
            var padOn = game.isGamepadConnected();
            var padOn1 = game.isGamepadConnected(1);
            var axis = game.getGamepadAxis(0, 0);
            var btn = game.isGamepadButtonDown(0, 0);
            var btnPress = game.wasGamepadButtonPressed(0, 1);
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.game.wasKeyPressed(\" \")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.wasKeyReleased(\"arrowup\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.getTouches()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.isGamepadConnected()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.isGamepadConnected(1)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.getGamepadAxis(0, 0)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.isGamepadButtonDown(0, 0)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.wasGamepadButtonPressed(0, 1)", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsGameCollisionApis_ToMlRuntimeGame()
    {
        var source = """
            var hit = game.overlapRect(0, 0, 10, 10, 5, 5, 10, 10);
            var circles = game.overlapCircle(0, 0, 5, 8, 0, 5);
            var inBox = game.pointInRect(2, 3, 0, 0, 10, 10);
            var inDisk = game.pointInCircle(1, 1, 0, 0, 5);
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.game.overlapRect(0, 0, 10, 10, 5, 5, 10, 10)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.overlapCircle(0, 0, 5, 8, 0, 5)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.pointInRect(2, 3, 0, 0, 10, 10)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.pointInCircle(1, 1, 0, 0, 5)", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsGamePixelBufferApis_ToMlRuntimeGame()
    {
        var source = """
            var blitFn = game.blitPixels;
            game.createPixelBuffer();
            game.createPixelBuffer(320, 180);
            game.setPixel(2, 3, 10, 20, 30);
            game.setPixel(2, 3, 10, 20, 30, 255);
            game.blitPixels();
            game.blitPixels(pixels);
            game.blitPixels(pixels, 4, 5);
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let blitFn = mlRuntime.game.blitPixels;", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.createPixelBuffer()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.createPixelBuffer(320, 180)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.setPixel(2, 3, 10, 20, 30)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.setPixel(2, 3, 10, 20, 30, 255)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.blitPixels()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.blitPixels(pixels)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.blitPixels(pixels, 4, 5)", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsGameSpriteAndCameraApis_ToMlRuntimeGame()
    {
        var source = """
            var loadFn = game.loadImage;
            var tiles = game.loadImage("assets/sprite_tiles.png");
            var ready = game.imageIsReady(tiles);
            game.drawImage(tiles, 8, 16);
            game.drawImage(tiles, 8, 16, 32, 32);
            game.drawImageRect(tiles, 0, 0, 16, 16, 40, 80, 64, 64);
            game.drawLine(0, 0, 10, 10, "#ffffff", 2);
            game.strokeRect(1, 2, 3, 4, "#88ffcc", 3);
            game.setAlpha(0.5);
            game.setCamera(12, 24);
            var camX = game.getCameraX();
            var camY = game.getCameraY();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let loadFn = mlRuntime.game.loadImage;", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.loadImage(\"assets/sprite_tiles.png\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.imageIsReady(tiles)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.drawImage(tiles, 8, 16)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.drawImage(tiles, 8, 16, 32, 32)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.drawImageRect(tiles, 0, 0, 16, 16, 40, 80, 64, 64)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.drawLine(0, 0, 10, 10, \"#ffffff\", 2)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.strokeRect(1, 2, 3, 4, \"#88ffcc\", 3)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.setAlpha(0.5)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.setCamera(12, 24)", js, StringComparison.Ordinal);
        Assert.Contains("let camX = mlRuntime.game.getCameraX();", js, StringComparison.Ordinal);
        Assert.Contains("let camY = mlRuntime.game.getCameraY();", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_PlatformExample_EmitsKitCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "malda_platform.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.game.loadImage(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.drawImageRect(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.setCamera(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.wasKeyPressed(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.overlapRect(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioPlaySample(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.startFixed(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.save(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.load(", js, StringComparison.Ordinal);
        Assert.Contains("platform_high", js, StringComparison.Ordinal);
        Assert.Contains("assets/sprite_tiles.png", js, StringComparison.Ordinal);
        Assert.Contains("assets/beep_hi.wav", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MaldadashExample_EmitsGameLoopCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "maldadash.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.game.createCanvas(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.start(updateGame, renderGame)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.isKeyDown(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.fillRect(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.str.substring(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_SpriteSmokeExample_EmitsImageAndCameraCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "game_sprite_smoke.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.game.loadImage(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.imageIsReady(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.drawImageRect(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.drawImage(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.setCamera(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.drawLine(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.strokeRect(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.setAlpha(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_InputSmokeExample_EmitsGameInputCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "game_input_smoke.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.game.wasKeyPressed(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.wasKeyReleased(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.getTouches()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.isGamepadConnected(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.getGamepadAxis(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.wasGamepadButtonPressed(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_CollisionSmokeExample_EmitsOverlapCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "game_collision_smoke.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.game.overlapRect(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.overlapCircle(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.pointInRect(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.pointInCircle(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_AudioSampleSmokeExample_EmitsSampleCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "game_audio_sample_smoke.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.game.audioPlaySample(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioStopSample(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioPlayPattern(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioInit()", js, StringComparison.Ordinal);
        Assert.Contains("assets/beep_hi.wav", js, StringComparison.Ordinal);
        Assert.Contains("assets/beep_lo.wav", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_FixedSaveSmokeExample_EmitsStartFixedAndSave()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "game_fixed_save_smoke.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.game.startFixed(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.save(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.load(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.removeSave(", js, StringComparison.Ordinal);
        Assert.Contains("fixed_smoke_high", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_RayTracerExample_EmitsPixelBlitCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "ray_tracer.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.game.createPixelBuffer()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.setPixel(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.blitPixels()", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.", js, StringComparison.Ordinal);
    }

    [Fact]
    public void GameRuntime_SetPixelAndBlitPixels_WritesImageData()
    {
        Assert.True(Tier0JavaScriptRunner.IsAvailable(out var reason), "JavaScript backend unavailable: " + reason);

        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = CreateTempDirectory("malda_js_pixel_blit_");
        try
        {
            var scriptPath = Path.Combine(root, "pixel-blit-test.js");
            File.WriteAllText(scriptPath, """
class ImageData {
  constructor(width, height) {
    this.width = width;
    this.height = height;
    this.data = new Uint8ClampedArray(width * height * 4);
  }
}
globalThis.ImageData = ImageData;

function makeCanvas() {
  const ctx = {
    lastPut: null,
    fillStyle: "#000",
    font: "",
    fillRect() {},
    clearRect() {},
    beginPath() {},
    arc() {},
    fill() {},
    fillText() {},
    putImageData(image, x, y) {
      this.lastPut = {
        width: image.width,
        height: image.height,
        x,
        y,
        data: Uint8ClampedArray.from(image.data)
      };
    }
  };
  return {
    width: 0,
    height: 0,
    style: {},
    parentNode: null,
    _ctx: ctx,
    getContext() { return this._ctx; },
    getBoundingClientRect() {
      return { left: 0, top: 0, width: this.width, height: this.height };
    }
  };
}

const mount = {
  children: [],
  appendChild(el) {
    this.children.push(el);
    el.parentNode = this;
    return el;
  },
  removeChild(el) {
    this.children = this.children.filter((child) => child !== el);
    el.parentNode = null;
    return el;
  }
};

globalThis.document = {
  body: mount,
  querySelector() { return mount; },
  createElement(tag) {
    if (tag === "canvas") return makeCanvas();
    return { style: {} };
  }
};
globalThis.window = {
  addEventListener() {},
  removeEventListener() {},
  requestAnimationFrame() { return 1; },
  cancelAnimationFrame() {}
};

require(process.argv[2]);
const game = globalThis.mlRuntime.game;
game.createCanvas(2, 1, "#app");
game.createPixelBuffer();
game.setPixel(0, 0, 255, 128, 0);
game.setPixel(1, 0, 0, 64, 255, 200);
game.setPixel(-3, 0, 9, 9, 9);
game.blitPixels();
const first = mount.children[0]._ctx.lastPut;
if (!first || first.width !== 2 || first.height !== 1 || first.x !== 0 || first.y !== 0) {
  throw new Error("unexpected blit destination");
}
if (first.data[0] !== 255 || first.data[1] !== 128 || first.data[2] !== 0 || first.data[3] !== 255) {
  throw new Error("setPixel RGB failed: " + Array.from(first.data.slice(0, 4)).join(","));
}
if (first.data[4] !== 0 || first.data[5] !== 64 || first.data[6] !== 255 || first.data[7] !== 200) {
  throw new Error("setPixel RGBA failed: " + Array.from(first.data.slice(4, 8)).join(","));
}

game.blitPixels([10, 20, 30, 40, 50, 60]);
const rgb = mount.children[0]._ctx.lastPut;
if (rgb.data[0] !== 10 || rgb.data[1] !== 20 || rgb.data[2] !== 30 || rgb.data[3] !== 255) {
  throw new Error("packed RGB blit failed");
}
if (rgb.data[4] !== 40 || rgb.data[5] !== 50 || rgb.data[6] !== 60 || rgb.data[7] !== 255) {
  throw new Error("packed RGB second pixel failed");
}

game.blitPixels([1, 2, 3, 4, 5, 6, 7, 8], 1, 2);
const rgba = mount.children[0]._ctx.lastPut;
if (rgba.x !== 1 || rgba.y !== 2 || rgba.data[7] !== 8) {
  throw new Error("packed RGBA blit failed");
}

process.stdout.write("ok\n");
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for pixel blit runtime test.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            Assert.True(process.ExitCode == 0, $"pixel blit runtime test failed ({process.ExitCode}). stderr: {stderr}");
            Assert.Equal("ok", stdout.Trim());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void GameRuntime_SpritesCameraAndDrawExtras_ApplyWorldOffset()
    {
        Assert.True(Tier0JavaScriptRunner.IsAvailable(out var reason), "JavaScript backend unavailable: " + reason);

        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = CreateTempDirectory("malda_js_sprite_camera_");
        try
        {
            var scriptPath = Path.Combine(root, "sprite-camera-test.js");
            File.WriteAllText(scriptPath, """
class ImageData {
  constructor(width, height) {
    this.width = width;
    this.height = height;
    this.data = new Uint8ClampedArray(width * height * 4);
  }
}
globalThis.ImageData = ImageData;

class FakeImage {
  constructor() {
    this.width = 0;
    this.height = 0;
    this.naturalWidth = 0;
    this.naturalHeight = 0;
    this.onload = null;
    this.onerror = null;
    this._src = "";
  }
  set src(value) {
    this._src = String(value || "");
    if (!this._src || this._src.indexOf("missing") >= 0) {
      if (typeof this.onerror === "function") this.onerror();
      return;
    }
    this.width = 16;
    this.height = 16;
    this.naturalWidth = 16;
    this.naturalHeight = 16;
    if (typeof this.onload === "function") this.onload();
  }
  get src() { return this._src; }
}
globalThis.Image = FakeImage;

function makeCanvas() {
  const ctx = {
    lastPut: null,
    fillRects: [],
    strokeRects: [],
    lines: [],
    images: [],
    fillStyle: "#000",
    strokeStyle: "#000",
    lineWidth: 1,
    font: "",
    globalAlpha: 1,
    fillRect(x, y, w, h) { this.fillRects.push({ x, y, w, h, alpha: this.globalAlpha, fillStyle: this.fillStyle }); },
    strokeRect(x, y, w, h) { this.strokeRects.push({ x, y, w, h, alpha: this.globalAlpha, strokeStyle: this.strokeStyle, lineWidth: this.lineWidth }); },
    clearRect() {},
    beginPath() { this._path = []; },
    moveTo(x, y) { this._path = [{ op: "move", x, y }]; },
    lineTo(x, y) { this._path.push({ op: "line", x, y }); },
    stroke() { this.lines.push({ path: this._path.slice(), strokeStyle: this.strokeStyle, lineWidth: this.lineWidth, alpha: this.globalAlpha }); },
    arc() {},
    fill() {},
    fillText() {},
    drawImage() { this.images.push({ args: Array.from(arguments), x: arguments[1], y: arguments[2], alpha: this.globalAlpha }); },
    putImageData(image, x, y) {
      this.lastPut = { width: image.width, height: image.height, x, y };
    }
  };
  return {
    width: 0,
    height: 0,
    style: {},
    parentNode: null,
    _ctx: ctx,
    getContext() { return this._ctx; },
    getBoundingClientRect() {
      return { left: 0, top: 0, width: this.width, height: this.height };
    }
  };
}

const mount = {
  children: [],
  appendChild(el) {
    this.children.push(el);
    el.parentNode = this;
    return el;
  },
  removeChild(el) {
    this.children = this.children.filter((child) => child !== el);
    el.parentNode = null;
    return el;
  }
};

globalThis.document = {
  body: mount,
  querySelector() { return mount; },
  createElement(tag) {
    if (tag === "canvas") return makeCanvas();
    return { style: {} };
  }
};
globalThis.window = {
  addEventListener() {},
  removeEventListener() {},
  requestAnimationFrame() { return 1; },
  cancelAnimationFrame() {}
};

require(process.argv[2]);
const game = globalThis.mlRuntime.game;
game.createCanvas(64, 32, "#app");
const ctx = mount.children[0]._ctx;

const empty = game.loadImage("");
if (game.imageIsReady(empty)) throw new Error("empty url should stay unready");
game.drawImage(empty, 0, 0);
if (ctx.images.length !== 0) throw new Error("unready drawImage should no-op");

const missing = game.loadImage("missing.png");
if (game.imageIsReady(missing)) throw new Error("missing url should stay unready");

const tiles = game.loadImage("ok.png");
if (!game.imageIsReady(tiles)) throw new Error("ok.png should be ready");
const again = game.loadImage("ok.png");
if (again !== tiles) throw new Error("same url should return cached handle");

game.setCamera(10, 20);
if (game.getCameraX() !== 10 || game.getCameraY() !== 20) throw new Error("camera getters");
game.fillRect(5, 6, 8, 9, "#ff0000");
const rect = ctx.fillRects[ctx.fillRects.length - 1];
if (rect.x !== -5 || rect.y !== -14 || rect.w !== 8 || rect.h !== 9) {
  throw new Error("camera did not offset fillRect: " + JSON.stringify(rect));
}

game.drawLine(0, 0, 4, 8, "#ffffff", 2);
const line = ctx.lines[ctx.lines.length - 1];
if (!line || line.path[0].x !== -10 || line.path[0].y !== -20 || line.path[1].x !== -6 || line.path[1].y !== -12) {
  throw new Error("camera did not offset drawLine: " + JSON.stringify(line));
}

game.strokeRect(1, 2, 3, 4, "#88ffcc", 3);
const stroke = ctx.strokeRects[ctx.strokeRects.length - 1];
if (stroke.x !== -9 || stroke.y !== -18 || stroke.lineWidth !== 3) {
  throw new Error("camera did not offset strokeRect: " + JSON.stringify(stroke));
}

game.setAlpha(0.4);
game.drawImage(tiles, 8, 16, 32, 32);
const img = ctx.images[ctx.images.length - 1];
if (img.args.length !== 5 || img.args[1] !== -2 || img.args[2] !== -4 || img.args[3] !== 32 || img.args[4] !== 32 || img.alpha !== 0.4) {
  throw new Error("drawImage camera/alpha failed: " + JSON.stringify(img));
}

game.drawImageRect(tiles, 0, 0, 16, 16, 40, 80);
const atlas = ctx.images[ctx.images.length - 1];
if (atlas.args.length !== 9 || atlas.args[1] !== 0 || atlas.args[5] !== 30 || atlas.args[6] !== 60 || atlas.args[7] !== 16 || atlas.args[8] !== 16) {
  throw new Error("drawImageRect failed: " + JSON.stringify(atlas));
}

game.createPixelBuffer();
game.setPixel(0, 0, 1, 2, 3);
game.blitPixels();
if (!ctx.lastPut || ctx.lastPut.x !== 0 || ctx.lastPut.y !== 0) {
  throw new Error("camera must not offset blitPixels");
}

game.createCanvas(64, 32, "#app");
if (game.getCameraX() !== 0 || game.getCameraY() !== 0) throw new Error("createCanvas should reset camera");

process.stdout.write("ok\n");
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for sprite/camera runtime test.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            Assert.True(process.ExitCode == 0, $"sprite/camera runtime test failed ({process.ExitCode}). stderr: {stderr}");
            Assert.Equal("ok", stdout.Trim());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void GameRuntime_KeyEdgesTouchesAndGamepad_SnapshotOnUpdate()
    {
        Assert.True(Tier0JavaScriptRunner.IsAvailable(out var reason), "JavaScript backend unavailable: " + reason);

        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = CreateTempDirectory("malda_js_input_edges_");
        try
        {
            var scriptPath = Path.Combine(root, "input-edges-test.js");
            File.WriteAllText(scriptPath, """
function makeCanvas() {
  const ctx = {
    fillStyle: "#000",
    strokeStyle: "#000",
    lineWidth: 1,
    font: "",
    globalAlpha: 1,
    fillRect() {},
    strokeRect() {},
    clearRect() {},
    beginPath() {},
    moveTo() {},
    lineTo() {},
    stroke() {},
    arc() {},
    fill() {},
    fillText() {},
    drawImage() {},
    putImageData() {}
  };
  return {
    width: 0,
    height: 0,
    style: {},
    parentNode: null,
    _ctx: ctx,
    getContext() { return this._ctx; },
    getBoundingClientRect() {
      return { left: 0, top: 0, width: this.width, height: this.height };
    }
  };
}

const mount = {
  children: [],
  appendChild(el) {
    this.children.push(el);
    el.parentNode = this;
    return el;
  },
  removeChild(el) {
    this.children = this.children.filter((child) => child !== el);
    el.parentNode = null;
    return el;
  }
};

const listeners = {};
let rafQueue = [];
let pads = [];

globalThis.document = {
  body: mount,
  querySelector() { return mount; },
  createElement(tag) {
    if (tag === "canvas") return makeCanvas();
    return { style: {} };
  }
};
globalThis.window = {
  addEventListener(type, fn) {
    listeners[type] = listeners[type] || [];
    listeners[type].push(fn);
  },
  removeEventListener() {},
  requestAnimationFrame(cb) {
    rafQueue.push(cb);
    return rafQueue.length;
  },
  cancelAnimationFrame() {}
};
Object.defineProperty(globalThis, "navigator", {
  configurable: true,
  enumerable: true,
  writable: true,
  value: {
    getGamepads() { return pads; }
  }
});

function fire(type, event) {
  (listeners[type] || []).forEach((fn) => fn(event));
}
function runFrame(ts) {
  const cb = rafQueue.shift();
  if (!cb) throw new Error("no rAF callback");
  cb(ts);
}

require(process.argv[2]);
const game = globalThis.mlRuntime.game;
game.createCanvas(64, 32, "#app");

const log = [];
game.start(function update() {
  log.push({
    phase: "update",
    spacePressed: game.wasKeyPressed(" "),
    spaceReleased: game.wasKeyReleased(" "),
    spaceDown: game.isKeyDown(" "),
    aPressed: game.wasKeyPressed("a"),
    aReleased: game.wasKeyReleased("a"),
    padOn: game.isGamepadConnected(),
    axis: game.getGamepadAxis(0, 0),
    btnDown: game.isGamepadButtonDown(0, 0),
    btnPressed: game.wasGamepadButtonPressed(0, 0),
    touches: game.getTouches()
  });
}, function render() {
  log.push({
    phase: "render",
    spacePressed: game.wasKeyPressed(" "),
    spaceReleased: game.wasKeyReleased(" "),
    aPressed: game.wasKeyPressed("a"),
    aReleased: game.wasKeyReleased("a"),
    btnPressed: game.wasGamepadButtonPressed(0, 0)
  });
});

fire("keydown", { key: " " });
runFrame(16);
if (!log[0] || log[0].phase !== "update" || log[0].spacePressed !== true || log[0].spaceDown !== true) {
  throw new Error("first update should see wasKeyPressed: " + JSON.stringify(log[0]));
}
if (!log[1] || log[1].phase !== "render" || log[1].spacePressed !== false || log[1].spaceReleased !== false) {
  throw new Error("render should not see key edges: " + JSON.stringify(log[1]));
}

runFrame(32);
if (!log[2] || log[2].spacePressed !== false || log[2].spaceDown !== true) {
  throw new Error("held key must not retrigger press: " + JSON.stringify(log[2]));
}

fire("keyup", { key: " " });
runFrame(48);
if (!log[4] || log[4].spaceReleased !== true || log[4].spaceDown !== false) {
  throw new Error("keyup should edge on next update: " + JSON.stringify(log[4]));
}
if (!log[5] || log[5].phase !== "render" || log[5].spaceReleased !== false) {
  throw new Error("render should not see key release: " + JSON.stringify(log[5]));
}

fire("keydown", { key: "a" });
fire("keyup", { key: "a" });
runFrame(56);
const tap = log[log.length - 2];
const tapRender = log[log.length - 1];
if (!tap || tap.aPressed !== true || tap.aReleased !== true) {
  throw new Error("same-frame tap should press and release: " + JSON.stringify(tap));
}
if (!tapRender || tapRender.aPressed !== false || tapRender.aReleased !== false) {
  throw new Error("render should not see same-frame tap edges: " + JSON.stringify(tapRender));
}

fire("touchstart", {
  cancelable: true,
  preventDefault() {},
  touches: [{ identifier: 7, clientX: 12, clientY: 24 }],
  changedTouches: [{ identifier: 7, clientX: 12, clientY: 24 }]
});
if (game.getTouches().length !== 1 || game.getTouches()[0].id !== 7 || game.getTouches()[0].x !== 12 || game.isMouseDown(0) !== true) {
  throw new Error("touchstart should track canvas points and alias mouse: " + JSON.stringify(game.getTouches()));
}

fire("touchend", {
  touches: [],
  changedTouches: [{ identifier: 7, clientX: 12, clientY: 24 }]
});
if (game.getTouches().length !== 0 || game.isMouseDown(0) !== false) {
  throw new Error("touchend should clear touches and mouse alias");
}

if (game.isGamepadConnected() !== false || game.getGamepadAxis(0, 0) !== 0) {
  throw new Error("missing gamepad should be disconnected with zero axes");
}

pads = [{
  axes: [0.5, -0.25],
  buttons: [{ pressed: true }, { pressed: false }]
}];
runFrame(64);
const padFrame = log[log.length - 2];
if (!padFrame || padFrame.padOn !== true || padFrame.axis !== 0.5 || padFrame.btnDown !== true || padFrame.btnPressed !== true) {
  throw new Error("gamepad connect/press failed: " + JSON.stringify(padFrame));
}

runFrame(80);
const padHeld = log[log.length - 2];
if (!padHeld || padHeld.btnPressed !== false || padHeld.btnDown !== true) {
  throw new Error("held gamepad button must not retrigger: " + JSON.stringify(padHeld));
}

fire("keydown", { key: "z" });
game.stop();
if (game.isKeyDown("z") !== false || game.wasKeyPressed("z") !== false || game.wasGamepadButtonPressed(0, 0) !== false) {
  throw new Error("game.stop should clear keys and button edges");
}

process.stdout.write("ok\n");
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for input-edges runtime test.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            Assert.True(process.ExitCode == 0, $"input-edges runtime test failed ({process.ExitCode}). stderr: {stderr}");
            Assert.Equal("ok", stdout.Trim());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void GameRuntime_OverlapHelpers_InclusiveEdgesAndZeroSize()
    {
        Assert.True(Tier0JavaScriptRunner.IsAvailable(out var reason), "JavaScript backend unavailable: " + reason);

        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = CreateTempDirectory("malda_js_collision_");
        try
        {
            var scriptPath = Path.Combine(root, "collision-test.js");
            File.WriteAllText(scriptPath, """
require(process.argv[2]);
const game = globalThis.mlRuntime.game;

function assertEq(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(label + ": expected " + expected + ", got " + actual);
  }
}

assertEq(game.overlapRect(0, 0, 10, 10, 0, 0, 10, 10), true, "identical rects");
assertEq(game.overlapRect(0, 0, 10, 10, 5, 5, 10, 10), true, "interior overlap");
assertEq(game.overlapRect(0, 0, 10, 10, 10, 0, 10, 10), true, "touching vertical edge");
assertEq(game.overlapRect(0, 0, 10, 10, 0, 10, 10, 10), true, "touching horizontal edge");
assertEq(game.overlapRect(0, 0, 10, 10, 11, 0, 10, 10), false, "separated rects");
assertEq(game.overlapRect(0, 0, 0, 10, 0, 0, 10, 10), false, "zero width");
assertEq(game.overlapRect(0, 0, 10, 0, 0, 0, 10, 10), false, "zero height");
assertEq(game.overlapRect(0, 0, -4, 10, 0, 0, 10, 10), false, "negative width");
assertEq(game.overlapRect(0, 0, 10, 10, 0, 0, 10, -1), false, "negative other height");

assertEq(game.overlapCircle(0, 0, 5, 10, 0, 5), true, "circles touching");
assertEq(game.overlapCircle(0, 0, 5, 0, 0, 5), true, "concentric circles");
assertEq(game.overlapCircle(0, 0, 5, 11, 0, 5), false, "circles separated");
assertEq(game.overlapCircle(0, 0, 0, 0, 0, 5), false, "zero radius");
assertEq(game.overlapCircle(0, 0, -2, 0, 0, 5), false, "negative radius");

assertEq(game.pointInRect(0, 0, 0, 0, 10, 10), true, "rect origin corner");
assertEq(game.pointInRect(10, 10, 0, 0, 10, 10), true, "rect far corner");
assertEq(game.pointInRect(10.1, 5, 0, 0, 10, 10), false, "outside rect");
assertEq(game.pointInRect(0, 0, 0, 0, 0, 10), false, "point in zero-width rect");
assertEq(game.pointInRect(5, 5, 0, 0, 10, -3), false, "point in negative-height rect");

assertEq(game.pointInCircle(5, 0, 0, 0, 5), true, "point on circumference");
assertEq(game.pointInCircle(0, 0, 0, 0, 5), true, "circle center");
assertEq(game.pointInCircle(5.1, 0, 0, 0, 5), false, "outside circle");
assertEq(game.pointInCircle(0, 0, 0, 0, 0), false, "zero-radius circle");

process.stdout.write("ok\n");
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for collision runtime test.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            Assert.True(process.ExitCode == 0, $"collision runtime test failed ({process.ExitCode}). stderr: {stderr}");
            Assert.Equal("ok", stdout.Trim());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void GameRuntime_AudioPlaySample_OverlapsWithoutStoppingTrack()
    {
        Assert.True(Tier0JavaScriptRunner.IsAvailable(out var reason), "JavaScript backend unavailable: " + reason);

        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = CreateTempDirectory("malda_js_audio_sample_");
        try
        {
            var scriptPath = Path.Combine(root, "audio-sample-test.js");
            File.WriteAllText(scriptPath, """
globalThis.document = {
  body: {},
  querySelector() { return { appendChild() {}, removeChild() {} }; },
  createElement() { return { style: {} }; }
};

function FakeGain() {
  this.gain = {
    value: 1,
    lastValue: 1,
    setValueAtTime(v) { this.lastValue = v; this.value = v; },
    cancelScheduledValues() {},
    linearRampToValueAtTime() {}
  };
}
FakeGain.prototype.connect = function () {};
FakeGain.prototype.disconnect = function () {};

function FakeSource(kind) {
  this.kind = kind;
  this.buffer = null;
  this.loop = false;
  this.type = "sine";
  this.frequency = { setValueAtTime() {} };
  this.started = false;
  this.stopped = false;
  this.onended = null;
}
FakeSource.prototype.connect = function () {};
FakeSource.prototype.disconnect = function () {};
FakeSource.prototype.start = function () { this.started = true; };
FakeSource.prototype.stop = function (when) {
  if (arguments.length > 0) {
    this.scheduledStop = when;
    return;
  }
  this.stopped = true;
  if (typeof this.onended === "function") this.onended();
};

function FakeAudioContext() {
  this.state = "running";
  this.currentTime = 0;
  this.destination = {};
  this.bufferSources = [];
  this.oscillators = [];
  this.gains = [];
  this.decodeCalls = 0;
  FakeAudioContext.last = this;
}
FakeAudioContext.prototype.createGain = function () {
  const gain = new FakeGain();
  this.gains.push(gain);
  return gain;
};
FakeAudioContext.prototype.createBufferSource = function () {
  const source = new FakeSource("buffer");
  this.bufferSources.push(source);
  return source;
};
FakeAudioContext.prototype.createOscillator = function () {
  const source = new FakeSource("osc");
  this.oscillators.push(source);
  return source;
};
FakeAudioContext.prototype.decodeAudioData = function (bytes) {
  this.decodeCalls += 1;
  if (String(this._decodeUrl || "").indexOf("bad") >= 0) {
    return Promise.reject(new Error("decode failed"));
  }
  return Promise.resolve({ duration: 0.12, length: 8, sampleRate: 22050, numberOfChannels: 1 });
};
FakeAudioContext.prototype.resume = function () {
  this.state = "running";
  return Promise.resolve();
};

let lastTrack = null;
function FakeHtmlAudio(src) {
  this.src = src;
  this.loop = false;
  this.volume = 1;
  this.paused = true;
  this.currentTime = 0;
  this.readyState = 4;
  this.pauseCalls = 0;
  lastTrack = this;
}
FakeHtmlAudio.prototype.addEventListener = function (type, fn) {
  if (type === "canplay" && typeof fn === "function") fn();
};
FakeHtmlAudio.prototype.play = function () {
  this.paused = false;
  return Promise.resolve();
};
FakeHtmlAudio.prototype.pause = function () {
  this.pauseCalls += 1;
  this.paused = true;
};
FakeHtmlAudio.prototype.load = function () {};

const fetchCounts = {};
let resolveSlow = null;
const slowFetch = new Promise((resolve) => { resolveSlow = resolve; });
let resolveHeld = null;
const heldFetch = new Promise((resolve) => { resolveHeld = resolve; });

function okResponse() {
  return { ok: true, arrayBuffer() { return Promise.resolve(new ArrayBuffer(8)); } };
}

globalThis.fetch = function (url) {
  const key = String(url);
  fetchCounts[key] = (fetchCounts[key] || 0) + 1;
  if (FakeAudioContext.last) FakeAudioContext.last._decodeUrl = key;
  if (key === "slow.wav") return slowFetch.then(() => okResponse());
  if (key === "held.wav") return heldFetch.then(() => okResponse());
  if (key.indexOf("missing") >= 0) {
    return Promise.resolve({ ok: false, arrayBuffer() { return Promise.resolve(new ArrayBuffer(0)); } });
  }
  return Promise.resolve(okResponse());
};

globalThis.Audio = FakeHtmlAudio;
globalThis.window = {
  addEventListener() {},
  removeEventListener() {},
  requestAnimationFrame() { return 1; },
  cancelAnimationFrame() {},
  AudioContext: FakeAudioContext,
  fetch: globalThis.fetch
};

require(process.argv[2]);
const game = globalThis.mlRuntime.game;

function flush(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms === undefined ? 0 : ms));
}

function live() {
  return FakeAudioContext.last;
}

function startedBuffers() {
  return live().bufferSources.filter((source) => source.started);
}

(async () => {
  game.audioInit();
  if (!game.audioIsReady() || !live()) throw new Error("audioInit should construct AudioContext");

  game.audioLoadTrack("music.ogg", { "loop": true, "volume": 0.2 });
  game.audioPlayTrack();
  if (!lastTrack || lastTrack.pauseCalls !== 0) throw new Error("track should play without pausing");

  game.audioPlayTone(440, 400, "sine", 0.1);
  const tone = live().oscillators[0];
  if (!tone || !tone.started) throw new Error("tone should start");

  game.audioPlayPattern({
    "tempoBpm": 240,
    "loop": true,
    "tracks": [[{ "atBeats": 0, "noteHz": 220, "durBeats": 0.25, "waveType": "sine", "volume": 0.05 }]]
  });
  await flush(30);
  const oscAfterPattern = live().oscillators.length;
  if (oscAfterPattern < 1) throw new Error("pattern should schedule at least one tone");

  game.audioPlaySample("hi.wav", 0.4);
  game.audioPlaySample("lo.wav", 0.4);
  await flush(20);
  if (startedBuffers().length !== 2) {
    throw new Error("overlapping samples should both start: " + startedBuffers().length);
  }
  if (fetchCounts["hi.wav"] !== 1 || fetchCounts["lo.wav"] !== 1) {
    throw new Error("each URL should fetch once on first play: " + JSON.stringify(fetchCounts));
  }

  game.audioPlaySample("hi.wav", 0.4);
  await flush(20);
  if (fetchCounts["hi.wav"] !== 1) throw new Error("decoded sample must be cached");
  if (startedBuffers().length !== 3) throw new Error("cached URL should play a second overlapping voice");

  game.audioStopSample("hi.wav");
  const hiStopped = live().bufferSources.filter((source, index) => index !== 1).every((source) => source.stopped);
  if (!hiStopped || live().bufferSources[1].stopped) {
    throw new Error("audioStopSample(url) should stop only that URL");
  }
  if (tone.stopped) throw new Error("stopping a sample must not stop a tone");
  if (lastTrack.pauseCalls !== 0) throw new Error("stopping a sample must not pause the track");

  game.audioPlaySample("hi.wav", 2);
  await flush(20);
  const clampedGain = live().gains[live().gains.length - 1];
  if (!clampedGain || clampedGain.gain.lastValue !== 1) {
    throw new Error("sample volume should clamp to 1");
  }

  game.audioPlaySample("lo.wav", { "loop": true, "volume": 0.25 });
  await flush(20);
  const looped = live().bufferSources[live().bufferSources.length - 1];
  if (!looped.loop || looped.stopped) throw new Error("options.loop should keep the buffer source looping");
  const loopGain = live().gains[live().gains.length - 1];
  if (loopGain.gain.lastValue !== 0.25) throw new Error("object second-arg options should set volume");

  const startedBeforeStopAll = startedBuffers().filter((source) => !source.stopped).length;
  if (startedBeforeStopAll < 2) throw new Error("expected live samples before stop-all");
  game.audioStopSample();
  if (startedBuffers().some((source) => !source.stopped)) {
    throw new Error("omitted URL should stop every sample");
  }
  if (tone.stopped) throw new Error("audioStopSample() must not stop tones");
  if (lastTrack.pauseCalls !== 0) throw new Error("audioStopSample() must not stop the track");

  live().currentTime += 1;
  await flush(40);
  if (live().oscillators.length <= oscAfterPattern) {
    throw new Error("audioStopSample must not stop the v1 pattern scheduler");
  }

  const fetchBeforeEmpty = Object.keys(fetchCounts).length;
  game.audioPlaySample("");
  game.audioPlaySample(null);
  if (Object.keys(fetchCounts).length !== fetchBeforeEmpty) throw new Error("empty URL should no-op");

  const startedBeforeMissing = startedBuffers().length;
  game.audioPlaySample("missing.wav");
  await flush(20);
  if (startedBuffers().length !== startedBeforeMissing) throw new Error("failed fetch should not start a voice");

  game.audioPlaySample("bad.wav");
  await flush(20);
  if (startedBuffers().length !== startedBeforeMissing) throw new Error("failed decode should not start a voice");

  const startedBeforeSlow = startedBuffers().length;
  game.audioPlaySample("slow.wav");
  game.audioPlaySample("slow.wav");
  await flush(10);
  if (startedBuffers().length !== startedBeforeSlow) throw new Error("in-flight decode should queue, not start yet");
  resolveSlow();
  await flush(20);
  if (startedBuffers().length !== startedBeforeSlow + 2) {
    throw new Error("queued plays of the same URL should both start after decode");
  }

  const startedBeforeHeld = startedBuffers().length;
  game.audioPlaySample("held.wav");
  game.audioStopSample("held.wav");
  resolveHeld();
  await flush(20);
  if (startedBuffers().length !== startedBeforeHeld) {
    throw new Error("audioStopSample during load should drop pending plays");
  }

  game.audioStopAll();
  process.stdout.write("ok\n");
  process.exit(0);
})().catch((error) => {
  console.error(error && error.stack ? error.stack : error);
  process.exit(1);
});
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for sample-audio runtime test.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            Assert.True(process.ExitCode == 0, $"sample-audio runtime test failed ({process.ExitCode}). stderr: {stderr}");
            Assert.Equal("ok", stdout.Trim());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void GameRuntime_StartFixedAndSaveLoad_CapsCatchUpAndPrefixesKeys()
    {
        Assert.True(Tier0JavaScriptRunner.IsAvailable(out var reason), "JavaScript backend unavailable: " + reason);

        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = CreateTempDirectory("malda_js_fixed_save_");
        try
        {
            var scriptPath = Path.Combine(root, "fixed-save-test.js");
            File.WriteAllText(scriptPath, """
function makeCanvas() {
  return {
    width: 0,
    height: 0,
    style: {},
    parentNode: null,
    getContext() {
      return {
        fillStyle: "#000",
        font: "",
        globalAlpha: 1,
        fillRect() {},
        clearRect() {},
        beginPath() {},
        arc() {},
        fill() {},
        fillText() {}
      };
    },
    getBoundingClientRect() {
      return { left: 0, top: 0, width: this.width, height: this.height };
    }
  };
}

const mount = {
  children: [],
  appendChild(el) {
    this.children.push(el);
    el.parentNode = this;
    return el;
  },
  removeChild(el) {
    this.children = this.children.filter((child) => child !== el);
    el.parentNode = null;
    return el;
  }
};

const listeners = {};
let rafQueue = [];
const memory = {};
const storage = {
  getItem(key) {
    return Object.prototype.hasOwnProperty.call(memory, key) ? memory[key] : null;
  },
  setItem(key, value) {
    if (storage._throwOnSet) throw new Error("quota");
    memory[key] = String(value);
  },
  removeItem(key) { delete memory[key]; }
};

globalThis.document = {
  body: mount,
  querySelector() { return mount; },
  createElement(tag) {
    if (tag === "canvas") return makeCanvas();
    return { style: {} };
  }
};
globalThis.window = {
  addEventListener(type, fn) {
    listeners[type] = listeners[type] || [];
    listeners[type].push(fn);
  },
  removeEventListener() {},
  requestAnimationFrame(cb) {
    rafQueue.push(cb);
    return rafQueue.length;
  },
  cancelAnimationFrame() {},
  localStorage: storage
};

require(process.argv[2]);
const game = globalThis.mlRuntime.game;

function fire(type, event) {
  const list = listeners[type] || [];
  for (let i = 0; i < list.length; i++) list[i](event);
}

function runFrame(ts) {
  const cb = rafQueue.shift();
  if (!cb) throw new Error("no rAF callback at " + ts);
  cb(ts);
}

game.createCanvas(64, 32, "#app");

if (game.load("missing") !== null) throw new Error("missing key should be null");
if (game.save("", 1) !== null) throw new Error("empty key save should no-op");
if (game.load("") !== null) throw new Error("empty key load should be null");

game.save("high", 12);
if (game.load("high") !== 12) throw new Error("load number failed: " + game.load("high"));
if (memory["malda.game.high"] !== "12") throw new Error("save prefix missing: " + JSON.stringify(memory));

game.save("blob", { "name": "p1", "n": 3 });
const blob = game.load("blob");
if (!blob || blob.name !== "p1" || blob.n !== 3) throw new Error("load object failed");

memory["malda.game.broken"] = "{";
if (game.load("broken") !== null) throw new Error("corrupt JSON should be null");

game.removeSave("high");
if (game.load("high") !== null) throw new Error("removeSave should delete");
if (Object.prototype.hasOwnProperty.call(memory, "malda.game.high")) throw new Error("removeSave left prefix key");

storage._throwOnSet = true;
game.save("quota", 1);
storage._throwOnSet = false;
if (game.load("quota") !== null) throw new Error("quota failure should no-op");

const previousStorage = window.localStorage;
delete window.localStorage;
game.save("gone", 1);
if (game.load("gone") !== null) throw new Error("missing localStorage load should be null");
window.localStorage = previousStorage;

const log = [];
game.startFixed(function update(dt) {
  log.push({ phase: "update", dt: dt, space: game.wasKeyPressed(" ") });
}, function render() {
  log.push({ phase: "render", space: game.wasKeyPressed(" ") });
}, 16);

runFrame(0);
if (log.length !== 1 || log[0].phase !== "render") {
  throw new Error("first fixed frame is dt=0 so only render: " + JSON.stringify(log));
}

runFrame(16);
if (log.length !== 3 || log[1].phase !== "update" || log[1].dt !== 16 || log[2].phase !== "render") {
  throw new Error("16ms should step once: " + JSON.stringify(log.slice(1)));
}

fire("keydown", { key: " " });
runFrame(64);
const burst = log.slice(3);
if (burst.length !== 4) throw new Error("48ms should be 3 updates + render: " + JSON.stringify(burst));
if (burst[0].phase !== "update" || burst[0].dt !== 16 || burst[0].space !== true) {
  throw new Error("first catch-up tick should see the press: " + JSON.stringify(burst[0]));
}
if (burst[1].space !== false || burst[2].space !== false) {
  throw new Error("later catch-up ticks must not retrigger press");
}
if (burst[3].phase !== "render" || burst[3].space !== false) {
  throw new Error("render should not see edges");
}

const beforeCap = log.length;
runFrame(1064);
const cap = log.slice(beforeCap);
const capUpdates = cap.filter((row) => row.phase === "update");
if (capUpdates.length !== 5 || cap[cap.length - 1].phase !== "render") {
  throw new Error("spiral cap should be 5 updates + render: " + JSON.stringify(cap));
}

let startWhileFixed = false;
try {
  game.start(function () {}, function () {});
} catch (error) {
  startWhileFixed = String(error.message).indexOf("already running") >= 0;
}
if (!startWhileFixed) throw new Error("start during startFixed should throw already running");

game.stop();
rafQueue.length = 0;

game.start(function () {}, function () {});
let fixedWhileStart = false;
try {
  game.startFixed(function () {}, function () {}, 16);
} catch (error) {
  fixedWhileStart = String(error.message).indexOf("already running") >= 0;
}
if (!fixedWhileStart) throw new Error("startFixed during start should throw already running");
game.stop();
rafQueue.length = 0;

const defaultLog = [];
game.startFixed(function update(dt) { defaultLog.push(dt); }, function () {});
runFrame(0);
runFrame(1000 / 60);
if (defaultLog.length !== 1 || Math.abs(defaultLog[0] - 1000 / 60) > 0.0001) {
  throw new Error("default tickMs should be 1000/60: " + JSON.stringify(defaultLog));
}
game.stop();

process.stdout.write("ok\n");
process.exit(0);
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for fixed-timestep runtime test.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            Assert.True(process.ExitCode == 0, $"fixed-timestep runtime test failed ({process.ExitCode}). stderr: {stderr}");
            Assert.Equal("ok", stdout.Trim());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void JsTranspiler_MapsGameAudioApis_ToMlRuntimeGame()
    {
        var source = """
            var initFn = game.audioInit;
            game.audioInit();
            game.audioSetMasterVolume(0.6);
            game.audioPlayTone(440, 90, "square", 0.2);
            game.audioPlayNoise(80, 0.15);
            game.audioPlayPattern({ "tempoBpm": 120, "loop": false, "tracks": [] });
            game.audioStopPattern();
            game.audioStopAll();
            var ready = game.audioIsReady();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let initFn = mlRuntime.game.audioInit;", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioInit()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioSetMasterVolume(0.6)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioPlayTone(440, 90, \"square\", 0.2)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioPlayNoise(80, 0.15)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioPlayPattern(", js, StringComparison.Ordinal);
        Assert.Contains("\"tempoBpm\"", js, StringComparison.Ordinal);
        Assert.Contains("\"loop\"", js, StringComparison.Ordinal);
        Assert.Contains("\"tracks\"", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioStopPattern()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioStopAll()", js, StringComparison.Ordinal);
        Assert.Contains("let ready = mlRuntime.game.audioIsReady();", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsAdditiveGameTrackApis_ToMlRuntimeGame()
    {
        var source = """
            game.audioLoadTrack("audio/theme.ogg", { "autoplay": true, "loop": true, "volume": 0.4 });
            game.audioSetTrackOptions({ "loop": false, "volume": 0.3 });
            game.audioPlayTrack();
            game.audioStopTrack();
            var trackReady = game.audioTrackIsReady();
            var trackInfo = game.audioGetTrackInfo();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.game.audioLoadTrack(\"audio/theme.ogg\",", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioSetTrackOptions(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioPlayTrack()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioStopTrack()", js, StringComparison.Ordinal);
        Assert.Contains("let trackReady = mlRuntime.game.audioTrackIsReady();", js, StringComparison.Ordinal);
        Assert.Contains("let trackInfo = mlRuntime.game.audioGetTrackInfo();", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsGameSampleAudioApis_ToMlRuntimeGame()
    {
        var source = """
            game.audioPlaySample("assets/beep_hi.wav");
            game.audioPlaySample("assets/beep_lo.wav", 0.5);
            game.audioPlaySample("assets/beep_lo.wav", 0.5, { "loop": true });
            game.audioStopSample("assets/beep_hi.wav");
            game.audioStopSample();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.game.audioPlaySample(\"assets/beep_hi.wav\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioPlaySample(\"assets/beep_lo.wav\", 0.5)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioPlaySample(\"assets/beep_lo.wav\", 0.5,", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioStopSample(\"assets/beep_hi.wav\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.audioStopSample()", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsGameFixedTimestepAndSaveApis_ToMlRuntimeGame()
    {
        var source = """
            game.startFixed(update, render);
            game.startFixed(update, render, 1000 / 60);
            game.save("high", 12);
            var high = game.load("high");
            game.removeSave("high");
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.game.startFixed(update, render)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.startFixed(update, render,", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.coerceToFloat(60)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.save(\"high\", 12)", js, StringComparison.Ordinal);
        Assert.Contains("let high = mlRuntime.game.load(\"high\");", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.game.removeSave(\"high\")", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_DoesNotRewriteNonGameMemberCalls()
    {
        var source = """
            var engine = 0;
            engine.start();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("engine.start()", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.game.start()", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsThreeMemberAccessAndCalls_ToMlRuntimeThree()
    {
        var source = """
            var renderFn = three.render;
            var groupFactory = three.createGroup;
            three.render(renderer, scene, camera);
            three.setPosition(cube, 1, 2, 3);
            three.setScale(cube, 1, 1.25, 1);
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let renderFn = mlRuntime.three.render;", js, StringComparison.Ordinal);
        Assert.Contains("let groupFactory = mlRuntime.three.createGroup;", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.render(renderer, scene, camera)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setPosition(cube, 1, 2, 3)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setScale(cube, 1, 1.25, 1)", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsKeyThreeApis_ToMlRuntimeThree()
    {
        var source = """
            three.createRenderer(800, 500, "#app");
            three.setClearColor(renderer, "#101722");
            three.setRendererSize(renderer, 960, 540);
            three.setCameraAspect(camera, 16 / 9);
            var group = three.createGroup();
            var plane = three.createPlaneGeometry(8, 8);
            var sphere = three.createSphereGeometry(0.5, 24, 16);
            var ambient = three.createAmbientLight("#dde7ff", 0.4);
            three.start(update, render);
            var leftPressed = three.isKeyDown("arrowleft");
            var mouseX = three.getMouseX();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.three.createRenderer(800, 500, \"#app\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setClearColor(renderer, \"#101722\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setRendererSize(renderer, 960, 540)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setCameraAspect(camera, (mlRuntime.coerceToFloat(16) / mlRuntime.coerceToFloat(9)))", js, StringComparison.Ordinal);
        Assert.Contains("let group = mlRuntime.three.createGroup();", js, StringComparison.Ordinal);
        Assert.Contains("let plane = mlRuntime.three.createPlaneGeometry(8, 8);", js, StringComparison.Ordinal);
        Assert.Contains("let sphere = mlRuntime.three.createSphereGeometry(0.5, 24, 16);", js, StringComparison.Ordinal);
        Assert.Contains("let ambient = mlRuntime.three.createAmbientLight(\"#dde7ff\", 0.4);", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.start(update, render)", js, StringComparison.Ordinal);
        Assert.Contains("let leftPressed = mlRuntime.three.isKeyDown(\"arrowleft\");", js, StringComparison.Ordinal);
        Assert.Contains("let mouseX = mlRuntime.three.getMouseX();", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsThreeShaderApis_ToMlRuntimeThree()
    {
        var source = """
            var shaderFn = three.createShaderMaterial;
            var camera = three.createOrthographicCamera(-1, 1, 1, -1, 0, 1);
            var material = three.createShaderMaterial({
                "vertexShader": vert,
                "fragmentShader": frag,
                "uniforms": { "uTime": 0 }
            });
            three.setUniform(material, "uTime", 1.5);
            three.setUniform(material, "uResolution", [960, 540]);
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let shaderFn = mlRuntime.three.createShaderMaterial;", js, StringComparison.Ordinal);
        Assert.Contains(
            "let camera = mlRuntime.three.createOrthographicCamera((-mlRuntime.coerceToFloat(1)), 1, 1, (-mlRuntime.coerceToFloat(1)), 0, 1);",
            js,
            StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.createShaderMaterial(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setUniform(material, \"uTime\", 1.5)", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setUniform(material, \"uResolution\", [960, 540])", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_ShaderRayTracerExample_EmitsShaderCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "three_shader_raytracer.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.three.createShaderMaterial(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setUniform(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.createOrthographicCamera(", js, StringComparison.Ordinal);
        Assert.Contains("varying vec2 vUv", js, StringComparison.Ordinal);
        Assert.Contains("gl_FragColor", js, StringComparison.Ordinal);
        Assert.Contains("float hitSphere(vec3 center, float radius, vec3 origin, vec3 dir)", js, StringComparison.Ordinal);
        Assert.Contains("bool closestHit(", js, StringComparison.Ordinal);
        Assert.Contains("vec3 traceScene(vec3 origin, vec3 dir)", js, StringComparison.Ordinal);
        Assert.Contains("reflect(rd, normal)", js, StringComparison.Ordinal);
        Assert.Contains("refract(rd, n, eta)", js, StringComparison.Ordinal);
        Assert.Contains("const float GLASS_IOR = 1.1", js, StringComparison.Ordinal);
        Assert.DoesNotContain("function hitSphere", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.game.setPixel", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_ShaderPathTunnelExample_EmitsShaderCalls()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Games", "three_shader_path_tunnel.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("mlRuntime.three.createShaderMaterial(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.setUniform(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.three.createOrthographicCamera(", js, StringComparison.Ordinal);
        Assert.Contains("varying vec2 vUv", js, StringComparison.Ordinal);
        Assert.Contains("gl_FragColor", js, StringComparison.Ordinal);
        Assert.Contains("vec3 path(float z)", js, StringComparison.Ordinal);
        Assert.Contains("float xorNoise(vec3 p)", js, StringComparison.Ordinal);
        Assert.Contains("vec3 palette(float t)", js, StringComparison.Ordinal);
        Assert.Contains("const mat3 G =", js, StringComparison.Ordinal);
        Assert.DoesNotContain("function fragmentMain", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.game.setPixel", js, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeRuntime_CreateShaderMaterialAndSetUniform_WrapsVectors()
    {
        Assert.True(Tier0JavaScriptRunner.IsAvailable(out var reason), "JavaScript backend unavailable: " + reason);

        var runtimePath = PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js");
        var root = CreateTempDirectory("malda_js_shader_material_");
        try
        {
            var scriptPath = Path.Combine(root, "shader-material-test.js");
            File.WriteAllText(scriptPath, """
globalThis.document = {
  body: {},
  querySelector() { return { appendChild() {}, removeChild() {} }; },
  createElement() { return { style: {} }; }
};
globalThis.window = {
  addEventListener() {},
  removeEventListener() {},
  requestAnimationFrame() { return 1; },
  cancelAnimationFrame() {},
  devicePixelRatio: 1
};

function Vector2(x, y) {
  this.x = x; this.y = y;
  this.set = function (a, b) { this.x = a; this.y = b; return this; };
}
function Vector3(x, y, z) {
  this.x = x; this.y = y; this.z = z;
  this.set = function (a, b, c) { this.x = a; this.y = b; this.z = c; return this; };
}
function ShaderMaterial(options) {
  this.uniforms = options.uniforms;
  this.vertexShader = options.vertexShader;
  this.fragmentShader = options.fragmentShader;
  this.depthWrite = true;
}
function OrthographicCamera(left, right, top, bottom, near, far) {
  this.left = left; this.right = right; this.top = top; this.bottom = bottom;
  this.near = near; this.far = far;
  this.updateProjectionMatrix = function () {};
}

globalThis.THREE = {
  Vector2,
  Vector3,
  Vector4: function () {},
  Color: function () {},
  ShaderMaterial,
  OrthographicCamera
};

require(process.argv[2]);
const three = globalThis.mlRuntime.three;
const material = three.createShaderMaterial({
  vertexShader: "void main() {}",
  fragmentShader: "void main() {}",
  uniforms: { uTime: 0, uResolution: [320, 180], uCamPos: [1, 2, 3] },
  depthWrite: false
});
if (!material || material.depthWrite !== false) throw new Error("depthWrite flag not applied");
if (material.uniforms.uTime.value !== 0) throw new Error("uTime wrap failed");
if (material.uniforms.uResolution.value.x !== 320 || material.uniforms.uResolution.value.y !== 180) {
  throw new Error("uResolution Vector2 wrap failed");
}
if (material.uniforms.uCamPos.value.z !== 3) throw new Error("uCamPos Vector3 wrap failed");

three.setUniform(material, "uTime", 1.25);
three.setUniform(material, "uResolution", [960, 540]);
three.setUniform(material, "uCamPos", [4, 5, 6]);
if (material.uniforms.uTime.value !== 1.25) throw new Error("setUniform scalar failed");
if (material.uniforms.uResolution.value.x !== 960 || material.uniforms.uResolution.value.y !== 540) {
  throw new Error("setUniform Vector2 failed");
}
if (material.uniforms.uCamPos.value.x !== 4 || material.uniforms.uCamPos.value.z !== 6) {
  throw new Error("setUniform Vector3 failed");
}

const camera = three.createOrthographicCamera(-1, 1, 1, -1, 0, 1);
if (camera.left !== -1 || camera.top !== 1 || camera.far !== 1) throw new Error("ortho camera failed");

process.stdout.write("ok\n");
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } nodePath
                    ? nodePath
                    : "node",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(runtimePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for shader material runtime test.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            Assert.True(process.ExitCode == 0, $"shader material runtime test failed ({process.ExitCode}). stderr: {stderr}");
            Assert.Equal("ok", stdout.Trim());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void JsTranspiler_DoesNotRewriteNonThreeMemberCalls()
    {
        var source = """
            var engine = 0;
            engine.render();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("engine.render()", js, StringComparison.Ordinal);
        Assert.DoesNotContain("mlRuntime.three.render()", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsTernaryExpression_WithMaldaTruthiness()
    {
        var source = """
            var x = 0;
            var result = x ? "yes" : "no";
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let result = (mlRuntime.isTruthy(x) ? \"yes\" : \"no\");", js, StringComparison.Ordinal);
    }

    [Fact]
    public void MaldaHtmlTemplate_MalformedInterpolation_ReturnsDescriptiveError()
    {
        var malformedTemplate = "<div>{{ name </div>";
        var compiler = new Compiler.Compiler();

        var ex = Assert.Throws<Exception>(() =>
            compiler.TranspileToJavaScriptFromSource(malformedTemplate, "broken.malda.html"));

        Assert.Contains("Unclosed interpolation block '{{ ... }}'.", ex.Message, StringComparison.Ordinal);
        Assert.Contains("broken.malda.html", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Context:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_EmitsVariantConstructors_ForTypeDeclarations()
    {
        var source = """
            type Result = Ok(value) | Err(message);
            var r = Ok(42);
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("var Ok = function(value) {", js, StringComparison.Ordinal);
        Assert.Contains("var Err = function(message) {", js, StringComparison.Ordinal);
        Assert.Contains("if (arguments.length !== 1)", js, StringComparison.Ordinal);
        Assert.Contains("return mlRuntime.variant(\"Ok\", Array.from(arguments));", js, StringComparison.Ordinal);
        Assert.Contains("return mlRuntime.variant(\"Err\", Array.from(arguments));", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesMatch_WithVariantPatterns()
    {
        var source = """
            type Result = Ok(value) | Err(message);
            var result = match Ok(7) {
                case Ok(v): "ok:" + v;
                case Err(msg): "err:" + msg;
            };
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.matchPattern({ type: \"Variant\", tag: \"Ok\", payloadPatterns: [{ type: \"Identifier\", name: \"v\" }] }", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.matchPattern({ type: \"Variant\", tag: \"Err\", payloadPatterns: [{ type: \"Identifier\", name: \"msg\" }] }", js, StringComparison.Ordinal);
        Assert.Contains("const v = ", js, StringComparison.Ordinal);
        Assert.Contains("const msg = ", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesMatch_BareConstructorPattern()
    {
        var source = """
            type Result = Ok() | Err(message);
            var result = match Err("ciao") {
                case Ok: "ok: ";
                case Err(msg): "error: " + msg;
            };
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.matchPattern({ type: \"Variant\", tag: \"Ok\", payloadPatterns: [] }", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.matchPattern({ type: \"Variant\", tag: \"Err\", payloadPatterns: [{ type: \"Identifier\", name: \"msg\" }] }", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesMatch_WithGuard()
    {
        var source = """
            var result = match 3 {
                case x if x > 10: "big";
                case x: "small";
            };
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.isTruthy", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.matchPattern", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesMatch_WithArrayRestAndObjectPatterns()
    {
        var source = """
            var value = [1, 2, 3];
            var result = match value {
                case [first, ...rest]: first + length(rest);
                case { type: "A", nested: { x } }: x;
                default: 0;
            };
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("{ type: \"Array\", elements: [{ type: \"Identifier\", name: \"first\" }], rest: { type: \"Rest\", name: \"rest\" } }", js, StringComparison.Ordinal);
        Assert.Contains("{ type: \"Object\", properties: [{ key: \"type\", pattern: { type: \"Literal\", value: \"A\" }, bindingName: null }, { key: \"nested\", pattern: { type: \"Object\", properties: [{ key: \"x\", pattern: null, bindingName: \"x\" }] }, bindingName: null }] }", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesMatch_BlockBodyWithLastExpressionWins()
    {
        var source = """
            var result = match 42 {
                case 42: {
                    println("side");
                    "value";
                }
                default: "other";
            };
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.builtins.println(\"side\");", js, StringComparison.Ordinal);
        Assert.Contains("return \"value\";", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesMatch_WithoutDefault_WithParityErrorMessage()
    {
        var source = """
            var result = match 3 {
                case 1: "one";
                case 2: "two";
            };
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("throw new Error(\"Match expression had no matching case and no default case.\");", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_EmitsVariantConstructor_ArityErrorMessage()
    {
        var source = """
            type Option = Some(value) | None();
            var a = Some(1);
            var b = None();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("Variant constructor Some expects 1 argument(s) but got ", js, StringComparison.Ordinal);
        Assert.Contains("Variant constructor None expects 0 argument(s) but got ", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesActorSpawnSendAndStop_ToActorsRuntime()
    {
        var source = """
            actor Worker {
                on work() {
                    println("working");
                }
            }

            var w = spawn Worker();
            send w.work();
            send w.stop();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("class Worker {", js, StringComparison.Ordinal);
        Assert.Contains("async work()", js, StringComparison.Ordinal);
        Assert.Contains("let w = mlRuntime.actors.spawn(new Worker());", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.actors.send(__target, \"work\");", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.actors.callActorOrVoidStop(w);", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesSendThenTimeoutCallback_ToActorsRuntime()
    {
        var source = """
            actor Echo {
                on ping(value) {
                    reply(value);
                }
            }

            actor Controller {
                on start() {
                    var e = spawn Echo();
                    send e.ping("ok") then (result) {
                        println(result);
                    } timeout 250 catch (error) {
                        println(error);
                    };
                }
            }
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.actors.reply(value)", js, StringComparison.Ordinal);
        Assert.Contains("const __self = mlRuntime.actors.getSelf();", js, StringComparison.Ordinal);
        Assert.Contains("const __timeoutMs = mlRuntime.coerceToInt(250);", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.actors.sendWithCallback(__self, __target, \"ping\", __callback, __timeoutMs, __timeoutErrorHandler, \"ok\");", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_TranspilesReceiveAndSelfExpressions_InActorHandlers()
    {
        var source = """
            actor Looping {
                on handle() {
                    var msg = receive();
                    println(msg);
                    self.stop();
                }
            }
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("let msg = await mlRuntime.actors.receiveAsync();", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.actors.callActorOrVoidStop(mlRuntime.actors.getSelf())", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_EmitsAsyncMainWrapper_WithActorsShutdown()
    {
        var source = """
            println("x");
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("async function __maldaRunMain()", js, StringComparison.Ordinal);
        Assert.Contains("await MaldaApp.main();", js, StringComparison.Ordinal);
        Assert.Contains("await mlRuntime.actors.shutdownAsync();", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_EmitsNestedActorDeclarations_Inline()
    {
        var source = """
            function boot() {
                actor Counter {
                    on inc() {
                        reply(1);
                    }
                }
                var c = spawn Counter();
                send c.inc();
            }
            boot();
            """;
        var compiler = new Compiler.Compiler();

        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("function boot()", js, StringComparison.Ordinal);
        Assert.Contains("class Counter {", js, StringComparison.Ordinal);
        Assert.Contains("let c = mlRuntime.actors.spawn(new Counter());", js, StringComparison.Ordinal);
    }

    [Fact]
    public void FullStackSourceInspector_DetectsFullStackAndExtractsPort()
    {
        var source = """
            @server()
            function api() { return 1; }

            @client()
            function ui() { return 2; }

            @server()
            function boot() {
                var s = new HttpServer(8123);
                s.start();
            }
            """;

        Assert.True(FullStackSourceInspector.IsFullStackSource(source));
        Assert.Equal(8123, FullStackSourceInspector.ExtractHttpPort(source, 8090));
    }

    [Fact]
    public void Compile_ModeFullStack_WritesServerWebAndManifestArtifacts()
    {
        var root = CreateTempDirectory("malda_fullstack_compile_");
        try
        {
            var sourcePath = Path.Combine(root, "app.malda");
            File.WriteAllText(sourcePath, """
                @server()
                function boot() {
                    var s = new HttpServer(8092);
                    s.start();
                }

                @client()
                function ui() {
                    return "ok";
                }
                """);

            var outputDirectory = Path.Combine(root, "dist");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDirectory, CompilationMode.FullStack, includeLLamaSharp: false);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(outputDirectory, result.OutputPath);

            var serverDir = Path.Combine(outputDirectory, "server");
            var webDir = Path.Combine(outputDirectory, "web");
            var manifestPath = Path.Combine(outputDirectory, "manifest.json");

            Assert.True(Directory.Exists(serverDir));
            Assert.True(Directory.Exists(webDir));
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(Path.Combine(webDir, "index.html")));
            Assert.True(File.Exists(Path.Combine(webDir, "malda-js-runtime.js")));
            Assert.True(File.Exists(Path.Combine(webDir, "app.js")));
            Assert.True(File.Exists(Path.Combine(webDir, "app.js.map")));

            var manifest = File.ReadAllText(manifestPath);
            Assert.Contains("\"type\": \"malda-fullstack\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"MALDA_WEB_DIRECTORY\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"port\": 8092", manifest, StringComparison.Ordinal);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void JsTranspiler_MapsInterpolatedStrings_ToCoercedConcatenation()
    {
        var source = """
            var n = 3;
            println($"n is {n}");
            """;
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.coerceToString(n)", js, StringComparison.Ordinal);
        Assert.Contains("\"n is \"", js, StringComparison.Ordinal);
        Assert.DoesNotContain("${n}", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsDestructuring_ToRuntimeHelpers()
    {
        var source = """
            var [a, b, ...rest] = [1, 2, 3, 4];
            var { name: n } = dict { "name": "Ada" };
            println(a);
            println(n);
            """;
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.getArray(", js, StringComparison.Ordinal);
        Assert.Contains("Destructuring pattern did not match value.", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.objectHasKey(", js, StringComparison.Ordinal);
        Assert.Contains("let a =", js, StringComparison.Ordinal);
        Assert.Contains("let rest =", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsClassExtendsAndSuper()
    {
        var source = """
            class Animal {
                function Animal(name) {
                    this.name = name;
                }
            }
            class Dog extends Animal {
                function Dog(name) {
                    super(name);
                }
                function speak() {
                    return super.speak;
                }
            }
            """;
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("class Dog extends Animal {", js, StringComparison.Ordinal);
        Assert.Contains("super(name)", js, StringComparison.Ordinal);
        Assert.Contains("super.speak", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsMathStrIoAndJsonBuiltins()
    {
        var source = """
            println(math.abs(-4));
            println(str.upper("ada"));
            io.print(math.sqrt(9));
            var parsed = parseJSON("{\"k\":1}");
            println(toJSON(parsed));
            """;
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.math.abs(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.str.upper(\"ada\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.io.print(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.parseJSON(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.toJSON(", js, StringComparison.Ordinal);
    }

    [Fact]
    public void JsTranspiler_MapsSchemaValidateAndDatesHttpEnvWithin()
    {
        var source = """
            schema Person {
                name: string;
                age: int;
            }
            var check = validate("Person", dict { "name": "Ada", "age": 36 });
            println(check.ok);
            println(now());
            println(hasEnv("PATH"));
            var response = httpGet("https://example.invalid/");
            @within(50)
            function bounded() {
                return 1;
            }
            bounded();
            """;
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScriptFromSource(source);

        Assert.Contains("mlRuntime.schema.register(\"Person\"", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.schema.validate(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.now()", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.hasEnv(\"PATH\")", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.http.get(", js, StringComparison.Ordinal);
        Assert.Contains("mlRuntime.within.run(50, \"bounded\"", js, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileToJavaScript_FromFile_InlinesSelectiveImport()
    {
        var sourcePath = PlanningPaths.ResolveRepoFile("Examples", "Modules", "selective_import.malda");
        var compiler = new Compiler.Compiler();
        var js = compiler.TranspileToJavaScript(sourcePath);

        Assert.Contains("function add(", js, StringComparison.Ordinal);
        Assert.Contains("let VERSION =", js, StringComparison.Ordinal);
        Assert.DoesNotContain("function unused(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("function internalHelper(", js, StringComparison.Ordinal);
        Assert.Contains("add(2, 3)", js, StringComparison.Ordinal);
    }
}
