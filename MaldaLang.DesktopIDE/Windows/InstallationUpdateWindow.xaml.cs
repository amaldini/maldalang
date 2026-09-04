// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.IO;
using System.Windows;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;

namespace MaldaLang.DesktopIDE.Windows;

public partial class InstallationUpdateWindow : Window
{
    private readonly ThemeService _themeService;
    private readonly InstallationUpdateService _updateService = new();
    private readonly IReadOnlyList<string> _unsavedDocumentNames;
    private readonly InstallationLocation _location;
    private CancellationTokenSource? _cts;
    private UpdateCheckResult? _check;
    private bool _busy;

    public bool RestartRequired { get; private set; }

    public InstallationUpdateWindow(ThemeService themeService, IReadOnlyList<string>? unsavedDocumentNames = null)
    {
        InitializeComponent();
        _themeService = themeService;
        _unsavedDocumentNames = unsavedDocumentNames ?? Array.Empty<string>();
        _location = InstallationUpdateService.Locate();
        DialogTheming.Apply(this, _themeService.CurrentTheme);
        _themeService.ThemeChanged += OnThemeChanged;

        var currentTag = InstallationUpdateService.ReadInstalledTag(_location.RootPath);
        InstalledValueText.Text = string.IsNullOrWhiteSpace(currentTag) ? "Unknown" : currentTag;
        InstallFolderText.Text = _location.RootPath ?? "Not a zip install";

        Loaded += InstallationUpdateWindow_Loaded;
        Closed += InstallationUpdateWindow_Closed;
    }

    private async void InstallationUpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckForUpdateAsync();
    }

    private void InstallationUpdateWindow_Closed(object? sender, EventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private void OnThemeChanged(object? sender, Theme theme)
    {
        DialogTheming.Apply(this, theme);
    }

    private async Task CheckForUpdateAsync()
    {
        SetBusy(true, "Checking GitHub Releases…");
        LatestValueText.Text = "Checking…";
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            var latest = await _updateService.FetchLatestAsync(_cts.Token);
            var currentTag = InstallationUpdateService.ReadInstalledTag(_location.RootPath);
            _check = InstallationUpdateService.Evaluate(_location, currentTag, latest);
            LatestValueText.Text = InstallationUpdateService.NormalizeTag(latest.TagName);
            StatusText.Text = _check.Message;
            UpdateActionButton();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Check cancelled.";
        }
        catch (Exception ex)
        {
            LatestValueText.Text = "Unavailable";
            StatusText.Text = "Could not check GitHub Releases. " + ex.Message;
            _check = InstallationUpdateService.Evaluate(_location, InstallationUpdateService.ReadInstalledTag(_location.RootPath), null);
            UpdateActionButton();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_check?.WinX64Asset is null || string.IsNullOrWhiteSpace(_location.RootPath))
        {
            return;
        }

        if (!ConfirmRestart())
        {
            return;
        }

        var destination = _location.RootPath;
        var asset = _check.WinX64Asset;
        var tag = InstallationUpdateService.NormalizeTag(_check.Latest?.TagName ?? string.Empty);
        var cacheDir = Path.Combine(destination, ".cache");
        var zipPath = Path.Combine(cacheDir, asset.Name);
        var extractDir = Path.Combine(cacheDir, "extract-" + Guid.NewGuid().ToString("N"));

        SetBusy(true, "Downloading " + asset.Name + "…");
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.IsIndeterminate = false;
        DownloadProgress.Value = 0;

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var progress = new Progress<UpdateDownloadProgress>(report =>
            {
                if (report.TotalBytes is > 0)
                {
                    DownloadProgress.IsIndeterminate = false;
                    DownloadProgress.Value = Math.Min(100, 100.0 * report.BytesReceived / report.TotalBytes.Value);
                    StatusText.Text =
                        $"Downloading {asset.Name}… {InstallationUpdateService.FormatBytes(report.BytesReceived)} / {InstallationUpdateService.FormatBytes(report.TotalBytes.Value)}";
                }
                else
                {
                    DownloadProgress.IsIndeterminate = true;
                    StatusText.Text = $"Downloading {asset.Name}… {InstallationUpdateService.FormatBytes(report.BytesReceived)}";
                }
            });

            await _updateService.DownloadAsync(asset.BrowserDownloadUrl, zipPath, progress, token);

            StatusText.Text = "Extracting…";
            DownloadProgress.IsIndeterminate = true;
            await Task.Run(() => InstallationUpdateService.ExtractZip(zipPath, extractDir), token);

            var payloadRoot = InstallationUpdateService.ResolveExtractedRoot(extractDir);
            try
            {
                File.Delete(zipPath);
            }
            catch (Exception)
            {
                // Cache cleanup is best effort.
            }

            StatusText.Text = "Restarting to finish the update…";
            var request = new ApplyUpdateRequest(
                payloadRoot,
                destination,
                tag,
                Environment.ProcessId);
            InstallationUpdateService.StartApplyProcess(request);
            RestartRequired = true;
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Update cancelled.";
            TryDelete(extractDir);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update failed. " + ex.Message;
            TryDelete(extractDir);
        }
        finally
        {
            if (!RestartRequired)
            {
                SetBusy(false);
                DownloadProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OpenReleasesButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _check?.Latest?.HtmlUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            url = InstallationUpdateService.ReleasesPageUrl;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _cts?.Cancel();
            return;
        }

        Close();
    }

    private bool ConfirmRestart()
    {
        var message = "The Desktop IDE will close and restart to replace program files.";
        if (_unsavedDocumentNames.Count > 0)
        {
            message += "\n\nUnsaved files will be lost unless you save them first:\n- "
                       + string.Join("\n- ", _unsavedDocumentNames);
        }

        message += "\n\nContinue?";
        var result = MessageBox.Show(
            this,
            message,
            "Update Installation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private void UpdateActionButton()
    {
        if (_check is null || _check.WinX64Asset is null || _location.Kind != InstallationKind.Distribution)
        {
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "Download and Install";
            return;
        }

        UpdateButton.IsEnabled = !_busy;
        UpdateButton.Content = _check.Availability == UpdateAvailability.UpdateAvailable
            ? "Download and Install"
            : "Reinstall this version";
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        if (status != null)
        {
            StatusText.Text = status;
        }

        UpdateButton.IsEnabled = !busy && _check?.WinX64Asset != null && _location.Kind == InstallationKind.Distribution;
        CloseButton.Content = busy ? "Cancel" : "Close";
        if (!busy)
        {
            UpdateActionButton();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
