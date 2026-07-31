// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Channels;

using System;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Interpreter;

/// <summary>
/// Adapts an IAgentChannel to IInputProvider so the assistant script can use input() and print()
/// with output routed to the channel. GetInputAsync receives from the channel and stores the
/// chat id for SendOutput (used as the interpreter's output callback).
/// </summary>
public class ChannelInputProvider : IInputProvider
{
    private readonly IAgentChannel _channel;
    private string _currentChatId = "";
    private readonly object _chatIdLock = new();

    public ChannelInputProvider(IAgentChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public async Task<string> GetInputAsync(string prompt)
    {
        var (text, chatId) = await _channel.ReceiveMessageAsync().ConfigureAwait(false);
        lock (_chatIdLock)
        {
            _currentChatId = chatId;
        }
        if (!string.IsNullOrWhiteSpace(chatId))
            System.Environment.SetEnvironmentVariable("MALDA_CHAT_ID", chatId);
        return text ?? "";
    }

    /// <summary>
    /// Sends output (e.g. from print()) to the current chat. Called by the host as the interpreter's output callback.
    /// </summary>
    public void SendOutput(string text)
    {
        string chatId;
        lock (_chatIdLock)
        {
            chatId = _currentChatId;
        }
        if (string.IsNullOrEmpty(chatId))
            return;
        _channel.SendMessageAsync(text, chatId).GetAwaiter().GetResult();
    }

    public bool HasQueuedInput() => false;

    public string GetQueuedInput() => "";

    public void QueueInput(string input) { }
}
