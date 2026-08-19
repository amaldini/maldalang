// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using MaldaLang.DesktopIDE.Models;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Colors for editor popups (completion, signature help, hover, quick fix) derived from an IDE theme.
/// AvalonEdit completion windows and WPF ContextMenus do not inherit MainWindow resources.
/// </summary>
public sealed class EditorPopupTheme
{
    public Color Background { get; init; }
    public Color Foreground { get; init; }
    public Color Border { get; init; }
    public Color SelectionBackground { get; init; }
    public Color SelectionForeground { get; init; }

    public static EditorPopupTheme FromTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var background = theme.EditorBackground;
        var foreground = theme.EditorForeground;
        return new EditorPopupTheme
        {
            Background = background,
            Foreground = foreground,
            Border = theme.ListBorder,
            SelectionBackground = theme.EditorSelection,
            SelectionForeground = ContrastForeground(theme.EditorSelection, Colors.White, Colors.Black)
        };
    }

    public static Color ContrastForeground(Color background, Color lightText, Color darkText)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.55 ? darkText : lightText;
    }
}

/// <summary>
/// Applies <see cref="EditorPopupTheme"/> to AvalonEdit / WPF popup chrome.
/// </summary>
public static class EditorPopupTheming
{
    public static void Apply(Window window, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(theme);

        var spec = EditorPopupTheme.FromTheme(theme);
        var background = Freeze(spec.Background);
        var foreground = Freeze(spec.Foreground);
        var border = Freeze(spec.Border);
        var selection = Freeze(spec.SelectionBackground);
        var selectionText = Freeze(spec.SelectionForeground);

        window.Background = background;
        window.Foreground = foreground;
        window.BorderBrush = border;
        window.BorderThickness = new Thickness(1);

        var resources = window.Resources;
        resources[SystemColors.WindowBrushKey] = background;
        resources[SystemColors.WindowTextBrushKey] = foreground;
        resources[SystemColors.ControlBrushKey] = background;
        resources[SystemColors.ControlTextBrushKey] = foreground;
        resources[SystemColors.HighlightBrushKey] = selection;
        resources[SystemColors.HighlightTextBrushKey] = selectionText;
        resources[SystemColors.InactiveSelectionHighlightBrushKey] = selection;
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = selectionText;
        resources[SystemColors.InfoBrushKey] = background;
        resources[SystemColors.InfoTextBrushKey] = foreground;
        resources["EditorBackgroundBrush"] = background;
        resources["EditorForegroundBrush"] = foreground;
        resources["ListBackgroundBrush"] = background;
        resources["ListForegroundBrush"] = foreground;
        resources["InputBackgroundBrush"] = background;
        resources["TextForegroundBrush"] = foreground;
        resources["BorderBrush"] = border;

        void ApplyChrome()
        {
            if (window is CompletionWindow completion)
            {
                ApplyCompletionList(completion, background, foreground);
            }

            TintRootBorder(window, background, border);
        }

        if (window.IsLoaded)
        {
            ApplyChrome();
        }
        else
        {
            RoutedEventHandler? handler = null;
            handler = (_, _) =>
            {
                window.Loaded -= handler;
                ApplyChrome();
            };
            window.Loaded += handler;
        }
    }

    public static void Apply(ToolTip toolTip, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(toolTip);
        ArgumentNullException.ThrowIfNull(theme);

        var spec = EditorPopupTheme.FromTheme(theme);
        toolTip.Background = Freeze(spec.Background);
        toolTip.Foreground = Freeze(spec.Foreground);
        toolTip.BorderBrush = Freeze(spec.Border);
        toolTip.BorderThickness = new Thickness(1);
    }

    public static void Apply(ContextMenu menu, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(theme);

        var spec = EditorPopupTheme.FromTheme(theme);
        var background = Freeze(spec.Background);
        var foreground = Freeze(spec.Foreground);
        var border = Freeze(spec.Border);
        var hover = Freeze(theme.ButtonHover);

        menu.Background = background;
        menu.Foreground = foreground;
        menu.BorderBrush = border;
        menu.BorderThickness = new Thickness(1);

        var resources = menu.Resources;
        resources[SystemColors.MenuBrushKey] = background;
        resources[SystemColors.MenuTextBrushKey] = foreground;
        resources[SystemColors.ControlBrushKey] = background;
        resources[SystemColors.ControlTextBrushKey] = foreground;
        resources[SystemColors.HighlightBrushKey] = Freeze(spec.SelectionBackground);
        resources[SystemColors.HighlightTextBrushKey] = Freeze(spec.SelectionForeground);
        resources["EditorBackgroundBrush"] = background;
        resources["EditorForegroundBrush"] = foreground;
        resources["InputBackgroundBrush"] = background;
        resources["TextForegroundBrush"] = foreground;
        resources["TextSecondaryBrush"] = Freeze(theme.TextSecondary);
        resources["ButtonHoverBrush"] = hover;
        resources["BorderBrush"] = border;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.Foreground = foreground;
            item.Background = Brushes.Transparent;
        }

        menu.Opened += (_, _) => TintRootBorder(menu, background, border);
    }

    public static void PublishApplicationResources(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        var spec = EditorPopupTheme.FromTheme(theme);
        var background = Freeze(spec.Background);
        var foreground = Freeze(spec.Foreground);
        var border = Freeze(spec.Border);
        var selection = Freeze(spec.SelectionBackground);
        var selectionText = Freeze(spec.SelectionForeground);

        app.Resources["EditorBackgroundBrush"] = background;
        app.Resources["EditorForegroundBrush"] = foreground;
        app.Resources["InputBackgroundBrush"] = Freeze(theme.InputBackground);
        app.Resources["TextForegroundBrush"] = Freeze(theme.TextForeground);
        app.Resources["TextSecondaryBrush"] = Freeze(theme.TextSecondary);
        app.Resources["ButtonHoverBrush"] = Freeze(theme.ButtonHover);
        app.Resources["BorderBrush"] = border;
        app.Resources[SystemColors.MenuBrushKey] = background;
        app.Resources[SystemColors.MenuTextBrushKey] = foreground;
        app.Resources[SystemColors.InfoBrushKey] = background;
        app.Resources[SystemColors.InfoTextBrushKey] = foreground;
        app.Resources[SystemColors.HighlightBrushKey] = selection;
        app.Resources[SystemColors.HighlightTextBrushKey] = selectionText;
    }

    private static void ApplyCompletionList(CompletionWindow completion, SolidColorBrush background, SolidColorBrush foreground)
    {
        completion.CompletionList.Background = background;
        completion.CompletionList.Foreground = foreground;
        var listBox = completion.CompletionList.ListBox;
        if (listBox == null)
        {
            return;
        }

        listBox.Background = background;
        listBox.Foreground = foreground;
        listBox.BorderThickness = new Thickness(0);
    }

    private static void TintRootBorder(DependencyObject root, SolidColorBrush background, SolidColorBrush border)
    {
        if (VisualTreeHelper.GetChildrenCount(root) == 0)
        {
            return;
        }

        if (VisualTreeHelper.GetChild(root, 0) is Border chrome)
        {
            chrome.Background = background;
            chrome.BorderBrush = border;
        }
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
