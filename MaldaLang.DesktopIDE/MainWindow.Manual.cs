// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaldaLang.DesktopIDE.Services;
using Microsoft.Web.WebView2.Core;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private readonly List<ManualChapter> _manualChapters = new();
    private bool _manualSelecting;
    private string? _manualCurrentFile;

    private void ManualChapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_manualSelecting || ManualChapterCombo.SelectedItem is not ManualChapter chapter)
        {
            return;
        }

        _ = ShowReferenceManualPageAsync(chapter.File);
    }

    private void ManualOpenExternalButton_Click(object sender, RoutedEventArgs e)
    {
        OpenReferenceManualInBrowser(_manualCurrentFile ?? "index.html");
    }

    private void ManualMaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        SetSidebarPanelMaximized("manual", _maximizedSidebarTab != "manual");
    }

    internal async Task<bool> ShowReferenceManualPageAsync(string fileName, int timeoutMs = 20000)
    {
        var safeName = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(safeName) ||
            !safeName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            safeName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var manualPath = ResolveReferenceManualPath(safeName);
        if (manualPath == null)
        {
            return false;
        }

        SwitchToTab("manual");
        EnsureManualChaptersLoaded();
        SelectManualChapter(safeName);

        var core = await EnsureManualCoreAsync();
        if (core == null)
        {
            return false;
        }

        var target = new Uri($"https://{WebPreviewHostBuilder.VirtualHostName}/ReferenceManual/{safeName}");
        if (ManualWebView.Source != null &&
            string.Equals(ManualWebView.Source.AbsoluteUri, target.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            _manualCurrentFile = safeName;
            return true;
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            var uri = ManualWebView.Source?.AbsoluteUri ?? "";
            if (!uri.Contains(safeName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ManualWebView.NavigationCompleted -= Handler;
            done.TrySetResult(args.IsSuccess);
        }

        ManualWebView.NavigationCompleted += Handler;
        try
        {
            core.Navigate(target.AbsoluteUri);
            var finished = await Task.WhenAny(done.Task, Task.Delay(timeoutMs));
            var ok = finished == done.Task && done.Task.Result;
            if (ok)
            {
                _manualCurrentFile = safeName;
            }

            return ok;
        }
        finally
        {
            ManualWebView.NavigationCompleted -= Handler;
        }
    }

    private void EnsureManualChaptersLoaded()
    {
        if (_manualChapters.Count > 0 || ManualChapterCombo.Items.Count > 0)
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var chaptersPath = string.IsNullOrWhiteSpace(repoRoot)
            ? null
            : Path.Combine(repoRoot, "ReferenceManual", "chapters.json");
        if (chaptersPath != null && File.Exists(chaptersPath))
        {
            try
            {
                var catalog = JsonSerializer.Deserialize<ManualChapterCatalog>(
                    File.ReadAllText(chaptersPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (catalog?.Chapters != null)
                {
                    foreach (var chapter in catalog.Chapters)
                    {
                        if (string.IsNullOrWhiteSpace(chapter.File))
                        {
                            continue;
                        }

                        _manualChapters.Add(new ManualChapter
                        {
                            File = Path.GetFileName(chapter.File),
                            Title = string.IsNullOrWhiteSpace(chapter.Title) ? chapter.File : chapter.Title
                        });
                    }
                }
            }
            catch
            {
                _manualChapters.Clear();
            }
        }

        if (_manualChapters.Count == 0)
        {
            _manualChapters.Add(new ManualChapter { File = "index.html", Title = "Home" });
        }

        _manualSelecting = true;
        ManualChapterCombo.ItemsSource = _manualChapters;
        _manualSelecting = false;
    }

    private void SelectManualChapter(string fileName)
    {
        var match = _manualChapters.FirstOrDefault(chapter =>
            string.Equals(chapter.File, fileName, StringComparison.OrdinalIgnoreCase));
        if (match == null || ReferenceEquals(ManualChapterCombo.SelectedItem, match))
        {
            return;
        }

        _manualSelecting = true;
        ManualChapterCombo.SelectedItem = match;
        _manualSelecting = false;
    }

    private async Task<CoreWebView2?> EnsureManualCoreAsync()
    {
        try
        {
            await ManualWebView.EnsureCoreWebView2Async();
        }
        catch
        {
            return null;
        }

        var core = ManualWebView.CoreWebView2;
        if (core == null)
        {
            return null;
        }

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        EnsureWebPreviewVirtualHost(core);
        return core;
    }

    private string? ResolveReferenceManualPath(string fileName)
    {
        var repoRoot = FindRepoRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return null;
        }

        var path = Path.Combine(repoRoot, "ReferenceManual", fileName);
        return File.Exists(path) ? path : null;
    }

    private void OpenReferenceManualInBrowser(string fileName)
    {
        var path = ResolveReferenceManualPath(Path.GetFileName(fileName));
        if (path == null)
        {
            OpenDocumentationUrl();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            OpenDocumentationUrl();
        }
    }

    private sealed class ManualChapter
    {
        public string File { get; set; } = "";
        public string Title { get; set; } = "";
    }

    private sealed class ManualChapterCatalog
    {
        public List<ManualChapter> Chapters { get; set; } = new();
    }
}
