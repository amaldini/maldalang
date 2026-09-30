// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

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
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            Activate();
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

        Activate();
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
            WriteShowcaseStatus("armed", x, y, width, height, scene: null, error: null);
            var keepInFront = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            keepInFront.Tick += (_, _) => BringShowcaseToFront();
            keepInFront.Start();

            var startDeadline = Environment.TickCount64 + 180_000;
            while (!File.Exists(_showcase.StartPath))
            {
                if (Environment.TickCount64 > startDeadline)
                {
                    throw new TimeoutException("Showcase start file was not created within 3 minutes.");
                }

                await Task.Delay(100);
            }

            BringShowcaseToFront();

            for (var index = 0; index < _showcase.Playlist.Scenes.Count; index++)
            {
                var scene = _showcase.Playlist.Scenes[index];
                await PlayShowcaseSceneAsync(scene, x, y, width, height);
            }

            WriteShowcaseStatus("done", x, y, width, height, scene: null, error: null);
            keepInFront.Stop();
            Application.Current.Shutdown(0);
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
        using var navigationCancel = new CancellationTokenSource();
        Task<bool>? navigation = null;
        try
        {
            RestoreShowcaseLayout();
            StopActiveExecution();
            _lastDetectedWebUiUrl = null;
            await Task.Delay(80);

            _showcaseCaption = scene.Caption ?? "";
            OpenFileAndIncludedDocuments(scene.AbsolutePath);
            DropUntitledShowcaseDocument();
            BringShowcaseToFront();
            FocusShowcaseLine(scene.FocusLine);
            UpdateWindowChrome();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(200);
            BringShowcaseToFront();
            WriteShowcaseStatus("playing", frameX, frameY, frameWidth, frameHeight, scene.Caption, error: null);

            var timeout = _showcase!.Playlist.ReadyTimeoutMs;
            var ready = true;
            if (scene.Panel == "preview")
            {
                navigation = WaitForContentNavigationAsync(timeout, navigationCancel.Token);
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
                var url = await WaitForOpenUrlAsync(timeout);
                var uri = url == null ? null : TryResolveWebViewUri(url);
                if (uri == null)
                {
                    ready = false;
                }
                else
                {
                    navigation = WaitForContentNavigationAsync(timeout, navigationCancel.Token);
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
                await WaitForInterpretFinishedAsync(timeout);
            }

            if (scene.Maximize && ready)
            {
                await Task.Delay(Math.Max(0, _showcase.Playlist.SplitBeatMs));
                var tab = scene.Panel == "output" ? "output" : "webui";
                SetSidebarPanelMaximized(tab, true);
            }

            await Task.Delay(Math.Max(0, scene.HoldMs));
        }
        catch (Exception ex)
        {
            navigationCancel.Cancel();
            SetOutputText($"Showcase scene failed ({scene.File}): {ex.Message}", isError: true);
            SwitchToTab("output");
            await Task.Delay(Math.Max(800, scene.HoldMs));
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

    private async Task WaitForInterpretFinishedAsync(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (_runTask != null && !_runTask.IsCompleted && Environment.TickCount64 < deadline)
        {
            await Task.Delay(50);
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private async Task<string?> WaitForOpenUrlAsync(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
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

    private async Task<bool> WaitForContentNavigationAsync(int timeoutMs, CancellationToken cancellation)
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
            var delay = Task.Delay(timeoutMs, cancellation);
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
