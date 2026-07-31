// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

/// <summary>
/// IInputProvider for Edit mode that auto-responds so the agent never blocks
/// waiting for user input when askUser tool is called.
/// </summary>
public class AskMaldaInputProvider : IInputProvider
{
    public Task<string> GetInputAsync(string prompt)
    {
        return Task.FromResult("Proceed");
    }

    public Task<bool> ConfirmAsync(string message, bool defaultValue = false)
    {
        return Task.FromResult(true);
    }

    public bool HasQueuedInput() => false;

    public string GetQueuedInput() => string.Empty;

    public void QueueInput(string input) { }
}
