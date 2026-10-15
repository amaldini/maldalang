// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using System.IO;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

public partial class HttpServerInstance
{
    public void Start()
    {
        if (_isRunning)
            throw new Exception("HttpServer is already running");
        
        try
        {
            // Scan for @PAGE decorated functions
            ScanForRoutes();
            
            // Validate route conflicts
            _routeRegistry.ValidateRouteConflicts();
            
            // Print registered routes
            var routesSummary = _routeRegistry.GetRoutesSummary();
            Console.WriteLine(routesSummary);
            
            // Load static files into cache
            LoadStaticFiles();

            ResolveHttpsFromEnvironment();
            if (_httpsEnabled)
            {
                StartHttpsFrontAndLoopback();
            }
            else
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(BuildListenerPrefix(_host, _port));
                _listener.Start();
                MarkStarted();
                _ = Task.Run(async () => await HandleRequestsAsync());
            }
        }
        catch (Exception ex)
        {
            _isRunning = false;
            StopHttpsFront();
            throw new Exception($"Failed to start HttpServer: {ex.Message}");
        }
    }
    
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _mountedRest?.NotifyHostStopped();
        CloseAllSseConnections();
        StopHttpsFront();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // Listener may already be closing from Ctrl+C / concurrent Stop().
        }
        _listener = null;
    }

    private void MarkStarted()
    {
        _isRunning = true;
        _mountedRest?.NotifyHostStarted();
        EnsureConsoleCancelHooked();
    }

    /// <summary>
    /// Ctrl+C must cancel the default process abort: with live SSE, native
    /// <see cref="HttpListener"/> teardown often hangs so the console looks stuck.
    /// Swallow the signal, stop listeners, and let hosts (e.g. Second Brain ASK)
    /// observe <c>isRunning == false</c>.
    /// </summary>
    private static void EnsureConsoleCancelHooked()
    {
        if (Interlocked.CompareExchange(ref _consoleCancelHooked, 1, 0) != 0)
            return;
        Console.CancelKeyPress += ConsoleCancelHandler;
    }

    private static void OnConsoleCancel(object? sender, ConsoleCancelEventArgs e)
    {
        if (!AnyServerRunning())
            return;
        e.Cancel = true;
        ThreadPool.QueueUserWorkItem(static _ => StopAllForConsoleCancel());
    }

    private static bool AnyServerRunning()
    {
        lock (_instancesLock)
        {
            foreach (var instance in _instances)
            {
                if (instance._isRunning)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Test-only: stop every running HttpServer so HTTP traces do not leak ports.
    /// </summary>
    internal static void StopAllForTesting() => StopAllForConsoleCancel();

    /// <summary>
    /// Stops every running HttpServer. Used by Ctrl+C and tests.
    /// </summary>
    internal static void StopAllForConsoleCancel()
    {
        List<HttpServerInstance> snapshot;
        lock (_instancesLock)
        {
            snapshot = _instances.Where(s => s._isRunning).ToList();
        }
        foreach (var server in snapshot)
        {
            try
            {
                server.Stop();
            }
            catch
            {
                // Best-effort shutdown — never throw from a console-cancel path.
            }
        }
    }

    private static void CloseAllSseConnections()
    {
        List<SseConnection> connections;
        lock (_sseConnectionsLock)
        {
            connections = _sseConnections.Values.ToList();
            _sseConnections.Clear();
        }
        foreach (var connection in connections)
            connection.Close();
    }
    
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _staticFileCache.Clear();
        }
        // Reload static files
        LoadStaticFiles();
    }
    
    public RuntimeValue GetRoutes()
    {
        var routes = _routeRegistry.GetAllRoutes();
        var routesList = new List<RuntimeValue>();
        
        foreach (var route in routes)
        {
            var routeObj = new JsonObject();
            routeObj.Set("method", RuntimeValue.String(route.Method));
            routeObj.Set("path", RuntimeValue.String(route.PathPattern));
            routesList.Add(RuntimeValue.Object(routeObj));
        }
        
        return RuntimeValue.Array(routesList);
    }
}
