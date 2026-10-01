// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Threading;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Windows;

namespace MaldaLang.DesktopIDE;

public partial class App : Application
{
    internal static bool HoldStartupLauncher { get; private set; }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        if (InstallationUpdateService.TryParseApplyRequest(e.Args, out var request, out var error))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (request is null)
            {
                IdePromptWindow.Show(
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

        if (ShowcaseSession.TryParseLaunchArgs(e.Args, out var showcase, out var demoError))
        {
            if (showcase is null)
            {
                IdePromptWindow.Show(
                    demoError ?? "Could not start the showcase reel.",
                    "Showcase",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            ShowcaseSession.Pending = showcase;
            var demoWindow = new MainWindow();
            demoWindow.Show();
            return;
        }

        var location = InstallationUpdateService.Locate();
        if (location.Kind == InstallationKind.Distribution)
        {
            InstallationUpdateService.CleanupStaleCache(location.RootPath);
        }

        const int minimumSplashMs = 1200;
        HoldStartupLauncher = true;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new SplashWindow();
        splash.Show();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var shownAt = Environment.TickCount64;

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        var remaining = minimumSplashMs - (int)(Environment.TickCount64 - shownAt);
        if (remaining > 0 && !splash.Dismissed)
        {
            await Task.WhenAny(Task.Delay(remaining), splash.ClosedTask);
        }

        if (!splash.Dismissed)
        {
            splash.Dismiss();
        }

        await splash.ClosedTask;
        mainWindow.Activate();
        mainWindow.ShowStartupLauncher();
    }
}
