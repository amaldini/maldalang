// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class ComboBoxThemingGuardTests
{
    private static string RepoRoot =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void MainWindow_DoesNotHardcodeLightComboBoxItemColors()
    {
        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot, "MaldaLang.DesktopIDE", "MainWindow.xaml"));
        Assert.DoesNotContain("TargetType=\"ComboBoxItem\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void IdeChrome_ComboBoxTemplate_UsesThemeInputBrushes()
    {
        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot, "MaldaLang.DesktopIDE", "Themes", "IdeChrome.xaml"));

        Assert.Contains("x:Key=\"IdeComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"ComboBoxItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource InputBackgroundBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource InputForegroundBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"#212121\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"#E0E0E0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"#2196F3\"", xaml, StringComparison.Ordinal);
    }
}
