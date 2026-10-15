// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.IO;
using System.Text;
using MaldaLang.DesktopIDE.Services;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private async void PreviewWebButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await PreviewCurrentDocumentAsync();
        }
        catch (Exception ex)
        {
            IdePromptWindow.Show(
                this,
                $"Could not open web preview.\n\n{ex.Message}",
                "Web Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task PreviewCurrentDocumentAsync()
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var activePath = GetPhysicalPath(activeDocument);
        var sourceForExecution = GetSourceForExecution(activeDocument);
        if (string.IsNullOrWhiteSpace(activePath))
        {
            throw new InvalidOperationException("Save the current file first so web preview can keep relative includes and asset paths working.");
        }

        if (IsHtmlPreviewDocument(activePath))
        {
            await OpenUriInWebUiPanelAsync(new Uri(Path.GetFullPath(activePath)), activePath, switchToTab: true, ensureUiHost: false);
            return;
        }

        var repoRoot = FindRepoRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("Could not locate the repository root needed for web preview assets.");
        }

        var hostPath = ResolveWebPreviewHostPath(repoRoot);
        string scriptPath;
        if (IsJavaScriptPreviewDocument(activePath))
        {
            scriptPath = Path.GetFullPath(activePath);
        }
        else if (IsMaldaPreviewDocument(activePath))
        {
            scriptPath = WriteWebPreviewJavaScriptArtifact(repoRoot, sourceForExecution.Source, activePath).ScriptPath;
        }
        else
        {
            throw new InvalidOperationException("Web preview currently supports .malda, .malda.html, .js, and .html files.");
        }

        var previewUri = BuildWebPreviewHostUri(
            hostPath,
            repoRoot,
            scriptPath,
            Path.GetFileNameWithoutExtension(activePath),
            activePath);
        if (_showcasePreviewAutoplay)
        {
            var joiner = string.IsNullOrEmpty(previewUri.Query) ? "?" : "&";
            previewUri = new Uri(previewUri.AbsoluteUri + joiner + "autoplay=1");
        }

        await OpenUriInWebUiPanelAsync(previewUri, previewUri.AbsoluteUri, switchToTab: true, ensureUiHost: false);
    }

    private static bool IsMaldaPreviewDocument(string filePath)
    {
        return filePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".malda.html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJavaScriptPreviewDocument(string filePath)
    {
        return filePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtmlPreviewDocument(string filePath)
    {
        if (filePath.EndsWith(".malda.html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWebPreviewHostPath(string repoRoot)
    {
        var preferredHost = Path.Combine(repoRoot, DefaultWebPreviewHostFileName);
        if (File.Exists(preferredHost))
        {
            return preferredHost;
        }

        var fallbackHost = Path.Combine(repoRoot, "host.html");
        if (File.Exists(fallbackHost))
        {
            return fallbackHost;
        }

        return EnsureGeneratedWebPreviewHost(repoRoot);
    }

    private static string EnsureGeneratedWebPreviewHost(string repoRoot)
    {
        var previewDir = Path.Combine(repoRoot, PreviewArtifactsDirectoryName);
        Directory.CreateDirectory(previewDir);

        var generatedHost = Path.Combine(previewDir, DefaultWebPreviewHostFileName);
        File.WriteAllText(
            generatedHost,
            GeneratedWebPreviewHostHtml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return generatedHost;
    }

    private static Uri BuildWebPreviewHostUri(
        string hostPath,
        string repoRoot,
        string scriptPath,
        string title,
        string? sourceFilePath)
    {
        return WebPreviewHostBuilder.BuildHostUri(hostPath, repoRoot, scriptPath, title, sourceFilePath);
    }

    private const string GeneratedWebPreviewHostHtml =
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>MALDA JavaScript App</title>
          <style>
            :root {
              color-scheme: dark;
              font-family: Arial, sans-serif;
            }

            body {
              margin: 0;
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
          </style>
        </head>
        <body>
          <div id="status">Loading MALDA web preview...</div>
          <div id="app"></div>
          <script>
            (function () {
              var params = new URLSearchParams(window.location.search);
              var config = {
                title: params.get("title") || "MALDA JavaScript App",
                three: params.get("three") || "../Examples/Web/wwwroot/vendor/three.min.js",
                runtime: params.get("runtime") || "../Examples/Web/wwwroot/malda-js-runtime.js",
                script: params.get("script") || "program.js",
                rootSelector: params.get("root") || "#app",
                entry: params.get("entry") || "auto",
                assets: params.get("assets") || ""
              };

              var statusElement = document.getElementById("status");
              document.title = config.title;
              var assetBase = config.assets;
              if (assetBase && assetBase.charAt(assetBase.length - 1) !== "/") {
                assetBase += "/";
              }
              window.__maldaAssetBase = assetBase;
              if (params.get("autoplay") === "1" && !document.getElementById("malda-autoplay")) {
                var autoplayMarker = document.createElement("div");
                autoplayMarker.id = "malda-autoplay";
                autoplayMarker.hidden = true;
                document.body.appendChild(autoplayMarker);
              }

              function setStatus(message, isError) {
                statusElement.textContent = message;
                statusElement.className = isError ? "error" : "";
              }

              function loadScript(src) {
                return new Promise(function (resolve, reject) {
                  var script = document.createElement("script");
                  script.src = src;
                  script.onload = resolve;
                  script.onerror = function () {
                    reject(new Error("Could not load script: " + src));
                  };
                  document.head.appendChild(script);
                });
              }

              async function runEntryPoint() {
                if (!window.MaldaApp) {
                  throw new Error("MaldaApp was not registered by " + config.script + ".");
                }

                if (config.entry === "bootstrap" && typeof window.MaldaApp.bootstrap === "function") {
                  await window.MaldaApp.bootstrap(config.rootSelector);
                  return;
                }

                if (config.entry === "main" && typeof window.MaldaApp.main === "function") {
                  await window.MaldaApp.main();
                  return;
                }

                if (config.entry === "renderRoot" && typeof window.MaldaApp.renderRoot === "function") {
                  await window.MaldaApp.renderRoot(config.rootSelector);
                  return;
                }

                if (typeof window.MaldaApp.bootstrap === "function") {
                  await window.MaldaApp.bootstrap(config.rootSelector);
                  return;
                }

                if (typeof window.MaldaApp.main === "function") {
                  await window.MaldaApp.main();
                  return;
                }

                if (typeof window.MaldaApp.renderRoot === "function") {
                  await window.MaldaApp.renderRoot(config.rootSelector);
                  return;
                }

                throw new Error("No supported MALDA entry point was found. Expected bootstrap(), main(), or renderRoot().");
              }

              async function start() {
                setStatus("Loading browser runtime...", false);
                await loadScript(config.three);
                await loadScript(config.runtime);

                setStatus("Loading " + config.script + "...", false);
                await loadScript(config.script);
                await runEntryPoint();

                setStatus("Loaded " + config.script, false);
              }

              start().catch(function (error) {
                console.error(error);
                setStatus(error && error.message ? error.message : "Web preview failed.", true);
              });
            })();
          </script>
        </body>
        </html>
        """;

    private readonly record struct WebPreviewJavaScriptArtifact(string ScriptPath, string? SourceMapJson);

    private static WebPreviewJavaScriptArtifact WriteWebPreviewJavaScriptArtifact(string repoRoot, string source, string sourceFilePath)
    {
        var previewDir = Path.Combine(repoRoot, PreviewArtifactsDirectoryName);
        Directory.CreateDirectory(previewDir);

        var relativeSourcePath = Path.GetRelativePath(repoRoot, sourceFilePath);
        var outputFileName = SanitizePreviewArtifactName(relativeSourcePath) + ".js";
        var outputPath = Path.Combine(previewDir, outputFileName);

        var compiler = new Compiler.Compiler();
        var transpileResult = compiler.TranspileToJavaScriptWithSourceMapFromSource(source, sourceFilePath, outputFileName);
        var mapFileName = outputFileName + ".map";
        var javaScript = Compiler.Compiler.AppendJavaScriptSourceMapReference(transpileResult.JavaScript, mapFileName);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(outputPath, javaScript, encoding);
        if (!string.IsNullOrWhiteSpace(transpileResult.SourceMapJson))
        {
            File.WriteAllText(outputPath + ".map", transpileResult.SourceMapJson, encoding);
        }

        return new WebPreviewJavaScriptArtifact(outputPath, transpileResult.SourceMapJson);
    }

    private static string SanitizePreviewArtifactName(string relativePath)
    {
        var builder = new StringBuilder(relativePath.Length);
        foreach (var ch in relativePath)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "preview" : sanitized;
    }
}
