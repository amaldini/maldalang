// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows.Media;
using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class PrimaryButtonContrastTests
{
    private static string RepoRoot =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    // WCAG 2.x AA for normal-size text (toolbar label is 12px).
    private const double MinimumContrast = 4.5;

    [Fact]
    public void RunButton_UsesPrimaryForegroundOnLabel()
    {
        var xaml = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoRoot, "MaldaLang.DesktopIDE", "MainWindow.xaml"));

        var runStart = xaml.IndexOf("x:Name=\"RunButton\"", StringComparison.Ordinal);
        Assert.True(runStart >= 0, "RunButton was not found in MainWindow.xaml.");
        var runEnd = xaml.IndexOf("</Button>", runStart, StringComparison.Ordinal);
        Assert.True(runEnd > runStart, "RunButton closing tag was not found.");
        var runMarkup = xaml.Substring(runStart, runEnd - runStart);

        Assert.Contains("Style=\"{StaticResource ToolbarPrimaryButton}\"", runMarkup, StringComparison.Ordinal);
        Assert.Contains(
            "Foreground=\"{DynamicResource PrimaryButtonForegroundBrush}\"",
            runMarkup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ToolbarPrimaryButton_OverridesImplicitTextBlockForeground()
    {
        var xaml = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoRoot, "MaldaLang.DesktopIDE", "Themes", "IdeChrome.xaml"));

        var styleStart = xaml.IndexOf("x:Key=\"ToolbarPrimaryButton\"", StringComparison.Ordinal);
        Assert.True(styleStart >= 0, "ToolbarPrimaryButton was not found in IdeChrome.xaml.");
        var styleMarkup = ExtractStyleMarkup(xaml, styleStart);

        Assert.Contains("DynamicResource PrimaryButtonBackgroundBrush", styleMarkup, StringComparison.Ordinal);
        Assert.Contains("DynamicResource PrimaryButtonForegroundBrush", styleMarkup, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"TextBlock\"", styleMarkup, StringComparison.Ordinal);
    }

    private static string ExtractStyleMarkup(string xaml, int styleStart)
    {
        var depth = 1;
        var index = xaml.IndexOf('>', styleStart);
        Assert.True(index > styleStart, "ToolbarPrimaryButton opening tag was not closed.");
        index++;
        while (index < xaml.Length && depth > 0)
        {
            var nextOpen = xaml.IndexOf("<Style", index, StringComparison.Ordinal);
            var nextClose = xaml.IndexOf("</Style>", index, StringComparison.Ordinal);
            Assert.True(nextClose >= 0, "ToolbarPrimaryButton style was not closed.");
            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                index = nextOpen + 6;
            }
            else
            {
                depth--;
                index = nextClose + 8;
            }
        }

        return xaml.Substring(styleStart, index - styleStart);
    }

    [Fact]
    public void AllThemes_PrimaryButtonFillContrastsWithLabel()
    {
        var themes = new ThemeService().AvailableThemes.ToList();
        Assert.NotEmpty(themes);

        foreach (var theme in themes)
        {
            var fill = ContrastRatio(theme.PrimaryButtonBackground, theme.PrimaryButtonForeground);
            var hover = ContrastRatio(theme.PrimaryButtonHover, theme.PrimaryButtonForeground);
            Assert.True(
                fill >= MinimumContrast,
                $"{theme.Name} primary fill {ToHex(theme.PrimaryButtonBackground)} vs label {ToHex(theme.PrimaryButtonForeground)} contrast {fill:F2} is below {MinimumContrast}.");
            Assert.True(
                hover >= MinimumContrast,
                $"{theme.Name} primary hover {ToHex(theme.PrimaryButtonHover)} vs label {ToHex(theme.PrimaryButtonForeground)} contrast {hover:F2} is below {MinimumContrast}.");
        }
    }

    [Fact]
    public void HighContrast_DoesNotUseLightLabelOnYellowFill()
    {
        var theme = new ThemeService().GetTheme("HighContrast");
        Assert.Equal(Colors.Yellow, theme.PrimaryButtonBackground);
        Assert.Equal(Colors.Black, theme.PrimaryButtonForeground);
        Assert.True(ContrastRatio(theme.PrimaryButtonBackground, theme.PrimaryButtonForeground) >= MinimumContrast);
        Assert.True(ContrastRatio(theme.PrimaryButtonBackground, theme.TextForeground) < MinimumContrast);
    }

    private static double ContrastRatio(Color a, Color b)
    {
        var l1 = RelativeLuminance(a);
        var l2 = RelativeLuminance(b);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }

    private static double Linear(byte channel)
    {
        var srgb = channel / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
