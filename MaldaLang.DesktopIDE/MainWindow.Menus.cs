// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.IO;
using Microsoft.Win32;
using System.Diagnostics;
using MaldaLang.TraceViewer;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void ViewOpenTrace_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "MALDA trace files (*.malda-trace.jsonl)|*.malda-trace.jsonl|All files (*.*)|*.*",
            Title = "Open Trace File"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var session = TraceViewerService.LoadTrace(dlg.FileName);
            var window = new Windows.TraceViewerWindow(_themeService, session);
            window.Owner = this;
            window.Show();
        }
        catch (Exception ex)
        {
            IdePromptWindow.Show(this, ex.Message, "Trace load failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    
    // Tools Menu
    private void ToolsConfigureMCPServers_Click(object sender, RoutedEventArgs e)
    {
        var window = new Windows.MCPServerConfigWindow(_mcpConfigService, _mcpConnectionService, _themeService);
        window.Owner = this;
        window.ShowDialog();
    }

    private void ToolsBrowseModels_Click(object sender, RoutedEventArgs e)
    {
        var window = new MaldaLang.DesktopIDE.Windows.ModelBrowserWindow(_themeService);
        window.ShowDialog();
    }

    private void ToolsPackageManager_Click(object sender, RoutedEventArgs e)
    {
        var window = new MaldaLang.DesktopIDE.Windows.PackageBrowserWindow(_themeService);
        window.Owner = this;
        window.ShowDialog();
    }

    private void ToolsLoadExample_Click(object sender, RoutedEventArgs e)
    {
        BrowseExamplesButton_Click(sender, e);
    }
    
    // Help Menu
    private void HelpShowcase_Click(object sender, RoutedEventArgs e)
    {
        var repoRoot = FindRepoRoot();
        var playlist = string.IsNullOrWhiteSpace(repoRoot)
            ? null
            : Path.Combine(repoRoot, "scripts", "showcase", "playlist.json");
        if (playlist == null || !File.Exists(playlist))
        {
            IdePromptWindow.Show(
                this,
                "The showcase playlist was not found next to this installation.",
                "Showcase",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var exe = System.Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            IdePromptWindow.Show(
                this,
                "Could not find this IDE executable to launch the showcase.",
                "Showcase",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var handshake = Path.Combine(Path.GetTempPath(), "malda-showcase-" + Guid.NewGuid().ToString("N"));
            var start = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = repoRoot
            };
            start.ArgumentList.Add("--demo");
            start.ArgumentList.Add(playlist);
            start.ArgumentList.Add("--handshake");
            start.ArgumentList.Add(handshake);
            start.ArgumentList.Add("--autostart");
            Process.Start(start);
        }
        catch (Exception ex)
        {
            IdePromptWindow.Show(
                this,
                "Could not launch the showcase. " + ex.Message,
                "Showcase",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void HelpUpdateInstallation_Click(object sender, RoutedEventArgs e)
    {
        var unsaved = _openDocuments.Values
            .Where(document => document.IsDirty)
            .Select(GetDocumentDisplayName)
            .ToList();
        var window = new Windows.InstallationUpdateWindow(_themeService, unsaved)
        {
            Owner = this
        };
        if (window.ShowDialog() == true && window.RestartRequired)
        {
            Application.Current.Shutdown();
        }
    }

    private void HelpAbout_Click(object sender, RoutedEventArgs e)
    {
        IdePromptWindow.Show(
            "MALDA: The AI-First Programming Language - Desktop IDE\n\n" +
            "Version 1.0\n\n" +
            "A desktop IDE for the Multi Agent Language with Development Automation (MALDA).\n" +
            "Built with WPF and AvalonEdit.\n\n" +
            "(c) 2026 - Andrea Maldini",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
    
    private void HelpDocumentation_Click(object sender, RoutedEventArgs e)
    {
        if (TryOpenReferenceManual())
        {
            return;
        }

        OpenDocumentationUrl();
    }

    private async void HelpReferenceManual_Click(object sender, RoutedEventArgs e)
    {
        if (await ShowReferenceManualPageAsync("index.html"))
        {
            return;
        }

        IdePromptWindow.Show(
            this,
            "Could not find the local reference manual. Opening online documentation instead.",
            "Reference Manual",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        OpenDocumentationUrl();
    }

    private bool TryOpenReferenceManual()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return false;
            }

            var manualPath = Path.Combine(repoRoot, "ReferenceManual", "index.html");
            if (!File.Exists(manualPath))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = manualPath,
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private const string ProjectRepositoryUrl = "https://github.com/amaldini/maldalang";

    private void OpenDocumentationUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ProjectRepositoryUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            IdePromptWindow.Show("Could not open documentation. Please visit the project repository manually.",
                "Documentation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    
    private void HelpShortcuts_Click(object sender, RoutedEventArgs e)
    {
        ShowKeyboardShortcuts();
    }
}
