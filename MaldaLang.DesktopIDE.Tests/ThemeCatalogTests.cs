// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows.Media;
using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class ThemeCatalogTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private const double MinimumContrast = 4.5;

    private static readonly string[] ExpectedNames =
    [
        "Light",
        "Dark",
        "Blue",
        "HighContrast",
        "DarkPlus",
        "OneDark",
        "Nord",
        "Dracula",
        "Monokai",
        "SolarizedDark",
        "Midnight",
        "Forest",
        "SolarizedLight",
        "Sepia"
    ];

    private static readonly string[] DarkVariantNames =
    [
        "Dark",
        "DarkPlus",
        "OneDark",
        "Nord",
        "Dracula",
        "Monokai",
        "SolarizedDark",
        "Midnight",
        "Forest",
        "HighContrast"
    ];

    [Fact]
    public void Catalog_IncludesOriginalAndTenAdditionalThemes()
    {
        var themes = new ThemeService().AvailableThemes.ToList();

        Assert.Equal(ExpectedNames.Length, themes.Count);
        Assert.Equal(ExpectedNames.OrderBy(n => n, StringComparer.Ordinal),
            themes.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(themes.Count, themes.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(themes.Count, themes.Select(t => t.DisplayName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DarkVariants_AreClassifiedDark_AndLightsAreNot()
    {
        var service = new ThemeService();

        foreach (var name in DarkVariantNames)
        {
            var theme = service.GetTheme(name);
            Assert.True(theme.IsDark, $"{name} should be a dark theme.");
        }

        Assert.False(service.GetTheme("Light").IsDark);
        Assert.False(service.GetTheme("Blue").IsDark);
        Assert.False(service.GetTheme("SolarizedLight").IsDark);
        Assert.False(service.GetTheme("Sepia").IsDark);
    }

    [Fact]
    public void AllThemes_EditorForegroundContrastsWithBackground()
    {
        foreach (var theme in new ThemeService().AvailableThemes)
        {
            var ratio = ContrastRatio(theme.EditorBackground, theme.EditorForeground);
            Assert.True(
                ratio >= MinimumContrast,
                $"{theme.Name} editor {ToHex(theme.EditorForeground)} on {ToHex(theme.EditorBackground)} contrast {ratio:F2} is below {MinimumContrast}.");
        }
    }

    [Fact]
    public void GetTheme_UnknownName_FallsBackToLight()
    {
        var theme = new ThemeService().GetTheme("does-not-exist");
        Assert.Equal("Light", theme.Name);
    }

    [Fact]
    public void MainWindow_ThemeMenuIsPopulatedFromCatalog()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot, "MaldaLang.DesktopIDE", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"ThemeMenu\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewThemeLight_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Light\" Click=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Dark\" Click=", xaml, StringComparison.Ordinal);
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
