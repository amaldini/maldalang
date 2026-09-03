// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.ObjectModel;
using System.Windows;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE;

namespace MaldaLang.DesktopIDE.Windows;

public partial class StarterLauncherWindow : Window
{
    private readonly ObservableCollection<StarterOption> _starters = new();
    private readonly ObservableCollection<LearningBranch> _branches = new();
    private readonly ThemeService _themeService;
    private string _selectedTrack;

    public StarterOption? SelectedStarter { get; private set; }
    public string? SelectedExampleRelativePath { get; private set; }
    public bool StartBlankRequested { get; private set; }
    public bool BrowseExamplesRequested { get; private set; }

    public StarterLauncherWindow(ThemeService themeService, string initialTrack = "student")
    {
        InitializeComponent();

        _themeService = themeService;
        _selectedTrack = initialTrack;
        StartersListBox.ItemsSource = _starters;
        BranchesItemsControl.ItemsSource = _branches;
        BranchesDescriptionTextBlock.Text = $"After the core path, branch into {StarterCatalog.GetBranchTitleSummary()}.";

        _themeService.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;

        ApplyTheme(_themeService.CurrentTheme);
        LoadTrack(initialTrack);
    }

    private void ApplyTheme(Theme theme)
    {
        var isHighContrast = string.Equals(theme.Name, "HighContrast", StringComparison.OrdinalIgnoreCase);
        var isDarkTheme = IsDark(theme.WindowBackground);

        var panelBackground = theme.InputBackground;
        var secondaryPanelBackground = isHighContrast
            ? theme.WindowBackground
            : Mix(theme.InputBackground, theme.PrimaryButtonBackground, isDarkTheme ? 0.10 : 0.04);
        var accentSurface = isHighContrast
            ? theme.WindowBackground
            : Mix(theme.InputBackground, theme.PrimaryButtonBackground, isDarkTheme ? 0.18 : 0.10);
        var accentBorder = isHighContrast
            ? theme.BorderColor
            : Mix(theme.BorderColor, theme.PrimaryButtonBackground, isDarkTheme ? 0.45 : 0.25);
        var selectionBackground = isHighContrast
            ? theme.PrimaryButtonBackground
            : Mix(theme.PrimaryButtonBackground, theme.InputBackground, isDarkTheme ? 0.30 : 0.72);
        var selectionForeground = GetReadableTextColor(selectionBackground, theme.TextForeground, theme.PrimaryButtonForeground);
        var primaryForeground = GetReadableTextColor(theme.PrimaryButtonBackground, theme.TextForeground, theme.PrimaryButtonForeground);
        var accentText = GetReadableTextColor(accentSurface, theme.TextForeground, theme.PrimaryButtonForeground);

        Resources["WindowBackgroundBrush"] = ToBrush(theme.WindowBackground);
        Resources["PanelBackgroundBrush"] = ToBrush(panelBackground);
        Resources["SecondaryPanelBackgroundBrush"] = ToBrush(secondaryPanelBackground);
        Resources["BorderBrush"] = ToBrush(theme.BorderColor);
        Resources["PrimaryBrush"] = ToBrush(theme.PrimaryButtonBackground);
        Resources["PrimaryForegroundBrush"] = ToBrush(primaryForeground);
        Resources["TextBrush"] = ToBrush(theme.TextForeground);
        Resources["SubtleTextBrush"] = ToBrush(theme.TextSecondary);
        Resources["ButtonBackgroundBrush"] = ToBrush(theme.ButtonBackground);
        Resources["ButtonHoverBrush"] = ToBrush(theme.ButtonHover);
        Resources["AccentSurfaceBrush"] = ToBrush(accentSurface);
        Resources["AccentBorderBrush"] = ToBrush(accentBorder);
        Resources["AccentTextBrush"] = ToBrush(accentText);
        Resources["SelectionBrush"] = ToBrush(selectionBackground);
        Resources["SelectionTextBrush"] = ToBrush(selectionForeground);

        UpdateTrackButtons(theme, selectionBackground, selectionForeground);
    }

    private void LoadTrack(string track)
    {
        _selectedTrack = track;
        _starters.Clear();
        _branches.Clear();
        SelectedStarter = null;
        SelectedExampleRelativePath = null;
        TrackGuidanceTextBlock.Text = GetTrackGuidance(track);

        foreach (var starter in StarterCatalog.GetByTrack(track))
        {
            _starters.Add(starter);
        }

        foreach (var branch in StarterCatalog.GetBranchesForTrack(track))
        {
            _branches.Add(branch);
        }

        StartersListBox.SelectedIndex = _starters.Count > 0 ? 0 : -1;
        UpdateTrackButtons(_themeService.CurrentTheme);
        RenderSelection();
    }

    private void OnThemeChanged(object? sender, Theme theme)
    {
        Dispatcher.Invoke(() => ApplyTheme(theme));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
        Closed -= OnClosed;
    }

