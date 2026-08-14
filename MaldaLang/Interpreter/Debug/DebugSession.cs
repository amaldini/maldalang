// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter.Debug;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Shared interpret-mode debug core. Breakpoints, step mode, and the pause gate
/// live here so Desktop, Web, and tests share one implementation.
/// All line numbers on this type are 1-based.
/// </summary>
public sealed class DebugSession : IDebuggerHook
{
    private readonly object _gate = new();
    private readonly object _bpLock = new();
    private readonly Dictionary<string, Dictionary<int, string?>> _breakpoints = new(StringComparer.OrdinalIgnoreCase);

    private DebugMode _mode = DebugMode.Continue;
    private int _currentDepth;
    private int _stepOverDepth;
    private int _stepOutDepth;
    private bool _pauseOnNextStatement;
    private bool _stopOnEntry;
    private TaskCompletionSource? _resumeTcs;
    private CancellationTokenSource _stopCts = new();
    private string? _mainFile;

    /// <summary>Raised from <see cref="OnPause"/> with a 1-based line.</summary>
    public event Action<int, string?>? Paused;

    public int CurrentLine { get; private set; }
    public string? CurrentFile { get; private set; }

    /// <summary>
    /// File used when one side of a breakpoint compare is null/empty.
    /// Defaults to the first <see cref="OnStatement"/> file or <c>main.malda</c>.
    /// </summary>
    public string? MainFile
    {
        get => _mainFile;
        set => _mainFile = value;
    }

    public bool StopOnEntry
    {
        get => _stopOnEntry;
        set => _stopOnEntry = value;
    }

    public void SetBreakpoints(string file, IReadOnlyList<int> lines)
    {
        var key = NormalizeFile(file);
        lock (_bpLock)
        {
            var map = new Dictionary<int, string?>();
            foreach (var line in lines)
            {
                if (line > 0)
                    map[line] = null;
            }
            _breakpoints[key] = map;
        }
    }

    public void SetBreakpoint(string file, int line, string? condition = null)
    {
        if (line < 1)
            return;

        var key = NormalizeFile(file);
        lock (_bpLock)
        {
            if (!_breakpoints.TryGetValue(key, out var map))
            {
                map = new Dictionary<int, string?>();
                _breakpoints[key] = map;
            }
            map[line] = condition;
        }
    }

    public void ClearBreakpoints(string? file = null)
    {
        lock (_bpLock)
        {
            if (file == null)
            {
                _breakpoints.Clear();
                return;
            }

            _breakpoints.Remove(NormalizeFile(file));
        }
    }

    public bool HasBreakpoint(int line, string? file = null)
    {
        lock (_bpLock)
        {
            foreach (var kvp in _breakpoints)
            {
                if (FilesMatch(kvp.Key, file) && kvp.Value.ContainsKey(line))
                    return true;
            }
            return false;
        }
    }

    public bool CheckBreakpointCondition(int line, string? file, Func<bool> evaluator)
    {
        string? condition = null;
        var found = false;
        lock (_bpLock)
        {
            foreach (var kvp in _breakpoints)
            {
                if (FilesMatch(kvp.Key, file) && kvp.Value.TryGetValue(line, out condition))
                {
                    found = true;
                    break;
                }
            }
        }

        if (!found)
            return true;

        if (string.IsNullOrEmpty(condition))
            return true;

        try
        {
            return evaluator();
        }
        catch
        {
            return true;
        }
    }

    public bool OnStatement(int line, string? file = null)
    {
        EnsureFreshRunToken();

        if (_mainFile == null && !string.IsNullOrEmpty(file))
            _mainFile = file;

        CurrentLine = line;
        CurrentFile = file;

        var mode = GetDebugMode();
        var stopOnEntry = _stopOnEntry;
        if (mode == DebugMode.Continue && !stopOnEntry && !_pauseOnNextStatement && !HasBreakpoint(line, file))
            return true;

        if (mode == DebugMode.Paused)
        {
            EnterPause(line, file);
            return false;
        }

        if (stopOnEntry)
        {
            _stopOnEntry = false;
            EnterPause(line, file);
            return false;
        }

        if (HasBreakpoint(line, file))
        {
            if (!CheckBreakpointCondition(line, file, () => true))
                return true;

            EnterPause(line, file);
            return false;
        }

        if (_pauseOnNextStatement)
        {
            _pauseOnNextStatement = false;
            EnterPause(line, file);
            return false;
        }

        if (mode == DebugMode.StepOver && _currentDepth <= _stepOverDepth)
        {
            EnterPause(line, file);
            return false;
        }

        if (mode == DebugMode.StepInto)
        {
            EnterPause(line, file);
            return false;
        }

        return true;
    }

