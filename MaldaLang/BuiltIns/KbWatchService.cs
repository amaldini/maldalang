// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.IO;
using System.Threading;

public sealed class KbWatchService : IDisposable
{
    private readonly string _directory;
    private readonly string _pattern;
    private readonly Action _onChanged;
    private readonly int _debounceMs;
    private readonly object _sync = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private bool _disposed;

    public KbWatchService(string directory, string pattern, Action onChanged, int debounceMs = 2000)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
        _pattern = string.IsNullOrWhiteSpace(pattern) ? "*.md" : pattern;
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _debounceMs = Math.Max(100, debounceMs);
    }

    public void Start()
    {
        if (!Directory.Exists(_directory))
            Directory.CreateDirectory(_directory);

        _watcher = new FileSystemWatcher(_directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (!MatchesPattern(e.FullPath, _pattern))
            return;
        lock (_sync)
        {
            _timer ??= new Timer(_ => Trigger(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_debounceMs, Timeout.Infinite);
        }
    }

    private void Trigger()
    {
        try
        {
            _onChanged();
        }
        catch
        {
        }
    }

    private static bool MatchesPattern(string fullPath, string pattern)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (pattern == "**/*.md" || pattern == "*.md")
            return fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith("*.", StringComparison.Ordinal) && pattern.Length > 2)
            return fileName.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase);
        return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
        }
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
