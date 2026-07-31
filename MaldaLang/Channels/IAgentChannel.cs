// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Channels;

/// <summary>
/// Abstraction for a channel that delivers user messages to the agent and sends replies back.
/// Used by the gateway to run the assistant over different transports (e.g. Telegram).
/// </summary>
public interface IAgentChannel
{
    /// <summary>
    /// Waits until a message is available, then returns its text and the chat/session id for sending the reply.
    /// </summary>
    Task<(string text, string chatId)> ReceiveMessageAsync(CancellationToken cancel = default);
    
    /// <summary>
    /// Sends a reply to the given chat/session.
    /// </summary>
    Task SendMessageAsync(string text, string chatId);
}