    public void OnPause(int line, string? file = null)
    {
        CurrentLine = line;
        CurrentFile = file;
        Paused?.Invoke(line, file);
    }

    public void OnFunctionEnter(string functionName, string? className, int line)
    {
        _currentDepth++;
    }

    public void OnFunctionExit(string functionName)
    {
        _currentDepth--;
        if (_currentDepth < 0)
            _currentDepth = 0;

        if (GetDebugMode() == DebugMode.StepOut && _currentDepth < _stepOutDepth)
            _pauseOnNextStatement = true;
    }

    public DebugMode GetDebugMode()
    {
        lock (_gate)
        {
            return _mode;
        }
    }

    public void SetDebugMode(DebugMode mode)
    {
        switch (mode)
        {
            case DebugMode.Continue:
                Continue();
                break;
            case DebugMode.StepOver:
                StepOver();
                break;
            case DebugMode.StepInto:
                StepInto();
                break;
            case DebugMode.StepOut:
                StepOut();
                break;
            case DebugMode.Paused:
                lock (_gate)
                {
                    _mode = DebugMode.Paused;
                }
                break;
        }
    }

    public void Continue() => ReleaseGate(DebugMode.Continue);

    public void StepOver()
    {
        _stepOverDepth = _currentDepth;
        ReleaseGate(DebugMode.StepOver);
    }

    public void StepInto() => ReleaseGate(DebugMode.StepInto);

    public void StepOut()
    {
        _stepOutDepth = _currentDepth;
        ReleaseGate(DebugMode.StepOut);
    }

    public void Stop()
    {
        lock (_gate)
        {
            try
            {
                _stopCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _resumeTcs?.TrySetCanceled(_stopCts.Token);
            _resumeTcs = null;
        }
    }

    public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        if (_stopCts.IsCancellationRequested)
            throw new OperationCanceledException(_stopCts.Token);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource? tcs;
        lock (_gate)
        {
            if (_stopCts.IsCancellationRequested)
                throw new OperationCanceledException(_stopCts.Token);
            if (_mode != DebugMode.Paused)
                return;
            tcs = _resumeTcs;
            if (tcs == null)
                return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
        if (linked.Token.IsCancellationRequested)
            throw new OperationCanceledException(linked.Token);

        using (linked.Token.Register(() => tcs.TrySetCanceled(linked.Token)))
        {
            await tcs.Task.ConfigureAwait(false);
        }
    }

    internal static string NormalizeFile(string? file)
    {
        if (string.IsNullOrEmpty(file))
            return string.Empty;

        if (!IsFilesystemPath(file))
            return file;

        try
        {
            return Path.GetFullPath(file);
        }
        catch
        {
            return file;
        }
    }

    private void EnterPause(int line, string? file)
    {
        lock (_gate)
        {
            _mode = DebugMode.Paused;
            CurrentLine = line;
            CurrentFile = file;
            _resumeTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void ReleaseGate(DebugMode mode)
    {
        lock (_gate)
        {
            _mode = mode;
            _resumeTcs?.TrySetResult();
            _resumeTcs = null;
        }
    }

    private void EnsureFreshRunToken()
    {
        if (!_stopCts.IsCancellationRequested)
            return;

        lock (_gate)
        {
            if (!_stopCts.IsCancellationRequested)
                return;
            _stopCts.Dispose();
            _stopCts = new CancellationTokenSource();
        }
    }

    private bool FilesMatch(string stored, string? incoming)
    {
        var normalizedStored = NormalizeFile(stored);
        var normalizedIncoming = NormalizeFile(incoming);
        if (PathEquals(normalizedStored, normalizedIncoming) && !string.IsNullOrEmpty(normalizedStored))
            return true;

        var main = NormalizeFile(string.IsNullOrEmpty(_mainFile) ? "main.malda" : _mainFile);
        if (string.IsNullOrEmpty(incoming) && PathEquals(normalizedStored, main))
            return true;
        if (string.IsNullOrEmpty(stored) && PathEquals(normalizedIncoming, main))
            return true;
        if (string.IsNullOrEmpty(incoming) && string.IsNullOrEmpty(stored))
            return true;

        return false;
    }

    private static bool PathEquals(string a, string b)
    {
        return string.Equals(a, b, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static bool IsFilesystemPath(string file)
    {
        if (file.StartsWith("memory:", StringComparison.OrdinalIgnoreCase))
            return false;

        var colon = file.IndexOf(':');
        if (colon > 1)
            return false;

        if (Path.IsPathRooted(file))
            return true;

        return file.Contains(Path.DirectorySeparatorChar) || file.Contains(Path.AltDirectorySeparatorChar);
    }
}
