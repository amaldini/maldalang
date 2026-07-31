// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Trace Viewer window: event list, details (pretty JSON), file state for FileEdit,
// navigation (go to step, prev/next). Selection updates details and ReplayContext.StepTo.
// Placeholder comments for future time-travel: RunFromHere(), ReplayToHere(), BranchFromHere().

namespace MaldaLang.DesktopIDE.Windows;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.Runtime.Tracing;
using MaldaLang.TraceViewer;

public partial class TraceViewerWindow : Window
{
    private readonly ThemeService _themeService;
    private readonly TraceViewerSession _session;
    private bool _suppressSelectionChange;

    public TraceViewerWindow(ThemeService themeService, TraceViewerSession session)
    {
        InitializeComponent();
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        ApplyTheme(_themeService.CurrentTheme);
        _themeService.ThemeChanged += (_, theme) => Dispatcher.Invoke(() => ApplyTheme(theme));

        EventsDataGrid.ItemsSource = _session.Events;
        if (_session.Events.Count > 0)
        {
            EventsDataGrid.SelectedIndex = 0;
            OnSelectionChanged();
        }
    }

    private void ApplyTheme(Theme theme)
    {
        Resources["WindowBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.WindowBackground);
        Resources["MainBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.MainBackground);
        Resources["ToolbarBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.ToolbarBackground);
        Resources["ToolbarBorderBrush"] = new System.Windows.Media.SolidColorBrush(theme.ToolbarBorder);
        Resources["TextForegroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.TextForeground);
        Resources["TextSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(theme.TextSecondary);
        Resources["ButtonBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.ButtonBackground);
        Resources["ButtonForegroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.ButtonForeground);
        Resources["ButtonBorderBrush"] = new System.Windows.Media.SolidColorBrush(theme.ButtonBorder);
        Resources["InputBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.InputBackground);
        Resources["InputForegroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.InputForeground);
        Resources["InputBorderBrush"] = new System.Windows.Media.SolidColorBrush(theme.InputBorder);
        Resources["ListBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.ListBackground);
        Resources["ListForegroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.ListForeground);
        Resources["ListBorderBrush"] = new System.Windows.Media.SolidColorBrush(theme.ListBorder);
        Resources["BorderBrush"] = new System.Windows.Media.SolidColorBrush(theme.BorderColor);
        Resources["GridSplitterBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(theme.GridSplitterBackground);
    }

    private void EventsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChange) return;
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        var idx = EventsDataGrid.SelectedIndex;
        var hasSelection = idx >= 0 && idx < _session.Events.Count;
        if (ReplayToHereButton != null) ReplayToHereButton.IsEnabled = hasSelection;
        if (RunFromHereButton != null) RunFromHereButton.IsEnabled = hasSelection;
        if (BranchFromHereButton != null) BranchFromHereButton.IsEnabled = hasSelection;
        if (!hasSelection) return;

        var vm = _session.Events[idx];
        _session.Context.StepTo(idx);

        var payloadJson = vm.RawEvent.Payload as string ?? "{}";
        DetailsTextBox.Text = PrettifyPayload(payloadJson);

        if (vm.RawEvent.Type == TraceEventType.FileEdit)
        {
            FileStateLabel.Text = FormatFileEditState(vm.RawEvent, payloadJson);
            var afterContent = TryGetAfterContent(payloadJson);
            FileStateTextBox.Text = afterContent ?? "(no content or too large)";
        }
        else
        {
            FileStateLabel.Text = "Select a FileEdit event to see path, operation, and content.";
            FileStateTextBox.Text = "";
        }
    }

    private static string PrettifyPayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    private static string FormatFileEditState(TraceEvent evt, string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var r = doc.RootElement;
            var path = r.TryGetProperty("path", out var p) ? p.GetString() : null;
            var op = r.TryGetProperty("operation", out var o) ? o.GetString() : null;
            var after = r.TryGetProperty("afterContent", out var a) ? a.GetString() : null;
            var len = after?.Length ?? 0;
            return $"Path: {path ?? "-"}  |  Operation: {op ?? "-"}  |  afterContent length: {len}";
        }
        catch
        {
            return "Path, operation, and content for FileEdit events.";
        }
    }

    private static string? TryGetAfterContent(string payloadJson)
    {
        const int maxShow = 50_000;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("afterContent", out var a)) return null;
            var s = a.GetString();
            if (s == null) return null;
            if (s.Length > maxShow) return s[..maxShow] + "\n… (truncated)";
            return s;
        }
        catch
        {
            return null;
        }
    }

    private void GoToStepButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyGoToStep();
    }

    private void GoToStepTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ApplyGoToStep();
    }

    private void ApplyGoToStep()
    {
        if (!int.TryParse(GoToStepTextBox.Text, out var step) || step < 0 || step >= _session.Events.Count)
            return;
        _suppressSelectionChange = true;
        try
        {
            EventsDataGrid.SelectedIndex = step;
            EventsDataGrid.ScrollIntoView(EventsDataGrid.Items[step]);
            OnSelectionChanged();
        }
        finally
        {
            _suppressSelectionChange = false;
        }
    }

    private void PrevStepButton_Click(object sender, RoutedEventArgs e)
    {
        var idx = EventsDataGrid.SelectedIndex;
        if (idx <= 0) return;
        _suppressSelectionChange = true;
        try
        {
            EventsDataGrid.SelectedIndex = idx - 1;
            EventsDataGrid.ScrollIntoView(EventsDataGrid.Items[idx - 1]);
            OnSelectionChanged();
        }
        finally
        {
            _suppressSelectionChange = false;
        }
    }

    private void NextStepButton_Click(object sender, RoutedEventArgs e)
    {
        var idx = EventsDataGrid.SelectedIndex;
        if (idx < 0 || idx >= _session.Events.Count - 1) return;
        _suppressSelectionChange = true;
        try
        {
            EventsDataGrid.SelectedIndex = idx + 1;
            EventsDataGrid.ScrollIntoView(EventsDataGrid.Items[idx + 1]);
            OnSelectionChanged();
        }
        finally
        {
            _suppressSelectionChange = false;
        }
    }

    private void ReplayToHereButton_Click(object sender, RoutedEventArgs e)
    {
        var idx = EventsDataGrid.SelectedIndex;
        if (idx < 0 || idx >= _session.Events.Count)
        {
            MessageBox.Show(this, "Select an event first.", "Replay to Here", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _session.Context.StepTo(idx);
        var filePaths = _session.Context.Files.Keys.ToList();
        string? defaultDir = null;
        if (!string.IsNullOrEmpty(_session.SessionId))
            defaultDir = Path.Combine(Path.GetTempPath(), "TraceReplay_" + _session.SessionId);
        var dlg = new ReplayToHereDialog(filePaths, defaultDir) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var restored = TraceReplayEngine.RestoreStateToStep(_session.Context, idx, dlg.WorkingDirectory);
            var msg = restored.Count == 0
                ? "No files to restore at this step."
                : $"Restored {restored.Count} file(s):\n" + string.Join("\n", restored.Take(20)) + (restored.Count > 20 ? "\n..." : "");
            MessageBox.Show(this, msg, "Replay to Here", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Replay to Here failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RunFromHereButton_Click(object sender, RoutedEventArgs e)
    {
        var idx = EventsDataGrid.SelectedIndex;
        if (idx < 0 || idx >= _session.Events.Count)
        {
            MessageBox.Show(this, "Select an event first.", "Run from Here", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _session.Context.StepTo(idx);
        var filePaths = _session.Context.Files.Keys.ToList();
        string? defaultDir = Path.Combine(Path.GetTempPath(), "TraceReplay_" + (_session.SessionId ?? "run"));
        var dlg = new ReplayToHereDialog(filePaths, defaultDir) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            TraceReplayEngine.RestoreStateToStep(_session.Context, idx, dlg.WorkingDirectory);
            var state = TraceReplayEngine.PrepareAgentFromStep(_session, idx);
            var replayWindow = new AgentReplayWindow(_themeService, dlg.WorkingDirectory, state) { Owner = this };
            replayWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Run from Here failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BranchFromHereButton_Click(object sender, RoutedEventArgs e)
    {
        var idx = EventsDataGrid.SelectedIndex;
        if (idx < 0 || idx >= _session.Events.Count)
        {
            MessageBox.Show(this, "Select an event first.", "Branch from Here", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _session.Context.StepTo(idx);
        var filePaths = _session.Context.Files.Keys.ToList();
        string? defaultDir = Path.Combine(Path.GetTempPath(), "TraceReplay_" + (_session.SessionId ?? "branch"));
        var dlg = new ReplayToHereDialog(filePaths, defaultDir) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            TraceReplayEngine.RestoreStateToStep(_session.Context, idx, dlg.WorkingDirectory);
            var state = TraceReplayEngine.PrepareAgentFromStep(_session, idx);
            var branchSessionId = $"{_session.SessionId ?? "trace"}-branch-{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var traceBaseDir = Path.Combine(dlg.WorkingDirectory, "traces");
            var replayWindow = new AgentReplayWindow(_themeService, dlg.WorkingDirectory, state, branchSessionId, traceBaseDir) { Owner = this };
            replayWindow.Show();
            var tracePath = Path.Combine(traceBaseDir, branchSessionId + ".malda-trace.jsonl");
            MessageBox.Show(this, $"Branch created. New trace will be written to:\n{tracePath}", "Branch from Here", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Branch from Here failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
