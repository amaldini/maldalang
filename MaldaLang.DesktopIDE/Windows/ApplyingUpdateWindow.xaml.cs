// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Windows;
using MaldaLang.DesktopIDE.Services;

namespace MaldaLang.DesktopIDE.Windows;

public partial class ApplyingUpdateWindow : Window
{
    private readonly ApplyUpdateRequest _request;
    private bool _finished;

    public ApplyingUpdateWindow(ApplyUpdateRequest request)
    {
        InitializeComponent();
        _request = request;
        StatusText.Text = $"Installing {InstallationUpdateService.NormalizeTag(request.Tag)}…";
        Closing += ApplyingUpdateWindow_Closing;
        Loaded += ApplyingUpdateWindow_Loaded;
    }

    private async void ApplyingUpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() =>
            {
                InstallationUpdateService.WaitForProcessExit(_request.WaitPid, TimeSpan.FromMinutes(2));
                InstallationUpdateService.ApplyExtractedRelease(
                    _request.PayloadRoot,
                    _request.Destination,
                    _request.Tag);
            });

            var exe = InstallationUpdateService.DesktopExePath(_request.Destination);
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exe)
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _finished = true;
            Application.Current.Shutdown();
        }
    }

    private void ApplyingUpdateWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_finished)
        {
            e.Cancel = true;
        }
    }
}
