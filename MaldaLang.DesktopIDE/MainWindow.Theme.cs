// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Models;
using System.Windows.Threading;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void SetupThemes()
    {
        ThemeComboBox.Items.Clear();
        ThemeMenu.Items.Clear();
        foreach (var theme in _themeService.AvailableThemes)
        {
            var item = new ComboBoxItem 
            { 
                Content = theme.DisplayName, 
                Tag = theme
            };
            ThemeComboBox.Items.Add(item);

            var menuItem = new MenuItem
            {
                Header = theme.DisplayName,
                Tag = theme.Name,
                IsCheckable = true
            };
            menuItem.Click += ViewThemeMenuItem_Click;
            ThemeMenu.Items.Add(menuItem);
        }

        SyncThemeSelectors(_themeService.CurrentTheme);
        
        // Apply initial theme
        ApplyTheme(_themeService.CurrentTheme);
    }

    private void SyncThemeSelectors(Theme theme)
    {
        foreach (ComboBoxItem item in ThemeComboBox.Items)
        {
            if (item.Tag is Theme listed && listed.Name == theme.Name)
            {
                if (!Equals(ThemeComboBox.SelectedItem, item))
                {
                    ThemeComboBox.SelectedItem = item;
                }
                break;
            }
        }

        foreach (var obj in ThemeMenu.Items)
        {
            if (obj is MenuItem menuItem)
            {
                menuItem.IsChecked = menuItem.Tag is string name && name == theme.Name;
            }
        }
    }
    
    private void OnThemeChanged(object? sender, Theme theme)
    {
        Dispatcher.Invoke(() =>
        {
            SyncThemeSelectors(theme);
            ApplyTheme(theme);
        });
    }
    
    private void ApplyTheme(Theme theme)
    {
        // Update all dynamic resources
        Resources["WindowBackgroundBrush"] = new SolidColorBrush(theme.WindowBackground);
        Resources["MainBackgroundBrush"] = new SolidColorBrush(theme.MainBackground);
        Resources["SidebarBackgroundBrush"] = new SolidColorBrush(theme.SidebarBackground);
        Resources["ToolbarBackgroundBrush"] = new SolidColorBrush(theme.ToolbarBackground);
        Resources["ToolbarBorderBrush"] = new SolidColorBrush(theme.ToolbarBorder);
        Resources["EditorBackgroundBrush"] = new SolidColorBrush(theme.EditorBackground);
        Resources["EditorForegroundBrush"] = new SolidColorBrush(theme.EditorForeground);
        Resources["EditorLineNumbersBrush"] = new SolidColorBrush(theme.EditorLineNumbers);
        Resources["TextForegroundBrush"] = new SolidColorBrush(theme.TextForeground);
        Resources["TextSecondaryBrush"] = new SolidColorBrush(theme.TextSecondary);
        Resources["ButtonBackgroundBrush"] = new SolidColorBrush(theme.ButtonBackground);
        Resources["ButtonForegroundBrush"] = new SolidColorBrush(theme.ButtonForeground);
        Resources["ButtonBorderBrush"] = new SolidColorBrush(theme.ButtonBorder);
        Resources["ButtonHoverBrush"] = new SolidColorBrush(theme.ButtonHover);
        Resources["ButtonHoverBorderBrush"] = new SolidColorBrush(theme.ButtonHoverBorder);
        Resources["PrimaryButtonBackgroundBrush"] = new SolidColorBrush(theme.PrimaryButtonBackground);
        Resources["PrimaryButtonForegroundBrush"] = new SolidColorBrush(theme.PrimaryButtonForeground);
        Resources["PrimaryButtonBorderBrush"] = new SolidColorBrush(theme.PrimaryButtonBorder);
        Resources["PrimaryButtonHoverBrush"] = new SolidColorBrush(theme.PrimaryButtonHover);
        Resources["PrimaryButtonHoverBorderBrush"] = new SolidColorBrush(theme.PrimaryButtonHoverBorder);
        Resources["TabBackgroundBrush"] = new SolidColorBrush(theme.TabBackground);
        Resources["TabActiveBackgroundBrush"] = new SolidColorBrush(theme.TabActiveBackground);
        Resources["TabForegroundBrush"] = new SolidColorBrush(theme.TabForeground);
        Resources["TabHoverBrush"] = new SolidColorBrush(theme.TabHover);
        Resources["InputBackgroundBrush"] = new SolidColorBrush(theme.InputBackground);
        Resources["InputForegroundBrush"] = new SolidColorBrush(theme.InputForeground);
        Resources["InputBorderBrush"] = new SolidColorBrush(theme.InputBorder);
        Resources["BorderBrush"] = new SolidColorBrush(theme.BorderColor);
        Resources["GridSplitterBackgroundBrush"] = new SolidColorBrush(theme.GridSplitterBackground);
        Resources["ListBackgroundBrush"] = new SolidColorBrush(theme.ListBackground);
        Resources["ListForegroundBrush"] = new SolidColorBrush(theme.ListForeground);
        Resources["ListBorderBrush"] = new SolidColorBrush(theme.ListBorder);
        Resources["DebugAccentBrush"] = new SolidColorBrush(theme.DebugAccent);
        Resources["ErrorBrush"] = new SolidColorBrush(theme.ErrorColor);
        Resources["WarningBrush"] = new SolidColorBrush(theme.WarningColor);
        Resources["InfoBrush"] = new SolidColorBrush(theme.InfoColor);
        Resources["SuccessBrush"] = new SolidColorBrush(theme.SuccessColor);
        EditorPopupTheming.PublishApplicationResources(theme);
        if (_completionWindow != null)
        {
            EditorPopupTheming.Apply(_completionWindow, theme);
        }

        if (_signatureHelpWindow != null)
        {
            EditorPopupTheming.Apply(_signatureHelpWindow, theme);
        }

        if (_hoverToolTip != null)
        {
            EditorPopupTheming.Apply(_hoverToolTip, theme);
        }

        if (CodeEditor != null)
        {
            ApplyEditorContextMenuTheme(theme);
        }
        
        // Update system menu colors for submenu popups
        // This ensures submenu backgrounds use the correct theme color
        Resources[SystemColors.MenuBrushKey] = new SolidColorBrush(theme.InputBackground);
        Resources[SystemColors.MenuTextBrushKey] = new SolidColorBrush(theme.TextForeground);
        
        // Also update at Application level to ensure it propagates to all windows
        Application.Current.Resources[SystemColors.MenuBrushKey] = new SolidColorBrush(theme.InputBackground);
        Application.Current.Resources[SystemColors.MenuTextBrushKey] = new SolidColorBrush(theme.TextForeground);
        
        // Force update of any open menu popups by invalidating visual tree
        MainMenu?.InvalidateVisual();
        
        // Calculate scrollbar colors based on theme
        // For dark themes, make scrollbar slightly lighter than background
        // For light themes, make scrollbar slightly darker than background
        var isDarkTheme = theme.ListBackground.R < 128;
        Color scrollBarTrack, scrollBarThumb, scrollBarThumbHover, scrollBarButton;
        
        if (isDarkTheme)
        {
            // Dark theme: slightly lighter colors
            scrollBarTrack = Color.FromRgb(
                (byte)Math.Min(255, theme.ListBackground.R + 30),
                (byte)Math.Min(255, theme.ListBackground.G + 30),
                (byte)Math.Min(255, theme.ListBackground.B + 30));
            scrollBarThumb = Color.FromRgb(
                (byte)Math.Min(255, theme.ListBackground.R + 60),
                (byte)Math.Min(255, theme.ListBackground.G + 60),
                (byte)Math.Min(255, theme.ListBackground.B + 60));
            scrollBarThumbHover = Color.FromRgb(
                (byte)Math.Min(255, theme.ListBackground.R + 80),
                (byte)Math.Min(255, theme.ListBackground.G + 80),
                (byte)Math.Min(255, theme.ListBackground.B + 80));
            scrollBarButton = Color.FromRgb(
                (byte)Math.Min(255, theme.ListBackground.R + 50),
                (byte)Math.Min(255, theme.ListBackground.G + 50),
                (byte)Math.Min(255, theme.ListBackground.B + 50));
        }
        else
        {
            // Light theme: slightly darker colors
            scrollBarTrack = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 20),
                (byte)Math.Max(0, theme.ListBackground.G - 20),
                (byte)Math.Max(0, theme.ListBackground.B - 20));
            scrollBarThumb = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 40),
                (byte)Math.Max(0, theme.ListBackground.G - 40),
                (byte)Math.Max(0, theme.ListBackground.B - 40));
            scrollBarThumbHover = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 60),
                (byte)Math.Max(0, theme.ListBackground.G - 60),
                (byte)Math.Max(0, theme.ListBackground.B - 60));
            scrollBarButton = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 30),
                (byte)Math.Max(0, theme.ListBackground.G - 30),
                (byte)Math.Max(0, theme.ListBackground.B - 30));
        }
        
        Resources["ScrollBarTrackBrush"] = new SolidColorBrush(scrollBarTrack);
        Resources["ScrollBarThumbBrush"] = new SolidColorBrush(scrollBarThumb);
        Resources["ScrollBarThumbHoverBrush"] = new SolidColorBrush(scrollBarThumbHover);
        Resources["ScrollBarButtonBrush"] = new SolidColorBrush(scrollBarButton);
        
        // Update current line highlight color to match theme
        _currentLineRenderer?.SetColor(theme.DebugAccent);
        
        // Update tab button backgrounds
        UpdateTabButtonBackgrounds();
        
        // Update syntax highlighting for the new theme
        UpdateSyntaxHighlighting();
        
        // Refresh WebBrowser content to apply new theme colors
        // Note: WebBrowser doesn't support Background property, so we rely on HTML background
        // Always update the output panel to apply theme colors, even when empty
        SetOutputText(_executionService?.GetCurrentOutput() ?? "");
        UpdateToolCallsDisplay();
        
        // Update window title bar colors to match theme
        UpdateTitleBarColors(theme);
    }
    
    private void UpdateTitleBarColors(Theme theme)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                // Window handle not available yet, try again after window is loaded
                if (!IsLoaded)
                {
                    Loaded += OnWindowLoadedForTitleBar;
                }
                return;
            }
            
            ApplyTitleBarColors(hwnd, theme);
        }
        catch
        {
            // If DWM API calls fail (e.g., on older Windows versions), silently ignore
            // The window will use default title bar styling
        }
    }
    
    private void OnWindowLoadedForTitleBar(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoadedForTitleBar;
        UpdateTitleBarColors(_themeService.CurrentTheme);
    }
    
    private void ApplyTitleBarColors(IntPtr hwnd, Theme theme)
    {
        try
        {
            
            // Determine if this is a dark theme
            var isDarkTheme = theme.WindowBackground.R < 128;
            
            // Set dark mode for title bar
            int darkMode = isDarkTheme ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            
            // Set title bar background color (caption color)
            // Convert WPF Color to ARGB int (0xAARRGGBB format)
            var captionColor = (int)((255 << 24) | (theme.WindowBackground.R << 16) | (theme.WindowBackground.G << 8) | theme.WindowBackground.B);
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            
            // Set title bar text color
            var textColor = (int)((255 << 24) | (theme.TextForeground.R << 16) | (theme.TextForeground.G << 8) | theme.TextForeground.B);
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
            
            // Set border color (optional, uses caption color if not set)
            var borderColor = (int)((255 << 24) | (theme.BorderColor.R << 16) | (theme.BorderColor.G << 8) | theme.BorderColor.B);
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch
        {
            // If DWM API calls fail (e.g., on older Windows versions), silently ignore
            // The window will use default title bar styling
        }
    }
    
    private void UpdateTitleBarColorsForWindow(Window window, Theme theme)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ApplyTitleBarColors(hwnd, theme);
            }
            else
            {
                // Window handle not available yet, try again after window is loaded
                window.Loaded += (s, e) =>
                {
                    var hwnd2 = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (hwnd2 != IntPtr.Zero)
                    {
                        ApplyTitleBarColors(hwnd2, theme);
                    }
                };
            }
        }
        catch
        {
            // If DWM API calls fail, silently ignore
        }
    }
    
    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is Theme theme)
        {
            _themeService.SetTheme(theme.Name);
        }
    }
}
