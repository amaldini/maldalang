// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Text.Json;
using MaldaLang.Compiler;

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
}
