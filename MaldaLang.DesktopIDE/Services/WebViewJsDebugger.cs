// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Compiler;
using MaldaLang.DesktopIDE.Models;
using Microsoft.Web.WebView2.Core;

namespace MaldaLang.DesktopIDE.Services;

public sealed class JsDebugPauseSnapshot
{
    public required int Line { get; init; }
    public required string File { get; init; }
    public required IReadOnlyList<CallStackFrame> Frames { get; init; }
}

/// <summary>
/// Chromium debugger (CDP) for Desktop Web Preview. Maps editor breakpoints
/// onto generated JavaScript via <see cref="JsSourceMap"/>.
/// Must be used on the WebView2 UI thread.
/// </summary>
public sealed class WebViewJsDebugger : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Dictionary<int, string> _objectIds = new();
    private readonly Dictionary<int, List<DebugInspectNode>> _scopeCache = new();
    private readonly Dictionary<int, List<DebugInspectNode>> _childCache = new();
    private readonly List<string> _callFrameIds = new();
    private readonly List<string> _cdpBreakpointIds = new();

    private CoreWebView2? _core;
    private CoreWebView2DevToolsProtocolEventReceiver? _pausedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _resumedReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _consoleReceiver;
    private JsSourceMap? _sourceMap;
    private string _scriptFileName = "";
    private string _sourceFilePath = "main.malda";
    private string _urlRegex = "";
    private int _nextHandle = 1;
    private bool _disposed;

    public JsDebugPauseSnapshot? LastPause { get; private set; }
    public bool IsAttached => _core != null;

    public event Action<JsDebugPauseSnapshot>? Paused;
    public event Action? Resumed;
    public event Action<string>? Output;
    public event Action<string>? Failed;

    public async Task AttachAsync(
        CoreWebView2 core,
        JsSourceMap sourceMap,
        string scriptFileName,
        string sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(sourceMap);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFileName);

        await DetachCoreAsync().ConfigureAwait(true);

        _core = core;
        _sourceMap = sourceMap;
        _scriptFileName = scriptFileName;
        _sourceFilePath = string.IsNullOrWhiteSpace(sourceFilePath) ? "main.malda" : sourceFilePath;
        _urlRegex = Regex.Escape(scriptFileName) + @"(\?.*)?$";

        _pausedReceiver = core.GetDevToolsProtocolEventReceiver("Debugger.paused");
        _pausedReceiver.DevToolsProtocolEventReceived += OnPaused;
        _resumedReceiver = core.GetDevToolsProtocolEventReceiver("Debugger.resumed");
        _resumedReceiver.DevToolsProtocolEventReceived += OnResumed;
        _consoleReceiver = core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
        _consoleReceiver.DevToolsProtocolEventReceived += OnConsole;

        await CallAsync("Runtime.enable", "{}").ConfigureAwait(true);
        await CallAsync("Debugger.enable", "{}").ConfigureAwait(true);
        await CallAsync("Debugger.setSkipAllPauses", """{"skip":false}""").ConfigureAwait(true);
    }

    public async Task SyncBreakpointsAsync(IEnumerable<Breakpoint> breakpoints)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        if (_core == null || _sourceMap == null)
        {
            return;
        }

        foreach (var id in _cdpBreakpointIds)
        {
            try
            {
                await CallAsync(
                    "Debugger.removeBreakpoint",
                    JsonSerializer.Serialize(new { breakpointId = id }, JsonOptions)).ConfigureAwait(true);
            }
            catch
            {
                // The page may have navigated; ignore stale ids.
            }
        }

        _cdpBreakpointIds.Clear();

        var generated = JsDebugBreakpointMapper.Map(
            _sourceMap,
            breakpoints
                .Where(MatchesCurrentFile)
                .Select(bp => (bp.Line, bp.Condition, bp.Enabled)));

        foreach (var breakpoint in generated.Where(item => item.Verified))
        {
            var payload = new Dictionary<string, object?>
            {
                ["lineNumber"] = Math.Max(0, breakpoint.GeneratedLine - 1),
                ["urlRegex"] = _urlRegex
            };
            if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
            {
                payload["condition"] = breakpoint.Condition;
            }

            try
            {
                var json = await CallAsync(
                    "Debugger.setBreakpointByUrl",
                    JsonSerializer.Serialize(payload, JsonOptions)).ConfigureAwait(true);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("breakpointId", out var idElement))
                {
                    var id = idElement.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        _cdpBreakpointIds.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Failed?.Invoke("Could not set JavaScript breakpoint: " + ex.Message);
            }
        }
    }

    public Task ContinueAsync() => CallAsync("Debugger.resume", "{}");

    public Task StepOverAsync() => CallAsync("Debugger.stepOver", "{}");

    public Task StepIntoAsync() => CallAsync("Debugger.stepInto", "{}");

    public Task StepOutAsync() => CallAsync("Debugger.stepOut", "{}");

    public Task PauseAsync() => CallAsync("Debugger.pause", "{}");

    public IReadOnlyList<DebugInspectNode> GetCachedScopes(int frameId)
    {
        return _scopeCache.TryGetValue(frameId, out var scopes)
            ? scopes
            : Array.Empty<DebugInspectNode>();
    }

    public IReadOnlyList<DebugInspectNode> GetCachedChildren(int variablesReference)
    {
        return _childCache.TryGetValue(variablesReference, out var children)
            ? children
            : Array.Empty<DebugInspectNode>();
    }

    public async Task<IReadOnlyList<DebugInspectNode>> ExpandAsync(int variablesReference, int frameId)
    {
        if (variablesReference <= 0 || _core == null)
        {
            return Array.Empty<DebugInspectNode>();
        }

        if (_childCache.TryGetValue(variablesReference, out var cached))
        {
            return cached;
        }

        if (!_objectIds.TryGetValue(variablesReference, out var objectId))
        {
            return Array.Empty<DebugInspectNode>();
        }

        var children = await GetPropertiesAsync(objectId, frameId).ConfigureAwait(true);
        _childCache[variablesReference] = children;
        return children;
    }

    public async Task<DebugInspectNode> EvaluateWatchAsync(string expression, int frameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        if (_core == null)
        {
            return DebugInspectSnapshotBuilder.FromWatchError(expression, "JavaScript debugger is not attached.", frameId);
        }

        var index = Math.Clamp(frameId - 1, 0, Math.Max(0, _callFrameIds.Count - 1));
        if (_callFrameIds.Count == 0)
        {
            return DebugInspectSnapshotBuilder.FromWatchError(expression, "Not paused.", frameId);
        }

        try
        {
            var json = await CallAsync(
                "Debugger.evaluateOnCallFrame",
                JsonSerializer.Serialize(new
                {
                    callFrameId = _callFrameIds[index],
                    expression,
                    silent = true,
                    returnByValue = true,
                    generatePreview = true
                }, JsonOptions)).ConfigureAwait(true);

            using var document = JsonDocument.Parse(json);
            var result = document.RootElement.GetProperty("result");
            if (document.RootElement.TryGetProperty("exceptionDetails", out var exception))
            {
                var message = exception.TryGetProperty("text", out var text)
                    ? text.GetString() ?? "watch failed"
                    : "watch failed";
                return DebugInspectSnapshotBuilder.FromWatchError(expression, message, frameId);
            }

            return RemoteObjectToNode(expression, result, frameId);
        }
        catch (Exception ex)
        {
            return DebugInspectSnapshotBuilder.FromWatchError(expression, ex.Message, frameId);
        }
    }

    public async Task DetachAsync()
    {
        await DetachCoreAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = DetachCoreAsync();
    }

    private async Task DetachCoreAsync()
    {
        if (_pausedReceiver != null)
        {
            _pausedReceiver.DevToolsProtocolEventReceived -= OnPaused;
        }

        if (_resumedReceiver != null)
        {
            _resumedReceiver.DevToolsProtocolEventReceived -= OnResumed;
        }

        if (_consoleReceiver != null)
        {
            _consoleReceiver.DevToolsProtocolEventReceived -= OnConsole;
        }

        if (_core != null)
        {
            try
            {
                await CallAsync("Debugger.resume", "{}").ConfigureAwait(true);
            }
            catch
            {
                // Not paused.
            }

            try
            {
                await CallAsync("Debugger.disable", "{}").ConfigureAwait(true);
            }
            catch
            {
                // Page gone.
            }
        }

        _pausedReceiver = null;
        _resumedReceiver = null;
        _consoleReceiver = null;
        _core = null;
        _sourceMap = null;
        _cdpBreakpointIds.Clear();
        ClearInspect();
        LastPause = null;
    }

    private void ClearInspect()
    {
        _objectIds.Clear();
        _scopeCache.Clear();
        _childCache.Clear();
        _callFrameIds.Clear();
        _nextHandle = 1;
    }

    private bool MatchesCurrentFile(Breakpoint breakpoint)
    {
        if (string.IsNullOrWhiteSpace(breakpoint.FilePath))
        {
            return true;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(breakpoint.FilePath),
                Path.GetFullPath(_sourceFilePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(breakpoint.FilePath, _sourceFilePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnPaused(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        _ = HandlePausedAsync(e.ParameterObjectAsJson);
    }

    private void OnResumed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        LastPause = null;
        Resumed?.Invoke();
    }

    private void OnConsole(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (!document.RootElement.TryGetProperty("args", out var args) ||
                args.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var parts = new List<string>();
            foreach (var arg in args.EnumerateArray())
            {
                parts.Add(FormatRemoteObject(arg));
            }

            if (parts.Count > 0)
            {
                Output?.Invoke(string.Join(" ", parts));
            }
        }
        catch
        {
            // Ignore malformed console payloads.
        }
    }

    private async Task HandlePausedAsync(string json)
    {
        try
        {
            ClearInspect();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("callFrames", out var callFrames) ||
                callFrames.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var frames = new List<CallStackFrame>();
            var frameIndex = 0;
            foreach (var callFrame in callFrames.EnumerateArray())
            {
                var functionName = callFrame.TryGetProperty("functionName", out var nameElement)
                    ? nameElement.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(functionName))
                {
                    functionName = "<anonymous>";
                }

                var url = callFrame.TryGetProperty("url", out var urlElement)
                    ? urlElement.GetString() ?? ""
                    : "";
                var generatedLine = 0;
                if (callFrame.TryGetProperty("location", out var location) &&
                    location.TryGetProperty("lineNumber", out var lineElement))
                {
                    generatedLine = lineElement.GetInt32() + 1;
                }

                var (line, file) = MapLocation(url, generatedLine);
                frames.Add(new CallStackFrame
                {
                    FunctionName = functionName,
                    Line = line,
                    File = file
                });

                if (callFrame.TryGetProperty("callFrameId", out var idElement))
                {
                    var id = idElement.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        _callFrameIds.Add(id);
                    }
                }

                frameIndex++;
                var scopes = new List<DebugInspectNode>();
                if (callFrame.TryGetProperty("scopeChain", out var scopeChain) &&
                    scopeChain.ValueKind == JsonValueKind.Array)
                {
                    foreach (var scope in scopeChain.EnumerateArray())
                    {
                        var type = scope.TryGetProperty("type", out var typeElement)
                            ? typeElement.GetString() ?? ""
                            : "";
                        if (type is "global" or "script" or "module" or "with")
                        {
                            continue;
                        }

                        if (!scope.TryGetProperty("object", out var scopeObject))
                        {
                            continue;
                        }

                        var objectId = scopeObject.TryGetProperty("objectId", out var objectIdElement)
                            ? objectIdElement.GetString()
                            : null;
                        if (string.IsNullOrWhiteSpace(objectId))
                        {
                            continue;
                        }

                        var handle = _nextHandle++;
                        _objectIds[handle] = objectId;
                        var title = type switch
                        {
                            "local" => "Locals",
                            "closure" => "Closure",
                            "block" => "Block",
                            "catch" => "Catch",
                            _ => string.IsNullOrWhiteSpace(type) ? "Scope" : char.ToUpperInvariant(type[0]) + type[1..]
                        };
                        scopes.Add(new DebugInspectNode
                        {
                            Display = title,
                            Name = title,
                            VariablesReference = handle,
                            IsScope = true,
                            FrameId = frameIndex
                        });
                        _childCache[handle] = await GetPropertiesAsync(objectId, frameIndex).ConfigureAwait(true);
                    }
                }

                _scopeCache[frameIndex] = scopes;
            }

            if (frames.Count == 0)
            {
                return;
            }

            LastPause = new JsDebugPauseSnapshot
            {
                Line = frames[0].Line,
                File = frames[0].File,
                Frames = frames
            };
            Paused?.Invoke(LastPause);
        }
        catch (Exception ex)
        {
            Failed?.Invoke("JavaScript pause failed: " + ex.Message);
        }
    }

    private (int Line, string File) MapLocation(string url, int generatedLine)
    {
        if (!string.IsNullOrWhiteSpace(url) &&
            url.EndsWith(".malda", StringComparison.OrdinalIgnoreCase) &&
            generatedLine > 0)
        {
            return (generatedLine, _sourceFilePath);
        }

        var original = generatedLine > 0 ? _sourceMap?.ToOriginalLine(generatedLine) : null;
        if (original.HasValue)
        {
            return (original.Value, _sourceFilePath);
        }

        return (generatedLine, string.IsNullOrWhiteSpace(url) ? _scriptFileName : url);
    }

    private async Task<List<DebugInspectNode>> GetPropertiesAsync(string objectId, int frameId)
    {
        var nodes = new List<DebugInspectNode>();
        if (_core == null)
        {
            return nodes;
        }

        try
        {
            var json = await CallAsync(
                "Runtime.getProperties",
                JsonSerializer.Serialize(new
                {
                    objectId,
                    ownProperties = true,
                    accessorPropertiesOnly = false,
                    generatePreview = true
                }, JsonOptions)).ConfigureAwait(true);

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Array)
            {
                return nodes;
            }

            foreach (var property in result.EnumerateArray())
            {
                var name = property.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(name) ||
                    name.StartsWith("__", StringComparison.Ordinal) ||
                    name == "MaldaApp" ||
                    name == "mlRuntime")
                {
                    continue;
                }

                if (!property.TryGetProperty("value", out var value))
                {
                    continue;
                }

                nodes.Add(RemoteObjectToNode(name, value, frameId));
            }
        }
        catch
        {
            // Property walk is best-effort.
        }

        return nodes;
    }

    private DebugInspectNode RemoteObjectToNode(string name, JsonElement remote, int frameId)
    {
        var preview = FormatRemoteObject(remote);
        var objectId = remote.TryGetProperty("objectId", out var idElement)
            ? idElement.GetString()
            : null;
        var type = remote.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString() ?? ""
            : "";
        var handle = 0;
        if (!string.IsNullOrWhiteSpace(objectId) && type is "object" or "function")
        {
            handle = _nextHandle++;
            _objectIds[handle] = objectId;
        }

        return new DebugInspectNode
        {
            Display = string.IsNullOrEmpty(type) ? $"{name} = {preview}" : $"{name} = {preview} ({type})",
            Name = name,
            Value = preview,
            Type = type,
            VariablesReference = handle,
            FrameId = frameId
        };
    }

    private static string FormatRemoteObject(JsonElement remote)
    {
        if (remote.TryGetProperty("unserializableValue", out var unserializable))
        {
            return unserializable.GetString() ?? "";
        }

        if (remote.TryGetProperty("value", out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => value.GetRawText()
            };
        }

        if (remote.TryGetProperty("description", out var description))
        {
            return description.GetString() ?? "";
        }

        if (remote.TryGetProperty("type", out var type))
        {
            return type.GetString() ?? "";
        }

        return "";
    }

    private async Task<string> CallAsync(string method, string parametersJson)
    {
        if (_core == null)
        {
            return "{}";
        }

        return await _core.CallDevToolsProtocolMethodAsync(method, parametersJson).ConfigureAwait(true);
    }
}
