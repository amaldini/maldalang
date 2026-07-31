// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using Microsoft.JSInterop;

namespace MaldaLang.IDE.Services;

public class WebInputProvider : IInputProvider
{
    private readonly IJSRuntime _jsRuntime;
    private readonly Queue<string> _inputQueue = new();
    
    public WebInputProvider(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }
    
    public void QueueInput(string input)
    {
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
    
    public async Task<string> GetInputAsync(string prompt)
    {
        // Don't consume queued input here - queued input should be consumed by input() calling GetQueuedInput()
        // GetInputAsync should only wait for NEW input when there's no queued input
        
        // Otherwise, show a prompt dialog
        try
        {
            // Try direct prompt call first (simpler and more reliable)
            var result = await _jsRuntime.InvokeAsync<string>("prompt", prompt);
            // If user cancels, prompt returns null - treat as empty string (valid input)
            if (result != null)
            {
                return result;
            }
            // User cancelled - return empty string
            return "";
        }
        catch (JSException)
        {
            // JSInterop failure - try alternative approach with eval
            try
            {
                var result = await _jsRuntime.InvokeAsync<string>("eval", 
                    $"window.prompt ? window.prompt({System.Text.Json.JsonSerializer.Serialize(prompt)}) : null");
                return result ?? "";
            }
            catch
            {
                // If both fail, browser prompt is not available
                // Return empty string - the question has already been printed to output
                // User should provide input in the "Program Input" field and run again
                return "";
            }
        }
        catch
        {
            // Other exceptions - return empty string
            return "";
        }
    }

    public async Task<bool> ConfirmAsync(string message, bool defaultValue = false)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<bool>("confirm", message);
            return result;
        }
        catch
        {
            try
            {
                var serialized = System.Text.Json.JsonSerializer.Serialize(message);
                var result = await _jsRuntime.InvokeAsync<bool>("eval",
                    $"window.confirm ? window.confirm({serialized}) : {defaultValue.ToString().ToLowerInvariant()}");
                return result;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}