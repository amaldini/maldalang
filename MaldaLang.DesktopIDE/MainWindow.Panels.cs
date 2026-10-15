// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void OutputTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("output");
    }

    private void DebugTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("debug");
    }

    private void ErrorsTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("errors");
    }

    private void SearchTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("search");
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }
    
    private void ToolCallsTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("toolcalls");
        UpdateToolCallsDisplay();
    }
    
    private void AITabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("ai");
        UpdateAIChatPanelContext();
    }

    private void WebUITabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("webui");
    }

    private void ManualTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("manual");
        if (string.IsNullOrEmpty(_manualCurrentFile))
        {
            _ = ShowReferenceManualPageAsync("index.html");
        }
    }
    
    private void ClearToolCallsButton_Click(object sender, RoutedEventArgs e)
    {
        _toolCallLogService.Clear();
        UpdateToolCallsDisplay();
    }

    private void SwitchToTab(string tab)
    {
        if (_maximizedSidebarTab != null && IsSidePanel(tab) && _maximizedSidebarTab != tab)
        {
            SetSidebarPanelMaximized(_maximizedSidebarTab, false);
        }

        _activeTab = tab;
        if (IsSidePanel(tab))
        {
            _sideTab = tab;
            AIChatPanel.Visibility = tab == "ai" ? Visibility.Visible : Visibility.Collapsed;
            WebUIPanel.Visibility = tab == "webui" ? Visibility.Visible : Visibility.Collapsed;
            ManualPanel.Visibility = tab == "manual" ? Visibility.Visible : Visibility.Collapsed;
            if (_outputOnRight)
            {
                OutputPanel.Visibility = tab == "output" ? Visibility.Visible : Visibility.Collapsed;
            }

            if (tab == "ai")
            {
                UpdateAIChatPanelContext();
            }
        }
        else
        {
            _bottomTab = tab;
            if (!_outputOnRight)
            {
                OutputPanel.Visibility = tab == "output" ? Visibility.Visible : Visibility.Collapsed;
            }

            DebugPanel.Visibility = tab == "debug" ? Visibility.Visible : Visibility.Collapsed;
            ToolCallsPanel.Visibility = tab == "toolcalls" ? Visibility.Visible : Visibility.Collapsed;
            ErrorsPanel.Visibility = tab == "errors" ? Visibility.Visible : Visibility.Collapsed;
            SearchPanel.Visibility = tab == "search" ? Visibility.Visible : Visibility.Collapsed;
        }
        
        // Update button styles using theme colors
        UpdateTabButtonBackgrounds();
        
        // Update View menu check states
        UpdateViewMenuStates();
    }
    
    private void UpdateViewMenuStates()
    {
        // Find the View menu item and update its children
        if (MainMenu == null) return;
        
        foreach (var topLevelItem in MainMenu.Items)
        {
            if (topLevelItem is not MenuItem menuItem) continue;
            
            var header = menuItem.Header?.ToString()?.Replace("_", "");
            if (header == "View")
            {
                foreach (var childItem in menuItem.Items)
                {
                    if (childItem is not MenuItem childMenuItem) continue;
                    
                    var itemHeader = childMenuItem.Header?.ToString()?.Replace("_", "");
                    if (itemHeader == "Show Syntax Panel")
                        childMenuItem.IsChecked = _isSyntaxPanelVisible;
                    else if (itemHeader == "Show Output Panel")
                        childMenuItem.IsChecked = _outputOnRight ? _sideTab == "output" : _bottomTab == "output";
                    else if (itemHeader == "Output on the Right")
                        childMenuItem.IsChecked = _outputOnRight;
                    else if (itemHeader == "Show Debug Panel")
                        childMenuItem.IsChecked = _bottomTab == "debug";
                    else if (itemHeader == "Show Tool Calls Panel")
                        childMenuItem.IsChecked = _bottomTab == "toolcalls";
                    else if (itemHeader == "Show Problems Panel")
                        childMenuItem.IsChecked = _bottomTab == "errors";
                    else if (itemHeader == "Show Search Panel")
                        childMenuItem.IsChecked = _bottomTab == "search";
                    else if (itemHeader == "Show AI Panel")
                        childMenuItem.IsChecked = _sideTab == "ai";
                    else if (itemHeader == "Show Web UI Panel")
                        childMenuItem.IsChecked = _sideTab == "webui";
                    else if (itemHeader == "Show Manual")
                        childMenuItem.IsChecked = _sideTab == "manual";
                    else if (itemHeader == "Maximize AI Panel")
                        childMenuItem.IsChecked = IsAiPanelMaximized;
                    else if (itemHeader == "Maximize Web Preview")
                        childMenuItem.IsChecked = IsWebUiMaximized;
                    else if (itemHeader == "Type Errors as Errors")
                        childMenuItem.IsChecked = _typeAnalysisSettingsService.TypeErrors;
                }
                break;
            }
        }
    }

    private void ViewToggleTypeErrors_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
            return;

        _typeAnalysisSettingsService.SetTypeErrors(menuItem.IsChecked);
        UpdateViewMenuStates();
        UpdateDiagnostics();
    }
    
    private void UpdateTabButtonBackgrounds()
    {
        var theme = _themeService.CurrentTheme;
        var activeBrush = new SolidColorBrush(theme.TabActiveBackground);
        var inactiveBrush = new SolidColorBrush(theme.TabBackground);
        var accentBrush = new SolidColorBrush(theme.DebugAccent);
        var transparent = Brushes.Transparent;

        ApplySidebarTabChrome(OutputTabButton, _outputOnRight ? _sideTab == "output" : _bottomTab == "output", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(DebugTabButton, _bottomTab == "debug", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(ToolCallsTabButton, _bottomTab == "toolcalls", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(ErrorsTabButton, _bottomTab == "errors", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(SearchTabButton, _bottomTab == "search", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(AITabButton, _sideTab == "ai", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(WebUITabButton, _sideTab == "webui", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(ManualTabButton, _sideTab == "manual", activeBrush, inactiveBrush, accentBrush, transparent);
        UpdateViewMenuStates();
        RefreshDocumentTabs();
    }

    private static void ApplySidebarTabChrome(Button button, bool isActive, Brush activeBrush, Brush inactiveBrush, Brush accentBrush, Brush transparent)
    {
        button.Background = isActive ? activeBrush : inactiveBrush;
        button.BorderBrush = isActive ? accentBrush : transparent;
        button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
    }
    
    private void UpdateAIChatPanelContext()
    {
        if (_aiChatPanel != null)
        {
            _aiChatPanel.CurrentCode = GetCurrentCode();
            _aiChatPanel.CursorPosition = GetCursorPosition();
            _aiChatPanel.SelectedCode = GetSelectedText();
            var activeDocument = GetActiveDocument();
            var (source, sourceKey) = GetSourceForAnalysis(activeDocument);
            _aiChatPanel.Errors = _languageService.GetDiagnostics(
                    source,
                    sourceKey,
                    strictTypesOptions: _typeAnalysisSettingsService.ToOptions())
                .Where(MaldaLang.IDE.DiagnosticSourceFiles.IsInDocument)
                .ToList();
        }
    }
    
    private void ApplyAICodeChange(string newCode)
    {
        SetEditorTextUndoable(newCode);
        _fileService.SetContent(newCode);
        // Update diagnostics
        _diagnosticsTimer?.Stop();
        _diagnosticsTimer?.Start();
        // Update AI chat panel context
        UpdateAIChatPanelContext();
    }

    // View Menu
    private void ViewThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name })
        {
            _themeService.SetTheme(name);
        }
    }
    
    private void ViewToggleOutputPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                OutputTabButton_Click(sender, e);
            }
            else
            {
                if (_outputOnRight)
                {
                    if (_sideTab == "output")
                    {
                        SwitchToTab("ai");
                    }
                }
                else if (_bottomTab == "output")
                {
                    SwitchToTab("errors");
                }
            }
            UpdateViewMenuStates();
        }
    }

    private void ViewToggleOutputOnRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        ApplyOutputDock(menuItem.IsChecked, refreshChrome: true);
        _layoutSettings.SetOutputOnRight(menuItem.IsChecked);
    }
    
    private void ViewToggleDebugPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                DebugTabButton_Click(sender, e);
            }
            else
            {
                if (_bottomTab == "debug")
                {
                    SwitchToTab("output");
                }
            }
            UpdateViewMenuStates();
        }
    }
    
    private void ViewToggleToolCallsPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                ToolCallsTabButton_Click(sender, e);
            }
            else
            {
                if (_bottomTab == "toolcalls")
                {
                    SwitchToTab("output");
                }
            }
            UpdateViewMenuStates();
        }
    }
    
    private void ViewToggleErrorsPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                ErrorsTabButton_Click(sender, e);
            }
            else
            {
                if (_bottomTab == "errors")
                {
                    SwitchToTab("output");
                }
            }
            UpdateViewMenuStates();
        }
    }

    private void ViewToggleSearchPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                SearchTabButton_Click(sender, e);
            }
            else if (_bottomTab == "search")
            {
                SwitchToTab("output");
            }

            UpdateViewMenuStates();
        }
    }
    
    private void ViewToggleAIPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                AITabButton_Click(sender, e);
            }
            else
            {
                if (_sideTab == "ai")
                {
                    SwitchToTab("webui");
                }
            }
            UpdateViewMenuStates();
        }
    }

    private void ViewToggleWebUIPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                WebUITabButton_Click(sender, e);
            }
            else
            {
                if (_sideTab == "webui")
                {
                    SwitchToTab("ai");
                }
            }
            UpdateViewMenuStates();
        }
    }

    private void ViewToggleManualPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                ManualTabButton_Click(sender, e);
            }
            else if (_sideTab == "manual")
            {
                SwitchToTab(_outputOnRight ? "output" : "ai");
            }

            UpdateViewMenuStates();
        }
    }
}
