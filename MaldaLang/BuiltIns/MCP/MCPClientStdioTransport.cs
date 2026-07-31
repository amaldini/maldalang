// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.MCP;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

public class MCPClientStdioTransport : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _isRunning = false;
    private Thread? _readThread;
    private readonly object _lockObject = new object();
    private readonly Dictionary<string, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
    private int _requestIdCounter = 1;
    
    private string NormalizeId(object? id)
    {
        return id?.ToString() ?? "";
    }

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<string>? ErrorReceived;
    public bool IsRunning => _isRunning;

    public async Task<bool> StartAsync(string command, List<string> args, Dictionary<string, string>? env = null)
    {
        if (_isRunning)
            return false;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = string.Join(" ", args.Select(arg => $"\"{arg.Replace("\"", "\\\"")}\"")),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set environment variables
            if (env != null)
            {
                foreach (var kvp in env)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            _process = new Process { StartInfo = startInfo };
            _process.Start();

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            _isRunning = true;

            // Start reading thread
            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "MCP-Client-Stdio-Reader"
            };
            _readThread.Start();

            // Also read stderr in background
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_process.HasExited && _isRunning)
                    {
                        var line = await _process.StandardError.ReadLineAsync();
                        if (line == null)
                            break;
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            ErrorReceived?.Invoke(this, line);
                        }
                    }
                }
                catch
                {
                    // Ignore errors
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            _isRunning = false;
            throw new Exception($"Failed to start MCP server process: {ex.Message}", ex);
        }
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;

        try
        {
            _stdin?.Close();
            _stdout?.Close();
        }
        catch
        {
            // Ignore errors
        }

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(1000);
            }
        }
        catch
        {
            // Ignore errors
        }

        _readThread?.Join(2000);
        _process?.Dispose();
        _process = null;
        _stdin = null;
        _stdout = null;

        // Cancel all pending requests
        lock (_lockObject)
        {
            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }
    }

    public async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request)
    {
        if (!_isRunning || _stdin == null)
            throw new InvalidOperationException("Transport is not running");

        // Assign ID if not set
        if (request.Id == null)
        {
            lock (_lockObject)
            {
                request.Id = _requestIdCounter++;
            }
        }

        var tcs = new TaskCompletionSource<JsonRpcResponse>();
        var requestIdKey = NormalizeId(request.Id);
        lock (_lockObject)
        {
            _pendingRequests[requestIdKey] = tcs;
        }

        try
        {
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            await _stdin.WriteLineAsync(json);
            await _stdin.FlushAsync();

            // Wait for response with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cts.Token).ContinueWith(_ => {
                if (!tcs.Task.IsCompleted)
                {
                    lock (_lockObject)
                    {
                        _pendingRequests.Remove(requestIdKey);
                    }
                    tcs.TrySetCanceled();
                }
            }, TaskContinuationOptions.ExecuteSynchronously);

            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
            
            if (completedTask == timeoutTask && !tcs.Task.IsCompleted)
            {
                // Timeout occurred
                throw new TaskCanceledException("Request timed out after 30 seconds");
            }

            var response = await tcs.Task;
            return response;
        }
        catch (Exception ex)
        {
            lock (_lockObject)
            {
                _pendingRequests.Remove(requestIdKey);
            }
            throw new Exception($"Failed to send request: {ex.Message}", ex);
        }
    }

    private void ReadLoop()
    {
        while (_isRunning && _stdout != null)
        {
            try
            {
                var line = _stdout.ReadLine();
                if (line == null)
                {
                    // EOF reached
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Parse JSON-RPC response
                try
                {
                    var response = JsonSerializer.Deserialize<JsonRpcResponse>(line);
                    if (response != null && response.Id != null)
                    {
                        var responseIdKey = NormalizeId(response.Id);
                        TaskCompletionSource<JsonRpcResponse>? tcs = null;
                        lock (_lockObject)
                        {
                            if (_pendingRequests.TryGetValue(responseIdKey, out tcs))
                            {
                                _pendingRequests.Remove(responseIdKey);
                            }
                        }

                        if (tcs != null)
                        {
                            tcs.TrySetResult(response);
                        }
                    }

                    MessageReceived?.Invoke(this, line);
                }
                catch (JsonException)
                {
                    // Invalid JSON, skip
                    ErrorReceived?.Invoke(this, $"Invalid JSON received: {line}");
                }
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    ErrorReceived?.Invoke(this, $"Error reading from stdout: {ex.Message}");
                }
                break;
            }
        }

        _isRunning = false;
    }

    public void Dispose()
    {
        Stop();
    }
}