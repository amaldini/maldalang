// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Runtime.Profiling;

namespace MaldaLang.Compiler;

public partial class Compiler
{
    public string TranspileToJavaScript(string sourcePath)
    {
        try
        {
            var source = File.ReadAllText(sourcePath);
            var transpileResult = TranspileToJavaScriptArtifactsFromSource(source, sourcePath, generatedFileName: Path.GetFileName(sourcePath));
            return transpileResult.JavaScript;
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    public string TranspileToJavaScriptFromSource(string source)
    {
        return TranspileToJavaScriptFromSource(source, sourceFilePath: null);
    }

    public string TranspileToJavaScriptFromSource(string source, string? sourceFilePath)
    {
        try
        {
            var transpileResult = TranspileToJavaScriptArtifactsFromSource(source, sourceFilePath, generatedFileName: null);
            return transpileResult.JavaScript;
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    public JsTranspileResult TranspileToJavaScriptWithSourceMapFromSource(
        string source,
        string? sourceFilePath,
        string? generatedFileName = null)
    {
        try
        {
            return TranspileToJavaScriptArtifactsFromSource(source, sourceFilePath, generatedFileName);
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    public static string AppendJavaScriptSourceMapReference(string jsCode, string mapFileName)
    {
        return AppendSourceMapReference(jsCode, mapFileName);
    }

    private static string PreprocessTemplateSourceIfNeeded(string source, string? sourceFilePath)
    {
        if (!TemplatePreprocessor.IsTemplatePath(sourceFilePath))
        {
            return source;
        }

        return TemplatePreprocessor.Preprocess(source, sourceFilePath);
    }

    /// <summary>
    /// <c>--mode esm</c> from a source string: an ES module that imports
    /// <c>./malda-js-runtime.js</c>, runs the program when evaluated and exports its declarations.
    /// </summary>
    public string TranspileToJavaScriptModuleFromSource(string source, string? sourceFilePath = null)
    {
        try
        {
            return TranspileToJavaScriptArtifactsFromSource(source, sourceFilePath, generatedFileName: null, JsModuleFormat.EsModule).JavaScript;
        }
        catch (ParseException ex)
        {
            throw new Exception($"Syntax error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Transpilation error: {ex}");
        }
    }

    public CompilationResult CompileToJavaScript(string sourcePath, string outputPath) =>
        CompileToJavaScriptFile(sourcePath, outputPath, JsModuleFormat.Script);

    /// <summary><c>--mode esm</c>: an ES module (default <c>.mjs</c>) beside the runtime and an <c>index.html</c> that imports it.</summary>
    public CompilationResult CompileToJavaScriptModule(string sourcePath, string outputPath) =>
        CompileToJavaScriptFile(sourcePath, outputPath, JsModuleFormat.EsModule);

    private CompilationResult CompileToJavaScriptFile(string sourcePath, string outputPath, JsModuleFormat moduleFormat)
    {
        try
        {
            var isEsModule = moduleFormat == JsModuleFormat.EsModule;
            var finalOutputPath = ResolveJavaScriptOutputPath(sourcePath, outputPath, isEsModule ? ".mjs" : ".js");
            var source = File.ReadAllText(sourcePath);
            var transpileResult = TranspileToJavaScriptArtifactsFromSource(source, sourcePath, generatedFileName: Path.GetFileName(finalOutputPath), moduleFormat);
            var mapFileName = Path.GetFileName(finalOutputPath) + ".map";
            var jsWithSourceMap = AppendSourceMapReference(transpileResult.JavaScript, mapFileName);

            // Like the exe and PWA paths: `-o dist/app.js` must work when dist/ does not exist yet.
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(finalOutputPath)) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(outputDirectory);

            File.WriteAllText(finalOutputPath, jsWithSourceMap, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(transpileResult.SourceMapJson))
            {
                var mapPath = finalOutputPath + ".map";
                File.WriteAllText(mapPath, transpileResult.SourceMapJson, new UTF8Encoding(false));
            }

            var mainScriptFileName = Path.GetFileName(finalOutputPath);
            var appName = Path.GetFileNameWithoutExtension(mainScriptFileName);
            JsRuntimeAreas.WriteBeside(outputDirectory, GetJavaScriptRuntimeAssetContent(), transpileResult.JavaScript);
            var usesUi = WriteUiClientBesideIfUsed(outputDirectory, transpileResult.JavaScript);
            var usesThree = WriteThreeBundleBesideIfUsed(outputDirectory, transpileResult.JavaScript);
            File.WriteAllText(
                Path.Combine(outputDirectory, "index.html"),
                isEsModule
                    ? GenerateJavaScriptModuleHostHtml(appName, mainScriptFileName, usesUi, usesThree)
                    : GenerateJavaScriptHostHtml(appName, mainScriptFileName, usesUi, usesThree),
                Encoding.UTF8);

            return new CompilationResult
            {
                Success = true,
                OutputPath = finalOutputPath,
                Report = FinishJavaScriptBuild(outputDirectory, finalOutputPath, !string.IsNullOrWhiteSpace(transpileResult.SourceMapJson))
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Transpilation error: {ex.Message}"
            };
        }
    }

    public CompilationResult CompileToPwa(string sourcePath, string outputPath)
    {
        try
        {
            var outputDirectory = ResolvePwaOutputDirectory(sourcePath, outputPath);
            Directory.CreateDirectory(outputDirectory);

            var appName = GetPwaAppName(sourcePath);
            var mainScriptFileName = appName + ".js";
            var source = File.ReadAllText(sourcePath);
            var transpileResult = TranspileToJavaScriptArtifactsFromSource(source, sourcePath, generatedFileName: mainScriptFileName);
            var jsOutputPath = Path.Combine(outputDirectory, mainScriptFileName);
            var mapFileName = mainScriptFileName + ".map";
            var jsWithSourceMap = AppendSourceMapReference(transpileResult.JavaScript, mapFileName);

            File.WriteAllText(jsOutputPath, jsWithSourceMap, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(transpileResult.SourceMapJson))
            {
                File.WriteAllText(jsOutputPath + ".map", transpileResult.SourceMapJson, new UTF8Encoding(false));
            }

            JsRuntimeAreas.WriteBeside(outputDirectory, GetJavaScriptRuntimeAssetContent(), transpileResult.JavaScript);
            var usesUi = WriteUiClientBesideIfUsed(outputDirectory, transpileResult.JavaScript);
            var usesThree = WriteThreeBundleBesideIfUsed(outputDirectory, transpileResult.JavaScript);
            File.WriteAllText(Path.Combine(outputDirectory, "index.html"), GeneratePwaIndexHtml(appName, mainScriptFileName, usesUi, usesThree), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "icon.svg"), GeneratePwaIconSvg(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "manifest.webmanifest"), GeneratePwaManifest(appName), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "sw.js"), GeneratePwaServiceWorker(appName, mainScriptFileName, usesUi, usesThree), Encoding.UTF8);

            return new CompilationResult
            {
                Success = true,
                OutputPath = outputDirectory,
                Report = FinishJavaScriptBuild(outputDirectory, jsOutputPath, !string.IsNullOrWhiteSpace(transpileResult.SourceMapJson))
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Transpilation error: {ex.Message}"
            };
        }
    }

    public CompilationResult CompileToFullStack(string sourcePath, string outputPath, bool includeLLamaSharp = false, bool includeUiHost = false, ProfilingOptions? profilingOptions = null, int typedTranspileLevel = 1)
    {
        try
        {
            var source = File.ReadAllText(sourcePath);
            if (!FullStackSourceInspector.IsFullStackSource(source))
            {
                return new CompilationResult
                {
                    Success = false,
                    ErrorMessage = "Full-stack compile requires both client and server targets (or route decorators) in the same source."
                };
            }

            var outputDirectory = ResolveFullStackOutputDirectory(sourcePath, outputPath);
            var appName = GetPwaAppName(sourcePath);
            var serverDirectory = Path.Combine(outputDirectory, "server");
            var webDirectory = Path.Combine(outputDirectory, "web");
            Directory.CreateDirectory(serverDirectory);
            Directory.CreateDirectory(webDirectory);

            var serverOutputPath = Path.Combine(serverDirectory, appName + ".server.exe");
            var clientScriptPath = Path.Combine(webDirectory, appName + ".js");

            var serverResult = CompileWithTranspilation(sourcePath, serverOutputPath, includeLLamaSharp, includeUiHost, profilingOptions, typedTranspileLevel);
            if (!serverResult.Success)
            {
                return serverResult;
            }

            var clientResult = CompileToJavaScript(sourcePath, clientScriptPath);
            if (!clientResult.Success)
            {
                return clientResult;
            }

            var serverExecutablePath = serverResult.OutputPath ?? serverOutputPath;
            var clientScriptFileName = Path.GetFileName(clientResult.OutputPath ?? clientScriptPath);
            var manifestPath = Path.Combine(outputDirectory, "manifest.json");
            var port = FullStackSourceInspector.ExtractHttpPort(source, 8090);

            var manifestObject = new
            {
                type = "malda-fullstack",
                source = Path.GetFullPath(sourcePath),
                createdAtUtc = DateTime.UtcNow.ToString("O"),
                port,
                server = new
                {
                    executable = Path.GetFullPath(serverExecutablePath),
                    webDirectoryEnv = "MALDA_WEB_DIRECTORY"
                },
                web = new
                {
                    directory = Path.GetFullPath(webDirectory),
                    entryHtml = Path.GetFullPath(Path.Combine(webDirectory, "index.html")),
                    entryScript = Path.GetFullPath(Path.Combine(webDirectory, clientScriptFileName))
                },
                run = new
                {
                    command = Path.GetFileName(serverExecutablePath),
                    workingDirectory = Path.GetDirectoryName(Path.GetFullPath(serverExecutablePath)) ?? serverDirectory,
                    env = new
                    {
                        MALDA_WEB_DIRECTORY = Path.GetFullPath(webDirectory)
                    }
                }
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestObject, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

            return new CompilationResult
            {
                Success = true,
                OutputPath = outputDirectory
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                ErrorMessage = $"Full-stack compilation error: {ex.Message}"
            };
        }
    }

    private static string ResolveJavaScriptOutputPath(string sourcePath, string outputPath, string defaultExtension = ".js")
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.ChangeExtension(sourcePath, defaultExtension);
        }

        var extension = Path.GetExtension(outputPath);
        if (string.IsNullOrEmpty(extension))
        {
            return outputPath + defaultExtension;
        }

        return outputPath;
    }

    private static string ResolvePwaOutputDirectory(string sourcePath, string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return outputPath;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        var outputDirectoryName = GetPwaAppName(sourcePath);
        return string.IsNullOrWhiteSpace(sourceDirectory)
            ? outputDirectoryName
            : Path.Combine(sourceDirectory, outputDirectoryName);
    }

    private static string ResolveFullStackOutputDirectory(string sourcePath, string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return outputPath;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        var outputDirectoryName = GetPwaAppName(sourcePath) + "-fullstack";
        return string.IsNullOrWhiteSpace(sourceDirectory)
            ? outputDirectoryName
            : Path.Combine(sourceDirectory, outputDirectoryName);
    }

    private static string AppendSourceMapReference(string jsCode, string mapFileName)
    {
        var normalized = jsCode.TrimEnd();
        return $"{normalized}{Environment.NewLine}//# sourceMappingURL={mapFileName}{Environment.NewLine}";
    }

    private static JsTranspileResult TranspileToJavaScriptArtifactsFromSource(
        string source,
        string? sourceFilePath,
        string? generatedFileName,
        JsModuleFormat moduleFormat = JsModuleFormat.Script)
    {
        source = PreprocessTemplateSourceIfNeeded(source, sourceFilePath);
        var lexer = new Lexer(source, sourceFilePath);
        var tokens = lexer.Tokenize();
        var parser = new MaldaLang.Parser.Parser(tokens, sourceFilePath);
        var statements = parser.Parse();

        if (parser.Errors.Count > 0)
        {
            throw new Exception($"Parse errors: {string.Join(", ", parser.Errors.Select(e => e.Message))}");
        }

        statements = ModuleSymbolResolver.ExpandFileImportsForTranspile(statements, sourceFilePath);

        var partitionResult = TargetPartitioner.Partition(statements);
        ThrowIfTargetPartitionDiagnostics(partitionResult.Diagnostics);

        // I4: report every host-only feature (JS0xxx) before emit instead of the first
        // NotSupportedException, or a call that only fails in the browser.
        var capabilityDiagnostics = JsCapabilityChecker.Check(partitionResult.JavaScriptStatements);
        if (capabilityDiagnostics.Count > 0)
        {
            throw new JsCapabilityException(capabilityDiagnostics);
        }

        var transpiler = new JsTranspiler { ModuleFormat = moduleFormat };
        return transpiler.TranspileWithSourceMap(
            partitionResult.JavaScriptStatements,
            isLibrary: false,
            sourceFilePath,
            sourceContent: source,
            generatedFileName: generatedFileName);
    }

    private static void ThrowIfTargetPartitionDiagnostics(IReadOnlyList<TargetPartitionDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        var errorText = string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));
        throw new Exception($"Target partitioning error(s): {errorText}");
    }

