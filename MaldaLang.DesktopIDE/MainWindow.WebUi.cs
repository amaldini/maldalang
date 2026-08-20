// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isWebUiMaximized)
        {
            SetWebUiMaximized(false);
            e.Handled = true;
        }
    }

    private void WebUiMaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWebUiMaximized();
    }

    private void WebUiResetButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ResetWebUiPreviewAsync();
    }

    private void ViewToggleMaximizeWebPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        SetWebUiMaximized(menuItem.IsChecked);
    }

    private void ViewResetWebPreview_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("webui");
        _ = ResetWebUiPreviewAsync();
    }

    private void ToggleWebUiMaximized()
    {
        SetWebUiMaximized(!_isWebUiMaximized);
    }

    private void SetWebUiMaximized(bool maximized)
    {
        if (_isWebUiMaximized == maximized)
        {
            UpdateWebUiMaximizeChrome();
            return;
        }

        if (maximized)
        {
            SwitchToTab("webui");
            CaptureWebUiDefaultLayout();
            ApplyWebUiMaximizedLayout();
            _isWebUiMaximized = true;
        }
        else
        {
            _isWebUiMaximized = false;
            RestoreWebUiDefaultLayout();
        }

        UpdateWebUiMaximizeChrome();
        UpdateViewMenuStates();
    }

    private void CaptureWebUiDefaultLayout()
    {
        if (_isSyntaxPanelVisible && SyntaxPanelColumn.Width.Value > 0)
        {
            _syntaxPanelPreviousWidth = SyntaxPanelColumn.Width;
        }

        _syntaxPanelColumnMinWidthBeforeMaximize = SyntaxPanelColumn.MinWidth;
        _sidebarColumnMinWidthBeforeMaximize = SidebarColumn.MinWidth;
        _leftSplitterColumnBeforeMaximize = LeftSplitterColumn.Width;
        _rightSplitterColumnBeforeMaximize = RightSplitterColumn.Width;
        _editorColumnBeforeMaximize = EditorColumn.Width;
        _sidebarColumnBeforeMaximize = SidebarColumn.Width;
        _mainMenuVisibilityBeforeMaximize = MainMenu.Visibility;
        _mainToolbarVisibilityBeforeMaximize = MainToolbar.Visibility;
        _editorPaneVisibilityBeforeMaximize = EditorPane.Visibility;
        _sidebarTabBarVisibilityBeforeMaximize = SidebarTabBar.Visibility;
        _sidebarSplitterVisibilityBeforeMaximize = SidebarSplitter.Visibility;
    }

    private void ApplyWebUiMaximizedLayout()
    {
        MainMenu.Visibility = Visibility.Collapsed;
        MainToolbar.Visibility = Visibility.Collapsed;
        SyntaxPanel.Visibility = Visibility.Collapsed;
        SyntaxPanelSplitter.Visibility = Visibility.Collapsed;
        EditorPane.Visibility = Visibility.Collapsed;
        SidebarTabBar.Visibility = Visibility.Collapsed;
        SidebarSplitter.Visibility = Visibility.Collapsed;

        SyntaxPanelColumn.MinWidth = 0;
        SyntaxPanelColumn.Width = new GridLength(0);
        LeftSplitterColumn.Width = new GridLength(0);
        EditorColumn.Width = new GridLength(0);
        RightSplitterColumn.Width = new GridLength(0);
        SidebarColumn.MinWidth = 0;
        SidebarColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    private void RestoreWebUiDefaultLayout()
    {
        MainMenu.Visibility = _mainMenuVisibilityBeforeMaximize;
        MainToolbar.Visibility = _mainToolbarVisibilityBeforeMaximize;
        EditorPane.Visibility = _editorPaneVisibilityBeforeMaximize;
        SidebarTabBar.Visibility = _sidebarTabBarVisibilityBeforeMaximize;
        SidebarSplitter.Visibility = _sidebarSplitterVisibilityBeforeMaximize;

        SyntaxPanelColumn.MinWidth = _syntaxPanelColumnMinWidthBeforeMaximize;
        LeftSplitterColumn.Width = _leftSplitterColumnBeforeMaximize;
        EditorColumn.Width = _editorColumnBeforeMaximize;
        RightSplitterColumn.Width = _rightSplitterColumnBeforeMaximize;
        SidebarColumn.MinWidth = _sidebarColumnMinWidthBeforeMaximize;
        SidebarColumn.Width = _sidebarColumnBeforeMaximize;

        UpdateSyntaxPanelVisibility();
    }

    private void UpdateWebUiMaximizeChrome()
    {
        if (WebUiMaximizeButton == null)
        {
            return;
        }

        if (_isWebUiMaximized)
        {
            WebUiMaximizeButton.Content = "Restore";
            WebUiMaximizeButton.ToolTip = "Return web preview to the side panel (Esc or Shift+F6)";
        }
        else
        {
            WebUiMaximizeButton.Content = "Maximize";
            WebUiMaximizeButton.ToolTip = "Show web preview across the entire IDE client area (Shift+F6)";
        }
    }

    private async Task ResetWebUiPreviewAsync()
    {
        WebUiUrlTextBox.Text = string.Empty;
        _lastDetectedWebUiUrl = null;

        try
        {
            await EnsureWebUiCoreAsync();
            var core = WebUiWebView.CoreWebView2;
            if (core == null)
            {
                WebUiWebView.Source = new Uri("about:blank");
                return;
            }

            core.Stop();
            core.NavigateToString(BuildEmptyWebPreviewHtml());
        }
        catch
        {
            try
            {
                WebUiWebView.Source = new Uri("about:blank");
            }
            catch
            {
                // Keep IDE functional even if WebView2 runtime is unavailable.
            }
        }
    }

    private string BuildEmptyWebPreviewHtml()
    {
        var background = _themeService.CurrentTheme.WindowBackground;
        var backgroundHex = $"#{background.R:X2}{background.G:X2}{background.B:X2}";
        return
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title></title>" +
            $"<style>html,body{{margin:0;height:100%;background:{backgroundHex};}}</style>" +
            "</head><body></body></html>";
    }

    private void WebUiWebView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isWebUiMaximized)
        {
            SetWebUiMaximized(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ToggleWebUiMaximized();
            e.Handled = true;
        }
    }
}
