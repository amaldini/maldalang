// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class EditorPopupThemingTests
{
    [Fact]
    public void FromTheme_Dark_UsesEditorBackgroundNotSystemWhite()
    {
        var theme = DarkTheme();

        var popup = EditorPopupTheme.FromTheme(theme);

        Assert.Equal(theme.EditorBackground, popup.Background);
        Assert.Equal(theme.EditorForeground, popup.Foreground);
        Assert.Equal(theme.ListBorder, popup.Border);
        Assert.Equal(theme.EditorSelection, popup.SelectionBackground);
        Assert.NotEqual(Colors.White, popup.Background);
        Assert.True(IsDark(popup.Background));
        Assert.False(IsDark(popup.Foreground));
    }

    [Fact]
    public void FromTheme_Light_KeepsDarkTextOnLightSelection()
    {
        var theme = LightTheme();

        var popup = EditorPopupTheme.FromTheme(theme);

        Assert.Equal(theme.EditorBackground, popup.Background);
        Assert.Equal(theme.EditorForeground, popup.Foreground);
        Assert.False(IsDark(popup.Background));
        Assert.True(IsDark(popup.SelectionForeground));
    }

    [Fact]
    public void FromTheme_HighContrast_UsesBlackTextOnYellowSelection()
    {
        var theme = HighContrastTheme();

        var popup = EditorPopupTheme.FromTheme(theme);

        Assert.Equal(theme.EditorBackground, popup.Background);
        Assert.Equal(Colors.Black, popup.SelectionForeground);
        Assert.Equal(theme.EditorSelection, popup.SelectionBackground);
    }

    [Fact]
    public void ContrastForeground_PicksBlackOnLightAndWhiteOnDark()
    {
        Assert.Equal(Colors.Black, EditorPopupTheme.ContrastForeground(Colors.Yellow, Colors.White, Colors.Black));
        Assert.Equal(Colors.White, EditorPopupTheme.ContrastForeground(Color.FromRgb(38, 79, 120), Colors.White, Colors.Black));
    }

    [Fact]
    public void PublishSystemChrome_Dark_UsesInputColorsNotSystemWhite()
    {
        var theme = DarkTheme();
        var resources = new ResourceDictionary();

        EditorPopupTheming.PublishSystemChrome(resources, theme);

        Assert.Equal(theme.InputBackground, BrushColor(resources, SystemColors.WindowBrushKey));
        Assert.Equal(theme.InputForeground, BrushColor(resources, SystemColors.WindowTextBrushKey));
        Assert.Equal(theme.InputBackground, BrushColor(resources, SystemColors.ControlBrushKey));
        Assert.Equal(theme.InputForeground, BrushColor(resources, SystemColors.ControlTextBrushKey));
        Assert.NotEqual(Colors.White, BrushColor(resources, SystemColors.WindowBrushKey));
        Assert.True(IsDark(BrushColor(resources, SystemColors.WindowBrushKey)));
        Assert.False(IsDark(BrushColor(resources, SystemColors.WindowTextBrushKey)));
    }

    [Fact]
    public void PublishSystemChrome_HighContrast_KeepsWhiteTextOnBlackInput()
    {
        var theme = HighContrastTheme();
        var resources = new ResourceDictionary();

        EditorPopupTheming.PublishSystemChrome(resources, theme);

        Assert.Equal(Colors.Black, BrushColor(resources, SystemColors.WindowBrushKey));
        Assert.Equal(Colors.White, BrushColor(resources, SystemColors.WindowTextBrushKey));
    }

    private static Color BrushColor(ResourceDictionary resources, object key)
    {
        return Assert.IsType<SolidColorBrush>(resources[key]).Color;
    }

    private static bool IsDark(Color color)
    {
        return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0 < 0.5;
    }

    private static Theme DarkTheme()
    {
        return new ThemeService().GetTheme("Dark");
    }

    private static Theme LightTheme()
    {
        return new ThemeService().GetTheme("Light");
    }

    private static Theme HighContrastTheme()
    {
        return new ThemeService().GetTheme("HighContrast");
    }
}