    private static string GetPwaAppName(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        const string templateSuffix = ".malda.html";
        if (fileName.EndsWith(templateSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^templateSuffix.Length];
        }

        return Path.GetFileNameWithoutExtension(sourcePath);
    }

    private static string GetJavaScriptRuntimeAssetContent()
    {
        foreach (var path in GetRuntimeAssetCandidatePaths("Examples", "Web", "wwwroot", "malda-js-runtime.js"))
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return "console.warn('malda-js-runtime.js was not found at compile time. The generated PWA may not run correctly.');";
    }

    public const string JsUiClientFileName = "malda-ui-client.js";

    /// <summary>Browser bundle that defines <c>globalThis.THREE</c>, copied beside a program that calls <c>three.*</c>.</summary>
    public const string ThreeBundleFileName = "three.min.js";

    /// <summary>
    /// Writes <c>malda-ui-client.js</c> beside a program whose JavaScript calls <c>ui.*</c>: in local
    /// mode the runtime's <c>ui</c> area diffs and draws through its <c>MaldaUiCore</c> /
    /// <c>MaldaUiDom</c>. The client is the one the UI host serves, embedded in MaldaLang.UIHost.
    /// </summary>
    private static bool WriteUiClientBesideIfUsed(string outputDirectory, string generatedJavaScript)
    {
        if (!JsRuntimeAreas.UsedBy(generatedJavaScript).Contains("ui"))
            return false;
        File.WriteAllText(Path.Combine(outputDirectory, JsUiClientFileName), GetUiClientAssetContent(), new UTF8Encoding(false));
        return true;
    }

