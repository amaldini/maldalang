// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.IO;
using MaldaLang.IDE;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void SetupExamples()
    {
        // Examples are now loaded from files via ExampleBrowserWindow
        // This method is kept for compatibility but no longer populates a ComboBox
    }

    private void SetupDataBinding()
    {
        UpdateButtonStates();
    }

    private void BrowseExamplesButton_Click(object sender, RoutedEventArgs e)
    {
        var browserWindow = new Windows.ExampleBrowserWindow(_themeService);
        browserWindow.Owner = this;
        browserWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        
        if (browserWindow.ShowDialog() == true && browserWindow.SelectedExample != null)
        {
            LoadExample(browserWindow.SelectedExample);
        }
    }
    
    private void LoadExample(MaldaLang.IDE.ExampleProgram example)
    {
        // Stop debugger if running
        if (_debuggerService.State.IsRunning)
        {
            _debuggerHook?.Stop();
            _debugCancellation?.Cancel();
            _debuggerService.Stop();
            _debuggerHook = null;
            _debugTask = null;
            _debugCancellation?.Dispose();
            _debugCancellation = null;
            ResetDualDebugState();
            _ = StopJsDebuggerAsync();
            ClearCurrentLineHighlight();
            UpdateButtonStates();
        }
        
        // Stop regular run if running
        if (_runTask != null && !_runTask.IsCompleted)
        {
            _runCancellation?.Cancel();
            _runTask = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            UpdateButtonStates();
        }
        
        // Clear breakpoints for the old file before loading new example
        var oldFilePath = GetCurrentPhysicalFilePath();
        _debuggerService.ClearBreakpointsForFile(oldFilePath);
        
        var resolvedExamplePath = TryResolveExampleAbsolutePath(example.AbsoluteFilePath);
        if (string.IsNullOrWhiteSpace(resolvedExamplePath))
        {
            resolvedExamplePath = TryResolveExampleAbsolutePath(example.FilePath);
        }
        if (!string.IsNullOrWhiteSpace(resolvedExamplePath) && File.Exists(resolvedExamplePath))
        {
            OpenFileAndIncludedDocuments(resolvedExamplePath);
        }
        else
        {
            ResetToSingleDocument(example.Code, null);
            UpdateAIChatPanelContext(); // Fallback to inline example code
        }

        SetCurrentExample(example);
    }

    private bool OpenExampleByRelativePath(string? relativeExamplePath, string statusPrefix)
    {
        var example = ExampleProgramsService.GetExampleByRelativePath(relativeExamplePath);
        if (example == null)
        {
            IdePromptWindow.Show(this, "The requested example could not be loaded.", "Example Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        LoadExample(example);
        SetOutputText($"{statusPrefix}: {example.Name}");
        return true;
    }

    private void StartWithMalda_Click(object sender, RoutedEventArgs e)
    {
        ShowStarterLauncher(initialTrack: "student", fallbackToBlank: false);
    }

    internal void ShowStartupLauncher()
    {
        if (_showcase != null || !string.IsNullOrWhiteSpace(CodeEditor.Text))
        {
            return;
        }

        ShowStarterLauncher(initialTrack: "student", fallbackToBlank: false);
    }

    private void ShowStarterLauncher(string initialTrack, bool fallbackToBlank)
    {
        var starterWindow = new Windows.StarterLauncherWindow(_themeService, initialTrack);
        starterWindow.Owner = this;
        starterWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (starterWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(starterWindow.SelectedExampleRelativePath))
        {
            if (OpenExampleByRelativePath(starterWindow.SelectedExampleRelativePath, "Loaded starter"))
            {
                return;
            }
        }

        if (starterWindow.BrowseExamplesRequested)
        {
            BrowseExamplesButton_Click(this, new RoutedEventArgs());
            return;
        }

        if (starterWindow.StartBlankRequested || fallbackToBlank)
        {
            ResetToSingleDocument("", null);
            UpdateAIChatPanelContext();
        }
    }

    private void SetCurrentExample(ExampleProgram? example)
    {
        _currentExample = example;
        _learningBranchBannerDismissed = false;
        UpdateLearningBranchBanner();
    }

    private void CloseLearningBranchBanner_Click(object sender, RoutedEventArgs e)
    {
        _learningBranchBannerDismissed = true;
        UpdateLearningBranchBanner();
    }

    private void UpdateLearningBranchBanner()
    {
        if (LearningBranchBanner == null || LearningBranchButtonsPanel == null)
        {
            return;
        }

        LearningBranchButtonsPanel.Children.Clear();

        if (_learningBranchBannerDismissed || _currentExample == null)
        {
            LearningBranchBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var examplePath = _currentExample.FilePath;
        var nextStarter = StarterCatalog.GetNextStudentStarter(examplePath);
        if (nextStarter != null)
        {
            LearningBranchTitleTextBlock.Text = $"Next lesson after {_currentExample.Name}";
            LearningBranchDescriptionTextBlock.Text = nextStarter.LearningGoal;
            var nextButton = new Button
            {
                Content = $"Next: {nextStarter.Title}",
                Tag = nextStarter.RelativeExamplePath,
                ToolTip = nextStarter.Description,
                Margin = new Thickness(4),
                Padding = new Thickness(10, 6, 10, 6)
            };
            nextButton.Click += LearningNextButton_Click;
            LearningBranchButtonsPanel.Children.Add(nextButton);
            LearningBranchBanner.Visibility = Visibility.Visible;
            return;
        }

        if (!StarterCatalog.IsLastStudentStarter(examplePath))
        {
            LearningBranchBanner.Visibility = Visibility.Collapsed;
            return;
        }

        LearningBranchTitleTextBlock.Text = $"Recommended next tracks after {_currentExample.Name}";
        LearningBranchDescriptionTextBlock.Text = $"After the core path, branch into {StarterCatalog.GetBranchTitleSummary()}.";

        foreach (var branch in StarterCatalog.GetBranches())
        {
            var button = new Button
            {
                Content = $"{branch.Title} ({branch.EstimatedTime})",
                Tag = branch.RelativeExamplePath,
                ToolTip = branch.Description,
                Margin = new Thickness(4),
                Padding = new Thickness(10, 6, 10, 6)
            };
            button.Click += LearningBranchButton_Click;
            LearningBranchButtonsPanel.Children.Add(button);
        }

        LearningBranchBanner.Visibility = Visibility.Visible;
    }
}
