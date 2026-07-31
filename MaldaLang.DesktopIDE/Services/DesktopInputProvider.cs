// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using System.Threading.Tasks;

namespace MaldaLang.DesktopIDE.Services;

public class DesktopInputProvider : IInputProvider
{
    private readonly Queue<string> _inputQueue = new();
    private TaskCompletionSource<string>? _pendingInputRequest;
    private string? _pendingPrompt;
    
    public event Action<string>? InputRequested;
    public event Action<string, TaskCompletionSource<bool>>? ConfirmRequested;
    
    private TaskCompletionSource<bool>? _pendingConfirmRequest;
    public void QueueInput(string input)
    {
        // If there's a pending request, complete it immediately
        // Don't queue it here - the ExecutionService will queue it after GetInputAsync returns
        if (_pendingInputRequest != null && !_pendingInputRequest.Task.IsCompleted)
        {
            var tcs = _pendingInputRequest;
            _pendingInputRequest = null;
            _pendingPrompt = null;
            tcs.SetResult(input);
            return;
        }
        
        // No pending request - queue it for later consumption
        _inputQueue.Enqueue(input);
    }
    
    public bool HasQueuedInput()
    {
        return _inputQueue.Count > 0;
    }
    
    public string GetQueuedInput()
    {
        if (_inputQueue.Count > 0)
        {
            return _inputQueue.Dequeue();
        }
        return "";
    }
    
    public Task<string> GetInputAsync(string prompt)
    {
        // Don't consume queued input here - queued input should be consumed by input() calling GetQueuedInput()
        // GetInputAsync should only wait for NEW input when there's no queued input
        
        // Create a TaskCompletionSource to wait for input from UI
        _pendingInputRequest = new TaskCompletionSource<string>();
        _pendingPrompt = prompt;
        
        // Capture the task BEFORE invoking the event (in case QueueInput is called synchronously)
        var task = _pendingInputRequest.Task;
        
        // Notify UI that input is needed
        InputRequested?.Invoke(prompt);
        
        // Return the captured task (not _pendingInputRequest.Task which might be null after QueueInput)
        return task;
    }

    public Task<bool> ConfirmAsync(string message, bool defaultValue = false)
    {
        if (ConfirmRequested != null)
        {
            _pendingConfirmRequest = new TaskCompletionSource<bool>();
            var task = _pendingConfirmRequest.Task;
            ConfirmRequested.Invoke(message, _pendingConfirmRequest);
            return task;
        }

        Console.WriteLine(message);
        Console.Write(defaultValue ? "[Y/n] " : "[y/N] ");
        var line = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(line))
            return Task.FromResult(defaultValue);
        return Task.FromResult(line is "y" or "yes" or "true" or "1");
    }

    public void CompleteConfirm(bool approved)
    {
        if (_pendingConfirmRequest != null && !_pendingConfirmRequest.Task.IsCompleted)
        {
            var tcs = _pendingConfirmRequest;
            _pendingConfirmRequest = null;
            tcs.SetResult(approved);
        }
    }
    
    public void CancelPendingInput()
    {
        if (_pendingInputRequest != null && !_pendingInputRequest.Task.IsCompleted)
        {
            var tcs = _pendingInputRequest;
            _pendingInputRequest = null;
            _pendingPrompt = null;
            tcs.SetCanceled();
        }
    }
    
    public void Clear()
    {
        _inputQueue.Clear();
    }
}