    public static string GetUiClientAssetContent()
    {
        const string resource = "MaldaLang.UIHost.wwwroot.malda-ui-client.js";
        using var stream = typeof(MaldaLang.UIHost.UiTreeHash).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"MaldaLang.UIHost.dll has no embedded {resource}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Copies <c>Examples/Web/wwwroot/vendor/three.min.js</c> beside a program that calls <c>three.*</c>.
    /// The runtime reads <c>globalThis.THREE</c> and does not bundle three.js itself.
    /// </summary>
    private static bool WriteThreeBundleBesideIfUsed(string outputDirectory, string generatedJavaScript)
    {
        if (!JsRuntimeAreas.UsedBy(generatedJavaScript).Contains("three"))
            return false;

        var source = FindThreeBundlePath();
        File.Copy(source, Path.Combine(outputDirectory, ThreeBundleFileName), overwrite: true);
        return true;
    }

    private static string FindThreeBundlePath()
    {
        foreach (var candidate in GetRuntimeAssetCandidatePaths("Examples", "Web", "wwwroot", "vendor", ThreeBundleFileName))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            "A program that calls three.* needs three.min.js on the page before malda-js-runtime.js. "
            + "Looked for Examples/Web/wwwroot/vendor/three.min.js walking up from the working directory and the compiler.");
    }

