// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using MaldaLang.DesktopIDE.Services;
using System.Windows.Threading;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using MaldaLang.UIHost;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _maximizedSidebarTab != null)
        {
            SetSidebarPanelMaximized(_maximizedSidebarTab, false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowCommandPalette();
            e.Handled = true;
        }
    }

    private void WebUiMaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWebUiMaximized();
    }

    private void AiPanelMaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleAiPanelMaximized();
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

    private void ViewToggleMaximizeAiPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        SetAiPanelMaximized(menuItem.IsChecked);
    }

    private void ViewResetWebPreview_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("webui");
        _ = ResetWebUiPreviewAsync();
    }

    private void ToggleWebUiMaximized()
    {
        SetWebUiMaximized(!IsWebUiMaximized);
    }

    private void ToggleAiPanelMaximized()
    {
        SetAiPanelMaximized(!IsAiPanelMaximized);
    }

    private void SetWebUiMaximized(bool maximized)
    {
        SetSidebarPanelMaximized("webui", maximized);
    }

    private void SetAiPanelMaximized(bool maximized)
    {
        SetSidebarPanelMaximized("ai", maximized);
    }

    private void SetSidebarPanelMaximized(string tab, bool maximized)
    {
        var currentlyMaximized = _maximizedSidebarTab;
        var thisIsMaximized = currentlyMaximized == tab;

        if (thisIsMaximized == maximized)
        {
            UpdateSidebarMaximizeChrome();
            return;
        }

        if (maximized)
        {
            var keepExistingLayout = currentlyMaximized != null;
            _maximizedSidebarTab = tab;
            SwitchToTab(tab);
            if (tab == "ai")
            {
                UpdateAIChatPanelContext();
            }
            if (!keepExistingLayout)
            {
                CaptureSidebarDefaultLayout();
                ApplySidebarMaximizedLayout();
            }
        }
        else if (currentlyMaximized == tab)
        {
            _maximizedSidebarTab = null;
            RestoreSidebarDefaultLayout();
        }

        UpdateSidebarMaximizeChrome();
        UpdateViewMenuStates();
    }

    private void CaptureSidebarDefaultLayout()
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

    private void ApplySidebarMaximizedLayout()
    {
        MainMenu.Visibility = Visibility.Collapsed;
        MainToolbar.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
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

    private void RestoreSidebarDefaultLayout()
    {
        MainMenu.Visibility = _mainMenuVisibilityBeforeMaximize;
        MainToolbar.Visibility = _mainToolbarVisibilityBeforeMaximize;
        StatusBar.Visibility = Visibility.Visible;
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

    private void UpdateSidebarMaximizeChrome()
    {
        if (WebUiMaximizeButton != null)
        {
            if (IsWebUiMaximized)
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

        if (ManualMaximizeButton != null)
        {
            if (_maximizedSidebarTab == "manual")
            {
                ManualMaximizeButton.Content = "Restore";
                ManualMaximizeButton.ToolTip = "Return the reference manual to the side panel (Esc)";
            }
            else
            {
                ManualMaximizeButton.Content = "Maximize";
                ManualMaximizeButton.ToolTip = "Show the reference manual across the IDE (Esc restores the editor)";
            }
        }

        if (AiPanelMaximizeButton != null)
        {
            if (IsAiPanelMaximized)
            {
                AiPanelMaximizeButton.Content = "Restore";
                AiPanelMaximizeButton.ToolTip = "Return AI panel to the side panel (Esc or Shift+F7)";
            }
            else
            {
                AiPanelMaximizeButton.Content = "Maximize";
                AiPanelMaximizeButton.ToolTip = "Show AI panel across the entire IDE client area (Shift+F7)";
            }
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
        if (e.Key == Key.Escape && _maximizedSidebarTab != null)
        {
            SetSidebarPanelMaximized(_maximizedSidebarTab, false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ToggleWebUiMaximized();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F7 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ToggleAiPanelMaximized();
            e.Handled = true;
        }
    }

    private void WebUiGoButton_Click(object sender, RoutedEventArgs e)
    {
        var target = WebUiUrlTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var uri = TryResolveWebViewUri(target);
        if (uri == null)
        {
            IdePromptWindow.Show(this, "Enter a valid URL or a local HTML file path.", "Web UI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _ = OpenUriInWebUiPanelAsync(uri, target, switchToTab: true, ensureUiHost: !uri.IsFile);
    }

    private void WebUiOpenExternalButton_Click(object sender, RoutedEventArgs e)
    {
        var target = WebUiUrlTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var uri = TryResolveWebViewUri(target);
        var externalTarget = uri?.IsFile == true ? uri.LocalPath : uri?.AbsoluteUri ?? target;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = externalTarget,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            IdePromptWindow.Show(this, $"Could not open URL: {ex.Message}", "Web UI", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TryAutoOpenWebUiFromOutput(string outputText)
    {
        if (_showcase != null || string.IsNullOrWhiteSpace(outputText))
        {
            return;
        }

        var match = Regex.Match(outputText, @"Open:\s*(https?://\S+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return;
        }

        var detectedUrl = match.Groups[1].Value.Trim();
        if (string.Equals(detectedUrl, _lastDetectedWebUiUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _lastDetectedWebUiUrl = detectedUrl;
        _ = OpenWebUiInPanelAsync(detectedUrl, switchToTab: true);
    }

    private async Task OpenWebUiInPanelAsync(string url, bool switchToTab)
    {
        var uri = TryResolveWebViewUri(url);
        if (uri == null)
        {
            throw new InvalidOperationException("The requested Web UI target is not a valid URL or local file.");
        }

        await OpenUriInWebUiPanelAsync(uri, url, switchToTab, ensureUiHost: !uri.IsFile);
    }

    private async Task OpenUriInWebUiPanelAsync(Uri uri, string displayText, bool switchToTab, bool ensureUiHost)
    {
        if (ensureUiHost)
        {
            await EnsureUiHostRunningAsync();
        }

        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                WebUiUrlTextBox.Text = displayText;
                WebUiWebView.Source = uri;
                if (switchToTab)
                {
                    SwitchToTab("webui");
                }
            }
            catch (Exception ex)
            {
                IdePromptWindow.Show(this, $"Failed to open Web UI: {ex.Message}", "Web UI", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private static Uri? TryResolveWebViewUri(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp ||
             absoluteUri.Scheme == Uri.UriSchemeHttps ||
             absoluteUri.Scheme == Uri.UriSchemeFile))
        {
            return absoluteUri;
        }

        var expandedPath = System.Environment.ExpandEnvironmentVariables(target);
        if (File.Exists(expandedPath))
        {
            return new Uri(Path.GetFullPath(expandedPath));
        }

        return null;
    }

    private async Task EnsureUiHostRunningAsync()
    {
        var uiHostUrl = System.Environment.GetEnvironmentVariable("MALDA_UI_HOST_URL");
        if (string.IsNullOrWhiteSpace(uiHostUrl))
        {
            uiHostUrl = DefaultUiHostUrl;
        }

        if (await IsUiHostHealthyAsync(uiHostUrl))
        {
            return;
        }

        try
        {
            await EmbeddedUiHostRuntime.TryStartAsync();
            await WaitForUiHostAsync(uiHostUrl);
        }
        catch
        {
            // Keep web preview usable even if embedded UIHost startup fails.
        }
    }

    private static async Task<bool> IsUiHostHealthyAsync(string uiHostUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await client.GetAsync($"{uiHostUrl.TrimEnd('/')}/health");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForUiHostAsync(string uiHostUrl)
    {
        const int maxAttempts = 120;
        for (var i = 0; i < maxAttempts; i++)
        {
            if (await IsUiHostHealthyAsync(uiHostUrl))
            {
                return;
            }
            await Task.Delay(500);
        }
    }

    private static string? FindRepoRoot()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            var current = new DirectoryInfo(dir);
            while (current != null)
            {
                var sourceMarker = Path.Combine(current.FullName, "MaldaLang.UIHost");
                var manualIndex = Path.Combine(current.FullName, "ReferenceManual", "index.html");
                var distBin = Path.Combine(current.FullName, "bin");
                var hasDistLayout =
                    File.Exists(manualIndex) &&
                    (Directory.Exists(Path.Combine(distBin, "desktop-ide")) ||
                     Directory.Exists(Path.Combine(distBin, "ui-host")) ||
                     Directory.Exists(Path.Combine(distBin, "malda")));

                if (Directory.Exists(sourceMarker) || hasDistLayout)
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
        }
        catch
        {
            // Best effort only.
        }
        return null;
    }

    private async Task InitializeWebUiPreviewAsync()
    {
        try
        {
            await EnsureWebUiCoreAsync();
        }
        catch
        {
            // Keep IDE functional even if WebView2 runtime is unavailable.
        }
    }

    private async Task<CoreWebView2?> EnsureWebUiCoreAsync()
    {
        await WebUiWebView.EnsureCoreWebView2Async();
        var core = WebUiWebView.CoreWebView2;
        if (core == null)
        {
            return null;
        }

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        EnsureWebPreviewVirtualHost(core);
        return core;
    }

    private void EnsureWebPreviewVirtualHost(CoreWebView2 core)
    {
        var repoRoot = FindRepoRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        core.SetVirtualHostNameToFolderMapping(
            WebPreviewHostBuilder.VirtualHostName,
            repoRoot,
            CoreWebView2HostResourceAccessKind.Allow);
    }
}
