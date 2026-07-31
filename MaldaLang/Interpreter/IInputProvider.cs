// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public interface IInputProvider
{
    Task<string> GetInputAsync(string prompt);
    bool HasQueuedInput();
    string GetQueuedInput();
    void QueueInput(string input);

    /// <summary>
    /// Ask the user to approve or deny an action (e.g. run_command). Default: console y/N prompt.
    /// </summary>
    Task<bool> ConfirmAsync(string message, bool defaultValue = false)
    {
        Console.WriteLine(message);
        Console.Write(defaultValue ? "[Y/n] " : "[y/N] ");
        var line = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(line))
            return Task.FromResult(defaultValue);
        return Task.FromResult(line is "y" or "yes" or "true" or "1");
    }
}
