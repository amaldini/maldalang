// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Models;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Applies IDE chrome colors to code-created dialogs. Those windows do not inherit
/// <c>MainWindow</c> resources, so implicit <c>IdeChrome</c> styles otherwise keep
/// light-theme <c>InputForegroundBrush</c> on a dark input background.
/// </summary>
public static class DialogTheming
{
    public static void Apply(Window window, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(theme);

        Publish(window.Resources, theme);

        var background = (SolidColorBrush)window.Resources["WindowBackgroundBrush"];
        var foreground = (SolidColorBrush)window.Resources["TextForegroundBrush"];
        var border = (SolidColorBrush)window.Resources["BorderBrush"];

        window.Background = background;
        window.Foreground = foreground;
        window.BorderBrush = border;

        window.Resources[SystemColors.WindowBrushKey] = background;
        window.Resources[SystemColors.WindowTextBrushKey] = foreground;
        window.Resources[SystemColors.ControlBrushKey] = background;
        window.Resources[SystemColors.ControlTextBrushKey] = foreground;
        window.Resources[SystemColors.GrayTextBrushKey] = (SolidColorBrush)window.Resources["TextSecondaryBrush"];
    }

    public static void Publish(ResourceDictionary resources, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(theme);

        resources["WindowBackgroundBrush"] = Freeze(theme.WindowBackground);
        resources["MainBackgroundBrush"] = Freeze(theme.MainBackground);
        resources["SidebarBackgroundBrush"] = Freeze(theme.SidebarBackground);
        resources["ToolbarBackgroundBrush"] = Freeze(theme.ToolbarBackground);
        resources["ToolbarBorderBrush"] = Freeze(theme.ToolbarBorder);
        resources["EditorBackgroundBrush"] = Freeze(theme.EditorBackground);
        resources["EditorForegroundBrush"] = Freeze(theme.EditorForeground);
        resources["EditorLineNumbersBrush"] = Freeze(theme.EditorLineNumbers);
        resources["TextForegroundBrush"] = Freeze(theme.TextForeground);
        resources["TextSecondaryBrush"] = Freeze(theme.TextSecondary);
        resources["ButtonBackgroundBrush"] = Freeze(theme.ButtonBackground);
        resources["ButtonForegroundBrush"] = Freeze(theme.ButtonForeground);
        resources["ButtonBorderBrush"] = Freeze(theme.ButtonBorder);
        resources["ButtonHoverBrush"] = Freeze(theme.ButtonHover);
        resources["ButtonHoverBorderBrush"] = Freeze(theme.ButtonHoverBorder);
        resources["PrimaryButtonBackgroundBrush"] = Freeze(theme.PrimaryButtonBackground);
        resources["PrimaryButtonForegroundBrush"] = Freeze(theme.PrimaryButtonForeground);
        resources["PrimaryButtonBorderBrush"] = Freeze(theme.PrimaryButtonBorder);
        resources["PrimaryButtonHoverBrush"] = Freeze(theme.PrimaryButtonHover);
        resources["PrimaryButtonHoverBorderBrush"] = Freeze(theme.PrimaryButtonHoverBorder);
        resources["TabBackgroundBrush"] = Freeze(theme.TabBackground);
        resources["TabActiveBackgroundBrush"] = Freeze(theme.TabActiveBackground);
        resources["TabForegroundBrush"] = Freeze(theme.TabForeground);
        resources["TabHoverBrush"] = Freeze(theme.TabHover);
        resources["InputBackgroundBrush"] = Freeze(theme.InputBackground);
        resources["InputForegroundBrush"] = Freeze(theme.InputForeground);
        resources["InputBorderBrush"] = Freeze(theme.InputBorder);
        resources["BorderBrush"] = Freeze(theme.BorderColor);
        resources["GridSplitterBackgroundBrush"] = Freeze(theme.GridSplitterBackground);
        resources["ListBackgroundBrush"] = Freeze(theme.ListBackground);
        resources["ListForegroundBrush"] = Freeze(theme.ListForeground);
        resources["ListBorderBrush"] = Freeze(theme.ListBorder);
        resources["DebugAccentBrush"] = Freeze(theme.DebugAccent);
        resources["ErrorBrush"] = Freeze(theme.ErrorColor);
        resources["WarningBrush"] = Freeze(theme.WarningColor);
        resources["InfoBrush"] = Freeze(theme.InfoColor);
        resources["SuccessBrush"] = Freeze(theme.SuccessColor);
        resources["ScrollBarTrackBrush"] = Freeze(ScrollTrack(theme));
        resources["ScrollBarThumbBrush"] = Freeze(ScrollThumb(theme));
        resources["ScrollBarThumbHoverBrush"] = Freeze(ScrollThumbHover(theme));
    }

    public static void CopyChrome(FrameworkElement source, FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        string[] keys =
        [
            "WindowBackgroundBrush",
            "MainBackgroundBrush",
            "SidebarBackgroundBrush",
            "ToolbarBackgroundBrush",
            "ToolbarBorderBrush",
            "EditorBackgroundBrush",
            "EditorForegroundBrush",
            "EditorLineNumbersBrush",
            "TextForegroundBrush",
            "TextSecondaryBrush",
            "ButtonBackgroundBrush",
            "ButtonForegroundBrush",
            "ButtonBorderBrush",
            "ButtonHoverBrush",
            "ButtonHoverBorderBrush",
            "PrimaryButtonBackgroundBrush",
            "PrimaryButtonForegroundBrush",
            "PrimaryButtonBorderBrush",
            "PrimaryButtonHoverBrush",
            "PrimaryButtonHoverBorderBrush",
            "TabBackgroundBrush",
            "TabActiveBackgroundBrush",
            "TabForegroundBrush",
            "TabHoverBrush",
            "InputBackgroundBrush",
            "InputForegroundBrush",
            "InputBorderBrush",
            "BorderBrush",
            "GridSplitterBackgroundBrush",
            "ListBackgroundBrush",
            "ListForegroundBrush",
            "ListBorderBrush",
            "DebugAccentBrush",
            "ErrorBrush",
            "WarningBrush",
            "InfoBrush",
            "SuccessBrush",
            "ScrollBarTrackBrush",
            "ScrollBarThumbBrush",
            "ScrollBarThumbHoverBrush"
        ];

        foreach (var key in keys)
        {
            if (source.TryFindResource(key) is SolidColorBrush solid)
            {
                target.Resources[key] = Freeze(solid.Color);
            }
        }
    }

    private static Color ScrollTrack(Theme theme) => Shift(theme.ListBackground, theme.IsDark ? 24 : -16);

    private static Color ScrollThumb(Theme theme) => Shift(theme.ListBackground, theme.IsDark ? 56 : -40);

    private static Color ScrollThumbHover(Theme theme) => Shift(theme.ListBackground, theme.IsDark ? 78 : -64);

    private static Color Shift(Color color, int delta)
    {
        return Color.FromRgb(
            (byte)Math.Clamp(color.R + delta, 0, 255),
            (byte)Math.Clamp(color.G + delta, 0, 255),
            (byte)Math.Clamp(color.B + delta, 0, 255));
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
