// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Windows;

namespace MaldaLang.DesktopIDE;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (InstallationUpdateService.TryParseApplyRequest(e.Args, out var request, out var error))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (request is null)
            {
                MessageBox.Show(
                    error ?? "Could not apply the installation update.",
                    "Update failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var applyWindow = new ApplyingUpdateWindow(request);
            applyWindow.Show();
            return;
        }

        var location = InstallationUpdateService.Locate();
        if (location.Kind == InstallationKind.Distribution)
        {
            InstallationUpdateService.CleanupStaleCache(location.RootPath);
        }

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