    private void StartersListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SelectedStarter = StartersListBox.SelectedItem as StarterOption;
        RenderSelection();
    }

    private void RenderSelection()
    {
        if (SelectedStarter == null)
        {
            TrackTextBlock.Text = string.Empty;
            TitleTextBlock.Text = "No starter selected";
            DescriptionTextBlock.Text = "Choose a starter from the list to see what it teaches and how it fits into the path.";
            GoalTextBlock.Text = string.Empty;
            TimeTextBlock.Text = string.Empty;
            HighlightsItemsControl.ItemsSource = null;
            UpdateBranchesVisibility();
            return;
        }

        TrackTextBlock.Text = GetSelectionLabel(SelectedStarter);
        TitleTextBlock.Text = SelectedStarter.Title;
        DescriptionTextBlock.Text = SelectedStarter.Description;
        GoalTextBlock.Text = $"Goal: {SelectedStarter.LearningGoal}";
        TimeTextBlock.Text = $"Estimated time: {SelectedStarter.EstimatedTime}";
        HighlightsItemsControl.ItemsSource = SelectedStarter.Highlights;
        UpdateBranchesVisibility();
    }

    private void UpdateBranchesVisibility()
    {
        if (BranchesPanel == null)
        {
            return;
        }

        var showBranches = SelectedStarter != null &&
            (!string.Equals(SelectedStarter.Track, "student", StringComparison.OrdinalIgnoreCase) ||
             StarterCatalog.IsLastStudentStarter(SelectedStarter.RelativeExamplePath));
        BranchesPanel.Visibility = showBranches ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StudentTrackButton_Click(object sender, RoutedEventArgs e) => LoadTrack("student");

    private void AiTrackButton_Click(object sender, RoutedEventArgs e) => LoadTrack("ai-builder");

    private void ShowcaseTrackButton_Click(object sender, RoutedEventArgs e) => LoadTrack("showcase");

    private void StartBlankButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedExampleRelativePath = null;
        StartBlankRequested = true;
        DialogResult = false;
        Close();
    }

    private void BrowseExamplesButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedExampleRelativePath = null;
        BrowseExamplesRequested = true;
        DialogResult = false;
        Close();
    }

    private void OpenStarterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStarter == null)
        {
            MessageBox.Show(this, "Choose a starter before continuing.", "Starter Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedExampleRelativePath = SelectedStarter.RelativeExamplePath;
        DialogResult = true;
        Close();
    }

    private void BranchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string relativeExamplePath ||
            string.IsNullOrWhiteSpace(relativeExamplePath))
        {
            return;
        }

        SelectedStarter = null;
        SelectedExampleRelativePath = relativeExamplePath;
        DialogResult = true;
        Close();
    }

    private static string GetTrackLabel(string track)
    {
        return track switch
        {
            "student" => "Learn programming",
            "ai-builder" => "Build with AI",
            "showcase" => "Commercial showcase",
            _ => track
        };
    }

    private string GetSelectionLabel(StarterOption starter)
    {
        var baseLabel = GetTrackLabel(starter.Track);
        if (!string.Equals(starter.Track, "student", StringComparison.OrdinalIgnoreCase))
        {
            return baseLabel;
        }

        var stepIndex = _starters.IndexOf(starter);
        if (stepIndex < 0)
        {
            return baseLabel;
        }

        return $"{baseLabel} - Step {stepIndex + 1} of {_starters.Count}";
    }

    private static string GetTrackGuidance(string track)
    {
        return track switch
        {
            "student" => "Open the programming starters from top to bottom. Each one adds one more core syntax idea before the next example.",
            "ai-builder" => "Start here if you already know the basics and want to move into prompts, agents, and local AI workflows.",
            "showcase" => "Jump straight to a polished MALDA capability demo, then branch into the areas you want to study next.",
            _ => "Choose a starter that matches how you want to learn MALDA."
        };
    }

    private void UpdateTrackButtons(Theme theme)
    {
        var selectionBackground = string.Equals(theme.Name, "HighContrast", StringComparison.OrdinalIgnoreCase)
            ? theme.PrimaryButtonBackground
            : Mix(theme.PrimaryButtonBackground, theme.InputBackground, IsDark(theme.WindowBackground) ? 0.30 : 0.72);
        var selectionForeground = GetReadableTextColor(selectionBackground, theme.TextForeground, theme.PrimaryButtonForeground);
        UpdateTrackButtons(theme, selectionBackground, selectionForeground);
    }

    private void UpdateTrackButtons(Theme theme, System.Windows.Media.Color selectionBackground, System.Windows.Media.Color selectionForeground)
    {
        ApplyTrackButtonState(StudentTrackButton, "student", theme, selectionBackground, selectionForeground);
        ApplyTrackButtonState(AiTrackButton, "ai-builder", theme, selectionBackground, selectionForeground);
        ApplyTrackButtonState(ShowcaseTrackButton, "showcase", theme, selectionBackground, selectionForeground);
    }

    private void ApplyTrackButtonState(System.Windows.Controls.Button button, string track, Theme theme, System.Windows.Media.Color selectionBackground, System.Windows.Media.Color selectionForeground)
    {
        var isSelected = string.Equals(_selectedTrack, track, StringComparison.OrdinalIgnoreCase);
        button.Background = ToBrush(isSelected ? selectionBackground : theme.ButtonBackground);
        button.Foreground = ToBrush(isSelected ? selectionForeground : theme.ButtonForeground);
        button.BorderBrush = ToBrush(isSelected ? selectionBackground : theme.ButtonBorder);
        button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static System.Windows.Media.SolidColorBrush ToBrush(System.Windows.Media.Color color)
    {
        var brush = new System.Windows.Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static bool IsDark(System.Windows.Media.Color color)
    {
        return ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255d < 0.5;
    }

    private static System.Windows.Media.Color Mix(System.Windows.Media.Color baseColor, System.Windows.Media.Color accentColor, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        var inverse = 1d - amount;
        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round((baseColor.R * inverse) + (accentColor.R * amount)),
            (byte)Math.Round((baseColor.G * inverse) + (accentColor.G * amount)),
            (byte)Math.Round((baseColor.B * inverse) + (accentColor.B * amount)));
    }

    private static System.Windows.Media.Color GetReadableTextColor(System.Windows.Media.Color background, System.Windows.Media.Color defaultColor, System.Windows.Media.Color alternateColor)
    {
        return IsDark(background) ? alternateColor : defaultColor;
    }
}
