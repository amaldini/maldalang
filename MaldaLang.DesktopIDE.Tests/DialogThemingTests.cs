// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class DialogThemingTests
{
    [Fact]
    public void Publish_Dark_UsesLightTextOnDarkChrome()
    {
        var theme = new ThemeService().GetTheme("Dark");
        var resources = new ResourceDictionary();

        DialogTheming.Publish(resources, theme);

        AssertBrush(resources, "WindowBackgroundBrush", theme.WindowBackground);
        AssertBrush(resources, "TextForegroundBrush", theme.TextForeground);
        AssertBrush(resources, "InputBackgroundBrush", theme.InputBackground);
        AssertBrush(resources, "InputForegroundBrush", theme.InputForeground);
        AssertBrush(resources, "ButtonForegroundBrush", theme.ButtonForeground);
        Assert.True(IsDark(BrushColor(resources, "WindowBackgroundBrush")));
        Assert.True(IsDark(BrushColor(resources, "InputBackgroundBrush")));
        Assert.False(IsDark(BrushColor(resources, "TextForegroundBrush")));
        Assert.False(IsDark(BrushColor(resources, "InputForegroundBrush")));
        Assert.NotEqual(Colors.White, BrushColor(resources, "WindowBackgroundBrush"));
        Assert.NotEqual(Colors.Black, BrushColor(resources, "InputForegroundBrush"));
    }

    [Fact]
    public void Publish_HighContrast_DoesNotUseBlackTextOnBlackInput()
    {
        var theme = new ThemeService().GetTheme("HighContrast");
        var resources = new ResourceDictionary();

        DialogTheming.Publish(resources, theme);

        Assert.Equal(Colors.Black, BrushColor(resources, "WindowBackgroundBrush"));
        Assert.Equal(Colors.Black, BrushColor(resources, "InputBackgroundBrush"));
        Assert.Equal(Colors.White, BrushColor(resources, "InputForegroundBrush"));
        Assert.Equal(Colors.White, BrushColor(resources, "TextForegroundBrush"));
    }

    [Fact]
    public void Publish_Light_KeepsDarkTextOnLightChrome()
    {
        var theme = new ThemeService().GetTheme("Light");
        var resources = new ResourceDictionary();

        DialogTheming.Publish(resources, theme);

        AssertBrush(resources, "WindowBackgroundBrush", theme.WindowBackground);
        AssertBrush(resources, "InputForegroundBrush", theme.InputForeground);
        Assert.False(IsDark(BrushColor(resources, "WindowBackgroundBrush")));
        Assert.True(IsDark(BrushColor(resources, "InputForegroundBrush")));
    }

    [Fact]
    public void Apply_SetsWindowBackgroundAndForegroundFromTheme()
    {
        var theme = new ThemeService().GetTheme("Dark");
        Exception? caught = null;
        Color? windowBackground = null;
        Color? windowForeground = null;
        Color? inputForeground = null;
        Color? systemWindow = null;
        Color? systemWindowText = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window();
                DialogTheming.Apply(window, theme);
                windowBackground = ((SolidColorBrush)window.Background).Color;
                windowForeground = ((SolidColorBrush)window.Foreground).Color;
                inputForeground = ((SolidColorBrush)window.Resources["InputForegroundBrush"]).Color;
                systemWindow = ((SolidColorBrush)window.Resources[SystemColors.WindowBrushKey]).Color;
                systemWindowText = ((SolidColorBrush)window.Resources[SystemColors.WindowTextBrushKey]).Color;
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null)
        {
            throw caught;
        }

        Assert.Equal(theme.WindowBackground, windowBackground);
        Assert.Equal(theme.TextForeground, windowForeground);
        Assert.Equal(theme.InputForeground, inputForeground);
        Assert.Equal(theme.WindowBackground, systemWindow);
        Assert.Equal(theme.TextForeground, systemWindowText);
    }

    private static void AssertBrush(ResourceDictionary resources, string key, Color expected)
    {
        Assert.Equal(expected, BrushColor(resources, key));
    }

    private static Color BrushColor(ResourceDictionary resources, string key)
    {
        return Assert.IsType<SolidColorBrush>(resources[key]).Color;
    }

    private static bool IsDark(Color color)
    {
        return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0 < 0.5;
    }
}
