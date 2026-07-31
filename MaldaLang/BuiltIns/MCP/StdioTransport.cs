// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.MCP;

using System.Text;
using System.Text.Json;

public class StdioTransport
{
    private readonly TextReader _stdin;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private bool _isRunning = false;
    private Thread? _readThread;

    public event EventHandler<string>? MessageReceived;
    public bool IsRunning => _isRunning;

    public StdioTransport()
    {
        _stdin = Console.In;
        _stdout = Console.Out;
        _stderr = Console.Error;
    }

    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "MCP-Stdio-Reader"
        };
        _readThread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _readThread?.Join(1000);
    }

    public void SendMessage(string jsonMessage)
    {
        try
        {
            _stdout.WriteLine(jsonMessage);
            _stdout.Flush();
        }
        catch (Exception ex)
        {
            _stderr.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    public void SendError(string error)
    {
        try
        {
            _stderr.WriteLine($"Error: {error}");
            _stderr.Flush();
        }
        catch
        {
            // Ignore errors when writing to stderr
        }
    }

    private void ReadLoop()
    {
        var buffer = new StringBuilder();
        
        while (_isRunning)
        {
            try
            {
                var line = _stdin.ReadLine();
                if (line == null)
                {
                    // EOF reached
                    break;
                }

                // MCP uses line-delimited JSON
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Try to parse as JSON to validate
                try
                {
                    JsonDocument.Parse(line);
                    MessageReceived?.Invoke(this, line);
                }
                catch (JsonException)
                {
                    // Invalid JSON, skip
                    SendError($"Invalid JSON received: {line}");
                }
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    SendError($"Error reading from stdin: {ex.Message}");
                }
                break;
            }
        }
    }
}