    /// <summary>The script tag of the local-mode UI client, before the runtime; empty when the program does not use ui.*.</summary>
    private static string UiClientScriptTag(bool usesUi) =>
        usesUi ? $"<script src=\"./{JsUiClientFileName}\" data-ui-mode=\"local\"></script>\n    " : string.Empty;

    /// <summary>The three.js bundle, before the runtime; empty when the program does not call three.*.</summary>
    private static string ThreeScriptTag(bool usesThree) =>
        usesThree ? $"<script src=\"./{ThreeBundleFileName}\"></script>\n    " : string.Empty;

    private static IEnumerable<string> GetRuntimeAssetCandidatePaths(params string[] relativeSegments)
    {
        var currentDirectory = new DirectoryInfo(Environment.CurrentDirectory);
        while (currentDirectory != null)
        {
            yield return Path.Combine(new[] { currentDirectory.FullName }.Concat(relativeSegments).ToArray());
            currentDirectory = currentDirectory.Parent;
        }

        var compilerLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        var compilerDirectory = string.IsNullOrWhiteSpace(compilerLocation) ? null : new DirectoryInfo(compilerLocation);
        while (compilerDirectory != null)
        {
            yield return Path.Combine(new[] { compilerDirectory.FullName }.Concat(relativeSegments).ToArray());
            compilerDirectory = compilerDirectory.Parent;
        }
    }

