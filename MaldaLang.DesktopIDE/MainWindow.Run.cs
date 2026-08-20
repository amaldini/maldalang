// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.UserControls;
using MaldaLang.IDE;
using MaldaLang.IDE.Services;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Compiler;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using System.Xml;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Markup;
using Markdig;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using MaldaLang.BuiltIns;
using MaldaLang.TraceViewer;
using MaldaLang.UIHost;
using MaldaLang.Testing;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{

    private bool IsMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        // More strict markdown detection - only detect actual markdown structures
        // Check for markdown patterns that indicate structured content, not just any text with * or #
        var trimmed = text.TrimStart();
        
        // Check for headings at the start of lines
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^#{1,6}\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        
        // Check for code blocks
        if (trimmed.Contains("```"))
            return true;
        
        // Check for horizontal rules (--- on its own line)
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^---+$", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        
        // Check for markdown lists (lines starting with - or * or numbers)
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\s]*[-*+]\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\s]*\d+\.\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        
        // Check for markdown tables
        if (trimmed.Contains("|") && trimmed.Contains("---"))
            return true;
        
        // Check for blockquotes
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^>\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        
        return false;
    }

    private void SetOutputText(string text, bool isError = false)
    {
        TryAutoOpenWebUiFromOutput(text);
        UpdateRegressionActionState(text);

        var theme = _themeService.CurrentTheme;
        var scrollbarCss = GetScrollbarCss(theme);
        if (string.IsNullOrEmpty(text))
        {
            OutputWebBrowser.NavigateToString($"<html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"UTF-8\"><style>body {{ margin: 0; padding: 8px; font-family: Consolas; color: {ColorToHex(theme.TextForeground)}; background: {ColorToHex(theme.ListBackground)}; min-height: 100vh; }} html {{ background: {ColorToHex(theme.ListBackground)}; }}{scrollbarCss}</style></head><body><p style='color: {ColorToHex(theme.TextSecondary)}; font-style: italic;'>No output yet. Run your program to see output here.</p></body></html>");
            return;
        }
        
        if (IsMarkdown(text))
        {
            // First, protect code blocks by replacing them with placeholders
            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var codeBlockPlaceholders = new System.Collections.Generic.Dictionary<string, string>();
            var placeholderCounter = 0;
            
            // Protect code blocks (```...```)
            var protectedText = System.Text.RegularExpressions.Regex.Replace(normalized, @"```[\s\S]*?```", 
                m => {
                    var placeholder = $"__CODE_BLOCK_{placeholderCounter}__";
                    codeBlockPlaceholders[placeholder] = m.Value;
                    placeholderCounter++;
                    return placeholder;
                });
            
            // Convert single newlines to <br> tags BEFORE markdown processing
            // This ensures newlines are preserved
            protectedText = System.Text.RegularExpressions.Regex.Replace(
                protectedText,
                @"(?<!\n)\n(?!\n)",
                "<br>" // Convert to HTML br tag
            );
            
            // Restore code blocks
            foreach (var kvp in codeBlockPlaceholders)
            {
                protectedText = protectedText.Replace(kvp.Key, kvp.Value);
            }
            
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
            
            var html = Markdown.ToHtml(protectedText, pipeline);
            
            // Post-process: Replace any remaining newlines outside of code/pre blocks with <br>
            // (as a backup in case some newlines weren't converted)
            var htmlCodeBlockPlaceholders = new System.Collections.Generic.Dictionary<string, string>();
            var htmlPlaceholderCounter = 0;
            var protectedHtml = System.Text.RegularExpressions.Regex.Replace(html, @"(<pre[^>]*>.*?</pre>|<code[^>]*>.*?</code>)", 
                m => {
                    var placeholder = $"__HTML_CODE_BLOCK_{htmlPlaceholderCounter}__";
                    htmlCodeBlockPlaceholders[placeholder] = m.Value;
                    htmlPlaceholderCounter++;
                    return placeholder;
                },
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Replace all newlines with <br> tags
            protectedHtml = protectedHtml.Replace("\n", "<br>");
            
            // Restore code blocks
            foreach (var kvp in htmlCodeBlockPlaceholders)
            {
                protectedHtml = protectedHtml.Replace(kvp.Key, kvp.Value);
            }
            
            html = protectedHtml;
            var codeBg = theme.ListBackground.R < 128 ? Color.FromRgb((byte)(theme.ListBackground.R + 30), (byte)(theme.ListBackground.G + 30), (byte)(theme.ListBackground.B + 30)) : Color.FromRgb((byte)(theme.ListBackground.R - 20), (byte)(theme.ListBackground.G - 20), (byte)(theme.ListBackground.B - 20));
            var borderColor = theme.BorderColor;
            var fullHtml = $@"
<html>
<head>
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <meta charset=""UTF-8"">
    <style>
        html {{
            background: {ColorToHex(theme.ListBackground)};
            margin: 0;
            padding: 0;
            height: 100%;
        }}
        body {{
            font-family: Consolas, Monaco, monospace;
            font-size: 16px;
            color: {ColorToHex(theme.TextForeground)};
            background: {ColorToHex(theme.ListBackground)};
            padding: 16px;
            line-height: 1.6;
            margin: 0;
            min-height: 100vh;
        }}
        p {{
            margin: 0.5em 0;
        }}
        h1, h2, h3, h4, h5, h6 {{
            color: {ColorToHex(theme.TextForeground)};
            margin-top: 1em;
            margin-bottom: 0.5em;
        }}
        h1 {{ font-size: 1.8em; }}
        h2 {{ font-size: 1.5em; }}
        h3 {{ font-size: 1.3em; }}
        code {{
            background: {ColorToHex(codeBg)};
            padding: 2px 6px;
            border-radius: 3px;
            font-family: Consolas, Monaco, monospace;
        }}
        pre {{
            background: {ColorToHex(codeBg)};
            padding: 12px;
            border-radius: 4px;
            overflow-x: auto;
            margin: 1em 0;
        }}
        pre code {{
            background: transparent;
            padding: 0;
        }}
        blockquote {{
            border-left: 4px solid {ColorToHex(borderColor)};
            padding-left: 1em;
            margin: 1em 0;
            color: {ColorToHex(theme.TextSecondary)};
        }}
        table {{
            border-collapse: collapse;
            width: 100%;
            margin: 1em 0;
        }}
        table th, table td {{
            border: 1px solid {ColorToHex(borderColor)};
            padding: 8px;
        }}
        table th {{
            background: {ColorToHex(codeBg)};
            font-weight: bold;
        }}
        a {{
            color: {ColorToHex(theme.DebugAccent)};
            text-decoration: none;
        }}
        a:hover {{
            text-decoration: underline;
        }}
        ul, ol {{
            margin: 1em 0;
            padding-left: 2em;
        }}
        li {{
            margin: 0.5em 0;
        }}
        .error {{
            color: {ColorToHex(theme.ErrorColor)};
            background: {ColorToHex(Color.FromArgb(255, (byte)Math.Min(255, theme.ErrorColor.R + 50), (byte)Math.Min(255, theme.ErrorColor.G + 30), (byte)Math.Min(255, theme.ErrorColor.B + 30)))};
            padding: 8px;
            border-left: 4px solid {ColorToHex(theme.ErrorColor)};
            margin: 1em 0;
        }}
        {scrollbarCss}
    </style>
</head>
<body>
    {(isError ? $"<div class='error'><strong>Error:</strong><br/>{html}</div>" : html)}
</body>
</html>";
            
            OutputWebBrowser.NavigateToString(fullHtml);
        }
        else
        {
            // Plain text output - convert newlines to <br> tags for proper display
            var escapedText = System.Security.SecurityElement.Escape(text);
            // Replace newlines with <br> tags after escaping
            escapedText = escapedText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");
            
            var plainHtml = $@"
<html>
<head>
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <meta charset=""UTF-8"">
    <style>
        html {{
            background: {ColorToHex(theme.ListBackground)};
            margin: 0;
            padding: 0;
            height: 100%;
        }}
        body {{
            font-family: Consolas, Monaco, monospace;
            font-size: 16px;
            color: {(isError ? ColorToHex(theme.ErrorColor) : ColorToHex(theme.TextForeground))};
            background: {ColorToHex(theme.ListBackground)};
            padding: 8px;
            word-wrap: break-word;
            margin: 0;
            min-height: 100vh;
        }}
        {scrollbarCss}
    </style>
</head>
<body>
    {(isError ? $"<strong>Error:</strong><br/>{escapedText}" : escapedText)}
</body>
</html>";
            
            OutputWebBrowser.NavigateToString(plainHtml);
        }
    }

    private void UpdateRegressionActionState(string outputText)
    {
        if (PropertyRegressionArtifactSupport.TryExtractFromOutput(outputText, out var request))
        {
            _pendingRegressionRequest = request;
            RegressionActionBar.Visibility = Visibility.Visible;
            var fileName = request?.RecommendedRegressionFileName;
            if (string.IsNullOrWhiteSpace(fileName) && request != null)
            {
                fileName = PropertyRegressionArtifactSupport.BuildRecommendedFileName(request);
            }

            RegressionHintTextBlock.Text = string.IsNullOrWhiteSpace(fileName)
                ? "CI payload supports regression generation"
                : $"CI payload -> {fileName}";
            return;
        }

        _pendingRegressionRequest = null;
        RegressionActionBar.Visibility = Visibility.Collapsed;
        RegressionHintTextBlock.Text = string.Empty;
    }

    private string ResolveRegressionOutputPath(PropertyRegressionArtifactRequest request)
    {
        var workspaceRoot = GetCurrentWorkspaceDirectory();
        return PropertyRegressionArtifactSupport.ResolveWorkspaceSafePreferredPath(request, workspaceRoot);
    }

    private string GetCurrentWorkspaceDirectory()
    {
        var activePath = GetCurrentPhysicalFilePath();
        if (!string.IsNullOrWhiteSpace(activePath))
        {
            var currentDir = Path.GetDirectoryName(activePath);
            if (!string.IsNullOrWhiteSpace(currentDir))
            {
                return currentDir;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private void CreateRegressionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingRegressionRequest == null)
        {
            MessageBox.Show(
                this,
                "No valid property failure CI payload is currently available in output.",
                "Create Regression",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var content = PropertyRegressionArtifactSupport.BuildArtifactContent(_pendingRegressionRequest);
            var preferredPath = ResolveRegressionOutputPath(_pendingRegressionRequest);
            var outputPath = PropertyRegressionArtifactSupport.ResolveCollisionSafePath(preferredPath, content);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
            File.WriteAllText(outputPath, content);

            OpenFileAndIncludedDocuments(outputPath);
            MessageBox.Show(
                this,
                $"Regression created:\n{outputPath}",
                "Create Regression",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to create regression artifact.\n{ex.Message}",
                "Create Regression",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var sourceForExecution = GetSourceForExecution(activeDocument);
        var source = sourceForExecution.Source;
        var input = ProgramInputTextBox.Text;

        if (IsFullStackSource(source))
        {
            var runChoice = ShowFullStackRunDialog();
            if (runChoice == null)
            {
                return;
            }

            if (runChoice == FullStackRunChoice.ClientPreview)
            {
                SetOutputText("Opening the client target in the Web Preview panel...");
                SwitchToTab("output");
                await PreviewCurrentDocumentAsync();
                return;
            }

            StartFullStackRun(source, sourceForExecution.SourceFilePath, runChoice == FullStackRunChoice.FullStack);
            return;
        }

        if (JsBrowserApiDetector.UsesBrowserHost(source))
        {
            SetOutputText("Opening the JavaScript program in the Web Preview panel...");
            SwitchToTab("output");
            await PreviewCurrentDocumentAsync();
            return;
        }
        
        // Clear any debugger line highlight for a normal run
        ClearCurrentLineHighlight();
        
        // Do not clear tool calls log here so Edit mode tool calls persist when user then runs code
        UpdateToolCallsDisplay();
        
        // Cancel any previous run
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        
        // Create new cancellation token for this run
        _runCancellation = new CancellationTokenSource();
        var token = _runCancellation.Token;
        
        SetOutputText(""); // Clear output at start
        
        // Run in a separate task to allow cancellation
        _runTask = Task.Run(async () =>
        {
            try
            {
                var fileName = sourceForExecution.SourceFilePath;
                var result = await _executionService.ExecuteAsync(source, input, fileName);
                
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        SetOutputText($"{result.Output}\n\nError: {result.Error}", isError: true);
                    }
                    else
                    {
                        SetOutputText(result.Output);
                    }
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    SetOutputText(_executionService.GetCurrentOutput() + "\n\nExecution cancelled by user.");
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    SetOutputText($"Error: {ex.Message}", isError: true);
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    _runTask = null;
                    _runCancellation?.Dispose();
                    _runCancellation = null;
                    UpdateButtonStates();
                });
            }
        }, token);
        
        UpdateButtonStates();
    }

    private void StartFullStackRun(string source, string sourcePath, bool openClientPreview)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            MessageBox.Show(
                this,
                "Save the current full-stack MALDA file before running it.",
                "Run Full-Stack App",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ClearCurrentLineHighlight();
        UpdateToolCallsDisplay();

        _runCancellation?.Cancel();
        KillActiveRunProcess();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var token = _runCancellation.Token;

        SetOutputText("Compiling server target...");
        SwitchToTab("output");

        _runTask = Task.Run(async () =>
        {
            var output = new StringBuilder();

            void AppendOutput(string text, bool isError = false)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                output.AppendLine(text);
                Dispatcher.Invoke(() =>
                {
                    SetOutputText(output.ToString(), isError);
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "malda-fullstack-run", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                var outputPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(sourcePath) + ".server.exe");
                var webDirectory = Path.Combine(tempDir, "web");
                Directory.CreateDirectory(webDirectory);
                var clientScriptPath = Path.Combine(webDirectory, Path.GetFileNameWithoutExtension(sourcePath) + ".js");

                AppendOutput("Compiling server target with @server/@shared partitioning...");
                var result = await _compilerService.CompileAsync(
                    sourcePath,
                    outputPath,
                    Compiler.CompilationMode.TranspileToCSharp,
                    includeLLamaSharp: false,
                    cancellationToken: token);

                if (!result.Success)
                {
                    AppendOutput(result.ErrorMessage ?? "Compilation failed.", isError: true);
                    return;
                }

                var executablePath = result.OutputPath ?? outputPath;
                AppendOutput($"Server target compiled: {executablePath}");

                AppendOutput("Compiling client target into the server web root...");
                var clientResult = await _compilerService.CompileAsync(
                    sourcePath,
                    clientScriptPath,
                    Compiler.CompilationMode.JavaScript,
                    includeLLamaSharp: false,
                    cancellationToken: token);

                if (!clientResult.Success)
                {
                    AppendOutput(clientResult.ErrorMessage ?? "Client compilation failed.", isError: true);
                    return;
                }

                AppendOutput($"Client distribution generated in: {webDirectory}");

                var workingDirectory = FindRepoRoot();
                if (string.IsNullOrWhiteSpace(workingDirectory))
                {
                    workingDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };
                process.StartInfo.Environment["MALDA_WEB_DIRECTORY"] = webDirectory;

                if (!process.Start())
                {
                    AppendOutput("Failed to start the compiled server process.", isError: true);
                    return;
                }

                lock (_activeRunProcessLock)
                {
                    _activeRunProcess = process;
                }

                AppendOutput($"Server process started (PID {process.Id}). Press Stop to terminate it.");

                if (openClientPreview)
                {
                    var serverUrl = $"http://localhost:{ExtractFullStackHttpPort(source)}/";
                    AppendOutput($"Opening client served by the app server: {serverUrl}");
                    var previewOperation = Dispatcher.InvokeAsync(() => OpenUriInWebUiPanelAsync(new Uri(serverUrl), serverUrl, switchToTab: true, ensureUiHost: false));
                    await await previewOperation.Task;
                }

                var stdoutTask = ReadRunProcessStreamAsync(process.StandardOutput, line => AppendOutput(line), token);
                var stderrTask = ReadRunProcessStreamAsync(process.StandardError, line => AppendOutput(line, isError: true), token);

                try
                {
                    await process.WaitForExitAsync(token);
                    await Task.WhenAll(stdoutTask, stderrTask);
                }
                catch (OperationCanceledException)
                {
                    KillActiveRunProcess();
                    AppendOutput("Server process stopped.");
                    throw;
                }

                if (process.ExitCode != 0)
                {
                    AppendOutput($"Server process exited with code {process.ExitCode}.", isError: true);
                }
                else
                {
                    AppendOutput("Server process exited.");
                }
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    SetOutputText(output + "\nExecution cancelled by user.");
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }
            catch (Exception ex)
            {
                AppendOutput($"Error: {ex.Message}", isError: true);
            }
            finally
            {
                KillActiveRunProcess();
                Dispatcher.Invoke(() =>
                {
                    _runTask = null;
                    _runCancellation?.Dispose();
                    _runCancellation = null;
                    UpdateButtonStates();
                });
            }
        }, token);

        UpdateButtonStates();
    }

    private static async Task ReadRunProcessStreamAsync(TextReader reader, Action<string> appendOutput, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }

            appendOutput(line);
        }
    }

    private void KillActiveRunProcess()
    {
        Process? process;
        lock (_activeRunProcessLock)
        {
            process = _activeRunProcess;
            _activeRunProcess = null;
        }

        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup; process may have exited naturally.
        }
        finally
        {
            process.Dispose();
        }
    }

    private FullStackRunChoice? ShowFullStackRunDialog()
    {
        var dialog = new Window
        {
            Title = "Run Full-Stack MALDA App",
            Width = 520,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var fullStackRadio = new RadioButton
        {
            GroupName = "FullStackRunMode",
            Content = "Run full stack - start server and open client preview",
            IsChecked = true,
            Margin = new Thickness(16, 12, 16, 6)
        };

        var serverRadio = new RadioButton
        {
            GroupName = "FullStackRunMode",
            Content = "Run server only - compile @server/@shared target",
            Margin = new Thickness(16, 0, 16, 6)
        };

        var clientRadio = new RadioButton
        {
            GroupName = "FullStackRunMode",
            Content = "Preview client only - transpile @client/@shared target",
            Margin = new Thickness(16, 0, 16, 12)
        };

        var info = new TextBlock
        {
            Text = "This source contains both server and client target decorators, so it cannot be run as a single interpreter script.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 4)
        };

        var okButton = new Button
        {
            Content = "Run",
            Width = 90,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 16),
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 16),
            IsCancel = true
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        var panel = new StackPanel();
        panel.Children.Add(info);
        panel.Children.Add(fullStackRadio);
        panel.Children.Add(serverRadio);
        panel.Children.Add(clientRadio);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        FullStackRunChoice? choice = null;
        okButton.Click += (_, _) =>
        {
            choice = clientRadio.IsChecked == true
                ? FullStackRunChoice.ClientPreview
                : serverRadio.IsChecked == true
                    ? FullStackRunChoice.Server
                    : FullStackRunChoice.FullStack;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            choice = null;
            dialog.Close();
        };

        dialog.ShowDialog();
        return choice;
    }

    private static bool IsFullStackSource(string source)
    {
        return FullStackSourceInspector.IsFullStackSource(source);
    }

    private static int ExtractFullStackHttpPort(string source)
    {
        return FullStackSourceInspector.ExtractHttpPort(source, 8090);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopActiveExecution();
    }

    private void StopActiveExecution()
    {
        // Stop debug execution if running
        _debuggerHook?.Stop();
        _debugCancellation?.Cancel();
        _debuggerService.Stop();
        _debuggerHook = null;
        _debugTask = null;
        _debugCancellation?.Dispose();
        _debugCancellation = null;
        _ = StopJsDebuggerAsync();
        
        // Stop regular run execution if running
        _runCancellation?.Cancel();
        KillActiveRunProcess();
        _runTask = null;
        _runCancellation?.Dispose();
        _runCancellation = null;
        
        ClearCurrentLineHighlight();
        UpdateButtonStates();
    }

    private async void CompileButton_Click(object sender, RoutedEventArgs e)
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var sourceForExecution = GetSourceForExecution(activeDocument);
        var source = sourceForExecution.Source;
        if (string.IsNullOrWhiteSpace(source))
        {
            MessageBox.Show("Please enter some code to compile.", "No Code", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Show compilation mode selection dialog
        var modeDialog = new Window
        {
            Title = "Compilation Options",
            Width = 450,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var interpreterRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Interpreter executable - Embed source and run via interpreter runtime",
            IsChecked = true,
            Margin = new Thickness(10, 10, 10, 6)
        };

        var transpileRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Transpile to C# executable - Convert to C# and compile to native executable",
            Margin = new Thickness(10, 10, 10, 6)
        };

        var dllRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Transpile to DLL - Convert to C# and compile as .NET library",
            Margin = new Thickness(10, 0, 10, 6)
        };

        var javascriptRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Transpile to JavaScript - Generate browser-ready JavaScript (.js)",
            Margin = new Thickness(10, 0, 10, 10)
        };

        var pwaRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Transpile to PWA - Generate a Progressive Web App output directory",
            Margin = new Thickness(10, 0, 10, 10)
        };

        var executableGroup = new GroupBox
        {
            Header = "Executable Output",
            Margin = new Thickness(20, 20, 20, 10),
            Content = new StackPanel
            {
                Children =
                {
                    interpreterRadio
                }
            }
        };

        var transpileGroup = new GroupBox
        {
            Header = "Transpiled Output",
            Margin = new Thickness(20, 0, 20, 10),
            Content = new StackPanel
            {
                Children =
                {
                    transpileRadio,
                    dllRadio,
                    javascriptRadio,
                    pwaRadio
                }
            }
        };

        var includeLLamaSharpCheckbox = new CheckBox
        {
            Content = "Include LLamaSharp and its dependencies",
            Margin = new Thickness(20, 0, 20, 20),
            IsChecked = false
        };
        includeLLamaSharpCheckbox.ToolTip = "Only applicable to executable and DLL outputs. Not used for JavaScript or PWA outputs.";

        var okButton = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 25,
            Margin = new Thickness(0, 0, 10, 20),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 25,
            Margin = new Thickness(0, 0, 20, 20),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 0)
        };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        void UpdateLlamaOptionAvailability()
        {
            var isBrowserOutputMode = javascriptRadio.IsChecked == true || pwaRadio.IsChecked == true;
            includeLLamaSharpCheckbox.IsEnabled = !isBrowserOutputMode;
            if (isBrowserOutputMode)
            {
                includeLLamaSharpCheckbox.IsChecked = false;
            }
        }

        interpreterRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        transpileRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        dllRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        javascriptRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        pwaRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        UpdateLlamaOptionAvailability();

        var mainPanel = new StackPanel();
        mainPanel.Children.Add(executableGroup);
        mainPanel.Children.Add(transpileGroup);
        mainPanel.Children.Add(includeLLamaSharpCheckbox);
        mainPanel.Children.Add(buttonPanel);

        modeDialog.Content = mainPanel;

        bool? dialogResult = null;
        okButton.Click += (s, args) => { dialogResult = true; modeDialog.Close(); };
        cancelButton.Click += (s, args) => { dialogResult = false; modeDialog.Close(); };

        modeDialog.ShowDialog();

        if (dialogResult != true)
        {
            return;
        }

        Compiler.CompilationMode compilationMode;
        if (interpreterRadio.IsChecked == true)
        {
            compilationMode = Compiler.CompilationMode.Interpreter;
        }
        else if (javascriptRadio.IsChecked == true)
        {
            compilationMode = Compiler.CompilationMode.JavaScript;
        }
        else if (pwaRadio.IsChecked == true)
        {
            compilationMode = Compiler.CompilationMode.PWA;
        }
        else if (dllRadio.IsChecked == true)
        {
            compilationMode = Compiler.CompilationMode.TranspileToDll;
        }
        else
        {
            compilationMode = Compiler.CompilationMode.TranspileToCSharp;
        }
        
        var includeLLamaSharp = includeLLamaSharpCheckbox.IsChecked == true;

        // Get output path from user
        string outputPath;
        if (compilationMode == Compiler.CompilationMode.PWA)
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Select PWA Output Folder"
            };

            if (folderDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(folderDialog.FolderName))
            {
                return;
            }

            outputPath = folderDialog.FolderName;
        }
        else
        {
            var defaultExt = compilationMode switch
            {
                Compiler.CompilationMode.TranspileToDll => "dll",
                Compiler.CompilationMode.JavaScript => "js",
                _ => "zip"
            };
            var defaultFileName = compilationMode switch
            {
                Compiler.CompilationMode.TranspileToDll => "program.dll",
                Compiler.CompilationMode.JavaScript => "program.js",
                _ => "program.zip"
            };
            var filter = compilationMode switch
            {
                Compiler.CompilationMode.TranspileToDll => "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                Compiler.CompilationMode.JavaScript => "JavaScript Files (*.js)|*.js|All Files (*.*)|*.*",
                _ => "Zip Files (*.zip)|*.zip|Executable Files (*.exe)|*.exe|All Files (*.*)|*.*"
            };

            var saveDialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = defaultFileName
            };

            if (saveDialog.ShowDialog() != true)
            {
                return;
            }

            outputPath = saveDialog.FileName;
        }

        var isJavaScript = compilationMode == Compiler.CompilationMode.JavaScript;
        var isPwa = compilationMode == Compiler.CompilationMode.PWA;
        var isZip = !isJavaScript && outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var isDll = compilationMode == Compiler.CompilationMode.TranspileToDll;
        var tempExePath = isPwa
            ? outputPath
            : isZip || isDll
            ? Path.Combine(Path.GetTempPath(), $"spl_{Guid.NewGuid()}.{(isDll ? "dll" : "exe")}")
            : outputPath;

        // Show progress
        var progressWindow = new Window
        {
            Title = "Compiling...",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var progressText = new TextBlock
        {
            Margin = new Thickness(20),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(33, 33, 33))
        };

        var progressBar = new ProgressBar
        {
            Margin = new Thickness(20, 0, 20, 20),
            Height = 20,
            IsIndeterminate = true
        };

        var stackPanel = new StackPanel();
        stackPanel.Children.Add(progressText);
        stackPanel.Children.Add(progressBar);
        progressWindow.Content = stackPanel;

        _compilerService.OnProgress += (progress) =>
        {
            Dispatcher.Invoke(() =>
            {
                progressText.Text = progress.Message;
                progressBar.Value = progress.Percentage;
                progressBar.IsIndeterminate = progress.Percentage < 100;
            });
        };

        progressWindow.Show();

        try
        {
            Compiler.Compiler.CompilationResult result;
            
            // Check if we have a file path, otherwise use temp file
            if (sourceForExecution.UsesPhysicalFileOnDisk && File.Exists(sourceForExecution.SourceFilePath))
            {
                result = await _compilerService.CompileAsync(sourceForExecution.SourceFilePath, tempExePath, compilationMode, includeLLamaSharp);
            }
            else
            {
                result = await _compilerService.CompileFromTextAsync(source, tempExePath, compilationMode, includeLLamaSharp);
            }

            progressWindow.Close();

            if (result.Success)
            {
                string finalPath = outputPath;

                if (compilationMode == Compiler.CompilationMode.PWA)
                {
                    MessageBox.Show($"Compilation successful!\n\nPWA saved to:\n{finalPath}", "Compilation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (compilationMode == Compiler.CompilationMode.JavaScript)
                {
                    var distributionDirectory = Path.GetDirectoryName(Path.GetFullPath(finalPath)) ?? Directory.GetCurrentDirectory();
                    MessageBox.Show(
                        $"Compilation successful!\n\nJavaScript distribution saved to:\n{distributionDirectory}\n\nOpen index.html to run the app.",
                        "Compilation Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                
                // For DLL mode, just copy the DLL to the output path
                if (compilationMode == Compiler.CompilationMode.TranspileToDll && result.OutputPath != null)
                {
                    if (File.Exists(result.OutputPath))
                    {
                        if (File.Exists(outputPath))
                            File.Delete(outputPath);
                        File.Copy(result.OutputPath, outputPath, true);
                        finalPath = outputPath;
                        // Clean up temp DLL
                        try
                        {
                            if (result.OutputPath != outputPath)
                                File.Delete(result.OutputPath);
                        }
                        catch { /* ignore */ }
                    }
                    MessageBox.Show($"Compilation successful!\n\nDLL saved to:\n{finalPath}", "Compilation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // If user requested zip, create it with dependencies
                if (isZip && result.OutputPath != null)
                {
                    var zipPath = _compilerService.CreateZipWithDependencies(result.OutputPath, outputPath);
                    if (zipPath != null)
                    {
                        finalPath = zipPath;
                        // Clean up temp exe
                        try
                        {
                            if (File.Exists(result.OutputPath) && result.OutputPath != outputPath)
                                File.Delete(result.OutputPath);
                            var dllPath = Path.Combine(Path.GetDirectoryName(result.OutputPath) ?? "", "MaldaLang.dll");
                            if (File.Exists(dllPath))
                                File.Delete(dllPath);
                        }
                        catch { }
                    }
                    else
                    {
                        // Zip creation failed, just copy exe
                        if (File.Exists(result.OutputPath))
                        {
                            File.Copy(result.OutputPath, outputPath, true);
                        }
                    }
                }
                else if (isZip && result.OutputPath != null)
                {
                    // User wanted zip but we'll just copy the exe
                    File.Copy(result.OutputPath, outputPath, true);
                }

                MessageBox.Show(
                    $"Compilation successful!\n\nOutput saved to:\n{finalPath}",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(
                    $"Compilation failed:\n\n{result.ErrorMessage}",
                    "Compilation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            MessageBox.Show(
                $"An error occurred during compilation:\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async void PreviewWebButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await PreviewCurrentDocumentAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
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

        var previewUri = BuildWebPreviewHostUri(hostPath, repoRoot, scriptPath, Path.GetFileNameWithoutExtension(activePath));
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

    private static Uri BuildWebPreviewHostUri(string hostPath, string repoRoot, string scriptPath, string title)
    {
        var hostDirectory = Path.GetDirectoryName(Path.GetFullPath(hostPath))
            ?? throw new InvalidOperationException("Could not resolve the web preview host directory.");
        var relativeScriptPath = Path.GetRelativePath(hostDirectory, scriptPath).Replace('\\', '/');
        var baseUri = new Uri(Path.GetFullPath(hostPath));
        var query = $"?script={Uri.EscapeDataString(relativeScriptPath)}&title={Uri.EscapeDataString(title)}";

        // Generated host lives under .malda-preview/; runtime assets stay at repo root.
        if (!string.Equals(Path.GetFullPath(hostDirectory), Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase))
        {
            query +=
                "&runtime=" + Uri.EscapeDataString("../Examples/Web/wwwroot/malda-js-runtime.js") +
                "&three=" + Uri.EscapeDataString("../Examples/Web/wwwroot/vendor/three.min.js");
        }

        return new Uri(baseUri.AbsoluteUri + query);
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
                entry: params.get("entry") || "auto"
              };

              var statusElement = document.getElementById("status");
              document.title = config.title;

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

    private void ClearOutputButton_Click(object sender, RoutedEventArgs e)
    {
        SetOutputText("");
        ProgramInputTextBox.Text = "";
        _toolCallLogService.Clear();
        UpdateToolCallsDisplay();
    }
}
