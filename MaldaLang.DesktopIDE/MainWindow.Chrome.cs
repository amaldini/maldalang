// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Windows;
using MaldaLang.IDE.Models;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void PlaceBottomPanels()
    {
        MoveToBottomPanel(DebugPanel);
        MoveToBottomPanel(ToolCallsPanel);
        MoveToBottomPanel(ErrorsPanel);
        MoveToBottomPanel(SearchPanel);
    }

    private void ApplyOutputDock(bool onRight, bool refreshChrome)
    {
        _outputOnRight = onRight;
        DetachFromParent(OutputTabButton);
        DetachFromParent(OutputPanel);

        if (onRight)
        {
            SidebarTabBar.Children.Insert(0, OutputTabButton);
            SidebarPanelContent.Children.Add(OutputPanel);
            if (_sideTab is not ("ai" or "webui"))
            {
                _sideTab = "output";
            }

            if (_bottomTab == "output")
            {
                _bottomTab = "errors";
            }
        }
        else
        {
            var index = BottomTabBar.Children.Count == 0 ? 0 : 1;
            BottomTabBar.Children.Insert(Math.Min(index, BottomTabBar.Children.Count), OutputTabButton);
            BottomPanelContent.Children.Add(OutputPanel);
            if (_sideTab == "output")
            {
                _sideTab = "ai";
            }

            _bottomTab = "output";
        }

        AIChatPanel.Visibility = _sideTab == "ai" ? Visibility.Visible : Visibility.Collapsed;
        WebUIPanel.Visibility = _sideTab == "webui" ? Visibility.Visible : Visibility.Collapsed;
        OutputPanel.Visibility = (_outputOnRight ? _sideTab == "output" : _bottomTab == "output")
            ? Visibility.Visible
            : Visibility.Collapsed;
        DebugPanel.Visibility = _bottomTab == "debug" ? Visibility.Visible : Visibility.Collapsed;
        ToolCallsPanel.Visibility = _bottomTab == "toolcalls" ? Visibility.Visible : Visibility.Collapsed;
        ErrorsPanel.Visibility = _bottomTab == "errors" ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = _bottomTab == "search" ? Visibility.Visible : Visibility.Collapsed;

        if (refreshChrome && _themeService != null)
        {
            UpdateTabButtonBackgrounds();
        }
    }

    private static void DetachFromParent(FrameworkElement element)
    {
        if (element.Parent is Panel parent)
        {
            parent.Children.Remove(element);
        }
    }

    private void MoveToBottomPanel(FrameworkElement element)
    {
        if (element.Parent is Panel parent)
        {
            parent.Children.Remove(element);
        }

        BottomPanelContent.Children.Add(element);
    }

    private bool IsSidePanel(string tab) =>
        tab is "ai" or "webui" || (_outputOnRight && tab == "output");

    private void UpdateWindowChrome()
    {
        var document = GetActiveDocument();
        if (document == null)
        {
            Title = "MALDA";
            StatusPathText.Text = "";
        }
        else
        {
            var name = GetDocumentDisplayName(document);
            Title = document.IsDirty ? $"{name}* — MALDA" : $"{name} — MALDA";
            StatusPathText.Text = GetPhysicalPath(document) ?? "Untitled";
        }

        if (CodeEditor?.Document != null)
        {
            var line = CodeEditor.TextArea.Caret.Line;
            var column = CodeEditor.TextArea.Caret.Column;
            StatusCaretText.Text = $"Ln {line}, Col {column}";
        }

        var debugging = _debuggerService?.State.IsRunning == true;
        var running = _runTask != null && !_runTask.IsCompleted;
        StatusModeText.Text = debugging ? "Debugging" : running ? "Running" : "Interpreter";
    }

    private void UpdateProblemsBadge(int errors, int warnings)
    {
        var total = errors + warnings;
        if (total <= 0)
        {
            ProblemsBadgeText.Visibility = Visibility.Collapsed;
            StatusDiagnosticsText.Text = "No problems";
            StatusDiagnosticsText.Foreground = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
            return;
        }

        ProblemsBadgeText.Visibility = Visibility.Visible;
        ProblemsBadgeText.Text = total.ToString();
        var parts = new List<string>();
        if (errors > 0)
        {
            parts.Add(errors == 1 ? "1 error" : $"{errors} errors");
        }

        if (warnings > 0)
        {
            parts.Add(warnings == 1 ? "1 warning" : $"{warnings} warnings");
        }

        StatusDiagnosticsText.Text = string.Join(", ", parts);
        StatusDiagnosticsText.Foreground = TryFindResource(errors > 0 ? "ErrorBrush" : "WarningBrush") as System.Windows.Media.Brush;
    }

    private void ViewCommandPalette_Click(object sender, RoutedEventArgs e)
    {
        ShowCommandPalette();
    }

    private void ShowCommandPalette()
    {
        var window = new CommandPaletteWindow(BuildPaletteCommands());
        window.Owner = this;
        DialogTheming.CopyChrome(this, window);
        if (window.ShowDialog() == true)
        {
            window.SelectedCommand?.Invoke();
        }
    }

    private void ShowKeyboardShortcuts()
    {
        var window = new KeyboardShortcutsWindow
        {
            Owner = this
        };
        DialogTheming.CopyChrome(this, window);
        window.ShowDialog();
    }

    private List<PaletteCommand> BuildPaletteCommands()
    {
        return
        [
            Command("New File", "Ctrl+N", () => FileNew_Click(this, new RoutedEventArgs())),
            Command("Open File", "Ctrl+O", () => FileOpen_Click(this, new RoutedEventArgs())),
            Command("Open Folder", "", () => FileOpenFolder_Click(this, new RoutedEventArgs())),
            Command("Save", "Ctrl+S", () => FileSave_Click(this, new RoutedEventArgs())),
            Command("Save As", "Ctrl+Shift+S", () => FileSaveAs_Click(this, new RoutedEventArgs())),
            Command("Find", "Ctrl+F", () => EditFind_Click(this, new RoutedEventArgs())),
            Command("Replace", "Ctrl+H", () => EditReplace_Click(this, new RoutedEventArgs())),
            Command("Format Document", "Ctrl+Alt+F", () => EditFormatDocument_Click(this, new RoutedEventArgs())),
            Command("Quick Fix", "Ctrl+.", () => EditQuickFix_Click(this, new RoutedEventArgs())),
            Command("Apply Autofixes", "", () => AutofixErrorsButton_Click(this, new RoutedEventArgs())),
            Command("Rename Symbol", "Ctrl+Alt+R", () => NavigateRenameSymbol_Click(this, new RoutedEventArgs())),
            Command("Go to Definition", "F12", () => NavigateGoToDefinition_Click(this, new RoutedEventArgs())),
            Command("Find References", "Shift+F12", () => NavigateFindReferences_Click(this, new RoutedEventArgs())),
            Command("Go to Symbol", "Ctrl+T", () => NavigateGoToSymbol_Click(this, new RoutedEventArgs())),
            Command("Run", "Ctrl+F5", () => RunButton_Click(this, new RoutedEventArgs())),
            Command("Debug", "F5", () => DebugButton_Click(this, new RoutedEventArgs())),
            Command("Stop", "Shift+F5", () => StopButton_Click(this, new RoutedEventArgs())),
            Command("Compile", "Ctrl+Shift+B", () => CompileButton_Click(this, new RoutedEventArgs())),
            Command("Preview Web", "F6", () => PreviewWebButton_Click(this, new RoutedEventArgs())),
            Command("Show Problems", "", () => SwitchToTab("errors")),
            Command("Show Output", "", () => SwitchToTab("output")),
            Command("Show Debug", "", () => SwitchToTab("debug")),
            Command("Show Search", "", () => SwitchToTab("search")),
            Command("Show Tool Calls", "", () => SwitchToTab("toolcalls")),
            Command("Show AI Panel", "", () => SwitchToTab("ai")),
            Command("Show Web UI", "", () => SwitchToTab("webui")),
            Command("Start With MALDA", "", () => StartWithMalda_Click(this, new RoutedEventArgs())),
            Command("Browse Examples", "", () => BrowseExamplesButton_Click(this, new RoutedEventArgs())),
            Command("Keyboard Shortcuts", "", ShowKeyboardShortcuts),
            Command("Reference Manual", "", () => HelpReferenceManual_Click(this, new RoutedEventArgs())),
            Command("About", "", () => HelpAbout_Click(this, new RoutedEventArgs()))
        ];
    }

    private static PaletteCommand Command(string title, string shortcut, Action invoke)
    {
        return new PaletteCommand
        {
            Title = title,
            Shortcut = shortcut,
            Invoke = invoke
        };
    }
}