    private static string GeneratePwaIndexHtml(string appName, string mainScriptFileName, bool usesUi, bool usesThree)
    {
        var scriptLiteral = JsonSerializer.Serialize(mainScriptFileName);
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="theme-color" content="#0f172a">
    <title>{{appName}}</title>
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='14' fill='%230f172a'/%3E%3Cpath d='M18 44V16h8v20h20v8H18Z' fill='%2360a5fa'/%3E%3C/svg%3E">
    <link rel="manifest" href="./manifest.webmanifest">
    <style>
        :root {
            color-scheme: dark;
            font-family: Arial, sans-serif;
        }

        body {
            margin: 0;
            min-height: 100vh;
            background: #020617;
            color: #e2e8f0;
        }

        #status {
            padding: 10px 14px;
            border-bottom: 1px solid #1e293b;
            background: #0f172a;
            color: #94a3b8;
            font-size: 14px;
        }

        #status.error {
            color: #fecaca;
            background: #450a0a;
            border-bottom-color: #7f1d1d;
        }

        #app {
            min-height: calc(100vh - 45px);
        }

        #status[hidden] + #app {
            min-height: 100vh;
        }
    </style>
</head>
<body>
    <div id="status"{{(usesUi ? " hidden" : "")}}>Loading {{appName}}...</div>
    <div id="app"></div>
    {{ThreeScriptTag(usesThree)}}{{UiClientScriptTag(usesUi)}}<script src="./malda-js-runtime.js"></script>
    <script src="./{{mainScriptFileName}}"></script>
    <script>
        (function () {
            var statusElement = document.getElementById("status");

            function setStatus(message, isError) {
                if (!statusElement) {
                    return;
                }

                statusElement.textContent = message;
                statusElement.className = isError ? "error" : "";
                if (isError) {
                    statusElement.hidden = false;
                }
            }

            async function runEntryPoint() {
                if (!window.MaldaApp) {
                    throw new Error("MaldaApp was not registered by " + {{scriptLiteral}} + ".");
                }

                if (typeof window.MaldaApp.main === "function") {
                    await window.MaldaApp.main();
                }

                if (typeof window.MaldaApp.bootstrap === "function") {
                    await window.MaldaApp.bootstrap("#app");
                    return;
                }

                if (typeof window.MaldaApp.renderRoot === "function") {
                    await window.MaldaApp.renderRoot("#app");
                    return;
                }

                if (typeof window.MaldaApp.main !== "function") {
                    throw new Error("No supported MALDA entry point was found. Expected main(), bootstrap(), or renderRoot().");
                }
            }

            window.addEventListener("load", function () {
                runEntryPoint()
                    .then(function () {
                        setStatus("Loaded {{appName}}", false);
                    })
                    .catch(function (error) {
                        console.error(error);
                        setStatus(error && error.message ? error.message : "PWA startup failed.", true);
                    });

                if ("serviceWorker" in navigator) {
                    navigator.serviceWorker.register("./sw.js").catch(function (error) {
                        console.warn("Service worker registration failed.", error);
                    });
                }
            });
        })();
    </script>
