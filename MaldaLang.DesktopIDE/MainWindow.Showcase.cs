// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MaldaLang.DesktopIDE.Services;
using Microsoft.Web.WebView2.Core;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private const int DwmwaExtendedFrameBounds = 9;
    private static readonly Regex OpenUrlRegex = new(@"Open:\s*(https?://\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect rect, int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void ApplyShowcaseWindowBounds()
    {
        if (_showcase == null)
        {
            return;
        }

        var playlist = _showcase.Playlist;
        var area = SystemParameters.WorkArea;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Left = playlist.Left == 0 ? area.Left : playlist.Left;
        Top = playlist.Top == 0 ? area.Top : playlist.Top;
        Width = Math.Max(640, Math.Min(playlist.Width, area.Width));
        Height = Math.Max(480, Math.Min(playlist.Height, area.Height));
        BringShowcaseToFront();
    }

    private void BringShowcaseToFront()
    {
        Topmost = true;
        var browser = _showcaseBrowser is { IsVisible: true } ? _showcaseBrowser : null;
        var target = browser ?? (Window)this;
        var hwnd = new WindowInteropHelper(target).Handle;
        if (hwnd == IntPtr.Zero)
        {
            if (browser == null)
            {
                Activate();
            }

            return;
        }

        if (GetForegroundWindow() == hwnd)
        {
            return;
        }

        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(foregroundThread, currentThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(foregroundThread, currentThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }

        target.Activate();
    }

    private Windows.ExampleBrowserWindow? _showcaseBrowser;

    private long ShowcaseNowMs => _showcaseClock?.ElapsedMilliseconds ?? 0;

    private async Task WaitUntilShowcaseMs(int targetMs)
    {
        while (ShowcaseNowMs < targetMs)
        {
            var remaining = targetMs - ShowcaseNowMs;
            var slice = (int)Math.Min(Math.Max(remaining, 1), 200);
            await Task.Delay(slice);
        }
    }

    private int ShowcaseSlotMs(int cueEndMs, int readyTimeoutMs)
    {
        var remaining = cueEndMs - ShowcaseNowMs;
        if (remaining < 1)
        {
            return 1;
        }

        if (remaining > readyTimeoutMs)
        {
            return readyTimeoutMs;
        }

        return (int)remaining;
    }

    private async Task BeginShowcaseSplashAsync(string? lyric = null)
    {
        // WebBrowser and WebView2 are HWND airspace: they paint over any WPF overlay.
        _showcaseAirspace = new (FrameworkElement Element, Visibility Visibility)[]
        {
            (OutputWebBrowser, OutputWebBrowser.Visibility),
            (ToolCallsWebBrowser, ToolCallsWebBrowser.Visibility),
            (WebUiWebView, WebUiWebView.Visibility),
            (ManualWebView, ManualWebView.Visibility)
        };

        foreach (var (element, _) in _showcaseAirspace)
        {
            element.Visibility = Visibility.Collapsed;
        }

        _showcaseCaption = "";
        var sung = string.IsNullOrWhiteSpace(lyric) ? _showcase?.Lyrics.Intro : lyric;
        SetShowcaseLyric(sung);
        UpdateWindowChrome();
        SplashOverlay.Visibility = Visibility.Visible;
        _showcaseSplashVisible = true;
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        BringShowcaseToFront();
    }

    private void EndShowcaseSplash()
    {
        if (!_showcaseSplashVisible && _showcaseAirspace == null)
        {
            return;
        }

        SplashOverlay.Visibility = Visibility.Collapsed;
        _showcaseSplashVisible = false;
        if (_showcaseAirspace != null)
        {
            foreach (var (element, visibility) in _showcaseAirspace)
            {
                element.Visibility = visibility;
            }

            _showcaseAirspace = null;
        }
    }

    private async Task PlayBrowseTourAsync(int frameX, int frameY, int frameWidth, int frameHeight)
    {
        var picks = _showcase?.Playlist.Browse;
        if (picks == null || picks.Count == 0)
        {
            return;
        }

        var browser = new Windows.ExampleBrowserWindow(_themeService)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            Topmost = true
        };
        var margin = 28.0;
        browser.Width = Math.Max(960, ActualWidth - (margin * 2));
        browser.Height = Math.Max(620, ActualHeight - (margin * 2) - 44);
        browser.Left = Left + ((ActualWidth - browser.Width) / 2);
        browser.Top = Top + ((ActualHeight - browser.Height) / 2) + 8;
        _showcaseBrowser = browser;
        browser.Show();
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            for (var index = 0; index < picks.Count; index++)
            {
                var pick = picks[index];
                await WaitUntilShowcaseMs(pick.StartMs);
                if (ShowcaseNowMs >= pick.EndMs)
                {
                    continue;
                }

                _showcaseCaption = string.IsNullOrWhiteSpace(pick.Caption) ? "Examples" : pick.Caption;
                SetShowcaseLyric(_showcase?.Lyrics.BrowseLyric(index, picks.Count));
                UpdateWindowChrome();
                browser.Title = "Browse Examples — " + _showcaseCaption;
                if (!browser.TrySelectExample(pick.File, pick.FocusLine))
                {
                    SetOutputText($"Showcase browse missed {pick.File}.", isError: true);
                }

                BringShowcaseToFront();
                WriteShowcaseStatus("playing", frameX, frameY, frameWidth, frameHeight, _showcaseCaption, error: null);
                await WaitUntilShowcaseMs(pick.EndMs);
            }
        }
        finally
        {
            _showcaseBrowser = null;
            if (browser.IsVisible)
            {
                browser.Close();
            }
        }
    }

    private async Task PlayManualTourAsync(int frameX, int frameY, int frameWidth, int frameHeight)
    {
        var pages = _showcase?.Playlist.Manual;
        if (pages == null || pages.Count == 0)
        {
            return;
        }

        var captions = new List<string>(pages.Count);
        foreach (var page in pages)
        {
            captions.Add(page.Caption);
        }

        try
        {
            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index];
                await WaitUntilShowcaseMs(page.StartMs);
                if (ShowcaseNowMs >= page.EndMs)
                {
                    continue;
                }

                _showcaseCaption = string.IsNullOrWhiteSpace(page.Caption) ? "Manual" : page.Caption;
                SetShowcaseLyric(_showcase?.Lyrics.ManualLyric(index, captions));
                UpdateWindowChrome();
                BringShowcaseToFront();
                var ready = await ShowReferenceManualPageAsync(
                    page.File,
                    ShowcaseSlotMs(page.EndMs, _showcase!.Playlist.ReadyTimeoutMs),
                    page.Anchor);
                if (!ready)
                {
                    SetOutputText($"Showcase manual missed {page.File}.", isError: true);
                }

                WriteShowcaseStatus("playing", frameX, frameY, frameWidth, frameHeight, _showcaseCaption, error: null);
                await WaitUntilShowcaseMs(page.EndMs);
            }
        }
        finally
        {
            RestoreShowcaseLayout();
        }
    }

    private async Task RunShowcaseAsync()
    {
        if (_showcase == null)
        {
            return;
        }

        try
        {
            PrepareShowcaseLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            try
            {
                await EnsureWebUiCoreAsync();
            }
            catch
            {
                // Preview scenes report their own failure if WebView2 is missing.
            }

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            BringShowcaseToFront();
            if (!TryGetFramePixels(out var x, out var y, out var width, out var height))
            {
                throw new InvalidOperationException("Could not measure the showcase window.");
            }

            TryDeleteFile(_showcase.StartPath);
            await BeginShowcaseSplashAsync();
            var holdClosingSplash = false;
            try
            {
                WriteShowcaseStatus("armed", x, y, width, height, scene: null, error: null);
                var keepInFront = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                keepInFront.Tick += (_, _) => BringShowcaseToFront();
                keepInFront.Start();

                if (!_showcase.AutoStart)
                {
                    var startDeadline = Environment.TickCount64 + 180_000;
                    while (!File.Exists(_showcase.StartPath))
                    {
                        if (Environment.TickCount64 > startDeadline)
                        {
                            throw new TimeoutException("Showcase start file was not created within 3 minutes.");
                        }

                        await Task.Delay(30);
                    }
                }

                _showcaseClock = Stopwatch.StartNew();
                BringShowcaseToFront();
                WriteShowcaseStatus("playing", x, y, width, height, "MALDA", error: null);
                await WaitUntilShowcaseMs(_showcase.Playlist.OpeningMs);
                EndShowcaseSplash();

                await PlayBrowseTourAsync(x, y, width, height);
                await PlayManualTourAsync(x, y, width, height);

                for (var index = 0; index < _showcase.Playlist.Scenes.Count; index++)
                {
                    var scene = _showcase.Playlist.Scenes[index];
                    await PlayShowcaseSceneAsync(scene, x, y, width, height);
                }

                if (_showcase.Playlist.ClosingMs > 0)
                {
                    StopActiveExecution();
                    QuietShowcasePreview();
                    holdClosingSplash = true;
                    var outro = _showcase.Lyrics.Outro;
                    if (string.IsNullOrWhiteSpace(outro))
                    {
                        outro = "Malda.";
                    }

                    await BeginShowcaseSplashAsync(outro);
                    WriteShowcaseStatus("playing", x, y, width, height, "MALDA", error: null);
                    await WaitUntilShowcaseMs(_showcase.Playlist.EndMs);
                }

                WriteShowcaseStatus("done", x, y, width, height, scene: null, error: null);
                keepInFront.Stop();
                Application.Current.Shutdown(0);
            }
            finally
            {
                if (!holdClosingSplash)
                {
                    EndShowcaseSplash();
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                WriteShowcaseStatus("error", 0, 0, 0, 0, scene: null, ex.Message);
            }
            catch
            {
                // The recorder times out if the status file cannot be written.
            }

            Application.Current.Shutdown(1);
        }
    }

    private void PrepareShowcaseLayout()
    {
        _isSyntaxPanelVisible = false;
        SyntaxPanelColumn.MinWidth = 0;
        UpdateSyntaxPanelVisibility();
        EditorColumn.Width = new GridLength(1.15, GridUnitType.Star);
        SidebarColumn.Width = new GridLength(1, GridUnitType.Star);
        BottomPanelRow.MinHeight = 0;
        BottomPanelRow.Height = new GridLength(0);
    }

    private async Task PlayShowcaseSceneAsync(ShowcaseScene scene, int frameX, int frameY, int frameWidth, int frameHeight)
    {
        await WaitUntilShowcaseMs(scene.StartMs);
        if (ShowcaseNowMs >= scene.EndMs)
        {
            return;
        }

        using var navigationCancel = new CancellationTokenSource();
        Task<bool>? navigation = null;
        try
        {
            RestoreShowcaseLayout();
            StopActiveExecution();
            _lastDetectedWebUiUrl = null;

            _showcaseCaption = scene.Caption ?? "";
            var sceneIndex = _showcase?.Playlist.Scenes.IndexOf(scene) ?? -1;
            SetShowcaseLyric(sceneIndex < 0 ? null : _showcase?.Lyrics.SceneLyric(sceneIndex));
            OpenFileAndIncludedDocuments(scene.AbsolutePath);
            DropUntitledShowcaseDocument();
            BringShowcaseToFront();
            FocusShowcaseLine(scene.FocusLine);
            UpdateWindowChrome();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            BringShowcaseToFront();
            WriteShowcaseStatus("playing", frameX, frameY, frameWidth, frameHeight, scene.Caption, error: null);

            var timeout = ShowcaseSlotMs(scene.EndMs, _showcase!.Playlist.ReadyTimeoutMs);
            var ready = true;
            if (scene.Panel == "preview")
            {
                navigation = WaitForContentNavigationAsync(timeout, navigationCancel.Token, scene.EndMs);
                _showcasePreviewAutoplay = true;
                try
                {
                    await PreviewCurrentDocumentAsync();
                }
                finally
                {
                    _showcasePreviewAutoplay = false;
                }

                ready = await navigation;
            }
            else if (scene.Panel == "server")
            {
                StartInterpretRunForActiveDocument();
                var url = await WaitForOpenUrlAsync(timeout, scene.EndMs);
                var uri = url == null ? null : TryResolveWebViewUri(url);
                if (uri == null)
                {
                    ready = false;
                }
                else
                {
                    navigation = WaitForContentNavigationAsync(timeout, navigationCancel.Token, scene.EndMs);
                    var pageUrl = url ?? "";
                    _lastDetectedWebUiUrl = pageUrl;
                    await OpenUriInWebUiPanelAsync(uri, pageUrl, switchToTab: true, ensureUiHost: false);
                    ready = await navigation;
                }
            }
            else
            {
                SwitchToTab("output");
                StartInterpretRunForActiveDocument();
                await WaitForInterpretFinishedAsync(timeout, scene.EndMs);
            }

            if (scene.Maximize && ready && scene.Panel == "output" && ShowcaseNowMs < scene.EndMs)
            {
                var beatAt = (int)Math.Min(scene.EndMs, ShowcaseNowMs + Math.Max(0, _showcase.Playlist.SplitBeatMs));
                await WaitUntilShowcaseMs(beatAt);
                if (ShowcaseNowMs < scene.EndMs)
                {
                    SetSidebarPanelMaximized("output", true);
                }
            }

            await WaitUntilShowcaseMs(scene.EndMs);
        }
        catch (Exception ex)
        {
            navigationCancel.Cancel();
            SetOutputText($"Showcase scene failed ({scene.File}): {ex.Message}", isError: true);
            SwitchToTab("output");
            await WaitUntilShowcaseMs(scene.EndMs);
        }
    }

    private void QuietShowcasePreview()
    {
        try
        {
            if (WebUiWebView.CoreWebView2 == null)
            {
                return;
            }

            WebUiWebView.CoreWebView2.Stop();
            WebUiWebView.CoreWebView2.Navigate("about:blank");
        }
        catch (Exception)
        {
            // The preview was never created, or WebView2 refused the stop. The splash still covers the window.
        }
    }

    private void StartInterpretRunForActiveDocument()
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var sourceForExecution = GetSourceForExecution(activeDocument);
        StartInterpretRun(sourceForExecution.Source, ProgramInputTextBox.Text, sourceForExecution.SourceFilePath);
    }

    private void DropUntitledShowcaseDocument()
    {
        if (_activeDocumentKey == UntitledDocumentKey || !_openDocuments.Remove(UntitledDocumentKey))
        {
            return;
        }

        _documentOrder.Remove(UntitledDocumentKey);
        RefreshDocumentTabs();
    }

    private void FocusShowcaseLine(int? line)
    {
        if (line is not int target || target < 1 || CodeEditor.Document == null)
        {
            return;
        }

        var clamped = Math.Min(target, Math.Max(1, CodeEditor.Document.LineCount));
        CodeEditor.ScrollToLine(clamped);
        CodeEditor.TextArea.Caret.Line = clamped;
        CodeEditor.TextArea.Caret.Column = 1;
    }

    private void RestoreShowcaseLayout()
    {
        if (_maximizedSidebarTab != null)
        {
            SetSidebarPanelMaximized(_maximizedSidebarTab, false);
        }
    }

    private async Task WaitForInterpretFinishedAsync(int timeoutMs, int cueEndMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (_runTask != null && !_runTask.IsCompleted && Environment.TickCount64 < deadline && ShowcaseNowMs < cueEndMs)
        {
            await Task.Delay(50);
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private async Task<string?> WaitForOpenUrlAsync(int timeoutMs, int cueEndMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && ShowcaseNowMs < cueEndMs)
        {
            string output;
            try
            {
                output = _executionService.GetCurrentOutput();
            }
            catch
            {
                output = "";
            }

            var match = OpenUrlRegex.Match(output);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            if (_runTask == null || _runTask.IsCompleted)
            {
                return null;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private async Task<bool> WaitForContentNavigationAsync(int timeoutMs, CancellationToken cancellation, int cueEndMs)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var uri = WebUiWebView.Source?.AbsoluteUri ?? "";
            if (uri.Length == 0 || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            WebUiWebView.NavigationCompleted -= Handler;
            done.TrySetResult(e.IsSuccess);
        }

        WebUiWebView.NavigationCompleted += Handler;
        try
        {
            var slot = ShowcaseSlotMs(cueEndMs, timeoutMs);
            var delay = Task.Delay(slot, cancellation);
            var finished = await Task.WhenAny(done.Task, delay);
            return finished == done.Task && done.Task.Result;
        }
        finally
        {
            WebUiWebView.NavigationCompleted -= Handler;
        }
    }

    private bool TryGetFramePixels(out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        NativeRect rect;
        var hr = DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<NativeRect>());
        if (hr != 0 && !GetWindowRect(hwnd, out rect))
        {
            return false;
        }

        x = rect.Left;
        y = rect.Top;
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return width > 0 && height > 0;
    }

    private void SetShowcaseLyric(string? lyric)
    {
        _showcaseLyric = string.IsNullOrWhiteSpace(lyric) ? null : lyric.Trim();
    }

    private void WriteShowcaseStatus(string phase, int x, int y, int width, int height, string? scene, string? error)
    {
        if (_showcase == null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            phase,
            x,
            y,
            width,
            height,
            scene,
            error
        });
        Directory.CreateDirectory(_showcase.HandshakeDirectory);
        var temporary = _showcase.StatusPath + ".tmp";
        File.WriteAllText(temporary, payload);
        File.Move(temporary, _showcase.StatusPath, overwrite: true);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // The recorder deletes the flag as well.
        }
    }
}