</body>
</html>
""";
    }

    private static string GenerateJavaScriptHostHtml(string appName, string mainScriptFileName, bool usesUi, bool usesThree)
    {
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{appName}}</title>
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='14' fill='%230f172a'/%3E%3Cpath d='M18 44V16h8v20h20v8H18Z' fill='%2360a5fa'/%3E%3C/svg%3E">
    <style>
        :root {
            color-scheme: dark;
            font-family: Arial, sans-serif;
        }

        body {
            margin: 0;
            min-height: 100vh;
            background: #020617;
            color: #e2e8f0;
        }

        #status {
            padding: 10px 14px;
            border-bottom: 1px solid #1e293b;
            background: #0f172a;
            color: #94a3b8;
            font-size: 14px;
        }

        #status.error {
            color: #fecaca;
            background: #450a0a;
            border-bottom-color: #7f1d1d;
        }

        #app {
            min-height: calc(100vh - 45px);
        }

        #status[hidden] + #app {
            min-height: 100vh;
        }
    </style>
</head>
<body>
    <div id="status"{{(usesUi ? " hidden" : "")}}>Loading {{appName}}...</div>
    <div id="app"></div>
    {{ThreeScriptTag(usesThree)}}{{UiClientScriptTag(usesUi)}}<script src="./malda-js-runtime.js"></script>
    <script src="./{{mainScriptFileName}}"></script>
    <script>
        (function () {
            var statusElement = document.getElementById("status");

            function setStatus(message, isError) {
                if (!statusElement) {
                    return;
                }

                statusElement.textContent = message;
                statusElement.className = isError ? "error" : "";
                if (isError) {
                    statusElement.hidden = false;
                }
            }

            async function runEntryPoint() {
                if (!window.MaldaApp) {
                    throw new Error("MaldaApp was not registered by " + "{{mainScriptFileName}}" + ".");
                }

                if (typeof window.MaldaApp.main === "function") {
                    await window.MaldaApp.main();
                }

                if (typeof window.MaldaApp.bootstrap === "function") {
                    await window.MaldaApp.bootstrap("#app");
                    return;
                }

                if (typeof window.MaldaApp.renderRoot === "function") {
                    await window.MaldaApp.renderRoot("#app");
                    return;
                }

                if (typeof window.MaldaApp.main !== "function") {
                    throw new Error("No supported MALDA entry point was found. Expected main(), bootstrap(), or renderRoot().");
                }
            }

            window.addEventListener("load", function () {
                runEntryPoint()
                    .then(function () {
                        setStatus("Loaded {{appName}}", false);
                    })
                    .catch(function (error) {
                        console.error(error);
                        setStatus(error && error.message ? error.message : "JavaScript app startup failed.", true);
                    });
            });
        })();
    </script>
</body>
</html>
""";
    }

    /// <summary>
    /// Host page for <c>--mode esm</c>: the module runs the program when it is imported, so the
    /// page only imports it and reports the outcome.
    /// </summary>
    private static string GenerateJavaScriptModuleHostHtml(string appName, string moduleFileName, bool usesUi, bool usesThree)
    {
        var moduleLiteral = JsonSerializer.Serialize("./" + moduleFileName);
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{appName}}</title>
    <style>
        body { margin: 0; min-height: 100vh; background: #020617; color: #e2e8f0; font-family: Arial, sans-serif; }
        #status { padding: 10px 14px; border-bottom: 1px solid #1e293b; background: #0f172a; color: #94a3b8; font-size: 14px; }
        #status.error { color: #fecaca; background: #450a0a; border-bottom-color: #7f1d1d; }
    </style>
</head>
<body>
    <div id="status"{{(usesUi ? " hidden" : "")}}>Loading {{appName}}...</div>
    <div id="app"></div>
    {{ThreeScriptTag(usesThree)}}{{UiClientScriptTag(usesUi)}}<script type="module">
        const status = document.getElementById("status");
        try {
            await import({{moduleLiteral}});
            status.textContent = "Loaded {{appName}}";
        } catch (error) {
            console.error(error);
            status.textContent = error && error.message ? error.message : "JavaScript module startup failed.";
            status.className = "error";
            status.hidden = false;
        }
    </script>
</body>
</html>
""";
    }

    private static string GeneratePwaManifest(string appName)
    {
        var nameLiteral = JsonSerializer.Serialize(appName);
        return $$"""
{
  "name": {{nameLiteral}},
  "short_name": {{nameLiteral}},
  "start_url": "./index.html",
  "display": "standalone",
  "background_color": "#020617",
  "theme_color": "#0f172a",
  "icons": [
    {
      "src": "./icon.svg",
      "sizes": "512x512",
      "type": "image/svg+xml"
    }
  ]
}
""";
    }

    private static string GeneratePwaIconSvg()
    {
        return """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <defs>
    <linearGradient id="maldaGradient" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#0f172a" />
      <stop offset="100%" stop-color="#2563eb" />
    </linearGradient>
  </defs>
  <rect width="512" height="512" rx="96" fill="url(#maldaGradient)" />
  <path d="M128 384V128h64l64 88 64-88h64v256h-64V232l-64 88-64-88v152z" fill="#f8fafc" />
</svg>
""";
    }

    private static string GeneratePwaServiceWorker(string appName, string mainScriptFileName, bool usesUi, bool usesThree)
    {
        var cacheNameLiteral = JsonSerializer.Serialize($"malda-pwa-{appName.ToLowerInvariant()}-v1");
        var mainScriptLiteral = JsonSerializer.Serialize("./" + mainScriptFileName);
        var uiClientEntry = usesUi ? $"\"./{JsUiClientFileName}\",\n    " : string.Empty;
        var threeEntry = usesThree ? $"\"./{ThreeBundleFileName}\",\n    " : string.Empty;
        return $$"""
const CACHE_NAME = {{cacheNameLiteral}};
const APP_SHELL = [
    "./",
    "./index.html",
    "./manifest.webmanifest",
    "./icon.svg",
    "./sw.js",
    {{threeEntry}}{{uiClientEntry}}"./malda-js-runtime.js",
    {{mainScriptLiteral}}
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => cache.addAll(APP_SHELL))
    );
    self.skipWaiting();
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
    );
    self.clients.claim();
});

self.addEventListener("fetch", (event) => {
    if (event.request.method !== "GET") {
        return;
    }

    const requestUrl = new URL(event.request.url);
    if (requestUrl.origin !== self.location.origin) {
        return;
    }

    event.respondWith(
        caches.match(event.request).then((cachedResponse) => {
            if (cachedResponse) {
                return cachedResponse;
            }

            return fetch(event.request).then((networkResponse) => {
                if (!networkResponse || networkResponse.status !== 200) {
                    return networkResponse;
                }

                const responseToCache = networkResponse.clone();
                caches.open(CACHE_NAME).then((cache) => cache.put(event.request, responseToCache));
                return networkResponse;
            });
        })
    );
});
""";
    }
}
